using System.Collections.ObjectModel;
using FIHModManager.Infrastructure;
using FIHModManager.Models;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    private string _repoInput = "";
    private string _catalogFilter = "";
    private string _catalogCategory = "All";
    private FilterOption? _catalogAuthor;
    private string? _catalogError;
    private bool _catalogLoaded;
    private IReadOnlyList<CatalogGroup> _catalogGroups = [];

    /// <summary>Todos los mods conocidos: los de "Explorar" y los que solo salen en la pestaña Speedrun.</summary>
    public ObservableCollection<CatalogItemViewModel> Catalog { get; } = [];

    /// <summary>Catálogo de "Explorar" agrupado por categoría con la búsqueda y el filtro aplicados; las categorías vacías no aparecen.</summary>
    public IReadOnlyList<CatalogGroup> CatalogGroups
    {
        get => _catalogGroups;
        private set
        {
            if (Set(ref _catalogGroups, value)) OnPropertyChanged(nameof(NoCatalogMatches));
        }
    }

    public bool NoCatalogMatches => CatalogGroups.Count == 0;

    public string CatalogFilter
    {
        get => _catalogFilter;
        set
        {
            if (Set(ref _catalogFilter, value)) RefreshCatalogGroups();
        }
    }

    /// <summary>"All" o una categoría ("cosmetic", "graphics"…): las fichas de filtro de "Explorar".</summary>
    public string CatalogCategory
    {
        get => _catalogCategory;
        set
        {
            if (Set(ref _catalogCategory, value)) RefreshCatalogGroups();
        }
    }

    /// <summary>"Todos los creadores" y cada autor de "Explorar" con cuántos mods tiene.</summary>
    public IReadOnlyList<FilterOption> CatalogAuthors { get; private set; } = [];

    public FilterOption? CatalogAuthor
    {
        get => _catalogAuthor;
        set
        {
            if (Set(ref _catalogAuthor, value)) RefreshCatalogGroups();
        }
    }

    public string RepoInput { get => _repoInput; set => Set(ref _repoInput, value); }

    public string? CatalogError { get => _catalogError; private set => Set(ref _catalogError, value); }

    public AsyncCommand InstallCatalogCommand { get; private set; } = null!;
    public AsyncCommand InstallRepoInputCommand { get; private set; } = null!;
    public AsyncCommand RefreshCatalogCommand { get; private set; } = null!;
    public RelayCommand OpenRepoCommand { get; private set; } = null!;

    private void InitExplore()
    {
        InitSpeedrun();
        ApplyCatalog(CatalogService.LoadLocal());

        InstallCatalogCommand = new AsyncCommand(p => InstallFromRepoAsync(((CatalogItemViewModel)p!).Repo, forceRefresh: false), _ => HasGame && !IsBusy);
        InstallRepoInputCommand = new AsyncCommand(InstallFromInputAsync, () => HasGame && !IsBusy && !string.IsNullOrWhiteSpace(RepoInput));
        RefreshCatalogCommand = new AsyncCommand(async () =>
        {
            await UpdateCatalogAsync();
            await LoadCatalogAsync(force: true);
        });
        OpenRepoCommand = new RelayCommand(p => GameLauncher.OpenUrl($"https://github.com/{p}"));
    }

    /// <summary>Rehace las tarjetas de "Explorar" y Speedrun con otra lista de mods (la guardada o la recién descargada).</summary>
    private void ApplyCatalog(IReadOnlyList<CatalogEntry> entries)
    {
        Catalog.Clear();
        // Con la release guardada se reconocen los mods instalados a mano desde el arranque, sin esperar a GitHub.
        foreach (var entry in entries) Catalog.Add(new CatalogItemViewModel(entry) { Release = GitHubService.GetCachedRelease(entry.Repo) });
        _catalogLoaded = false;
        RefreshCatalogAuthors();
        RefreshCatalogGroups();
        RefreshSpeedrun();
        SyncCatalogInstalled();
    }

    /// <summary>Descarga el catálogo del repo; si ha cambiado, lo aplica.</summary>
    private async Task<bool> UpdateCatalogAsync()
    {
        if (await CatalogService.FetchAsync() is not { } entries) return false;
        ApplyCatalog(entries);
        return true;
    }

    // Al arrancar se enseña el catálogo guardado y el nuevo se pide en segundo plano: sin conexión no debe frenar nada.
    private async Task UpdateCatalogOnStartupAsync()
    {
        if (await UpdateCatalogAsync() && CurrentPage is "Explore" or "Speedrun")
            await LoadCatalogAsync(force: false);
    }

    // Se rehace al cambiar el catálogo o el idioma; el autor elegido se conserva si sigue en la lista.
    private void RefreshCatalogAuthors()
    {
        var selected = CatalogAuthor?.Value ?? "";
        CatalogAuthors =
        [
            new FilterOption("", Loc.T("explore.filter.allAuthors")),
            ..Catalog.Where(c => c.Entry.Explore)
                .GroupBy(c => c.Author, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new FilterOption(g.Key, $"{g.Key} ({g.Count()})")),
        ];
        OnPropertyChanged(nameof(CatalogAuthors));
        _catalogAuthor = CatalogAuthors.FirstOrDefault(a => string.Equals(a.Value, selected, StringComparison.OrdinalIgnoreCase)) ?? CatalogAuthors[0];
        OnPropertyChanged(nameof(CatalogAuthor));
    }

    private void RefreshCatalogGroups() =>
        CatalogGroups = Catalog.Where(c => c.Entry.Explore && c.Matches(CatalogFilter)
                                           && (CatalogCategory == "All" || c.Entry.Category == CatalogCategory)
                                           && (CatalogAuthor is not { Value.Length: > 0 } author || string.Equals(c.Author, author.Value, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(c => c.Entry.Category)
            .OrderBy(g => Array.IndexOf(CatalogService.Categories, g.Key))
            .Select(g => new CatalogGroup(g.Key, g.ToList()))
            .ToList();

    private async Task LoadCatalogAsync(bool force)
    {
        if (_catalogLoaded && !force) return;
        _catalogLoaded = true;
        CatalogError = null;

        var tasks = Catalog.ToList().Select(async item =>
        {
            item.Loading = true;
            item.Failed = false;
            try
            {
                item.Release = await GitHubService.GetLatestReleaseAsync(item.Repo, force);
            }
            catch (HttpRequestException ex)
            {
                Log.Error($"Consultar {item.Repo}", ex);
                item.Failed = true;
                CatalogError ??= FriendlyError(ex);
            }
            finally
            {
                item.Loading = false;
            }
        });
        await Task.WhenAll(tasks);
        SyncCatalogInstalled();
    }

    /// <summary>Relaciona cada mod del catálogo con su mod instalado (por repo seguido o por nombre de DLL).</summary>
    private void SyncCatalogInstalled()
    {
        foreach (var item in Catalog)
        {
            var assetStems = item.Release?.Assets.Select(a => AssetStem(a.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            item.Installed = Mods.FirstOrDefault(m => string.Equals(m.Source?.Repo, item.Repo, StringComparison.OrdinalIgnoreCase))
                             ?? Mods.FirstOrDefault(m => m.Source is null && m.Entry.Dlls.Concat(m.Entry.RenamedDlls).Any(d => assetStems.Contains(Path.GetFileNameWithoutExtension(d))));
        }
        RaiseRunBlocking();
    }

    // "FIHMapEditor_v0.15.1.zip" -> "FIHMapEditor", "CharmReplacer-v1.6.0.zip" -> "CharmReplacer"
    private static string AssetStem(string assetName) =>
        System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(assetName), @"[-_ .]*v?\d+(\.\d+)+.*$", "");

    private async Task InstallFromInputAsync()
    {
        var repo = GitHubService.ParseRepo(RepoInput);
        if (repo is null)
        {
            ShowToast(Loc.T("toast.badRepo"), ToastKind.Error);
            return;
        }
        await InstallFromRepoAsync(repo, forceRefresh: true);
        RepoInput = "";
    }

    private async Task InstallFromRepoAsync(string repo, bool forceRefresh)
    {
        if (SelectedGame is not { } game || !EnsureGameClosed()) return;

        GitHubRelease? release = null;
        await RunBusyAsync(Loc.T("busy.querying", repo), async () => release = await GitHubService.GetLatestReleaseAsync(repo, forceRefresh));
        if (release is null)
        {
            ShowToast(Loc.T("toast.noReleases", repo), ToastKind.Error);
            return;
        }

        var asset = GitHubService.PickAsset(release);
        if (asset is null)
        {
            var options = GitHubService.InstallableAssets(release);
            if (options.Count == 0)
            {
                ShowToast(Loc.T("toast.noAssets", release.Tag), ToastKind.Error);
                return;
            }
            var choose = new DialogViewModel
            {
                Title = $"{repo} · {release.Tag}",
                Message = Loc.T("dlg.chooseAsset.msg"),
                PrimaryText = Loc.T("common.install"),
                Choices = new ObservableCollection<object>(options),
            };
            if (!await ShowDialogAsync(choose)) return;
            asset = (GitHubAsset)choose.SelectedChoice!;
        }

        await RunBusyAsync(Loc.T("busy.downloading", asset.Name), async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "FIHModManager", "downloads");
            Directory.CreateDirectory(tempDir);
            var file = Path.Combine(tempDir, asset.Name);
            try
            {
                await GitHubService.DownloadAsync(asset, file, new Progress<double>(p => Progress = p));

                BusyText = Loc.T("busy.installing", asset.Name);
                Progress = -1;
                var current = Mods.Select(m => m.Entry).ToList();
                var keys = await Task.Run(() => ModService.Install(game, file, current));
                foreach (var key in keys)
                {
                    _state.Sources[key] = new ModSource { Repo = repo, Tag = release.Tag, Asset = asset.Name, InstalledAt = DateTime.Now };
                }
                JsonStore.SaveState(game, _state);
                await ReloadModsAsync();
                foreach (var m in Mods.Where(m => keys.Contains(m.Entry.Key))) m.LatestTag = release.Tag;
                RaiseModCounts();
                RefreshFilteredMods();
                SyncCatalogInstalled();
                ShowToast(Loc.T("toast.repoInstalled", repo.Split('/')[1], release.Tag), ToastKind.Success);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        });
    }
}
