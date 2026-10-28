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
    private static readonly AccessTools.FieldRef<ZDOMan, List<ZDOID>> PendingDestruction =
        AccessTools.FieldRefAccess<ZDOMan, List<ZDOID>>("m_destroySendList");
    private static readonly Action<ZDOMan, ZDOID> HandleDestroyed =
        AccessTools.MethodDelegate<Action<ZDOMan, ZDOID>>(AccessTools.Method(typeof(ZDOMan), "HandleDestroyedZDO"));
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

    public static bool HasPendingZoneLoad(Vector2s zone) => LoadingObjects(ZoneSystem.instance).ContainsKey(zone);

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

    // Local saved-sector lookup, including unloaded sectors. Queued deletions are being replaced
    // by this reset and must not suppress their replacements. No whole-world cache or scan.
    internal static bool HasRemainingPrefabNearZone(Vector2s zone, int prefab, float border)
    {
        HashSet<ZDOID>? pending = null;
        var center = ZoneSystem.GetZonePos(zone);
        for (var x = -1; x <= 1; ++x)
        for (var z = -1; z <= 1; ++z)
        {
            var zx = (int)zone.x + x;
            var zz = (int)zone.y + z;
            if (zx < short.MinValue || zx > short.MaxValue || zz < short.MinValue || zz > short.MaxValue) continue;
            foreach (var item in GetZDOs(new Vector2s(zx, zz)))
            {
                if (item.GetPrefab() != prefab) continue;
                var position = item.GetPosition();
                var dx = Math.Max(0, Math.Abs(position.x - center.x) - 32);
                var dz = Math.Max(0, Math.Abs(position.z - center.z) - 32);
                if (dx * dx + dz * dz > border * border) continue;
                pending ??= new HashSet<ZDOID>(PendingDestruction(ZDOMan.instance));
                if (!pending.Contains(item.m_uid)) return true;
            }
        }
        return false;
    }

    // DestroyZDO only queues IDs. Finish the group's queued deletions on the host before
    // releasing generation state; leave notifications queued for the normal bounded sender.
    public static void CompleteZoneDeletions(HashSet<Vector2s> zones, bool protectEpicLoot,
        bool protectEpicLootBounties, AlwaysProtectedObjects? alwaysProtected, ZoneDeletionPlan? plan = null)
    {
        var manager = ZDOMan.instance;
        var pending = new HashSet<ZDOID>(PendingDestruction(manager));
        var objects = zones.SelectMany(GetZDOs).Concat(plan?.Remaining() ?? Enumerable.Empty<ZDO>()).Distinct().ToArray();
        if (objects.Any(zdo => IsDeletionProtected(zdo, protectEpicLoot, protectEpicLootBounties, alwaysProtected)))
            throw new InvalidOperationException("A protected object remains in a location zone group; regeneration was not enabled.");
        if (plan != null && objects.Any(zdo => !plan.Matches(zdo)))
            throw new InvalidOperationException("An unplanned or changed object remains in a location zone group; regeneration was not enabled.");
        var remaining = objects.Select(zdo => zdo.m_uid).Distinct().ToArray();
        // Validate the complete snapshot before native callbacks can run or pool ZDO objects.
        if (remaining.Any(id => !pending.Contains(id) || (plan != null && !plan.Contains(id))))
            throw new InvalidOperationException("A location zone group still contains objects that were not scheduled for deletion; regeneration was not enabled.");
        foreach (var id in remaining)
        {
            // A prior native callback may move or protect a later object in this snapshot.
            if (ObjectsById(manager).TryGetValue(id, out var current) && current.IsValid() &&
                (!zones.Contains(ZoneSystem.GetZone(current.GetPosition())) ||
                 (plan != null && !plan.Matches(current)) ||
                 IsDeletionProtected(current, protectEpicLoot, protectEpicLootBounties, alwaysProtected)))
                throw new InvalidOperationException("An object changed during location zone group deletion; regeneration was not enabled.");
            HandleDestroyed(manager, id);
        }
        if (zones.Any(zone => GetZDOs(zone).Count != 0 || HasPendingZoneLoad(zone)) || plan?.Remaining().Any() == true)
            throw new InvalidOperationException("Objects or pending loads appeared during location zone group deletion; regeneration was not enabled.");
    }

    // Group-local snapshot only. No persistent cache, global scan, forced deletion or generation release.
    internal sealed class ZoneDeletionPlan
    {
        private sealed class Entry
        {
            internal readonly ZDO Object;
            internal readonly ZDOID Id, Spawned;
            internal readonly int Prefab;
            internal readonly Vector3 Position;
            internal readonly bool PendingBefore;
            internal string DeletePath = "not-attempted";
            internal Entry(ZDO zdo, HashSet<ZDOID> pending)
            {
                Object = zdo; Id = zdo.m_uid; Prefab = zdo.GetPrefab(); Position = zdo.GetPosition();
                Spawned = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                PendingBefore = pending.Contains(Id);
            }
            internal bool Matches(ZDO zdo) => ReferenceEquals(Object, zdo) && zdo.m_uid == Id &&
                zdo.GetPrefab() == Prefab && zdo.GetPosition().Equals(Position) &&
                zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned) == Spawned;
        }

        private readonly HashSet<Vector2s> zones;
        private readonly bool epicLoot, bounties;
        private readonly AlwaysProtectedObjects? absolute;
        private readonly Dictionary<ZDOID, Entry> entries = new();
        private readonly DateTime plannedUtc = DateTime.UtcNow;
        private DateTime executionUtc;
        internal string Stage { get; private set; } = "Plan";
        internal ZoneDeletionPlan(HashSet<Vector2s> group, bool protectEpicLoot, bool protectBounties, AlwaysProtectedObjects? alwaysProtected)
        { zones = new(group); epicLoot = protectEpicLoot; bounties = protectBounties; absolute = alwaysProtected; }

        internal void Capture()
        {
            var pending = new HashSet<ZDOID>(PendingDestruction(ZDOMan.instance));
            foreach (var zone in zones)
                foreach (var zdo in GetZDOs(zone)) Capture(zdo, pending);
            Validate();
        }

        private void Capture(ZDO zdo, HashSet<ZDOID> pending)
        {
            if (!zdo.IsValid() || !zones.Contains(ZoneSystem.GetZone(zdo.GetPosition())) || entries.ContainsKey(zdo.m_uid)) return;
            var entry = new Entry(zdo, pending);
            entries.Add(entry.Id, entry);
            // Include linked objects even if a mod has omitted them from the sector index; cycles terminate by ID.
            if (entry.Spawned != ZDOID.None && ObjectsById(ZDOMan.instance).TryGetValue(entry.Spawned, out var child)) Capture(child, pending);
        }

        internal bool Contains(ZDOID id) => entries.ContainsKey(id);
        internal bool Matches(ZDO zdo) => entries.TryGetValue(zdo.m_uid, out var entry) && entry.Matches(zdo);
        internal IEnumerable<ZDO> Remaining()
        {
            foreach (var id in entries.Keys)
                if (ObjectsById(ZDOMan.instance).TryGetValue(id, out var zdo) && zdo.IsValid()) yield return zdo;
        }

        internal void Validate()
        {
            Stage = "Preflight";
            if (zones.Any(HasPendingZoneLoad)) throw new InvalidOperationException("A location zone group has a pending load before deletion.");
            var manager = ObjectsById(ZDOMan.instance);
            foreach (var entry in entries.Values)
                if (!manager.TryGetValue(entry.Id, out var current) || !current.IsValid() || !entry.Matches(current) ||
                    Protection(current) != "none")
                    throw new InvalidOperationException("A location zone group object changed or is protected before deletion.");
            foreach (var zone in zones)
                foreach (var zdo in GetZDOs(zone))
                    if (!Matches(zdo)) throw new InvalidOperationException("An unplanned object appeared in a location zone group before deletion.");
        }

        internal void Delete()
        {
            Stage = "Delete";
            executionUtc = DateTime.UtcNow;
            bool WithinPlan(ZDO zdo) => zones.Contains(ZoneSystem.GetZone(zdo.GetPosition())) && Matches(zdo);
            void Deleting(ZDO zdo, string path)
            {
                if (!WithinPlan(zdo) || Protection(zdo) != "none")
                    throw new InvalidOperationException("A planned object changed during location zone group deletion.");
                entries[zdo.m_uid].DeletePath = path;
            }
            foreach (var entry in entries.Values)
            {
                if (!ObjectsById(ZDOMan.instance).TryGetValue(entry.Id, out var current) || !current.IsValid()) continue;
                if (!entry.Matches(current)) throw new InvalidOperationException("A planned object was replaced during location zone group deletion.");
                RemoveZDO(current, null, epicLoot, bounties, absolute, WithinPlan, Deleting);
            }
            Stage = "PostDelete";
            CompleteZoneDeletions(zones, epicLoot, bounties, absolute, this);
        }

        private string Protection(ZDO zdo)
        {
            if (IsPlayer(zdo)) return "player";
            if (absolute?.IsProtectedObject(zdo) == true) return "absolute-or-PoV";
            if (JotunInvasionProtection.IsProtectedObject(zdo)) return "jotun-objective";
            if (EpicLootProtection.IsProtectedObject(zdo, epicLoot, bounties)) return "epicloot";
            return "none";
        }

        internal void ReportFailure(Action<string> warn, string runId, Vector2s anchor, Exception error)
        {
            try
            {
                const int limit = 8;
                warn($"Location group stopped: runId={runId}; phase={Stage}; anchor={anchor}; error={error.Message}");
                var manager = ObjectsById(ZDOMan.instance);
                var pending = new HashSet<ZDOID>(PendingDestruction(ZDOMan.instance));
                var current = new Dictionary<ZDOID, ZDO>();
                foreach (var zdo in zones.SelectMany(GetZDOs)) current[zdo.m_uid] = zdo;
                foreach (var entry in entries.Values)
                    if (manager.TryGetValue(entry.Id, out var zdo)) current[entry.Id] = zdo;
                var locations = zones.Where(zone => ZoneSystem.instance.m_locationInstances.ContainsKey(zone)).Take(limit)
                    .Select(zone => ZoneSystem.instance.m_locationInstances[zone].m_location?.m_prefab.Name ?? "unknown");
                warn($"Location group state: locations={string.Join(",", locations)}; " +
                    $"sectors={zones.Count} [{string.Join(",", zones.Take(16))}]; omittedSectors={Math.Max(0, zones.Count - 16)}; plannedUtc={plannedUtc:O}; executionUtc={(executionUtc == default ? "not-started" : executionUtc.ToString("O"))}; " +
                    $"planned={entries.Count}; residual={current.Count}; plannedAbsentOrInvalid={entries.Keys.Count(id => !manager.TryGetValue(id, out var item) || !item.IsValid())}; detailLimit={limit}");
                var suspects = current.Where(pair => !entries.TryGetValue(pair.Key, out var entry) || !entry.Matches(pair.Value) ||
                    Protection(pair.Value) != "none" || !pending.Contains(pair.Key)).ToArray();
                var shown = suspects.Length > 0 ? suspects : current.ToArray();
                foreach (var pair in shown.Take(limit))
                {
                    var zdo = pair.Value;
                    entries.TryGetValue(pair.Key, out var entry);
                    var hash = zdo.GetPrefab();
                    var name = ZNetScene.instance.GetPrefab(hash)?.name ?? "unknown";
                    var inDictionary = manager.TryGetValue(pair.Key, out var resolved) && ReferenceEquals(resolved, zdo);
                    var inScene = SceneInstances(ZNetScene.instance).TryGetValue(zdo, out var view) && view != null;
                    var protection = Protection(zdo);
                    var exclusion = entry == null ? "unplanned" : !entry.Matches(zdo) ? "changed" : protection != "none" ? "protected" :
                        !pending.Contains(pair.Key) ? "not-queued" : "queued-native-deletion";
                    warn($"Location group object: id={pair.Key}; prefab={name}; hash={hash}; position={zdo.GetPosition()}; zone={ZoneSystem.GetZone(zdo.GetPosition())}; " +
                        $"valid={zdo.IsValid()}; dictionary={inDictionary}; scene={inScene}; planned={entry != null}; inScope={zones.Contains(ZoneSystem.GetZone(zdo.GetPosition()))}; protection={protection}; exclusion={exclusion}; " +
                        $"spawned={zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned)}; deletePath={entry?.DeletePath ?? "not-planned"}; " +
                        $"pendingBefore={entry?.PendingBefore.ToString() ?? "unknown"}; pendingNow={pending.Contains(pair.Key)}; newSincePlan={entry == null}");
                }
                if (shown.Length > limit) warn($"Location group diagnostics: {shown.Length - limit} additional objects omitted.");
            }
            catch { /* Diagnostics must never replace the original failure or unblock regeneration. */ }
        }
    }

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
        RemoveZDO(zdo, protectEpicLoot, protectEpicLootBounties, null);

    public static void RemoveZDO(ZDO zdo, bool protectEpicLoot, bool protectEpicLootBounties, AlwaysProtectedObjects? alwaysProtected) =>
        RemoveZDO(zdo, protectEpicLoot, protectEpicLootBounties, alwaysProtected, null);

    public static void RemoveZDO(ZDO zdo, bool protectEpicLoot, bool protectEpicLootBounties, AlwaysProtectedObjects? alwaysProtected,
        Func<ZDO, bool>? withinScope) =>
        RemoveZDO(zdo, null, protectEpicLoot, protectEpicLootBounties, alwaysProtected, withinScope);

    private static void RemoveZDO(ZDO zdo, HashSet<ZDOID>? visited, bool protectEpicLoot, bool protectEpicLootBounties, AlwaysProtectedObjects? alwaysProtected,
        Func<ZDO, bool>? withinScope, Action<ZDO, string>? deleting = null)
    {
        if (zdo == null || !zdo.IsValid() || (withinScope != null && !withinScope(zdo)) ||
            IsDeletionProtected(zdo, protectEpicLoot, protectEpicLootBounties, alwaysProtected)) return;
        if (visited != null && !visited.Add(zdo.m_uid)) return;
        var manager = ZDOMan.instance;
        zdo.SetOwner(ZDOMan.GetSessionID());
        var spawned = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
        if (spawned != ZDOID.None && ObjectsById(manager).TryGetValue(spawned, out var child) && child != zdo)
        {
            // A corrupt/modded connection cycle must not recurse forever while cleaning a zone.
            visited ??= new HashSet<ZDOID> { zdo.m_uid };
            RemoveZDO(child, visited, protectEpicLoot, protectEpicLootBounties, alwaysProtected, withinScope, deleting);
        }
        if (!zdo.IsValid()) return;
        var scene = ZNetScene.instance;
        if (SceneInstances(scene).TryGetValue(zdo, out var view) && view != null)
        {
            deleting?.Invoke(zdo, "ZNetScene.Destroy");
            scene.Destroy(view.gameObject);
        }
        else
        {
            deleting?.Invoke(zdo, "ZDOMan.DestroyZDO");
            manager.DestroyZDO(zdo);
        }
    }

    private static bool IsDeletionProtected(ZDO zdo, bool protectEpicLoot, bool protectEpicLootBounties,
        AlwaysProtectedObjects? alwaysProtected) => IsPlayer(zdo) || alwaysProtected?.IsProtectedObject(zdo) == true ||
        JotunInvasionProtection.IsProtectedObject(zdo) ||
        EpicLootProtection.IsProtectedObject(zdo, protectEpicLoot, protectEpicLootBounties);

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

    // Raw destruction notifications may outlive the coroutine; do not remove their batching patch early.
    internal static bool HasPendingMaintenanceCleanup
    {
        get
        {
            EnsureLoadWorld();
            return DeferredReleases.Count != 0 || OwnedLoads.Count != 0 ||
                (ZDOMan.instance != null && PendingDestruction(ZDOMan.instance).Count != 0);
        }
    }

    // Admission-only: pending destruction notifications are safe to drain during consumer work,
    // but our deferred root/view removal must finish before another generator owns the world.
    internal static bool HasPendingZoneCleanup
    {
        get { EnsureLoadWorld(); return DeferredReleases.Count != 0 || OwnedLoads.Count != 0; }
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
        [HarmonyPrefix]
        private static bool Prefix(ZDOMan __instance)
        {
            var pending = PendingDestruction(__instance);
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
