using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using FIHModManager.Infrastructure;
using FIHModManager.Models;
using FIHModManager.Services;
using Microsoft.Win32;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = JsonStore.LoadSettings();
    private readonly DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ManagerState _state = new();

    private GameInstall? _selectedGame;
    private string _currentPage = "Home";
    private bool _isBusy;
    private string _busyText = "";
    private double _progress = -1;
    private bool _isGameRunning;
    private BepInExStatus _bepinex = new(BepInExState.NotInstalled, null, false);
    private DialogViewModel? _dialog;

    public MainViewModel()
    {
        Loc.Instance.SetLanguage(_settings.Language);
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        InstallBepInExCommand = new AsyncCommand(InstallBepInExAsync, () => HasGame && !IsBusy);
        UninstallBepInExCommand = new AsyncCommand(UninstallBepInExAsync, () => HasGame && !IsBusy && BepInExInstalled);
        PlayCommand = new RelayCommand(() => Launch(true), () => HasGame && !IsGameRunning);
        PlayVanillaCommand = new RelayCommand(() => Launch(false), () => HasGame && !IsGameRunning);
        NavigateCommand = new RelayCommand(p => CurrentPage = p as string ?? "Home");
        OpenGameFolderCommand = new RelayCommand(() => GameLauncher.OpenFolder(SelectedGame!.Path), () => HasGame);
        AddGameFolderCommand = new RelayCommand(AddGameFolder);
        RemoveGameCommand = new RelayCommand(p => RemoveGame((GameInstall)p!), p => p is GameInstall { Origin: "Manual" });
        RefreshCommand = new AsyncCommand(RefreshAllAsync);
        CheckAppUpdateCommand = new AsyncCommand(() => CheckAppUpdateAsync(silent: false), () => !IsBusy);

        InitMods();
        InitExplore();
        InitConfig();
        InitProfiles();

        // RequerySuggested solo salta con eventos de entrada; tras operaciones async los botones se quedarían grises.
        PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();

        _runningTimer.Tick +=(_, _) => IsGameRunning = SelectedGame is not null && GameLauncher.IsRunning(SelectedGame);
        _runningTimer.Start();
    }

    // ---------- Estado general ----------

    public ObservableCollection<GameInstall> Games { get; } = [];
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public GameInstall? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!Set(ref _selectedGame, value)) return;
            OnPropertyChanged(nameof(HasGame));
            if (value is not null)
            {
                _settings.LastGamePath = value.Path;
                JsonStore.SaveSettings(_settings);
            }
            _ = RefreshGameAsync();
        }
    }

    public bool HasGame => SelectedGame is not null;

    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!Set(ref _currentPage, value)) return;
            if (value == "Explore") _ = LoadCatalogAsync(force: false);
            if (value == "Config") LoadConfigFiles();
        }
    }

    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

    /// <summary>0..1, o -1 para progreso indeterminado.</summary>
    public double Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value)) OnPropertyChanged(nameof(IsIndeterminate));
        }
    }

    public bool IsIndeterminate => Progress < 0;

    public bool IsGameRunning { get => _isGameRunning; private set => Set(ref _isGameRunning, value); }

    public DialogViewModel? Dialog { get => _dialog; private set => Set(ref _dialog, value); }

    // ---------- BepInEx ----------

    public BepInExStatus BepInEx
    {
        get => _bepinex;
        private set
        {
            if (!Set(ref _bepinex, value)) return;
            OnPropertyChanged(nameof(BepInExInstalled));
            OnPropertyChanged(nameof(BepInExStatusText));
            OnPropertyChanged(nameof(BepInExDetailText));
            OnPropertyChanged(nameof(BepInExNeedsInstall));
            OnPropertyChanged(nameof(BepInExOutdated));
            OnPropertyChanged(nameof(FirstLaunchPending));
        }
    }

    public string? BundledVersion => BepInExService.BundledVersion;
    public string BundledText => Loc.T("home.bundled", BundledVersion);

    // ---------- Idioma ----------

    public IReadOnlyList<LanguageInfo> Languages => Loc.Languages;

    public LanguageInfo SelectedLanguage
    {
        get => Loc.Instance.Language;
        set
        {
            if (value is null || value == Loc.Instance.Language) return;
            _settings.Language = value.Code;
            JsonStore.SaveSettings(_settings);
            Loc.Instance.SetLanguage(value.Code);
        }
    }

    /// <summary>Los textos calculados en los ViewModels no escuchan a Loc: se refrescan aquí.</summary>
    private void OnLanguageChanged()
    {
        RefreshAll();
        foreach (var m in Mods) m.RefreshAll();
        foreach (var c in Catalog) c.RefreshAll();
        foreach (var p in Profiles) p.RefreshAll();
        foreach (var e in SelectedConfig?.Sections.SelectMany(s => s.Entries) ?? []) e.RefreshAll();
    }
    public bool BepInExInstalled => BepInEx.State != BepInExState.NotInstalled;
    public bool BepInExNeedsInstall => BepInEx.State != BepInExState.Installed;
    public bool BepInExOutdated => BepInEx.State == BepInExState.Installed && BepInEx.Version is not null && BepInEx.Version != BundledVersion;
    public bool FirstLaunchPending => BepInEx.State == BepInExState.Installed && !BepInEx.InteropGenerated;

    public string BepInExStatusText => BepInEx.State switch
    {
        BepInExState.Installed => Loc.T("bep.installed"),
        BepInExState.Incomplete => Loc.T("bep.incomplete"),
        _ => Loc.T("bep.notInstalled"),
    };

    public string BepInExDetailText => BepInEx.State switch
    {
        BepInExState.Installed when BepInExOutdated => Loc.T("bep.detail.outdated", BepInEx.Version, BundledVersion),
        BepInExState.Installed => Loc.T("bep.detail.version", BepInEx.Version ?? Loc.T("common.unknown")),
        BepInExState.Incomplete => Loc.T("bep.detail.incomplete"),
        _ => Loc.T("bep.detail.notInstalled", BundledVersion),
    };

    public AsyncCommand InstallBepInExCommand { get; }
    public AsyncCommand UninstallBepInExCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand PlayVanillaCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand OpenGameFolderCommand { get; }
    public RelayCommand AddGameFolderCommand { get; }
    public RelayCommand RemoveGameCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand CheckAppUpdateCommand { get; }

    public string AppVersionText => Loc.T("app.version", SelfUpdateService.CurrentVersionText);

    // ---------- Arranque ----------

    public async Task InitializeAsync()
    {
        await RunBusyAsync(Loc.T("busy.search"), async () =>
        {
            var games = await Task.Run(() => GameLocator.FindAll(_settings.CustomGamePaths));
            Games.Clear();
            foreach (var g in games) Games.Add(g);
        });

        SelectedGame = Games.FirstOrDefault(g => string.Equals(g.Path, _settings.LastGamePath, StringComparison.OrdinalIgnoreCase))
                       ?? Games.FirstOrDefault();
        if (SelectedGame is null)
            ShowToast(Loc.T("toast.noGame"), ToastKind.Info);

        _ = SelfUpdateService.CleanupAsync();
        await CheckAppUpdateAsync(silent: true);
    }

    // ---------- Actualización del propio programa ----------

    private async Task CheckAppUpdateAsync(bool silent)
    {
        GitHubRelease? release;
        try
        {
            release = await SelfUpdateService.CheckAsync(force: !silent);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("Buscar actualización del programa", ex);
            if (!silent) ShowToast(FriendlyError(ex), ToastKind.Error);
            return;
        }

        if (release is null)
        {
            if (!silent) ShowToast(Loc.T("toast.appUpToDate", SelfUpdateService.CurrentVersionText), ToastKind.Success);
            return;
        }

        // Las notas de GitHub vienen en Markdown; el diálogo es texto plano.
        var plain = System.Text.RegularExpressions.Regex.Replace(release.Body, @"\*\*|`|^#+\s*", "",
            System.Text.RegularExpressions.RegexOptions.Multiline).Trim();
        var notes = plain.Length > 600 ? plain[..600] + "…" : plain;
        var ok = await ShowDialogAsync(new DialogViewModel
        {
            Title = Loc.T("upd.available.title", release.Tag),
            Message = Loc.T("upd.available.msg", SelfUpdateService.CurrentVersionText) + (notes.Length > 0 ? "\n\n" + notes : ""),
            PrimaryText = Loc.T("upd.now"),
            SecondaryText = Loc.T("upd.later"),
        });
        if (!ok) return;

        await RunBusyAsync(Loc.T("busy.updating", release.Tag), async () =>
        {
            try
            {
                await SelfUpdateService.DownloadAndRestartAsync(release, new Progress<double>(p => Progress = p));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
            {
                Log.Error("Actualizar el programa", ex);
                ShowToast(Loc.T("toast.updateFailed", FriendlyError(ex)), ToastKind.Error);
            }
        });
    }

    private async Task RefreshAllAsync()
    {
        var current = SelectedGame?.Path;
        var games = await Task.Run(() => GameLocator.FindAll(_settings.CustomGamePaths));
        Games.Clear();
        foreach (var g in games) Games.Add(g);
        _selectedGame = null;
        SelectedGame = Games.FirstOrDefault(g => g.Path == current) ?? Games.FirstOrDefault();
    }

    private async Task RefreshGameAsync()
    {
        if (SelectedGame is not { } game)
        {
            Mods.Clear();
            Profiles.Clear();
            BepInEx = new BepInExStatus(BepInExState.NotInstalled, null, false);
            return;
        }

        IsGameRunning = GameLauncher.IsRunning(game);
        BepInEx = await Task.Run(() => BepInExService.GetStatus(game));
        _state = JsonStore.LoadState(game);
        await ReloadModsAsync();
        LoadProfiles();
        if (CurrentPage == "Config") LoadConfigFiles();
        _ = CheckUpdatesAsync(silent: true);
    }

    // ---------- Acciones de BepInEx ----------

    private async Task InstallBepInExAsync()
    {
        var game = SelectedGame!;
        if (!EnsureGameClosed()) return;

        if (BepInEx.State == BepInExState.Installed && !BepInExOutdated)
        {
            var ok = await ShowDialogAsync(new DialogViewModel
            {
                Title = Loc.T("dlg.reinstall.title"),
                Message = Loc.T("dlg.reinstall.msg"),
                PrimaryText = Loc.T("common.reinstall"),
            });
            if (!ok) return;
        }

        await RunBusyAsync(Loc.T("busy.installBep", BundledVersion), async () =>
        {
            var progress = new Progress<double>(p => Progress = p);
            await Task.Run(() => BepInExService.Install(game, progress));
            BepInEx = BepInExService.GetStatus(game);
            ShowToast(Loc.T("toast.bepInstalled"), ToastKind.Success);
        });
    }

    private async Task UninstallBepInExAsync()
    {
        var game = SelectedGame!;
        if (!EnsureGameClosed()) return;
        var ok = await ShowDialogAsync(new DialogViewModel
        {
            Title = Loc.T("dlg.uninstall.title"),
            Message = Loc.T("dlg.uninstall.msg"),
            PrimaryText = Loc.T("common.uninstall"),
            IsDanger = true,
        });
        if (!ok) return;

        await RunBusyAsync(Loc.T("busy.uninstallBep"), async () =>
        {
            await Task.Run(() => BepInExService.Uninstall(game));
            BepInEx = BepInExService.GetStatus(game);
            ShowToast(Loc.T("toast.bepUninstalled"), ToastKind.Success);
        });
    }

    private void Launch(bool withMods)
    {
        var game = SelectedGame!;
        if (withMods && BepInEx.State != BepInExState.Installed)
            ShowToast(Loc.T("toast.launchNoBep"), ToastKind.Info);

        try
        {
            GameLauncher.Launch(game, withMods);
            ShowToast(Loc.T(withMods ? "toast.launchMods" : "toast.launchVanilla"), ToastKind.Success);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Lanzar juego", ex);
            ShowToast(Loc.T("toast.launchFailed", ex.Message), ToastKind.Error);
        }
    }

    // ---------- Ajustes: instalaciones ----------

    private void AddGameFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("picker.gameFolder") };
        if (dialog.ShowDialog() != true) return;

        var game = GameLocator.TryCreate(dialog.FolderName, "Manual");
        if (game is null)
        {
            ShowToast(Loc.T("toast.notGameFolder"), ToastKind.Error);
            return;
        }

        if (!_settings.CustomGamePaths.Contains(game.Path, StringComparer.OrdinalIgnoreCase))
        {
            _settings.CustomGamePaths.Add(game.Path);
            JsonStore.SaveSettings(_settings);
        }

        var existing = Games.FirstOrDefault(g => string.Equals(g.Path, game.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is null) Games.Add(existing = game);
        SelectedGame = existing;
        ShowToast(Loc.T("toast.gameAdded", game.Name), ToastKind.Success);
    }

    private void RemoveGame(GameInstall game)
    {
        _settings.CustomGamePaths.RemoveAll(p => string.Equals(p, game.Path, StringComparison.OrdinalIgnoreCase));
        JsonStore.SaveSettings(_settings);
        Games.Remove(game);
        if (SelectedGame == game) SelectedGame = Games.FirstOrDefault();
    }

    // ---------- Utilidades compartidas ----------

    private bool EnsureGameClosed()
    {
        if (SelectedGame is null || !GameLauncher.IsRunning(SelectedGame)) return true;
        ShowToast(Loc.T("toast.closeGame"), ToastKind.Error);
        return false;
    }

    private async Task RunBusyAsync(string text, Func<Task> action)
    {
        IsBusy = true;
        BusyText = text;
        Progress = -1;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error(text, ex);
            ShowToast(FriendlyError(ex), ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
            Progress = -1;
        }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => Loc.T("err.noPermission"),
        IOException io when io.HResult == unchecked((int)0x80070020) => Loc.T("err.fileInUse"),
        GitHubRateLimitException rateLimit => rateLimit.Message,
        HttpRequestException http => Loc.T("err.network", http.Message),
        _ => ex.Message,
    };

    public void ShowToast(string message, ToastKind kind)
    {
        var toast = new ToastViewModel(message, kind);
        Toasts.Add(toast);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 7 : 4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();
    }

    public RelayCommand DismissToastCommand => new(p => Toasts.Remove((ToastViewModel)p!));

    private async Task<bool> ShowDialogAsync(DialogViewModel dialog)
    {
        Dialog = dialog;
        try { return await dialog.Result; }
        finally { Dialog = null; }
    }

}
