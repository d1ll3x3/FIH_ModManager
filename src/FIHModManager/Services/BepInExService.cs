using System.IO.Compression;
using System.Reflection;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

public enum BepInExState { NotInstalled, Incomplete, Installed }

public sealed record BepInExStatus(BepInExState State, string? Version, bool InteropGenerated);

/// <summary>Instala/desinstala el BepInEx IL2CPP embebido en el ejecutable.</summary>
public static class BepInExService
{
    private const string ResourceName = "BepInEx.zip";
    private const string CoreDll = "BepInEx/core/BepInEx.Core.dll";

    // Carpetas del usuario que nunca se tocan al desinstalar.
    private static readonly string[] KeepPrefixes = ["BepInEx/plugins/", "BepInEx/patchers/", "BepInEx/config/"];

    // Carpetas que BepInEx genera en el primer arranque.
    private static readonly string[] GeneratedDirs = ["BepInEx/cache", "BepInEx/interop", "BepInEx/unity-libs"];

    private static readonly Lazy<string?> BundledVersionLazy = new(ReadBundledVersion);

    public static string? BundledVersion => BundledVersionLazy.Value;

    private static Stream OpenBundle() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException(Loc.T("err.noBundle"));

    public static BepInExStatus GetStatus(GameInstall game)
    {
        var loader = File.Exists(Path.Combine(game.Path, "winhttp.dll"));
        var core = Path.Combine(game.Path, CoreDll);
        var il2cpp = File.Exists(Path.Combine(game.BepInExDir, "core", "BepInEx.Unity.IL2CPP.dll"));
        var interop = Directory.Exists(Path.Combine(game.BepInExDir, "interop"))
                      && Directory.EnumerateFiles(Path.Combine(game.BepInExDir, "interop")).Any();

        if (!loader && !il2cpp) return new BepInExStatus(BepInExState.NotInstalled, null, false);

        var version = File.Exists(core) ? PluginInspector.Inspect(core).InformationalVersion : null;
        var state = loader && il2cpp ? BepInExState.Installed : BepInExState.Incomplete;
        return new BepInExStatus(state, Shorten(version), interop);
    }

    public static void Install(GameInstall game, IProgress<double> progress)
    {
        using var zip = new ZipArchive(OpenBundle(), ZipArchiveMode.Read);
        var root = Path.GetFullPath(game.Path) + Path.DirectorySeparatorChar;
        var files = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();

        for (var i = 0; i < files.Count; i++)
        {
            var entry = files[i];
            var dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            progress.Report((i + 1) / (double)files.Count);
        }

        // plugins/ y patchers/ vienen vacíos en el zip; nos aseguramos de que existan.
        Directory.CreateDirectory(game.PluginsDir);
        Directory.CreateDirectory(game.ConfigDir);
    }

    /// <summary>Quita el loader y lo generado, conservando plugins, configs y patchers.</summary>
    public static void Uninstall(GameInstall game)
    {
        using (var zip = new ZipArchive(OpenBundle(), ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                if (KeepPrefixes.Any(p => entry.FullName.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                var file = Path.Combine(game.Path, entry.FullName);
                if (File.Exists(file)) File.Delete(file);
            }
        }

        foreach (var dir in GeneratedDirs.Select(d => Path.Combine(game.Path, d)).Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);

        foreach (var log in new[] { "LogOutput.log", "ErrorLog.log" }.Select(f => Path.Combine(game.BepInExDir, f)).Where(File.Exists))
            File.Delete(log);

        DeleteIfEmpty(Path.Combine(game.Path, "dotnet"));
        DeleteIfEmpty(Path.Combine(game.BepInExDir, "core"));
    }

    private static void DeleteIfEmpty(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var sub in Directory.EnumerateDirectories(dir)) DeleteIfEmpty(sub);
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    private static string? ReadBundledVersion()
    {
        using var zip = new ZipArchive(OpenBundle(), ZipArchiveMode.Read);
        var entry = zip.GetEntry(CoreDll);
        if (entry is null) return null;
        using var ms = new MemoryStream();
        using (var s = entry.Open()) s.CopyTo(ms);
        ms.Position = 0;
        return Shorten(PluginInspector.Inspect(ms, CoreDll).InformationalVersion);
    }

    // "6.0.0-be.755+3fab71a..." -> "6.0.0-be.755"
    private static string? Shorten(string? version) => version?.Split('+')[0];
}
