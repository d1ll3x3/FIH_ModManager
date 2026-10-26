using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

/// <summary>
/// Gestión de mods en BepInEx/plugins. Los mods desactivados se mueven a BepInEx/disabled_plugins,
/// fuera del alcance del cargador, para no tener que renombrar DLLs.
/// </summary>
public static partial class ModService
{
    private static readonly string[] JunkNames = ["__MACOSX", ".DS_Store", "Thumbs.db"];

    public static List<ModEntry> Scan(GameInstall game)
    {
        var mods = new List<ModEntry>();
        mods.AddRange(ScanDir(game.PluginsDir, enabled: true));
        mods.AddRange(ScanDir(game.DisabledDir, enabled: false));
        return mods;
    }

    // Extensiones con las que se suele "apagar" un mod a mano sin moverlo de plugins/.
    private static readonly string[] RenamedDllSuffixes = [".dl", ".dll.disabled", ".dll.off"];

    private static bool IsRenamedDll(string path) =>
        RenamedDllSuffixes.Any(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    private static string RestoredDllName(string path) =>
        path[..^RenamedDllSuffixes.First(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase)).Length] + ".dll";

    private static IEnumerable<ModEntry> ScanDir(string dir, bool enabled)
    {
        if (!Directory.Exists(dir)) yield break;

        foreach (var path in Directory.EnumerateFileSystemEntries(dir))
        {
            var isDir = Directory.Exists(path);
            if (!isDir && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !IsRenamedDll(path)) continue;

            var dlls = isDir
                ? Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).ToList()
                : IsRenamedDll(path) ? [] : [path];
            // Solo cuentan como "apagado a mano" si no queda ninguna .dll real (si no, son copias de seguridad).
            var renamed = dlls.Count > 0 ? []
                : isDir ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(IsRenamedDll).ToList()
                : [path];

            var metas = dlls.Concat(renamed).Select(PluginInspector.Inspect).ToList();
            var plugins = metas.SelectMany(m => m.Plugins).ToList();
            var kind = metas.Select(m => m.Kind).Where(k => k != LoaderKind.Library).DefaultIfEmpty(LoaderKind.Library).Min();

            var files = isDir ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList() : [new FileInfo(path)];

            yield return new ModEntry
            {
                Key = Path.GetFileName(path),
                FullPath = path,
                IsDirectory = isDir,
                Enabled = enabled && renamed.Count == 0,
                Dlls = dlls,
                RenamedDlls = renamed,
                Plugins = plugins,
                Kind = kind,
                SizeBytes = files.Sum(f => f.Length),
                Modified = files.Count == 0 ? Directory.GetLastWriteTime(path) : files.Max(f => f.LastWriteTime),
            };
        }
    }

    public static void SetEnabled(GameInstall game, ModEntry mod, bool enabled)
    {
        if (mod.Enabled == enabled) return;
        var path = mod.FullPath;

        if (enabled && mod.RenamedDlls.Count > 0)
        {
            // Apagado a mano renombrando la DLL: se le devuelve la extensión .dll.
            foreach (var f in mod.RenamedDlls) File.Move(f, RestoredDllName(f));
            if (!mod.IsDirectory) path = RestoredDllName(path);
        }

        var inPlugins = string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)!), Path.GetFullPath(game.PluginsDir), StringComparison.OrdinalIgnoreCase);
        if (inPlugins == enabled) return;

        var targetDir = enabled ? game.PluginsDir : game.DisabledDir;
        Directory.CreateDirectory(targetDir);
        var target = Path.Combine(targetDir, Path.GetFileName(path));
        if (Path.Exists(target))
            throw new IOException(Loc.T("err.exists", Path.GetFileName(path), enabled ? "plugins" : "disabled_plugins"));

        if (mod.IsDirectory) Directory.Move(path, target);
        else File.Move(path, target);
    }

    /// <summary>Envía el mod a la Papelera de reciclaje (recuperable).</summary>
    public static void Delete(ModEntry mod) => RecycleBin.Send(mod.FullPath);

    /// <summary>
    /// Instala un .dll, .zip o carpeta. Devuelve las claves de los mods resultantes.
    /// Si el mod ya existe (misma carpeta o mismo GUID) se actualiza encima, sin borrar
    /// los datos del usuario que haya dentro (mapas, skins...).
    /// </summary>
    public static IReadOnlyList<string> Install(GameInstall game, string source, IReadOnlyList<ModEntry> installed)
    {
        var staging = Path.Combine(Path.GetTempPath(), "FIHModManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var sourceName = Path.GetFileNameWithoutExtension(source.TrimEnd('\\', '/'));
            if (Directory.Exists(source))
                CopyDir(source, Path.Combine(staging, Path.GetFileName(source.TrimEnd('\\', '/'))));
            else if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ExtractZip(source, staging);
            else if (source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                File.Copy(source, Path.Combine(staging, Path.GetFileName(source)));
            else
                throw new NotSupportedException(Loc.T("err.format"));

            return InstallFromRoot(game, staging, sourceName, installed);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static IReadOnlyList<string> InstallFromRoot(GameInstall game, string root, string sourceName, IReadOnlyList<ModEntry> installed)
    {
        while (true)
        {
            var dirs = Directory.GetDirectories(root).Where(d => !IsJunk(d)).ToList();
            var files = Directory.GetFiles(root).Where(f => !IsJunk(f)).ToList();

            // Estructura completa (BepInEx/plugins/...) o relativa a BepInEx (plugins/...).
            var bepinex = dirs.FirstOrDefault(d => NameIs(d, "BepInEx"));
            if (bepinex is not null) return MergeBepInExLayout(game, bepinex);
            if (dirs.Any(d => NameIs(d, "plugins")) && dirs.All(d => NameIs(d, "plugins") || NameIs(d, "patchers") || NameIs(d, "config")))
                return MergeBepInExLayout(game, root);

            if (files.Count == 0 && dirs.Count == 1)
            {
                var only = dirs[0];
                var inner = Directory.GetDirectories(only);
                // Carpeta envoltorio tipo "Mod-1.0/BepInEx/...": se desciende.
                if (inner.Any(d => NameIs(d, "BepInEx") || NameIs(d, "plugins")))
                {
                    root = only;
                    continue;
                }
                return [InstallPayload(game, only, Path.GetFileName(only), installed)];
            }

            if (files.Count == 0 && dirs.Count == 0)
                throw new InvalidOperationException(Loc.T("err.empty"));

            return [InstallPayload(game, root, DeriveName(root, sourceName), installed)];
        }
    }

    private static string InstallPayload(GameInstall game, string payload, string name, IReadOnlyList<ModEntry> installed)
    {
        var newDlls = Directory.EnumerateFiles(payload, "*.dll", SearchOption.AllDirectories)
            .Select(f => (Path: f, Meta: PluginInspector.Inspect(f)))
            .ToList();
        var newGuids = newDlls.SelectMany(d => d.Meta.Plugins).Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var match = installed.FirstOrDefault(m => m.Guids.Any(newGuids.Contains))
                    ?? installed.FirstOrDefault(m => m.IsDirectory && string.Equals(m.Key, name, StringComparison.OrdinalIgnoreCase));

        string target;
        if (match is { IsDirectory: true })
        {
            target = match.FullPath;
            // Si la DLL nueva se llama distinto que la vieja con el mismo GUID, la vieja sobra (si no, cargaría dos veces).
            foreach (var old in match.Dlls.Concat(match.RenamedDlls))
            {
                var oldGuids = PluginInspector.Inspect(old).Plugins.Select(p => p.Guid);
                var replaced = newDlls.Any(n => string.Equals(Path.GetFileName(n.Path), Path.GetFileName(old), StringComparison.OrdinalIgnoreCase));
                if (!replaced && oldGuids.Any(newGuids.Contains)) File.Delete(old);
            }
        }
        else
        {
            var baseDir = match is { Enabled: false } ? game.DisabledDir : game.PluginsDir;
            // Un mod antiguo como DLL suelta en plugins/ pasa a tener su propia carpeta.
            if (match is not null) File.Delete(match.FullPath);
            target = Path.Combine(baseDir, name);
        }

        CopyDir(payload, target);
        return Path.GetFileName(target);
    }

    private static IReadOnlyList<string> MergeBepInExLayout(GameInstall game, string bepinexRoot)
    {
        CopyDir(bepinexRoot, game.BepInExDir);
        var plugins = Path.Combine(bepinexRoot, "plugins");
        return Directory.Exists(plugins)
            ? Directory.EnumerateFileSystemEntries(plugins).Select(Path.GetFileName).OfType<string>().ToList()
            : [];
    }

    private static string DeriveName(string root, string sourceName)
    {
        var dlls = Directory.GetFiles(root, "*.dll");
        if (dlls.Length == 1) return Path.GetFileNameWithoutExtension(dlls[0]);

        // Paquetes estilo Thunderstore traen manifest.json con el nombre.
        var manifest = Path.Combine(root, "manifest.json");
        if (File.Exists(manifest))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s) return s;
        }

        var cleaned = VersionSuffixRegex().Replace(sourceName, "");
        return cleaned.Length > 0 ? cleaned : sourceName;
    }

    private static void ExtractZip(string zipPath, string dest)
    {
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            // Algunos zips (p. ej. hechos con PowerShell) usan '\' como separador.
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            var full = Path.GetFullPath(Path.Combine(root, name));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Loc.T("err.zipSlip", entry.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
        }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (IsJunk(file)) continue;
            var dest = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static bool NameIs(string path, string name) =>
        string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase);

    private static bool IsJunk(string path) =>
        path.Split('\\', '/').Any(part => JunkNames.Contains(part, StringComparer.OrdinalIgnoreCase));

    // "FIHMapEditor_v0.15.1" -> "FIHMapEditor"
    [GeneratedRegex(@"[-_ .]*v?\d+(\.\d+)+.*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffixRegex();

    private static class RecycleBin
    {
        private const uint FO_DELETE = 3;
        private const ushort FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMATION = 0x10, FOF_SILENT = 0x4, FOF_NOERRORUI = 0x400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        public static void Send(string path)
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0", // la API exige doble terminador
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
            };
            var result = SHFileOperation(ref op);
            if (result != 0 || op.fAnyOperationsAborted)
                throw new IOException(Loc.T("err.recycle", result.ToString("X")));
        }
    }
}
