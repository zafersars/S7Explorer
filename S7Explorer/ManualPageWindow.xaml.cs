using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using S7Explorer.ManualPages;

namespace S7Explorer;

/// <summary>
/// EN: One row of the live value grid.
/// TR: Canlı değer tablosunun bir satırı.
/// </summary>
public class ManualValueRow
{
    /// <summary>EN: What the symbol is used for on the page. TR: Sembolün sayfada ne için kullanıldığı.</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>EN: Operator-facing label. TR: Operatöre gösterilen etiket.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>EN: Symbolic name. TR: Sembolik ad.</summary>
    public string Symbol { get; set; } = string.Empty;
    /// <summary>EN: Resolved physical address. TR: Çözümlenen fiziksel adres.</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>EN: PLC data type. TR: PLC veri tipi.</summary>
    public string DataType { get; set; } = string.Empty;
    /// <summary>EN: Latest value read. TR: Okunan son değer.</summary>
    public string Value { get; set; } = "-";
    /// <summary>EN: Read state or error text. TR: Okuma durumu veya hata metni.</summary>
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// EN: One alarm event: when it arrived, what it was, and whether it is still standing.
///
///     Raises change notifications because a row is mutated in place when its alarm clears.
///     Without them the list keeps showing a cleared alarm as if it were still active, which on a
///     diagnostic screen is worse than showing nothing at all.
/// TR: Tek bir alarm olayı: ne zaman geldiği, ne olduğu ve hâlâ duruyor olup olmadığı.
///
///     Alarm düştüğünde satır yerinde değiştirildiği için değişiklik bildirimi yapar. Bildirim
///     olmadan liste, düşmüş bir alarmı hâlâ aktifmiş gibi göstermeye devam eder; teşhis ekranında
///     bu, hiçbir şey göstermemekten daha kötüdür.
/// </summary>
public class AlarmRow : INotifyPropertyChanged
{
    private Brush _accent = new SolidColorBrush(HmiStyle.Red);
    private Brush _messageBrush = HmiStyle.TextHigh;
    private TextDecorationCollection? _messageDecorations;
    private string _clearedTime = string.Empty;

    /// <summary>EN: Time the alarm appeared. TR: Alarmın belirdiği saat.</summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>EN: Operator-facing message. TR: Operatöre gösterilen mesaj.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>EN: True while the alarm bit is still set. TR: Alarm biti hâlâ set ise true.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>EN: Icon colour: red while active, grey once cleared. TR: Simge rengi: aktifken kırmızı, düştüğünde gri.</summary>
    public Brush Accent { get => _accent; private set => Set(ref _accent, value); }

    /// <summary>EN: Message colour, dimmed once the alarm has cleared. TR: Mesaj rengi; alarm düştüğünde soluklaşır.</summary>
    public Brush MessageBrush { get => _messageBrush; private set => Set(ref _messageBrush, value); }

    /// <summary>EN: Strikethrough applied to a cleared alarm. TR: Düşmüş alarmın üstü çizilir.</summary>
    public TextDecorationCollection? MessageDecorations
    {
        get => _messageDecorations;
        private set => Set(ref _messageDecorations, value);
    }

    /// <summary>EN: Time the alarm cleared, empty while it is still standing. TR: Alarmın düştüğü saat; hâlâ duruyorsa boş.</summary>
    public string ClearedTime { get => _clearedTime; private set => Set(ref _clearedTime, value); }

    /// <summary>
    /// EN: Marks the alarm as gone: the row stays in the list as history but stops claiming to be
    ///     an active fault.
    /// TR: Alarmı düşmüş olarak işaretler: satır geçmiş olarak listede kalır ama artık aktif bir
    ///     arıza olduğunu iddia etmez.
    /// </summary>
    public void MarkCleared(DateTime when)
    {
        IsActive = false;
        Accent = new SolidColorBrush(HmiStyle.OffGrey);
        MessageBrush = HmiStyle.TextLow;
        MessageDecorations = TextDecorations.Strikethrough;
        ClearedTime = when.ToString("HH:mm:ss");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>
/// EN: A validation finding shaped for display.
/// TR: Gösterim için biçimlendirilmiş doğrulama bulgusu.
/// </summary>
public class ValidationRow
{
    public string SeverityText { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Brush SeverityBrush { get; set; } = Brushes.Gray;
}

/// <summary>
/// EN: Hosts a manual test page: lists the definitions found in the pages folder, shows what
///     validation made of the selected one, renders its operator panel and cyclically refreshes it.
///
///     Writing starts disabled and has to be armed deliberately; even then commands only reach the
///     PLC while the machine confirms manual mode, and only for symbols validation approved.
///     Stopping, disarming or closing clears the command bits first.
/// TR: Manuel test sayfasını barındırır: sayfalar klasöründe bulunan tanımları listeler, seçilenin
///     doğrulama sonucunu gösterir, operatör panelini çizer ve döngüsel olarak tazeler.
///
///     Yazma kapalı başlar ve bilinçli olarak etkinleştirilmelidir; etkinken bile komutlar yalnızca
///     makine manuel modu teyit ettiği sürece ve yalnızca doğrulamanın onayladığı sembollere gider.
///     Durdurma, yazmayı kapatma veya kapanış önce komut bitlerini temizler.
/// </summary>
public partial class ManualPageWindow : Window
{
    private static LocalizationManager L => LocalizationManager.Instance;

    private readonly PlcService _plcService;
    private readonly ManualPageLoader _loader = new();
    private readonly ObservableCollection<ManualValueRow> _rows = new();
    private readonly ObservableCollection<ValidationRow> _validationRows = new();
    private readonly Dictionary<string, ManualValueRow> _rowsBySymbol = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EN: Standard equipment card width; four fit across the screen. TR: Standart ekipman kartı genişliği; ekrana dördü sığar.</summary>
    private const double CardWidth = 300;

    /// <summary>EN: Alarm panel width, sized to sit beside the wide equipment card. TR: Alarm paneli genişliği; geniş ekipman kartının yanına sığacak ölçüde.</summary>
    private const double AlarmCardWidth = 330;

    /// <summary>EN: How many alarm events are kept. TR: Kaç alarm olayının saklanacağı.</summary>
    private const int AlarmHistoryLimit = 60;

    private readonly List<ManualControl> _controls = new();

    /// <summary>EN: Operator-facing name per alarm symbol. TR: Alarm sembolü başına operatöre gösterilen ad.</summary>
    private readonly Dictionary<string, string> _alarmNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EN: Previous alarm bit states, so only transitions are recorded. TR: Önceki alarm bit durumları; yalnızca geçişler kaydedilsin.</summary>
    private readonly Dictionary<string, bool> _alarmPrevious = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EN: Alarm events, newest first. TR: Alarm olayları, en yenisi başta.</summary>
    private readonly ObservableCollection<AlarmRow> _alarmRows = new();

    private ManualPageRunner? _runner;
    private ManualPageLoadResult? _selected;

    /// <summary>
    /// EN: Raised for every message that belongs in the main window's log.
    /// TR: Ana penceredeki loga girmesi gereken her mesaj için tetiklenir.
    /// </summary>
    public event EventHandler<string>? LogMessage;

    /// <summary>
    /// EN: Creates the window for a PLC service. The service does not need to be connected yet;
    ///     monitoring simply cannot start until it is.
    /// TR: Pencereyi bir PLC servisi için oluşturur. Servisin henüz bağlı olması gerekmez;
    ///     yalnızca bağlanmadan izleme başlatılamaz.
    /// </summary>
    public ManualPageWindow(PlcService plcService)
    {
        InitializeComponent();
        _plcService = plcService;

        GridValues.ItemsSource = _rows;
        LstValidation.ItemsSource = _validationRows;

        // Panolarda saat daima görünür; izleme durmuşken de akmaya devam etmeli.
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => ShowClock();
        clock.Start();
        ShowClock();

        ApplyLanguage();
        L.LanguageChanged += (_, _) => ApplyLanguage();

        LoadPages();
    }

    /// <summary>EN: Shows the wall clock, as panels always do. TR: Panolarda daima bulunan duvar saatini gösterir.</summary>
    private void ShowClock()
    {
        LblDate.Text  = DateTime.Now.ToString("dd.MM.yyyy");
        LblClock.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    /// <summary>
    /// EN: Applies the current language to every static caption.
    /// TR: Geçerli dili tüm sabit metinlere uygular.
    /// </summary>
    private void ApplyLanguage()
    {
        Title                     = L.T("Manual_Title");
        LblPageSelect.Text        = L.T("Manual_Page");
        TxtBtnReload.Text         = L.T("Manual_Reload");
        LblReadOnlyBanner.Text    = L.T("Manual_ReadOnlyBanner");
        TxtValidationHeader.Text  = L.T("Manual_Validation");
        LblSystemCaption.Text     = L.T("Manual_SystemCaption");
        LblCycleCaption.Text      = L.T("Manual_CycleCaption");

        ColRole.Header    = L.T("Manual_ColRole");
        ColLabel.Header   = L.T("Manual_ColLabel");
        ColSymbol.Header  = L.T("Manual_ColSymbol");
        ColAddress.Header = L.T("Manual_ColAddress");
        ColType.Header    = L.T("Manual_ColType");
        ColValue.Header   = L.T("Manual_ColValue");
        ColState.Header   = L.T("Manual_ColState");

        TabControls.Header       = L.T("Manual_TabControls");
        TabValues.Header         = L.T("Manual_TabValues");
        ChkArmWrite.Content      = L.T("Manual_ArmWrite");
        LblManualModeWarning.Text = L.T("Manual_ManualModeWarning");

        UpdateArmBanner(_runner?.IsWriteEnabled == true);
        UpdateStartStopCaption();
    }

    /// <summary>
    /// EN: Scans the pages folder and fills the selector. A page that failed validation is still
    ///     listed, marked, so the engineer can open it and read why.
    /// TR: Sayfalar klasörünü tarar ve seçiciyi doldurur. Doğrulamayı geçemeyen sayfa da işaretli
    ///     olarak listelenir; mühendis açıp nedenini okuyabilsin.
    /// </summary>
    private void LoadPages()
    {
        StopRunner();

        var results = _loader.LoadAll(_plcService.SymbolMapper);
        CmbPages.Items.Clear();

        foreach (var result in results)
        {
            var name = result.Page?.PageName ?? result.FileName;
            var caption = result.CanLoad ? name : $"{name}  ⚠ {L.T("Manual_Rejected")}";
            CmbPages.Items.Add(new ComboBoxItem { Content = caption, Tag = result });
        }

        if (CmbPages.Items.Count == 0)
        {
            Log(L.T("Manual_NoPagesFound", ManualPageLoader.PagesFolder));
            ShowNoPagesState();
            return;
        }

        CmbPages.SelectedIndex = 0;
    }

    /// <summary>
    /// EN: Clears the view and disables monitoring when there is nothing to show.
    /// TR: Gösterilecek bir şey olmadığında görünümü temizler ve izlemeyi devre dışı bırakır.
    /// </summary>
    private void ShowNoPagesState()
    {
        _selected = null;
        _rows.Clear();
        _rowsBySymbol.Clear();
        _validationRows.Clear();
        BtnStartStop.IsEnabled = false;
        TxtValidationHeader.Text = L.T("Manual_NoPages");
    }

    private void CmbPages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbPages.SelectedItem is ComboBoxItem { Tag: ManualPageLoadResult result })
            SelectPage(result);
    }

    /// <summary>
    /// EN: Shows the validation outcome for a page and, when it is loadable, builds its value rows.
    /// TR: Bir sayfanın doğrulama sonucunu gösterir; yüklenebilirse değer satırlarını kurar.
    /// </summary>
    private void SelectPage(ManualPageLoadResult result)
    {
        StopRunner();
        _selected = result;
        LblScreenTitle.Text = result.Page?.PageName?.ToUpperInvariant() ?? L.T("Manual_Title");

        FillValidation(result);
        BuildRows(result);

        BtnStartStop.IsEnabled = result.CanLoad;
        UpdateStartStopCaption();
        ResetStatusIndicators();

        if (!result.CanLoad)
        {
            Log(L.T("Manual_PageRejected", result.FileName, result.Validation.Errors.Count()));
            ExpValidation.IsExpanded = true;
        }
    }

    /// <summary>
    /// EN: Fills the validation list and reflects the worst severity in the expander header.
    /// TR: Doğrulama listesini doldurur ve en kötü ciddiyeti expander başlığına yansıtır.
    /// </summary>
    private void FillValidation(ManualPageLoadResult result)
    {
        _validationRows.Clear();
        foreach (var issue in result.Validation.Issues)
        {
            _validationRows.Add(new ValidationRow
            {
                SeverityText  = issue.Severity == ValidationSeverity.Error ? L.T("Manual_Error") : L.T("Manual_Warning"),
                SeverityBrush = issue.Severity == ValidationSeverity.Error ? Brushes.OrangeRed : Brushes.Goldenrod,
                Path          = issue.Path,
                Message       = issue.Message
            });
        }

        var errorCount   = result.Validation.Errors.Count();
        var warningCount = result.Validation.Warnings.Count();

        if (errorCount > 0)
        {
            IconValidation.Text = "";
            TxtValidationHeader.Text = L.T("Manual_ValidationErrors", errorCount, warningCount);
        }
        else if (warningCount > 0)
        {
            IconValidation.Text = "";
            TxtValidationHeader.Text = L.T("Manual_ValidationWarnings", warningCount);
        }
        else
        {
            IconValidation.Text = "";
            TxtValidationHeader.Text = L.T("Manual_ValidationClean");
        }
    }

    /// <summary>
    /// EN: Builds one grid row per symbol the page touches. Command symbols are listed too, marked
    ///     as such, so the engineer can see exactly what the page would write once writing is enabled.
    /// TR: Sayfanın dokunduğu her sembol için bir satır kurar. Komut sembolleri de işaretli olarak
    ///     listelenir; böylece mühendis, yazma açıldığında sayfanın tam olarak neye yazacağını görür.
    /// </summary>
    private void BuildRows(ManualPageLoadResult result)
    {
        _rows.Clear();
        _rowsBySymbol.Clear();

        if (result.Page is null) return;
        var symbols = _plcService.SymbolMapper;

        void Add(string role, string label, string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol) || _rowsBySymbol.ContainsKey(symbol)) return;

            var info = symbols.GetSymbolInfo(symbol);
            var row = new ManualValueRow
            {
                Role     = role,
                Label    = string.IsNullOrWhiteSpace(label) ? (info?.Description ?? string.Empty) : label,
                Symbol   = symbol,
                Address  = info?.PhysicalAddress ?? "?",
                DataType = info?.DataType ?? "?"
            };
            _rows.Add(row);
            _rowsBySymbol[symbol] = row;
        }

        var m = result.Page.Machine;
        // LifeOut da okunur: el sıkışmada PLC'nin onu sıfırlamasını görmemiz gerekiyor.
        Add(L.T("Manual_RoleLife"),   L.T("Manual_PlcLife"),    m.LifeOut ?? string.Empty);
        Add(L.T("Manual_RoleLife"),   L.T("Manual_PlcLife"),    m.LifeIn ?? string.Empty);
        Add(L.T("Manual_RoleManual"), L.T("Manual_ManualMode"), m.ManualState ?? string.Empty);

        foreach (var c in result.Page.Controls)
        {
            var isCommand = !c.Type.Equals(nameof(ControlKind.Lamp), StringComparison.OrdinalIgnoreCase) &&
                            !c.Type.Equals(nameof(ControlKind.Numeric), StringComparison.OrdinalIgnoreCase);

            Add(isCommand ? L.T("Manual_RoleCommand") : L.T("Manual_RoleDisplay"), c.Label, c.Symbol);
            Add(L.T("Manual_RoleFeedback"), c.Label, c.Feedback ?? string.Empty);
            Add(L.T("Manual_RoleBusy"),     c.Label, c.Busy ?? string.Empty);
            Add(L.T("Manual_RoleDone"),     c.Label, c.Done ?? string.Empty);
        }

        if (result.Page.AlarmGroup is { } alarms && !string.IsNullOrWhiteSpace(alarms.Prefix))
        {
            Add(L.T("Manual_RoleAlarmSummary"), L.T("Manual_AlarmSummary"), alarms.Summary ?? string.Empty);

            foreach (var symbol in result.Validation.ReadSymbols
                         .Where(s => s.StartsWith(alarms.Prefix, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Add(L.T("Manual_RoleAlarm"), string.Empty, symbol);
            }
        }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e) => LoadPages();

    private void BtnStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_runner is { IsRunning: true })
        {
            StopRunner();
            Log(L.T("Manual_MonitoringStopped"));
            return;
        }

        if (_selected?.Page is null || !_selected.CanLoad) return;

        if (!_plcService.IsConnected)
        {
            MessageDialog.Show(L.T("Manual_NotConnected"), L.T("MsgTitle_Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        // Yalnızca tabloda gerçekten satırı olan semboller okunur; doğrulamanın topladığı liste
        // alarm önekindeki her şeyi içerir, ekranda karşılığı olmayanı okumanın anlamı yok.
        // Yazılabilir liste doğrulayıcının kararından gelir: runner bunun dışına yazmayı reddeder.
        _runner = new ManualPageRunner(_plcService, _selected.Page,
            _rowsBySymbol.Keys, _selected.Validation.WrittenSymbols);
        _runner.Updated += OnRunnerUpdated;
        _runner.Notice  += (_, message) => Log(message);

        BuildControlPanel(_selected);
        _runner.Start();
        ChkArmWrite.IsEnabled = true;

        Log(L.T("Manual_MonitoringStarted", _selected.Page.PageName,
            _selected.Page.Machine.PollIntervalMs, _rowsBySymbol.Count));
        UpdateStartStopCaption();
    }

    /// <summary>
    /// EN: Builds the operator control panel and the alarm strip from the page definition.
    /// TR: Operatör kumanda panelini ve alarm şeridini sayfa tanımından kurar.
    /// </summary>
    private void BuildControlPanel(ManualPageLoadResult result)
    {
        _controls.Clear();
        _alarmNames.Clear();
        _alarmPrevious.Clear();
        _alarmRows.Clear();
        PanelControls.Children.Clear();

        if (result.Page is null || _runner is null) return;

        var manualCmd = result.Page.Machine.ManualCmd;

        string? currentGroup = null;
        WrapPanel? currentBody = null;
        var parents = new Dictionary<string, WrapPanel>(StringComparer.Ordinal);

        foreach (var definition in result.Page.Controls)
        {
            var control = ManualControlFactory.Create(definition, _runner, _plcService.SymbolMapper);
            if (control is null) continue;

            control.IsManualModeControl =
                !string.IsNullOrWhiteSpace(manualCmd) &&
                string.Equals(definition.Symbol, manualCmd, StringComparison.OrdinalIgnoreCase);

            // Her grup kendi kartına alınır; kart yalnızca grup adı değiştiğinde açılır, böylece
            // tanım sırası korunur. Ad "Üst / Alt" biçimindeyse alt kart, üst kartın içine girer —
            // beş motorun tek bir "AC Motor Kontrolleri" kartında toplanması böyle sağlanır.
            var group = definition.Group;
            if (currentBody is null || !string.Equals(group, currentGroup, StringComparison.Ordinal))
            {
                currentGroup = group;
                currentBody = new WrapPanel();
                AddCard(group, currentBody, parents);
            }

            _controls.Add(control);
            currentBody.Children.Add(control.Element);
        }

        BuildAlarmPanel(result);
    }

    /// <summary>
    /// EN: Places a card, nesting it inside its parent when the group name carries a "Parent / Child"
    ///     path. Parents size to their children; standalone cards take the standard card width.
    /// TR: Kartı yerleştirir; grup adı "Üst / Alt" yolu taşıyorsa üst kartın içine yerleştirir.
    ///     Üst kartlar çocuklarına göre boyutlanır; tek başına kartlar standart genişliği alır.
    /// </summary>
    private void AddCard(string? group, WrapPanel body, Dictionary<string, WrapPanel> parents)
    {
        var separator = group?.IndexOf('/') ?? -1;

        if (group is null || separator < 0)
        {
            PanelControls.Children.Add(HmiStyle.Card(group, body, CardWidth));
            return;
        }

        var parentName = group[..separator].Trim();
        var childName  = group[(separator + 1)..].Trim();

        if (!parents.TryGetValue(parentName, out var parentBody))
        {
            parentBody = new WrapPanel();
            parents[parentName] = parentBody;
            PanelControls.Children.Add(HmiStyle.Card(parentName, parentBody, tightTitle: true));
        }

        parentBody.Children.Add(HmiStyle.Card(childName, body, tightTitle: false));
    }

    /// <summary>
    /// EN: Builds the alarm panel: a red-headed card holding the most recent alarm events with the
    ///     time they arrived. A live lamp grid tells an operator what is wrong now; a timestamped
    ///     list also tells them what happened while they were looking elsewhere.
    /// TR: Alarm panelini kurar: en son alarm olaylarını geliş saatleriyle tutan, kırmızı başlıklı
    ///     bir kart. Canlı lamba tablosu operatöre şu an neyin bozuk olduğunu söyler; zaman damgalı
    ///     liste ise başka yere bakarken ne olduğunu da söyler.
    /// </summary>
    private void BuildAlarmPanel(ManualPageLoadResult result)
    {
        var prefix = result.Page?.AlarmGroup?.Prefix;
        if (string.IsNullOrWhiteSpace(prefix)) return;

        foreach (var symbol in result.Validation.ReadSymbols
                     .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var info = _plcService.SymbolMapper.GetSymbolInfo(symbol);
            _alarmNames[symbol] = string.IsNullOrWhiteSpace(info?.Description)
                ? symbol[(symbol.LastIndexOf('.') + 1)..]
                : info.Description;
        }

        var list = new ItemsControl { ItemsSource = _alarmRows, Margin = new Thickness(0, 0, 0, 0) };
        list.ItemTemplate = (DataTemplate)Resources["AlarmRowTemplate"];

        var body = new StackPanel { Width = AlarmCardWidth - 34 };
        body.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 300,
            Content = list
        });

        PanelControls.Children.Add(
            HmiStyle.AlarmCard(L.T("Manual_ActiveAlarms"), body, AlarmCardWidth, BuildAlarmAckButton()));
    }

    /// <summary>
    /// EN: The alarm strip's acknowledge button. It removes the alarms that have already cleared and
    ///     leaves the standing ones alone, so acknowledging can never hide a fault that is still
    ///     there — the list is tidied, not silenced.
    /// TR: Alarm şeridinin onay butonu. Yalnızca düşmüş alarmları listeden kaldırır, duran alarmlara
    ///     dokunmaz; böylece onaylamak hâlâ var olan bir arızayı asla gizleyemez — liste susturulmaz,
    ///     yalnızca derlenir.
    /// </summary>
    private Button BuildAlarmAckButton()
    {
        var button = new Button
        {
            Content = L.T("Manual_AlarmAck"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 3, 10, 3),
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = L.T("Manual_AlarmAckTip")
        };

        button.Click += (_, _) =>
        {
            var removed = 0;
            for (var i = _alarmRows.Count - 1; i >= 0; i--)
            {
                if (_alarmRows[i].IsActive) continue;
                _alarmRows.RemoveAt(i);
                removed++;
            }

            var standing = _alarmRows.Count;
            Log(standing > 0
                ? L.T("Manual_AlarmAcked", removed, standing)
                : L.T("Manual_AlarmAckedAll", removed));
        };

        return button;
    }
    /// <summary>
    /// EN: Stops and detaches the current runner, clearing any command it was holding first.
    /// TR: Geçerli koşucuyu durdurur ve bağlantısını keser; önce tuttuğu komutları temizler.
    /// </summary>
    private async void StopRunner()
    {
        if (_runner is null) return;

        var runner = _runner;
        _runner = null;
        runner.Updated -= OnRunnerUpdated;

        // Kontroller basılı tuttukları biti bıraksın, sonra koşucu komutları sıfırlayıp dursun.
        foreach (var control in _controls)
        {
            try { await control.ReleaseAsync(); }
            catch (Exception ex) { Log($"Kontrol serbest bırakılamadı: {ex.Message}"); }
        }

        try { await runner.ShutdownAsync(); }
        catch (Exception ex) { Log($"Kapatma sırasında hata: {ex.Message}"); }

        ChkArmWrite.IsChecked = false;
        ChkArmWrite.IsEnabled = false;
        UpdateArmBanner(false);
        ResetStatusIndicators();
        UpdateStartStopCaption();
    }

    /// <summary>
    /// EN: Arms or disarms command writing. Disarming clears the command bits before returning.
    /// TR: Komut yazmayı açar veya kapatır. Kapatma, dönmeden önce komut bitlerini temizler.
    /// </summary>
    private async void ChkArmWrite_Changed(object sender, RoutedEventArgs e)
    {
        if (_runner is null) return;

        var wanted = ChkArmWrite.IsChecked == true;

        if (wanted && MessageDialog.Show(L.T("Manual_ArmConfirm"), L.T("MsgTitle_Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, this) != MessageBoxResult.Yes)
        {
            ChkArmWrite.IsChecked = false;
            return;
        }

        foreach (var control in _controls)
        {
            if (!wanted) await control.ReleaseAsync();
        }

        await _runner.SetWriteEnabledAsync(wanted);
        UpdateArmBanner(wanted);
    }

    /// <summary>
    /// EN: Reflects the arm state in the banner so the current mode is never ambiguous.
    /// TR: Yazma durumunu banda yansıtır; geçerli kip asla belirsiz kalmasın.
    /// </summary>
    private void UpdateArmBanner(bool armed)
    {
        ArmBanner.Background  = new SolidColorBrush(armed ? Color.FromArgb(0x33, 0xFF, 0x45, 0x00)
                                                          : Color.FromArgb(0x33, 0xFF, 0xA5, 0x00));
        ArmBanner.BorderBrush = new SolidColorBrush(armed ? Color.FromRgb(0xFF, 0x45, 0x00)
                                                          : Color.FromRgb(0xFF, 0xA5, 0x00));
        IconArm.Text = armed ? "" : "";
        LblReadOnlyBanner.Text = armed ? L.T("Manual_WriteArmedBanner") : L.T("Manual_ReadOnlyBanner");
    }

    /// <summary>
    /// EN: Writes one cycle's values into the grid and updates the health indicators.
    ///     Runs on the UI thread because the runner's timer ticks there.
    /// TR: Bir turun değerlerini tabloya yazar ve sağlık göstergelerini güncelleyir.
    ///     Koşucunun zamanlayıcısı UI thread'inde tick attığı için burada da UI thread'indeyiz.
    /// </summary>
    private void OnRunnerUpdated(object? sender, ManualPageSnapshot snapshot)
    {
        foreach (var row in _rows)
        {
            if (snapshot.Errors.TryGetValue(row.Symbol, out var error))
            {
                row.State = snapshot.PausedSymbols.Contains(row.Symbol)
                    ? L.T("Manual_StatePaused")
                    : error;
            }
            else if (snapshot.PausedSymbols.Contains(row.Symbol))
            {
                row.State = L.T("Manual_StatePaused");
            }
            else if (snapshot.Values.TryGetValue(row.Symbol, out var value))
            {
                row.Value = value?.ToString() ?? "null";
                row.State = L.T("Manual_StateOk");
            }
        }

        // ManualValueRow degisiklik bildirimi yapmadigi icin tabloyu tazelemek gerekiyor;
        // satir sayisi kucuk oldugu icin bu, her satiri INotifyPropertyChanged yapmaktan sade.
        GridValues.Items.Refresh();

        LblCycle.Text   = snapshot.CycleMs + " ms";
        LblOverrun.Text = snapshot.OverrunCount > 0 ? L.T("Manual_Overruns", snapshot.OverrunCount) : string.Empty;

        UpdateControls(snapshot);
    }

    /// <summary>
    /// EN: Refreshes the operator controls and alarm lamps.
    ///
    ///     Commands are permitted only when writing is armed AND the machine reports manual mode.
    ///     When the page declares no manual-mode symbol there is nothing to confirm against, so
    ///     the arm switch alone decides — the validator warns about that case at load time.
    /// TR: Operatör kontrollerini ve alarm lambalarını tazeler.
    ///
    ///     Komutlara yalnızca yazma etkinken VE makine manuel modu bildirdiğinde izin verilir.
    ///     Sayfa manuel mod sembolü bildirmiyorsa karşılaştırılacak bir teyit yoktur; o zaman
    ///     kararı tek başına yazma anahtarı verir — doğrulayıcı bu durumu yüklemede uyarır.
    /// </summary>
    private void UpdateControls(ManualPageSnapshot snapshot)
    {
        var armed = _runner?.IsWriteEnabled == true;
        var manualOk = snapshot.ManualModeActive ?? true;
        var commandsAllowed = armed && manualOk;

        ManualModeWarning.Visibility =
            armed && snapshot.ManualModeActive == false ? Visibility.Visible : Visibility.Collapsed;

        // Bir buton pasifse sebebini döşemede göster; "bastım, bir şey olmadı" sorusunu
        // ekranın kendisi cevaplasın.
        var lockReason = !armed
            ? L.T("Manual_LockedNotArmed")
            : snapshot.ManualModeActive == false ? L.T("Manual_LockedNotManual") : null;

        foreach (var control in _controls)
        {
            // Bazı komutlar manuel mod kapısından muaftır: manuel modu talep eden kontrolün kendisi
            // (yoksa kapı, kendisini açabilecek tek anahtarı da kilitler) ve sayfanın açıkça
            // requiresManualMode: false bildirdiği komutlar — acilden çıkış ve ilk açılış
            // senaryolarında reset, makine manueli kabul etmeden önce çalışmak zorundadır.
            // Yazma yine açık olmak zorunda.
            var bypasses = control.BypassesManualModeGate;
            var allowed = bypasses ? armed : commandsAllowed;
            control.Update(snapshot, allowed,
                bypasses ? (armed ? null : L.T("Manual_LockedNotArmed")) : lockReason);
        }

        UpdateAlarms(snapshot);
        UpdateSystemStatus(snapshot, armed);
    }

    /// <summary>
    /// EN: Puts the health indicators back to "unknown".
    /// TR: Sağlık göstergelerini "bilinmiyor" durumuna alır.
    /// </summary>
    private void ResetStatusIndicators()
    {
        DotSystem.Fill      = new SolidColorBrush(HmiStyle.OffGrey);
        LblSystemState.Text = L.T("Manual_SystemIdle");
        LblCycle.Text       = "—";
        LblOverrun.Text     = string.Empty;
    }

    /// <summary>
    /// EN: Records alarm transitions. Only the moment a bit goes true is logged, so a standing
    ///     alarm produces one line rather than one per cycle; when it clears, the line greys out.
    /// TR: Alarm geçişlerini kaydeder. Yalnızca bir bitin true olduğu an loglanır; böylece duran
    ///     bir alarm turda bir değil, bir kez satır üretir. Alarm düştüğünde satır griye döner.
    /// </summary>
    private void UpdateAlarms(ManualPageSnapshot snapshot)
    {
        foreach (var (symbol, name) in _alarmNames)
        {
            if (!snapshot.Values.TryGetValue(symbol, out var v) || v is not bool active) continue;

            _alarmPrevious.TryGetValue(symbol, out var previous);
            _alarmPrevious[symbol] = active;

            if (active && !previous)
            {
                _alarmRows.Insert(0, new AlarmRow
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Message = name
                });

                while (_alarmRows.Count > AlarmHistoryLimit)
                    _alarmRows.RemoveAt(_alarmRows.Count - 1);

                Log(L.T("Manual_AlarmRaised", name));
            }
            else if (!active && previous)
            {
                // Düşen alarm silinmez, işaretlenir: az önce ne olduğunu görmek teşhis için değerli.
                // Satırın kendisi bildirim yaptığı için listeye dokunmak gerekmez.
                _alarmRows.FirstOrDefault(r => r.IsActive && r.Message == name)?.MarkCleared(DateTime.Now);
                Log(L.T("Manual_AlarmCleared", name));
            }
        }
    }

    /// <summary>
    /// EN: Summarises the whole screen in the footer, worst state first: no link, then PLC silence,
    ///     then a standing alarm, then not-in-manual, otherwise normal.
    /// TR: Tüm ekranı alt çubukta özetler, en kötü durum önce: bağlantı yok, PLC sessiz, duran
    ///     alarm, manuel değil; hiçbiri yoksa normal.
    /// </summary>
    private void UpdateSystemStatus(ManualPageSnapshot snapshot, bool armed)
    {
        var anyAlarm = _alarmPrevious.Any(p => p.Value);

        var (caption, color) =
            !_plcService.IsConnected      ? (L.T("Manual_SystemNoLink"),  HmiStyle.Red)
            : snapshot.LifeOk == false    ? (L.T("Manual_SystemNoLife"),  HmiStyle.Red)
            : anyAlarm                    ? (L.T("Manual_SystemAlarm"),   HmiStyle.Red)
            : snapshot.ManualModeActive == false && armed
                                          ? (L.T("Manual_SystemNotManual"), HmiStyle.Amber)
            : (L.T("Manual_SystemNormal"), HmiStyle.Green);

        LblSystemState.Text = caption;
        LblSystemState.Foreground = new SolidColorBrush(HmiStyle.Darken(color, 0.15));
        DotSystem.Fill = new SolidColorBrush(color);
    }

    /// <summary>
    /// EN: Keeps the start/stop button caption and glyph in sync with the runner state.
    /// TR: Başlat/durdur butonunun metnini ve ikonunu koşucu durumuyla eşler.
    /// </summary>
    private void UpdateStartStopCaption()
    {
        var running = _runner is { IsRunning: true };
        TxtBtnStartStop.Text = running ? L.T("Manual_StopMonitoring") : L.T("Manual_StartMonitoring");
        BtnStartStop.Background = running
            ? new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C))
            : new SolidColorBrush(Color.FromRgb(0x00, 0x6E, 0x76));
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => StopRunner();

    private void Log(string message) => LogMessage?.Invoke(this, message);
}
