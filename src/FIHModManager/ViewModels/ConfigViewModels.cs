using System.Collections.ObjectModel;
using FIHModManager.Infrastructure;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public enum ConfigEditorKind { Toggle, Choice, Flags, Text }

public sealed class ConfigFlagViewModel(string name, bool isChecked, Action changed) : ObservableObject
{
    private bool _checked = isChecked;
    public string Name { get; } = name;

    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (Set(ref _checked, value)) changed();
        }
    }
}

public sealed class ConfigEntryViewModel : ObservableObject
{
    private readonly CfgEntry _entry;
    private readonly Action _changed;
    private string _value;
    private string _original;

    public ConfigEntryViewModel(CfgEntry entry, Action changed)
    {
        _entry = entry;
        _changed = changed;
        _value = _original = entry.Value;

        Kind = entry.SettingType?.Equals("Boolean", StringComparison.OrdinalIgnoreCase) == true ? ConfigEditorKind.Toggle
            : entry.AcceptableValues is { Count: > 0 } && entry.IsFlags ? ConfigEditorKind.Flags
            : entry.AcceptableValues is { Count: > 0 } ? ConfigEditorKind.Choice
            : ConfigEditorKind.Text;

        if (Kind == ConfigEditorKind.Flags)
        {
            var selected = SplitFlags(_value);
            Flags = new ObservableCollection<ConfigFlagViewModel>(
                entry.AcceptableValues!.Select(v => new ConfigFlagViewModel(v, selected.Contains(v), OnFlagsChanged)));
        }

        ResetCommand = new RelayCommand(() => Value = _entry.DefaultValue ?? _value, () => _entry.DefaultValue is not null && !IsDefault);
    }

    public ConfigEditorKind Kind { get; }
    public string Key => _entry.Key;
    public string Description => _entry.Description;
    public string? DefaultValue => _entry.DefaultValue;
    public IReadOnlyList<string>? Options => _entry.AcceptableValues;
    public ObservableCollection<ConfigFlagViewModel>? Flags { get; }
    public RelayCommand ResetCommand { get; }

    public string Hint => string.Join("   ·   ", new[]
    {
        _entry.SettingType is { } t ? Loc.T("cfg.type", t) : null,
        _entry.Range is { } r ? Loc.T("cfg.range", FormatRange(r)) : null,
        _entry.DefaultValue is { } d ? Loc.T("cfg.default", d) : null,
    }.Where(s => s is not null));

    public string Value
    {
        get => _value;
        set
        {
            if (!Set(ref _value, value ?? "")) return;
            _entry.Value = _value;
            if (Kind == ConfigEditorKind.Flags && Flags is not null)
            {
                var selected = SplitFlags(_value);
                foreach (var f in Flags) f.IsChecked = selected.Contains(f.Name);
            }
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(IsDefault));
            _changed();
        }
    }

    public bool BoolValue
    {
        get => _value.Equals("true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    public bool IsModified => _value != _original;
    public bool IsDefault => _entry.DefaultValue is null || string.Equals(_value, _entry.DefaultValue, StringComparison.OrdinalIgnoreCase);

    public void MarkSaved()
    {
        _original = _value;
        OnPropertyChanged(nameof(IsModified));
    }

    private void OnFlagsChanged()
    {
        var joined = string.Join(", ", Flags!.Where(f => f.IsChecked).Select(f => f.Name));
        if (joined.Length == 0) joined = "None";
        if (joined != _value) Value = joined;
    }

    // BepInEx escribe "From 0 to 10"; se muestra como "0 – 10" para no depender del idioma.
    private static string FormatRange(string range)
    {
        var parts = range.Replace("From", "", StringComparison.OrdinalIgnoreCase).Split(" to ", StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? $"{parts[0]} – {parts[1]}" : range;
    }

    private static HashSet<string> SplitFlags(string v) =>
        v.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed record ConfigSectionViewModel(string Name, IReadOnlyList<ConfigEntryViewModel> Entries);

public sealed class ConfigFileViewModel : ObservableObject
{
    private ConfigFile? _cfg;
    private string _rawText = "";
    private bool _isDirty;

    public ConfigFileViewModel(string path, string displayName, string? modTitle)
    {
        Path = path;
        DisplayName = displayName;
        ModTitle = modTitle;
    }

    public string Path { get; }
    public string DisplayName { get; }
    public string? ModTitle { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool IsCfg => Path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase);
    public string Glyph => IsCfg ? "\uE713" : "\uE943";
    public ObservableCollection<ConfigSectionViewModel> Sections { get; } = [];

    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }

    public string RawText
    {
        get => _rawText;
        set
        {
            if (Set(ref _rawText, value)) IsDirty = true;
        }
    }

    public void Load()
    {
        Sections.Clear();
        if (IsCfg)
        {
            _cfg = ConfigFile.Load(Path);
            foreach (var group in _cfg.Entries.GroupBy(e => e.Section))
                Sections.Add(new ConfigSectionViewModel(group.Key, group.Select(e => new ConfigEntryViewModel(e, () => IsDirty = true)).ToList()));
        }
        else
        {
            _rawText = File.ReadAllText(Path);
            OnPropertyChanged(nameof(RawText));
        }
        IsDirty = false;
    }

    public void Save()
    {
        if (IsCfg)
        {
            _cfg!.Save();
            foreach (var e in Sections.SelectMany(s => s.Entries)) e.MarkSaved();
        }
        else
        {
            File.WriteAllText(Path, RawText);
        }
        IsDirty = false;
    }
}
