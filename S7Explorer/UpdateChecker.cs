using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace S7Explorer;

/// <summary>
/// EN: A newer release found on GitHub: normalized version, original tag text and the page to open.
/// TR: GitHub'da bulunan daha yeni sürüm: normalize edilmiş sürüm, etiketin özgün metni ve açılacak sayfa.
/// </summary>
public sealed record UpdateInfo(Version Version, string TagName, string ReleaseUrl);

/// <summary>
/// EN: Compares the running assembly version against the latest GitHub release.
///     Read-only and best effort: it never blocks startup and stays silent when there is no network.
/// TR: Çalışan derlemenin sürümünü GitHub'daki en son release ile karşılaştırır.
///     Salt okunur ve "elinden geleni yapar" niteliktedir: açılışı bekletmez, ağ yoksa sessiz kalır.
/// </summary>
public static class UpdateChecker
{
    private const string Owner = "zafersars";
    private const string Repo  = "S7Explorer";

    /// <summary>
    /// EN: GitHub API endpoint for the latest published release (pre-releases are excluded by GitHub).
    /// TR: En son yayımlanmış release için GitHub API adresi (ön sürümleri GitHub zaten hariç tutar).
    /// </summary>
    private static readonly Uri LatestReleaseApi =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    /// <summary>
    /// EN: Human-facing releases page, used as a fallback when the API response carries no URL.
    /// TR: Kullanıcıya açılan release sayfası; API cevabında adres yoksa yedek olarak kullanılır.
    /// </summary>
    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repo}/releases/latest";

    // Saha PC'sinde ağ kapalıysa istek asılı kalmasın; açılışta çalışan bir kontrol için 10 sn yeter.
    // Lazy: istemci User-Agent'ında CurrentVersion'ı kullanır, o da bu sınıfın statik kurulumunda
    // atanır. İlk isteğe kadar ertelemek, alan bildirim sırasına bağımlılığı ortadan kaldırır.
    private static readonly Lazy<HttpClient> HttpClientLazy = new(CreateClient);

    private static HttpClient Http => HttpClientLazy.Value;

    /// <summary>
    /// EN: Version of the running assembly, normalized to Major.Minor.Patch.
    /// TR: Çalışan derlemenin sürümü, Major.Minor.Patch olarak normalize edilmiş.
    /// </summary>
    public static Version CurrentVersion { get; } =
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// EN: Current version as it is shown to the user ("v1.0.2").
    /// TR: Kullanıcıya gösterildiği haliyle geçerli sürüm ("v1.0.2").
    /// </summary>
    public static string CurrentVersionText => "v" + CurrentVersion.ToString(3);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub API User-Agent'sız istekleri 403 ile reddeder.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(Repo, CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    /// EN: Queries the latest release and returns it only when it is newer than the running build;
    ///     returns null when up to date or when the tag cannot be read as a version.
    ///     Network and parse failures surface as exceptions for the caller to log.
    /// TR: En son release'i sorgular ve yalnızca çalışan sürümden yeniyse döndürür;
    ///     güncelse ya da etiket sürüm olarak okunamıyorsa null döner.
    ///     Ağ ve ayrıştırma hataları, çağıranın loglaması için istisna olarak yükselir.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(tag) || !TryParseTag(tag, out var latest))
            return null;

        if (latest <= CurrentVersion)
            return null;

        var url = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString()
            : null;

        return new UpdateInfo(latest, tag.Trim(), string.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url);
    }

    /// <summary>
    /// EN: Parses release tags of the form "v1.2.3" / "1.2.3" / "1.2.3-rc1" into a version.
    /// TR: "v1.2.3" / "1.2.3" / "1.2.3-rc1" biçimindeki release etiketlerini sürüme çevirir.
    /// </summary>
    private static bool TryParseTag(string tag, out Version version)
    {
        var text = tag.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text[1..];

        // "1.2.3-rc1" gibi son ekler System.Version tarafından ayrıştırılamaz; atılır.
        var dashIndex = text.IndexOf('-');
        if (dashIndex >= 0)
            text = text[..dashIndex];

        if (Version.TryParse(text, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>
    /// EN: Levels versions to Major.Minor.Patch so "1.0.2" and "1.0.2.0" compare as equal.
    /// TR: Sürümleri Major.Minor.Patch'e indirger; "1.0.2" ile "1.0.2.0" eşit karşılaştırılsın diye.
    /// </summary>
    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
