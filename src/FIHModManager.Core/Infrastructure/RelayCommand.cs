using System.Windows.Input;

namespace FIHModManager.Infrastructure;

internal interface IRaisableCommand
{
    void RaiseCanExecuteChanged();
}

/// <summary>
/// Sustituye a CommandManager.RequerySuggested de WPF, que no existe en Avalonia: cualquier cambio de
/// propiedad en un ViewModel pide re-evaluar CanExecute de todos los comandos vivos (agrupado en un solo
/// aviso por ciclo del hilo de interfaz). Las referencias son débiles para no retener ViewModels viejos.
/// </summary>
public static class CommandHub
{
    private static readonly List<WeakReference<IRaisableCommand>> Commands = [];
    private static int _pending;

    internal static void Register(IRaisableCommand command)
    {
        lock (Commands) Commands.Add(new WeakReference<IRaisableCommand>(command));
    }

    public static void InvalidateAll()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        UiThread.Post(() =>
        {
            Interlocked.Exchange(ref _pending, 0);
            List<IRaisableCommand> live = [];
            lock (Commands)
            {
                Commands.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in Commands)
                    if (w.TryGetTarget(out var c)) live.Add(c);
            }
            foreach (var c in live) c.RaiseCanExecuteChanged();
        });
    }
}

public sealed class RelayCommand : ICommand, IRaisableCommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        CommandHub.Register(this);
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Comando asíncrono. Los errores no se tragan: el ViewModel envuelve la acción en RunBusyAsync,
/// que los registra y los muestra como toast.
/// </summary>
public sealed class AsyncCommand : ICommand, IRaisableCommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _running;

    public AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        CommandHub.Register(this);
    }

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public async void Execute(object? parameter)
    {
        _running = true;
        RaiseCanExecuteChanged();
        try { await _execute(parameter); }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }
}
