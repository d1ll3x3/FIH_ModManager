using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FIHModManager.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            bool b => b,
            int i => i > 0,
            string s => !string.IsNullOrWhiteSpace(s),
            null => false,
            _ => true,
        };
        return flag ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
}

/// <summary>Compara el valor con el parámetro; se usa para marcar la página activa en el menú.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

/// <summary>Nombre neutro de icono del catálogo ("edit", "timer"…) a glifo de Segoe Fluent Icons.</summary>
public sealed class IconGlyphConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["edit"] = "\uE70F",
        ["timer"] = "\uE916",
        ["palette"] = "\uE790",
        ["flight"] = "\uE709",
        ["person"] = "\uE77B",
        ["bolt"] = "\uE945",
        ["link"] = "\uE71B",
        ["visibility"] = "\uE7B3",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key && Glyphs.TryGetValue(key, out var glyph) ? glyph : "\uE74C";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bytes = System.Convert.ToDouble(value);
        string[] units = ["B", "KB", "MB", "GB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.#} {units[i]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
