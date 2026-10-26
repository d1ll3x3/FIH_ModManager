using System.Diagnostics;
using System.Reflection;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

/// <summary>
/// Actualiza el propio ejecutable desde las releases de GitHub. Windows permite renombrar un .exe
/// en uso, así que se aparta el actual a .old, se coloca el nuevo y se relanza; el .old se borra
/// en el siguiente arranque.
/// </summary>
public static class SelfUpdateService
{
    public const string Repo = "d1ll3x3/FIH_ModManager";
    // Cada sistema descarga su propio binario de la misma release.
    public static string AssetName => OperatingSystem.IsWindows() ? "FIHModManager.exe" : "FIHModManager-linux-x64";

    public static Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentVersionText => $"v{CurrentVersion.ToString(3)}";

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath");

    /// <summary>Devuelve la release si es más nueva que la versión en ejecución.</summary>
    public static async Task<GitHubRelease?> CheckAsync(bool force)
    {
        var release = await GitHubService.GetLatestReleaseAsync(Repo, force);
        if (release is null || !Version.TryParse(release.Tag.TrimStart('v', 'V'), out var latest)) return null;
        var hasAsset = release.Assets.Any(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase));
        return latest > CurrentVersion && hasAsset ? release : null;
    }

    /// <summary>Coloca el nuevo ejecutable y lo lanza; quien llama debe cerrar la aplicación actual.</summary>
    public static async Task DownloadAndRestartAsync(GitHubRelease release, IProgress<double> progress)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(Loc.T("err.noUpdateAsset", AssetName));

        var exe = ExePath;
        var newFile = exe + ".new";
        var oldFile = exe + ".old";

        // Se descarga junto al exe (mismo disco) para que el cambio final sea un simple renombrado.
        await GitHubService.DownloadAsync(asset, newFile, progress);
        // En Linux el archivo descargado no es ejecutable: se copian los permisos del binario actual.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(newFile, File.GetUnixFileMode(exe));
        if (File.Exists(oldFile)) File.Delete(oldFile);
        File.Move(exe, oldFile);
        try
        {
            File.Move(newFile, exe);
        }
        catch
        {
            // Si no se pudo colocar el nuevo, se restaura el actual para no dejar el programa roto.
            File.Move(oldFile, exe);
            throw;
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    /// <summary>Borra el .old de una actualización anterior (el proceso viejo puede tardar en cerrarse).</summary>
    public static async Task CleanupAsync()
    {
        var oldFile = ExePath + ".old";
        for (var attempt = 0; attempt < 10 && File.Exists(oldFile); attempt++)
        {
            try
            {
                File.Delete(oldFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 9) Log.Error($"No se pudo borrar {oldFile}", ex);
                await Task.Delay(1000);
            }
        }
    }
}
