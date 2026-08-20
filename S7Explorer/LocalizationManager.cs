using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace S7Explorer;

public record LanguageInfo(string Code, string DisplayName);

/// <summary>
/// EN: Reads JSON language files from the 'lang' folder and provides translations.
///     Usage: LocalizationManager.Instance.T("Key")
/// TR: 'lang' klasöründeki JSON dil dosyalarını okur ve çevirileri sağlar.
///     Kullanım: LocalizationManager.Instance.T("Key")
/// </summary>
public sealed class LocalizationManager
{
    public static readonly LocalizationManager Instance = new();

    public event EventHandler? LanguageChanged;

    private Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _fallback = new(StringComparer.OrdinalIgnoreCase);
    public string CurrentLanguageCode { get; private set; } = "en-US";

    private readonly List<LanguageInfo> _available = new();
    public IReadOnlyList<LanguageInfo> Available => _available;

    private static string LangFolder =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");

    // "key": "value" satırlarını yakalar; value içindeki escaped karakterleri destekler
    private static readonly Regex EntryRegex = new(
        @"^\s*""(?<key>[^""\\]*(?:\\.[^""\\]*)*)""\s*:\s*""(?<val>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled);

    /// <summary>
    /// EN: Required signature value. Only files containing this key-value pair are loaded.
    /// TR: Zorunlu imza değeri. Sadece bu anahtar-değer çiftini içeren dosyalar yüklenir.
    /// </summary>
    public const string RequiredSignature = "S7Explorer.Lang.v1";

    private LocalizationManager() { }

    /// <summary>
    /// EN: Parses a JSON language file in a fault-tolerant way: valid entries are loaded even if some lines are malformed.
    /// TR: JSON dil dosyasını hata toleranslı şekilde ayrıştırır: bazı satırlar hatalı olsa bile geçerli girdiler yüklenir.
    /// </summary>
    private static Dictionary<string, string> ParseFaultTolerant(string content)
    {
        // Önce standart parse'ı dene (hızlı yol)
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(content,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { /* standart parse başarısız, toleranslı parse'a geç */ }

        // Toleranslı parse: her satırı ayrı ayrı oku
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split('\n'))
        {
            var m = EntryRegex.Match(line);
            if (!m.Success) continue;
            var key = Regex.Unescape(m.Groups["key"].Value);
            var val = Regex.Unescape(m.Groups["val"].Value);
            result.TryAdd(key, val);
        }
        return result;
    }

    /// <summary>
    /// EN: Scans the 'lang' folder and registers all available languages. Call once at app startup.
    /// TR: 'lang' klasörünü tarar ve mevcut tüm dilleri kaydeder. Uygulama başlatılırken bir kez çağrılır.
    /// </summary>
    public void Scan()
    {
        _available.Clear();

        if (Directory.Exists(LangFolder))
        {
            foreach (var file in Directory.GetFiles(LangFolder, "*.json").OrderBy(f => f))
                {
                    var code = Path.GetFileNameWithoutExtension(file);
                    try
                    {
                        var content = File.ReadAllText(file, Encoding.UTF8);
                        var dict = ParseFaultTolerant(content);

                        // İmza kontrolü: imzasız dosyaları yoksay
                        if (!dict.TryGetValue("_Signature", out var sig) || sig != RequiredSignature)
                            continue;

                        var displayName = dict.TryGetValue("_LanguageName", out var n) ? n : code;
                        _available.Add(new LanguageInfo(code, displayName));
                    }
                    catch { /* skip unreadable file */ }
                }
        }

        if (_available.Count == 0)
            _available.Add(new LanguageInfo("en-US", "English"));

        // Always load English as fallback
        LoadFallback();
    }

    private void LoadFallback()
    {
        var file = Path.Combine(LangFolder, "en-US.json");
        if (!File.Exists(file)) return;
        try
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            _fallback = ParseFaultTolerant(content);
        }
        catch { }
    }

    /// <summary>
    /// EN: Loads the specified language file and raises LanguageChanged. Falls back to en-US if the file is not found.
    /// TR: Belirtilen dil dosyasını yükler ve LanguageChanged olayını tetikler. Dosya bulunamazsa en-US'ye döner.
    /// </summary>
    public void SetLanguage(string code)
    {
        var file = Path.Combine(LangFolder, $"{code}.json");

        if (!File.Exists(file))
        {
            var fallback = Path.Combine(LangFolder, "en-US.json");
            if (File.Exists(fallback)) { file = fallback; code = "en-US"; }
            else { CurrentLanguageCode = code; LanguageChanged?.Invoke(this, EventArgs.Empty); return; }
        }

        try
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            _strings = ParseFaultTolerant(content);
        }
        catch { _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }

        CurrentLanguageCode = code;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// EN: Returns the translated string for the given key, falls back to English, then to the key itself.
    /// TR: Verilen anahtar için çevrilmiş metni döndürür; önce İngilizce'ye, sonra anahtarın kendisine düşer.
    /// </summary>
    public string T(string key)
    {
        if (_strings.TryGetValue(key, out var val)) return val;
        if (_fallback.TryGetValue(key, out var eng)) return eng;
        return key;
    }

    /// <summary>
    /// EN: Returns the translated string, or the given text when the key is missing from every
    ///     language file.
    ///
    ///     This is deliberately not an overload of <see cref="T(string)"/>. It used to be
    ///     'T(string key, string? fallback = null)', which sat next to the params overload below —
    ///     and C# prefers a method applicable in normal form over one needing params expansion.
    ///     So every 'T("Key", someString)' call silently bound to the fallback overload and
    ///     returned the template unformatted, leaving '{0}' on screen wherever a single string was
    ///     being substituted. Separate names make that mistake impossible to write.
    /// TR: Çevrilmiş metni döndürür; anahtar hiçbir dil dosyasında yoksa verilen metni döndürür.
    ///
    ///     Bu bilinçli olarak <see cref="T(string)"/> ile aynı adı taşımıyor. Eskiden
    ///     'T(string key, string? fallback = null)' idi ve aşağıdaki params aşırı yüklemesiyle
    ///     yan yana duruyordu — C# ise normal biçimde uygulanabilen metodu, params açılımı
    ///     gerektirene tercih eder. Dolayısıyla her 'T("Key", birString)' çağrısı sessizce fallback
    ///     aşırı yüklemesine bağlanıyor ve şablonu biçimlendirmeden döndürüyordu; tek bir string
    ///     yerleştirilen her yerde ekranda '{0}' kalıyordu. Ayrı adlar bu hatayı yazılamaz kılıyor.
    /// </summary>
    /// <param name="key">EN: Translation key. TR: Çeviri anahtarı.</param>
    /// <param name="fallback">EN: Text to use when the key is missing. TR: Anahtar yoksa kullanılacak metin.</param>
    public string TOr(string key, string fallback)
    {
        if (_strings.TryGetValue(key, out var val)) return val;
        if (_fallback.TryGetValue(key, out var eng)) return eng;
        return fallback;
    }

    /// <summary>
    /// EN: Returns the translated string formatted with the given arguments.
    /// TR: Verilen argümanlarla biçimlendirilmiş çevrilmiş metni döndürür.
    /// </summary>
    public string T(string key, params object[] args)
    {
        var tpl = T(key);
        try { return string.Format(tpl, args); }
        catch { return tpl; }
    }
}
