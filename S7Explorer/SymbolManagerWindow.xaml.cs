using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace S7Explorer;

/// <summary>
/// EN: Symbol Manager window. Allows adding, editing, deleting, filtering, sorting,
///     importing and exporting PLC symbolic address definitions.
/// TR: Sembol Yöneticisi penceresi. PLC sembolik adres tanımlamalarını ekleme, düzenleme,
///     silme, filtreleme, sıralama, içeri/dışa aktarma işlemlerini sağlar.
/// </summary>
public partial class SymbolManagerWindow : Window
{
    private static LocalizationManager L => LocalizationManager.Instance;
    private EventHandler? _languageChangedHandler;

    private readonly SymbolMapper _symbolMapper;
    private readonly ObservableCollection<SymbolEntry> _symbols;
    private ICollectionView _symbolsView;

    // Filtre değerleri
    private string _filterSymbolicName = string.Empty;
    private string _filterPhysicalAddress = string.Empty;
    private string _filterDataType = string.Empty;
    private string _filterDefaultValue = string.Empty;
    private string _filterDescription = string.Empty;
    private string _searchText = string.Empty;

    // Filtre etkinlik göstergesi
    private readonly Dictionary<string, Button> _filterButtonsByColumn = new();

    /// <summary>
    /// EN: Initializes the Symbol Manager window with the provided symbol mapper.
    /// TR: Sembol Yöneticisi penceresini verilen sembol eşleyici ile başlatır.
    /// </summary>
    /// <param name="symbolMapper">EN: The symbol mapper to manage. TR: Yönetilecek sembol eşleyici.</param>
    public SymbolManagerWindow(SymbolMapper symbolMapper)
    {
        InitializeComponent();
        _symbolMapper = symbolMapper;
        _symbols = new ObservableCollection<SymbolEntry>();

        LoadSymbols();

        _symbolsView = CollectionViewSource.GetDefaultView(_symbols);
        _symbolsView.Filter = FilterSymbols;
        DgSymbols.ItemsSource = _symbolsView;

        _languageChangedHandler = (_, _) => ApplyLanguage();
        L.LanguageChanged += _languageChangedHandler;
        InitializeFilterIndicatorMap();
        InitializeLanguageMenu();
        ApplyLanguage();
    }

    /// <summary>
    /// EN: Initializes map between filter columns and header toggle buttons.
    /// TR: Filtre sütunları ile başlık açma-kapama butonları arasındaki eşlemeyi başlatır.
    /// </summary>
    private void InitializeFilterIndicatorMap()
    {
        _filterButtonsByColumn["SymbolicName"] = BtnFilterSymbolicName;
        _filterButtonsByColumn["PhysicalAddress"] = BtnFilterPhysicalAddress;
        _filterButtonsByColumn["DataType"] = BtnFilterDataType;
        _filterButtonsByColumn["DefaultValue"] = BtnFilterDefaultValue;
        _filterButtonsByColumn["Description"] = BtnFilterDescription;
    }

    /// <summary>
    /// EN: Applies the active language to all UI elements in this window.
    /// TR: Aktif dili bu penceredeki tüm arayüz öğelerine uygular.
    /// </summary>
    private void ApplyLanguage()
    {
        Title = L.T("Win_SymbolMgr");
        LblSymbolMgrHeader.Text = L.T("SymbolMgr_Header");
        LblSearch.Text = L.T("SymbolMgr_SearchLabel");
        TxtFilterHint.Text = L.T("SymbolMgr_FilterHint", "Column filters are available from header arrows.");
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(TxtSearch, L.T("SymbolMgr_SearchPlaceholder"));
        ColHdrSymbolicName.Text = L.T("Col_SymbolicName");
        ColHdrPhysicalAddress.Text = L.T("Col_PhysicalAddress");
        ColHdrDataType.Text = L.T("Col_DataType");
        ColHdrDefaultValue.Text = L.T("Col_DefaultValue");
        ColHdrDescription.Text = L.T("Col_Description");
        TxtFilterSymbolicName.ToolTip = L.T("Filter_SymbolicName_Tooltip");
        TxtFilterPhysicalAddress.ToolTip = L.T("Filter_PhysicalAddress_Tooltip");
        TxtFilterDataType.ToolTip = L.T("Filter_DataType_Tooltip");
        TxtFilterDefaultValue.ToolTip = L.T("Filter_DefaultValue_Tooltip");
        TxtFilterDescription.ToolTip = L.T("Filter_Description_Tooltip");
        LblFileOps.Text = L.T("Label_FileOps");
        LblMainFilePrefix.Text = L.T("Label_MainFile");
        TxtBtnResetSort.Text = L.T("Btn_ResetSort");
        TxtBtnClearFilters.Text = L.T("Btn_ClearFilters");
        TxtBtnSelectAll.Text = L.T("Btn_SelectAll");
        BtnSelectAll.ToolTip = L.T("Btn_SelectAll_Tooltip");
        TxtBtnDeleteSelected.Text = L.T("Btn_DeleteSelected");
        TxtBtnParseDb.Text = L.T("Btn_ParseDb");
        TxtBtnLoadCsv.Text = L.T("Btn_LoadJson");
        TxtBtnSaveCsv.Text = L.T("Btn_SaveJson");
        TxtBtnSave.Text = L.T("Btn_Save");
        TxtBtnCancel.Text = L.T("Btn_Cancel");
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(TxtNewSymbolicName, L.T("SymbolMgr_NewSymbolicPlaceholder", "Symbolic name (required)"));
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(TxtNewPhysicalAddress, L.T("SymbolMgr_NewPhysicalPlaceholder", "Physical address (required)"));
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(TxtNewDataType, L.T("SymbolMgr_NewTypePlaceholder", "Type"));
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(TxtNewDefaultValue, L.T("SymbolMgr_NewDefaultPlaceholder", "Default"));
        ModernWpf.Controls.Primitives.ControlHelper.SetPlaceholderText(TxtNewDescription, L.T("SymbolMgr_NewDescriptionPlaceholder", "Description"));
        MiSave.Header = L.T("Btn_Save");
        MiSaveAndClose.Header = L.T("Btn_SaveAndClose");
        UpdateThemeMenuHeaders();
        UpdateFilterSummary();
    }

    /// <summary>
    /// EN: Updates theme menu item headers with the current language strings.
    /// TR: Tema menü öğelerinin başlıklarını geçerli dil metinleriyle günceller.
    /// </summary>
    private void UpdateThemeMenuHeaders()
    {
        if (BtnToggleTheme.ContextMenu == null)
            return;

        foreach (MenuItem item in BtnToggleTheme.ContextMenu.Items)
        {
            item.Header = item.Tag?.ToString() switch
            {
                "Light" => L.T("Theme_Light"),
                "Dark" => L.T("Theme_Dark"),
                "Industrial" => L.T("Theme_Industrial"),
                "Night" => L.T("Theme_Night"),
                _ => item.Header
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
    /// EN: Called when a language menu item is clicked. Changes the active application language.
    /// TR: Dil menü öğesi tıklandığında çağrılır. Aktif uygulama dilini değiştirir.
    /// </summary>
    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var code = mi.Tag?.ToString() ?? "en-US";
        foreach (MenuItem item in BtnToggleLanguage.ContextMenu.Items)
            item.IsChecked = item == mi;
        App.SetLanguage(code);
    }

    /// <summary>
    /// EN: Called when the window is closed. Unsubscribes from the language changed event.
    /// TR: Pencere kapandığında çağrılır. Dil değişikliği olayından aboneyi kaldırır.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        if (_languageChangedHandler != null)
            L.LanguageChanged -= _languageChangedHandler;
        base.OnClosed(e);
    }

    /// <summary>
    /// EN: Loads all symbols from the mapper into the DataGrid's observable collection.
    /// TR: Sembol eşleyicideki tüm sembolleri DataGrid'in gözlemlenebilir koleksiyonuna yükler.
    /// </summary>
    private void LoadSymbols()
    {
        _symbols.Clear();
        var index = 0;
        foreach (var symbol in _symbolMapper.GetAllSymbols())
        {
            _symbols.Add(new SymbolEntry
            {
                OriginalIndex = index++,
                SymbolicName = symbol.Key,
                PhysicalAddress = symbol.Value.PhysicalAddress,
                DataType = symbol.Value.DataType,
                DefaultValue = symbol.Value.DefaultValue,
                Description = symbol.Value.Description
            });
        }
        UpdateFilterComboSources();
        _symbolsView?.Refresh();
        UpdateFilterSummary();
    }

    /// <summary>
    /// EN: Keeps header filter controls consistent after data changes.
    /// TR: Veri değişikliklerinden sonra başlık filtre kontrollerini tutarlı tutar.
    /// </summary>
    private void UpdateFilterComboSources()
    {
        // Header filters are text boxes now; no list source is required.
    }

    /// <summary>
    /// EN: Updates the filter summary text (visible/total) for the toolbar.
    /// TR: Araç çubuğundaki filtre özet metnini (görünen/toplam) günceller.
    /// </summary>
    private void UpdateFilterSummary()
    {
        var visibleCount = _symbolsView?.Cast<object>().Count() ?? _symbols.Count;
        var activeFilterCount = GetActiveFilterCount();
        TxtFilterSummary.Text = activeFilterCount > 0
            ? $"{visibleCount} / {_symbols.Count} • F:{activeFilterCount}"
            : $"{visibleCount} / {_symbols.Count}";

        UpdateFilterIndicators();
    }

    /// <summary>
    /// EN: Returns number of active filters including global search.
    /// TR: Genel arama dahil etkin filtre sayısını döndürür.
    /// </summary>
    private int GetActiveFilterCount()
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(_searchText)) count++;
        if (!string.IsNullOrWhiteSpace(_filterSymbolicName)) count++;
        if (!string.IsNullOrWhiteSpace(_filterPhysicalAddress)) count++;
        if (!string.IsNullOrWhiteSpace(_filterDataType)) count++;
        if (!string.IsNullOrWhiteSpace(_filterDefaultValue)) count++;
        if (!string.IsNullOrWhiteSpace(_filterDescription)) count++;
        return count;
    }

    /// <summary>
    /// EN: Updates header filter button visuals for active/inactive states.
    /// TR: Başlık filtre butonlarının aktif/pasif görsel durumlarını günceller.
    /// </summary>
    private void UpdateFilterIndicators()
    {
        SetFilterIndicator("SymbolicName", !string.IsNullOrWhiteSpace(_filterSymbolicName));
        SetFilterIndicator("PhysicalAddress", !string.IsNullOrWhiteSpace(_filterPhysicalAddress));
        SetFilterIndicator("DataType", !string.IsNullOrWhiteSpace(_filterDataType));
        SetFilterIndicator("DefaultValue", !string.IsNullOrWhiteSpace(_filterDefaultValue));
        SetFilterIndicator("Description", !string.IsNullOrWhiteSpace(_filterDescription));
    }

    private void SetFilterIndicator(string column, bool isActive)
    {
        if (!_filterButtonsByColumn.TryGetValue(column, out var button))
            return;

        button.Opacity = isActive ? 1.0 : 0.8;
        button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        button.ToolTip = isActive
            ? $"{L.T("Btn_ClearFilters")} • {column}"
            : L.T("Filter_DataType_Tooltip");
    }

    /// <summary>
    /// EN: Filtering predicate for the symbols collection view. Applies both global search and per-column filters.
    /// TR: Sembol koleksiyon görünümü için filtreleme yöntemi. Genel arama ve sütun bazlı filtreleri uygular.
    /// </summary>
    private bool FilterSymbols(object obj)
    {
        if (obj is not SymbolEntry symbol)
            return false;

        // Genel arama (tüm sütunlarda)
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.ToLower();
            if (!symbol.SymbolicName.ToLower().Contains(search) &&
                !symbol.PhysicalAddress.ToLower().Contains(search) &&
                !symbol.DataType.ToLower().Contains(search) &&
                !symbol.DefaultValue.ToLower().Contains(search) &&
                !symbol.Description.ToLower().Contains(search))
            {
                return false;
            }
        }

        // Sütun bazlı filtreler
        if (!string.IsNullOrWhiteSpace(_filterSymbolicName) &&
            !symbol.SymbolicName.Contains(_filterSymbolicName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterPhysicalAddress) &&
            !symbol.PhysicalAddress.Contains(_filterPhysicalAddress, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterDataType) &&
            !symbol.DataType.Contains(_filterDataType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterDefaultValue) &&
            !symbol.DefaultValue.Contains(_filterDefaultValue, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterDescription) &&
            !symbol.Description.Contains(_filterDescription, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// EN: Called when the global search box text changes. Refreshes the symbol view.
    /// TR: Genel arama kutusu metni değiştiğinde çağrılır. Sembol görünümünü yeniler.
    /// </summary>
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = TxtSearch.Text;
        _symbolsView.Refresh();
        UpdateFilterSummary();
    }

    /// <summary>
    /// EN: Auto-fills the default value field based on the selected PLC data type.
    /// TR: Seçilen PLC veri tipine göre varsayılan değer alanını otomatik doldurur.
    /// </summary>
    private void TxtNewDataType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyDefaultValueForDataType();
    }

    /// <summary>
    /// EN: Applies default value after manual text entry in the editable data type ComboBox.
    /// TR: Düzenlenebilir veri tipi ComboBox'ına elle yazım sonrası varsayılan değeri uygular.
    /// </summary>
    private void TxtNewDataType_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyDefaultValueForDataType();
    }

    /// <summary>
    /// EN: Resolves and writes the default value according to the currently entered data type.
    /// TR: Girilen veri tipine göre varsayılan değeri belirler ve yazar.
    /// </summary>
    private void ApplyDefaultValueForDataType()
    {
        var selectedType = TxtNewDataType.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(selectedType))
            return;

        var resolvedDefault = selectedType switch
        {
            "BOOL" => "false",
            "BYTE" or "WORD" or "DWORD" or "LWORD" => "0",
            "SINT" or "USINT" or "INT" or "UINT" or "DINT" or "UDINT" or "LINT" or "ULINT" => "0",
            "REAL" or "LREAL" => "0.0",
            "CHAR" => "A",
            "WCHAR" => "A",
            "TIME" => "T#0ms",
            "DATE" => "D#1970-01-01",
            "TIME_OF_DAY" or "TOD" => "TOD#00:00:00",
            "DATE_AND_TIME" or "DT" => "DT#1970-01-01-00:00:00",
            "DTL" => "DTL#1970-01-01-00:00:00",
            _ when selectedType.StartsWith("STRING") => string.Empty,
            _ when selectedType.StartsWith("WSTRING") => string.Empty,
            _ => null
        };

        if (resolvedDefault != null)
            TxtNewDefaultValue.Text = resolvedDefault;
    }

    /// <summary>
    /// EN: Toggles the visibility of a column filter text box under the clicked header.
    /// TR: Tıklanan başlığın altındaki sütun filtre metin kutusunun görünürlüğünü değiştirir.
    /// </summary>
    private void HeaderFilterToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var targetName = button.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(targetName))
            return;

        var target = targetName switch
        {
            "TxtFilterSymbolicName" => TxtFilterSymbolicName,
            "TxtFilterPhysicalAddress" => TxtFilterPhysicalAddress,
            "TxtFilterDataType" => TxtFilterDataType,
            "TxtFilterDefaultValue" => TxtFilterDefaultValue,
            "TxtFilterDescription" => TxtFilterDescription,
            _ => null
        };

        if (target == null)
            return;

        target.Visibility = target.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (target.Visibility == Visibility.Visible)
            target.Focus();
    }

    /// <summary>
    /// EN: Called when a column filter text box changes. Updates the corresponding filter and refreshes the view.
    /// TR: Sütun filtre kutusu değiştiğinde çağrılır. İlgili filtreyi günceller ve görünümü yeniler.
    /// </summary>
    private void ColumnFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var filterValue = textBox.Text;
        var columnName = textBox.Tag?.ToString();

        switch (columnName)
        {
            case "SymbolicName":
                _filterSymbolicName = filterValue;
                break;
            case "PhysicalAddress":
                _filterPhysicalAddress = filterValue;
                break;
            case "DataType":
                _filterDataType = filterValue;
                break;
            case "DefaultValue":
                _filterDefaultValue = filterValue;
                break;
            case "Description":
                _filterDescription = filterValue;
                break;
        }

        _symbolsView.Refresh();
        UpdateFilterSummary();
    }

    /// <summary>
    /// EN: Clears all column and global search filters.
    /// TR: Tüm sütun ve genel arama filtrelerini temizler.
    /// </summary>
    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = string.Empty;
        TxtFilterSymbolicName.Text = string.Empty;
        TxtFilterPhysicalAddress.Text = string.Empty;
        TxtFilterDataType.Text = string.Empty;
        TxtFilterDefaultValue.Text = string.Empty;
        TxtFilterDescription.Text = string.Empty;

        _searchText = string.Empty;
        _filterSymbolicName = string.Empty;
        _filterPhysicalAddress = string.Empty;
        _filterDataType = string.Empty;
        _filterDefaultValue = string.Empty;
        _filterDescription = string.Empty;

        _symbolsView.Refresh();
        UpdateFilterSummary();
    }

    /// <summary>
    /// EN: Adds a new symbol row from manual input fields.
    /// TR: Manuel giriş alanlarından yeni bir sembol satırı ekler.
    /// </summary>
    private void BtnAddManualSymbol_Click(object sender, RoutedEventArgs e)
    {
        var symbolicName = TxtNewSymbolicName.Text.Trim();
        var physicalAddress = TxtNewPhysicalAddress.Text.Trim();

        if (string.IsNullOrWhiteSpace(symbolicName) || string.IsNullOrWhiteSpace(physicalAddress))
        {
            MessageDialog.Show("Symbolic name and physical address are required.",
                L.T("MsgTitle_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        var exists = _symbols.Any(x => string.Equals(x.SymbolicName, symbolicName, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            MessageDialog.Show($"A symbol named '{symbolicName}' already exists.",
                L.T("MsgTitle_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        var nextIndex = _symbols.Count == 0 ? 0 : _symbols.Max(x => x.OriginalIndex) + 1;
        _symbols.Add(new SymbolEntry
        {
            OriginalIndex = nextIndex,
            SymbolicName = symbolicName,
            PhysicalAddress = physicalAddress,
            DataType = TxtNewDataType.Text.Trim(),
            DefaultValue = TxtNewDefaultValue.Text.Trim(),
            Description = TxtNewDescription.Text.Trim()
        });

        var newlyAdded = _symbols.LastOrDefault();
        if (newlyAdded != null)
        {
            DgSymbols.SelectedItem = newlyAdded;
            DgSymbols.ScrollIntoView(newlyAdded);
        }

        TxtNewSymbolicName.Text = string.Empty;
        TxtNewPhysicalAddress.Text = string.Empty;
        TxtNewDataType.Text = string.Empty;
        TxtNewDefaultValue.Text = string.Empty;
        TxtNewDescription.Text = string.Empty;

        _symbolsView.Refresh();
        UpdateFilterSummary();
    }

    /// <summary>
    /// EN: Clears all sort descriptors and returns the table to its original insertion order.
    /// TR: Tüm sıralama tanımlarını temizler ve tabloyu orijinal ekleme sırasına döndürür.
    /// </summary>
    private void BtnResetSort_Click(object sender, RoutedEventArgs e)
    {
        // Tüm sıralamaları temizle
        _symbolsView.SortDescriptions.Clear();

        // Orijinal index'e göre sırala
        _symbolsView.SortDescriptions.Add(new System.ComponentModel.SortDescription("OriginalIndex", System.ComponentModel.ListSortDirection.Ascending));

        _symbolsView.Refresh();

        MessageDialog.Show(L.T("Msg_ResetSort"), L.T("MsgTitle_Info"),
            MessageBoxButton.OK, MessageBoxImage.Information, this);
    }

    /// <summary>
    /// EN: Selects all visible rows in the DataGrid.
    /// TR: DataGrid'deki tüm görünen satırları seçer.
    /// </summary>
    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        DgSymbols.SelectAll();
        DgSymbols.Focus();
    }

    /// <summary>
    /// EN: Handles Ctrl+A to select all records when focus is not in a TextBox.
    /// TR: TextBox dışında odak varken Ctrl+A ile tüm kayıtları seçer.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.A &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 &&
            !(e.OriginalSource is System.Windows.Controls.TextBox))
        {
            DgSymbols.SelectAll();
            DgSymbols.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// EN: Deletes the selected rows from the symbol list after user confirmation.
    /// TR: Kullanıcı onayının ardından seçili satırları sembol listesinden siler.
    /// </summary>
    private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DgSymbols.SelectedItems.Count == 0)
        {
            MessageDialog.Show(L.T("Msg_NoSelection"), L.T("MsgTitle_Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        var result = MessageDialog.Show(
            L.T("Msg_DeleteConfirm", DgSymbols.SelectedItems.Count),
            L.T("MsgTitle_DeleteConfirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question, this);

        if (result == MessageBoxResult.Yes)
        {
            var itemsToRemove = DgSymbols.SelectedItems.OfType<SymbolEntry>().ToList();
            foreach (var item in itemsToRemove)
            {
                _symbols.Remove(item);
            }

            UpdateFilterComboSources();
            _symbolsView.Refresh();
            UpdateFilterSummary();
        }
    }

    /// <summary>
    /// EN: Saves all symbols from the DataGrid to the symbol mapper.
    /// TR: DataGrid'deki tüm sembolleri sembol eşleyiciye kaydeder.
    /// </summary>
    private bool SaveSymbols()
    {
        try
        {
            // Mevcut sembolleri temizle
            _symbolMapper.Clear();

            // DataGrid'deki sembolleri ekle
            foreach (var symbol in _symbols)
            {
                if (!string.IsNullOrWhiteSpace(symbol.SymbolicName) &&
                    !string.IsNullOrWhiteSpace(symbol.PhysicalAddress))
                {
                    _symbolMapper.AddSymbol(symbol.SymbolicName, symbol.PhysicalAddress, 
                        symbol.DataType ?? string.Empty,
                        symbol.DefaultValue ?? string.Empty,
                        symbol.Description ?? string.Empty);
                }
            }

            // Otomatik olarak symbols.json dosyasına kaydet
            _symbolMapper.Save();

            MessageDialog.Show(L.T("Msg_SaveSuccess"), L.T("MsgTitle_Success"),
                MessageBoxButton.OK, MessageBoxImage.Information, this);
            return true;
        }
        catch (Exception ex)
        {
            MessageDialog.Show(L.T("Msg_SaveError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
            return false;
        }
    }

    /// <summary>
    /// EN: Executes default save action (save only).
    /// TR: Varsayılan kaydet eylemini çalıştırır (sadece kaydet).
    /// </summary>
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveSymbols();
    }

    /// <summary>
    /// EN: Opens the save options context menu.
    /// TR: Kaydet seçenekleri bağlam menüsünü açar.
    /// </summary>
    private void BtnSaveOptions_Click(object sender, RoutedEventArgs e)
    {
        var menu = BtnSaveOptions.ContextMenu;
        menu.PlacementTarget = BtnSave;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Width = double.NaN;
        menu.MinWidth = BtnSave.ActualWidth + BtnSaveOptions.ActualWidth;
        menu.HorizontalOffset = 0;
        menu.IsOpen = true;
    }

    /// <summary>
    /// EN: Saves symbols without closing the window.
    /// TR: Pencereyi kapatmadan sembolleri kaydeder.
    /// </summary>
    private void MiSave_Click(object sender, RoutedEventArgs e)
    {
        SaveSymbols();
    }

    /// <summary>
    /// EN: Saves symbols and closes the window.
    /// TR: Sembolleri kaydeder ve pencereyi kapatır.
    /// </summary>
    private void MiSaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveSymbols())
            return;

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// EN: Cancels all changes and closes the Symbol Manager window.
    /// TR: Tüm değişikliklerden vazgeçer ve Sembol Yöneticisi penceresini kapatır.
    /// </summary>
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// EN: Opens a file dialog to load symbols from a JSON file into the symbol mapper.
    /// TR: JSON dosyasından sembolleri sembol eşleyiciye yüklemek için dosya dialogı açar.
    /// </summary>
    private void BtnLoadCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = L.T("FileFilter_Json"),
                Title = L.T("Dialog_LoadJson_Title"),
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                _symbolMapper.LoadFromJson(dialog.FileName);
                LoadSymbols();

                MessageDialog.Show(L.T("Msg_LoadSuccess", _symbols.Count),
                    L.T("MsgTitle_Success"), MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Show(L.T("Msg_LoadError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    /// <summary>
    /// EN: Opens a file dialog to export all symbols to a JSON file.
    /// TR: Tüm sembolleri JSON dosyasına dışa aktarmak için dosya dialogı açar.
    /// </summary>
    private void BtnSaveCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = L.T("FileFilter_Json"),
                Title = L.T("Dialog_SaveJson_Title"),
                FileName = L.T("Dialog_SaveJson_DefaultName"),
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                _symbolMapper.Clear();
                foreach (var symbol in _symbols)
                {
                    if (!string.IsNullOrWhiteSpace(symbol.SymbolicName) &&
                        !string.IsNullOrWhiteSpace(symbol.PhysicalAddress))
                    {
                        _symbolMapper.AddSymbol(symbol.SymbolicName, symbol.PhysicalAddress,
                            symbol.DataType ?? string.Empty,
                            symbol.DefaultValue ?? string.Empty,
                            symbol.Description ?? string.Empty);
                    }
                }

                _symbolMapper.SaveToJson(dialog.FileName);

                MessageDialog.Show(L.T("Msg_SaveJsonSuccess", dialog.FileName),
                    L.T("MsgTitle_Success"),
                    MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Show(L.T("Msg_SaveJsonError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    /// <summary>
    /// EN: Opens a file dialog to select a Siemens DB file, parses it, generates JSON outputs,
    ///     and optionally imports the resulting symbols into the current symbol map.
    /// TR: Siemens DB dosyasını seçmek için dosya dialogı açar, dosyayı ayrıştırır, JSON çıktıları oluşturur
    ///     ve isteğe bağlı olarak sonuçtaki sembolleri geçerli sembol haritasına aktarır.
    /// </summary>
    private void BtnParseDb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = L.T("FileFilter_Db"),
                Title = L.T("Dialog_SelectDb_Title"),
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var parser = new DbParser();
                var result = parser.Parse(openFileDialog.FileName);

                // JSON dosyası oluştur (orijinal yapı)
                var jsonFileName = Path.ChangeExtension(openFileDialog.FileName, ".json");
                var jsonContent = parser.ToJson(result, true);
                File.WriteAllText(jsonFileName, jsonContent);

                // Hiyerarşik JSON dosyası oluştur (struct yapısını koruyarak)
                var hierarchicalJsonFileName = Path.ChangeExtension(openFileDialog.FileName, "_hierarchical.json");

                // DB numarasını al
                var dbNumberDialog = new DbNumberInputDialog();
                if (dbNumberDialog.ShowDialog() == true)
                {
                    var dbNumber = dbNumberDialog.DbNumber;

                    // Hiyerarşik JSON oluştur
                    var hierarchicalJsonContent = parser.ToHierarchicalJson(result, dbNumber, true);
                    File.WriteAllText(hierarchicalJsonFileName, hierarchicalJsonContent);

                    // Düz sembol haritası oluştur (detaylı bilgi ile)
                    var symbolMapWithInfo = parser.GenerateSymbolMapWithInfo(result, dbNumber);

                    // Sembol haritasını JSON olarak kaydet
                    var symbolsJsonFileName = Path.ChangeExtension(openFileDialog.FileName, "_symbols.json");
                    var fullSymbolMapWithInfo = new Dictionary<string, SymbolInfo>();
                    foreach (var symbol in symbolMapWithInfo)
                    {
                        fullSymbolMapWithInfo[$"{result.DbName}.{symbol.Key}"] = symbol.Value;
                    }

                    var symbolsJsonContent = System.Text.Json.JsonSerializer.Serialize(fullSymbolMapWithInfo, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    File.WriteAllText(symbolsJsonFileName, symbolsJsonContent);

                    // Sembolleri mevcut haritaya ekle
                    var addSymbolsResult = MessageDialog.Show(
                        L.T("Msg_ParseDbAddSymbols", symbolMapWithInfo.Count, result.DbName, "Receive.Control.Life", dbNumber),
                        L.T("MsgTitle_ParseDbAdd"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question, this);

                    if (addSymbolsResult == MessageBoxResult.Yes)
                    {
                        _symbolMapper.AddSymbols(fullSymbolMapWithInfo);
                        _symbolMapper.Save();
                        LoadSymbols(); // Yeni sembolleri DataGrid'e yükle

                        MessageDialog.Show(
                            L.T("Msg_ParseDbSuccess_Add", result.DbName,
                                Path.GetFileName(jsonFileName),
                                Path.GetFileName(hierarchicalJsonFileName),
                                Path.GetFileName(symbolsJsonFileName),
                                symbolMapWithInfo.Count),
                            L.T("MsgTitle_ParseDbSuccess"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information, this);
                    }
                    else
                    {
                        MessageDialog.Show(
                            L.T("Msg_ParseDbSuccess_NoAdd", result.DbName,
                                Path.GetFileName(jsonFileName),
                                Path.GetFileName(hierarchicalJsonFileName),
                                Path.GetFileName(symbolsJsonFileName),
                                symbolMapWithInfo.Count),
                            L.T("MsgTitle_ParseDbSuccess"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information, this);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Show(L.T("Msg_ParseDbError", ex.Message), L.T("MsgTitle_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    /// <summary>
    /// EN: Opens the theme selection context menu.
    /// TR: Tema seçim bağlam menüsünü açar.
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
    /// EN: Called when a theme is selected from the theme menu. Applies the selected theme.
    /// TR: Tema menüsünden tema seçildiğinde çağrılır. Seçilen temayı uygular.
    /// </summary>
    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var themeName = mi.Tag?.ToString() ?? "Light";
        foreach (MenuItem item in BtnToggleTheme.ContextMenu.Items)
            item.IsChecked = item == mi;
        App.SetNamedTheme(themeName);
    }

}

/// <summary>
/// EN: Symbol entry model for the DataGrid in the Symbol Manager window.
///     Represents a single PLC symbolic address definition.
/// TR: Sembol Yöneticisi penceresindeki DataGrid için sembol giriş modeli.
///     Tek bir PLC sembolik adres tanımlamasını temsil eder.
/// </summary>
public class SymbolEntry
{
    /// <summary>
    /// EN: Stores the original insertion index for reset-sort functionality.
    /// TR: Sıfalama sıfarlama işlevi için orijinal ekleme indeksini saklar.
    /// </summary>
    public int OriginalIndex { get; set; }

    /// <summary>
    /// EN: Symbolic name (alias) of the PLC address.
    /// TR: PLC adresinin sembolik adı (takma adı).
    /// </summary>
    public string SymbolicName { get; set; } = string.Empty;

    /// <summary>
    /// EN: Physical address on the PLC (e.g., DB1.DBD0).
    /// TR: PLC üzerindeki fiziksel adres (örn. DB1.DBD0).
    /// </summary>
    public string PhysicalAddress { get; set; } = string.Empty;

    /// <summary>
    /// EN: PLC data type of the address (e.g., BOOL, INT, REAL, STRING[20]).
    /// TR: Adresin PLC veri tipi (örn. BOOL, INT, REAL, STRING[20]).
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// EN: Optional default value for the address.
    /// TR: Adres için isteğe bağlı varsayılan değer.
    /// </summary>
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>
    /// EN: Optional human-readable description of the address.
    /// TR: Adresin isteğe bağlı okunabilir açıklaması.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
