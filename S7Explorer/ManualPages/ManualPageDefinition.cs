using System.Text.Json.Serialization;

namespace S7Explorer.ManualPages;

/// <summary>
/// EN: Control kinds a manual page may declare.
///     Command kinds write to the PLC; display kinds only read.
/// TR: Manuel sayfanın tanımlayabileceği kontrol tipleri.
///     Komut tipleri PLC'ye yazar; gösterge tipleri yalnızca okur.
/// </summary>
public enum ControlKind
{
    /// <summary>EN: Read-only bit indicator. TR: Salt okunur bit göstergesi.</summary>
    Lamp,
    /// <summary>EN: Read-only numeric display. TR: Salt okunur sayısal gösterge.</summary>
    Numeric,
    /// <summary>EN: On/off command bit, stays where the operator left it. TR: Aç/kapa komut biti, operatör bıraktığı yerde kalır.</summary>
    Toggle,
    /// <summary>EN: Active only while the button is held; released on mouse-up, focus loss or page close. TR: Yalnızca butona basılı tutulduğu sürece aktif; bırakma, odak kaybı veya sayfa kapanışında düşer.</summary>
    HoldToRun,
    /// <summary>EN: Single short pulse. TR: Tek kısa darbe.</summary>
    Momentary,
    /// <summary>EN: Set by the app, cleared by the PLC; progress reported through Busy/Done bits. TR: Uygulama set eder, PLC temizler; ilerleme Busy/Done bitleriyle bildirilir.</summary>
    Handshake,
    /// <summary>EN: Numeric setpoint written to the PLC. TR: PLC'ye yazılan sayısal set değeri.</summary>
    Setpoint,
    /// <summary>EN: Read-only state badge, e.g. the ACTIVE / PASSIVE chip beside a drive. TR: Salt okunur durum rozeti, ör. bir sürücünün yanındaki AKTİF / PASİF etiketi.</summary>
    Badge
}

/// <summary>
/// EN: A manual test page loaded from a JSON definition file.
///     Page definitions are pure data — they are validated before use and never execute code.
/// TR: JSON tanım dosyasından yüklenen manuel test sayfası.
///     Sayfa tanımları saf veridir — kullanılmadan önce doğrulanır ve asla kod çalıştırmaz.
/// </summary>
public class ManualPageDefinition
{
    /// <summary>
    /// EN: Definition format version. Only <see cref="SupportedSchemaVersion"/> is accepted.
    /// TR: Tanım format sürümü. Yalnızca <see cref="SupportedSchemaVersion"/> kabul edilir.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>EN: Page title shown to the operator. TR: Operatöre gösterilen sayfa başlığı.</summary>
    public string PageName { get; set; } = string.Empty;

    /// <summary>EN: Machine-level connection and safety settings. TR: Makine düzeyi bağlantı ve güvenlik ayarları.</summary>
    public MachineConfig Machine { get; set; } = new();

    /// <summary>EN: Controls rendered on the page, in declaration order. TR: Sayfada gösterilecek kontroller, tanım sırasıyla.</summary>
    public List<ControlDefinition> Controls { get; set; } = new();

    /// <summary>EN: Optional alarm strip fed from a symbol prefix. TR: Sembol önekinden beslenen isteğe bağlı alarm şeridi.</summary>
    public AlarmGroupDefinition? AlarmGroup { get; set; }

    /// <summary>
    /// EN: Path the definition was loaded from. Set by the loader, not present in the JSON.
    /// TR: Tanımın yüklendiği dosya yolu. Yükleyici tarafından atanır, JSON'da bulunmaz.
    /// </summary>
    [JsonIgnore]
    public string? SourceFilePath { get; set; }

    /// <summary>
    /// EN: The only schema version this build understands. Bump together with a migration path.
    /// TR: Bu sürümün anladığı tek şema sürümü. Değiştirirken göç yolu da eklenmelidir.
    /// </summary>
    public const int SupportedSchemaVersion = 1;
}

/// <summary>
/// EN: Machine-level settings: which DB the page talks to, the life (watchdog) bits,
///     the manual-mode request/confirm pair and the polling cadence.
/// TR: Makine düzeyi ayarlar: sayfanın konuştuğu DB, canlılık (watchdog) bitleri,
///     manuel mod istek/teyit çifti ve okuma periyodu.
/// </summary>
public class MachineConfig
{
    /// <summary>EN: Data block number the page operates on. TR: Sayfanın çalıştığı veri bloğu numarası.</summary>
    public int Db { get; set; }

    /// <summary>
    /// EN: Symbol prefix the page is allowed to write to. Enforced at load time:
    ///     a command control outside this prefix rejects the whole page.
    /// TR: Sayfanın yazmasına izin verilen sembol öneki. Yükleme anında dayatılır:
    ///     bu önekin dışına yazan bir komut kontrolü tüm sayfayı reddettirir.
    /// </summary>
    public string WritablePrefix { get; set; } = string.Empty;

    /// <summary>EN: Life bit written by the app so the PLC can detect PC loss. TR: PLC'nin PC kaybını algılaması için uygulamanın yazdığı canlılık biti.</summary>
    public string? LifeOut { get; set; }

    /// <summary>EN: Life bit written by the PLC so the app can detect a stalled PLC. TR: Uygulamanın PLC takılmasını algılaması için PLC'nin yazdığı canlılık biti.</summary>
    public string? LifeIn { get; set; }

    /// <summary>EN: Manual-mode request bit (app to PLC). TR: Manuel mod istek biti (uygulamadan PLC'ye).</summary>
    public string? ManualCmd { get; set; }

    /// <summary>EN: Manual-mode confirmation bit (PLC to app). Commands stay disabled until this is set. TR: Manuel mod teyit biti (PLC'den uygulamaya). Bu set olmadan komutlar pasif kalır.</summary>
    public string? ManualState { get; set; }

    /// <summary>EN: Cyclic read period in milliseconds. TR: Döngüsel okuma periyodu (milisaniye).</summary>
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>
    /// EN: How long the incoming life bit may stall before the page is treated as disconnected.
    ///     Must be comfortably larger than the poll interval or network jitter causes false trips.
    /// TR: Gelen canlılık biti bu süre boyunca değişmezse sayfa bağlantısız sayılır.
    ///     Okuma periyodundan belirgin şekilde büyük olmalı, aksi halde ağ dalgalanması boşuna alarm üretir.
    /// </summary>
    public int LifeTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// EN: How often the life bit is sent. A write costs a full PLC round trip — as much as the
    ///     whole block read — so sending it every cycle doubles the cycle time for no benefit.
    ///     Once a second is ample against a timeout measured in seconds.
    /// TR: Canlılık bitinin hangi sıklıkla gönderileceği. Bir yazma, tam bir PLC gidiş-dönüşü
    ///     demek — blok okumanın tamamı kadar — bu yüzden her turda göndermek, hiçbir kazanç
    ///     sağlamadan tur süresini ikiye katlar. Saniyeler mertebesindeki bir zaman aşımına karşı
    ///     saniyede bir fazlasıyla yeterlidir.
    /// </summary>
    public int LifeIntervalMs { get; set; } = 1000;
}

/// <summary>
/// EN: A single control on the page. Which fields apply depends on <see cref="Type"/>.
/// TR: Sayfadaki tek bir kontrol. Hangi alanların geçerli olduğu <see cref="Type"/> değerine bağlıdır.
/// </summary>
public class ControlDefinition
{
    /// <summary>
    /// EN: Control kind as written in the JSON. Kept as text so an unknown value produces
    ///     a validation error pointing at the offending line instead of a deserialization crash.
    /// TR: JSON'da yazıldığı haliyle kontrol tipi. Metin olarak tutulur ki bilinmeyen bir değer,
    ///     ayrıştırma çökmesi yerine hatalı satırı gösteren bir doğrulama hatası üretsin.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>EN: Text shown next to the control. TR: Kontrolün yanında gösterilen metin.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// EN: Optional heading this control belongs under. Controls sharing a group are drawn together
    ///     beneath one header, in declaration order. Omitting it leaves the control ungrouped.
    /// TR: Kontrolün altında yer alacağı isteğe bağlı başlık. Aynı gruptaki kontroller tek bir
    ///     başlığın altında, tanım sırasıyla birlikte çizilir. Boş bırakılırsa kontrol gruplanmaz.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>EN: Primary symbol — written for command kinds, read for display kinds. TR: Ana sembol — komut tiplerinde yazılır, gösterge tiplerinde okunur.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// EN: Optional read-back symbol. When set, the page compares command against feedback
    ///     and flags the control when the machine does not confirm.
    /// TR: İsteğe bağlı geri okuma sembolü. Tanımlıysa sayfa komut ile geri beslemeyi karşılaştırır
    ///     ve makine teyit etmediğinde kontrolü işaretler.
    /// </summary>
    public string? Feedback { get; set; }

    /// <summary>EN: Handshake only — bit reporting that the PLC is working on the request. TR: Yalnızca handshake — PLC'nin isteği işlediğini bildiren bit.</summary>
    public string? Busy { get; set; }

    /// <summary>EN: Handshake only — bit reporting that the request completed. TR: Yalnızca handshake — isteğin tamamlandığını bildiren bit.</summary>
    public string? Done { get; set; }

    /// <summary>EN: Handshake only — how long to wait for Done before reporting a fault. TR: Yalnızca handshake — hata bildirmeden önce Done için beklenecek süre.</summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>EN: Ask the operator to confirm before sending this command. TR: Bu komutu göndermeden önce operatörden onay iste.</summary>
    public bool Confirm { get; set; }

    /// <summary>
    /// EN: Whether this command needs the machine to confirm manual mode before it may be sent.
    ///     True by default, because moving an axis while the machine is running automatically is
    ///     exactly what the gate exists to prevent.
    ///
    ///     Set false only for commands that come <em>before</em> manual mode in the operating
    ///     sequence and would otherwise be unreachable — a reset after an emergency stop, or the
    ///     first reset at power-up, both of which have to run while the machine is still refusing
    ///     to go to manual. Arming is still required, so such a command is never one click away.
    /// TR: Bu komutun gönderilebilmesi için makinenin manuel modu teyit etmesi gerekip gerekmediği.
    ///     Varsayılan true, çünkü makine otomatikte çalışırken bir ekseni hareket ettirmek, kapının
    ///     var olma sebebinin ta kendisidir.
    ///
    ///     Yalnızca işletme sırasında manuel moddan <em>önce</em> gelen ve aksi halde erişilemeyecek
    ///     komutlar için false yapın — acil stoptan çıkıştaki reset ya da ilk açılıştaki reset gibi;
    ///     ikisi de makine henüz manuele geçmeyi reddederken çalışmak zorundadır. Yazmanın açık
    ///     olması yine şart olduğundan böyle bir komut asla tek tık uzakta değildir.
    /// </summary>
    public bool RequiresManualMode { get; set; } = true;

    /// <summary>EN: Unit suffix shown after the value (Hz, bar, ms...). TR: Değerin ardında gösterilen birim (Hz, bar, ms...).</summary>
    public string? Unit { get; set; }

    /// <summary>EN: Setpoint only — lowest value the operator may enter. TR: Yalnızca setpoint — operatörün girebileceği en küçük değer.</summary>
    public double? Min { get; set; }

    /// <summary>EN: Setpoint only — highest value the operator may enter. TR: Yalnızca setpoint — operatörün girebileceği en büyük değer.</summary>
    public double? Max { get; set; }

    /// <summary>EN: Decimal places used when displaying a numeric value. TR: Sayısal değer gösterilirken kullanılacak ondalık basamak sayısı.</summary>
    public int Decimals { get; set; }

    /// <summary>
    /// EN: Factor converting the raw PLC value into what the operator sees. A drive holding speed
    ///     at 0.1 Hz resolution stores 200 for 20.0 Hz, so it declares 0.1 here.
    ///
    ///     Everything the operator deals with — the displayed value and <see cref="Min"/>/<see cref="Max"/> —
    ///     is in scaled units. The raw value written to the PLC is the entry divided by this factor.
    /// TR: Ham PLC değerini operatörün gördüğü değere çeviren çarpan. Hızı 0.1 Hz çözünürlükle
    ///     tutan bir sürücü 20.0 Hz için 200 saklar; dolayısıyla buraya 0.1 yazar.
    ///
    ///     Operatörün gördüğü her şey — gösterilen değer ve <see cref="Min"/>/<see cref="Max"/> —
    ///     ölçeklenmiş birimdedir. PLC'ye yazılan ham değer, girilenin bu çarpana bölünmüşüdür.
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// EN: Indicator colour, following panel convention: "green" for healthy or running, "red" for
    ///     fault, "yellow" for attention, "blue" for information. Defaults to green.
    ///     These stay fixed across application themes — on a machine panel a red lamp means the
    ///     same thing whatever the screen looks like.
    /// TR: Gösterge rengi, pano geleneğine uygun: sağlıklı/çalışıyor için "green", arıza için "red",
    ///     dikkat için "yellow", bilgi için "blue". Varsayılan yeşildir.
    ///     Bu renkler uygulama temalarından etkilenmez — makine panosunda kırmızı lamba, ekran
    ///     nasıl görünürse görünsün aynı anlama gelir.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// EN: Segoe MDL2 Assets glyph shown on the control, e.g. "" for a forward arrow.
    ///     A picture is read faster than a word, which matters on a panel operated at arm's length.
    /// TR: Kontrolün üzerinde gösterilecek Segoe MDL2 Assets simgesi, ör. ileri ok için "".
    ///     Simge kelimeden hızlı okunur; kol mesafesinden kullanılan bir panoda bu önemlidir.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// EN: Setpoint only — how much the minus and plus buttons move the value, in display units.
    /// TR: Yalnızca setpoint — eksi ve artı butonlarının değeri kaç birim değiştireceği (gösterim birimi).
    /// </summary>
    public double Step { get; set; } = 1.0;
}

/// <summary>
/// EN: Alarm strip definition: every symbol under the prefix becomes an alarm lamp,
///     labelled from the symbol description carried in symbols.json.
/// TR: Alarm şeridi tanımı: önek altındaki her sembol bir alarm lambası olur,
///     etiketi symbols.json içindeki sembol açıklamasından alınır.
/// </summary>
public class AlarmGroupDefinition
{
    /// <summary>EN: Symbol prefix holding the alarm bits. TR: Alarm bitlerini tutan sembol öneki.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>EN: Optional summary bit indicating that at least one alarm is active. TR: En az bir alarmın aktif olduğunu bildiren isteğe bağlı özet biti.</summary>
    public string? Summary { get; set; }
}
