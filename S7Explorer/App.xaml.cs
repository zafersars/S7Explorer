using System.Windows;
using System.Windows.Media;
using ModernWpf;

namespace S7Explorer
{
    public partial class App : Application
    {
        /// <summary>
        /// EN: Active theme name (Light / Dark / Industrial / Night)
        /// TR: Aktif tema adý (Light / Dark / Industrial / Night)
        /// </summary>
        public static string CurrentThemeName { get; private set; } = "Light";

        protected override void OnStartup(StartupEventArgs e)
        {
            // ModernWPF 0.9.6 Türkçe kaynak hatasý için InvariantCulture kullan
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture =
                System.Globalization.CultureInfo.InvariantCulture;

            base.OnStartup(e);

            // Localization: lang klasörünü tara ve kaydedilen dili yükle
            LocalizationManager.Instance.Scan();
            var savedLanguage = ConnectionSettings.Load().Language;
            LocalizationManager.Instance.SetLanguage(savedLanguage);

            // Varsayýlan tema — LoadConnectionSettings() tarafýndan üzerine yazýlýr
            SetNamedTheme("Light");

            // Localization hazýr olduktan sonra ana pencereyi aç
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        /// <summary>
        /// EN: Applies the named theme. Light / Dark / Industrial / Night
        /// TR: Adlandýrýlmýþ temayý uygular. Light / Dark / Industrial / Night
        /// </summary>
        public static void SetNamedTheme(string themeName)
        {
            var (appTheme, accent) = themeName switch
            {
                "Dark"       => (ApplicationTheme.Dark,  Color.FromRgb(0x00, 0x78, 0xD4)),
                "Industrial" => (ApplicationTheme.Light, Color.FromRgb(0xD0, 0x70, 0x10)),
                "Night"      => (ApplicationTheme.Dark,  Color.FromRgb(0x00, 0xB4, 0xC0)),
                _            => (ApplicationTheme.Light, Color.FromRgb(0x00, 0x78, 0xD4)),
            };

            ThemeManager.Current.ApplicationTheme = appTheme;
            ThemeManager.Current.AccentColor = accent;
            CurrentThemeName = themeName;
        }

        /// <summary>
        /// EN: Changes the language and saves it to the settings file.
        /// TR: Dil deðiþtirir ve ayarlar dosyasýna kaydeder.
        /// </summary>
        public static void SetLanguage(string code)
        {
            LocalizationManager.Instance.SetLanguage(code);
            try
            {
                var settings = ConnectionSettings.Load();
                settings.Language = code;
                settings.Save();
            }
            catch { }
        }
    }
}
