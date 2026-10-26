namespace FIHModManager.Infrastructure;

/// <summary>Lo poco que el ViewModel necesita de la interfaz concreta (WPF en Windows, Avalonia en Linux).</summary>
public interface IPlatform
{
    Task<string?> PickFolderAsync(string title);

    /// <summary>Devuelve las rutas elegidas, o una lista vacía si se cancela.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync(string title, string filterName, IReadOnlyList<string> extensions);

    void Shutdown();
}
