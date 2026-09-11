using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using S7.Net;
using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace S7Explorer;

/// <summary>
/// EN: Main application window. Manages PLC connection, read/write operations,
///     symbol tree, theme and language selection.
/// TR: Ana uygulama penceresi. PLC bağlantısı, okuma/yazma işlemleri,
///     sembol ağacı, tema ve dil seçimini yönetir.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PlcService _plcService;
    private CancellationTokenSource? _connectionCancellationTokenSource;
    private ObservableCollection<SymbolTreeNode> _symbolTreeNodes = new();
    private Dictionary<string, SymbolInfo> _allSymbols = new();
    private bool _isLeftPanelExpanded = true;
    private const double LeftPanelExpandedWidth = 250;
    private const double LeftPanelCollapsedWidth = 36;

    // Mandalı koddan konumlandırırken Checked/Unchecked yazma tetiklemesin.
    private bool _suppressBoolToggleWrite;

    // Sembol tablosunda olmayan, elle yazılmış bit adresleri: DB1.DBX0.0 / M0.0 / I0.0 / Q0.0
    private static readonly Regex BoolPhysicalAddressRegex = new(
        @"^(DB\d+\.DBX\d+\.[0-7]|[IQM]\d+\.[0-7])$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static LocalizationManager L => LocalizationManager.Instance;
    private EventHandler? _languageChangedHandler;
    private bool _hasReadValue = false;

    /// <summary>
    /// EN: The open manual test window, if any. Kept so a second click focuses the existing window
    ///     instead of starting a second cyclic reader against the same PLC connection.
    /// TR: Açıksa manuel test penceresi. İkinci tıklamanın aynı PLC bağlantısına karşı ikinci bir
    ///     döngüsel okuyucu başlatmak yerine mevcut pencereyi öne getirmesi için tutulur.
    /// </summary>
    private ManualPageWindow? _manualPageWindow;

    // IP adresi validasyon pattern'i (0-255.0-255.0-255.0-255)
    private static readonly Regex IpRegex = new(
        @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// EN: Initializes the main window, sets up the PLC service, loads settings, and applies language.
    /// TR: Ana pencereyi başlatır, PLC servisini kurar, ayarları yükler ve dili uygular.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _plcService = new PlcService();
        _plcService.StatusChanged += OnPlcStatusChanged;

        // CPU tiplerini ComboBox'a ekle
        InitializeCpuTypes();

        // Sembol adreslerini ComboBox'lara yükle
        LoadSymbolAddresses();

        // Bağlantı ayarlarını yükle
        LoadConnectionSettings();

        // Dil desteği
        _languageChangedHandler = (_, _) => ApplyLanguage();
        L.LanguageChanged += _languageChangedHandler;
        InitializeLanguageMenu();
        ApplyLanguage();

        // Sürüm, durum çubuğunun sağında durur; güncelleme kontrolünün karşılaştırdığı değerle aynıdır.
        TxtVersion.Text = UpdateChecker.CurrentVersionText;

        // Sürüm kontrolü açılışı bekletmemeli; pencere göründükten sonra çalışır.
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// EN: One-shot startup work that must not delay the window: the GitHub version check.
    /// TR: Pencereyi geciktirmemesi gereken tek seferlik açılış işi: GitHub sürüm kontrolü.
    /// </summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await CheckForUpdatesAsync();
    }

    /// <summary>
    /// EN: Asks GitHub for the latest release and offers its download page when it is newer than
    ///     this build. Failures are logged only: on a machine PC having no internet is the normal case.
    /// TR: GitHub'a en son release'i sorar ve bu yapıdan yeniyse indirme sayfasını önerir.
    ///     Hatalar yalnızca loglanır: makine PC'sinde internet olmaması olağan durumdur.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        var settings = ConnectionSettings.Load();
        if (!settings.CheckUpdatesOnStartup)
            return;

        UpdateInfo? update;
        try
        {
            update = await UpdateChecker.CheckAsync();
        }
        catch (Exception ex)
        {
            // Ağ yoksa ya da GitHub cevap vermiyorsa kullanıcıyı rahatsız etme; log yeter.
            AddLog(L.T("Log_UpdateCheckFailed", ex.Message));
            return;
        }

        if (update is null)
        {
            AddLog(L.T("Log_UpdateUpToDate", UpdateChecker.CurrentVersionText));
            return;
        }

        AddLog(L.T("Log_UpdateAvailable", update.TagName, UpdateChecker.CurrentVersionText));

        // Operatör bu sürümü bir kez atladıysa aynı soru her açılışta sorulmaz.
        if (string.Equals(update.TagName, settings.SkippedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            return;

        var answer = MessageDialog.Show(
            L.T("Update_Message", update.TagName, UpdateChecker.CurrentVersionText),
            L.T("Update_Title"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Information,
            this,
            L.T("Update_Download"),
            L.T("Update_Skip"),
            L.T("Update_Later"));

        if (answer == MessageBoxResult.Yes)
            OpenReleasePage(update.ReleaseUrl);
        else if (answer == MessageBoxResult.No)
            SkipUpdateVersion(settings, update.TagName);
    }

    /// <summary>
    /// EN: Opens the release page in the default browser. The app never downloads or replaces itself.
    /// TR: Release sayfasını varsayılan tarayıcıda açar. Uygulama kendini indirmez, kendini değiştirmez.
    /// </summary>
    private void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddLog(L.T("Log_UpdateOpenFailed", url, ex.Message));
        }
    }

    /// <summary>
    /// EN: Remembers the skipped release tag so this one version is not offered again.
    /// TR: Atlanan release etiketini hatırlar; yalnızca o sürüm bir daha önerilmez.
    /// </summary>
    private void SkipUpdateVersion(ConnectionSettings settings, string tagName)
    {
        settings.SkippedUpdateVersion = tagName;
        try
        {
            settings.Save();
            AddLog(L.T("Log_UpdateSkipped", tagName));
        }
        catch (Exception ex)
        {
            // Program Files altında settings.json yazılabilir değil; atlama kalıcı olmaz.
            AddLog(L.T("Log_UpdateSkipFailed", ex.Message));
        }
    }

    /// <summary>
    /// EN: Opens the application settings menu, reading the current state from the settings file.
    /// TR: Uygulama ayarları menüsünü açar; güncel durumu ayar dosyasından okur.
    /// </summary>
    private void BtnAppSettings_Click(object sender, RoutedEventArgs e)
    {
        // İşareti her açılışta diskten tazele: settings.json elle de düzenlenmiş olabilir.
        MnuCheckUpdates.IsChecked = ConnectionSettings.Load().CheckUpdatesOnStartup;

        var menu = BtnAppSettings.ContextMenu;
        menu.PlacementTarget = BtnAppSettings;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// EN: Turns the startup update check on or off and persists it. Turning it back on also clears the
    ///     skipped release, so this menu item is the one place to undo "skip this version".
    /// TR: Açılıştaki güncelleme kontrolünü açar/kapatır ve kaydeder. Yeniden açmak atlanan sürümü de
    ///     temizler; böylece "bu sürümü atla" kararını geri almanın tek yeri bu menü öğesidir.
    /// </summary>
    private void MnuCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var enabled = MnuCheckUpdates.IsChecked;
        try
        {
            var settings = ConnectionSettings.Load();
            settings.CheckUpdatesOnStartup = enabled;

            var hadSkippedVersion = !string.IsNullOrEmpty(settings.SkippedUpdateVersion);
            if (enabled)
                settings.SkippedUpdateVersion = string.Empty;

            settings.Save();

            AddLog(enabled ? L.T("Log_UpdateCheckEnabled") : L.T("Log_UpdateCheckDisabled"));
            if (enabled && hadSkippedVersion)
                AddLog(L.T("Log_UpdateSkipCleared"));
        }
        catch (Exception ex)
        {
            // Kaydedilemediyse (ör. Program Files altında) menü diski yanlış temsil etmesin.
            MnuCheckUpdates.IsChecked = !enabled;
            AddLog(L.T("Log_SettingsSaveError", ex.Message));
        }
    }

    /// <summary>
    /// EN: Opens the About box.
    /// TR: Program hakkında kutusunu açar.
    /// </summary>
    private void MnuAbout_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow(this).ShowDialog();
    }

    /// <summary>
    /// EN: Applies the active language to all UI elements.
    /// TR: Aktif dili tüm arayüz öğelerine uygular.
    /// </summary>
    private void ApplyLanguage()
    {
        Title = L.T("Win_Main");
        LblLeftPanelTitle.Text = L.T("LeftPanel_Title");
        LblCpuType.Text = L.T("Settings_CpuType");
        LblIpAddress.Text = L.T("Settings_IpAddress");
        LblPort.Text = L.T("Settings_Port");
        LblRack.Text = L.T("Settings_Rack");
        LblSlot.Text = L.T("Settings_Slot");
        LblPlcOps.Text = L.T("Section_PlcOps");
        LblReadAddress.Text = L.T("Read_AddressLabel");
        LblValuePrefix.Text = L.T("Read_ValueLabel");
        LblWriteAddress.Text = L.T("Write_AddressLabel");
        TxtInfoExpanderHeader.Text = L.T("Info_Header");
        TxtInfoContent.Text = L.T("InfoContent");
        LblLogTitle.Text = L.T("Log_Title");
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(CmbReadAddress, L.T("Read_Placeholder"));
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(CmbWriteAddress, L.T("Write_Placeholder"));
        if (string.IsNullOrEmpty(TxtReadValue.Text) || !_hasReadValue)
            TxtReadValue.Text = L.T("Read_NoValue");
        BtnSymbols.Content = L.T("Btn_Symbols");
        TxtBtnManualPage.Text = L.T("Btn_ManualPage");
        BtnManualPage.ToolTip = L.T("Btn_ManualPageTip");
        BtnRead.Content = L.T("Btn_Read");
        BtnWrite.Content = L.T("Btn_Write");
        TxtBtnClearLog.Text = L.T("Btn_ClearLog");
        TxtBtnSaveLog.Text = L.T("Btn_SaveLog");
        if (!_plcService.IsConnected)
        {
            TxtConnect.Text = L.T("Btn_Connect");
            TxtStatus.Text = L.T("Status_NotConnected");
            TxtStatusBar.Text = L.T("StatusBar_NotConnected");
        }
        BtnAppSettings.ToolTip = L.T("Menu_AppSettingsTip");
        MnuCheckUpdates.Header = L.T("Menu_CheckUpdatesOnStartup");
        MnuAbout.Header = L.T("Menu_About");
        UpdateThemeMenuHeaders();
        UpdateWriteValuePlaceholder();
        UpdateBoolToggleCaption();
    }

    /// <summary>
    /// EN: Updates theme menu item headers with the current language strings.
    /// TR: Tema menü öğelerinin başlıklarını geçerli dil string'leriyle günceller.
    /// </summary>
    private void UpdateThemeMenuHeaders()
    {
        foreach (MenuItem item in BtnToggleTheme.ContextMenu.Items)
        {
            item.Header = item.Tag?.ToString() switch
            {
                "Light"      => L.T("Theme_Light"),
                "Dark"       => L.T("Theme_Dark"),
                "Industrial" => L.T("Theme_Industrial"),
                "Night"      => L.T("Theme_Night"),
                _            => item.Header
            };
        }
    }

    /// <summary>
    /// EN: Populates the language selector context menu with available languages.
    /// TR: Dil seçici bağlam menüsünü mevcut dillerle doldurur.
    /// </summary>
    private void InitializeLanguageMenu()
    {
        var contextMenu = new ContextMenu();
        foreach (var lang in L.Available)
        {
            var mi = new MenuItem
            {
                Header = lang.DisplayName,
                Tag = lang.Code,
                IsCheckable = true,
                IsChecked = lang.Code == L.CurrentLanguageCode
            };
            mi.Click += LanguageMenuItem_Click;
            contextMenu.Items.Add(mi);
        }
        BtnToggleLanguage.ContextMenu = contextMenu;
    }

    /// <summary>
    /// EN: Opens the language selection context menu.
    /// TR: Dil seçim bağlam menüsünü açar.
    /// </summary>
    private void BtnToggleLanguage_Click(object sender, RoutedEventArgs e)
    {
        var menu = BtnToggleLanguage.ContextMenu;
        foreach (MenuItem item in menu.Items)
            item.IsChecked = item.Tag?.ToString() == L.CurrentLanguageCode;
        menu.PlacementTarget = BtnToggleLanguage;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// EN: Called when a language is selected from the language menu. Switches the active language.
    /// TR: Dil menüsünden dil seçildiğinde çağrılır. Aktif dili değiştirir.
    /// </summary>
    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var code = mi.Tag?.ToString() ?? "en-US";
        foreach (MenuItem item in BtnToggleLanguage.ContextMenu.Items)
            item.IsChecked = item == mi;
        App.SetLanguage(code);
        AddLog(L.T("Log_LanguageChanged", mi.Header));
    }

    private void InitializeInfoContent()
    {
        // Content is set via ApplyLanguage() › L.T("InfoContent")
    }

    /// <summary>
    /// EN: Populates the CPU type ComboBox with all supported Siemens PLC types.
    /// TR: CPU tipi ComboBox'ını desteklenen tüm Siemens PLC tipleriyle doldurur.
    /// </summary>
    private void InitializeCpuTypes()
    {
        // S7.Net kütüphanesindeki tüm CPU tiplerini ekle
        CmbCpuType.Items.Add(new CpuTypeItem("S7-1200", CpuType.S71200));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-1500", CpuType.S71500));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-300", CpuType.S7300));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-400", CpuType.S7400));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-200", CpuType.S7200));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-200 Smart", CpuType.S7200Smart));

        CmbCpuType.SelectedIndex = 0; // Varsayılan: S7-1200
    }

    /// <summary>
    /// EN: Loads the saved connection settings from disk and applies them to the UI.
    /// TR: Kaydedilmiş bağlantı ayarlarını diskten yükler ve arayüze uygular.
    /// </summary>
    private void LoadConnectionSettings()
    {
        try
        {
            var settings = ConnectionSettings.Load();

            // Kaydedilen temayı uygula
            App.SetNamedTheme(settings.Theme);

            if (settings.IsValid())
            {
                // CPU tipini seç
                for (int i = 0; i < CmbCpuType.Items.Count; i++)
                {
                    var item = (CpuTypeItem)CmbCpuType.Items[i];
                    if (item.DisplayName == settings.CpuType)
                    {
                        CmbCpuType.SelectedIndex = i;
                        break;
                    }
                }

                // IP adresi ve Port'u yükle
                TxtIpAddress.Text = settings.IpAddress;
                TxtPort.Text = settings.Port.ToString();

                // Rack ve Slot değerlerini yükle
                TxtRack.Text = settings.Rack.ToString();
                TxtSlot.Text = settings.Slot.ToString();

                AddLog(L.T("Log_SettingsLoaded"));
            }
        }
        catch (Exception ex)
        {
            AddLog(L.T("Log_SettingsLoadError", ex.Message));
        }
    }

    /// <summary>
    /// EN: Saves the current connection settings from the UI to disk.
    /// TR: Arayüzdeki geçerli bağlantı ayarlarını diske kaydeder.
    /// </summary>
    private void SaveConnectionSettings()
    {
        try
        {
            var selectedCpu = (CpuTypeItem)CmbCpuType.SelectedItem;

            var settings = ConnectionSettings.Load();
            settings.CpuType = selectedCpu.DisplayName;
            settings.IpAddress = TxtIpAddress.Text.Trim();
            settings.Port = int.Parse(TxtPort.Text);
            settings.Rack = short.Parse(TxtRack.Text);
            settings.Slot = short.Parse(TxtSlot.Text);

            settings.Save();
            AddLog(L.T("Log_SettingsSaved"));
        }
        catch (Exception ex)
        {
            AddLog(L.T("Log_SettingsSaveError", ex.Message));
        }
    }

    /// <summary>
    /// EN: Reloads symbol addresses into the read/write ComboBoxes and rebuilds the symbol tree.
    /// TR: Sembol adreslerini okuma/yazma ComboBox'larına yeniden yükler ve sembol ağacını yeniden oluşturur.
    /// </summary>
    private void LoadSymbolAddresses()
    {
        // Mevcut sembolleri ComboBox'lara yükle
        CmbReadAddress.Items.Clear();
        CmbWriteAddress.Items.Clear();

        var symbols = _plcService.SymbolMapper.GetAllSymbols();
        _allSymbols = symbols.ToDictionary(s => s.Key, s => s.Value);

        foreach (var symbol in symbols.OrderBy(s => s.Key))
        {
            CmbReadAddress.Items.Add(symbol.Key);
            CmbWriteAddress.Items.Add(symbol.Key);
        }

        // Eğer sembol varsa, ilk sembolü seç
        if (CmbReadAddress.Items.Count > 0)
        {
            CmbReadAddress.SelectedIndex = 0;
            CmbWriteAddress.SelectedIndex = 0;
        }

        // TreeView ağacını oluştur
        BuildSymbolTree();
    }

    /// <summary>
    /// EN: Builds the hierarchical symbol tree from the symbol map and binds it to the TreeView.
    /// TR: Sembol haritasından hiyerarşik sembol ağacını oluşturur ve TreeView'a bağlar.
    /// </summary>
    private void BuildSymbolTree()
    {
        _symbolTreeNodes.Clear();
        var rootNodes = new Dictionary<string, SymbolTreeNode>();

        foreach (var symbol in _allSymbols)
        {
            var parts = symbol.Key.Split('.');
            var currentLevel = rootNodes;
            SymbolTreeNode? parentNode = null;
            string currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";

                if (!currentLevel.ContainsKey(part))
                {
                    var isLeaf = i == parts.Length - 1;
                    var node = new SymbolTreeNode
                    {
                        Name = part,
                        FullPath = currentPath,
                        Icon = isLeaf ? "\uE8EA" : "\uE8B7", // ?? Item : ?? Folder
                        IsLeaf = isLeaf
                    };

                    if (isLeaf && symbol.Value != null)
                    {
                        node.PhysicalAddress = symbol.Value.PhysicalAddress;
                        node.DataType = symbol.Value.DataType;
                    }

                    currentLevel[part] = node;

                    if (parentNode != null)
                    {
                        parentNode.Children.Add(node);
                    }
                    else
                    {
                        _symbolTreeNodes.Add(node);
                    }
                }

                parentNode = currentLevel[part];

                // Bir sonraki seviye için dictionary oluştur
                var nextLevel = new Dictionary<string, SymbolTreeNode>();
                foreach (var child in parentNode.Children)
                {
                    nextLevel[child.Name] = child;
                }
                currentLevel = nextLevel;
            }
        }

        TreeSymbols.ItemsSource = _symbolTreeNodes;
    }

    /// <summary>
    /// EN: Called when the selected tree node changes. Filters ComboBoxes by the selected node.
    /// TR: Seçili ağaç düğümü değiştiğinde çağrılır. ComboBox'ları seçili düğüme göre filtreler.
    /// </summary>
    private void TreeSymbols_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SymbolTreeNode node)
        {
            FilterComboBoxByNode(CmbReadAddress, node);
            FilterComboBoxByNode(CmbWriteAddress, node);
        }
    }

    /// <summary>
    /// EN: Toggles the symbol panel expand/collapse state with an animation.
    /// TR: Sembol panelini animasyonla açıp kapatan butonu yönetir.
    /// </summary>
    private void BtnToggleSymbolPanel_Click(object sender, RoutedEventArgs e)
    {
        _isLeftPanelExpanded = !_isLeftPanelExpanded;

        var animation = new DoubleAnimation
        {
            To = _isLeftPanelExpanded ? LeftPanelExpandedWidth : LeftPanelCollapsedWidth,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        if (_isLeftPanelExpanded)
        {
            IconTogglePanel.Glyph = "\uE76B";
            animation.Completed += (s, _) =>
            {
                LeftPanelHeaderContent.Visibility = Visibility.Visible;
                TreeSymbols.Visibility = Visibility.Visible;
            };
        }
        else
        {
            LeftPanelHeaderContent.Visibility = Visibility.Collapsed;
            TreeSymbols.Visibility = Visibility.Hidden;
            IconTogglePanel.Glyph = "\uE76C";
        }

        LeftPanelBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
    }

    /// <summary>
    /// EN: Called when the read address ComboBox selection changes. Updates the physical address label.
    /// TR: Okuma adresi ComboBox seçimi değiştiğinde çağrılır. Fiziksel adres etiketini günceller.
    /// </summary>
    private void CmbReadAddress_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdatePhysicalAddress(CmbReadAddress, TxtReadPhysicalAddress);
    }

    /// <summary>
    /// EN: Called when the write address ComboBox selection changes. Updates the physical address label and value placeholder.
    /// TR: Yazma adresi ComboBox seçimi değiştiğinde çağrılır. Fiziksel adres etiketini ve değer yer tutucuçusunu günceller.
    /// </summary>
    private void CmbWriteAddress_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdatePhysicalAddress(CmbWriteAddress, TxtWritePhysicalAddress);
        UpdateWriteValuePlaceholder();
        UpdateBoolWriteToggle();

        // Mandal PLC'deki biti temsil etmeli; seçim değişince mevcut değeri okuyup hizala.
        // (Okumak yazmak değildir; PLC'ye hiçbir şey gönderilmez.)
        _ = SyncBoolToggleFromPlcAsync();
    }

    /// <summary>
    /// EN: Called while the write address ComboBox text is edited; keeps the BOOL toggle in sync.
    /// TR: Yazma adresi ComboBox metni düzenlenirken çağrılır; BOOL mandalını güncel tutar.
    /// </summary>
    private void CmbWriteAddress_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateBoolWriteToggle();
    }

    /// <summary>
    /// EN: Shows the latching BOOL button only when the write target is a BOOL, and refreshes its caption.
    /// TR: Mandallı BOOL butonunu yalnızca yazma hedefi BOOL ise gösterir ve yazısını tazeler.
    /// </summary>
    private void UpdateBoolWriteToggle()
    {
        // XAML ayrıştırılırken TextChanged, alanlar atanmadan önce tetiklenebiliyor.
        if (BtnWriteBoolToggle is null || CmbWriteAddress is null)
            return;

        var isBool = IsBoolWriteTarget(CmbWriteAddress.Text?.Trim() ?? string.Empty);
        BtnWriteBoolToggle.Visibility = isBool ? Visibility.Visible : Visibility.Collapsed;
        BtnWriteBoolToggle.IsEnabled = isBool && _plcService.IsConnected;

        if (!isBool)
            SetBoolToggleState(false);
        else
            UpdateBoolToggleCaption();
    }

    /// <summary>
    /// EN: Decides whether the given write address refers to a BOOL (symbol table first, then physical bit syntax).
    /// TR: Verilen yazma adresinin BOOL olup olmadığına karar verir (önce sembol tablosu, sonra fiziksel bit yazımı).
    /// </summary>
    private bool IsBoolWriteTarget(string address)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        var symbolInfo = _plcService.SymbolMapper.GetSymbolInfo(address);
        if (symbolInfo != null && !string.IsNullOrEmpty(symbolInfo.DataType))
            return symbolInfo.DataType.Trim().ToUpperInvariant() == "BOOL";

        // Sembol tablosunda yoksa elle yazılmış fiziksel bit adresi olabilir: DB1.DBX0.0 / M0.0 / I0.0 / Q0.0
        return BoolPhysicalAddressRegex.IsMatch(address);
    }

    /// <summary>
    /// EN: Sets the toggle state without raising a write.
    /// TR: Yazma tetiklemeden mandalın konumunu ayarlar.
    /// </summary>
    private void SetBoolToggleState(bool isOn)
    {
        _suppressBoolToggleWrite = true;
        try
        {
            BtnWriteBoolToggle.IsChecked = isOn;
        }
        finally
        {
            _suppressBoolToggleWrite = false;
        }
        UpdateBoolToggleCaption();
    }

    /// <summary>
    /// EN: Refreshes the toggle caption and tooltip from the current language and state.
    /// TR: Mandalın yazısını ve ipucunu geçerli dile ve konuma göre tazeler.
    /// </summary>
    private void UpdateBoolToggleCaption()
    {
        if (BtnWriteBoolToggle is null)
            return;

        BtnWriteBoolToggle.Content = BtnWriteBoolToggle.IsChecked == true
            ? L.T("Write_BoolOn")
            : L.T("Write_BoolOff");
        BtnWriteBoolToggle.ToolTip = L.T("Write_BoolToggleTip");
    }

    /// <summary>
    /// EN: Best-effort read of the selected BOOL address to align the latch with the PLC; failures are logged only.
    /// TR: Mandalı PLC ile hizalamak için seçili BOOL adresini okumayı dener; hata yalnızca loglanır.
    /// </summary>
    private async Task SyncBoolToggleFromPlcAsync()
    {
        if (BtnWriteBoolToggle is null || BtnWriteBoolToggle.Visibility != Visibility.Visible)
            return;
        if (!_plcService.IsConnected)
            return;

        var address = CmbWriteAddress.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(address))
            return;

        try
        {
            var value = await _plcService.ReadAsync(address);
            // Adres bu arada değişmiş olabilir; geç gelen cevabı yanlış adrese uygulama.
            if (!string.Equals(address, CmbWriteAddress.Text?.Trim(), StringComparison.Ordinal))
                return;

            var isOn = value switch
            {
                bool b  => b,
                byte by => by != 0,
                int i   => i != 0,
                _       => false
            };
            SetBoolToggleState(isOn);
        }
        catch (Exception ex)
        {
            AddLog(L.T("Log_BoolToggleSyncFailed", address, ex.Message));
        }
    }

    /// <summary>
    /// EN: Latching BOOL button: each click writes the new latched state (TRUE/FALSE) to the PLC.
    /// TR: Mandallı BOOL butonu: her tıklama yeni mandal konumunu (TRUE/FALSE) PLC'ye yazar.
    /// </summary>
    private async void BtnWriteBoolToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressBoolToggleWrite)
            return;

        var desired = BtnWriteBoolToggle.IsChecked == true;
        UpdateBoolToggleCaption();

        var address = CmbWriteAddress.Text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            SetBoolToggleState(!desired);
            MessageDialog.Show(L.T("Msg_EmptyAddress"), L.T("MsgTitle_Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        if (!_plcService.IsConnected)
        {
            SetBoolToggleState(!desired);
            MessageDialog.Show(L.T("Msg_NotConnected"), L.T("MsgTitle_Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        // Yaz butonu ile mandal aynı değeri göstersin; operatör iki farklı şey görmesin.
        TxtWriteValue.Text = desired ? "true" : "false";

        try
        {
            AddLog(L.T("Log_Writing", address, desired));
            await _plcService.WriteAsync(address, desired);
            AddLog(L.T("Log_WriteSuccess", address));
        }
        catch (Exception ex)
        {
            // Yazma başarısızsa mandal PLC'yi yanlış temsil etmesin: eski konumuna dönsün.
            SetBoolToggleState(!desired);
            TxtWriteValue.Text = !desired ? "true" : "false";
            MessageDialog.Show(L.T("Msg_WriteError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    /// <summary>
    /// EN: Updates a physical address label based on the selected ComboBox item.
    /// TR: Seçili ComboBox öğesine göre fiziksel adres etiketini günceller.
    /// </summary>
    private void UpdatePhysicalAddress(System.Windows.Controls.ComboBox comboBox, System.Windows.Controls.TextBlock textBlock)
    {
        if (comboBox.SelectedItem is string selectedAddress && !string.IsNullOrEmpty(selectedAddress))
        {
            if (_allSymbols.TryGetValue(selectedAddress, out var symbolInfo))
            {
                textBlock.Text = $"?? {symbolInfo.PhysicalAddress}  •  {symbolInfo.DataType}";
            }
            else
            {
                textBlock.Text = "";
            }
        }
        else
        {
            textBlock.Text = "";
        }
    }

    /// <summary>
    /// EN: Updates the write value TextBox placeholder text based on the selected address data type.
    /// TR: Seçili adresin veri tipine göre yazma değeri TextBox yer tutucuçusunu günceller.
    /// </summary>
    private void UpdateWriteValuePlaceholder()
    {
        var dataType = string.Empty;
        if (CmbWriteAddress.SelectedItem is string selectedAddress &&
            _allSymbols.TryGetValue(selectedAddress, out var symbolInfo))
        {
            dataType = symbolInfo.DataType;
        }
        TxtWriteValue.Text = string.Empty;
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(
            TxtWriteValue, GetWriteValuePlaceholder(dataType));
    }

    /// <summary>
    /// EN: Returns the appropriate placeholder text for the write value field based on the PLC data type.
    /// TR: PLC veri tipine göre yazma değeri alanı için uygun yer tutucuçu metni döndürür.
    /// </summary>
    private static string GetWriteValuePlaceholder(string dataType)
    {
        if (string.IsNullOrEmpty(dataType))
            return L.T("Placeholder_Write_Generic");

        var upper = dataType.ToUpperInvariant();

        if (upper.StartsWith("STRING"))  return L.T("Placeholder_Write_String");
        if (upper.StartsWith("WSTRING")) return L.T("Placeholder_Write_String");

        return upper switch
        {
            "BOOL"                      => "true / false",
            "BYTE" or "USINT"           => "0 … 255",
            "SINT"                      => "-128 … 127",
            "WORD" or "UINT"            => "0 … 65535",
            "INT"                       => "-32768 … 32767",
            "DWORD" or "UDINT"          => "0 … 4294967295",
            "DINT"                      => "-2147483648 … 2147483647",
            "REAL"                      => L.T("Placeholder_Write_Real"),
            "LWORD" or "ULINT"          => "0 … 18446744073709551615",
            "LINT"                      => "-9223372036854775808 … 9223372036854775807",
            "LREAL"                     => L.T("Placeholder_Write_LReal"),
            "CHAR" or "WCHAR"           => L.T("Placeholder_Write_Char"),
            "TIME"                      => L.T("Placeholder_Write_Time"),
            "S5TIME"                    => L.T("Placeholder_Write_S5Time"),
            "DATE"                      => L.T("Placeholder_Write_Date"),
            "TIME_OF_DAY" or "TOD"      => L.T("Placeholder_Write_Tod"),
            "DATE_AND_TIME" or "DT"     => L.T("Placeholder_Write_Dt"),
            "DTL"                       => L.T("Placeholder_Write_Dtl"),
            _                           => L.T("Placeholder_Write_Generic")
        };
    }

    /// <summary>
    /// EN: Filters a ComboBox to show only the addresses under the selected tree node.
    /// TR: Bir ComboBox'ı seçili ağaç düğümü altındaki adresleri gösterecek şekilde filtreler.
    /// </summary>
    private void FilterComboBoxByNode(System.Windows.Controls.ComboBox comboBox, SymbolTreeNode node)
    {
        comboBox.Items.Clear();

        if (node.IsLeaf)
        {
            // Eğer leaf ise, sadece o adresi ekle ve seç
            comboBox.Items.Add(node.FullPath);
            comboBox.SelectedIndex = 0;
        }
        else
        {
            // Eğer parent node ise, altındaki tüm leaf'leri ekle
            var leaves = GetAllLeaves(node);
            foreach (var leaf in leaves.OrderBy(l => l))
            {
                comboBox.Items.Add(leaf);
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }
    }

    /// <summary>
    /// EN: Recursively collects all leaf node full paths under a given tree node.
    /// TR: Verilen ağaç düğümü altındaki tüm yaprak düğüm tam yollarını özyineli olarak toplar.
    /// </summary>
    private List<string> GetAllLeaves(SymbolTreeNode node)
    {
        var leaves = new List<string>();

        if (node.IsLeaf)
        {
            leaves.Add(node.FullPath);
        }
        else
        {
            foreach (var child in node.Children)
            {
                leaves.AddRange(GetAllLeaves(child));
            }
        }

        return leaves;
    }

    #region IP Address Validation

    /// <summary>
    /// EN: Handles IP address text box input, allowing only digits and dots.
    /// TR: IP adresi metin kutusu girişini yönetir, yalnızca rakam ve noktaya izin verir.
    /// </summary>
    private void TxtIpAddress_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Sadece rakam ve nokta girilmesine izin ver
        var regex = new Regex(@"[^0-9.]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    /// <summary>
    /// EN: Validates the IP address when the text box loses focus.
    /// TR: Metin kutusu odak kaybettiğinde IP adresini doğrular.
    /// </summary>
    private void TxtIpAddress_LostFocus(object sender, RoutedEventArgs e)
    {
        ValidateIpAddress();
    }

    /// <summary>
    /// EN: Validates the IP address field against the IPv4 regex pattern.
    /// TR: IP adresi alanını IPv4 regex kalıbına göre doğrular.
    /// </summary>
    private bool ValidateIpAddress()
    {
        var ipText = TxtIpAddress.Text.Trim();

        if (string.IsNullOrEmpty(ipText))
        {
            SetIpValidationState(false, L.T("Validation_IpEmpty"));
            return false;
        }

        if (!IpRegex.IsMatch(ipText))
        {
            SetIpValidationState(false, L.T("Validation_IpFormat"));
            return false;
        }

        SetIpValidationState(true, null);
        return true;
    }

    /// <summary>
    /// EN: Sets the visual validation state of the IP address TextBox (border color and tooltip).
    /// TR: IP adresi TextBox'ın görsel doğrulama durumunu ayarlar (kenar rengi ve ipucu).
    /// </summary>
    private void SetIpValidationState(bool isValid, string? errorMessage)
    {
        if (isValid)
        {
            TxtIpAddress.BorderBrush = SystemColors.ControlDarkBrush;
            TxtIpAddress.BorderThickness = new Thickness(1);
            TxtIpAddress.ToolTip = null;
        }
        else
        {
            TxtIpAddress.BorderBrush = Brushes.Red;
            TxtIpAddress.BorderThickness = new Thickness(2);
            TxtIpAddress.ToolTip = errorMessage;
        }
    }

    #endregion

    /// <summary>
    /// EN: Handles PLC status change events. Dispatches the message to the UI thread and adds it to the log.
    /// TR: PLC durum değişikliği olaylarını yönetir. Mesajı UI thread'e iletir ve loga ekler.
    /// </summary>
    private void OnPlcStatusChanged(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            AddLog(message);
            // StatusBar sadece bağlantı durumu için kullanılıyor
        });
    }

    /// <summary>
    /// EN: Handles the Connect/Disconnect/Cancel button click. Manages connection state transitions.
    /// TR: Bağlan/Kes/İptal buton tıklamasını yönetir. Bağlantı durumu geçişlerini yönetir.
    /// </summary>
    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        // Eğer bağlıysa, bağlantıyı kes
        if (_plcService.IsConnected)
        {
            TxtStatus.Text = L.T("Status_Disconnecting");
            StatusIndicator.Fill = Brushes.Orange;
            BtnConnect.IsEnabled = false;
            IconConnect.Glyph = "\uE711";
            TxtConnect.Text = L.T("Btn_Disconnecting");

            await _plcService.DisconnectAsync();

            StatusIndicator.Fill = Brushes.Gray;
            TxtStatus.Text = L.T("Status_NotConnected");
            IconConnect.Glyph = "\uE703";
            TxtConnect.Text = L.T("Btn_Connect");
            BtnConnect.IsEnabled = true;
            BtnRead.IsEnabled = false;
            BtnWrite.IsEnabled = false;
            BtnWriteBoolToggle.IsEnabled = false;
            CmbCpuType.IsEnabled = true;
            TxtIpAddress.IsEnabled = true;
            TxtPort.IsEnabled = true;
            TxtRack.IsEnabled = true;
            TxtSlot.IsEnabled = true;

            TxtStatusBar.Text = L.T("StatusBar_NotConnected");
            return;
        }

        // Eğer vazgeç modundaysa, bağlantıyı iptal et
        if (_connectionCancellationTokenSource != null)
        {
            _connectionCancellationTokenSource.Cancel();
            AddLog(L.T("Log_ConnectionCancelled"));
            TxtStatus.Text = L.T("Status_Cancelling");
            StatusIndicator.Fill = Brushes.Orange;
            IconConnect.Glyph = "\uE703";
            TxtConnect.Text = L.T("Btn_Connect");
            BtnConnect.IsEnabled = false;
            return;
        }

        // IP adresini kontrol et
        if (!ValidateIpAddress())
        {
            MessageDialog.Show(L.T("Msg_InvalidIp"), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
            TxtIpAddress.Focus();
            return;
        }

        IconConnect.Glyph = "\uE711";
        TxtConnect.Text = L.T("Btn_CancelConnect");
        TxtStatus.Text = L.T("Status_Connecting");
        StatusIndicator.Fill = Brushes.Orange;
        _connectionCancellationTokenSource = new CancellationTokenSource();

        var selectedCpu = (CpuTypeItem)CmbCpuType.SelectedItem;
        AddLog(L.T("Log_Connecting", selectedCpu.DisplayName, TxtIpAddress.Text));

        try
        {
            var connected = await _plcService.ConnectAsync(
                selectedCpu.CpuType,
                TxtIpAddress.Text,
                short.Parse(TxtRack.Text),
                short.Parse(TxtSlot.Text),
                _connectionCancellationTokenSource.Token
            );

            if (connected)
            {
                StatusIndicator.Fill = Brushes.LimeGreen;
                TxtStatus.Text = L.T("Status_Connected");
                IconConnect.Glyph = "\uE711";
                TxtConnect.Text = L.T("Btn_Disconnect");
                BtnConnect.IsEnabled = true;
                BtnRead.IsEnabled = true;
                BtnWrite.IsEnabled = true;
                BtnWriteBoolToggle.IsEnabled = BtnWriteBoolToggle.Visibility == Visibility.Visible;
                CmbCpuType.IsEnabled = false;
                TxtIpAddress.IsEnabled = false;
                TxtPort.IsEnabled = false;
                TxtRack.IsEnabled = false;
                TxtSlot.IsEnabled = false;

                var port = int.Parse(TxtPort.Text);
                TxtStatusBar.Text = L.T("StatusBar_Connected", selectedCpu.DisplayName,
                    _plcService.ConnectedIpAddress, port,
                    _plcService.ConnectedRack, _plcService.ConnectedSlot);

                // Bağlantı ayarlarını kaydet
                SaveConnectionSettings();
            }
            else
            {
                StatusIndicator.Fill = Brushes.Red;
                TxtStatus.Text = L.T("Status_ConnectionFailed");
                IconConnect.Glyph = "\uE703";
                TxtConnect.Text = L.T("Btn_Connect");
                BtnConnect.IsEnabled = true;
                TxtStatusBar.Text = L.T("StatusBar_NotConnected");
            }
        }
        catch (OperationCanceledException)
        {
            StatusIndicator.Fill = Brushes.Gray;
            TxtStatus.Text = L.T("Status_ConnectionCancelled");
            IconConnect.Glyph = "\uE703";
            TxtConnect.Text = L.T("Btn_Connect");
            BtnConnect.IsEnabled = true;
            TxtStatusBar.Text = L.T("StatusBar_NotConnected");
            AddLog(L.T("Log_ConnectionCancelledUser"));
        }
        catch (Exception ex)
        {
            StatusIndicator.Fill = Brushes.Red;
            TxtStatus.Text = L.T("Status_Error");
            MessageDialog.Show(L.T("Msg_UnexpectedError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
            IconConnect.Glyph = "\uE703";
            TxtConnect.Text = L.T("Btn_Connect");
            BtnConnect.IsEnabled = true;
        }
        finally
        {
            _connectionCancellationTokenSource?.Dispose();
            _connectionCancellationTokenSource = null;
        }
    }

    /// <summary>
    /// EN: Reads the value from the PLC at the address entered in the read ComboBox.
    /// TR: Okuma ComboBox'ında girilen adresten PLC değerini okur.
    /// </summary>
    private async void BtnRead_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var address = CmbReadAddress.Text.Trim();
            if (string.IsNullOrEmpty(address))
            {
                MessageDialog.Show(L.T("Msg_EmptyAddress"), L.T("MsgTitle_Warning"),
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            AddLog(L.T("Log_Reading", address));
            var value = await _plcService.ReadAsync(address);
            _hasReadValue = true;
            TxtReadValue.Text = $"{value}";
            AddLog(L.T("Log_ReadResult", address, value));
        }
        catch (Exception ex)
        {
            MessageDialog.Show(L.T("Msg_ReadError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    /// <summary>
    /// EN: Writes the entered value to the PLC at the address selected in the write ComboBox.
    /// TR: Yazma ComboBox'ında seçilen adrese PLC'ye girilen değeri yazar.
    /// </summary>
    private async void BtnWrite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var address = CmbWriteAddress.Text.Trim();
            var valueText = TxtWriteValue.Text.Trim();

            if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(valueText))
            {
                MessageDialog.Show(L.T("Msg_EmptyAddressValue"), L.T("MsgTitle_Warning"),
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            // Sembol bilgisinden veri tipini al
            var symbolInfo = _plcService.SymbolMapper.GetSymbolInfo(address);
            object value;

            // Veri tipine göre uygun dönüşüm yap
            if (symbolInfo != null && !string.IsNullOrEmpty(symbolInfo.DataType))
            {
                var dataType = symbolInfo.DataType.ToUpperInvariant();

                // STRING[xx] ve WSTRING[xx] formatını kontrol et
                if (dataType.StartsWith("STRING") || dataType.StartsWith("WSTRING"))
                {
                    value = valueText; // String olarak direkt kullan
                }
                else if (dataType == "CHAR" || dataType == "WCHAR")
                {
                    value = valueText; // PlcService'te byte/ushort'a dönüştürülecek
                }
                else if (dataType == "BOOL")
                {
                    // Bool için: "true", "false", "0", "1" kabul et
                    if (bool.TryParse(valueText, out var boolVal))
                        value = boolVal;
                    else if (int.TryParse(valueText, out var intVal))
                        value = intVal == 1;
                    else
                        value = false;
                }
                else if (dataType == "REAL")
                {
                    value = double.Parse(valueText);
                }
                else if (dataType == "UINT" || dataType == "USINT")
                {
                    value = ushort.Parse(valueText);
                }
                else if (dataType == "UDINT")
                {
                    value = uint.Parse(valueText);
                }
                else if (dataType == "LREAL")
                {
                    value = double.Parse(valueText.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (dataType == "LINT")
                {
                    value = long.Parse(valueText);
                }
                else if (dataType == "ULINT" || dataType == "LWORD")
                {
                    value = ulong.Parse(valueText);
                }
                else if (dataType == "TIME" || dataType == "S5TIME" || dataType == "COUNTER" ||
                         dataType == "DATE" || dataType == "TIME_OF_DAY" || dataType == "TOD" ||
                         dataType == "DATE_AND_TIME" || dataType == "DT" || dataType == "DTL")
                {
                    value = valueText; // PlcService'te uygun tipe dönüştürülecek
                }
                else if (dataType == "DWORD")
                {
                    value = uint.Parse(valueText);   // 0..4294967295
                }
                else if (dataType == "WORD")
                {
                    value = ushort.Parse(valueText); // 0..65535
                }
                else if (dataType.Contains("INT")) // SINT, INT, DINT
                {
                    value = int.Parse(valueText);
                }
                else
                {
                    // Bilinmeyen tip, akıllı tahmin et
                    value = SmartParseValue(valueText);
                }
            }
            else
            {
                // Sembol bilgisi yoksa, akıllı tahmin et
                value = SmartParseValue(valueText);
            }

            AddLog(L.T("Log_Writing", address, value));
            await _plcService.WriteAsync(address, value);
            AddLog(L.T("Log_WriteSuccess", address));
        }
        catch (Exception ex)
        {
            MessageDialog.Show(L.T("Msg_WriteError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    /// <summary>
    /// Değeri akıllıca parse eder (tip bilinmiyorsa)
    /// </summary>
    /// <summary>
    /// EN: Smartly parses a string value to the most appropriate .NET type (bool, double, int, or string).
    /// TR: String değeri en uygun .NET tipine (bool, double, int veya string) dönüştürür.
    /// </summary>
    private object SmartParseValue(string valueText)
    {
        // Bool kontrolü
        if (bool.TryParse(valueText, out var boolVal))
            return boolVal;

        // Sayı kontrolü
        if (valueText.Contains(".") || valueText.Contains(","))
        {
            // Ondalıklı sayı
            if (double.TryParse(valueText.Replace(',', '.'), System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
                return doubleVal;
        }
        else
        {
            // Tam sayı
            if (int.TryParse(valueText, out var intVal))
                return intVal;
        }

        // Hiçbiri değilse string olarak kabul et
        return valueText;
    }

    /// <summary>
    /// EN: Appends a timestamped message to the log panel.
    /// TR: Log paneline zaman damgalı mesaj ekler.
    /// </summary>
    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        TxtLog.Text += $"[{timestamp}] {message}\n";
        TxtLog.ScrollToEnd();
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        if (MessageDialog.Show(L.T("Msg_ClearLogConfirm"),
            L.T("MsgTitle_ClearLog"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question, this) == MessageBoxResult.Yes)
        {
            TxtLog.Clear();
            AddLog(L.T("Log_LogCleared"));
        }
    }

    private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = L.T("FileFilter_Txt"),
                DefaultExt = ".txt",
                FileName = $"PLC_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = L.T("Dialog_SaveLog_Title")
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveFileDialog.FileName, TxtLog.Text);
                AddLog(L.T("Log_LogSaved", Path.GetFileName(saveFileDialog.FileName)));
                MessageDialog.Show(L.T("Msg_LogSaved", saveFileDialog.FileName),
                    L.T("MsgTitle_Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information, this);
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Show(L.T("Msg_LogSaveFailed", ex.Message),
                L.T("MsgTitle_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error, this);
        }
    }

    private void BtnSymbols_Click(object sender, RoutedEventArgs e)
    {
        var symbolWindow = new SymbolManagerWindow(_plcService.SymbolMapper);
        if (symbolWindow.ShowDialog() == true)
        {
            AddLog(L.T("Log_SymbolsUpdated", _plcService.SymbolMapper.GetAllSymbols().Count));

            // ComboBox'ları yeniden yükle
            LoadSymbolAddresses();
        }
    }

    /// <summary>
    /// EN: Opens the manual test page window. Shown non-modally so the operator can keep watching
    ///     the main window's connection state and log while a page is being monitored.
    /// TR: Manuel test sayfası penceresini açar. Modal olmayan biçimde gösterilir; böylece sayfa
    ///     izlenirken operatör ana penceredeki bağlantı durumunu ve logu görmeye devam eder.
    /// </summary>
    private void BtnManualPage_Click(object sender, RoutedEventArgs e)
    {
        if (_manualPageWindow is { IsLoaded: true })
        {
            _manualPageWindow.Activate();
            return;
        }

        _manualPageWindow = new ManualPageWindow(_plcService) { Owner = this };
        _manualPageWindow.LogMessage += (_, message) => AddLog(message);
        _manualPageWindow.Closed += (_, _) => _manualPageWindow = null;
        _manualPageWindow.Show();
    }

    /// <summary>
    /// Tema seçim menüsünü açar
    /// </summary>
    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var menu = BtnToggleTheme.ContextMenu;
        foreach (MenuItem item in menu.Items)
            item.IsChecked = item.Tag?.ToString() == App.CurrentThemeName;

        menu.PlacementTarget = BtnToggleTheme;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Tema menüsünden seçim yapıldığında çağrılır
    /// </summary>
    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;

        var themeName = mi.Tag?.ToString() ?? "Light";

        // Diğer item'ların işaretini kaldır
        foreach (MenuItem item in BtnToggleTheme.ContextMenu.Items)
            item.IsChecked = item == mi;

        App.SetNamedTheme(themeName);
        AddLog(L.T("Log_ThemeChanged", mi.Header));
    }

    /// <summary>
    /// Aktif temayı ayarlar dosyasına kaydeder
    /// </summary>
    private void SaveThemeSetting()
    {
        try
        {
            var settings = ConnectionSettings.Load();
            settings.Theme = App.CurrentThemeName;
            settings.Save();
        }
        catch { }
    }

    /// <summary>
    /// Bağlantı ayarlarını göster/gizle butonu
    /// </summary>
    private void BtnToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        ConnectionSettingsPopup.IsOpen = !ConnectionSettingsPopup.IsOpen;
    }

    /// <summary>
    /// Bağlantı ayarlarını kapat butonu
    /// </summary>
    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        ConnectionSettingsPopup.IsOpen = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_languageChangedHandler != null)
            L.LanguageChanged -= _languageChangedHandler;
        SaveThemeSetting();
        _plcService.DisconnectAsync().Wait();
        base.OnClosed(e);
    }

    // ComboBox için yardımcı sınıf
    /// <summary>
    /// EN: Helper class for displaying CPU types in the ComboBox.
    /// TR: ComboBox'ta CPU tiplerini göstermek için yardımcı sınıf.
    /// </summary>
    private class CpuTypeItem(string displayName, CpuType cpuType)
    {
        public string DisplayName { get; set; } = displayName;
        public CpuType CpuType { get; set; } = cpuType;

        public override string ToString() => DisplayName;
    }
}

/// <summary>
/// EN: Tree node model for displaying symbolic addresses in a TreeView.
/// TR: Sembolik adresleri TreeView'da göstermek için ağaç node modeli.
/// </summary>
public class SymbolTreeNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE8B7"; // ?? Folder icon
    public string PhysicalAddress { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsLeaf { get; set; }
    public ObservableCollection<SymbolTreeNode> Children { get; set; } = new();
}