using System.Text.Json;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(Log.DataDir, "settings.json");

    public static AppSettings LoadSettings() => Load<AppSettings>(SettingsPath) ?? new AppSettings();

    public static void SaveSettings(AppSettings settings) => Save(SettingsPath, settings);

    private static string StatePath(GameInstall game) => Path.Combine(game.BepInExDir, "fihmm.json");

    public static ManagerState LoadState(GameInstall game)
    {
        var state = Load<ManagerState>(StatePath(game)) ?? new ManagerState();
        // El deserializador no conserva el comparador del diccionario.
        state.Sources = new Dictionary<string, ModSource>(state.Sources, StringComparer.OrdinalIgnoreCase);
        return state;
    }

    public static void SaveState(GameInstall game, ManagerState state) => Save(StatePath(game), state);

    internal static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            // Un JSON corrupto no debe impedir arrancar; se guarda copia para no perderlo.
            Log.Error($"JSON inválido en {path}, se renombra a .corrupt", ex);
            File.Copy(path, path + ".corrupt", overwrite: true);
            return null;
        }
    }

    internal static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
