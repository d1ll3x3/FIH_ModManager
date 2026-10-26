using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FIHModManager.Infrastructure;
using FIHModManager.ViewModels;
using FIHModManager.Views;

namespace FIHModManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new AvaloniaPlatform(this));
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelChanged;
        ShowPage(_vm.CurrentPage);
        ApplyLanguage();
        Loc.Instance.LanguageChanged += ApplyLanguage;

        Opened += async (_, _) => await _vm.InitializeAsync();

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void ShowUnexpectedError(Exception ex) =>
        _vm.ShowToast(Loc.T("err.unexpected", ex.Message, Path.Combine(Log.DataDir, "manager.log")), ToastKind.Error);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage)) ShowPage(_vm.CurrentPage);
        if (e.PropertyName == nameof(MainViewModel.Dialog) && _vm.Dialog is { ShowInput: true })
            Dispatcher.UIThread.Post(() => DialogInput.Focus(), DispatcherPriority.Input);
    }

    private void ShowPage(string page) => PageHost.Content = page switch
    {
        "Mods" => new ModsView(),
        "Explore" => new ExploreView(),
        "Config" => new ConfigView(),
        "Profiles" => new ProfilesView(),
        "Settings" => new SettingsView(),
        _ => new HomeView(),
    };

    // Árabe se dibuja de derecha a izquierda.
    private void ApplyLanguage() =>
        FlowDirection = Loc.Instance.IsRightToLeft ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = files;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        if (paths.Count == 0) return;
        _vm.CurrentPage = "Mods";
        await _vm.InstallFilesAsync(paths);
    }
}
