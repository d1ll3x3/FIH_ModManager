using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FIHModManager.Infrastructure;
using FIHModManager.Models;

namespace FIHModManager.Services;

/// <summary>
/// Lee los atributos [BepInPlugin] de una DLL sin cargarla (System.Reflection.Metadata),
/// así se obtiene nombre/versión/GUID real del mod y con qué BepInEx se compiló.
/// </summary>
public static class PluginInspector
{
    // Abrir una DLL que el antivirus no conoce puede tardar segundos (la analiza antes de dejarla leer; se vieron 33 s
    // con una veintena de mods). Lo leído se guarda en disco y al arrancar solo se abren las DLL nuevas o cambiadas.
    private static readonly string CachePath = Path.Combine(Log.DataDir, "plugin-cache.json");
    private static readonly Dictionary<string, CachedMeta> Cache = LoadCache();
    private static bool _cacheDirty;

    public sealed record CachedMeta(long Size, DateTime Modified, AssemblyMeta Meta);

    /// <summary>Como <see cref="Inspect(string)"/>, pero reutiliza lo leído antes si la DLL no ha cambiado.</summary>
    public static AssemblyMeta InspectCached(string dllPath)
    {
        var info = new FileInfo(dllPath);
        lock (Cache)
        {
            if (Cache.TryGetValue(dllPath, out var cached) && cached.Size == info.Length && cached.Modified == info.LastWriteTimeUtc)
                return cached.Meta;
        }

        var meta = Inspect(dllPath);
        lock (Cache)
        {
            Cache[dllPath] = new CachedMeta(info.Length, info.LastWriteTimeUtc, meta);
            _cacheDirty = true;
        }
        return meta;
    }

    /// <summary>Guarda la caché si ha cambiado, sin las DLL que ya no existen.</summary>
    public static void SaveCache()
    {
        lock (Cache)
        {
            foreach (var path in Cache.Keys.Where(p => !File.Exists(p)).ToList())
            {
                Cache.Remove(path);
                _cacheDirty = true;
            }
            if (!_cacheDirty) return;
            try
            {
                JsonStore.Save(CachePath, Cache);
                _cacheDirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // La caché es una optimización: si no se puede escribir, el próximo arranque vuelve a leer las DLL.
                Log.Error("Guardar caché de plugins", ex);
            }
        }
    }

    private static Dictionary<string, CachedMeta> LoadCache() =>
        new(JsonStore.Load<Dictionary<string, CachedMeta>>(CachePath) ?? [], StringComparer.OrdinalIgnoreCase);

    public static AssemblyMeta Inspect(string dllPath)
    {
        using var fs = File.OpenRead(dllPath);
        return Inspect(fs, dllPath);
    }

    public static AssemblyMeta Inspect(Stream stream, string nameForLog)
    {
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) return new AssemblyMeta([], LoaderKind.Library, null);
            var md = pe.GetMetadataReader();

            var plugins = new List<PluginMeta>();
            string? infoVersion = null;
            foreach (var handle in md.CustomAttributes)
            {
                var attr = md.GetCustomAttribute(handle);
                switch (AttributeName(md, attr))
                {
                    case "BepInPlugin":
                        var args = attr.DecodeValue(StringTypeProvider.Instance).FixedArguments;
                        if (args.Length >= 3)
                            plugins.Add(new PluginMeta(args[0].Value as string ?? "", args[1].Value as string ?? "", args[2].Value as string ?? ""));
                        break;
                    case "AssemblyInformationalVersionAttribute" when attr.Parent.Kind == HandleKind.AssemblyDefinition:
                        var v = attr.DecodeValue(StringTypeProvider.Instance).FixedArguments;
                        if (v.Length == 1) infoVersion = v[0].Value as string;
                        break;
                }
            }

            var refs = md.AssemblyReferences.Select(r => md.GetString(md.GetAssemblyReference(r).Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var kind = plugins.Count == 0 ? LoaderKind.Library
                : refs.Contains("BepInEx.Unity.IL2CPP") ? LoaderKind.BepInEx6Il2Cpp
                : refs.Contains("BepInEx.Unity.Mono") ? LoaderKind.BepInEx6Mono
                : refs.Contains("BepInEx") ? LoaderKind.BepInEx5
                : LoaderKind.BepInEx6Il2Cpp;

            return new AssemblyMeta(plugins, kind, infoVersion);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        {
            // DLL nativa u ofuscada: se lista igualmente, solo que sin metadatos.
            Log.Error($"No se pudieron leer metadatos de {nameForLog}", ex);
            return new AssemblyMeta([], LoaderKind.Library, null);
        }
    }

    private static string? AttributeName(MetadataReader md, CustomAttribute attr)
    {
        EntityHandle typeHandle = attr.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor).GetDeclaringType(),
            _ => default,
        };
        return typeHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)typeHandle).Name),
            HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)typeHandle).Name),
            _ => null,
        };
    }

    /// <summary>Proveedor mínimo para decodificar argumentos de atributos (solo nos interesan strings).</summary>
    private sealed class StringTypeProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly StringTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type is "System.Type" or "Type";
    }
}
