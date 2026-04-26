using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace S7Explorer;

/// <summary>
/// EN: A simple dialog for entering a DB number.
/// TR: DB numarası girmek için basit bir dialog.
/// </summary>
public partial class DbNumberInputDialog : Window
{
    private static LocalizationManager L => LocalizationManager.Instance;
    private EventHandler? _languageChangedHandler;

    public int DbNumber { get; private set; } = 1;

    public DbNumberInputDialog()
    {
        InitializeComponent();
        TxtDbNumber.Focus();
        TxtDbNumber.SelectAll();

        _languageChangedHandler = (_, _) => ApplyLanguage();
        L.LanguageChanged += _languageChangedHandler;
        InitializeLanguageMenu();
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        Title = L.T("Win_DbNumber");
        LblDbPrompt.Text = L.T("DbDialog_Prompt");
        LblDbNumberLabel.Text = L.T("DbDialog_Label");
        BtnOk.Content = L.T("Btn_Ok");
        BtnCancel.Content = L.T("Btn_Cancel");
        LblDbDescription.Text = L.T("DbDialog_Description");
        LblDbExample.Text = L.T("DbDialog_Example");
    }

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

    private void BtnToggleLanguage_Click(object sender, RoutedEventArgs e)
    {
        var menu = BtnToggleLanguage.ContextMenu;
        foreach (MenuItem item in menu.Items)
            item.IsChecked = item.Tag?.ToString() == L.CurrentLanguageCode;
        menu.PlacementTarget = BtnToggleLanguage;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var code = mi.Tag?.ToString() ?? "en-US";
        foreach (MenuItem item in BtnToggleLanguage.ContextMenu.Items)
            item.IsChecked = item == mi;
        App.SetLanguage(code);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_languageChangedHandler != null)
            L.LanguageChanged -= _languageChangedHandler;
        base.OnClosed(e);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtDbNumber.Text, out var dbNumber) && dbNumber > 0)
        {
            DbNumber = dbNumber;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(L.T("Msg_InvalidDbNumber"),
                L.T("MsgTitle_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            TxtDbNumber.Focus();
            TxtDbNumber.SelectAll();
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TxtDbNumber_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnOk_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            BtnCancel_Click(sender, e);
        }
    }

    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var menu = BtnToggleTheme.ContextMenu;
        foreach (MenuItem item in menu.Items)
            item.IsChecked = item.Tag?.ToString() == App.CurrentThemeName;
        menu.PlacementTarget = BtnToggleTheme;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var themeName = mi.Tag?.ToString() ?? "Light";
        foreach (MenuItem item in BtnToggleTheme.ContextMenu.Items)
            item.IsChecked = item == mi;
        App.SetNamedTheme(themeName);
    }
}
