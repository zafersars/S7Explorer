using System.Diagnostics;
using System.Windows.Threading;

namespace S7Explorer.ManualPages;

/// <summary>
/// EN: One cycle's worth of values read from the PLC, plus the health information the page needs
///     to decide whether it may show those values as trustworthy.
/// TR: PLC'den bir turda okunan değerler ve sayfanın bu değerleri güvenilir gösterip
///     gösteremeyeceğine karar vermesi için gereken sağlık bilgisi.
/// </summary>
public class ManualPageSnapshot
{
    /// <summary>EN: Value per symbol for this cycle. TR: Bu turda sembol başına okunan değer.</summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EN: Error text per symbol that could not be read. TR: Okunamayan semboller için hata metni.</summary>
    public Dictionary<string, string> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EN: Symbols skipped because they failed repeatedly. TR: Üst üste hata verdiği için atlanan semboller.</summary>
    public HashSet<string> PausedSymbols { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EN: How long this cycle took. TR: Bu turun süresi.</summary>
    public long CycleMs { get; set; }

    /// <summary>EN: Cycles skipped so far because the previous one had not finished. TR: Öncekisi bitmediği için şu ana kadar atlanan tur sayısı.</summary>
    public int OverrunCount { get; set; }

    /// <summary>
    /// EN: PLC round trips this cycle cost. With block reading this should stay far below the
    ///     symbol count; if it approaches it, the DB layout is too scattered to batch.
    /// TR: Bu turun kaç PLC gidiş-dönüşüne mal olduğu. Blok okumayla bu sayı sembol sayısının
    ///     çok altında kalmalı; ona yaklaşıyorsa DB düzeni toplu okumaya fazla dağınık demektir.
    /// </summary>
    public int RoundTrips { get; set; }

    /// <summary>
    /// EN: False when the PLC's life bit has not changed within the configured timeout, i.e. the
    ///     PLC is not talking even though the socket is open. Null when no life bit is configured.
    /// TR: PLC'nin canlılık biti tanımlı zaman aşımı içinde değişmediyse false; yani soket açık olsa
    ///     bile PLC konuşmuyor. Canlılık biti tanımlı değilse null.
    /// </summary>
    public bool? LifeOk { get; set; }

    /// <summary>EN: Whether the machine reports itself to be in manual mode. Null when not configured. TR: Makinenin kendini manuel modda bildirip bildirmediği. Tanımlı değilse null.</summary>
    public bool? ManualModeActive { get; set; }
}

/// <summary>
/// EN: Cyclically reads the symbols a manual page displays and publishes one snapshot per cycle.
///
///     This build is read-only on purpose: it never writes to the PLC, not even the outgoing life
///     bit. That keeps a first commissioning test incapable of moving the machine. Writing is
///     enabled in a later step, together with the life bit that lets the PLC drop stale commands.
///
///     Reads go through <see cref="PlcService"/> one symbol at a time. The whole Send area of a
///     comm DB is contiguous, so this is the seam where a single block read will replace N round
///     trips — the runner's callers do not need to change when that happens.
/// TR: Manuel sayfanın gösterdiği sembolleri döngüsel okur ve tur başına bir anlık görüntü yayınlar.
///
///     Bu sürüm bilinçli olarak salt okunurdur: PLC'ye hiçbir şey yazmaz, giden canlılık bitini bile.
///     Böylece ilk devreye alma testinin makineyi hareket ettirmesi mümkün olmaz. Yazma, PLC'nin
///     bayatlamış komutları düşürmesini sağlayan canlılık bitiyle birlikte sonraki adımda açılır.
///
///     Okumalar <see cref="PlcService"/> üzerinden sembol sembol yapılır. Haberleşme DB'sinin Send
///     alanı bitişik olduğundan, N round-trip yerine tek blok okumanın geçeceği dikiş burasıdır —
///     o değişiklikte bu sınıfı çağıranların değişmesi gerekmez.
/// </summary>
public class ManualPageRunner
{
    /// <summary>
    /// EN: Consecutive failures after which a symbol is skipped, so one bad address cannot
    ///     dominate every cycle and flood the log.
    /// TR: Bir sembolün atlanmaya başlaması için gereken üst üste hata sayısı; böylece tek bir
    ///     hatalı adres her turu meşgul edip logu boğamaz.
    /// </summary>
    private const int FailuresBeforePause = 3;

    private readonly PlcService _plc;
    private readonly ManualPageDefinition _page;
    private readonly List<string> _readSymbols;
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// EN: The only symbols this runner will ever write, taken from the validator's ruling.
    ///     Checked again on every write: even a UI bug cannot send a command the page did not
    ///     declare and validation did not approve.
    /// TR: Bu koşucunun yazabileceği tek semboller; doğrulayıcının kararından alınır.
    ///     Her yazmada yeniden denetlenir: arayüzdeki bir hata bile, sayfanın bildirmediği ve
    ///     doğrulamanın onaylamadığı bir komutu gönderemez.
    /// </summary>
    private readonly HashSet<string> _writableSymbols;

    private readonly Dictionary<string, int> _consecutiveFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _paused = new(StringComparer.OrdinalIgnoreCase);

    private bool _isPolling;
    private int _overrunCount;

    /// <summary>
    /// EN: When the PLC last acknowledged the life bit by clearing it. The handshake is: this app
    ///     writes 1 every cycle, the PLC program writes it back to 0. Reading a 0 is therefore
    ///     proof that the PLC is scanning and listening — one bit covers both directions.
    /// TR: PLC'nin canlılık bitini sıfırlayarak en son ne zaman teyit ettiği. El sıkışma şöyle:
    ///     bu uygulama her turda 1 yazar, PLC programı 0'a çeker. 0 okumak bu yüzden PLC'nin
    ///     tarama yaptığının ve dinlediğinin kanıtıdır — tek bit iki yönü de kapsar.
    /// </summary>
    private DateTime _lastLifeAckUtc = DateTime.UtcNow;

    /// <summary>EN: When the life bit was last sent, used to throttle it. TR: Canlılık bitinin en son ne zaman gönderildiği; gönderim sıklığını sınırlamak için.</summary>
    private DateTime _lastLifeSentUtc = DateTime.MinValue;

    /// <summary>EN: Fires once per completed cycle. TR: Tamamlanan her tur için bir kez tetiklenir.</summary>
    public event EventHandler<ManualPageSnapshot>? Updated;

    /// <summary>
    /// EN: Fires when something worth logging happens — a symbol starting or stopping to fail,
    ///     a cycle overrun, the life bit stalling. Deliberately not raised every cycle.
    /// TR: Loglanmayı hak eden bir şey olduğunda tetiklenir — bir sembolün hata vermeye başlaması
    ///     ya da düzelmesi, tur aşımı, canlılık bitinin durması. Bilinçli olarak her turda tetiklenmez.
    /// </summary>
    public event EventHandler<string>? Notice;

    /// <summary>EN: True while the cyclic read is active. TR: Döngüsel okuma etkinken true.</summary>
    public bool IsRunning => _timer.IsEnabled;

    /// <summary>
    /// EN: Whether commands may be sent. Starts false on purpose: monitoring a page must never be
    ///     enough to move anything. The operator has to arm writing explicitly, and arming also
    ///     starts the outgoing life bit so the PLC can drop commands if this app goes away.
    /// TR: Komut gönderilebilir mi. Bilinçli olarak false başlar: bir sayfayı izlemek asla tek
    ///     başına bir şeyi hareket ettirmeye yetmemelidir. Operatör yazmayı açıkça etkinleştirmek
    ///     zorundadır; etkinleştirme aynı zamanda giden canlılık bitini başlatır, böylece bu
    ///     uygulama kaybolursa PLC komutları düşürebilir.
    /// </summary>
    public bool IsWriteEnabled { get; private set; }

    /// <summary>EN: The page being run. TR: Çalıştırılan sayfa.</summary>
    public ManualPageDefinition Page => _page;

    /// <summary>
    /// EN: Creates a runner for one page.
    /// TR: Tek bir sayfa için koşucu oluşturur.
    /// </summary>
    /// <param name="plc">EN: Connected PLC service. TR: Bağlı PLC servisi.</param>
    /// <param name="page">EN: Validated page definition. TR: Doğrulanmış sayfa tanımı.</param>
    /// <param name="readSymbols">EN: Symbols to read every cycle. TR: Her turda okunacak semboller.</param>
    public ManualPageRunner(PlcService plc, ManualPageDefinition page,
        IEnumerable<string> readSymbols, IEnumerable<string> writableSymbols)
    {
        _plc = plc;
        _page = page;
        _readSymbols = readSymbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _writableSymbols = new HashSet<string>(writableSymbols, StringComparer.OrdinalIgnoreCase);

        // DispatcherTimer bilinçli tercih: Tick UI thread'inde çalışır ve await sonrası da UI
        // thread'ine döner, böylece anlık görüntüyü ekrana taşımak için ayrıca marshaling gerekmez.
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(page.Machine.PollIntervalMs)
        };
        _timer.Tick += OnTick;
    }

    /// <summary>EN: Starts cyclic reading. TR: Döngüsel okumayı başlatır.</summary>
    public void Start()
    {
        _lastLifeAckUtc = DateTime.UtcNow;
        _lastLifeOk = null;
        _timer.Start();
    }

    /// <summary>
    /// EN: Stops cyclic reading. Does not clear commands — callers that are tearing down should
    ///     use <see cref="ShutdownAsync"/> so the machine is not left holding a command.
    /// TR: Döngüsel okumayı durdurur. Komutları temizlemez — kapatan çağıranlar
    ///     <see cref="ShutdownAsync"/> kullanmalıdır ki makine elinde komutla kalmasın.
    /// </summary>
    public void Stop() => _timer.Stop();

    /// <summary>
    /// EN: Enables or disables command writing. Disabling always clears the command bits first,
    ///     so switching back to read-only cannot leave a command set in the PLC.
    /// TR: Komut yazmayı açar veya kapatır. Kapatma her zaman önce komut bitlerini temizler;
    ///     böylece salt okunura dönmek PLC'de set kalmış bir komut bırakamaz.
    /// </summary>
    public async Task SetWriteEnabledAsync(bool enabled)
    {
        if (enabled == IsWriteEnabled) return;

        if (!enabled)
        {
            await ResetAllCommandsAsync();
            IsWriteEnabled = false;
            Notice?.Invoke(this, "Yazma kapatıldı, komut bitleri sıfırlandı.");
            return;
        }

        IsWriteEnabled = true;

        // UYARI: giden canlılık biti (watchdog) şu an kullanılmıyor — protokolü, ayrı bir PC
        // yazılımının sahip olduğu bir el sıkışma (PC 1 yazar, PLC 0'a çeker) ve o yazılım
        // çalışmıyor. Sonuç: ağ koparsa ya da bu uygulama çökerse, set kalmış bir komut bitini
        // PLC kendiliğinden düşüremez. Şu an makine bağlı olmadığı için risk yok; makine
        // bağlanmadan önce watchdog konusu yeniden ele alınmalı.
        Notice?.Invoke(this, "Yazma etkinleştirildi. Dikkat: watchdog yok, komutlar yalnızca " +
                             "bu uygulama tarafından düşürülür.");
    }

    /// <summary>
    /// EN: Writes a command value, but only for a symbol the validator approved as writable.
    ///     A rejected write is reported and not attempted — the allowlist is the last line of
    ///     defence between a UI mistake and the machine.
    /// TR: Komut değeri yazar, ancak yalnızca doğrulayıcının yazılabilir onayladığı bir sembol için.
    ///     Reddedilen yazma bildirilir ve denenmez — izin listesi, arayüzdeki bir hata ile makine
    ///     arasındaki son savunma hattıdır.
    /// </summary>
    /// <param name="symbol">EN: Command symbol. TR: Komut sembolü.</param>
    /// <param name="value">EN: Value to write. TR: Yazılacak değer.</param>
    /// <returns>EN: True when the write went through. TR: Yazma gerçekleştiyse true.</returns>
    public async Task<bool> WriteCommandAsync(string symbol, object value)
    {
        if (!IsWriteEnabled)
        {
            Notice?.Invoke(this, $"Yazma kapalıyken komut gönderilemez: '{symbol}'.");
            return false;
        }

        if (!_writableSymbols.Contains(symbol))
        {
            Notice?.Invoke(this, $"REDDEDİLDİ: '{symbol}' bu sayfanın yazabileceği semboller arasında değil.");
            return false;
        }

        try
        {
            await _plc.WriteAsync(symbol, value);

            // Başarılı komutlar da loglanır: devreye alırken "bastım ama makine tepki vermedi"
            // sorusunu ancak komutun gerçekten gönderildiğini görerek ayırt edebilirsin.
            // Bu aynı zamanda kimin neyi ne zaman komutladığının kaydıdır.
            Notice?.Invoke(this, $"KOMUT → {symbol} = {value}");
            return true;
        }
        catch (Exception ex)
        {
            Notice?.Invoke(this, $"'{symbol}' yazılamadı: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// EN: Writes false to every writable bit the page declares, except the life bit which is
    ///     handled by the cycle. Used when disarming, stopping, or closing.
    /// TR: Sayfanın bildirdiği her yazılabilir bite false yazar; tur tarafından yönetilen canlılık
    ///     biti hariç. Yazmayı kapatırken, durdururken veya kapanışta kullanılır.
    /// </summary>
    public async Task ResetAllCommandsAsync()
    {
        if (!_plc.IsConnected) return;

        var lifeOut = _page.Machine.LifeOut;
        foreach (var symbol in _writableSymbols)
        {
            if (string.Equals(symbol, lifeOut, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                // Sayısal set değerleri sıfırlanmaz; onlar bir komut değil, bir parametredir.
                // Yalnızca komut bitleri düşürülür.
                if (IsBitSymbol(symbol))
                    await _plc.WriteAsync(symbol, false);
            }
            catch (Exception ex)
            {
                Notice?.Invoke(this, $"'{symbol}' sıfırlanamadı: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// EN: Stops the cycle and leaves the PLC in a safe state: commands cleared, life bit no longer
    ///     refreshed. Call this when closing the page rather than <see cref="Stop"/>.
    /// TR: Turu durdurur ve PLC'yi güvenli durumda bırakır: komutlar temizlenir, canlılık biti artık
    ///     tazelenmez. Sayfayı kapatırken <see cref="Stop"/> yerine bunu çağırın.
    /// </summary>
    public async Task ShutdownAsync()
    {
        _timer.Stop();
        if (IsWriteEnabled)
        {
            await ResetAllCommandsAsync();
            IsWriteEnabled = false;
        }
    }

    /// <summary>
    /// EN: True when the symbol's declared data type is a single bit.
    /// TR: Sembolün bildirilen veri tipi tek bit ise true.
    /// </summary>
    private bool IsBitSymbol(string symbol) =>
        _plc.SymbolMapper.GetSymbolInfo(symbol)?.DataType
            .Equals("BOOL", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>
    /// EN: Reads every symbol once and publishes a snapshot. Skips the cycle entirely when the
    ///     previous one is still running — overlapping cycles would queue up behind the PLC gate
    ///     and drift further behind with every tick.
    /// TR: Her sembolü bir kez okur ve anlık görüntü yayınlar. Öncekisi hâlâ sürüyorsa turu
    ///     tamamen atlar — üst üste binen turlar PLC kapısının arkasında kuyruğa girer ve her
    ///     tick'te geriye daha çok düşer.
    /// </summary>
    private async void OnTick(object? sender, EventArgs e)
    {
        if (_isPolling)
        {
            _overrunCount++;
            if (_overrunCount == 1 || _overrunCount % 20 == 0)
            {
                Notice?.Invoke(this, $"Tur aşımı: okuma periyodu ({_page.Machine.PollIntervalMs} ms) " +
                                     $"yetmiyor, atlanan tur sayısı {_overrunCount}.");
            }
            return;
        }

        if (!_plc.IsConnected)
            return;

        _isPolling = true;
        var sw = Stopwatch.StartNew();
        var snapshot = new ManualPageSnapshot { OverrunCount = _overrunCount };

        try
        {
            // Canlılık el sıkışması: her turda 1 yazılır, PLC 0'a çeker. Bu, PLC'ye "PC burada"
            // demenin yolu — bu blok tipinde PLC, PC'yi yok saydığında Receive.Control alanındaki
            // komutları güvenlik gereği sıfırlayabilir, dolayısıyla komutların tutması buna bağlı.
            // Yalnızca yazma etkinken gönderilir: salt okunur izleme hiçbir şeye dokunmaz.
            // Her turda göndermek gereksiz ve pahalı: bir yazma da bir okuma kadar gidiş-dönüş
            // demek, yani turu iki katına çıkarır. Zaman aşımının üçte birinde bir göndermek hem
            // PLC'yi ikna etmeye fazlasıyla yeter, hem de okuma turunu yavaşlatmaz.
            if (IsWriteEnabled && !string.IsNullOrWhiteSpace(_page.Machine.LifeOut) &&
                (DateTime.UtcNow - _lastLifeSentUtc).TotalMilliseconds >= _page.Machine.LifeIntervalMs)
            {
                _lastLifeSentUtc = DateTime.UtcNow;
                try
                {
                    await _plc.WriteAsync(_page.Machine.LifeOut, true);
                }
                catch (Exception ex)
                {
                    Notice?.Invoke(this, $"Canlılık biti yazılamadı: {ex.Message}");
                }
            }

            var wanted = new List<string>();
            foreach (var symbol in _readSymbols)
            {
                if (_paused.Contains(symbol))
                    snapshot.PausedSymbols.Add(symbol);
                else
                    wanted.Add(symbol);
            }

            // Tek toplu çağrı: PlcService bitişik byte aralıklarını birleştirip sembol başına
            // bir gidiş-dönüş yerine blok okuma yapar.
            var read = await _plc.ReadManyAsync(wanted);
            snapshot.RoundTrips = read.RoundTrips;

            foreach (var symbol in wanted)
            {
                if (read.Errors.TryGetValue(symbol, out var error))
                {
                    snapshot.Errors[symbol] = error;
                    RecordFailure(symbol, error);
                    if (_paused.Contains(symbol))
                        snapshot.PausedSymbols.Add(symbol);
                }
                else if (read.Values.TryGetValue(symbol, out var value))
                {
                    snapshot.Values[symbol] = value;
                    ClearFailure(symbol);
                }
            }

            EvaluateLife(snapshot);
            EvaluateManualMode(snapshot);
        }
        catch (Exception ex)
        {
            // Bağlantı düzeyinde hata: tüm parti düştü.
            Notice?.Invoke(this, $"Okuma turu başarısız: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            snapshot.CycleMs = sw.ElapsedMilliseconds;
            _isPolling = false;
        }

        Updated?.Invoke(this, snapshot);
    }

    /// <summary>
    /// EN: Tracks the incoming life bit. A socket can stay open while the PLC program has stopped
    ///     updating, so a successful read is not by itself evidence that the machine is alive.
    /// TR: Gelen canlılık bitini izler. PLC programı güncellemeyi bıraksa da soket açık kalabilir;
    ///     bu yüzden başarılı bir okuma tek başına makinenin canlı olduğunun kanıtı değildir.
    /// </summary>
    private void EvaluateLife(ManualPageSnapshot snapshot)
    {
        var lifeOut = _page.Machine.LifeOut;

        // Yazma kapalıyken canlılık biti gönderilmiyor, dolayısıyla teyit beklemek anlamsız.
        if (!IsWriteEnabled || string.IsNullOrWhiteSpace(lifeOut))
        {
            snapshot.LifeOk = null;
            _lastLifeAckUtc = DateTime.UtcNow;   // yazma açılınca sayaç sıfırdan başlasın
            return;
        }

        // Yazdığımız 1'i 0 olarak geri okuduysak, PLC programı onu temizlemiş demektir.
        // Aynı turda okunduğunda PLC henüz taramamış olabilir; bu yüzden tek bir 1 okuması
        // hata sayılmaz, yalnızca zaman aşımı boyunca hiç teyit gelmemesi sayılır.
        if (snapshot.Values.TryGetValue(lifeOut, out var v) && v is bool acknowledged && !acknowledged)
            _lastLifeAckUtc = DateTime.UtcNow;

        var sinceAck = (DateTime.UtcNow - _lastLifeAckUtc).TotalMilliseconds;
        var ok = sinceAck <= _page.Machine.LifeTimeoutMs;

        if (_lastLifeOk != false && !ok)
        {
            Notice?.Invoke(this, $"PLC canlılık teyidi {sinceAck:F0} ms boyunca gelmedi — " +
                                 "PLC programı canlılık bitini sıfırlamıyor.");
        }
        else if (_lastLifeOk == false && ok)
        {
            Notice?.Invoke(this, "PLC canlılık teyidi geri geldi.");
        }

        _lastLifeOk = ok;
        snapshot.LifeOk = ok;
    }

    private bool? _lastLifeOk;

    /// <summary>
    /// EN: Reads back whether the machine confirms manual mode. Commands stay disabled unless it does.
    /// TR: Makinenin manuel modu teyit edip etmediğini geri okur. Teyit yoksa komutlar pasif kalır.
    /// </summary>
    private void EvaluateManualMode(ManualPageSnapshot snapshot)
    {
        var manualState = _page.Machine.ManualState;
        snapshot.ManualModeActive =
            !string.IsNullOrWhiteSpace(manualState) &&
            snapshot.Values.TryGetValue(manualState, out var v) && v is bool b
                ? b
                : null;
    }

    /// <summary>
    /// EN: Counts a failure and pauses the symbol once it has failed enough times in a row.
    /// TR: Hatayı sayar ve sembol üst üste yeterince hata verdiğinde onu duraklatır.
    /// </summary>
    private void RecordFailure(string symbol, string message)
    {
        _consecutiveFailures.TryGetValue(symbol, out var count);
        count++;
        _consecutiveFailures[symbol] = count;

        if (count == 1)
            Notice?.Invoke(this, $"'{symbol}' okunamadı: {message}");

        if (count == FailuresBeforePause && _paused.Add(symbol))
            Notice?.Invoke(this, $"'{symbol}' üst üste {FailuresBeforePause} kez hata verdi, artık atlanıyor.");
    }

    /// <summary>
    /// EN: Clears the failure streak and reports recovery, so a transient glitch leaves a trace
    ///     in the log without repeating every cycle.
    /// TR: Hata serisini temizler ve düzelmeyi bildirir; böylece geçici bir aksaklık her turda
    ///     tekrarlanmadan logda iz bırakır.
    /// </summary>
    private void ClearFailure(string symbol)
    {
        if (_consecutiveFailures.TryGetValue(symbol, out var count) && count > 0)
        {
            _consecutiveFailures[symbol] = 0;
            if (count >= FailuresBeforePause)
                Notice?.Invoke(this, $"'{symbol}' yeniden okunabiliyor.");
        }
    }

    /// <summary>
    /// EN: Clears the paused list so skipped symbols are retried.
    /// TR: Duraklatılmış listeyi temizler; atlanan semboller yeniden denenir.
    /// </summary>
    public void RetryPausedSymbols()
    {
        _paused.Clear();
        foreach (var key in _consecutiveFailures.Keys.ToList())
            _consecutiveFailures[key] = 0;
    }
}
