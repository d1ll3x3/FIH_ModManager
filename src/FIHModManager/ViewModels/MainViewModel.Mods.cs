using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using FIHModManager.Infrastructure;
using FIHModManager.Models;
using FIHModManager.Services;
using Microsoft.Win32;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    private string _modFilter = "";

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ICollectionView ModsView { get; private set; } = null!;

    public int EnabledModCount => Mods.Count(m => m.IsEnabled);
    public int UpdateCount => Mods.Count(m => m.UpdateAvailable);
    public string ModsActiveText => Loc.T("mods.activeOfTotal", EnabledModCount, Mods.Count);
    public string ModsInstalledText => Loc.T("mods.installedSummary", EnabledModCount, Mods.Count);
    public string UpdatesText => Loc.T("home.updates", UpdateCount);

    public string ModFilter
    {
        get => _modFilter;
        set
        {
            if (Set(ref _modFilter, value)) ModsView.Refresh();
        }
    }

    public AsyncCommand AddModFileCommand { get; private set; } = null!;
    public AsyncCommand CheckUpdatesCommand { get; private set; } = null!;
    public AsyncCommand DeleteModCommand { get; private set; } = null!;
    public AsyncCommand UpdateModCommand { get; private set; } = null!;
    public RelayCommand OpenModFolderCommand { get; private set; } = null!;
    public RelayCommand OpenPluginsFolderCommand { get; private set; } = null!;
    public RelayCommand OpenModConfigCommand { get; private set; } = null!;

    private void InitMods()
    {
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = o => ((ModItemViewModel)o).Matches(ModFilter);
        ModsView.SortDescriptions.Add(new SortDescription(nameof(ModItemViewModel.Title), ListSortDirection.Ascending));

        AddModFileCommand = new AsyncCommand(AddModFromFileAsync, () => HasGame && !IsBusy);
        CheckUpdatesCommand = new AsyncCommand(() => CheckUpdatesAsync(silent: false), () => HasGame && !IsBusy);
        DeleteModCommand = new AsyncCommand(p => DeleteModAsync((ModItemViewModel)p!));
        UpdateModCommand = new AsyncCommand(p => UpdateModAsync((ModItemViewModel)p!), _ => !IsBusy);
        OpenModFolderCommand = new RelayCommand(p =>
        {
            var mod = ((ModItemViewModel)p!).Entry;
            GameLauncher.OpenFolder(mod.IsDirectory ? mod.FullPath : Path.GetDirectoryName(mod.FullPath)!);
        });
        OpenPluginsFolderCommand = new RelayCommand(() => GameLauncher.OpenFolder(SelectedGame!.PluginsDir), () => HasGame);
        OpenModConfigCommand = new RelayCommand(p =>
        {
            CurrentPage = "Config";
            SelectedConfig = ConfigFiles.FirstOrDefault(c => c.Path == ((ModItemViewModel)p!).ConfigPath);
        });
    }

    private async Task ReloadModsAsync()
    {
        var game = SelectedGame;
        if (game is null) return;
        var entries = await Task.Run(() => ModService.Scan(game));
        if (game != SelectedGame) return;

        var configs = Directory.Exists(game.ConfigDir)
            ? Directory.GetFiles(game.ConfigDir).ToDictionary(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            : [];

        // Se conserva la última versión consultada en GitHub para no perder el aviso de actualización.
        var latest = Mods.Where(m => m.LatestTag is not null).ToDictionary(m => m.Entry.Key, m => m.LatestTag, StringComparer.OrdinalIgnoreCase);

        Mods.Clear();
        foreach (var e in entries)
        {
            _state.Sources.TryGetValue(e.Key, out var source);
            var cfg = e.Guids.Select(g => configs.GetValueOrDefault(g)).FirstOrDefault(c => c is not null);
            var vm = new ModItemViewModel(e, source, cfg, ToggleModAsync);
            if (latest.TryGetValue(e.Key, out var tag)) vm.LatestTag = tag;
            Mods.Add(vm);
        }

        // Fuentes de mods que ya no existen (borrados a mano) se limpian.
        var keys = entries.Select(e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_state.Sources.Keys.Any(k => !keys.Contains(k)))
        {
            foreach (var k in _state.Sources.Keys.Where(k => !keys.Contains(k)).ToList()) _state.Sources.Remove(k);
            JsonStore.SaveState(game, _state);
        }

        RaiseModCounts();
        SyncCatalogInstalled();
    }

    private void RaiseModCounts()
    {
        OnPropertyChanged(nameof(EnabledModCount));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(ModsActiveText));
        OnPropertyChanged(nameof(ModsInstalledText));
        OnPropertyChanged(nameof(UpdatesText));
    }

    private async Task ToggleModAsync(ModItemViewModel mod, bool enabled)
    {
        if (!EnsureGameClosed())
        {
            mod.SetEnabledState(mod.IsEnabled);
            return;
        }

        try
        {
            ModService.SetEnabled(SelectedGame!, mod.Entry, enabled);
            mod.SetEnabledState(enabled);
            ShowToast(Loc.T(enabled ? "toast.modEnabled" : "toast.modDisabled", mod.Title), ToastKind.Success);
        }
        catch (IOException ex)
        {
            Log.Error($"Cambiar estado de {mod.Entry.Key}", ex);
            mod.SetEnabledState(!enabled);
            ShowToast(FriendlyError(ex), ToastKind.Error);
        }
        await ReloadModsAsync();
    }

    private async Task AddModFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("picker.mods"),
            Filter = Loc.T("picker.modsFilter"),
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;
        await InstallFilesAsync(dialog.FileNames);
    }

    /// <summary>Instala archivos locales (botón o arrastrar y soltar).</summary>
    public async Task InstallFilesAsync(IReadOnlyList<string> paths)
    {
        if (SelectedGame is not { } game)
        {
            ShowToast(Loc.T("toast.selectGame"), ToastKind.Error);
            return;
        }
        if (!EnsureGameClosed()) return;

        await RunBusyAsync(Loc.T("busy.installMods"), async () =>
        {
            var installedNames = new List<string>();
            foreach (var path in paths)
            {
                BusyText = Loc.T("busy.installing", Path.GetFileName(path));
                var current = Mods.Select(m => m.Entry).ToList();
                var keys = await Task.Run(() => ModService.Install(game, path, current));
                // Instalar a mano sustituye a la versión de GitHub: se deja de seguir su repo.
                foreach (var k in keys) _state.Sources.Remove(k);
                installedNames.AddRange(keys);
                await ReloadModsAsync();
            }
            JsonStore.SaveState(game, _state);
            ShowToast(Loc.T("toast.installed", string.Join(", ", installedNames)), ToastKind.Success);
            if (BepInEx.State != BepInExState.Installed)
                ShowToast(Loc.T("toast.needBep"), ToastKind.Info);
        });
    }

    private async Task DeleteModAsync(ModItemViewModel mod)
    {
        if (!EnsureGameClosed()) return;
        var ok = await ShowDialogAsync(new DialogViewModel
        {
            Title = Loc.T("dlg.delete.title", mod.Title),
            Message = Loc.T("dlg.delete.msg", mod.Entry.Key),
            PrimaryText = Loc.T("common.delete"),
            IsDanger = true,
        });
        if (!ok) return;

        await RunBusyAsync(Loc.T("busy.deleting", mod.Title), async () =>
        {
            await Task.Run(() => ModService.Delete(mod.Entry));
            _state.Sources.Remove(mod.Entry.Key);
            JsonStore.SaveState(SelectedGame!, _state);
            await ReloadModsAsync();
            ShowToast(Loc.T("toast.deleted", mod.Title), ToastKind.Success);
        });
    }

    private async Task UpdateModAsync(ModItemViewModel mod)
    {
        if (mod.Source is null) return;
        await InstallFromRepoAsync(mod.Source.Repo, forceRefresh: false);
    }

    private async Task CheckUpdatesAsync(bool silent)
    {
        var tracked = Mods.Where(m => m.Source is not null).ToList();
        if (tracked.Count == 0)
        {
            if (!silent) ShowToast(Loc.T("toast.noTracked"), ToastKind.Info);
            return;
        }

        try
        {
            foreach (var mod in tracked)
            {
                var release = await GitHubService.GetLatestReleaseAsync(mod.Source!.Repo, force: !silent);
                mod.LatestTag = release?.Tag;
            }
            RaiseModCounts();
            if (!silent)
                ShowToast(UpdateCount == 0 ? Loc.T("toast.upToDate") : Loc.T("toast.updatesAvailable", UpdateCount), UpdateCount == 0 ? ToastKind.Success : ToastKind.Info);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("Buscar actualizaciones", ex);
            if (!silent) ShowToast(FriendlyError(ex), ToastKind.Error);
        }
    }
}
