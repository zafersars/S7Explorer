namespace S7Explorer.ManualPages;

/// <summary>
/// EN: The physical panel a manual page is being designed for. A page that fits comfortably on a
///     10" panel can overflow a 7" one, and an operator standing at the machine cannot scroll a
///     membrane screen the way a mouse can scroll a window — so the page has to be laid out for the
///     screen it will actually run on.
///
///     Every metric is a screen dimension, not a style choice: colours and shapes stay identical
///     across profiles, only sizes and the available area change.
/// TR: Manuel sayfanın hangi fiziksel pano için tasarlandığı. 10" panoya rahat sığan bir sayfa
///     7"ye taşabilir ve makinenin başındaki operatör, bir pencereyi fareyle kaydırdığı gibi
///     membran ekranı kaydıramaz — bu yüzden sayfa, gerçekte çalışacağı ekrana göre yerleşmelidir.
///
///     Buradaki her ölçü bir ekran ölçüsüdür, bir stil tercihi değil: renkler ve biçimler
///     profiller arasında aynı kalır, yalnızca boyutlar ve kullanılabilir alan değişir.
/// </summary>
/// <param name="Key">EN: Stable identifier used in settings. TR: Ayarlarda kullanılan sabit kimlik.</param>
/// <param name="Inches">EN: Nominal diagonal in inches. TR: Nominal köşegen (inç).</param>
/// <param name="ScreenWidth">EN: Panel resolution width in pixels. TR: Pano çözünürlük genişliği (piksel).</param>
/// <param name="ScreenHeight">EN: Panel resolution height in pixels. TR: Pano çözünürlük yüksekliği (piksel).</param>
public record PanelProfile(
    string Key,
    double Inches,
    double ScreenWidth,
    double ScreenHeight,
    double CardWidth,
    double AlarmCardWidth,
    double AlarmListHeight,
    double ControlWidth,
    double ButtonWidth,
    double ButtonHeight,
    double ButtonGlyph,
    double PressStackWidth,
    double PressButtonWidth,
    double PressButtonHeight,
    double PressGlyph,
    double LampStackWidth,
    double LampSize,
    double ValueFontSize,
    double CaptionFontSize,
    double CardTitleSize,
    double ScreenTitleSize)
{
    /// <summary>
    /// EN: SIMATIC 7" panel, 800×480 — the KTP700 / TP700 / MTP700 class.
    ///     Roughly four fifths of the 10" metrics: still finger-sized, but noticeably tighter.
    /// TR: SIMATIC 7" pano, 800×480 — KTP700 / TP700 / MTP700 sınıfı.
    ///     10" ölçülerinin kabaca beşte dördü: hâlâ parmakla kullanılabilir ama belirgin şekilde dar.
    /// </summary>
    public static readonly PanelProfile Panel7 = new(
        Key: "7", Inches: 7, ScreenWidth: 800, ScreenHeight: 480,
        CardWidth: 240, AlarmCardWidth: 270, AlarmListHeight: 170,
        ControlWidth: 120, ButtonWidth: 120, ButtonHeight: 38, ButtonGlyph: 14,
        PressStackWidth: 88, PressButtonWidth: 82, PressButtonHeight: 70, PressGlyph: 28,
        LampStackWidth: 76, LampSize: 24,
        ValueFontSize: 21, CaptionFontSize: 10, CardTitleSize: 13, ScreenTitleSize: 22);

    /// <summary>
    /// EN: SIMATIC 10" panel, 1280×800 — the MTP1000 Unified class. This is the size the panel was
    ///     originally drawn at, so its metrics are the defaults used everywhere else.
    /// TR: SIMATIC 10" pano, 1280×800 — MTP1000 Unified sınıfı. Panel aslen bu boyutta çizildiği
    ///     için ölçüleri, diğer her yerde kullanılan varsayılanlardır.
    /// </summary>
    public static readonly PanelProfile Panel10 = new(
        Key: "10", Inches: 10, ScreenWidth: 1280, ScreenHeight: 800,
        CardWidth: 300, AlarmCardWidth: 330, AlarmListHeight: 300,
        ControlWidth: 148, ButtonWidth: 148, ButtonHeight: 44, ButtonGlyph: 16,
        PressStackWidth: 108, PressButtonWidth: 100, PressButtonHeight: 86, PressGlyph: 34,
        LampStackWidth: 92, LampSize: 30,
        ValueFontSize: 26, CaptionFontSize: 11, CardTitleSize: 15, ScreenTitleSize: 30);

    /// <summary>EN: Selectable profiles, in the order shown to the user. TR: Seçilebilir profiller, kullanıcıya gösterildiği sırayla.</summary>
    public static readonly IReadOnlyList<PanelProfile> All = [Panel7, Panel10];

    /// <summary>EN: Finds a profile by key, falling back to the 10" default. TR: Profili anahtarıyla bulur; bulunamazsa 10" varsayılanına döner.</summary>
    public static PanelProfile FromKey(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Panel10;

    /// <summary>EN: Label shown in the selector, e.g. <c>7" · 800×480</c>. TR: Seçicide gösterilen etiket, ör. <c>7" · 800×480</c>.</summary>
    public string DisplayName => $"{Inches:0.#}\" · {ScreenWidth:0}×{ScreenHeight:0}";
}
