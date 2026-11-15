using FIHModManager.Infrastructure;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    private const string LeaderboardUrl = "https://www.speedrun.com/Flipping_Is_Hard";

    private IReadOnlyList<SpeedrunItemViewModel> _speedrunTools = [];
    private IReadOnlyList<SpeedrunItemViewModel> _speedrunAllowed = [];
    private string _speedrunTab = "Tools";

    /// <summary>"Tools" (Mods para speedrun, la principal) o "Allowed" (Permitidos en runs): las dos listas de la pestaña Speedrun.</summary>
    public string SpeedrunTab
    {
        get => _speedrunTab;
        set
        {
            if (Set(ref _speedrunTab, value)) OnPropertyChanged(nameof(SpeedrunItems));
        }
    }

    public IReadOnlyList<SpeedrunItemViewModel> SpeedrunItems => SpeedrunTab == "Allowed" ? _speedrunAllowed : _speedrunTools;

    public int SpeedrunToolCount => _speedrunTools.Count;
    public int SpeedrunAllowedCount => _speedrunAllowed.Count;

    /// <summary>Mods activados que no están en "Permitidos" (también los que no salen en el catálogo): invalidan una run.</summary>
    public IReadOnlyList<ModItemViewModel> RunBlockingMods => Mods.Where(m => m.IsEnabled && !IsAllowedInRuns(m)).ToList();
    public bool HasRunBlockingMods => RunBlockingMods.Count > 0;
    public string RunBlockingText => Loc.T("speedrun.blocking", string.Join(", ", RunBlockingMods.Select(m => m.Title)));

    public RelayCommand OpenLeaderboardCommand { get; private set; } = null!;
    public AsyncCommand SpeedrunPlayCommand { get; private set; } = null!;

    private void InitSpeedrun()
    {
        OpenLeaderboardCommand = new RelayCommand(() => GameLauncher.OpenUrl(LeaderboardUrl));
        SpeedrunPlayCommand = new AsyncCommand(PlayForRunAsync, () => HasGame && !IsGameRunning);
    }

    // Por repo seguido o por el mod que el catálogo reconoció como instalado (también los instalados a mano).
    private bool IsAllowedInRuns(ModItemViewModel mod) =>
        _speedrunAllowed.Any(s => s.Item.Installed == mod || string.Equals(mod.Source?.Repo, s.Item.Repo, StringComparison.OrdinalIgnoreCase));

    private void RaiseRunBlocking()
    {
        OnPropertyChanged(nameof(RunBlockingMods));
        OnPropertyChanged(nameof(HasRunBlockingMods));
        OnPropertyChanged(nameof(RunBlockingText));
    }

    private async Task PlayForRunAsync()
    {
        if (HasRunBlockingMods)
        {
            var ok = await ShowDialogAsync(new DialogViewModel
            {
                Title = Loc.T("dlg.runBlocking.title"),
                Message = Loc.T("dlg.runBlocking.msg", string.Join("\n", RunBlockingMods.Select(m => "• " + m.Title))),
                PrimaryText = Loc.T("dlg.runBlocking.play"),
                IsDanger = true,
            });
            if (!ok) return;
        }
        PlayCommand.Execute(null);
    }

    // Usa las mismas tarjetas que "Explorar", así comparten la release consultada y el mod instalado.
    // Los no permitidos solo salen si son herramientas de speedrun; el resto activado ya sale en el aviso de RunBlockingMods.
    private void RefreshSpeedrun()
    {
        _speedrunTools = Catalog.Where(c => c.Entry.Speedrun is { Tool: true })
            .Select(c => new SpeedrunItemViewModel(c, c.Entry.Speedrun!, ShowStatus: true))
            .ToList();
        _speedrunAllowed = Catalog.Where(c => c.Entry.Speedrun is { Allowed: true })
            .Select(c => new SpeedrunItemViewModel(c, c.Entry.Speedrun!, ShowStatus: false))
            .ToList();
        OnPropertyChanged(nameof(SpeedrunItems));
        OnPropertyChanged(nameof(SpeedrunToolCount));
        OnPropertyChanged(nameof(SpeedrunAllowedCount));
    }
}
