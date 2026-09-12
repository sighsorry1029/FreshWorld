// Deliberately small substitutes for the native engine boundary. Tests source-link the production pipeline,
// tracking wrappers, exclusivity gate, and coroutine runner; no Valheim process or world is loaded.
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

public readonly record struct Vector2s(short x, short y) { public Vector2s(int x, int y) : this((short)x, (short)y) { } }
public class Terminal
{
    public class ConsoleEventArgs(string line, Terminal context)
    {
        public string Line = line;
        public Terminal Context = context;
    }
}
public class Console : Terminal { public static Console instance = new(); }
public class ZRpc { }
public class ZDO { }
public class World { public long m_uid = 123; }
public class ZNet
{
    public static ZNet instance = new();
    public static World World = new();
    public static bool m_loadError;
    public static Action? WorldSaveStarted;
    public bool Saving;
    public bool HaveStopped;
    public float SaveDoneTime;
    public bool IsServer() => true;
    public long GetWorldUID() => World.m_uid;
    public bool IsSaving() => Saving;
    public void Save(bool sync)
    {
        if (Fake.IgnoreSave) return;
        WorldSaveStarted?.Invoke();
        Saving = true;
        Fake.Calls.Add("save.start");
        if (Fake.SaveError != null)
            UnityEngine.Application.Emit(Fake.SaveError, UnityEngine.LogType.Error);
        if (!Fake.HoldSave)
        {
            Saving = false;
            SaveDoneTime++;
        }
    }
}
public class ZNetScene { public static ZNetScene instance = new(); }
public class ZDOMan { public static ZDOMan instance = new(); }
public class WorldGenerator { public static WorldGenerator instance = new(); }
public class DungeonDB
{
    public static DungeonDB instance = new();
    public bool SkipSaving() => false;
}
public sealed class Prefab(string id) { public string name = id; }
public readonly record struct LocationPrefab(string Name) { public bool IsValid => true; }
public class ZoneSystem
{
    public static ZoneSystem instance = new();
    public HashSet<Vector2s> m_generatedZones = new();
    public HashSet<Vector2s> Loaded = new();
    public List<ZoneVegetation> m_vegetation = new();
    public List<ZoneLocation> m_locations = new();
    public Dictionary<Vector2s, LocationInstance> m_locationInstances = new();
    public List<UnityEngine.GameObject> m_tempSpawnedObjects = new();
    public bool IsZoneLoaded(Vector2s zone) => Loaded.Contains(zone);
    public bool SkipSaving() => false;
    public class ZoneVegetation(string id) { public Prefab m_prefab = new(id); }
    public class ZoneLocation(string id) { public LocationPrefab m_prefab = new(id); }
    public struct LocationInstance
    {
        public bool m_placed;
        public ZoneLocation m_location;
    }
}

internal static class Fake
{
    public sealed record VegetationPass(string[] Ids, FreshWorld.Engine.OperationParameters Arguments)
    {
        public List<int> ChangedZones { get; } = new();
    }
    public static List<string> Calls = new();
    public static List<VegetationPass> VegetationPasses = new();
    public static Dictionary<string, FreshWorld.Engine.OperationParameters> Arguments = new();
    public static HashSet<Vector2s> MarkerZones = new();
    public static HashSet<Vector2s> PlayerZones = new();
    public static Action<string>? OnStart;
    public static string? ThrowOnZone;
    public static string? ThrowOnStart;
    public static bool LoadOnPoke = true;
    public static bool HoldSave;
    public static bool IgnoreSave;
    public static string? SaveError;
    public static int BorderRepairs;
    public static int LoadReleases;
    public static HashSet<Vector2s> ReleaseFailures = new();
    public static List<Exception> UnexpectedErrors = new();
    public static HashSet<UnityEngine.GameObject> DestroyedGhosts = new();

    public static void Reset()
    {
        Calls = new(); Arguments = new(); MarkerZones = new(); PlayerZones = new(); VegetationPasses = new();
        OnStart = null; ThrowOnZone = null; ThrowOnStart = null;
        LoadOnPoke = true; HoldSave = false; IgnoreSave = false; SaveError = null;
        BorderRepairs = 0; LoadReleases = 0;
        ReleaseFailures = new();
        UnexpectedErrors = new(); DestroyedGhosts = new();
        ZoneSystem.instance = new(); ZNet.instance = new(); ZNet.World = new();
        ZNet.m_loadError = false; ZNet.WorldSaveStarted = null;
        ZNetScene.instance = new(); ZDOMan.instance = new(); WorldGenerator.instance = new();
        UnityEngine.Application.Reset();
        UnityEngine.Time.timeScale = 1;
        
        
        
        
        FreshWorld.Engine.TerrainResetter.Active = false;
        FreshWorld.Engine.GameWorld.Pending.Clear();
    }

    public static void AddZone(int x, bool marker = false, string? location = "cave")
    {
        var zone = new Vector2s(x, 0);
        ZoneSystem.instance.m_generatedZones.Add(zone);
        if (marker) MarkerZones.Add(zone);
        if (location == null) return;
        var prefab = new ZoneSystem.ZoneLocation(location);
        if (!ZoneSystem.instance.m_locations.Any(loc => loc.m_prefab.Name == location))
            ZoneSystem.instance.m_locations.Add(prefab);
        ZoneSystem.instance.m_locationInstances[zone] = new()
        {
            m_location = prefab,
            m_placed = true
        };
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(GameObject obj) => Fake.DestroyedGhosts.Add(obj);
    }
    public class GameObject : Object { }
    public static class Time { public static float timeScale = 1; }
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public static class Application
    {
        public delegate void LogCallback(string message, string trace, LogType type);
        public static event LogCallback? logMessageReceivedThreaded;
        public static int ListenerCount => logMessageReceivedThreaded?.GetInvocationList().Length ?? 0;
        public static void Emit(string message, LogType type) => logMessageReceivedThreaded?.Invoke(message, "", type);
        public static void Reset() => logMessageReceivedThreaded = null;
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string name) { }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }
    public static class AccessTools
    {
        public delegate ref F FieldRef<in T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            if (name == "m_generatedZones")
                return (T instance) => ref Unsafe.As<HashSet<Vector2s>, F>(ref ((ZoneSystem)(object)instance!).m_generatedZones);
            if (name == "m_tempSpawnedObjects")
                return (T instance) => ref Unsafe.As<List<UnityEngine.GameObject>, F>(ref ((ZoneSystem)(object)instance!).m_tempSpawnedObjects);
            throw new MissingFieldException(name);
        }
        public static FieldInfo? Field(Type type, string name) => type.GetField(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            ?? throw new MissingMethodException(type.FullName, name);
    }
}

namespace FreshWorld.Configuration
{
    public sealed class RunOptions
    {
        public bool ZonesEnabled { get; set; } = true;
        public int ZoneSafeZones { get; set; } = 1;
        public string[] ProtectedPlayerObjects { get; set; } = [];
        public string[] ProtectedObjects { get; set; } = [];
        public bool VegetationEnabled { get; set; } = true;
        public string[] TerrainVegetationIds { get; set; } = ["copper"];
        public string[] VegetationIds { get; set; } = [];
        public float VegetationTerrainRadius { get; set; } = 20;
        public int VegetationSafeZones { get; set; }
        public bool LocationsEnabled { get; set; } = true;
        public string[] LocationIds { get; set; } = ["cave"];
        public int LocationSafeZones { get; set; }
        public int MaxZonesPerFrame { get; set; } = 1;
        public double FrameBudgetMilliseconds { get; set; } = 8;
        public float SaveTimeoutSeconds { get; set; } = 180;
    }
}

// Simulated low-level game operations. The production orchestration and operation lifecycle are source-linked.
namespace FreshWorld.Engine
{
    public static class NativePlacement
    {
        public static bool IsValidLocationPrefab(ZoneSystem.ZoneLocation location) => location.m_prefab.IsValid;
    }
    public static class GameWorld
    {
        public static readonly HashSet<Vector2s> Pending = new();
        public static Vector2s[] GeneratedSnapshot(HashSet<Vector2s>? candidates = null) =>
            ZoneSystem.instance.m_generatedZones.Where(zone => candidates == null || candidates.Contains(zone)).OrderBy(z => z.x).ToArray();
        public static HashSet<Vector2s> GeneratedSetSnapshot() => new(ZoneSystem.instance.m_generatedZones);
        public static void CollectPlayerZones(HashSet<Vector2s> zones) => zones.UnionWith(Fake.PlayerZones);
        public static bool IsGenerated(Vector2s zone) => ZoneSystem.instance.m_generatedZones.Contains(zone);
        public static void RecalculateTerrain() => Fake.Calls.Add("terrain.refresh");
        public static void PokeZone(Vector2s zone)
        {
            Pending.Add(zone);
            if (Fake.LoadOnPoke) ZoneSystem.instance.Loaded.Add(zone);
        }
        public static void ReleaseZone(Vector2s zone)
        {
            if (!Pending.Remove(zone)) return;
            ZoneSystem.instance.Loaded.Remove(zone);
            Fake.LoadReleases++;
            if (Fake.ReleaseFailures.Contains(zone)) throw new InvalidOperationException("injected release failure");
        }
    }
    public static class BaseProtection
    {
        public static void Configure(IEnumerable<string> placed, IEnumerable<string> always) { }
        public static void InvalidateCache() { }
        public static HashSet<Vector2s> GetExcluded(int size) => size > 0 ? new(Fake.MarkerZones) : new();
    }
    public static class TerrainResetter
    {
        public static bool Active;
        public static void InvalidateCache() { }
    }
    internal abstract class FakeZoneOperation : ZoneOperation
    {
        protected readonly string Kind;
        protected HashSet<string>? LocationIds;
        protected FakeZoneOperation(string kind, Action<string> log, OperationParameters args, HashSet<Vector2s>? candidates) : base(log, args, candidates)
        {
            Kind = kind;
            Fake.Arguments[kind] = args;
            Fake.Calls.Add(kind + ".create");
        }
        protected override string OnInit()
        {
            if (LocationIds != null)
                ZonesToUpgrade = ZonesToUpgrade.Where(zone => ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out var location) &&
                    LocationIds.Contains(location.m_location.m_prefab.Name)).ToArray();
            return base.OnInit();
        }
        protected override void OnStart()
        {
            Fake.Calls.Add(Kind + ".start");
            Fake.OnStart?.Invoke(Kind);
            if (Fake.ThrowOnStart == Kind) throw new InvalidOperationException("injected startup failure");
        }
        protected override void OnEnd() => Fake.Calls.Add(Kind + ".end");
    }
    internal class ResetZones(Action<string> log, OperationParameters args, HashSet<Vector2s>? candidates = null) : FakeZoneOperation("zones", log, args, candidates)
    {
        protected override bool ExecuteZone(Vector2s zone)
        {
            if (Fake.ThrowOnZone == Kind) throw new InvalidOperationException("injected reset failure");
            Fake.Calls.Add($"zones.change:{zone.x}");
            ZoneSystem.instance.m_generatedZones.Remove(zone);
            return true;
        }
        protected override void OnEnd() { Fake.BorderRepairs++; base.OnEnd(); }
    }
    internal class ResetVegetation : FakeZoneOperation
    {
        public HashSet<string> VegetationIds;
        private readonly Fake.VegetationPass pass;
        public ResetVegetation(Action<string> log, HashSet<string> ids, OperationParameters args, HashSet<Vector2s>? candidates = null) : base("vegetation", log, args, candidates)
        {
            VegetationIds = ids;
            pass = new(ids.OrderBy(id => id).ToArray(), args);
            Fake.VegetationPasses.Add(pass);
            Fake.Calls.Add("vegetation.ids:" + string.Join(",", pass.Ids));
        }
        protected override bool ExecuteZone(Vector2s zone)
        {
            if (!ZoneSystem.instance.IsZoneLoaded(zone)) { GameWorld.PokeZone(zone); return false; }
            if (Fake.ThrowOnZone == Kind)
            {
                ZoneSystem.instance.m_vegetation = new();
                TerrainResetter.Active = true;
                ZoneSystem.instance.m_tempSpawnedObjects.Clear();
                ZoneSystem.instance.m_tempSpawnedObjects.Add(new());
                throw new InvalidOperationException("injected vegetation generation failure");
            }
            Fake.Calls.Add($"vegetation.change:{zone.x}");
            pass.ChangedZones.Add(zone.x);
            GameWorld.ReleaseZone(zone);
            return true;
        }
    }
    internal class RegenerateLocations : FakeZoneOperation
    {
        public RegenerateLocations(Action<string> log, HashSet<string> ids, OperationParameters args, HashSet<Vector2s>? candidates = null) : base("locations", log, args, candidates)
        { LocationIds = ids; }
        protected override bool ExecuteZone(Vector2s zone)
        {
            if (!ZoneSystem.instance.IsZoneLoaded(zone)) { GameWorld.PokeZone(zone); return false; }
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out var location)) ExecuteLocation(zone, location);
            GameWorld.ReleaseZone(zone);
            return true;
        }
        protected virtual bool ExecuteLocation(Vector2s zone, ZoneSystem.LocationInstance location)
        {
            if (Fake.ThrowOnZone == Kind)
            {
                ZoneSystem.instance.m_tempSpawnedObjects.Clear();
                ZoneSystem.instance.m_tempSpawnedObjects.Add(new());
                throw new InvalidOperationException("injected location failure");
            }
            Fake.Calls.Add($"locations.change:{zone.x}");
            return true;
        }
    }
}
