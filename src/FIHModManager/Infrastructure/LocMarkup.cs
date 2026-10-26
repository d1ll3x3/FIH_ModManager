using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace FIHModManager.Infrastructure;

/// <summary>Uso en XAML: Text="{loc:Tr nav.home}". Se actualiza al cambiar de idioma.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>
/// Traduce un valor del modelo con un prefijo de clave (p. ej. Origin "Manual" -> "origin.Manual").
/// Se usa en un MultiBinding junto a Loc.Language para refrescarse al cambiar de idioma.
/// </summary>
public sealed class TrKeyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        Loc.Instance[$"{parameter}{values[0]}"];

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
