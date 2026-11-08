using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

/// <summary>
/// Lista de mods de "Explorar" y de la pestaña Speedrun. Vive en mods.json del repo FIH-ModCatalog, donde los modders
/// añaden sus mods con un PR; así un mod nuevo llega a todos sin sacar otra versión del programa.
/// </summary>
public static partial class CatalogService
{
    // raw.githubusercontent.com no gasta el cupo de 60 consultas por hora de la API.
    private const string Url = "https://raw.githubusercontent.com/d1ll3x3/FIH-ModCatalog/main/mods.json";
    private static readonly string CachePath = Path.Combine(Log.DataDir, "catalog.json");
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Secciones de "Explorar" en orden. Una categoría nueva en el catálogo necesita una versión nueva del programa.</summary>
    public static readonly string[] Categories = ["cosmetic", "graphics", "practice", "challenge", "multiplayer", "creation"];

    /// <summary>El último catálogo descargado o, si nunca se ha descargado (o está roto), la copia que va dentro del programa.</summary>
    public static IReadOnlyList<CatalogEntry> LoadLocal()
    {
        try
        {
            if (File.Exists(CachePath) && Parse(File.ReadAllText(CachePath)) is { } cached) return cached;
        }
        catch (IOException ex)
        {
            Log.Error("Leer la caché del catálogo", ex);
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json")!;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) ?? [];
    }

    /// <summary>Descarga el catálogo y lo guarda. Null si no ha cambiado desde la última vez o si no se pudo descargar.</summary>
    public static async Task<IReadOnlyList<CatalogEntry>?> FetchAsync()
    {
        string json;
        try
        {
            json = await GitHubService.Http.GetStringAsync(Url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Sin conexión se sigue con el catálogo guardado: no hace falta avisar.
            Log.Error("Descargar el catálogo", ex);
            return null;
        }

        try
        {
            if (File.Exists(CachePath) && File.ReadAllText(CachePath) == json) return null;
        }
        catch (IOException ex)
        {
            Log.Error("Leer la caché del catálogo", ex);
        }

        if (Parse(json) is not { } mods) return null;
        try
        {
            File.WriteAllText(CachePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // La caché es una optimización: si no se puede escribir, se usa el catálogo descargado igualmente.
            Log.Error("Guardar la caché del catálogo", ex);
        }
        return mods;
    }

    private static IReadOnlyList<CatalogEntry>? Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<CatalogFile>(json, Options);
            // Una entrada incompleta o con una categoría desconocida (de un formato más nuevo) se salta; el resto se muestra.
            return file?.Mods.Where(IsValid).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error("Leer el catálogo", ex);
            return null;
        }
    }

    private static bool IsValid(CatalogEntry m) =>
        RepoRegex().IsMatch(m.Repo)
        && m.Title.Length > 0
        && Categories.Contains(m.Category)
        && m.Description.ContainsKey(Loc.DefaultCode)
        && (m.Explore || m.Speedrun is not null);

    [GeneratedRegex(@"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex RepoRegex();
}
