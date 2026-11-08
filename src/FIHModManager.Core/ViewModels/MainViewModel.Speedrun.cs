using FIHModManager.Infrastructure;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    private const string LeaderboardUrl = "https://www.speedrun.com/Flipping_Is_Hard";

    private IReadOnlyList<SpeedrunItemViewModel> _speedrunMods = [];
    private string _speedrunCategory = "Allowed";

    /// <summary>En la pestaña Speedrun la barra lateral muestra la portada del juego en vez del icono del programa.</summary>
    public bool IsSpeedrunPage => CurrentPage == "Speedrun";

    public int SpeedrunAllowedCount => _speedrunMods.Count(m => m.Allowed);
    public int SpeedrunBannedCount => _speedrunMods.Count(m => m.Banned);

    /// <summary>"Allowed" o "Banned": las dos categorías de la pestaña Speedrun.</summary>
    public string SpeedrunCategory
    {
        get => _speedrunCategory;
        set
        {
            if (Set(ref _speedrunCategory, value)) OnPropertyChanged(nameof(SpeedrunItems));
        }
    }

    public IReadOnlyList<SpeedrunItemViewModel> SpeedrunItems =>
        _speedrunMods.Where(m => m.Allowed == (SpeedrunCategory == "Allowed")).ToList();

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
        _speedrunMods.Any(s => s.Allowed && (s.Item.Installed == mod || string.Equals(mod.Source?.Repo, s.Item.Repo, StringComparison.OrdinalIgnoreCase)));

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
    private void RefreshSpeedrun()
    {
        _speedrunMods = Catalog.Where(c => c.Entry.Speedrun is not null)
            .Select(c => new SpeedrunItemViewModel(c, c.Entry.Speedrun!))
            .ToList();
        OnPropertyChanged(nameof(SpeedrunAllowedCount));
        OnPropertyChanged(nameof(SpeedrunBannedCount));
        OnPropertyChanged(nameof(SpeedrunItems));
    }
}
