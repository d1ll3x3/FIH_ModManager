using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using FIHModManager.Infrastructure;
using FIHModManager.ViewModels;
using FIHModManager.Views;

namespace FIHModManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        ApplyLanguage();
        Loc.Instance.LanguageChanged += ApplyLanguage;
        _vm.PropertyChanged += OnViewModelChanged;
        ShowPage(_vm.CurrentPage);

        SourceInitialized += (_, _) => ApplyWindowsChrome();
        StateChanged += (_, _) => OnStateChanged();
        Loaded += async (_, _) => await _vm.InitializeAsync();

        DragEnter += OnDragEnter;
        DragOver += OnDragEnter;
        DragLeave += (_, _) => DropOverlay.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage)) ShowPage(_vm.CurrentPage);
        if (e.PropertyName == nameof(MainViewModel.Dialog) && _vm.Dialog is { ShowInput: true })
            Dispatcher.BeginInvoke(() => DialogInput.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    // Cada visita crea la página de nuevo para que se reproduzca la animación de entrada.
    private void ShowPage(string page) => PageHost.Content = page switch
    {
        "Mods" => new ModsView(),
        "Explore" => new ExploreView(),
        "Config" => new ConfigView(),
        "Profiles" => new ProfilesView(),
        "Settings" => new SettingsView(),
        _ => new HomeView(),
    };

    // Árabe se dibuja de derecha a izquierda; Language hace que WPF elija la fuente adecuada para chino/coreano.
    private void ApplyLanguage()
    {
        FlowDirection = Loc.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Language = XmlLanguage.GetLanguage(Loc.Instance.Language.Culture);
    }

    // ---------- Arrastrar y soltar ----------

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        _vm.CurrentPage = "Mods";
        await _vm.InstallFilesAsync(paths);
    }

    // ---------- Ventana sin bordes ----------

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged()
    {
        // Maximizada, WindowChrome desborda la pantalla unos píxeles por cada lado.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void ApplyWindowsChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1, round = 2; // DWMWCP_ROUND
        // Fallan en silencio en Windows 10 antiguos; no afecta al funcionamiento.
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));  // DWMWA_USE_IMMERSIVE_DARK_MODE
        _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
