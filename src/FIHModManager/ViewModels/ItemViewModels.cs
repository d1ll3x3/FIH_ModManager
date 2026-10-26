using System.Collections.ObjectModel;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.ViewModels;

public sealed class ModItemViewModel(ModEntry entry, ModSource? source, string? configPath, Func<ModItemViewModel, bool, Task> toggle)
    : ObservableObject
{
    private bool _enabled = entry.Enabled;
    private string? _latestTag;

    public ModEntry Entry { get; } = entry;
    public ModSource? Source { get; } = source;
    public string? ConfigPath { get; } = configPath;

    public string Title => Entry.Plugins.FirstOrDefault()?.Name is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(Entry.Key);
    public string Version => Entry.Plugins.FirstOrDefault()?.Version is { Length: > 0 } v ? "v" + v : "";
    public string Guid => Entry.Plugins.FirstOrDefault()?.Guid ?? Entry.Key;
    public string ExtraPlugins => Entry.Plugins.Count > 1 ? Loc.T("mod.extraPlugins", Entry.Plugins.Count - 1) : "";
    public bool IsGitHub => Source is not null;
    public bool HasConfig => ConfigPath is not null;

    public string? Warning => Entry.Kind switch
    {
        _ when Entry.RenamedDlls.Count > 0 => null,
        LoaderKind.BepInEx5 => Loc.T("mod.warn.bep5"),
        LoaderKind.BepInEx6Mono => Loc.T("mod.warn.mono"),
        LoaderKind.Library when Entry.Dlls.Count == 0 => Loc.T("mod.warn.noDll"),
        LoaderKind.Library => Loc.T("mod.warn.library"),
        _ => null,
    };

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;
            _ = toggle(this, value);
        }
    }

    /// <summary>Lo llama el ViewModel principal cuando el mover de carpeta ha ido bien (o para revertir el switch).</summary>
    public void SetEnabledState(bool value)
    {
        _enabled = value;
        OnPropertyChanged(nameof(IsEnabled));
    }

    public string? LatestTag
    {
        get => _latestTag;
        set
        {
            if (!Set(ref _latestTag, value)) return;
            OnPropertyChanged(nameof(UpdateAvailable));
            OnPropertyChanged(nameof(UpdateText));
        }
    }

    public string UpdateText => Loc.T("mod.updateTo", LatestTag);

    public bool UpdateAvailable => Source is not null && LatestTag is not null && !string.Equals(LatestTag, Source.Tag, StringComparison.OrdinalIgnoreCase);

    public bool Matches(string filter) =>
        string.IsNullOrWhiteSpace(filter)
        || Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Guid.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Entry.Key.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

public sealed class CatalogItemViewModel(CatalogEntry entry) : ObservableObject
{
    private GitHubRelease? _release;
    private bool _loading = true;
    private string? _error;
    private ModItemViewModel? _installed;

    public CatalogEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public string Description => Loc.T("cat.desc." + Entry.Repo.Split('/')[1]);
    public string Icon => Entry.Icon;
    public string Repo => Entry.Repo;

    public GitHubRelease? Release
    {
        get => _release;
        set
        {
            if (Set(ref _release, value)) Refresh();
        }
    }

    public bool Loading
    {
        get => _loading;
        set
        {
            if (Set(ref _loading, value)) Refresh();
        }
    }
    public string? Error { get => _error; set => Set(ref _error, value); }

    public ModItemViewModel? Installed
    {
        get => _installed;
        set
        {
            if (Set(ref _installed, value)) Refresh();
        }
    }

    public string LatestText => Release is null ? Loc.T(Loading ? "cat.loading" : "cat.noReleases") : Release.Tag;
    public bool IsInstalled => Installed is not null;
    public bool CanUpdate => Installed?.Source is { } s && Release is not null && !string.Equals(s.Tag, Release.Tag, StringComparison.OrdinalIgnoreCase);

    public string ActionText => Loc.T(Installed is null ? "common.install" : CanUpdate ? "common.update" : Installed.Source is null ? "cat.installGitHub" : "common.reinstall");

    private void Refresh()
    {
        OnPropertyChanged(nameof(LatestText));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(ActionText));
    }
}

public sealed class ProfileItemViewModel(ModProfile profile, IReadOnlyCollection<string> installedKeys) : ObservableObject
{
    public ModProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Summary => Profile.EnabledKeys.Count == 0 ? Loc.T("prof.noMods") : Loc.T("prof.activeCount", Profile.EnabledKeys.Count);
    public string ModsText => Profile.EnabledKeys.Count == 0 ? "—" : string.Join("  ·  ", Profile.EnabledKeys.Select(Path.GetFileNameWithoutExtension));
    public int MissingCount => Profile.EnabledKeys.Count(k => !installedKeys.Contains(k, StringComparer.OrdinalIgnoreCase));
    public string MissingText => Loc.T("prof.missing", MissingCount);
    public string SavedText => Loc.T("prof.saved", Profile.SavedAt.ToString("g", Loc.Instance.Culture));
}

public enum ToastKind { Info, Success, Error }

public sealed record ToastViewModel(string Message, ToastKind Kind)
{
    public string Glyph => Kind switch { ToastKind.Success => "\uE73E", ToastKind.Error => "\uE783", _ => "\uE946" };
}

/// <summary>Diálogo modal que se pinta dentro de la ventana (overlay), no como ventana aparte.</summary>
public sealed class DialogViewModel : ObservableObject
{
    private string _input = "";
    private object? _selectedChoice;
    private readonly TaskCompletionSource<bool> _tcs = new();

    public required string Title { get; init; }
    public string Message { get; init; } = "";
    public string PrimaryText { get; init; } = Loc.T("common.ok");
    public string? SecondaryText { get; init; } = Loc.T("common.cancel");
    public bool IsDanger { get; init; }
    public bool ShowInput { get; init; }
    public string InputPlaceholder { get; init; } = "";
    public ObservableCollection<object>? Choices { get; init; }

    public DialogViewModel()
    {
        PrimaryCommand = new RelayCommand(() => _tcs.TrySetResult(true),
            () => (!ShowInput || !string.IsNullOrWhiteSpace(Input)) && (Choices is null || SelectedChoice is not null));
        SecondaryCommand = new RelayCommand(() => _tcs.TrySetResult(false));
    }

    // Se fuerza la re-evaluación del botón al escribir/elegir; si no, Enter llega con el botón aún deshabilitado.
    public string Input
    {
        get => _input;
        set
        {
            if (Set(ref _input, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public object? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (Set(ref _selectedChoice, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public Task<bool> Result => _tcs.Task;

    public RelayCommand PrimaryCommand { get; }
    public RelayCommand SecondaryCommand { get; }
}
