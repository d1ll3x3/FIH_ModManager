using System.Diagnostics;
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
        if (game.SteamAppId is { } appId && GameLocator.SteamExe is { } steam)
        {
            Process.Start(new ProcessStartInfo(steam, $"-applaunch {appId} {args}".TrimEnd()) { UseShellExecute = false });
            return;
        }

        Process.Start(new ProcessStartInfo(game.ExePath, args)
        {
            WorkingDirectory = game.Path,
            UseShellExecute = false,
        });
    }

    public static bool IsRunning(GameInstall game)
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

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
