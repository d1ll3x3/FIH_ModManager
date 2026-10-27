using System.Collections.ObjectModel;
using FIHModManager.Infrastructure;
using FIHModManager.Models;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    private string _repoInput = "";
    private string? _catalogError;
    private bool _catalogLoaded;

    public ObservableCollection<CatalogItemViewModel> Catalog { get; } = [];

    public IReadOnlyList<CatalogGroup> CatalogGroups { get; private set; } = [];

    public string RepoInput { get => _repoInput; set => Set(ref _repoInput, value); }

    public string? CatalogError { get => _catalogError; private set => Set(ref _catalogError, value); }

    public AsyncCommand InstallCatalogCommand { get; private set; } = null!;
    public AsyncCommand InstallRepoInputCommand { get; private set; } = null!;
    public AsyncCommand RefreshCatalogCommand { get; private set; } = null!;
    public RelayCommand OpenRepoCommand { get; private set; } = null!;

    private void InitExplore()
    {
        foreach (var entry in GitHubService.Catalog) Catalog.Add(new CatalogItemViewModel(entry));
        CatalogGroups = Catalog.GroupBy(c => c.Entry.Author).Select(g => new CatalogGroup(g.Key, g.ToList())).ToList();

        InstallCatalogCommand = new AsyncCommand(p => InstallFromRepoAsync(((CatalogItemViewModel)p!).Repo, forceRefresh: false), _ => HasGame && !IsBusy);
        InstallRepoInputCommand = new AsyncCommand(InstallFromInputAsync, () => HasGame && !IsBusy && !string.IsNullOrWhiteSpace(RepoInput));
        RefreshCatalogCommand = new AsyncCommand(() => LoadCatalogAsync(force: true));
        OpenRepoCommand = new RelayCommand(p => GameLauncher.OpenUrl($"https://github.com/{p}"));
    }

    private async Task LoadCatalogAsync(bool force)
    {
        if (_catalogLoaded && !force) return;
        _catalogLoaded = true;
        CatalogError = null;

        var tasks = Catalog.Select(async item =>
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
