using FIHModManager.Infrastructure;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    private const string LeaderboardUrl = "https://www.speedrun.com/Flipping_Is_Hard";

    /// <summary>Mods del catálogo permitidos en runs: los únicos que lista la pestaña Speedrun.</summary>
    public IReadOnlyList<SpeedrunItemViewModel> SpeedrunItems { get; private set; } = [];

    public int SpeedrunAllowedCount => SpeedrunItems.Count;

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
        SpeedrunItems.Any(s => s.Item.Installed == mod || string.Equals(mod.Source?.Repo, s.Item.Repo, StringComparison.OrdinalIgnoreCase));

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
    // Los no permitidos no se listan: cualquier mod activado fuera de esta lista ya sale en el aviso de RunBlockingMods.
    private void RefreshSpeedrun()
    {
        SpeedrunItems = Catalog.Where(c => c.Entry.Speedrun is { Allowed: true })
            .Select(c => new SpeedrunItemViewModel(c, c.Entry.Speedrun!))
            .ToList();
        OnPropertyChanged(nameof(SpeedrunItems));
        OnPropertyChanged(nameof(SpeedrunAllowedCount));
    }
}
