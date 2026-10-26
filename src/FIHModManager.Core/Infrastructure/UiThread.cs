namespace FIHModManager.Infrastructure;

/// <summary>
/// Hilo de interfaz independiente del framework (WPF o Avalonia): se captura su SynchronizationContext
/// al crear el ViewModel principal y se usa para temporizadores y avisos que vienen de otros hilos.
/// </summary>
public static class UiThread
{
    private static SynchronizationContext? _context;

    // Un Timer sin referencias puede ser recolectado antes de disparar; se mantienen vivos aquí.
    private static readonly HashSet<Timer> OneShots = [];

    public static void Capture() =>
        _context = SynchronizationContext.Current
                   ?? throw new InvalidOperationException("UiThread.Capture must run on the UI thread.");

    public static void Post(Action action)
    {
        if (_context is null) action();
        else _context.Post(_ => action(), null);
    }

    public static IDisposable Every(TimeSpan interval, Action action) =>
        new Timer(_ => Post(action), null, interval, interval);

    public static void After(TimeSpan delay, Action action)
    {
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            lock (OneShots) OneShots.Remove(timer!);
            timer!.Dispose();
            Post(action);
        });
        lock (OneShots) OneShots.Add(timer);
        timer.Change(delay, Timeout.InfiniteTimeSpan);
    }
}
