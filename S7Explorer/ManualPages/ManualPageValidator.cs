namespace S7Explorer.ManualPages;

/// <summary>
/// EN: How serious a validation finding is. Errors block the page from loading; warnings do not.
/// TR: Doğrulama bulgusunun ciddiyeti. Hatalar sayfanın yüklenmesini engeller; uyarılar engellemez.
/// </summary>
public enum ValidationSeverity
{
    Warning,
    Error
}

/// <summary>
/// EN: A single validation finding. <paramref name="Code"/> and <paramref name="Path"/> are stable and
///     machine-readable so the UI can localize the text later; <paramref name="Message"/> is the
///     Turkish fallback used in logs until that happens.
/// TR: Tek bir doğrulama bulgusu. <paramref name="Code"/> ve <paramref name="Path"/> sabit ve makine
///     tarafından okunabilir olduğu için arayüz metni sonradan yerelleştirebilir; <paramref name="Message"/>
///     o zamana kadar loglarda kullanılan Türkçe karşılıktır.
/// </summary>
/// <param name="Severity">EN: Error or warning. TR: Hata veya uyarı.</param>
/// <param name="Code">EN: Stable finding code. TR: Sabit bulgu kodu.</param>
/// <param name="Path">EN: Location in the definition, e.g. "controls[3].symbol". TR: Tanım içindeki konum, ör. "controls[3].symbol".</param>
/// <param name="Message">EN: Human readable description. TR: İnsan tarafından okunabilir açıklama.</param>
public record ValidationIssue(ValidationSeverity Severity, string Code, string Path, string Message);

/// <summary>
/// EN: Outcome of validating a page definition, including the symbol lists used to show the
///     operator what a page is about to touch before it is allowed to load.
/// TR: Sayfa tanımının doğrulama sonucu. Sayfanın yüklenmesine izin verilmeden önce operatöre
///     neye dokunacağını göstermek için kullanılan sembol listelerini de içerir.
/// </summary>
public class ManualPageValidationResult
{
    /// <summary>EN: All findings, in discovery order. TR: Tüm bulgular, bulunma sırasıyla.</summary>
    public List<ValidationIssue> Issues { get; } = new();

    /// <summary>EN: Symbols this page will write to. TR: Bu sayfanın yazacağı semboller.</summary>
    public List<string> WrittenSymbols { get; } = new();

    /// <summary>EN: Symbols this page will read. TR: Bu sayfanın okuyacağı semboller.</summary>
    public List<string> ReadSymbols { get; } = new();

    /// <summary>EN: True when no error-severity finding was recorded. TR: Hata seviyesinde bulgu yoksa true.</summary>
    public bool IsValid => !Issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>EN: Error-severity findings only. TR: Yalnızca hata seviyesindeki bulgular.</summary>
    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == ValidationSeverity.Error);

    /// <summary>EN: Warning-severity findings only. TR: Yalnızca uyarı seviyesindeki bulgular.</summary>
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == ValidationSeverity.Warning);

    internal void Error(string code, string path, string message) =>
        Issues.Add(new ValidationIssue(ValidationSeverity.Error, code, path, message));

    internal void Warn(string code, string path, string message) =>
        Issues.Add(new ValidationIssue(ValidationSeverity.Warning, code, path, message));
}

/// <summary>
/// EN: Validates a manual page definition against the symbol table before it is allowed to run.
///     This is where the "within rules" promise of the plugin model is actually enforced: a page
///     may only write below its declared writable prefix, only to symbols that exist, only with a
///     data type that matches the control, and only within the declared value range.
/// TR: Manuel sayfa tanımını, çalışmasına izin verilmeden önce sembol tablosuna karşı doğrular.
///     Eklenti modelinin "kurallar dahilinde" vaadi asıl burada dayatılır: bir sayfa yalnızca
///     bildirdiği yazılabilir önek altına, yalnızca var olan sembollere, yalnızca kontrole uyan
///     veri tipiyle ve yalnızca bildirilen değer aralığında yazabilir.
/// </summary>
public static class ManualPageValidator
{
    /// <summary>EN: Control kinds that write to the PLC. TR: PLC'ye yazan kontrol tipleri.</summary>
    private static readonly ControlKind[] CommandKinds =
    [
        ControlKind.Toggle, ControlKind.HoldToRun, ControlKind.Momentary,
        ControlKind.Handshake, ControlKind.Setpoint
    ];

    /// <summary>EN: Control kinds whose primary symbol is a bit. TR: Ana sembolü bit olan kontrol tipleri.</summary>
    private static readonly ControlKind[] BitKinds =
    [
        ControlKind.Lamp, ControlKind.Badge, ControlKind.Toggle, ControlKind.HoldToRun,
        ControlKind.Momentary, ControlKind.Handshake
    ];

    /// <summary>EN: PLC data types treated as numeric. TR: Sayısal kabul edilen PLC veri tipleri.</summary>
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BYTE", "SINT", "USINT", "WORD", "INT", "UINT", "DWORD", "DINT", "UDINT",
        "REAL", "LREAL", "LINT", "ULINT", "LWORD"
    };

    /// <summary>
    /// EN: Inclusive value range of the integer PLC types, used to catch a setpoint range that the
    ///     target type cannot physically hold.
    /// TR: Tamsayı PLC tiplerinin kapsayıcı değer aralığı; hedef tipin fiziksel olarak tutamayacağı
    ///     bir setpoint aralığını yakalamak için kullanılır.
    /// </summary>
    private static readonly Dictionary<string, (double Min, double Max)> IntegerRanges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BYTE"]  = (0, 255),
        ["USINT"] = (0, 255),
        ["SINT"]  = (-128, 127),
        ["WORD"]  = (0, 65535),
        ["UINT"]  = (0, 65535),
        ["INT"]   = (-32768, 32767),
        ["DWORD"] = (0, 4294967295),
        ["UDINT"] = (0, 4294967295),
        ["DINT"]  = (-2147483648, 2147483647)
    };

    /// <summary>
    /// EN: Validates the page definition. Never throws for bad content — every problem is reported
    ///     as an issue so the caller can show all of them at once.
    /// TR: Sayfa tanımını doğrular. Hatalı içerik için asla istisna fırlatmaz — her sorun bulgu olarak
    ///     bildirilir ki çağıran tümünü bir kerede gösterebilsin.
    /// </summary>
    /// <param name="page">EN: Definition to validate. TR: Doğrulanacak tanım.</param>
    /// <param name="symbols">EN: Symbol table the page is checked against. TR: Sayfanın karşısında denetlendiği sembol tablosu.</param>
    public static ManualPageValidationResult Validate(ManualPageDefinition page, SymbolMapper symbols)
    {
        var r = new ManualPageValidationResult();

        ValidateHeader(page, r);
        ValidateMachine(page, symbols, r);
        ValidateControls(page, symbols, r);
        ValidateAlarmGroup(page, symbols, r);

        return r;
    }

    /// <summary>
    /// EN: Checks the schema version and page name.
    /// TR: Şema sürümünü ve sayfa adını denetler.
    /// </summary>
    private static void ValidateHeader(ManualPageDefinition page, ManualPageValidationResult r)
    {
        if (page.SchemaVersion != ManualPageDefinition.SupportedSchemaVersion)
        {
            r.Error("UnsupportedSchemaVersion", "schemaVersion",
                $"Desteklenmeyen şema sürümü: {page.SchemaVersion}. Bu sürüm yalnızca " +
                $"{ManualPageDefinition.SupportedSchemaVersion} sürümünü okuyabilir.");
        }

        if (string.IsNullOrWhiteSpace(page.PageName))
            r.Error("MissingPageName", "pageName", "Sayfa adı boş olamaz.");
    }

    /// <summary>
    /// EN: Checks the machine block: DB number, writable prefix, life bits, manual-mode pair and timings.
    /// TR: Makine bloğunu denetler: DB numarası, yazılabilir önek, canlılık bitleri, manuel mod çifti ve zamanlamalar.
    /// </summary>
    private static void ValidateMachine(ManualPageDefinition page, SymbolMapper symbols, ManualPageValidationResult r)
    {
        var m = page.Machine;

        if (m.Db <= 0)
            r.Error("InvalidDbNumber", "machine.db", $"DB numarası pozitif olmalı, bulunan: {m.Db}.");

        if (string.IsNullOrWhiteSpace(m.WritablePrefix))
        {
            r.Error("MissingWritablePrefix", "machine.writablePrefix",
                "Yazılabilir önek tanımlanmadan sayfa yükleyemem: bu olmadan sayfanın nereye " +
                "yazabileceğini sınırlayamam.");
        }

        // Canlılık bitleri olmadan sayfa çalışır ama ağ koptuğunda komut bitleri PLC'de asılı kalır.
        // Bu güvenlik kaybını sessizce geçmiyoruz.
        if (string.IsNullOrWhiteSpace(m.LifeOut))
        {
            r.Warn("NoLifeOut", "machine.lifeOut",
                "Giden canlılık biti tanımlı değil. Bağlantı koparsa PLC bunu anlayamaz ve " +
                "komut bitleri set halde kalır.");
        }
        else
        {
            RequireSymbol(m.LifeOut, "machine.lifeOut", symbols, r, mustBeBool: true);
            RequireWritable(m.LifeOut, "machine.lifeOut", m.WritablePrefix, r);
            r.WrittenSymbols.Add(m.LifeOut);
        }

        // LifeOut el sıkışması (PC 1 yazar, PLC 0'a çeker) zaten PLC'nin canlı olduğunu kanıtlar;
        // o varken ayrı bir gelen canlılık biti aramaya gerek yok.
        if (string.IsNullOrWhiteSpace(m.LifeIn))
        {
            if (string.IsNullOrWhiteSpace(m.LifeOut))
            {
                r.Warn("NoLifeIn", "machine.lifeIn",
                    "Gelen canlılık biti tanımlı değil. PLC takılırsa uygulama bunu fark edemez.");
            }
        }
        else
        {
            RequireSymbol(m.LifeIn, "machine.lifeIn", symbols, r, mustBeBool: true);
            r.ReadSymbols.Add(m.LifeIn);
        }

        if (!string.IsNullOrWhiteSpace(m.ManualCmd))
        {
            RequireSymbol(m.ManualCmd, "machine.manualCmd", symbols, r, mustBeBool: true);
            RequireWritable(m.ManualCmd, "machine.manualCmd", m.WritablePrefix, r);
            r.WrittenSymbols.Add(m.ManualCmd);
        }

        if (string.IsNullOrWhiteSpace(m.ManualState))
        {
            r.Warn("NoManualState", "machine.manualState",
                "Manuel mod teyit biti tanımlı değil. Makine manuel modda olmasa da komutlar " +
                "aktif görünecek.");
        }
        else
        {
            RequireSymbol(m.ManualState, "machine.manualState", symbols, r, mustBeBool: true);
            r.ReadSymbols.Add(m.ManualState);
        }

        if (m.PollIntervalMs < 100 || m.PollIntervalMs > 60_000)
        {
            r.Error("InvalidPollInterval", "machine.pollIntervalMs",
                $"Okuma periyodu 100–60000 ms aralığında olmalı, bulunan: {m.PollIntervalMs}.");
        }

        // Canlılık zaman aşımı periyodun en az iki katı olmalı; aksi halde tek bir gecikmiş
        // tur bile boşuna komut düşürür.
        if (m.LifeTimeoutMs < m.PollIntervalMs * 2)
        {
            r.Error("LifeTimeoutTooShort", "machine.lifeTimeoutMs",
                $"Canlılık zaman aşımı ({m.LifeTimeoutMs} ms) okuma periyodunun ({m.PollIntervalMs} ms) " +
                "en az iki katı olmalı, yoksa tek bir gecikmiş tur boşuna alarm üretir.");
        }
    }

    /// <summary>
    /// EN: Checks every control: kind, label, symbol existence, write permission, data type match,
    ///     kind-specific fields, and that no two command controls drive the same bit.
    /// TR: Her kontrolü denetler: tip, etiket, sembolün varlığı, yazma izni, veri tipi uyumu,
    ///     tipe özgü alanlar ve iki komut kontrolünün aynı biti sürmediği.
    /// </summary>
    private static void ValidateControls(ManualPageDefinition page, SymbolMapper symbols, ManualPageValidationResult r)
    {
        if (page.Controls.Count == 0)
            r.Warn("NoControls", "controls", "Sayfada hiç kontrol tanımlı değil.");

        var commandSymbols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < page.Controls.Count; i++)
        {
            var c = page.Controls[i];
            var path = $"controls[{i}]";

            if (!Enum.TryParse<ControlKind>(c.Type, ignoreCase: true, out var kind))
            {
                r.Error("UnknownControlType", $"{path}.type",
                    $"Bilinmeyen kontrol tipi: '{c.Type}'. Geçerli tipler: " +
                    string.Join(", ", Enum.GetNames<ControlKind>()));
                continue;
            }

            if (string.IsNullOrWhiteSpace(c.Label))
                r.Warn("MissingLabel", $"{path}.label", "Kontrol etiketi boş; operatör ne olduğunu anlayamaz.");

            if (string.IsNullOrWhiteSpace(c.Symbol))
            {
                r.Error("MissingSymbol", $"{path}.symbol", "Kontrol sembolü boş olamaz.");
                continue;
            }

            var isCommand = CommandKinds.Contains(kind);
            var wantsBit = BitKinds.Contains(kind);

            RequireSymbol(c.Symbol, $"{path}.symbol", symbols, r, mustBeBool: wantsBit, mustBeNumeric: !wantsBit);

            if (isCommand)
            {
                RequireWritable(c.Symbol, $"{path}.symbol", page.Machine.WritablePrefix, r);
                r.WrittenSymbols.Add(c.Symbol);

                // Aynı biti iki ayrı butonun sürmesi, operatörün gördüğü durumla PLC'deki
                // durumun ayrışmasına yol açar.
                if (commandSymbols.TryGetValue(c.Symbol, out var firstIndex))
                {
                    r.Error("DuplicateCommandSymbol", $"{path}.symbol",
                        $"'{c.Symbol}' sembolü zaten controls[{firstIndex}] tarafından sürülüyor. " +
                        "Bir komut bitini yalnızca tek bir kontrol sürmelidir.");
                }
                else
                {
                    commandSymbols[c.Symbol] = i;
                }
            }
            else
            {
                r.ReadSymbols.Add(c.Symbol);

                // Gösterge kontrolü yazılabilir alana bakıyorsa, büyük ihtimalle Send yerine
                // yanlışlıkla Receive sembolü yazılmıştır. Rozet bunun istisnasıdır: bir komut
                // bitinin geri okunmuş halini göstermek onun asıl işidir.
                if (kind != ControlKind.Badge && IsUnderPrefix(c.Symbol, page.Machine.WritablePrefix))
                {
                    r.Warn("DisplayReadsCommandArea", $"{path}.symbol",
                        $"'{c.Symbol}' yazılabilir alanın içinde. Gösterge kontrolleri genelde " +
                        "PLC'nin bildirdiği durum sembollerini okumalıdır.");
                }
            }

            if (!string.IsNullOrWhiteSpace(c.Feedback))
            {
                RequireSymbol(c.Feedback, $"{path}.feedback", symbols, r, mustBeBool: wantsBit, mustBeNumeric: !wantsBit);
                r.ReadSymbols.Add(c.Feedback);
            }

            if (kind == ControlKind.Handshake)
                ValidateHandshake(c, path, symbols, r);

            if (kind == ControlKind.Setpoint)
                ValidateSetpoint(c, path, symbols, r);
        }
    }

    /// <summary>
    /// EN: Handshake controls need progress feedback; without Done the page can never tell the
    ///     operator whether the command succeeded.
    /// TR: Handshake kontrolleri ilerleme geri beslemesi gerektirir; Done olmadan sayfa operatöre
    ///     komutun başarılı olup olmadığını hiçbir zaman söyleyemez.
    /// </summary>
    private static void ValidateHandshake(ControlDefinition c, string path, SymbolMapper symbols, ManualPageValidationResult r)
    {
        if (string.IsNullOrWhiteSpace(c.Done))
        {
            r.Error("HandshakeMissingDone", $"{path}.done",
                "Handshake kontrolü için tamamlanma biti zorunlu, yoksa komutun bittiği anlaşılamaz.");
        }
        else
        {
            RequireSymbol(c.Done, $"{path}.done", symbols, r, mustBeBool: true);
            r.ReadSymbols.Add(c.Done);
        }

        if (!string.IsNullOrWhiteSpace(c.Busy))
        {
            RequireSymbol(c.Busy, $"{path}.busy", symbols, r, mustBeBool: true);
            r.ReadSymbols.Add(c.Busy);
        }

        if (c.TimeoutMs <= 0)
            r.Error("InvalidHandshakeTimeout", $"{path}.timeoutMs", $"Zaman aşımı pozitif olmalı, bulunan: {c.TimeoutMs}.");
    }

    /// <summary>
    /// EN: Setpoints must declare a range, and that range has to fit the target PLC type —
    ///     otherwise an accepted entry silently wraps when written.
    /// TR: Setpoint'ler bir aralık bildirmek zorunda ve bu aralık hedef PLC tipine sığmalı —
    ///     aksi halde kabul edilen bir giriş yazılırken sessizce taşar.
    /// </summary>
    private static void ValidateSetpoint(ControlDefinition c, string path, SymbolMapper symbols, ManualPageValidationResult r)
    {
        if (c.Min is null || c.Max is null)
        {
            r.Error("SetpointMissingRange", $"{path}.min/max",
                "Set değeri için min ve max zorunlu: operatörün girebileceği aralığı sınırlayan tek şey bu.");
            return;
        }

        if (c.Min >= c.Max)
        {
            r.Error("SetpointInvalidRange", $"{path}.min/max",
                $"min ({c.Min}) değeri max ({c.Max}) değerinden küçük olmalı.");
            return;
        }

        if (c.Scale == 0)
        {
            r.Error("InvalidScale", $"{path}.scale",
                "Ölçek sıfır olamaz: ham değere çeviremem.");
            return;
        }

        // min/max operatör birimindedir; tip sınırı ise ham değeri bağlar. Karşılaştırmadan
        // önce ölçeği geri alıyoruz, yoksa 0.1 çözünürlüklü bir alan boşuna reddedilir.
        var dataType = symbols.GetSymbolInfo(c.Symbol)?.DataType;
        if (!string.IsNullOrWhiteSpace(dataType) &&
            IntegerRanges.TryGetValue(dataType, out var range))
        {
            var rawMin = c.Min.Value / c.Scale;
            var rawMax = c.Max.Value / c.Scale;
            if (rawMin > rawMax) (rawMin, rawMax) = (rawMax, rawMin);  // negatif ölçek

            if (rawMin < range.Min || rawMax > range.Max)
            {
                r.Error("SetpointRangeExceedsType", $"{path}.min/max",
                    $"Bildirilen aralık ({c.Min}–{c.Max}) ham değere çevrildiğinde " +
                    $"({rawMin}–{rawMax}), '{dataType}' tipinin taşıyabileceği aralığı " +
                    $"({range.Min}–{range.Max}) aşıyor.");
            }
        }
    }

    /// <summary>
    /// EN: Checks the optional alarm group and warns when its prefix matches no symbols at all.
    /// TR: İsteğe bağlı alarm grubunu denetler ve öneki hiçbir sembolle eşleşmiyorsa uyarır.
    /// </summary>
    private static void ValidateAlarmGroup(ManualPageDefinition page, SymbolMapper symbols, ManualPageValidationResult r)
    {
        var g = page.AlarmGroup;
        if (g is null) return;

        if (string.IsNullOrWhiteSpace(g.Prefix))
        {
            r.Error("AlarmGroupMissingPrefix", "alarmGroup.prefix", "Alarm grubu öneki boş olamaz.");
            return;
        }

        // Reserve_* bitleri atlanır: DB'lerde alarm alanı ileride büyümek üzere sonuna kadar
        // rezerve bitlerle doldurulur (DB_PC'de 64 bitin ~15'i isimli). Bunları alarm şeridine
        // koymak operatöre onlarca anlamsız lamba göstermek ve her turda boşuna okumak olur.
        var matches = symbols.GetAllSymbols().Keys
            .Where(k => k.StartsWith(g.Prefix, StringComparison.OrdinalIgnoreCase))
            .Where(k => !IsReserveSymbol(k))
            .ToList();

        if (matches.Count == 0)
        {
            r.Warn("AlarmGroupEmpty", "alarmGroup.prefix",
                $"'{g.Prefix}' önekiyle eşleşen sembol yok; alarm şeridi boş kalacak.");
        }
        else
        {
            r.ReadSymbols.AddRange(matches);
        }

        if (!string.IsNullOrWhiteSpace(g.Summary))
        {
            RequireSymbol(g.Summary, "alarmGroup.summary", symbols, r, mustBeBool: true);
            r.ReadSymbols.Add(g.Summary);
        }
    }

    /// <summary>
    /// EN: Requires that the symbol exists in the symbol table and, optionally, that its data type
    ///     matches what the control needs. A symbol with no recorded type only warns, since symbol
    ///     files imported from the old format carry no type information.
    /// TR: Sembolün sembol tablosunda bulunmasını ve isteğe bağlı olarak veri tipinin kontrolün
    ///     ihtiyacıyla uyuşmasını şart koşar. Tipi kayıtlı olmayan sembol yalnızca uyarı üretir,
    ///     çünkü eski formattan alınan sembol dosyalarında tip bilgisi yoktur.
    /// </summary>
    private static void RequireSymbol(string symbol, string path, SymbolMapper symbols,
        ManualPageValidationResult r, bool mustBeBool = false, bool mustBeNumeric = false)
    {
        var info = symbols.GetSymbolInfo(symbol);
        if (info is null)
        {
            r.Error("SymbolNotFound", path,
                $"'{symbol}' sembol tablosunda yok. Sembolleri yükleyip sayfayı yeniden açın.");
            return;
        }

        if (string.IsNullOrWhiteSpace(info.DataType))
        {
            r.Warn("SymbolTypeUnknown", path,
                $"'{symbol}' sembolünün veri tipi kayıtlı değil; tip uyumu denetlenemedi.");
            return;
        }

        if (mustBeBool && !info.DataType.Equals("BOOL", StringComparison.OrdinalIgnoreCase))
        {
            r.Error("SymbolTypeMismatch", path,
                $"Bu kontrol BOOL sembol bekliyor ancak '{symbol}' tipi '{info.DataType}'.");
        }
        else if (mustBeNumeric && !NumericTypes.Contains(info.DataType))
        {
            r.Error("SymbolTypeMismatch", path,
                $"Bu kontrol sayısal sembol bekliyor ancak '{symbol}' tipi '{info.DataType}'.");
        }
    }

    /// <summary>
    /// EN: Requires that a written symbol sits below the page's declared writable prefix.
    ///     This is the rule that keeps a page from reaching into another machine's area.
    /// TR: Yazılan bir sembolün, sayfanın bildirdiği yazılabilir önek altında olmasını şart koşar.
    ///     Bir sayfanın başka bir makinenin alanına uzanmasını engelleyen kural budur.
    /// </summary>
    private static void RequireWritable(string symbol, string path, string writablePrefix, ManualPageValidationResult r)
    {
        if (string.IsNullOrWhiteSpace(writablePrefix)) return; // eksik önek zaten ayrıca raporlandı

        if (!IsUnderPrefix(symbol, writablePrefix))
        {
            r.Error("WriteOutsideAllowedPrefix", path,
                $"'{symbol}' yazılabilir önek '{writablePrefix}' dışında. Sayfanın bu sembole " +
                "yazmasına izin verilmiyor.");
        }
    }

    /// <summary>
    /// EN: True when the symbol's leaf name marks it as an unused reserve placeholder.
    ///     Reserve fields are how these DBs leave room to grow; they carry no meaning for the operator.
    /// TR: Sembolün son parçası onu kullanılmayan rezerv alanı olarak işaretliyorsa true.
    ///     Rezerv alanlar bu DB'lerin büyümeye yer bırakma biçimidir; operatör için anlam taşımazlar.
    /// </summary>
    private static bool IsReserveSymbol(string symbol)
    {
        var lastDot = symbol.LastIndexOf('.');
        var leaf = lastDot >= 0 ? symbol[(lastDot + 1)..] : symbol;
        return leaf.StartsWith("Reserve", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EN: True when the symbol sits below the given prefix.
    /// TR: Sembol verilen önek altındaysa true.
    /// </summary>
    private static bool IsUnderPrefix(string symbol, string prefix) =>
        !string.IsNullOrWhiteSpace(prefix) &&
        symbol.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
