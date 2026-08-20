using System.IO;
using System.Text.Json;

namespace S7Explorer.ManualPages;

/// <summary>
/// EN: A page definition together with the outcome of validating it. Invalid pages are still
///     returned so the engineer can be shown exactly what is wrong instead of the page just
///     vanishing from the list.
/// TR: Sayfa tanımı ve doğrulama sonucu birlikte. Geçersiz sayfalar da döndürülür ki mühendise
///     neyin yanlış olduğu gösterilebilsin; sayfa listeden sessizce kaybolmasın.
/// </summary>
/// <param name="FileName">EN: File the definition came from. TR: Tanımın geldiği dosya.</param>
/// <param name="Page">EN: Parsed definition, null when the file could not be parsed at all. TR: Ayrıştırılmış tanım; dosya hiç ayrıştırılamadıysa null.</param>
/// <param name="Validation">EN: Validation outcome. TR: Doğrulama sonucu.</param>
public record ManualPageLoadResult(string FileName, ManualPageDefinition? Page, ManualPageValidationResult Validation)
{
    /// <summary>EN: True when the page parsed and validated cleanly enough to run. TR: Sayfa ayrıştırılıp çalıştırılabilecek kadar temiz doğrulandıysa true.</summary>
    public bool CanLoad => Page is not null && Validation.IsValid;
}

/// <summary>
/// EN: Discovers manual page definitions in the pages folder, parses them and validates each one
///     against the symbol table. Nothing here executes page content — a definition is data only.
/// TR: Manuel sayfa tanımlarını sayfalar klasöründe bulur, ayrıştırır ve her birini sembol
///     tablosuna karşı doğrular. Burada sayfa içeriği çalıştırılmaz — tanım yalnızca veridir.
/// </summary>
public class ManualPageLoader
{
    private const string PagesFolderName = "pages";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// EN: Folder scanned for page definitions.
    ///     NOTE: this follows the existing settings.json convention and lives next to the executable,
    ///     which is not writable under Program Files. Moving application state to %AppData% is a
    ///     separate planned task; when that happens this property is the single place to change.
    /// TR: Sayfa tanımlarının arandığı klasör.
    ///     NOT: mevcut settings.json geleneğini izleyip çalıştırılabilir dosyanın yanında durur; bu konum
    ///     Program Files altında yazılabilir değildir. Uygulama durumunu %AppData%'ya taşımak ayrı bir
    ///     planlı iştir; o zaman değiştirilecek tek yer bu özelliktir.
    /// </summary>
    public static string PagesFolder =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PagesFolderName);

    /// <summary>
    /// EN: Loads and validates every *.json definition in the pages folder.
    ///     A missing folder is normal on a fresh install and yields an empty list.
    /// TR: Sayfalar klasöründeki her *.json tanımını yükler ve doğrular.
    ///     Klasörün olmaması yeni kurulumda normaldir ve boş liste döner.
    /// </summary>
    /// <param name="symbols">EN: Symbol table to validate against. TR: Karşısında doğrulama yapılacak sembol tablosu.</param>
    public IReadOnlyList<ManualPageLoadResult> LoadAll(SymbolMapper symbols)
    {
        if (!Directory.Exists(PagesFolder))
            return [];

        return Directory.GetFiles(PagesFolder, "*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => Load(f, symbols))
            .ToList();
    }

    /// <summary>
    /// EN: Loads and validates a single definition file. Parse failures come back as an error
    ///     finding rather than an exception, so one broken file cannot stop the others loading.
    /// TR: Tek bir tanım dosyasını yükler ve doğrular. Ayrıştırma hataları istisna yerine hata
    ///     bulgusu olarak döner; böylece bozuk bir dosya diğerlerinin yüklenmesini engellemez.
    /// </summary>
    /// <param name="filePath">EN: Definition file to load. TR: Yüklenecek tanım dosyası.</param>
    /// <param name="symbols">EN: Symbol table to validate against. TR: Karşısında doğrulama yapılacak sembol tablosu.</param>
    public ManualPageLoadResult Load(string filePath, SymbolMapper symbols)
    {
        var fileName = Path.GetFileName(filePath);

        ManualPageDefinition? page;
        try
        {
            page = JsonSerializer.Deserialize<ManualPageDefinition>(File.ReadAllText(filePath), JsonOptions);
        }
        catch (JsonException ex)
        {
            return Failed(fileName, "InvalidJson", $"Dosya okunamadı, JSON geçersiz: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Failed(fileName, "FileUnreadable", $"Dosya açılamadı: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failed(fileName, "FileUnreadable", $"Dosyaya erişim reddedildi: {ex.Message}");
        }

        if (page is null)
            return Failed(fileName, "EmptyDefinition", "Dosya boş ya da 'null' içeriyor.");

        page.SourceFilePath = filePath;
        return new ManualPageLoadResult(fileName, page, ManualPageValidator.Validate(page, symbols));
    }

    /// <summary>
    /// EN: Builds a result carrying a single file-level error.
    /// TR: Tek bir dosya düzeyi hatası taşıyan sonuç üretir.
    /// </summary>
    private static ManualPageLoadResult Failed(string fileName, string code, string message)
    {
        var validation = new ManualPageValidationResult();
        validation.Issues.Add(new ValidationIssue(ValidationSeverity.Error, code, fileName, message));
        return new ManualPageLoadResult(fileName, null, validation);
    }
}
