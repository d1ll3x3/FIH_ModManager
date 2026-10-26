using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace FIHModManager.Infrastructure;

public sealed record LanguageInfo(string Code, string NativeName, string Culture)
{
    public override string ToString() => NativeName;
}

/// <summary>
/// Textos de la interfaz. Cada idioma es un JSON embebido (Resources/Lang/xx.json); lo que falte
/// en un idioma cae al inglés. El indexador notifica "Item[]" para que la UI cambie en caliente.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string DefaultCode = "en";

    public static IReadOnlyList<LanguageInfo> Languages { get; } =
    [
        new("en", "English", "en-US"),
        new("es", "Español", "es-ES"),
        new("fr", "Français", "fr-FR"),
        new("ru", "Русский", "ru-RU"),
        new("pl", "Polski", "pl-PL"),
        new("ar", "العربية", "ar-SA"),
        new("zh", "简体中文", "zh-CN"),
        new("ko", "한국어", "ko-KR"),
    ];

    // Después de Languages: los inicializadores estáticos corren en orden y el constructor la usa.
    public static Loc Instance { get; } = new();

    private readonly Dictionary<string, string> _fallback = LoadStrings(DefaultCode);
    private Dictionary<string, string> _strings;

    private Loc()
    {
        _strings = _fallback;
        Language = Languages[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public LanguageInfo Language { get; private set; }
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language.Culture);
    public bool IsRightToLeft => Language.Code == "ar";

    public string this[string key] =>
        _strings.TryGetValue(key, out var s) || _fallback.TryGetValue(key, out s) ? s : key;

    public static string T(string key, params object?[] args) =>
        args.Length == 0 ? Instance[key] : string.Format(Instance.Culture, Instance[key], args);

    public void SetLanguage(string? code)
    {
        var lang = Languages.FirstOrDefault(l => l.Code == code) ?? Languages[0];
        if (lang == Language && _strings.Count > 0) return;
        Language = lang;
        _strings = lang.Code == DefaultCode ? _fallback : LoadStrings(lang.Code);
        CultureInfo.CurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightToLeft)));
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string> LoadStrings(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Lang.{code}.json")
            ?? throw new InvalidOperationException($"Missing language resource Lang.{code}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
