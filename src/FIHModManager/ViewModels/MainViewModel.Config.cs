using System.Collections.ObjectModel;
using FIHModManager.Infrastructure;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    private ConfigFileViewModel? _selectedConfig;

    public ObservableCollection<ConfigFileViewModel> ConfigFiles { get; } = [];

    public ConfigFileViewModel? SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            if (_selectedConfig == value) return;
            _selectedConfig = value;
            if (value is not null)
            {
                try
                {
                    value.Load();
                }
                catch (IOException ex)
                {
                    Log.Error($"Leer {value.Path}", ex);
                    ShowToast(FriendlyError(ex), ToastKind.Error);
                }
            }
            OnPropertyChanged();
        }
    }

    public RelayCommand SaveConfigCommand { get; private set; } = null!;
    public RelayCommand ReloadConfigCommand { get; private set; } = null!;
    public RelayCommand OpenConfigFolderCommand { get; private set; } = null!;

    private void InitConfig()
    {
        SaveConfigCommand = new RelayCommand(SaveConfig, () => SelectedConfig is { IsDirty: true });
        ReloadConfigCommand = new RelayCommand(() =>
        {
            SelectedConfig?.Load();
            ShowToast(Loc.T("toast.discarded"), ToastKind.Info);
        }, () => SelectedConfig is { IsDirty: true });
        OpenConfigFolderCommand = new RelayCommand(() => GameLauncher.OpenFolder(SelectedGame!.ConfigDir), () => HasGame);
    }

    private void LoadConfigFiles()
    {
        var previous = SelectedConfig?.Path;
        ConfigFiles.Clear();
        _selectedConfig = null;
        if (SelectedGame is not { } game || !Directory.Exists(game.ConfigDir))
        {
            OnPropertyChanged(nameof(SelectedConfig));
            return;
        }

        var modsByGuid = Mods.SelectMany(m => m.Entry.Plugins.Select(p => (p.Guid, m.Title)))
            .GroupBy(x => x.Guid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Title, StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(game.ConfigDir)
            .Where(f => f.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var isCore = stem.Equals("BepInEx", StringComparison.OrdinalIgnoreCase);
            var title = isCore ? "BepInEx" : modsByGuid.GetValueOrDefault(stem);
            ConfigFiles.Add(new ConfigFileViewModel(file, title ?? stem, title is null ? null : stem));
        }

        // BepInEx primero, luego los mods por nombre.
        var sorted = ConfigFiles.OrderBy(c => c.DisplayName != "BepInEx").ThenBy(c => c.DisplayName).ToList();
        ConfigFiles.Clear();
        foreach (var c in sorted) ConfigFiles.Add(c);

        SelectedConfig = ConfigFiles.FirstOrDefault(c => c.Path == previous) ?? ConfigFiles.FirstOrDefault();
    }

    private void SaveConfig()
    {
        try
        {
            SelectedConfig!.Save();
            ShowToast(Loc.T(IsGameRunning ? "toast.savedRunning" : "toast.saved"), ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Guardar {SelectedConfig!.Path}", ex);
            ShowToast(FriendlyError(ex), ToastKind.Error);
        }
    }
}
