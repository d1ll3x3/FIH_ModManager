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

    public RelayCommand OpenLeaderboardCommand { get; private set; } = null!;

    // Usa las mismas tarjetas que "Explorar", así comparten la release consultada y el mod instalado.
    private void InitSpeedrun()
    {
        _speedrunMods = GitHubService.Speedrun
            .Select(s => new SpeedrunItemViewModel(Catalog.First(c => c.Repo == s.Repo), s.Allowed, s.NoteKey))
            .ToList();
        OpenLeaderboardCommand = new RelayCommand(() => GameLauncher.OpenUrl(LeaderboardUrl));
    }
}
