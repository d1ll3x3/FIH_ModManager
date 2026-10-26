using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FIHModManager.Infrastructure;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        CommandHub.InvalidateAll();
    }

    /// <summary>Re-evalúa todos los bindings del objeto (p. ej. tras cambiar de idioma).</summary>
    public void RefreshAll() => OnPropertyChanged(string.Empty);

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
