// Minimal game boundary for source-linked native generation/control-flow tests. It does not simulate
// actual world generation, terrain meshes, asset bundles, networking, or Unity object lifetime.
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using FreshWorld.Engine;
using UnityEngine;

public readonly record struct Vector2s(short x, short y) { public Vector2s(int x, int y) : this((short)x, (short)y) { } }
public static class StableHash
{
    public static int GetStableHashCode(this string value)
    {
        unchecked { return value.Aggregate(17, (hash, ch) => hash * 31 + ch); }
    }
}
public sealed class ZDO(string prefab, Vector3 position)
{
    public readonly string Name = prefab;
    public int GetPrefab() => Name.GetStableHashCode();
    public Vector3 GetPosition() => position;
}
public sealed class World { public long m_uid = 1; }
public sealed class ZNet
{
    public static ZNet instance = new();
    public static World World = new();
    public long GetWorldUID() => World.m_uid;
}
public sealed class ZDOMan { public static ZDOMan instance = new(); }
public sealed class ZNetView
{
    private static bool m_ghostInit;
    public static bool Ghost => m_ghostInit;
    public static void StartGhostInit() => m_ghostInit = true;
    public static void FinishGhostInit() => m_ghostInit = false;
}
public sealed class WearNTear
{
    public static bool m_randomInitialDamage;
    public static bool RandomDamage { get => m_randomInitialDamage; set => m_randomInitialDamage = value; }
}
public sealed class ZNetScene
{
    public static ZNetScene instance = new();
    public readonly Dictionary<string, GameObject> Prefabs = new();
    public GameObject? GetPrefab(string name) => Prefabs.GetValueOrDefault(name);
}
public sealed class Heightmap : UnityEngine.Object { }
public struct PrefabReference(bool valid, string? name, GameObject? asset = null)
{
    public static int AssetReads;
    private readonly string? m_name = name;
    public bool IsValid => valid;
    public bool IsLoaded => valid && asset != null;
    public GameObject Asset { get { AssetReads++; return asset ?? throw new InvalidOperationException("Asset access must not cause an extra load."); } }
    public string Name => m_name ?? throw new InvalidOperationException("An unnamed invalid asset must not resolve its Name.");
}
public sealed class ZoneSystem
{
    public enum SpawnMode { Ghost }
    private sealed class ClearArea(Vector3 center, float radius)
    {
        public readonly Vector3 Center = center;
        public readonly float Radius = radius;
    }
    public sealed class ZoneVegetation
    {
        public GameObject m_prefab = null!;
        public bool m_enable;
        public ZoneVegetation Clone() => (ZoneVegetation)MemberwiseClone();
    }
    public sealed class ZoneLocation
    {
        public PrefabReference m_prefab;
        public bool m_clearArea = true;
        public float m_exteriorRadius = 10;
    }
    public struct LocationInstance
    {
        public ZoneLocation m_location;
        public Vector3 m_position;
        public bool m_placed;
    }

    public static ZoneSystem instance = new();
    private HashSet<Vector2s> m_generatedZones = new();
    private List<GameObject> m_tempSpawnedObjects = new();
    public readonly HashSet<Vector2s> Loaded = new();
    public readonly Dictionary<Vector2s, GameObject> Roots = new();
    public readonly Dictionary<Vector2s, LocationInstance> m_locationInstances = new();
    public List<ZoneVegetation> m_vegetation = new();
    public HashSet<Vector2s> Generated => m_generatedZones;
    public List<GameObject> Temporary => m_tempSpawnedObjects;
    public Action<IList, List<GameObject>>? VegetationPlacement;
    public Action<IList, List<GameObject>>? LocationPlacement;
    public bool AssetReady = true;
    public int VegetationCalls, LocationCalls, AssetChecks;
    public bool IsZoneLoaded(Vector2s zone) => Loaded.Contains(zone);
    public static Vector3 GetZonePos(Vector2s zone) => new(zone.x * 64, 0, zone.y * 64);
    private bool PokeCanSpawnLocation(ZoneLocation location, bool firstSpawn) { AssetChecks++; return AssetReady; }
    private void PlaceVegetation(Vector2s zone, Vector3 center, Transform parent, Heightmap heightmap,
        List<ClearArea> clearAreas, SpawnMode mode, List<GameObject> spawned)
    {
        VegetationCalls++;
        VegetationPlacement?.Invoke(clearAreas, spawned);
    }
    private void PlaceLocations(Vector2s zone, Vector3 center, Transform parent, Heightmap heightmap,
        List<ClearArea> clearAreas, SpawnMode mode, List<GameObject> spawned)
    {
        LocationCalls++;
        LocationPlacement?.Invoke(clearAreas, spawned);
        var instance = m_locationInstances[zone];
        instance.m_placed = true;
        m_locationInstances[zone] = instance;
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object obj) => obj.Destroyed = true;
    }
    public sealed class GameObject : Object
    {
        public string name;
        public readonly Transform transform;
        public readonly List<Transform> Children = new();
        public bool activeSelf { get; private set; } = true;
        public Heightmap? Heightmap;
        public GameObject(string name = "object") { this.name = name; transform = new Transform(this); }
        public void SetActive(bool active) => activeSelf = active;
        public T? GetComponentInChildren<T>() where T : class => Heightmap as T;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class => Children.Prepend(transform).OfType<T>().ToArray();
    }
    public sealed class Transform(GameObject owner)
    {
        public GameObject gameObject = owner;
        public Vector3 position, localPosition;
        public Quaternion rotation, localRotation;
        public Vector3 localScale = new(1, 1, 1);
    }
    public readonly record struct Quaternion(int Value);
    public readonly struct Vector3(float px, float py, float pz)
    {
        public readonly float x = px, y = py, z = pz;
    }
    public static class Time { public static float timeScale = 1; }
    public static class Random
    {
        public readonly record struct State(int Value);
        public static State state;
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public delegate ref F FieldRef<in T, F>(T instance);
        public static Type? Inner(Type type, string name) => type.GetNestedType(name, Flags);
        public static FieldInfo Field(Type type, string name) => type.GetField(name, Flags)!;
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, Flags)!;
        public static ConstructorInfo Constructor(Type type, Type[] arguments) => type.GetConstructor(Flags, null, arguments, null)!;
        public static T MethodDelegate<T>(MethodInfo method) where T : Delegate => (T)method.CreateDelegate(typeof(T));
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            var method = new DynamicMethod(name, typeof(F).MakeByRefType(), new[] { typeof(T) }, typeof(AccessTools).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, Field(typeof(T), name));
            il.Emit(OpCodes.Ret);
            return (FieldRef<T, F>)method.CreateDelegate(typeof(FieldRef<T, F>));
        }
    }
}

namespace FreshWorld.Engine
{
    internal static class GameWorld
    {
        public static readonly Dictionary<Vector2s, List<ZDO>> Objects = new();
        public static readonly List<ZDO> Removed = new();
        public static int Pokes, Releases, Recalculations;
        public static Vector2s[] GeneratedSnapshot(HashSet<Vector2s>? candidates = null) =>
            ZoneSystem.instance.Generated.Where(zone => candidates == null || candidates.Contains(zone)).ToArray();
        public static bool IsGenerated(Vector2s zone) => ZoneSystem.instance.Generated.Contains(zone);
        public static List<ZDO> GetZDOs(Vector2s zone) => Objects.TryGetValue(zone, out var entries) ? new(entries) : new();
        public static void RemoveZDO(ZDO zdo) => Removed.Add(zdo);
        public static bool TryGetRoot(Vector2s zone, out GameObject root) => ZoneSystem.instance.Roots.TryGetValue(zone, out root!);
        public static void PokeZone(Vector2s zone) => Pokes++;
        public static void ReleaseZone(Vector2s zone) => Releases++;
        public static void RecalculateTerrain() => Recalculations++;
    }
    internal static class BaseProtection
    {
        public static HashSet<Vector2s> GetExcluded(int size) => new();
    }
    internal static class TerrainResetter
    {
        public static bool Active;
        public static readonly List<Vector3> Restored = new();
        public static void Execute(Vector3 position, float radius) => Restored.Add(position);
    }
    internal class ResetZones(Action<string> log, OperationParameters args, HashSet<Vector2s>? candidates = null) : ZoneOperation(log, args, candidates)
    {
        protected override bool ExecuteZone(Vector2s zone) => true;
    }
}
