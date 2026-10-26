using System.Windows;
using FIHModManager.Infrastructure;
using Microsoft.Win32;

namespace FIHModManager;

/// <summary>Diálogos y cierre de la aplicación con WPF.</summary>
public sealed class WpfPlatform(Window owner) : IPlatform
{
    public Task<string?> PickFolderAsync(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return Task.FromResult(dialog.ShowDialog(owner) == true ? dialog.FolderName : null);
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(string title, string filterName, IReadOnlyList<string> extensions)
    {
        var patterns = string.Join(";", extensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{filterName} ({patterns})|{patterns}|*.*|*.*",
            Multiselect = true,
        };
        IReadOnlyList<string> result = dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
        return Task.FromResult(result);
    }

    public void Shutdown() => Application.Current.Shutdown();
}
