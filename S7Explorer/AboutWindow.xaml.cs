using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace S7Explorer;

/// <summary>
/// EN: "About" box: product name, running version, license and the project page.
///     The version shown here is the one the update check compares against GitHub.
/// TR: "Program hakkında" kutusu: ürün adı, çalışan sürüm, lisans ve proje sayfası.
///     Burada görünen sürüm, güncelleme kontrolünün GitHub ile karşılaştırdığı sürümdür.
/// </summary>
public partial class AboutWindow : Window
{
    private static LocalizationManager L => LocalizationManager.Instance;

    public AboutWindow(Window? owner = null)
    {
        InitializeComponent();
        Owner = owner ?? Application.Current?.MainWindow;

        Title = L.T("Win_About");
        TxtAboutVersion.Text = L.T("About_Version", UpdateChecker.CurrentVersionText);
        TxtAboutDescription.Text = L.T("About_Description");
        TxtAboutSafety.Text = L.T("About_Safety");
        TxtAboutLicense.Text = L.T("About_License");
        TxtRepositoryLink.Text = UpdateChecker.RepositoryUrl.Replace("https://", string.Empty);
        LinkRepository.NavigateUri = new Uri(UpdateChecker.RepositoryUrl);
        BtnClose.Content = L.T("Btn_Close");
    }

    /// <summary>
    /// EN: Opens the project page in the default browser.
    /// TR: Proje sayfasını varsayılan tarayıcıda açar.
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Tarayıcı açılamadıysa hakkında kutusu yüzünden uygulama düşmemeli.
        }
        e.Handled = true;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
