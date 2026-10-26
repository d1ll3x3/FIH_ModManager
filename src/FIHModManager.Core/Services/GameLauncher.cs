using System.Diagnostics;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

public static class GameLauncher
{
    // Doorstop 4 permite desactivarse por línea de comandos, sin tocar doorstop_config.ini.
    private const string VanillaArgs = "--doorstop-enabled false";

    public static void Launch(GameInstall game, bool withMods)
    {
        var args = withMods ? "" : VanillaArgs;

        // Los juegos de Steam se lanzan a través de Steam para que no se relancen solos y pierdan los argumentos.
        // En Linux, además, es Steam quien aplica Proton y la opción de lanzamiento WINEDLLOVERRIDES.
        if (game.SteamAppId is { } appId && GameLocator.SteamExe is { } steam)
        {
            Process.Start(new ProcessStartInfo(steam.FileName, $"{steam.PrefixArgs} -applaunch {appId} {args}".Trim()) { UseShellExecute = false });
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(game.ExePath, args) { WorkingDirectory = game.Path, UseShellExecute = false });
            return;
        }

        // Copia fuera de Steam en Linux: se lanza con Wine y el override que BepInEx necesita para cargar.
        var wine = GameLocator.FindInPath("wine") ?? throw new InvalidOperationException(Loc.T("err.noWine"));
        var start = new ProcessStartInfo(wine) { WorkingDirectory = game.Path, UseShellExecute = false };
        start.ArgumentList.Add(game.ExePath);
        if (!withMods) foreach (var a in VanillaArgs.Split(' ')) start.ArgumentList.Add(a);
        start.Environment["WINEDLLOVERRIDES"] = SteamLaunchOptions.DllOverride;
        Process.Start(start);
    }

    public static bool IsRunning(GameInstall game) =>
        OperatingSystem.IsWindows() ? IsRunningWindows(game) : IsRunningLinux(game);

    private static bool IsRunningWindows(GameInstall game)
    {
        var processes = Process.GetProcessesByName(game.ExeName);
        try
        {
            return processes.Any(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, game.ExePath, StringComparison.OrdinalIgnoreCase); }
                catch (System.ComponentModel.Win32Exception) { return true; } // sin permiso para leer la ruta: asumimos que es este
            });
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    // Bajo Wine el nombre del proceso es el del cargador; el .exe del juego aparece en su línea de comandos.
    private static bool IsRunningLinux(GameInstall game)
    {
        var exe = Path.GetFileName(game.ExePath);
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out _)) continue;
            try
            {
                if (File.ReadAllText(Path.Combine(dir, "cmdline")).Contains(exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // El proceso terminó mientras se leía o no es nuestro: no interesa.
            }
        }
        return false;
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else
            OpenWithDesktop(path);
    }

    public static void OpenUrl(string url)
    {
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else OpenWithDesktop(url);
    }

    private static void OpenWithDesktop(string target)
    {
        var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(target);
        Process.Start(start);
    }
}
