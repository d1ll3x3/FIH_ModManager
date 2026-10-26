using System.Text.RegularExpressions;
using FIHModManager.Infrastructure;
using FIHModManager.Models;
using Microsoft.Win32;

namespace FIHModManager.Services;

/// <summary>Busca instalaciones de Flipping is Hard en Steam, accesos directos del escritorio y rutas manuales.</summary>
public static partial class GameLocator
{
    private const string ExePrefix = "Flipping is Hard";

    public static string? SteamDir =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string p
            ? Path.GetFullPath(p)
            : null;

    public static string? SteamExe =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) is string p && File.Exists(p)
            ? Path.GetFullPath(p)
            : null;

    public static List<GameInstall> FindAll(IEnumerable<string> customPaths)
    {
        var found = new Dictionary<string, GameInstall>(StringComparer.OrdinalIgnoreCase);

        void Add(GameInstall? g)
        {
            if (g is not null) found.TryAdd(Normalize(g.Path), g);
        }

        foreach (var g in FromSteam()) Add(g);
        foreach (var dir in FromDesktopShortcuts()) Add(TryCreate(dir, "Shortcut"));
        foreach (var dir in customPaths) Add(TryCreate(dir, "Manual"));

        return found.Values.OrderBy(g => g.Name).ToList();
    }

    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    /// <summary>Devuelve la instalación si la carpeta contiene el juego (exe + GameAssembly.dll).</summary>
    public static GameInstall? TryCreate(string dir, string origin, int? appId = null)
    {
        if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "GameAssembly.dll"))) return null;
        var exe = Directory.EnumerateFiles(dir, "*.exe")
            .FirstOrDefault(f => Path.GetFileName(f).StartsWith(ExePrefix, StringComparison.OrdinalIgnoreCase));
        if (exe is null) return null;
        return new GameInstall(Path.GetFileName(Normalize(dir)), Normalize(dir), exe, appId, origin);
    }

    private static IEnumerable<GameInstall> FromSteam()
    {
        var steam = SteamDir;
        if (steam is null) yield break;

        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in VdfPathRegex().Matches(File.ReadAllText(vdf)))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;

            var appIds = ReadAppManifests(apps);
            var common = Path.Combine(apps, "common");
            if (!Directory.Exists(common)) continue;

            foreach (var dir in Directory.EnumerateDirectories(common, ExePrefix + "*"))
            {
                appIds.TryGetValue(Path.GetFileName(dir), out var id);
                var g = TryCreate(dir, "Steam", id == 0 ? null : id);
                if (g is not null) yield return g;
            }
        }
    }

    private static Dictionary<string, int> ReadAppManifests(string steamapps)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
        {
            var text = File.ReadAllText(acf);
            var dir = InstallDirRegex().Match(text);
            var id = AppIdRegex().Match(text);
            if (dir.Success && id.Success && int.TryParse(id.Groups[1].Value, out var appId))
                map[dir.Groups[1].Value] = appId;
        }
        return map;
    }

    private static IEnumerable<string> FromDesktopShortcuts()
    {
        var desktops = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) yield break;
        dynamic shell = Activator.CreateInstance(shellType)!;

        foreach (var desktop in desktops.Where(Directory.Exists))
        {
            foreach (var lnk in Directory.EnumerateFiles(desktop, "*.lnk"))
            {
                string? target = null;
                try
                {
                    target = (string)shell.CreateShortcut(lnk).TargetPath;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    Log.Error($"No se pudo leer el acceso directo {lnk}", ex);
                }

                if (string.IsNullOrEmpty(target)
                    || !Path.GetFileName(target).StartsWith(ExePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                // El acceso directo puede apuntar al .exe o directamente a la carpeta del juego.
                yield return Directory.Exists(target) ? target : Path.GetDirectoryName(target)!;
            }
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPathRegex();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"")]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex("\"appid\"\\s+\"(\\d+)\"")]
    private static partial Regex AppIdRegex();
}
