using System.Text;

namespace FIHModManager.Services;

public sealed class CfgEntry
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string Value { get; set; }
    public required string OriginalValue { get; set; }
    public required int LineIndex { get; init; }
    public string Description { get; init; } = "";
    public string? SettingType { get; init; }
    public string? DefaultValue { get; init; }
    public IReadOnlyList<string>? AcceptableValues { get; init; }
    public bool IsFlags { get; init; }
    public string? Range { get; init; }
}

/// <summary>
/// Lector/escritor del formato .cfg de BepInEx. Al guardar solo se reescriben las líneas
/// "Clave = valor" que han cambiado; comentarios, orden y finales de línea (BepInEx a veces
/// los mezcla) se conservan byte a byte.
/// </summary>
public sealed class ConfigFile
{
    private readonly List<string> _lines;
    private readonly List<string> _eols;
    private readonly bool _bom;

    public string Path { get; }
    public List<CfgEntry> Entries { get; } = [];

    private ConfigFile(string path, List<string> lines, List<string> eols, bool bom)
    {
        Path = path;
        _lines = lines;
        _eols = eols;
        _bom = bom;
        Parse();
    }

    public static ConfigFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

        var lines = new List<string>();
        var eols = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            var crlf = i > start && text[i - 1] == '\r';
            lines.Add(text[start..(crlf ? i - 1 : i)]);
            eols.Add(crlf ? "\r\n" : "\n");
            start = i + 1;
        }
        lines.Add(text[start..]);
        eols.Add("");
        return new ConfigFile(path, lines, eols, bom);
    }

    public void Save()
    {
        foreach (var e in Entries.Where(e => e.Value != e.OriginalValue))
        {
            _lines[e.LineIndex] = $"{e.Key} = {e.Value}";
            e.OriginalValue = e.Value;
        }
        var sb = new StringBuilder();
        for (var i = 0; i < _lines.Count; i++) sb.Append(_lines[i]).Append(_eols[i]);
        File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(_bom));
    }

    private void Parse()
    {
        var section = "";
        var desc = new StringBuilder();
        string? type = null, def = null, range = null;
        List<string>? acceptable = null;
        var flags = false;

        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                desc.Clear();
                type = def = range = null;
                acceptable = null;
                flags = false;
            }
            else if (line.StartsWith("##"))
            {
                if (desc.Length > 0) desc.Append('\n');
                desc.Append(line[2..].Trim());
            }
            else if (line.StartsWith('#'))
            {
                var c = line[1..].Trim();
                if (TryAfter(c, "Setting type:", out var t)) type = t;
                else if (TryAfter(c, "Default value:", out var d)) def = d;
                else if (TryAfter(c, "Acceptable values:", out var a)) acceptable = a.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                else if (TryAfter(c, "Acceptable value range:", out var r)) range = r;
                else if (c.StartsWith("Multiple values can be set", StringComparison.OrdinalIgnoreCase)) flags = true;
            }
            else
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Entries.Add(new CfgEntry
                {
                    Section = section,
                    Key = line[..eq].Trim(),
                    Value = line[(eq + 1)..].Trim(),
                    OriginalValue = line[(eq + 1)..].Trim(),
                    LineIndex = i,
                    Description = desc.ToString(),
                    SettingType = type,
                    DefaultValue = def,
                    AcceptableValues = acceptable,
                    IsFlags = flags,
                    Range = range,
                });
                desc.Clear();
                type = def = range = null;
                acceptable = null;
                flags = false;
            }
        }
    }

    private static bool TryAfter(string text, string prefix, out string rest)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = text[prefix.Length..].Trim();
            return true;
        }
        rest = "";
        return false;
    }
}
