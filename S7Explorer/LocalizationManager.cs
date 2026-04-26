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
    public string T(string key, string? fallback = null)
    {
        if (_strings.TryGetValue(key, out var val)) return val;
        if (_fallback.TryGetValue(key, out var eng)) return eng;
        return fallback ?? key;
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
