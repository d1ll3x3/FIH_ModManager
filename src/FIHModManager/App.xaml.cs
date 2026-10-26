using System.Windows;
using System.Windows.Threading;
using FIHModManager.Infrastructure;

namespace FIHModManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Excepción no controlada", e.Exception);
        MessageBox.Show(Loc.T("err.unexpected", e.Exception.Message, Path.Combine(Log.DataDir, "manager.log")),
            "FIH Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
