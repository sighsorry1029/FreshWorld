using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Small game boundary shared by the native reset operations. No publicized game DLL is required.</summary>
internal static class GameWorld
{
    private static readonly AccessTools.FieldRef<ZoneSystem, HashSet<Vector2s>> GeneratedZones =
        AccessTools.FieldRefAccess<ZoneSystem, HashSet<Vector2s>>("m_generatedZones");
    private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> ObjectsById =
        AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
    private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> SceneInstances =
        AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
    private static readonly AccessTools.FieldRef<ZoneSystem, Dictionary<Vector2s, List<ZDO>>> LoadingObjects =
        AccessTools.FieldRefAccess<ZoneSystem, Dictionary<Vector2s, List<ZDO>>>("m_loadingObjectsInZones");
    private static readonly Func<ZoneSystem, Vector2s, bool> PokeLocalZone =
        AccessTools.MethodDelegate<Func<ZoneSystem, Vector2s, bool>>(AccessTools.Method(typeof(ZoneSystem), "PokeLocalZone"));

    // ZoneData is a private nested game type; retain that boundary behind non-generic dictionary access.
    private static readonly FieldInfo ZoneRoots = AccessTools.Field(typeof(ZoneSystem), "m_zones")
        ?? throw new MissingFieldException(typeof(ZoneSystem).FullName, "m_zones");
    private static readonly FieldInfo RootObject = AccessTools.Field(ZoneRoots.FieldType.GetGenericArguments()[1], "m_root")
        ?? throw new MissingFieldException("ZoneSystem.ZoneData", "m_root");
    private static readonly FieldInfo Heightmaps = AccessTools.Field(typeof(Heightmap), "s_heightmaps")
        ?? throw new MissingFieldException(typeof(Heightmap).FullName, "s_heightmaps");
    private static readonly FieldInfo HeightmapBuildData = AccessTools.Field(typeof(Heightmap), "m_buildData")
        ?? throw new MissingFieldException(typeof(Heightmap).FullName, "m_buildData");
    private static readonly int PlayerPrefab = "Player".GetStableHashCode();
    private static readonly Dictionary<Vector2s, GameObject> OwnedLoads = new();
    private static readonly HashSet<Vector2s> DeferredReleases = new();
    private static ZoneSystem? _loadWorld;

    public static Vector2s[] GeneratedSnapshot(HashSet<Vector2s>? candidates = null)
    {
        IEnumerable<Vector2s> zones = GeneratedZones(ZoneSystem.instance);
        if (candidates != null) zones = zones.Where(candidates.Contains);
        // Filter the native enumeration before its stable sort to preserve equal-distance ordering.
        return zones.OrderBy(zone => (long)zone.x * zone.x + (long)zone.y * zone.y).ToArray();
    }

    public static HashSet<Vector2s> GeneratedSetSnapshot() => new(GeneratedZones(ZoneSystem.instance));

    public static bool IsGenerated(Vector2s zone) => GeneratedZones(ZoneSystem.instance).Contains(zone);

    /// <summary>Returns a copy: deletion must not invalidate the live sector enumeration.</summary>
    public static List<ZDO> GetZDOs(Vector2s zone)
    {
        var objects = new List<ZDO>();
        // 1.0 stores portals separately and shares sector 0 between out-of-map coordinates.
        // The public API includes both registries; filter its snapshot to the requested zone.
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), objects);
        objects.RemoveAll(zdo => zdo == null || !zdo.IsValid() || ZoneSystem.GetZone(zdo.GetPosition()) != zone);
        return objects;
    }

    public static IEnumerable<ZDO> AllZDOs() => ObjectsById(ZDOMan.instance).Values.ToArray();

    /// <summary>Appends occupied host-world zones; the caller owns their lifetime for one maintenance run.</summary>
    public static void CollectPlayerZones(HashSet<Vector2s> zones)
    {
        if (zones == null) throw new ArgumentNullException(nameof(zones));
        var network = ZNet.instance;
        if (network == null || !network.IsServer() || network.HaveStopped) return;

        var local = Player.m_localPlayer;
        if (!network.IsDedicated() && local != null) TryAddPlayerZone(zones, local.transform.position);

        var manager = ZDOMan.instance;
        var objects = manager == null ? null : ObjectsById(manager);
        foreach (var peer in network.GetPeers())
        {
            if (peer == null || peer.m_server || !peer.IsReady() || peer.m_socket == null || !peer.m_socket.IsConnected()) continue;
            if (peer.m_characterID != ZDOID.None && objects != null &&
                objects.TryGetValue(peer.m_characterID, out var character) && character != null && character.IsValid() &&
                TryAddPlayerZone(zones, character.GetPosition())) continue;

            // Native PeerInfo establishes this reference before marking the peer ready. It is
            // updated independently of public map visibility and also covers loading/respawning.
            TryAddPlayerZone(zones, peer.GetRefPos());
        }
    }

    private static bool TryAddPlayerZone(HashSet<Vector2s> zones, Vector3 position)
    {
        if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
            float.IsNaN(position.y) || float.IsInfinity(position.y) ||
            float.IsNaN(position.z) || float.IsInfinity(position.z)) return false;
        // Match the native coordinate conversion's float intermediate before its short conversion.
        // Invalid or overflowing coordinates must never turn into an invented origin/edge zone.
        var x = (float)(((double)position.x + 32.0) / 64.0);
        var z = (float)(((double)position.z + 32.0) / 64.0);
        if (Math.Floor(x) < short.MinValue || Math.Floor(x) > short.MaxValue ||
            Math.Floor(z) < short.MinValue || Math.Floor(z) > short.MaxValue) return false;
        zones.Add(ZoneSystem.GetZone(position));
        return true;
    }

    public static void RemoveZDO(ZDO zdo) => RemoveZDO(zdo, true, false);

    public static void RemoveZDO(ZDO zdo, bool protectEpicLoot, bool protectEpicLootBounties) =>
        RemoveZDO(zdo, null, protectEpicLoot, protectEpicLootBounties);

    private static void RemoveZDO(ZDO zdo, HashSet<ZDOID>? visited, bool protectEpicLoot, bool protectEpicLootBounties)
    {
        if (zdo == null || !zdo.IsValid() || IsPlayer(zdo) ||
            EpicLootProtection.IsProtectedObject(zdo, protectEpicLoot, protectEpicLootBounties)) return;
        if (visited != null && !visited.Add(zdo.m_uid)) return;
        var manager = ZDOMan.instance;
        zdo.SetOwner(ZDOMan.GetSessionID());
        var spawned = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
        if (spawned != ZDOID.None && ObjectsById(manager).TryGetValue(spawned, out var child) && child != zdo)
        {
            // A corrupt/modded connection cycle must not recurse forever while cleaning a zone.
            visited ??= new HashSet<ZDOID> { zdo.m_uid };
            RemoveZDO(child, visited, protectEpicLoot, protectEpicLootBounties);
        }
        if (!zdo.IsValid()) return;
        var scene = ZNetScene.instance;
        if (SceneInstances(scene).TryGetValue(zdo, out var view) && view != null)
            scene.Destroy(view.gameObject);
        else
            manager.DestroyZDO(zdo);
    }

    private static bool IsPlayer(ZDO zdo)
    {
        if (zdo.GetPrefab() == PlayerPrefab) return true;
        var local = Player.m_localPlayer;
        if (local != null && local.GetZDOID() == zdo.m_uid) return true;
        var network = ZNet.instance;
        if (network == null) return false;
        var id = zdo.m_uid;
        foreach (var player in network.GetPlayerList())
            if (player.m_characterID == id) return true;
        foreach (var peer in network.GetPeers())
            if (peer.m_characterID == id) return true;
        return false;
    }

    private static void EnsureLoadWorld()
    {
        var current = ZoneSystem.instance;
        if (ReferenceEquals(current, _loadWorld)) return;
        OwnedLoads.Clear();
        DeferredReleases.Clear();
        _loadWorld = current;
    }

    public static string DescribeZoneLoad(Vector2s zone)
    {
        var world = ZoneSystem.instance;
        var hasRoot = TryGetRoot(zone, out _);
        var loading = LoadingObjects(world);
        var count = loading.TryGetValue(zone, out var objects) ? objects.Count : 0;
        return $"root={hasRoot}, loaded={world.IsZoneLoaded(zone)}, loadingObjects={count}";
    }

    public static void PokeZone(Vector2s zone)
    {
        EnsureLoadWorld();
        var world = ZoneSystem.instance;
        if (world.IsZoneLoaded(zone)) return;
        // A root that already exists may still be loading for a player. FreshWorld does not own it.
        var hadRoot = TryGetRoot(zone, out _);
        PokeLocalZone(world, zone);
        if (!hadRoot && TryGetRoot(zone, out var createdRoot))
        {
            OwnedLoads[zone] = createdRoot;
            DeferredReleases.Remove(zone);
        }
    }

    public static void ReleaseZone(Vector2s zone)
    {
        EnsureLoadWorld();
        if (!OwnedLoads.TryGetValue(zone, out var ownedRoot)) return;
        // Native streaming can replace a root before a deferred release is polled.
        if (!TryGetRoot(zone, out var root) || !ReferenceEquals(root, ownedRoot))
        {
            OwnedLoads.Remove(zone);
            DeferredReleases.Remove(zone);
            return;
        }
        // LocationProxy and DungeonGenerator unregister their loads during their own lifecycle.
        // Removing their root before that work finishes can interrupt asset spawning and zone readiness.
        if (!ZoneSystem.instance.IsZoneLoaded(zone))
        {
            DeferredReleases.Add(zone);
            return;
        }
        OwnedLoads.Remove(zone);
        DeferredReleases.Remove(zone);
        var objects = GetZDOs(zone);
        // A player can arrive while a manually poked zone is loading. Hand it back to normal streaming.
        if (objects.Any(zdo => zdo != null && zdo.IsValid() && IsPlayer(zdo))) return;
        var scene = ZNetScene.instance;
        var instances = SceneInstances(scene);
        foreach (var zdo in objects)
        {
            if (!instances.TryGetValue(zdo, out var view)) continue;
            if (view != null)
            {
                var go = view.gameObject;
                if (view.GetZDO() != null) view.ResetZDO();
                // Unspawn the scene object while retaining its network/world record.
                UnityEngine.Object.Destroy(go);
            }
            instances.Remove(zdo);
        }
        RemoveRoot(zone);
    }

    public static int ProcessDeferredReleases()
    {
        if (DeferredReleases.Count == 0) return 0;
        EnsureLoadWorld();
        if (ZNetScene.instance == null || ZDOMan.instance == null) return 0;
        var released = 0;
        foreach (var zone in DeferredReleases.ToArray())
        {
            if (!ZoneSystem.instance.IsZoneLoaded(zone) && TryGetRoot(zone, out _)) continue;
            ReleaseZone(zone);
            if (!DeferredReleases.Contains(zone)) released++;
        }
        return released;
    }

    public static bool TryGetRoot(Vector2s zone, out GameObject root)
    {
        var roots = (IDictionary)(ZoneRoots.GetValue(ZoneSystem.instance)
            ?? throw new InvalidOperationException("The zone root registry is not ready."));
        if (roots.Contains(zone) && RootObject.GetValue(roots[zone]) is GameObject value && value != null)
        {
            root = value;
            return true;
        }
        root = null!;
        return false;
    }

    public static void RemoveGeneratedZone(Vector2s zone)
    {
        EnsureLoadWorld();
        GeneratedZones(ZoneSystem.instance).Remove(zone);
        OwnedLoads.Remove(zone);
        DeferredReleases.Remove(zone);
        RemoveRoot(zone);
    }

    private static void RemoveRoot(Vector2s zone)
    {
        var world = ZoneSystem.instance;
        var roots = (IDictionary)(ZoneRoots.GetValue(world)
            ?? throw new InvalidOperationException("The zone root registry is not ready."));
        if (TryGetRoot(zone, out var root)) UnityEngine.Object.Destroy(root);
        roots.Remove(zone);
        // LocationProxy owns its loading registration. Unity dispatches OnDestroy later; removing
        // the bucket here breaks its strict UnsetLoadingInZone lookup (and can lose a new proxy).
    }

    public static void RecalculateTerrain()
    {
        var maps = (List<Heightmap>)(Heightmaps.GetValue(null)
            ?? throw new InvalidOperationException("The heightmap registry is not ready."));
        foreach (var map in maps.ToArray())
        {
            if (map == null) continue;
            HeightmapBuildData.SetValue(map, null);
            map.Poke(1, false);
        }
    }

    // Adapted from Upgrade World's deletion batching. Sending enormous destruction packets can crash
    // clients; limiting each update is required even after a maintenance coroutine has completed.
    [HarmonyPatch(typeof(ZDOMan), "SendDestroyed")]
    private static class DestroyBatchPatch
    {
        private const int MaxPerPacket = 10000;
        private static readonly AccessTools.FieldRef<ZDOMan, List<ZDOID>> Pending =
            AccessTools.FieldRefAccess<ZDOMan, List<ZDOID>>("m_destroySendList");

        [HarmonyPrefix]
        private static bool Prefix(ZDOMan __instance)
        {
            var pending = Pending(__instance);
            if (pending.Count < MaxPerPacket) return true;
            var package = new ZPackage();
            package.Write(MaxPerPacket);
            for (var i = 0; i < MaxPerPacket; i++) package.Write(pending[i]);
            pending.RemoveRange(0, MaxPerPacket);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "DestroyZDO", package);
            return false;
        }
    }
}
