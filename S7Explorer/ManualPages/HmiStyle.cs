using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace S7Explorer.ManualPages;

/// <summary>
/// EN: The visual language of the manual control panel: a light field, white equipment cards with
///     centred titles, chunky coloured buttons and pilot lamps.
///
///     The palette is fixed and does not follow the application theme. On a machine screen colour
///     carries meaning — green runs, red faults, amber warns, blue is a neutral action — and that
///     meaning must not shift with a user preference.
/// TR: Manuel kumanda panelinin görsel dili: açık zemin, ortalanmış başlıklı beyaz ekipman
///     kartları, kalın renkli butonlar ve sinyal lambaları.
///
///     Palet sabittir, uygulama temasını izlemez. Makine ekranında renk anlam taşır — yeşil
///     çalıştırır, kırmızı arıza, amber uyarı, mavi nötr eylem — ve bu anlam kullanıcı tercihiyle
///     kaymamalıdır.
/// </summary>
public static class HmiStyle
{
    // ── Zemin ve yüzeyler ────────────────────────────────────────────────────
    public static readonly Brush Field       = Frozen(0xEC, 0xEF, 0xF2);
    public static readonly Brush Surface     = Frozen(0xFF, 0xFF, 0xFF);
    public static readonly Brush SurfaceSoft = Frozen(0xF7, 0xF9, 0xFA);
    public static readonly Brush Stroke      = Frozen(0xDB, 0xE0, 0xE5);
    public static readonly Brush StrokeSoft  = Frozen(0xEA, 0xEE, 0xF1);

    public static readonly Brush TextHigh = Frozen(0x1F, 0x29, 0x37);
    public static readonly Brush TextMid  = Frozen(0x55, 0x61, 0x70);
    public static readonly Brush TextLow  = Frozen(0x8A, 0x95, 0xA1);

    // ── Eylem renkleri ───────────────────────────────────────────────────────
    public static readonly Color Green     = Color.FromRgb(0x43, 0xA0, 0x47);
    public static readonly Color GreenLite = Color.FromRgb(0x5C, 0xB8, 0x60);
    public static readonly Color Blue      = Color.FromRgb(0x19, 0x76, 0xD2);
    public static readonly Color BlueLite  = Color.FromRgb(0x21, 0x96, 0xF3);
    public static readonly Color Red       = Color.FromRgb(0xE5, 0x39, 0x35);
    public static readonly Color Amber     = Color.FromRgb(0xF5, 0xA6, 0x23);
    public static readonly Color Slate     = Color.FromRgb(0x3A, 0x41, 0x49);
    public static readonly Color OffGrey   = Color.FromRgb(0xBD, 0xC4, 0xCB);

    public static readonly Brush ValueGreen = Frozen(0x22, 0xA7, 0x45);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>EN: Resolves a colour name to a state colour, defaulting to green. TR: Renk adını durum rengine çevirir, varsayılan yeşil.</summary>
    public static Color ResolveLampColor(string? name) => name?.ToLowerInvariant() switch
    {
        "red"    => Red,
        "yellow" => Amber,
        "blue"   => BlueLite,
        "grey" or "gray" => OffGrey,
        _        => Color.FromRgb(0x4C, 0xAF, 0x50)
    };

    /// <summary>EN: Vertical gradient used on solid action buttons. TR: Dolu eylem butonlarında kullanılan dikey gradyan.</summary>
    public static LinearGradientBrush Gradient(Color top, Color bottom) => new(top, bottom, 90);

    // ── Kart ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// EN: An equipment card: white surface, centred title, and the device's own controls below.
    /// TR: Ekipman kartı: beyaz yüzey, ortalanmış başlık ve altında cihazın kendi kontrolleri.
    /// </summary>
    public static Border Card(string? title, UIElement body, double? width = null, bool tightTitle = false)
    {
        var stack = new StackPanel();

        if (!string.IsNullOrWhiteSpace(title))
        {
            stack.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = tightTitle ? 12 : 15,
                FontWeight = FontWeights.Bold,
                Foreground = TextHigh,
                HorizontalAlignment = tightTitle ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, tightTitle ? 10 : 14),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        stack.Children.Add(body);

        var card = new Border
        {
            Background = Surface,
            BorderBrush = Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14, 16, 16),
            Margin = new Thickness(0, 0, 12, 12),
            Child = stack,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x8A, 0x95, 0xA1), BlurRadius = 8,
                ShadowDepth = 1, Direction = 270, Opacity = 0.18
            }
        };
        if (width is { } w) card.Width = w;
        return card;
    }

    /// <summary>
    /// EN: The alarm card, headed in red so it reads as the exception on a screen of white cards.
    /// TR: Alarm kartı; beyaz kartlardan oluşan bir ekranda istisna gibi okunsun diye kırmızı başlıklı.
    /// </summary>
    public static Border AlarmCard(string title, UIElement body, double width, UIElement? headerAction = null)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = "",                                   // Ringer
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(), FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
        });

        // Başlık şeridine isteğe bağlı bir eylem (ör. alarm onaylama) sağa yaslanarak eklenir.
        UIElement headerContent = header;
        if (headerAction is not null)
        {
            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Children.Add(header);
            Grid.SetColumn(headerAction, 1);
            bar.Children.Add(headerAction);
            headerContent = bar;
        }

        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Background = Gradient(Lighten(Red, 0.1), Red),
            CornerRadius = new CornerRadius(9, 9, 0, 0),
            Padding = new Thickness(16, 10, 16, 10),
            Child = headerContent
        });
        stack.Children.Add(new Border { Padding = new Thickness(14, 10, 14, 12), Child = body });

        return new Border
        {
            Width = width,
            Background = Surface,
            BorderBrush = new SolidColorBrush(Lighten(Red, 0.55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 12, 12),
            Child = stack,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x8A, 0x95, 0xA1), BlurRadius = 8,
                ShadowDepth = 1, Direction = 270, Opacity = 0.18
            }
        };
    }

    // ── Butonlar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// EN: A solid coloured button carrying a glyph, a caption, or both.
    /// TR: Bir simge, bir metin ya da ikisini birden taşıyan dolu renkli buton.
    /// </summary>
    public static Button SolidButton(string? glyph, string? caption, double width, double height,
                                     double glyphSize = 30)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrWhiteSpace(glyph))
        {
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = glyphSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, string.IsNullOrWhiteSpace(caption) ? 0 : 8, 0)
            });
        }

        if (!string.IsNullOrWhiteSpace(caption))
        {
            content.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
        }

        var button = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            BorderThickness = new Thickness(1),
            IsEnabled = false
        };

        if (Application.Current?.TryFindResource("HmiButtonStyle") is Style style)
            button.Style = style;

        return button;
    }

    /// <summary>
    /// EN: Paints a button in one of the panel's roles.
    /// TR: Butonu panelin rollerinden biriyle boyar.
    /// </summary>
    /// <param name="button">EN: Button to paint. TR: Boyanacak buton.</param>
    /// <param name="fill">EN: Face colour; null paints the neutral outlined style. TR: Yüz rengi; null nötr çerçeveli stili boyar.</param>
    /// <param name="enabled">EN: Whether it may be pressed. TR: Basılabilir olup olmadığı.</param>
    public static void PaintButton(Button button, Color? fill, bool enabled)
    {
        button.IsEnabled = enabled;

        if (fill is { } color)
        {
            button.Background = Gradient(Lighten(color, 0.16), color);
            button.BorderBrush = new SolidColorBrush(Darken(color, 0.18));
            button.Foreground = Brushes.White;
        }
        else
        {
            button.Background = Gradient(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF2, 0xF4, 0xF6));
            button.BorderBrush = Stroke;
            button.Foreground = enabled ? TextHigh : TextLow;
        }
    }

    // ── Göstergeler ──────────────────────────────────────────────────────────

    /// <summary>EN: A pilot lamp with a glass highlight. TR: Cam parlaması olan sinyal lambası.</summary>
    public static Ellipse Lamp(double size = 30) => new()
    {
        Width = size,
        Height = size,
        Fill = new SolidColorBrush(OffGrey),
        Stroke = new SolidColorBrush(Color.FromRgb(0xA8, 0xB0, 0xB8)),
        StrokeThickness = 1.5
    };

    /// <summary>
    /// EN: Lights or darkens a lamp. An unknown value is drawn hollow so "could not read" is never
    ///     mistaken for "off" — on a diagnostic screen that difference matters.
    /// TR: Lambayı yakar veya söndürür. Bilinmeyen değer içi boş çizilir; "okuyamadım" asla
    ///     "kapalı" ile karışmaz — teşhis ekranında bu fark önemlidir.
    /// </summary>
    public static void SetLamp(Ellipse lamp, bool? on, Color color)
    {
        if (on == true)
        {
            lamp.Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.28),
                GradientStops =
                {
                    new GradientStop(Lighten(color, 0.55), 0.0),
                    new GradientStop(color, 0.7),
                    new GradientStop(Darken(color, 0.18), 1.0)
                }
            };
            lamp.Stroke = new SolidColorBrush(Darken(color, 0.3));
            lamp.Effect = new DropShadowEffect { Color = color, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.65 };
        }
        else
        {
            lamp.Fill = on is null ? Brushes.Transparent : new SolidColorBrush(OffGrey);
            lamp.Stroke = new SolidColorBrush(Color.FromRgb(0xA8, 0xB0, 0xB8));
            lamp.Effect = null;
        }
    }

    /// <summary>EN: A small state badge, as panels put next to a device name. TR: Panoların cihaz adının yanına koyduğu türden küçük durum rozeti.</summary>
    public static Border Pill(out TextBlock text)
    {
        text = new TextBlock
        {
            Text = "—",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        return new Border
        {
            Background = new SolidColorBrush(OffGrey),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 3, 9, 3),
            MinWidth = 56,
            Child = text
        };
    }

    /// <summary>EN: Repaints a state badge. TR: Durum rozetini yeniden boyar.</summary>
    public static void SetPill(Border pill, TextBlock text, string caption, Color color)
    {
        text.Text = caption;
        pill.Background = new SolidColorBrush(color);
    }

    /// <summary>EN: The large live value a panel shows for a measurement. TR: Panonun bir ölçüm için gösterdiği büyük canlı değer.</summary>
    public static TextBlock BigValue() => new()
    {
        Text = "––",
        FontSize = 26,
        FontWeight = FontWeights.Bold,
        Foreground = ValueGreen,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    /// <summary>EN: A small caption above a value or control. TR: Bir değerin veya kontrolün üstündeki küçük başlık.</summary>
    public static TextBlock Caption(string text, double size = 11) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = TextMid,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>
    /// EN: A minus / value / plus stepper, the way a panel lets an operator nudge a setpoint
    ///     without a keyboard.
    /// TR: Eksi / değer / artı üçlüsü; panonun operatöre klavyesiz set değeri değiştirtme biçimi.
    /// </summary>
    public static Grid Stepper(out Button minus, out Button plus, out TextBox value, double width = 148)
    {
        minus = StepButton("");   // Remove
        plus  = StepButton("");   // Add

        value = new TextBox
        {
            Text = "0",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = TextHigh,
            Background = Surface,
            BorderBrush = Stroke,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 32,
            IsEnabled = false
        };
        if (Application.Current?.TryFindResource("HmiTextBoxStyle") is Style s) value.Style = s;

        var grid = new Grid { Width = width };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        Grid.SetColumn(minus, 0);
        Grid.SetColumn(value, 1);
        Grid.SetColumn(plus, 2);
        value.Margin = new Thickness(4, 0, 4, 0);

        grid.Children.Add(minus);
        grid.Children.Add(value);
        grid.Children.Add(plus);
        return grid;
    }

    private static Button StepButton(string glyph)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 30,
            Height = 32,
            Background = Gradient(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF0, 0xF2, 0xF4)),
            BorderBrush = Stroke,
            BorderThickness = new Thickness(1),
            Foreground = TextHigh,
            IsEnabled = false
        };
        if (Application.Current?.TryFindResource("HmiButtonStyle") is Style style) button.Style = style;
        return button;
    }

    internal static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    internal static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));
}
