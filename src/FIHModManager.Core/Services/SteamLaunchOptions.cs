using System.Text;
using System.Text.RegularExpressions;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

public enum ProtonStatus { NotApplicable, Set, Missing, NoConfig }

public sealed class SteamRunningException() : InvalidOperationException(Loc.T("proton.steamRunning"));

/// <summary>
/// En Linux el juego corre con Proton (Wine), que por defecto usa su propia winhttp.dll e ignora la de
/// BepInEx. La opción de lanzamiento WINEDLLOVERRIDES="winhttp=n,b" %command% hace que cargue la nativa.
/// Se escribe en userdata/*/config/localconfig.vdf de cada cuenta de Steam, siempre con Steam cerrado
/// (al salir, Steam sobrescribe ese archivo con lo que tiene en memoria).
/// </summary>
public static partial class SteamLaunchOptions
{
    public const string DllOverride = "winhttp=n,b";
    private const string BackupSuffix = ".fihmm-backup";

    public static ProtonStatus GetStatus(GameInstall game)
    {
        if (OperatingSystem.IsWindows() || game.SteamAppId is null) return ProtonStatus.NotApplicable;
        var configs = LocalConfigs().ToList();
        if (configs.Count == 0) return ProtonStatus.NoConfig;
        var all = configs.All(c => HasOverride(GetLaunchOptions(VdfNode.Parse(File.ReadAllText(c)), game.SteamAppId.Value)));
        return all ? ProtonStatus.Set : ProtonStatus.Missing;
    }

    public static void Apply(GameInstall game) => Update(game, AddOverride);

    public static void Remove(GameInstall game) => Update(game, RemoveOverride);

    private static void Update(GameInstall game, Func<string, string> change)
    {
        if (game.SteamAppId is not { } appId) return;
        if (IsSteamRunning()) throw new SteamRunningException();
        var configs = LocalConfigs().ToList();
        if (configs.Count == 0) throw new InvalidOperationException(Loc.T("proton.noConfig"));

        foreach (var path in configs)
        {
            var root = VdfNode.Parse(File.ReadAllText(path));
            var current = GetLaunchOptions(root, appId);
            var updated = change(current);
            if (updated == current) continue;

            File.Copy(path, path + BackupSuffix, overwrite: true);
            AppNode(root, appId).SetValue("LaunchOptions", updated);
            File.WriteAllText(path, root.Serialize());
            Log.Info($"LaunchOptions de {appId} en {path}: \"{current}\" -> \"{updated}\"");
        }
    }

    public static bool IsSteamRunning()
    {
        if (OperatingSystem.IsWindows()) return false;
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out _)) continue;
            try
            {
                if (File.ReadAllText(Path.Combine(dir, "comm")).Trim() == "steam") return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Proceso terminado o ajeno: se ignora.
            }
        }
        return false;
    }

    private static IEnumerable<string> LocalConfigs() =>
        GameLocator.SteamDirs
            .Select(steam => Path.Combine(steam, "userdata"))
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(user => Path.Combine(user, "config", "localconfig.vdf"))
            .Where(File.Exists);

    private static string GetLaunchOptions(VdfNode root, int appId) =>
        root.Find("UserLocalConfigStore", "Software", "Valve", "Steam", "apps", appId.ToString())?.GetValue("LaunchOptions") ?? "";

    private static VdfNode AppNode(VdfNode root, int appId) =>
        root.GetOrAdd("UserLocalConfigStore").GetOrAdd("Software").GetOrAdd("Valve").GetOrAdd("Steam").GetOrAdd("apps").GetOrAdd(appId.ToString());

    public static bool HasOverride(string options) =>
        OverrideRegex().Match(options) is { Success: true } m && m.Groups[1].Value.Contains("winhttp=n", StringComparison.OrdinalIgnoreCase);

    /// <summary>Añade winhttp=n,b respetando lo que el usuario ya tuviera (otros overrides, argumentos, %command%).</summary>
    public static string AddOverride(string options)
    {
        options = options.Trim();
        if (HasOverride(options)) return options;

        if (OverrideRegex().Match(options) is { Success: true } existing)
        {
            var merged = $"{DllOverride};{existing.Groups[1].Value}";
            return options.Remove(existing.Index, existing.Length).Insert(existing.Index, $"WINEDLLOVERRIDES=\"{merged}\"");
        }

        var prefix = $"WINEDLLOVERRIDES=\"{DllOverride}\"";
        if (options.Contains("%command%")) return $"{prefix} {options}";
        // Sin %command%, Steam trata el texto como argumentos del juego: hay que añadirlo para poder anteponer la variable.
        return options.Length == 0 ? $"{prefix} %command%" : $"{prefix} %command% {options}";
    }

    public static string RemoveOverride(string options)
    {
        if (OverrideRegex().Match(options) is not { Success: true } m) return options;

        var rest = m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(d => !d.Trim().StartsWith("winhttp=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var replacement = rest.Count == 0 ? "" : $"WINEDLLOVERRIDES=\"{string.Join(';', rest)}\"";
        var result = WhitespaceRegex().Replace(options.Remove(m.Index, m.Length).Insert(m.Index, replacement), " ").Trim();
        // Si solo queda "%command%" es que lo pusimos nosotros: Steam lo trata igual que vacío.
        return result == "%command%" ? "" : result;
    }

    [GeneratedRegex("WINEDLLOVERRIDES=\"([^\"]*)\"")]
    private static partial Regex OverrideRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();
}

/// <summary>Formato KeyValues de Valve (.vdf): claves entre comillas con valor o bloque { }.</summary>
public sealed class VdfNode
{
    public string Key { get; }
    public string? Value { get; private set; }
    public List<VdfNode> Children { get; } = [];

    private VdfNode(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public VdfNode? Find(params string[] path)
    {
        var node = this;
        foreach (var key in path)
        {
            node = node.Children.FirstOrDefault(c => c.Value is null && c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (node is null) return null;
        }
        return node;
    }

    public VdfNode GetOrAdd(string key)
    {
        var child = Children.FirstOrDefault(c => c.Value is null && c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (child is null) Children.Add(child = new VdfNode(key));
        return child;
    }

    public string? GetValue(string key) =>
        Children.FirstOrDefault(c => c.Value is not null && c.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    public void SetValue(string key, string value)
    {
        var child = Children.FirstOrDefault(c => c.Value is not null && c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (child is null) Children.Add(new VdfNode(key, value));
        else child.Value = value;
    }

    public static VdfNode Parse(string text)
    {
        var root = new VdfNode("");
        var stack = new Stack<VdfNode>();
        stack.Push(root);
        string? pendingKey = null;
        var i = 0;

        while (NextToken(text, ref i) is { } token)
        {
            switch (token)
            {
                case { Kind: TokenKind.Open }:
                    var block = new VdfNode(pendingKey ?? throw new FormatException($"'{{' without key at {i}"));
                    stack.Peek().Children.Add(block);
                    stack.Push(block);
                    pendingKey = null;
                    break;
                case { Kind: TokenKind.Close }:
                    if (stack.Count == 1) throw new FormatException($"Unbalanced '}}' at {i}");
                    stack.Pop();
                    break;
                case { Kind: TokenKind.Condition }:
                    break; // [$WIN32] y similares: Steam los usa en otros .vdf; aquí no afectan.
                default:
                    if (pendingKey is null) pendingKey = token.Text;
                    else
                    {
                        stack.Peek().Children.Add(new VdfNode(pendingKey, token.Text));
                        pendingKey = null;
                    }
                    break;
            }
        }

        if (stack.Count != 1 || pendingKey is not null) throw new FormatException("Truncated VDF");
        return root;
    }

    public string Serialize()
    {
        var sb = new StringBuilder();
        foreach (var child in Children) child.Write(sb, 0);
        return sb.ToString();
    }

    private void Write(StringBuilder sb, int depth)
    {
        var indent = new string('\t', depth);
        if (Value is not null)
        {
            sb.Append(indent).Append('"').Append(Escape(Key)).Append("\"\t\t\"").Append(Escape(Value)).Append("\"\n");
            return;
        }
        sb.Append(indent).Append('"').Append(Escape(Key)).Append("\"\n").Append(indent).Append("{\n");
        foreach (var child in Children) child.Write(sb, depth + 1);
        sb.Append(indent).Append("}\n");
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");

    private enum TokenKind { String, Open, Close, Condition }

    private sealed record Token(TokenKind Kind, string Text);

    private static Token? NextToken(string text, ref int i)
    {
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '{') { i++; return new Token(TokenKind.Open, "{"); }
            if (c == '}') { i++; return new Token(TokenKind.Close, "}"); }
            if (c == '[')
            {
                var end = text.IndexOf(']', i);
                if (end < 0) throw new FormatException($"Unclosed condition at {i}");
                var cond = text[i..(end + 1)];
                i = end + 1;
                return new Token(TokenKind.Condition, cond);
            }
            if (c == '"') return new Token(TokenKind.String, ReadQuoted(text, ref i));

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"')) i++;
            return new Token(TokenKind.String, text[start..i]);
        }
        return null;
    }

    private static string ReadQuoted(string text, ref int i)
    {
        var sb = new StringBuilder();
        i++; // comilla de apertura
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == '"') return sb.ToString();
            if (c == '\\' && i < text.Length)
            {
                var e = text[i++];
                sb.Append(e switch { 'n' => '\n', 't' => '\t', _ => e });
            }
            else sb.Append(c);
        }
        throw new FormatException("Unterminated string");
    }
}
