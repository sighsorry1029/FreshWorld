// Simulated engine boundaries. The actual native GameWorld and ResetZones implementations are source-linked.
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

public readonly record struct Vector2i(int x, int y);
public readonly record struct ZDOID(long Id)
{
    public static ZDOID None => new(0);
}
public static class HashExtensions
{
    public static int GetStableHashCode(this string value)
    {
        unchecked
        {
            var first = 5381; var second = 5381;
            for (var i = 0; i < value.Length; i += 2)
            {
                first = ((first << 5) + first) ^ value[i];
                if (i == value.Length - 1) break;
                second = ((second << 5) + second) ^ value[i + 1];
            }
            return first + second * 1566083941;
        }
    }
}
public static class ZDOExtraData { public enum ConnectionType { Spawned } }
public static class ZDOVars { public static readonly int s_creator = "creator".GetStableHashCode(); }
public class ZDO(long id, string prefab, Vector3 position)
{
    public ZDOID m_uid = new(id);
    public bool Valid = true;
    public long Owner;
    public long Creator;
    public ZDOID Spawned;
    public bool IsValid() => Valid;
    public int GetPrefab() => prefab.GetStableHashCode();
    public long GetLong(int key) => key == ZDOVars.s_creator ? Creator : 0;
    public Vector3 GetPosition() => position;
    public void SetPosition(Vector3 value) => position = value;
    public void SetOwner(long owner) => Owner = owner;
    public ZDOID GetConnectionZDOID(ZDOExtraData.ConnectionType kind) => Spawned;
}
public class ZDOMan
{
    public static ZDOMan instance = new();
    private readonly Dictionary<ZDOID, ZDO> m_objectsByID = new();
    private readonly List<ZDO>[] m_objectsBySector = [new()];
    private readonly Dictionary<Vector2i, List<ZDO>> m_objectsByOutsideSector = new();
    private readonly List<ZDOID> m_destroySendList = new();
    public readonly List<ZDO> Destroyed = new();
    public Action<ZDO>? AfterDestroy;
    private int SectorToIndex(Vector2i zone) => zone == new Vector2i(0, 0) ? 0 : -1;
    public static long GetSessionID() => 987;
    public void DestroyZDO(ZDO zdo)
    {
        Destroyed.Add(zdo); m_destroySendList.Add(zdo.m_uid); zdo.Valid = false;
        AfterDestroy?.Invoke(zdo);
    }
    private void SendDestroyed() { }
    public void Add(ZDO zdo, Vector2i? sector = null)
    {
        m_objectsByID[zdo.m_uid] = zdo;
        var zone = sector ?? ZoneSystem.GetZone(zdo.GetPosition());
        if (zone == new Vector2i(0, 0)) m_objectsBySector[0].Add(zdo);
        else
        {
            if (!m_objectsByOutsideSector.TryGetValue(zone, out var list))
                m_objectsByOutsideSector[zone] = list = new();
            list.Add(zdo);
        }
    }
    public void ClearObjects() { m_objectsByID.Clear(); m_objectsBySector[0].Clear(); m_objectsByOutsideSector.Clear(); }
    public List<ZDOID> DestructionQueue => m_destroySendList;
}
public class ZNetView(ZDO zdo)
{
    public GameObject gameObject = new();
    private ZDO? _zdo = zdo;
    public bool Reset;
    public ZDO? GetZDO() => _zdo;
    public void ResetZDO() { Reset = true; _zdo = null; }
}
public class ZNetScene
{
    public static ZNetScene instance = new();
    private readonly Dictionary<ZDO, ZNetView> m_instances = new();
    public int DestroyCalls;
    public void Add(ZDO zdo, ZNetView view) => m_instances[zdo] = view;
    public bool Has(ZDO zdo) => m_instances.ContainsKey(zdo);
    public void Destroy(GameObject go)
    {
        DestroyCalls++;
        var entry = m_instances.Single(pair => ReferenceEquals(pair.Value.gameObject, go));
        entry.Key.Valid = false;
        UnityEngine.Object.Destroy(go);
        m_instances.Remove(entry.Key);
    }
}
public class Player
{
    public static Player? m_localPlayer;
    public ZDOID Id;
    public Transform transform = new();
    public ZDOID GetZDOID() => Id;
}
public class PeerSocket
{
    public bool Connected = true;
    public bool IsConnected() => Connected;
}
public class ZNetPeer
{
    public long m_uid = 1;
    public bool m_server;
    public ZDOID m_characterID;
    public Vector3 m_refPos;
    public bool m_publicRefPos;
    public PeerSocket? m_socket = new();
    public bool IsReady() => m_uid != 0;
    public Vector3 GetRefPos() => m_refPos;
}
public class ZNet
{
    public static ZNet instance = new();
    public sealed class PlayerInfo { public ZDOID m_characterID; public Vector3 m_position; }
    public bool Server = true;
    public bool Dedicated;
    public bool HaveStopped;
    public bool IsServer() => Server;
    public bool IsDedicated() => Dedicated;
    public List<PlayerInfo> Players = new();
    public List<ZNetPeer> Peers = new();
    public List<PlayerInfo> GetPlayerList() => Players;
    public List<ZNetPeer> GetPeers() => Peers;
}
public class ZoneSystem
{
    public static ZoneSystem instance = new();
    private sealed class ZoneData { public GameObject m_root = new(); }
    private Dictionary<Vector2i, ZoneData> m_zones = new();
    private HashSet<Vector2i> m_generatedZones = new();
    private Dictionary<Vector2i, List<ZDO>> m_loadingObjectsInZones = new();
    private readonly HashSet<Vector2i> _loaded = new();
    public Dictionary<Vector2i, LocationInstance> m_locationInstances = new();
    public int Pokes;
    public struct LocationInstance { public bool m_placed; public Vector3 m_position; }
    public bool IsZoneLoaded(Vector2i zone) => m_zones.ContainsKey(zone) && _loaded.Contains(zone) &&
        !m_loadingObjectsInZones.ContainsKey(zone);
    public static Vector2i GetZone(Vector3 position) => new(
        (int)Math.Floor((float)(((double)position.x + 32.0) / 64.0)),
        (int)Math.Floor((float)(((double)position.z + 32.0) / 64.0)));
    private bool PokeLocalZone(Vector2i zone)
    {
        Pokes++;
        if (!m_zones.ContainsKey(zone)) m_zones[zone] = new();
        _loaded.Add(zone);
        return true;
    }
    public GameObject AddRoot(Vector2i zone, bool loaded = true)
    {
        var data = new ZoneData(); m_zones[zone] = data;
        if (loaded) _loaded.Add(zone);
        return data.m_root;
    }
    public void SetLoadingInZone(ZDO zdo)
    {
        var zone = GetZone(zdo.GetPosition());
        if (m_loadingObjectsInZones.TryGetValue(zone, out var loading)) loading.Add(zdo);
        else m_loadingObjectsInZones.Add(zone, new() { zdo });
    }
    public void UnsetLoadingInZone(ZDO zdo)
    {
        // Match the game's strict indexing: the proxy that registered must retain its bucket.
        var zone = GetZone(zdo.GetPosition());
        m_loadingObjectsInZones[zone].Remove(zdo);
        if (m_loadingObjectsInZones[zone].Count == 0) m_loadingObjectsInZones.Remove(zone);
    }
    public void AddGenerated(Vector2i zone) => m_generatedZones.Add(zone);
    public bool HasRoot(Vector2i zone) => m_zones.ContainsKey(zone);
    public bool IsLoading(Vector2i zone) => m_loadingObjectsInZones.ContainsKey(zone);
}
public class WorldGenerator
{
    public static WorldGenerator instance = new();
    public float GetHeight(float x, float z) => 77;
}
public class ClutterSystem
{
    public static ClutterSystem instance = new();
    public int Cleared;
    public void ClearAll() => Cleared++;
}
public class Minimap
{
    public static Minimap instance = new();
    public int Refreshes;
    private void UpdateLocationPins(float dt) => Refreshes++;
}
public class Heightmap
{
    private static readonly List<Heightmap> s_heightmaps = new();
    private object? m_buildData = new();
    public int Pokes;
    public bool HasBuildData => m_buildData != null;
    public void Poke(bool delayed) => Pokes++;
    public static Heightmap Add() { var map = new Heightmap(); s_heightmaps.Add(map); return map; }
    public static void Reset() => s_heightmaps.Clear();
}
public class ZPackage
{
    public int Count;
    public List<ZDOID> Ids = new();
    public void Write(int count) => Count = count;
    public void Write(ZDOID id) => Ids.Add(id);
}
public class ZRoutedRpc
{
    public static ZRoutedRpc instance = new();
    public const long Everybody = -1;
    public List<ZPackage> Sent = new();
    public void InvokeRoutedRPC(long target, string method, params object[] args) => Sent.Add((ZPackage)args[0]);
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public class Object
    {
        public bool Destroyed;
        public Action? OnDestroy;
        private static readonly Queue<Action> DeferredDestroyCallbacks = new();
        public static void Destroy(Object obj)
        {
            if (obj.Destroyed) return;
            obj.Destroyed = true;
            if (obj.OnDestroy != null) DeferredDestroyCallbacks.Enqueue(obj.OnDestroy);
        }
        public static void FlushDestroyCallbacks()
        {
            while (DeferredDestroyCallbacks.Count > 0) DeferredDestroyCallbacks.Dequeue()();
        }
        public static void ResetDestroyCallbacks() => DeferredDestroyCallbacks.Clear();
    }
    public class GameObject : Object { }
    public class Transform { public Vector3 position; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch(Type type, string method) : Attribute
    {
        public Type Target = type;
        public string Method = method;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public delegate ref F FieldRef<in T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            var field = Field(typeof(T), name);
            var method = new DynamicMethod("test_field_" + name, typeof(F).MakeByRefType(), [typeof(T)], true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, field); il.Emit(OpCodes.Ret);
            return (FieldRef<T, F>)method.CreateDelegate(typeof(FieldRef<T, F>));
        }
        public static FieldInfo Field(Type type, string name) => type.GetField(name, Flags) ?? throw new MissingFieldException(type.FullName, name);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, Flags) ?? throw new MissingMethodException(type.FullName, name);
        public static T MethodDelegate<T>(MethodInfo method) where T : Delegate => (T)method.CreateDelegate(typeof(T));
    }
}
namespace FreshWorld.Engine
{
    internal sealed class OperationParameters { }
    internal abstract class ZoneOperation
    {
        protected Action<string> Log;
        protected int Failed;
        protected ZoneOperation(Action<string> log, OperationParameters args, HashSet<Vector2i>? candidates = null) { Log = log; Failed = 0; }
        protected abstract bool ExecuteZone(Vector2i zone);
        protected abstract void OnEnd();
    }
    [Flags]
    internal enum BorderDirection { None, North = 1, East = 2, South = 4, West = 8, NorthEast = 16, SouthEast = 32, SouthWest = 64, NorthWest = 128 }
    internal static class TerrainResetter
    {
        public static Dictionary<Vector2i, BorderDirection>? Borders;
        public static void ResetBorders(IReadOnlyDictionary<Vector2i, BorderDirection> borders) => Borders = new(borders);
    }
}
