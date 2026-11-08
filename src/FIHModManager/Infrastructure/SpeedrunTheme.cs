using System.Windows;
using System.Windows.Media;

namespace FIHModManager.Infrastructure;

/// <summary>
/// Paleta de la página de Flipping is Hard en speedrun.com para la pestaña Speedrun. Los pinceles de aquí tapan a los de
/// Theme.xaml desde los recursos de la aplicación; al quitarlos vuelven los originales. Solo cambia lo que usa
/// DynamicResource (Theme.xaml, MainWindow y SpeedrunView): las demás páginas no se ven mientras está activo.
/// </summary>
public static class SpeedrunTheme
{
    private static readonly Dictionary<string, Brush> Brushes = new()
    {
        ["BgBrush"] = Gradient(new Point(0, 0), new Point(0, 1), "#0C3150", "#111618"),
        ["SidebarBrush"] = Solid("#062F52"),
        ["SurfaceBrush"] = Solid("#D9064779"),
        ["Surface2Brush"] = Solid("#032642"),
        ["Surface3Brush"] = Solid("#214058"),
        ["BorderBrush"] = Solid("#104E7F"),
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
        ["AccentGradient"] = Gradient(new Point(0, 0), new Point(1, 1), "#55EA6A", "#5BE6C4"),
        ["HeroGradient"] = Gradient(new Point(0, 0), new Point(1, 1), "#0B5A3A", "#064779", "#0C2B48"),
    };

    public static void Apply(bool enabled)
    {
        var resources = Application.Current.Resources;
        foreach (var (key, brush) in Brushes)
        {
            if (enabled) resources[key] = brush;
            else resources.Remove(key);
        }
    }

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(Point start, Point end, params string[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        for (var i = 0; i < stops.Length; i++)
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i / (double)(stops.Length - 1)));
        brush.Freeze();
        return brush;
    }
}
