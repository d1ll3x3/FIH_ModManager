using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace FIHModManager.Infrastructure;

/// <summary>
/// Uso en XAML: Text="{loc:Tr nav.home}". Se enlaza a Loc.Language (y no al indexador como en WPF,
/// porque Avalonia no refresca bindings de indexador con "Item[]"), así cambia en caliente con el idioma.
/// </summary>
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Loc.Language))
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
            Converter = new FuncValueConverter<LanguageInfo, string>(_ => Loc.Instance[key]),
        };
}

/// <summary>Traduce un valor del modelo con prefijo de clave; el segundo valor (Loc.Language) fuerza el refresco.</summary>
public sealed class TrKeyConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        Loc.Instance[$"{parameter}{values[0]}"];
}

/// <summary>Compara el valor (como texto) con el parámetro: página activa, tipo de editor, tipo de aviso…</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}

/// <summary>Número mayor que cero (o igual a cero con Invert) para IsVisible.</summary>
public sealed class CountConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is int n && n > 0) ^ Invert;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BytesConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        string[] units = ["B", "KB", "MB", "GB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.#} {units[i]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Glifos de Material Icons (puntos de código de MaterialIcons-Regular.codepoints).</summary>
public static class Icons
{
    public const string Home = "";
    public const string Mods = "";
    public const string Explore = "";
    public const string Config = "";
    public const string Profiles = "";
    public const string Settings = "";
    public const string Play = "";
    public const string Folder = "";
    public const string Delete = "";
    public const string Refresh = "";
    public const string Add = "";
    public const string Search = "";
    public const string Save = "";
    public const string Ok = "";
    public const string Warning = "";
    public const string Info = "";
    public const string Error = "";
    public const string Close = "";
    public const string Link = "";
    public const string Download = "";
    public const string Reset = "";
    public const string Tip = "";
    public const string Sync = "";
    public const string CfgFile = "";
    public const string TextFile = "";
    public const string Game = "";

    private static readonly Dictionary<string, string> Catalog = new()
    {
        ["edit"] = "",
        ["timer"] = "",
        ["palette"] = "",
        ["flight"] = "",
        ["person"] = "",
        ["bolt"] = "",
        ["link"] = "",
        ["visibility"] = "",
    };

    public static string ForCatalog(string? name) => name is not null && Catalog.TryGetValue(name, out var g) ? g : Mods;
}

public sealed class IconGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Icons.ForCatalog(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
