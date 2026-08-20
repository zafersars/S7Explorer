using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using S7.Net;
using System.IO;
using System.Collections.ObjectModel;

namespace S7Explorer;

/// <summary>
/// EN: Main application window. Manages PLC connection, read/write operations,
///     symbol tree, theme and language selection.
/// TR: Ana uygulama penceresi. PLC ba�lant�s�, okuma/yazma i�lemleri,
///     sembol a�ac�, tema ve dil se�imini y�netir.
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
    /// TR: Ana pencereyi ba�lat�r, PLC servisini kurar, ayarlar� y�kler ve dili uygular.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _plcService = new PlcService();
        _plcService.StatusChanged += OnPlcStatusChanged;

        // CPU tiplerini ComboBox'a ekle
        InitializeCpuTypes();

        // Sembol adreslerini ComboBox'lara y�kle
        LoadSymbolAddresses();

        // Ba�lant� ayarlar�n� y�kle
        LoadConnectionSettings();

        // Dil deste�i
        _languageChangedHandler = (_, _) => ApplyLanguage();
        L.LanguageChanged += _languageChangedHandler;
        InitializeLanguageMenu();
        ApplyLanguage();
    }

    /// <summary>
    /// EN: Applies the active language to all UI elements.
    /// TR: Aktif dili t�m aray�z ��elerine uygular.
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
        UpdateThemeMenuHeaders();
        UpdateWriteValuePlaceholder();
    }

    /// <summary>
    /// EN: Updates theme menu item headers with the current language strings.
    /// TR: Tema men� ��elerinin ba�l�klar�n� ge�erli dil string'leriyle g�nceller.
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
    /// TR: Dil se�ici ba�lam men�s�n� mevcut dillerle doldurur.
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
    /// TR: Dil se�im ba�lam men�s�n� a�ar.
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
    /// TR: Dil men�s�nden dil se�ildi�inde �a�r�l�r. Aktif dili de�i�tirir.
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
        // Content is set via ApplyLanguage() � L.T("InfoContent")
    }

    /// <summary>
    /// EN: Populates the CPU type ComboBox with all supported Siemens PLC types.
    /// TR: CPU tipi ComboBox'�n� desteklenen t�m Siemens PLC tipleriyle doldurur.
    /// </summary>
    private void InitializeCpuTypes()
    {
        // S7.Net k�t�phanesindeki t�m CPU tiplerini ekle
        CmbCpuType.Items.Add(new CpuTypeItem("S7-1200", CpuType.S71200));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-1500", CpuType.S71500));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-300", CpuType.S7300));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-400", CpuType.S7400));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-200", CpuType.S7200));
        CmbCpuType.Items.Add(new CpuTypeItem("S7-200 Smart", CpuType.S7200Smart));

        CmbCpuType.SelectedIndex = 0; // Varsay�lan: S7-1200
    }

    /// <summary>
    /// EN: Loads the saved connection settings from disk and applies them to the UI.
    /// TR: Kaydedilmi� ba�lant� ayarlar�n� diskten y�kler ve aray�ze uygular.
    /// </summary>
    private void LoadConnectionSettings()
    {
        try
        {
            var settings = ConnectionSettings.Load();

            // Kaydedilen temay� uygula
            App.SetNamedTheme(settings.Theme);

            if (settings.IsValid())
            {
                // CPU tipini se�
                for (int i = 0; i < CmbCpuType.Items.Count; i++)
                {
                    var item = (CpuTypeItem)CmbCpuType.Items[i];
                    if (item.DisplayName == settings.CpuType)
                    {
                        CmbCpuType.SelectedIndex = i;
                        break;
                    }
                }

                // IP adresi ve Port'u y�kle
                TxtIpAddress.Text = settings.IpAddress;
                TxtPort.Text = settings.Port.ToString();

                // Rack ve Slot de�erlerini y�kle
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
    /// TR: Aray�zdeki ge�erli ba�lant� ayarlar�n� diske kaydeder.
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
    /// TR: Sembol adreslerini okuma/yazma ComboBox'lar�na yeniden y�kler ve sembol a�ac�n� yeniden olu�turur.
    /// </summary>
    private void LoadSymbolAddresses()
    {
        // Mevcut sembolleri ComboBox'lara y�kle
        CmbReadAddress.Items.Clear();
        CmbWriteAddress.Items.Clear();

        var symbols = _plcService.SymbolMapper.GetAllSymbols();
        _allSymbols = symbols.ToDictionary(s => s.Key, s => s.Value);

        foreach (var symbol in symbols.OrderBy(s => s.Key))
        {
            CmbReadAddress.Items.Add(symbol.Key);
            CmbWriteAddress.Items.Add(symbol.Key);
        }

        // E�er sembol varsa, ilk sembol� se�
        if (CmbReadAddress.Items.Count > 0)
        {
            CmbReadAddress.SelectedIndex = 0;
            CmbWriteAddress.SelectedIndex = 0;
        }

        // TreeView a�ac�n� olu�tur
        BuildSymbolTree();
    }

    /// <summary>
    /// EN: Builds the hierarchical symbol tree from the symbol map and binds it to the TreeView.
    /// TR: Sembol haritas�ndan hiyerar�ik sembol a�ac�n� olu�turur ve TreeView'a ba�lar.
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

                // Bir sonraki seviye i�in dictionary olu�tur
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
    /// TR: Se�ili a�a� d���m� de�i�ti�inde �a�r�l�r. ComboBox'lar� se�ili d���me g�re filtreler.
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
    /// TR: Sembol panelini animasyonla a��p kapatan butonu y�netir.
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
    /// TR: Okuma adresi ComboBox se�imi de�i�ti�inde �a�r�l�r. Fiziksel adres etiketini g�nceller.
    /// </summary>
    private void CmbReadAddress_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdatePhysicalAddress(CmbReadAddress, TxtReadPhysicalAddress);
    }

    /// <summary>
    /// EN: Called when the write address ComboBox selection changes. Updates the physical address label and value placeholder.
    /// TR: Yazma adresi ComboBox se�imi de�i�ti�inde �a�r�l�r. Fiziksel adres etiketini ve de�er yer tutucu�usunu g�nceller.
    /// </summary>
    private void CmbWriteAddress_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdatePhysicalAddress(CmbWriteAddress, TxtWritePhysicalAddress);
        UpdateWriteValuePlaceholder();
    }

    /// <summary>
    /// EN: Updates a physical address label based on the selected ComboBox item.
    /// TR: Se�ili ComboBox ��esine g�re fiziksel adres etiketini g�nceller.
    /// </summary>
    private void UpdatePhysicalAddress(System.Windows.Controls.ComboBox comboBox, System.Windows.Controls.TextBlock textBlock)
    {
        if (comboBox.SelectedItem is string selectedAddress && !string.IsNullOrEmpty(selectedAddress))
        {
            if (_allSymbols.TryGetValue(selectedAddress, out var symbolInfo))
            {
                textBlock.Text = $"?? {symbolInfo.PhysicalAddress}  �  {symbolInfo.DataType}";
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
    /// TR: Se�ili adresin veri tipine g�re yazma de�eri TextBox yer tutucu�usunu g�nceller.
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
    /// TR: PLC veri tipine g�re yazma de�eri alan� i�in uygun yer tutucu�u metni d�nd�r�r.
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
            "BYTE" or "USINT"           => "0 � 255",
            "SINT"                      => "-128 � 127",
            "WORD" or "UINT"            => "0 � 65535",
            "INT"                       => "-32768 � 32767",
            "DWORD" or "UDINT"          => "0 � 4294967295",
            "DINT"                      => "-2147483648 � 2147483647",
            "REAL"                      => L.T("Placeholder_Write_Real"),
            "LWORD" or "ULINT"          => "0 � 18446744073709551615",
            "LINT"                      => "-9223372036854775808 � 9223372036854775807",
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
    /// TR: Bir ComboBox'� se�ili a�a� d���m� alt�ndaki adresleri g�sterecek �ekilde filtreler.
    /// </summary>
    private void FilterComboBoxByNode(System.Windows.Controls.ComboBox comboBox, SymbolTreeNode node)
    {
        comboBox.Items.Clear();

        if (node.IsLeaf)
        {
            // E�er leaf ise, sadece o adresi ekle ve se�
            comboBox.Items.Add(node.FullPath);
            comboBox.SelectedIndex = 0;
        }
        else
        {
            // E�er parent node ise, alt�ndaki t�m leaf'leri ekle
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
    /// TR: Verilen a�a� d���m� alt�ndaki t�m yaprak d���m tam yollar�n� �zyineli olarak toplar.
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
    /// TR: IP adresi metin kutusu giri�ini y�netir, yaln�zca rakam ve noktaya izin verir.
    /// </summary>
    private void TxtIpAddress_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Sadece rakam ve nokta girilmesine izin ver
        var regex = new Regex(@"[^0-9.]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    /// <summary>
    /// EN: Validates the IP address when the text box loses focus.
    /// TR: Metin kutusu odak kaybetti�inde IP adresini do�rular.
    /// </summary>
    private void TxtIpAddress_LostFocus(object sender, RoutedEventArgs e)
    {
        ValidateIpAddress();
    }

    /// <summary>
    /// EN: Validates the IP address field against the IPv4 regex pattern.
    /// TR: IP adresi alan�n� IPv4 regex kal�b�na g�re do�rular.
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
    /// TR: IP adresi TextBox'�n g�rsel do�rulama durumunu ayarlar (kenar rengi ve ipucu).
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
    /// TR: PLC durum de�i�ikli�i olaylar�n� y�netir. Mesaj� UI thread'e iletir ve loga ekler.
    /// </summary>
    private void OnPlcStatusChanged(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            AddLog(message);
            // StatusBar sadece ba�lant� durumu i�in kullan�l�yor
        });
    }

    /// <summary>
    /// EN: Handles the Connect/Disconnect/Cancel button click. Manages connection state transitions.
    /// TR: Ba�lan/Kes/�ptal buton t�klamas�n� y�netir. Ba�lant� durumu ge�i�lerini y�netir.
    /// </summary>
    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        // E�er ba�l�ysa, ba�lant�y� kes
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
            CmbCpuType.IsEnabled = true;
            TxtIpAddress.IsEnabled = true;
            TxtPort.IsEnabled = true;
            TxtRack.IsEnabled = true;
            TxtSlot.IsEnabled = true;

            TxtStatusBar.Text = L.T("StatusBar_NotConnected");
            return;
        }

        // E�er vazge� modundaysa, ba�lant�y� iptal et
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
                CmbCpuType.IsEnabled = false;
                TxtIpAddress.IsEnabled = false;
                TxtPort.IsEnabled = false;
                TxtRack.IsEnabled = false;
                TxtSlot.IsEnabled = false;

                var port = int.Parse(TxtPort.Text);
                TxtStatusBar.Text = L.T("StatusBar_Connected", selectedCpu.DisplayName,
                    _plcService.ConnectedIpAddress, port,
                    _plcService.ConnectedRack, _plcService.ConnectedSlot);

                // Ba�lant� ayarlar�n� kaydet
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
            TxtStatus.Text = "Hata olu�tu";
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
    /// TR: Okuma ComboBox'�nda girilen adresten PLC de�erini okur.
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
    /// TR: Yazma ComboBox'�nda se�ilen adrese PLC'ye girilen de�eri yazar.
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

            // Veri tipine g�re uygun d�n���m yap
            if (symbolInfo != null && !string.IsNullOrEmpty(symbolInfo.DataType))
            {
                var dataType = symbolInfo.DataType.ToUpperInvariant();

                // STRING[xx] ve WSTRING[xx] format�n� kontrol et
                if (dataType.StartsWith("STRING") || dataType.StartsWith("WSTRING"))
                {
                    value = valueText; // String olarak direkt kullan
                }
                else if (dataType == "CHAR" || dataType == "WCHAR")
                {
                    value = valueText; // PlcService'te byte/ushort'a d�n��t�r�lecek
                }
                else if (dataType == "BOOL")
                {
                    // Bool i�in: "true", "false", "0", "1" kabul et
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
                    value = valueText; // PlcService'te uygun tipe d�n��t�r�lecek
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
                    // Bilinmeyen tip, ak�ll� tahmin et
                    value = SmartParseValue(valueText);
                }
            }
            else
            {
                // Sembol bilgisi yoksa, ak�ll� tahmin et
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
    /// De�eri ak�ll�ca parse eder (tip bilinmiyorsa)
    /// </summary>
    /// <summary>
    /// EN: Smartly parses a string value to the most appropriate .NET type (bool, double, int, or string).
    /// TR: String de�eri en uygun .NET tipine (bool, double, int veya string) d�n��t�r�r.
    /// </summary>
    private object SmartParseValue(string valueText)
    {
        // Bool kontrol�
        if (bool.TryParse(valueText, out var boolVal))
            return boolVal;

        // Say� kontrol�
        if (valueText.Contains(".") || valueText.Contains(","))
        {
            // Ondal�kl� say�
            if (double.TryParse(valueText.Replace(',', '.'), System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
                return doubleVal;
        }
        else
        {
            // Tam say�
            if (int.TryParse(valueText, out var intVal))
                return intVal;
        }

        // Hi�biri de�ilse string olarak kabul et
        return valueText;
    }

    /// <summary>
    /// EN: Appends a timestamped message to the log panel.
    /// TR: Log paneline zaman damgal� mesaj ekler.
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

            // ComboBox'lar� yeniden y�kle
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
    /// Tema se�im men�s�n� a�ar
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
    /// Tema men�s�nden se�im yap�ld���nda �a�r�l�r
    /// </summary>
    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;

        var themeName = mi.Tag?.ToString() ?? "Light";

        // Di�er item'lar�n i�aretini kald�r
        foreach (MenuItem item in BtnToggleTheme.ContextMenu.Items)
            item.IsChecked = item == mi;

        App.SetNamedTheme(themeName);
        AddLog(L.T("Log_ThemeChanged", mi.Header));
    }

    /// <summary>
    /// Aktif temay� ayarlar dosyas�na kaydeder
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
    /// Ba�lant� ayarlar�n� g�ster/gizle butonu
    /// </summary>
    private void BtnToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        ConnectionSettingsPopup.IsOpen = !ConnectionSettingsPopup.IsOpen;
    }

    /// <summary>
    /// Ba�lant� ayarlar�n� kapat butonu
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

    // ComboBox i�in yard�mc� s�n�f
    /// <summary>
    /// EN: Helper class for displaying CPU types in the ComboBox.
    /// TR: ComboBox'ta CPU tiplerini g�stermek i�in yard�mc� s�n�f.
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
/// TR: Sembolik adresleri TreeView'da g�stermek i�in a�a� node modeli.
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