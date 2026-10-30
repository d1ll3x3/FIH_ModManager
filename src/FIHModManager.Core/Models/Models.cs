namespace FIHModManager.Models;

public sealed record GameInstall(string Name, string Path, string ExePath, int? SteamAppId, string Origin)
{
    public string ExeName => System.IO.Path.GetFileNameWithoutExtension(ExePath);
    public string BepInExDir => System.IO.Path.Combine(Path, "BepInEx");
    public string PluginsDir => System.IO.Path.Combine(BepInExDir, "plugins");
    public string DisabledDir => System.IO.Path.Combine(BepInExDir, "disabled_plugins");
    public string ConfigDir => System.IO.Path.Combine(BepInExDir, "config");
}

public sealed record PluginMeta(string Guid, string Name, string Version);

public enum LoaderKind
{
    /// <summary>DLL sin [BepInPlugin]: dependencia o librería auxiliar.</summary>
    Library,
    BepInEx6Il2Cpp,
    BepInEx6Mono,
    BepInEx5,
}

public sealed record AssemblyMeta(IReadOnlyList<PluginMeta> Plugins, LoaderKind Kind, string? InformationalVersion);

public sealed class ModEntry
{
    /// <summary>Nombre de la entrada dentro de plugins/ (carpeta o .dll). Identifica al mod.</summary>
    public required string Key { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required bool Enabled { get; init; }
    public required IReadOnlyList<string> Dlls { get; init; }
    /// <summary>DLLs apagadas a mano (.dl, .dll.disabled…) cuando el mod no tiene ninguna .dll real.</summary>
    public required IReadOnlyList<string> RenamedDlls { get; init; }
    public required IReadOnlyList<PluginMeta> Plugins { get; init; }
    public required LoaderKind Kind { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime Modified { get; init; }

    public IEnumerable<string> Guids => Plugins.Select(p => p.Guid);
}

public sealed class ModSource
{
    public string Repo { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Asset { get; set; } = "";
    public DateTime InstalledAt { get; set; }
}

public sealed class ModProfile
{
    public string Name { get; set; } = "";
    public List<string> EnabledKeys { get; set; } = [];
    public DateTime SavedAt { get; set; }
}

/// <summary>Estado del manager por instalación del juego (BepInEx/fihmm.json).</summary>
public sealed class ManagerState
{
    public Dictionary<string, ModSource> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ModProfile> Profiles { get; set; } = [];
}

public sealed class AppSettings
{
    public List<string> CustomGamePaths { get; set; } = [];
    public string? LastGamePath { get; set; }
    public string? Language { get; set; }
}

public sealed record GitHubAsset(string Name, string DownloadUrl, long Size, long Downloads);

public sealed record GitHubRelease(string Repo, string Tag, string Name, string Body, DateTime PublishedAt, string HtmlUrl, IReadOnlyList<GitHubAsset> Assets);

/// <summary>Icon es un nombre neutro ("edit", "timer"…); cada interfaz lo traduce a su fuente de iconos.</summary>
public sealed record CatalogEntry(string Repo, string Title, string Icon, string Author);
