using Avalonia;
using Avalonia.Media;

namespace FIHModManager.Infrastructure;

/// <summary>
/// Paleta de la página de Flipping is Hard en speedrun.com para la pestaña Speedrun. Sustituye los pinceles de App.axaml
/// (y el acento del tema Fluent, que usan interruptores y campos) y guarda los originales para devolverlos al salir.
/// Solo cambia lo que usa DynamicResource (App.axaml, MainWindow y SpeedrunView): las demás páginas no se ven mientras está activo.
/// </summary>
public static class SpeedrunTheme
{
    private static readonly Dictionary<string, object> Values = new()
    {
        ["BgBrush"] = Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative), "#0C3150", "#111618"),
        ["SidebarBrush"] = Solid("#062F52"),
        ["SurfaceBrush"] = Solid("#D9064779"),
        ["Surface2Brush"] = Solid("#032642"),
        ["Surface3Brush"] = Solid("#214058"),
        ["LineBrush"] = Solid("#104E7F"),
        ["TextBrush"] = Solid("#FFFFFF"),
        ["MutedBrush"] = Solid("#BFCFDE"),
        ["AccentBrush"] = Solid("#55EA6A"),
        ["Accent2Brush"] = Solid("#7FDCFF"),
        ["SuccessBrush"] = Solid("#5CE8B1"),
        ["WarningBrush"] = Solid("#FDC357"),
        ["DangerBrush"] = Solid("#FFB9B4"),
        ["AccentSoftBrush"] = Solid("#3355EA6A"),
        ["SuccessSoftBrush"] = Solid("#265CE8B1"),
        ["WarningSoftBrush"] = Solid("#26FDC357"),
        ["DangerSoftBrush"] = Solid("#33FFB9B4"),
        ["Accent2SoftBrush"] = Solid("#2E7FDCFF"),
        ["OnAccentBrush"] = Solid("#1D1D1D"),
        ["AccentGradient"] = Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative), "#55EA6A", "#5BE6C4"),
        ["HeroGradient"] = Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative), "#0B5A3A", "#064779", "#0C2B48"),
        ["SystemAccentColor"] = Color.Parse("#55EA6A"),
        ["SystemAccentColorDark1"] = Color.Parse("#4DD35F"),
        ["SystemAccentColorDark2"] = Color.Parse("#40B050"),
        ["SystemAccentColorDark3"] = Color.Parse("#338C40"),
        ["SystemAccentColorLight1"] = Color.Parse("#88F097"),
        ["SystemAccentColorLight2"] = Color.Parse("#BBF7C3"),
        ["SystemAccentColorLight3"] = Color.Parse("#D4FADA"),
    };

    // Valor que tenía cada clave en los recursos de la aplicación antes de aplicar el tema (null = no estaba).
    private static readonly Dictionary<string, object?> Originals = [];

    public static void Apply(bool enabled)
    {
        var resources = Application.Current!.Resources;
        if (enabled == Originals.Count > 0) return;

        foreach (var (key, value) in Values)
        {
            if (enabled)
            {
                Originals[key] = resources.TryGetValue(key, out var original) ? original : null;
                resources[key] = value;
            }
            else if (Originals[key] is { } original)
            {
                resources[key] = original;
            }
            else
            {
                resources.Remove(key);
            }
        }
        if (!enabled) Originals.Clear();
    }

    private static SolidColorBrush Solid(string hex) => new(Color.Parse(hex));

    private static LinearGradientBrush Gradient(RelativePoint start, RelativePoint end, params string[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        for (var i = 0; i < stops.Length; i++)
            brush.GradientStops.Add(new GradientStop(Color.Parse(stops[i]), i / (double)(stops.Length - 1)));
        return brush;
    }
}
