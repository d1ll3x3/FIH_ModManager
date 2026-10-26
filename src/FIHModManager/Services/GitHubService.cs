using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

/// <summary>Consulta releases públicas de GitHub (sin token: 60 peticiones/hora por IP).</summary>
public static partial class GitHubService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, (DateTime At, GitHubRelease? Release)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>Mods de Flipping is Hard que aparecen en "Explorar".</summary>
    public static readonly IReadOnlyList<CatalogEntry> Catalog =
    [
        new("d1ll3x3/FIH_MapEditor", "Map Editor", "\uE70F"),
        new("d1ll3x3/FlippingIsHard_TAS_Tool", "TAS Tool", "\uE916"),
        new("d1ll3x3/SkinChanger_FlippingIsHard", "Charm Replacer", "\uE790"),
        new("d1ll3x3/FlippingIsHard-Trainer-BepInEx", "Trainer", "\uE709"),
        new("d1ll3x3/FIH_Ghost", "Ghost", "\uE77B"),
        new("d1ll3x3/ChaosIsHard", "Chaos is Hard", "\uE945"),
        new("d1ll3x3/ChainedIsHard", "Chained is Hard", "\uE71B"),
        new("d1ll3x3/FirstPersonIsHard", "First Person", "\uE7B3"),
    ];

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FIHModManager", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Acepta "owner/repo" o cualquier URL de github.com/owner/repo/...</summary>
    public static string? ParseRepo(string input)
    {
        var m = RepoRegex().Match(input.Trim());
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value.Replace(".git", "")}" : null;
    }

    public static async Task<GitHubRelease?> GetLatestReleaseAsync(string repo, bool force = false)
    {
        lock (Cache)
        {
            if (!force && Cache.TryGetValue(repo, out var c) && DateTime.UtcNow - c.At < CacheTtl) return c.Release;
        }

        // /releases/latest ignora prereleases; si solo hay prereleases se usa la más reciente.
        var release = await GetJsonAsync($"https://api.github.com/repos/{repo}/releases/latest") is { } latest
            ? ParseRelease(repo, latest)
            : await GetJsonAsync($"https://api.github.com/repos/{repo}/releases?per_page=1") is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() > 0
                ? ParseRelease(repo, list[0])
                : null;

        lock (Cache) Cache[repo] = (DateTime.UtcNow, release);
        return release;
    }

    public static async Task DownloadAsync(GitHubAsset asset, string destination, IProgress<double> progress)
    {
        using var response = await Http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n));
            read += n;
            if (total > 0) progress.Report(read / (double)total);
        }
    }

    /// <summary>Prefiere el .zip (suele traer assets extra); si no, la única .dll. Null = hay que preguntar.</summary>
    public static GitHubAsset? PickAsset(GitHubRelease release)
    {
        var installable = InstallableAssets(release);
        var zips = installable.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (zips.Count == 1) return zips[0];
        if (zips.Count == 0 && installable.Count == 1) return installable[0];
        return null;
    }

    public static List<GitHubAsset> InstallableAssets(GitHubRelease release) =>
        release.Assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                  || a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();

    private static async Task<JsonElement?> GetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            throw new HttpRequestException(Loc.T("err.rateLimit"));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static GitHubRelease ParseRelease(string repo, JsonElement e)
    {
        var assets = e.GetProperty("assets").EnumerateArray()
            .Select(a => new GitHubAsset(a.GetProperty("name").GetString()!, a.GetProperty("browser_download_url").GetString()!, a.GetProperty("size").GetInt64()))
            .ToList();
        return new GitHubRelease(
            repo,
            e.GetProperty("tag_name").GetString() ?? "",
            e.GetProperty("name").GetString() ?? "",
            e.GetProperty("body").GetString() ?? "",
            e.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTime() : DateTime.MinValue,
            e.GetProperty("html_url").GetString() ?? "",
            assets);
    }

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?(?:github\.com/)?([\w.-]+)/([\w.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RepoRegex();
}
