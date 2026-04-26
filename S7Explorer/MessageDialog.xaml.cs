using System.Windows;
using System.Windows.Media;
using ModernWpf.Controls;

namespace S7Explorer
{
    /// <summary>
    /// ModernWpf temasıyla uyumlu MessageBox alternatifi.
    /// </summary>
    public partial class MessageDialog : Window
    {
        /// <summary>
        /// EN: Gets the dialog result selected by the user.
        /// TR: Kullanıcının seçtiği ileti kutusu sonucunu alır.
        /// </summary>
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        // ── Statik yardımcılar ────────────────────────────────────────────────

        /// <summary>
        /// EN: Shows a themed dialog with a single OK button.
        /// TR: Tek OK butonlu temalı iletişim penceresi gösterir.
        /// </summary>
        public static MessageBoxResult Show(
            string message,
            string title = "",
            MessageBoxImage icon = MessageBoxImage.Information,
            Window? owner = null)
            => Show(message, title, MessageBoxButton.OK, icon, owner);

        /// <summary>
        /// EN: Shows a themed dialog with configurable button and icon options.
        /// TR: Özelleştirilebilir buton ve ikon seçenekleriyle temalı iletişim penceresi gösterir.
        /// </summary>
        public static MessageBoxResult Show(
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage icon = MessageBoxImage.Information,
            Window? owner = null)
        {
            var dlg = new MessageDialog(message, title, buttons, icon);
            dlg.Owner = owner ?? Application.Current?.MainWindow;
            dlg.ShowDialog();
            return dlg.Result;
        }

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// EN: Initializes the dialog content, icon, and button layout.
        /// TR: Diyalog içeriğini, ikonunu ve buton düzenini başlatır.
        /// </summary>
        public MessageDialog(
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage icon)
        {
            InitializeComponent();
            Title = title;
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            ApplyIcon(icon);
            ApplyButtons(buttons);
        }

        private static string T(string key, string fallback)
            => LocalizationManager.Instance.T(key, fallback);

        // ── İkon ─────────────────────────────────────────────────────────────

        /// <summary>
        /// EN: Applies the icon glyph and color according to the selected message type.
        /// TR: Seçilen mesaj tipine göre ikon karakterini ve rengini uygular.
        /// </summary>
        private void ApplyIcon(MessageBoxImage icon)
        {
            var (glyph, colorKey) = icon switch
            {
                MessageBoxImage.Error       => ("\uEA39", "IconError"),
                MessageBoxImage.Warning     => ("\uE7BA", "IconWarning"),
                MessageBoxImage.Question    => ("\uE9CE", "IconQuestion"),
                MessageBoxImage.Information => ("\uE946", "IconInfo"),
                _                           => ("\uE946", "IconInfo"),
            };

            DialogIcon.Glyph = glyph;
            if (TryFindResource(colorKey) is Brush brush)
            {
                DialogIcon.Foreground = brush;
                TxtTitle.Foreground = brush;
            }
        }

        // ── Butonlar ──────────────────────────────────────────────────────────

        /// <summary>
        /// EN: Configures visible buttons and captions based on MessageBoxButton mode.
        /// TR: MessageBoxButton moduna göre görünen butonları ve başlıklarını ayarlar.
        /// </summary>
        private void ApplyButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    BtnYes.Content = T("Btn_Ok", "OK");
                    BtnNo.Visibility = Visibility.Collapsed;
                    BtnCancel.Visibility = Visibility.Collapsed;
                    break;

                case MessageBoxButton.OKCancel:
                    BtnYes.Content = T("Btn_Ok", "OK");
                    BtnNo.Visibility = Visibility.Collapsed;
                    BtnCancel.Content = T("Btn_Cancel", "Cancel");
                    break;

                case MessageBoxButton.YesNo:
                    BtnYes.Content = T("Btn_Yes", "Yes");
                    BtnNo.Content = T("Btn_No", "No");
                    BtnCancel.Visibility = Visibility.Collapsed;
                    break;

                case MessageBoxButton.YesNoCancel:
                    BtnYes.Content = T("Btn_Yes", "Yes");
                    BtnNo.Content = T("Btn_No", "No");
                    BtnCancel.Content = T("Btn_Cancel", "Cancel");
                    break;
            }
        }

        // ── Click handler'ları ────────────────────────────────────────────────

        /// <summary>
        /// EN: Handles the primary action button (OK/Yes) and closes the dialog.
        /// TR: Birincil işlem butonunu (OK/Yes) işler ve diyaloğu kapatır.
        /// </summary>
        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = BtnYes.Content?.ToString() == T("Btn_Ok", "OK")
                ? MessageBoxResult.OK
                : MessageBoxResult.Yes;
            Close();
        }

        /// <summary>
        /// EN: Handles the No button click and closes the dialog.
        /// TR: No butonu tıklamasını işler ve diyaloğu kapatır.
        /// </summary>
        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            Close();
        }

        /// <summary>
        /// EN: Handles the Cancel button click and closes the dialog.
        /// TR: Cancel butonu tıklamasını işler ve diyaloğu kapatır.
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }
    }
}
