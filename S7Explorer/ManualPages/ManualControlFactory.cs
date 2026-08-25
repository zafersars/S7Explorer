using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace S7Explorer.ManualPages;

/// <summary>
/// EN: A rendered control bound to one <see cref="ControlDefinition"/>. Holds the visual, the
///     logic that sends its command, and the logic that refreshes it from a cycle snapshot.
/// TR: Tek bir <see cref="ControlDefinition"/> için üretilmiş kontrol. Görseli, komutunu gönderen
///     mantığı ve turdan gelen anlık görüntüyle kendini tazeleyen mantığı bir arada tutar.
/// </summary>
public abstract class ManualControl
{
    /// <summary>EN: Definition this control was built from. TR: Bu kontrolün üretildiği tanım.</summary>
    public ControlDefinition Definition { get; }

    /// <summary>EN: Element to place on the card. TR: Karta yerleştirilecek öğe.</summary>
    public FrameworkElement Element { get; protected set; } = new Grid();

    /// <summary>EN: Runner used to send commands. TR: Komut göndermek için kullanılan koşucu.</summary>
    protected ManualPageRunner Runner { get; }

    /// <summary>EN: Whether commands are currently permitted. TR: Komutlara şu an izin verilip verilmediği.</summary>
    protected bool CommandsAllowed { get; private set; }

    /// <summary>
    /// EN: True for the control that requests manual mode itself. It must stay usable while the
    ///     machine is still in automatic, otherwise the gate locks out the only switch that can
    ///     open it. Being armed is still required.
    /// TR: Manuel modu talep eden kontrolün kendisi için true. Makine hâlâ otomatikteyken de
    ///     kullanılabilir kalmalıdır; aksi halde kapı, kendisini açabilecek tek anahtarı da
    ///     kilitler. Yazmanın açık olması yine şarttır.
    /// </summary>
    public bool IsManualModeControl { get; set; }

    /// <summary>
    /// EN: True when this control may be operated while the machine has not confirmed manual mode.
    ///     Two cases qualify: the control that requests manual mode itself, and a command the page
    ///     explicitly declared with <c>requiresManualMode: false</c> — a reset, typically, which the
    ///     operator has to run before the machine will accept manual at all. Arming is still required.
    /// TR: Makine manuel modu teyit etmemişken de kullanılabilen kontroller için true.
    ///     İki durum bu kapsama girer: manuel modu talep eden kontrolün kendisi ve sayfanın açıkça
    ///     <c>requiresManualMode: false</c> ile bildirdiği bir komut — tipik olarak, makine manueli
    ///     kabul etmeden önce operatörün çalıştırmak zorunda olduğu reset. Yazmanın açık olması yine şarttır.
    /// </summary>
    public bool BypassesManualModeGate => IsManualModeControl || !Definition.RequiresManualMode;

    /// <summary>EN: True for controls that write. TR: Yazan kontroller için true.</summary>
    protected virtual bool IsCommand => true;

    /// <summary>
    /// EN: The element's state line, held here so the base can explain why a control is locked.
    ///     "Nothing happened when I pressed it" is the commonest question on a commissioning
    ///     screen, and a disabled button answers it with silence unless the reason is written down.
    /// TR: Elemanın durum satırı; kontrolün neden kilitli olduğunu tabanın yazabilmesi için burada
    ///     tutulur. Devreye almada en sık sorulan soru "bastım, bir şey olmadı"dır ve pasif bir
    ///     buton, sebep yazılmadıkça bu soruyu sessizlikle cevaplar.
    /// </summary>
    protected TextBlock? StateBlock { get; set; }

    protected ManualControl(ControlDefinition definition, ManualPageRunner runner)
    {
        Definition = definition;
        Runner = runner;
    }

    /// <summary>EN: Refreshes the control from the latest cycle. TR: Kontrolü son turdan gelen verilerle tazeler.</summary>
    public void Update(ManualPageSnapshot snapshot, bool commandsAllowed, string? lockReason = null)
    {
        CommandsAllowed = commandsAllowed;
        OnUpdate(snapshot);

        if (IsCommand && !commandsAllowed && StateBlock is not null && lockReason is not null)
            StateBlock.Text = lockReason;
    }

    /// <summary>EN: Type-specific refresh. TR: Tipe özgü tazeleme.</summary>
    protected abstract void OnUpdate(ManualPageSnapshot snapshot);

    /// <summary>EN: Called when the page stops or disarms, so a control holding a command can let go. TR: Sayfa durduğunda veya yazma kapandığında çağrılır; komut tutan bir kontrol bırakabilsin.</summary>
    public virtual Task ReleaseAsync() => Task.CompletedTask;

    /// <summary>EN: Reads a bool from the snapshot. TR: Anlık görüntüden bool okur.</summary>
    protected static bool? Bit(ManualPageSnapshot s, string? symbol) =>
        !string.IsNullOrWhiteSpace(symbol) && s.Values.TryGetValue(symbol, out var v) && v is bool b ? b : null;

    /// <summary>
    /// EN: Asks for confirmation when the definition demands it. Dangerous commands should not be
    ///     one click away from an operator who meant to press the button next to it.
    /// TR: Tanım istiyorsa onay sorar. Tehlikeli komutlar, yanındaki butona basmak isteyen bir
    ///     operatörden tek tık uzakta olmamalıdır.
    /// </summary>
    protected bool ConfirmIfRequired()
    {
        if (!Definition.Confirm) return true;

        return MessageDialog.Show(
            LocalizationManager.Instance.T("Manual_ConfirmCommand", Definition.Label),
            LocalizationManager.Instance.T("MsgTitle_Confirm"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning,
            Window.GetWindow(Element)) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// EN: Stacks a caption above a control and a state line below, the arrangement every element
    ///     on the panel shares.
    /// TR: Bir başlığı kontrolün üstüne, durum satırını altına dizer; paneldeki her elemanın
    ///     paylaştığı düzen budur.
    /// </summary>
    protected static StackPanel Stack(string? caption, UIElement control, out TextBlock state,
                                      double width, bool captionBelow = false)
    {
        var panel = new StackPanel { Width = width, Margin = new Thickness(0, 0, 6, 8) };

        var captionBlock = string.IsNullOrWhiteSpace(caption) ? null : HmiStyle.Caption(caption!);
        if (captionBlock is not null && !captionBelow)
        {
            captionBlock.Margin = new Thickness(0, 0, 0, 5);
            panel.Children.Add(captionBlock);
        }

        if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(control);

        if (captionBlock is not null && captionBelow)
        {
            captionBlock.Margin = new Thickness(0, 5, 0, 0);
            panel.Children.Add(captionBlock);
        }

        state = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = HmiStyle.TextLow,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0)
        };
        panel.Children.Add(state);

        return panel;
    }
}

/// <summary>
/// EN: Builds the visual controls a page declares. Anything that writes stays disabled until
///     writing is armed and the machine confirms manual mode.
/// TR: Bir sayfanın bildirdiği görsel kontrolleri üretir. Yazan her şey, yazma etkinleştirilene ve
///     makine manuel modu teyit edene kadar pasif kalır.
/// </summary>
public static class ManualControlFactory
{
    /// <summary>EN: Creates the control for a definition, or null when its type is not renderable. TR: Bir tanım için kontrolü üretir; tipi çizilemiyorsa null döner.</summary>
    public static ManualControl? Create(ControlDefinition definition, ManualPageRunner runner, SymbolMapper symbols)
    {
        if (!Enum.TryParse<ControlKind>(definition.Type, ignoreCase: true, out var kind))
            return null;

        return kind switch
        {
            ControlKind.Lamp      => new LampControl(definition, runner),
            ControlKind.Badge     => new BadgeControl(definition, runner),
            ControlKind.Numeric   => new NumericControl(definition, runner),
            ControlKind.Toggle    => new ToggleControl(definition, runner),
            ControlKind.HoldToRun => new HoldToRunControl(definition, runner),
            ControlKind.Momentary => new MomentaryControl(definition, runner),
            ControlKind.Handshake => new HandshakeControl(definition, runner),
            ControlKind.Setpoint  => new SetpointControl(definition, runner, symbols),
            _                     => null
        };
    }
}

/// <summary>EN: Read-only pilot lamp with its caption. TR: Başlığıyla birlikte salt okunur sinyal lambası.</summary>
public class LampControl : ManualControl
{
    protected override bool IsCommand => false;

    private readonly Ellipse _lamp = HmiStyle.Lamp();
    private readonly TextBlock _state;
    private readonly Color _color;

    public LampControl(ControlDefinition d, ManualPageRunner r) : base(d, r)
    {
        _color = HmiStyle.ResolveLampColor(d.Color);
        Element = Stack(d.Label, _lamp, out _state, HmiStyle.Metrics.LampStackWidth);
        StateBlock = _state;
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        var v = Bit(s, Definition.Symbol);
        HmiStyle.SetLamp(_lamp, v, _color);
        _state.Text = string.Empty;
    }
}

/// <summary>EN: Read-only state badge, e.g. ACTIVE / PASSIVE beside a drive. TR: Salt okunur durum rozeti, ör. bir sürücünün yanında AKTİF / PASİF.</summary>
public class BadgeControl : ManualControl
{
    protected override bool IsCommand => false;

    private readonly Border _pill;
    private readonly TextBlock _pillText;
    private readonly TextBlock _state;

    public BadgeControl(ControlDefinition d, ManualPageRunner r) : base(d, r)
    {
        _pill = HmiStyle.Pill(out _pillText);

        // Rozet, başlığıyla yan yana durur: "Durum [AKTİF]" tek satırda okunur.
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = d.Label, FontSize = HmiStyle.Metrics.CaptionFontSize, Foreground = HmiStyle.TextMid,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(_pill);

        Element = Stack(null, row, out _state, HmiStyle.Metrics.ControlWidth);
        StateBlock = _state;
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        var L = LocalizationManager.Instance;
        var v = Bit(s, Definition.Symbol);
        HmiStyle.SetPill(_pill, _pillText,
            v switch { true => L.T("Manual_BadgeOn"), false => L.T("Manual_BadgeOff"), null => "—" },
            v switch { true => HmiStyle.Green, false => HmiStyle.OffGrey, null => HmiStyle.OffGrey });
        _state.Text = string.Empty;
    }
}

/// <summary>EN: Read-only measurement, shown large the way a panel shows a live value. TR: Salt okunur ölçüm; panonun canlı değeri gösterdiği gibi büyük.</summary>
public class NumericControl : ManualControl
{
    protected override bool IsCommand => false;

    private readonly TextBlock _value = HmiStyle.BigValue();
    private readonly TextBlock _state;

    public NumericControl(ControlDefinition d, ManualPageRunner r) : base(d, r)
    {
        var caption = string.IsNullOrWhiteSpace(d.Unit) ? d.Label : $"{d.Label} ({d.Unit})";
        Element = Stack(caption, _value, out _state, HmiStyle.Metrics.ControlWidth);
        StateBlock = _state;
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        _value.Text = s.Values.TryGetValue(Definition.Symbol, out var v) && v is not null
            ? FormatScaled(v, Definition)
            : "––";
        _state.Text = string.Empty;
    }

    /// <summary>
    /// EN: Converts a raw PLC value to the operator's units and formats it. Non-numeric values are
    ///     shown as they are rather than silently becoming zero.
    /// TR: Ham PLC değerini operatörün birimine çevirir ve biçimlendirir. Sayısal olmayan değerler
    ///     sessizce sıfıra dönmek yerine olduğu gibi gösterilir.
    /// </summary>
    internal static string FormatScaled(object value, ControlDefinition definition)
    {
        if (value is not IConvertible || value is string || value is bool)
            return value.ToString() ?? "—";

        double scaled;
        try { scaled = Convert.ToDouble(value, CultureInfo.InvariantCulture) * definition.Scale; }
        catch (Exception) { return value.ToString() ?? "—"; }

        return scaled.ToString("F" + Math.Max(0, definition.Decimals), CultureInfo.CurrentCulture);
    }
}

/// <summary>
/// EN: On/off command bit. The button reflects the value read back from the PLC, not what was
///     clicked — if the PLC refuses the command the operator sees it stay off.
/// TR: Aç/kapa komut biti. Buton, tıklananı değil PLC'den geri okunanı yansıtır — PLC komutu kabul
///     etmezse operatör kapalı kaldığını görür.
/// </summary>
public class ToggleControl : ManualControl
{
    private readonly Button _button;
    private readonly TextBlock _state;
    private readonly Color _color;
    private bool _lastKnown;

    public ToggleControl(ControlDefinition d, ManualPageRunner r) : base(d, r)
    {
        _color = HmiStyle.ResolveLampColor(d.Color);
        _button = HmiStyle.SolidButton(d.Icon, d.Label, HmiStyle.Metrics.ButtonWidth,
            HmiStyle.Metrics.ButtonHeight, HmiStyle.Metrics.ButtonGlyph);
        _button.Click += OnClick;
        Element = Stack(null, _button, out _state, 148);
        StateBlock = _state;
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (!CommandsAllowed || !ConfirmIfRequired()) return;

        // Hedef, bilinen son durumun tersi. Yazma başarılıysa beklenen değer hemen kaydedilir ki
        // aynı tur içinde ikinci kez basmak (periyot 500 ms, insan daha hızlı tıklar) kaybolmasın.
        var wanted = !_lastKnown;
        if (await Runner.WriteCommandAsync(Definition.Symbol, wanted))
            _lastKnown = wanted;
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        var L = LocalizationManager.Instance;
        var actual = Bit(s, Definition.Symbol);
        if (actual is not null) _lastKnown = actual.Value;

        HmiStyle.PaintButton(_button, actual == true ? _color : null, CommandsAllowed);

        var feedback = Bit(s, Definition.Feedback);
        _state.Text = feedback is null
            ? (actual == true ? L.T("Manual_StateOn") : actual == false ? L.T("Manual_StateOff") : "—")
            : feedback == true ? L.T("Manual_StateConfirmed") : actual == true ? L.T("Manual_StatePending") : "—";
    }
}

/// <summary>
/// EN: Active only while the button is physically held. The bit is dropped on mouse-up, on losing
///     mouse capture, on leaving the button, and when the page releases the control — an operator
///     who drags off the button, alt-tabs away or closes the window must not leave it commanded.
/// TR: Yalnızca butona fiziksel olarak basılı tutulduğu sürece aktif. Bit; fare bırakıldığında,
///     yakalama kaybedildiğinde, butondan çıkıldığında ve sayfa kontrolü serbest bıraktığında
///     düşer — butondan kayan, alt-tab yapan veya pencereyi kapatan operatör komutu bırakmamalıdır.
/// </summary>
public class HoldToRunControl : ManualControl
{
    private readonly Button _button;
    private readonly TextBlock _state;
    private readonly Color _color;
    private bool _held;

    public HoldToRunControl(ControlDefinition d, ManualPageRunner r) : base(d, r)
    {
        _color = HmiStyle.ResolveLampColor(d.Color);
        _button = HmiStyle.SolidButton(d.Icon, string.IsNullOrWhiteSpace(d.Icon) ? d.Label : null,
            HmiStyle.Metrics.PressButtonWidth, HmiStyle.Metrics.PressButtonHeight, HmiStyle.Metrics.PressGlyph);
        _button.PreviewMouseLeftButtonDown += async (_, _) => await PressAsync();
        _button.PreviewMouseLeftButtonUp   += async (_, _) => await ReleaseAsync();
        _button.LostMouseCapture           += async (_, _) => await ReleaseAsync();
        _button.MouseLeave                 += async (_, _) => await ReleaseAsync();
        Element = Stack(d.Label, _button, out _state, HmiStyle.Metrics.PressStackWidth);
        StateBlock = _state;
    }

    private async Task PressAsync()
    {
        if (_held || !CommandsAllowed || !ConfirmIfRequired()) return;
        _held = true;
        await Runner.WriteCommandAsync(Definition.Symbol, true);
    }

    public override async Task ReleaseAsync()
    {
        if (!_held) return;
        _held = false;
        await Runner.WriteCommandAsync(Definition.Symbol, false);
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        if (!CommandsAllowed && _held) _ = ReleaseAsync();

        var actual = Bit(s, Definition.Symbol);
        HmiStyle.PaintButton(_button, actual == true ? _color : HmiStyle.Slate, CommandsAllowed);
        _state.Text = string.Empty;
    }
}

/// <summary>EN: Single short pulse, for trigger-style commands. TR: Tetik tarzı komutlar için tek kısa darbe.</summary>
public class MomentaryControl : ManualControl
{
    private const int PulseMs = 300;

    private readonly Button _button;
    private readonly TextBlock _state;
    private readonly Color _color;
    private bool _busy;

    public MomentaryControl(ControlDefinition d, ManualPageRunner r) : base(d, r)
    {
        _color = HmiStyle.ResolveLampColor(d.Color ?? "blue");
        _button = HmiStyle.SolidButton(d.Icon, string.IsNullOrWhiteSpace(d.Icon) ? d.Label : null,
            HmiStyle.Metrics.PressButtonWidth, HmiStyle.Metrics.PressButtonHeight, HmiStyle.Metrics.PressGlyph);
        _button.Click += OnClick;
        Element = Stack(d.Label, _button, out _state, HmiStyle.Metrics.PressStackWidth);
        StateBlock = _state;
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy || !CommandsAllowed || !ConfirmIfRequired()) return;

        _busy = true;
        try
        {
            if (await Runner.WriteCommandAsync(Definition.Symbol, true))
            {
                await Task.Delay(PulseMs);
                await Runner.WriteCommandAsync(Definition.Symbol, false);
            }
        }
        finally { _busy = false; }
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        var actual = Bit(s, Definition.Symbol);
        HmiStyle.PaintButton(_button, actual == true || _busy ? _color : _color, CommandsAllowed && !_busy);
        _state.Text = string.Empty;
    }

    public override async Task ReleaseAsync()
    {
        if (_busy) await Runner.WriteCommandAsync(Definition.Symbol, false);
    }
}

/// <summary>
/// EN: Set by the app, cleared by the PLC. Progress comes from the Busy/Done bits, and a timeout
///     turns a request the machine never answered into a visible fault instead of a stuck button.
/// TR: Uygulama set eder, PLC temizler. İlerleme Busy/Done bitlerinden gelir; zaman aşımı,
///     makinenin hiç cevaplamadığı isteği takılı buton yerine görünür bir hataya çevirir.
/// </summary>
public class HandshakeControl : ManualControl
{
    private readonly Button _button;
    private readonly TextBlock _state;
    private readonly Color _color;
    private DateTime? _startedUtc;

    public HandshakeControl(ControlDefinition d, ManualPageRunner r) : base(d, r)
    {
        _color = HmiStyle.ResolveLampColor(d.Color ?? "yellow");
        _button = HmiStyle.SolidButton(d.Icon, d.Label, HmiStyle.Metrics.ButtonWidth,
            HmiStyle.Metrics.ButtonHeight, HmiStyle.Metrics.ButtonGlyph);
        _button.Click += OnClick;
        Element = Stack(null, _button, out _state, 148);
        StateBlock = _state;
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (_startedUtc is not null || !CommandsAllowed || !ConfirmIfRequired()) return;
        if (await Runner.WriteCommandAsync(Definition.Symbol, true)) _startedUtc = DateTime.UtcNow;
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        var L = LocalizationManager.Instance;
        var busy = Bit(s, Definition.Busy);
        var done = Bit(s, Definition.Done);

        if (_startedUtc is { } started)
        {
            var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
            if (done == true)            { _state.Text = L.T("Manual_HandshakeDone"); _startedUtc = null; }
            else if (elapsed > Definition.TimeoutMs) { _state.Text = L.T("Manual_HandshakeTimeout", Definition.TimeoutMs); _startedUtc = null; }
            else _state.Text = busy == true ? L.T("Manual_HandshakeBusy", (int)elapsed)
                                            : L.T("Manual_HandshakeWaiting", (int)elapsed);
        }
        else
        {
            _state.Text = done == true ? L.T("Manual_HandshakeDone") : string.Empty;
        }

        HmiStyle.PaintButton(_button, _startedUtc is not null || busy == true ? _color : null,
            CommandsAllowed && _startedUtc is null);
    }
}

/// <summary>
/// EN: Numeric setpoint with minus / value / plus, so a value can be nudged without a keyboard.
///     The entry is range-checked against the declared limits before anything is sent, so a
///     mistyped digit is refused here rather than clamped somewhere in the PLC.
/// TR: Eksi / değer / artı üçlüsüyle sayısal set değeri; klavyesiz de değiştirilebilir.
///     Giriş, bir şey gönderilmeden önce bildirilen sınırlara karşı denetlenir; yanlış yazılmış
///     bir hane PLC'de kırpılmak yerine burada reddedilir.
/// </summary>
public class SetpointControl : ManualControl
{
    private readonly TextBox _input;
    private readonly Button _minus;
    private readonly Button _plus;
    private readonly TextBlock _state;
    private readonly string _dataType;

    public SetpointControl(ControlDefinition d, ManualPageRunner r, SymbolMapper symbols) : base(d, r)
    {
        _dataType = symbols.GetSymbolInfo(d.Symbol)?.DataType ?? string.Empty;

        var stepper = HmiStyle.Stepper(out _minus, out _plus, out _input, HmiStyle.Metrics.ControlWidth);
        _minus.Click += (_, _) => Nudge(-Definition.Step);
        _plus.Click  += (_, _) => Nudge(+Definition.Step);
        _input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Send(); };
        _input.LostFocus += (_, _) => Send();

        var caption = string.IsNullOrWhiteSpace(d.Unit) ? d.Label : $"{d.Label} ({d.Unit})";
        Element = Stack(caption, stepper, out _state, HmiStyle.Metrics.ControlWidth);
        StateBlock = _state;
    }

    /// <summary>
    /// EN: Moves the entry by one step and sends it, clamped to the declared range.
    /// TR: Girişi bir adım kaydırıp gönderir; bildirilen aralığa kırpılır.
    /// </summary>
    private void Nudge(double delta)
    {
        if (!CommandsAllowed) return;

        var current = ParseInput() ?? Definition.Min ?? 0;
        var moved = Math.Clamp(current + delta, Definition.Min ?? double.MinValue, Definition.Max ?? double.MaxValue);
        _input.Text = moved.ToString("F" + Math.Max(0, Definition.Decimals), CultureInfo.CurrentCulture);
        Send();
    }

    private double? ParseInput() =>
        double.TryParse(_input.Text.Trim().Replace(',', '.'), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var v) ? v : null;

    private async void Send()
    {
        var L = LocalizationManager.Instance;
        if (!CommandsAllowed) return;

        if (ParseInput() is not { } value)
        {
            _state.Text = L.T("Manual_SetpointNotNumeric");
            return;
        }

        if (value < Definition.Min || value > Definition.Max)
        {
            _state.Text = L.T("Manual_SetpointOutOfRange", Definition.Min!.Value, Definition.Max!.Value);
            return;
        }

        if (!ConfirmIfRequired()) return;

        // Operatör ölçeklenmiş birimde girer (ör. 20.0 Hz), PLC ham değeri saklar (ör. 200).
        // Tam sayı tiplerinde yuvarlama şart: 20.0 / 0.1 kayan noktada 199.999... verir.
        var raw = Definition.Scale != 0 ? value / Definition.Scale : value;

        object toWrite = _dataType.ToUpperInvariant() switch
        {
            "REAL"             => (float)raw,
            "LREAL"            => raw,
            "BYTE" or "USINT"  => (byte)Math.Round(raw),
            "SINT"             => (sbyte)Math.Round(raw),
            "WORD" or "UINT"   => (ushort)Math.Round(raw),
            "INT"              => (short)Math.Round(raw),
            "DWORD" or "UDINT" => (uint)Math.Round(raw),
            "DINT"             => (int)Math.Round(raw),
            _                  => raw
        };

        if (await Runner.WriteCommandAsync(Definition.Symbol, toWrite))
            _state.Text = string.Empty;
    }

    protected override void OnUpdate(ManualPageSnapshot s)
    {
        _minus.IsEnabled = CommandsAllowed;
        _plus.IsEnabled  = CommandsAllowed;
        _input.IsEnabled = CommandsAllowed;

        // Kullanıcı yazarken üzerine yazma; yalnızca odak dışındayken PLC'yi yansıt.
        if (!_input.IsFocused)
        {
            var source = Definition.Feedback ?? Definition.Symbol;
            if (s.Values.TryGetValue(source, out var v) && v is not null)
                _input.Text = NumericControl.FormatScaled(v, Definition);
        }
    }
}
