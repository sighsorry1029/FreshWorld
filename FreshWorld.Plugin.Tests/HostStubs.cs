// Only external host facilities and the destructive backend are faked. Tests execute the real
// plugin controller, configuration validation, scheduler/state persistence, and coroutine guard.
using System.Collections;
using FreshWorld.Configuration;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    {
        public string Guid { get; } = guid;
        public string Name { get; } = name;
        public string Version { get; } = version;
    }
    public static class Paths
    {
        public static string ConfigPath { get; set; } = Path.GetTempPath();
    }
    public abstract class BaseUnityPlugin
    {
        public Configuration.ConfigFile Config { get; }
        public Logging.ManualLogSource Logger { get; } = new();
        protected BaseUnityPlugin()
        {
            // Match BepInEx: the standard Config instance is named from the plugin metadata GUID.
            var metadata = (BepInPlugin?)Attribute.GetCustomAttribute(GetType(), typeof(BepInPlugin))
                ?? throw new InvalidOperationException("Plugin metadata is missing.");
            Config = new Configuration.ConfigFile(Path.Combine(Paths.ConfigPath, metadata.Guid + ".cfg"));
        }
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public List<string> Messages { get; } = new();
        public void LogInfo(object message) => Messages.Add("INFO: " + message);
        public void LogWarning(object message) => Messages.Add("WARN: " + message);
        public void LogError(object message) => Messages.Add("ERROR: " + message);
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigFile(string path)
    {
        private readonly Dictionary<string, object> entries = new();
        private readonly Dictionary<string, object> unbound = new();
        public string ConfigFilePath { get; } = path;
        public bool SaveOnConfigSet { get; set; } = true;
        public int ReloadCount { get; private set; }
        public int SaveCount { get; private set; }
        public ConfigEntry<T> Bind<T>(string section, string key, T value, ConfigDescription description)
        {
            string combined = section + "." + key;
            if (entries.TryGetValue(combined, out object? existing)) return (ConfigEntry<T>)existing;
            var entry = new ConfigEntry<T>(unbound.TryGetValue(combined, out object? raw) ?
                raw is T typed ? typed : (T)TomlTypeConverter.ConvertToValue((string)raw, typeof(T)) : value,
                new ConfigDefinition(section, key), description);
            entries.Add(combined, entry);
            return entry;
        }
        public void Set(string section, string key, string value)
        {
            string combined = section + "." + key;
            if (entries.TryGetValue(combined, out object? entry)) ((ConfigEntryBase)entry).SetSerializedValue(value);
            else unbound[combined] = value;
        }
        public bool IsBound(string section, string key) => entries.ContainsKey(section + "." + key);
        public void Reload() => ReloadCount++;
        public void Save() => SaveCount++;
    }
}

namespace HarmonyLib
{
    public sealed class Harmony(string id)
    {
        public string Id { get; } = id;
        public List<Type> PatchedTypes { get; } = new();
        public void PatchAll(Type type) => PatchedTypes.Add(type);
        public void UnpatchSelf() { }
    }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch(Type type, string name, params Type[] argumentTypes) : Attribute
    {
        public Type Type { get; } = type;
        public string Name { get; } = name;
        public Type[] ArgumentTypes { get; } = argumentTypes;
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
}

namespace ServerSync
{
    // Embedded libraries register their own patches; plugin discovery must leave them alone.
    [HarmonyLib.HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown), typeof(bool))]
    internal static class EmbeddedPatchFixture { }
}

namespace UnityEngine
{
    public static class Time
    {
        public static float realtimeSinceStartup;
        public static float timeScale = 1;
    }
}

public sealed class ZNet
{
    public static ZNet instance = null!;
    public static object? World;
    public static bool m_loadError;
    public bool Server = true;
    public bool HaveStopped;
    public bool Saving;
    public long Uid = 42;
    public double Seconds;
    public bool IsServer() => Server;
    public bool IsSaving() => Saving;
    public long GetWorldUID() => Uid;
    public string GetWorldName() => "Fixture world";
    public double GetTimeSeconds() => Seconds;
    public void Shutdown(bool save = true) { }
    public void ShutdownWithoutSave(bool suspending) { }
}
public sealed class ZoneSystem
{
    public static ZoneSystem instance = null!;
    public bool LocationsGenerated = true;
}
public sealed class ZDOMan { public static ZDOMan instance = null!; }
public sealed class ZNetScene { public static ZNetScene instance = null!; }
public sealed class WorldGenerator { public static WorldGenerator instance = null!; }
public sealed class DungeonDB { public static DungeonDB instance = null!; }
public sealed class Game { public static Game instance = null!; }
public sealed class EnvMan
{
    public static EnvMan instance = null!;
    public float m_dayLengthSec = 1800; // Vanilla _Environment prefab value overrides EnvMan's field initializer.
}

namespace FreshWorld.Engine
{
    internal static class GameWorld
    {
        public static int ProcessDeferredReleases() => 0;
    }
}

namespace FreshWorld.Backend
{
    internal sealed class MaintenancePipeline
    {
        internal sealed record Dispatch(RunOptions Options, bool IncludeVegetation);
        public static readonly List<Dispatch> Created = new();
        public static int Mutations;
        public static int Disposals;
        public static int HoldFrames;
        public static bool MutateBeforeYield;
        public static Exception? Failure;
        public MaintenancePipeline(RunOptions options, bool includeVegetation, Action<string> log, Action<string> warning)
            => Created.Add(new Dispatch(options, includeVegetation));
        public IEnumerator Run()
        {
            try
            {
                if (MutateBeforeYield) ++Mutations;
                for (int i = 0; i < HoldFrames; ++i) yield return null;
                if (Failure != null) throw Failure;
                ++Mutations;
            }
            finally { ++Disposals; }
        }
        public static void Reset()
        {
            Created.Clear(); Mutations = 0; Disposals = 0; HoldFrames = 0; MutateBeforeYield = false; Failure = null;
        }
    }
}
