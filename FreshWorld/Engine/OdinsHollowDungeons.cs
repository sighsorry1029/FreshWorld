using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Saved OH ownership and room geometry, independent of surface reset radii and scene loading.</summary>
internal sealed class OdinsHollowDungeons
{
    private const string LocationName = "OdinsHollowDungeon";
    private const string PluginGuid = "org.bepinex.plugins.odinshollow";
    private static readonly int LocationHash = LocationName.GetStableHashCode();
    private static readonly AccessTools.FieldRef<ZoneSystem, Dictionary<int, ZoneSystem.ZoneLocation>> LocationsByHash =
        AccessTools.FieldRefAccess<ZoneSystem, Dictionary<int, ZoneSystem.ZoneLocation>>("m_locationsByHash");
    private static readonly int LayoutKey = "OH_DG_Layout".GetStableHashCode();
    private static readonly int IdKey = "OH_DG_Id".GetStableHashCode();
    private static readonly int PackKey = "OH_Pack".GetStableHashCode();
    private static readonly int LocationKey = "location".GetStableHashCode();
    private readonly Dictionary<ZDOID, Interior> roots = new();
    private readonly Dictionary<int, Interior> owners = new();
    private readonly Dictionary<Vector2s, List<Interior>> sectors = new();
    private readonly Dictionary<ZDOID, ZDOID> linked = new();
    private readonly Dictionary<ZDOID, ZDO> contentSources = new();
    private LayoutContract? contract;
    private object? startRoom;
    private int refreshedFrame = -1;
    private bool hasTaggedObjects;
    public static bool IsInstalled => Chainloader.PluginInfos.ContainsKey(PluginGuid);
    public bool IsAvailable => contract != null || roots.Count > 0 || hasTaggedObjects;

    public static void CheckMissingOwner(ZoneSystem world)
    {
        // Vanilla restores saved location instances through this hash index. Usually this is a single miss.
        if (LocationsByHash(world).TryGetValue(LocationHash, out var definition) && IsDungeonLocation(definition) &&
            world.m_locationInstances.Values.Any(instance => instance.m_placed && ReferenceEquals(instance.m_location, definition)))
            throw new InvalidOperationException("A placed Odin's Hollow entrance requires the owning mod before reset.");
    }

    private static bool IsDungeonLocation(ZoneSystem.ZoneLocation location) =>
        NativePlacement.IsValidLocationPrefab(location) && location.m_prefab.name == LocationName;

    public IEnumerator Initialize(Action requireWorld)
    {
        var definition = ZoneSystem.instance.m_locations.FirstOrDefault(IsDungeonLocation);
        if (definition == null) yield break;
        contract = new LayoutContract();
        var reference = definition.m_prefab;
        if (!reference.IsValid) throw new InvalidOperationException("Odin's Hollow requires a readable registered location prefab before reset.");
        var acquired = false;
        var elapsed = Stopwatch.StartNew();
        try
        {
            acquired = true;
            reference.LoadAsync();
            while (!reference.IsLoaded)
            {
                requireWorld();
                if (Time.timeScale <= 0) elapsed.Stop(); else elapsed.Start();
                if (elapsed.Elapsed.TotalSeconds >= 30) throw new TimeoutException("Odin's Hollow entrance geometry did not load; no reset was started.");
                yield return null;
            }
            requireWorld();
            startRoom = contract.ReadStart(reference.Asset);
        }
        finally { if (acquired) reference.Release(); }
    }

    public static bool HasSavedData(ZDO zdo) => IsRoot(zdo) || zdo.GetInt(IdKey) != 0;

    public void Capture(IEnumerable<ZDO>? snapshot = null)
    {
        var objects = snapshot ?? GameWorld.AllZDOs();
        foreach (var zdo in objects)
            if (zdo.IsValid() && IsRoot(zdo)) Remember(zdo);
        Reindex();
        foreach (var zdo in objects)
        {
            if (!zdo.IsValid()) continue;
            var id = zdo.GetInt(IdKey);
            if (id != 0) hasTaggedObjects = true;
            if (owners.TryGetValue(id, out var owner)) RememberLinks(zdo, owner);
        }
    }

    private static bool IsWorldRoot(ZDO zdo) => zdo.GetInt(LocationKey) == LocationHash &&
        ZoneSystem.instance.m_locationProxyPrefab != null && zdo.GetPrefab() == ZoneSystem.instance.m_locationProxyPrefab.name.GetStableHashCode();
    private static bool IsRoot(ZDO zdo) => IsWorldRoot(zdo) || !string.IsNullOrEmpty(zdo.GetString(LayoutKey));

    private bool Remember(ZDO zdo)
    {
        if (roots.TryGetValue(zdo.m_uid, out var existing) && existing.Matches(zdo)) return false;
        roots[zdo.m_uid] = new Interior(zdo, ReadBoxes(zdo));
        return true;
    }

    private List<InteriorBox> ReadBoxes(ZDO root)
    {
        // Wand-created dungeons are not normal world locations. Keep their known content;
        // never apply the native elevated-layout contract or force regeneration to them.
        if (!IsWorldRoot(root)) return new();
        var text = root.GetString(LayoutKey);
        contract ??= new LayoutContract();
        if (startRoom == null) throw Invalid("the fixed entrance room was not resolved", root);
        var boxes = new List<InteriorBox> { contract.Transform(root, startRoom, Vector3.zero, Quaternion.identity) };
        if (text.Length == 0)
        {
            if (root.GetInt(IdKey) != 0 || root.GetBool("OH_DG_ContentPending".GetStableHashCode())) throw Invalid("content state has no saved layout", root);
            return boxes;
        }
        boxes.AddRange(contract.Read(root, text));
        return boxes;
    }

    private void Refresh()
    {
        // Existing native proxies are indexed even before OH creates a layout. No world scan per zone.
        if (refreshedFrame == Time.frameCount) return;
        refreshedFrame = Time.frameCount;
        var changed = false;
        foreach (var old in roots.Values.ToArray())
        {
            if (!old.Root.IsValid() || old.Root.m_uid != old.RootId) { roots.Remove(old.RootId); changed = true; }
            else if (!old.Matches(old.Root)) { Remember(old.Root); changed = true; }
        }
        if (changed) Reindex();
    }

    private void Reindex()
    {
        owners.Clear(); sectors.Clear();
        foreach (var interior in roots.Values)
        {
            if (interior.Id != 0)
            {
                if (owners.ContainsKey(interior.Id)) throw Invalid("multiple roots share a dungeon ID", interior.Root);
                owners.Add(interior.Id, interior);
            }
            foreach (var zone in interior.Zones)
            {
                if (!sectors.TryGetValue(zone, out var entries)) sectors[zone] = entries = new();
                entries.Add(interior);
            }
        }
    }

    private Interior? Owner(ZDO zdo)
    {
        Refresh();
        if (IsRoot(zdo) && (!roots.TryGetValue(zdo.m_uid, out var old) || !old.Matches(zdo)))
        { Remember(zdo); Reindex(); }
        if (roots.TryGetValue(zdo.m_uid, out var root)) return root;
        if (owners.TryGetValue(zdo.GetInt(IdKey), out var owner)) { RememberLinks(zdo, owner); return owner; }
        if (linked.TryGetValue(zdo.m_uid, out var rootId) && roots.TryGetValue(rootId, out owner)) return owner;
        if (!sectors.TryGetValue(ZoneSystem.GetZone(zdo.GetPosition()), out var nearby)) return null;
        foreach (var interior in nearby)
            if (interior.Boxes.Any(box => box.Contains(zdo.GetPosition())))
            {
                if (owner != null && owner != interior) throw Invalid("overlapping dungeon interiors", interior.Root);
                owner = interior;
            }
        return owner;
    }

    private void RememberLinks(ZDO parent, Interior owner)
    {
        contentSources[parent.m_uid] = parent;
        var spawned = parent.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
        if (spawned != ZDOID.None) AddLink(spawned, owner);
        var pack = parent.GetString(PackKey);
        if (pack.Length == 0) return;
        foreach (var entry in pack.Split(','))
        {
            var parts = entry.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var user) ||
                !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                throw Invalid("invalid saved creature pack", parent);
            AddLink(new ZDOID(user, id), owner);
        }
    }

    private void AddLink(ZDOID id, Interior owner)
    {
        if (linked.TryGetValue(id, out var previous) && previous != owner.RootId && roots.ContainsKey(previous)) throw Invalid("ambiguous content ownership", owner.Root);
        linked[id] = owner.RootId;
    }

    public bool IsRetainedObject(ZDO zdo) => zdo.IsValid() && (Owner(zdo) != null || zdo.GetInt(IdKey) != 0);

    public DungeonResetPlan Prepare(HashSet<Vector2s> resetAnchors, Func<Vector2s, bool> canReset,
        Func<ZDO, bool>? retainForeign)
    {
        Refresh();
        // A proxy can have appeared during native loading since the initial snapshot.
        var changed = false;
        foreach (var anchor in resetAnchors)
            foreach (var zdo in GameWorld.GetZDOs(anchor))
                if (IsRoot(zdo)) changed |= Remember(zdo);
        if (changed) Reindex();
        var selected = roots.Values.Where(interior => IsWorldRoot(interior.Root) && resetAnchors.Contains(interior.Anchor)).ToArray();
        foreach (var anchor in resetAnchors)
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(anchor, out var location) && location.m_placed &&
                location.m_location != null && IsDungeonLocation(location.m_location) && selected.Count(interior => interior.Anchor == anchor) != 1)
                throw new InvalidOperationException($"Odin's Hollow at {anchor}: a unique saved location proxy is required before reset.");

        var chosen = new HashSet<ZDOID>(selected.Select(interior => interior.RootId));
        foreach (var source in contentSources.Values.ToArray())
            if (source.IsValid() && owners.TryGetValue(source.GetInt(IdKey), out var owner)) RememberLinks(source, owner);
        // Query saved ZDOs, including unloaded Unity sectors and tagged objects outside room bounds.
        // This scan occurs only for an OH reset, not for every ordinary surface sector.
        var extras = new HashSet<ZDO>();
        if (selected.Length != 0)
        {
            var objects = GameWorld.AllZDOs().Where(zdo => zdo.IsValid()).ToArray();
            foreach (var zdo in objects)
                if (owners.TryGetValue(zdo.GetInt(IdKey), out var owner)) RememberLinks(zdo, owner);
            foreach (var zdo in objects)
            {
                var owner = Owner(zdo);
                if (owner == null || !chosen.Contains(owner.RootId)) continue;
                if (retainForeign?.Invoke(zdo) == true) throw new DungeonResetBlockedException($"Odin's Hollow {owner.RootId}: protected foreign dungeon content overlaps this reset; skipped.");
                extras.Add(zdo);
            }
        }
        var protectedSectors = new HashSet<Vector2s>(selected.SelectMany(interior => interior.Zones));
        protectedSectors.UnionWith(extras.Select(zdo => ZoneSystem.GetZone(zdo.GetPosition())));
        void Validate()
        {
            foreach (var interior in selected)
                if (!interior.Root.IsValid() || !interior.Matches(interior.Root))
                    throw Invalid("layout or root changed before reset", interior.Root);
            foreach (var zone in protectedSectors)
                if (!canReset(zone) || GameWorld.HasPendingZoneLoad(zone)) throw new DungeonResetBlockedException($"Odin's Hollow interior at {zone} is protected or loading; the entire dungeon reset was skipped.");
        }
        Validate();
        bool Retain(ZDO zdo)
        {
            if (retainForeign?.Invoke(zdo) == true) return true;
            var owner = Owner(zdo);
            return owner != null ? !chosen.Contains(owner.RootId) : zdo.GetInt(IdKey) != 0;
        }
        void Complete()
        {
            if (selected.Length == 0) return;
            var ids = new HashSet<int>(selected.Select(interior => interior.Id).Where(id => id != 0));
            foreach (var zdo in GameWorld.AllZDOs())
            {
                if (!zdo.IsValid()) continue;
                if (chosen.Contains(zdo.m_uid) || ids.Contains(zdo.GetInt(IdKey)) ||
                    (linked.TryGetValue(zdo.m_uid, out var id) && chosen.Contains(id)) ||
                    selected.Any(interior => interior.Boxes.Any(box => box.Contains(zdo.GetPosition()))))
                    throw Invalid("interior objects appeared or survived deletion", selected[0].Root);
            }
            if (protectedSectors.Any(GameWorld.HasPendingZoneLoad))
                throw Invalid("interior loading started during deletion", selected[0].Root);
        }
        return new DungeonResetPlan(Retain, extras, Validate, Complete);
    }

    private sealed class Interior
    {
        public readonly ZDO Root;
        public readonly ZDOID RootId;
        public readonly Vector2s Anchor;
        public readonly int Id;
        public readonly List<InteriorBox> Boxes;
        public readonly HashSet<Vector2s> Zones = new();
        private readonly string layout;
        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly bool pending, manual;
        private readonly int prefab, location;
        public Interior(ZDO root, List<InteriorBox> boxes)
        {
            Root = root; RootId = root.m_uid; Boxes = boxes; Id = root.GetInt(IdKey); layout = root.GetString(LayoutKey);
            prefab = root.GetPrefab(); location = root.GetInt(LocationKey);
            position = root.GetPosition(); rotation = root.GetRotation(); Anchor = ZoneSystem.GetZone(position);
            pending = root.GetBool("OH_DG_ContentPending".GetStableHashCode()); manual = root.GetBool("OH_DG_Manual".GetStableHashCode());
            foreach (var box in boxes)
            {
                var min = ZoneSystem.GetZone(box.Bounds.min); var max = ZoneSystem.GetZone(box.Bounds.max);
                for (var x = (int)min.x; x <= max.x; x++)
                    for (var y = (int)min.y; y <= max.y; y++)
                    {
                        Zones.Add(new Vector2s(x, y));
                        if (Zones.Count > 4096) throw Invalid("interior exceeds the supported sector range", root);
                    }
            }
        }
        public bool Matches(ZDO root) => ReferenceEquals(root, Root) && RootId == root.m_uid && prefab == root.GetPrefab() && location == root.GetInt(LocationKey) && Id == root.GetInt(IdKey) &&
            layout == root.GetString(LayoutKey) && position.Equals(root.GetPosition()) && rotation.Equals(root.GetRotation()) &&
            pending == root.GetBool("OH_DG_ContentPending".GetStableHashCode()) && manual == root.GetBool("OH_DG_Manual".GetStableHashCode());
    }

    private sealed class LayoutContract
    {
        private readonly MethodInfo deserialize;
        private readonly Type dungeon;
        private readonly MethodInfo startConnector, startBounds;
        private readonly FieldInfo room, position, rotation, bounds, center, half, boxRotation;
        public LayoutContract()
        {
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin))
                throw new InvalidOperationException("Saved Odin's Hollow dungeons require the owning mod to resolve their interior before reset.");
            var assembly = plugin.Instance.GetType().Assembly;
            Type Require(string name) => assembly.GetType("OdinsHollow.DungeonGen." + name) ?? throw new InvalidOperationException("Odin's Hollow layout contract is missing: " + name);
            var placed = Require("PlacedRoom"); var template = Require("RoomTemplate"); var obb = Require("Obb");
            dungeon = Require("OHDungeon");
            startConnector = dungeon.GetMethod("FindStartConnector", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                ?? throw new InvalidOperationException("Odin's Hollow entrance connector is unavailable.");
            startBounds = dungeon.GetMethod("StartRoomBounds", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Transform) }, null)
                ?? throw new InvalidOperationException("Odin's Hollow entrance bounds are unavailable.");
            if (startConnector.ReturnType != typeof(Transform) || startBounds.ReturnType != typeof(Nullable<>).MakeGenericType(obb))
                throw new InvalidOperationException("Odin's Hollow entrance geometry contract changed.");
            var parameters = new[] { typeof(string), typeof(int).MakeByRefType(), typeof(List<>).MakeGenericType(placed).MakeByRefType() };
            deserialize = Require("DungeonManager").GetMethod("TryDeserialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null)
                ?? throw new InvalidOperationException("Odin's Hollow layout reader is unavailable.");
            if (deserialize.ReturnType != typeof(bool)) throw new InvalidOperationException("Odin's Hollow layout reader signature changed.");
            room = Field(placed, "Room", template); position = Field(placed, "Position", typeof(Vector3)); rotation = Field(placed, "Rotation", typeof(Quaternion));
            bounds = Field(template, "Bounds", obb); center = Field(obb, "Center", typeof(Vector3)); half = Field(obb, "HalfExtents", typeof(Vector3)); boxRotation = Field(obb, "Rotation", typeof(Quaternion));
        }
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expected) throw new InvalidOperationException("Odin's Hollow layout field changed: " + type.Name + "." + name);
            return field;
        }
        public object ReadStart(GameObject prefab)
        {
            var instance = prefab.GetComponent(dungeon);
            if (instance == null || startConnector.Invoke(instance, null) is not Transform connector ||
                startBounds.Invoke(instance, new object[] { connector }) is not object box)
                throw new InvalidOperationException("Odin's Hollow fixed entrance room cannot be resolved; reset stopped.");
            return box;
        }
        public List<InteriorBox> Read(ZDO root, string layout)
        {
            var entries = layout.Split('|');
            if (!entries[0].StartsWith("OHDG1;", StringComparison.Ordinal) || entries.Length > 4097 || root.GetInt(IdKey) == 0)
                throw Invalid("unsupported saved layout", root);
            object?[] args = { layout, 0, null };
            if (deserialize.Invoke(null, args) is not true || args[2] is not IList rooms || rooms.Count != entries.Length - 1)
                throw Invalid("saved rooms are missing or unreadable", root);
            var result = new List<InteriorBox>();
            foreach (var placed in rooms)
            {
                var template = room.GetValue(placed)!; var box = bounds.GetValue(template)!;
                var p = (Vector3)position.GetValue(placed)!; var r = (Quaternion)rotation.GetValue(placed)!;
                result.Add(Transform(root, box, p, r));
            }
            return result;
        }
        public InteriorBox Transform(ZDO root, object box, Vector3 p, Quaternion r)
        {
                var c = (Vector3)center.GetValue(box)!; var h = (Vector3)half.GetValue(box)!; var q = (Quaternion)boxRotation.GetValue(box)!;
                if (!Finite(p) || !Finite(c) || !Finite(h) || h.x < 0 || h.y < 0 || h.z < 0 || h.magnitude > 1024 || !Valid(r) || !Valid(q))
                    throw Invalid("invalid room geometry", root);
                var worldRotation = root.GetRotation() * r;
                var worldCenter = root.GetPosition() + root.GetRotation() * p + worldRotation * c;
                var orientation = worldRotation * q;
                var extent = new Bounds(worldCenter, Vector3.zero);
                for (var x = -1; x <= 1; x += 2)
                    for (var y = -1; y <= 1; y += 2)
                        for (var z = -1; z <= 1; z += 2) extent.Encapsulate(worldCenter + orientation * new Vector3(x * h.x, y * h.y, z * h.z));
                // Never turn a malformed/ground wand layout into a broad surface deletion scope.
                if (!Finite(extent.min) || !Finite(extent.max) || extent.min.y < 1000 ||
                    Math.Abs(extent.min.x) > 1000000 || Math.Abs(extent.max.x) > 1000000 || Math.Abs(extent.min.z) > 1000000 || Math.Abs(extent.max.z) > 1000000)
                    throw Invalid("interior is outside the supported elevated world space", root);
                return new InteriorBox(worldCenter, orientation, h, extent);
        }
        private static bool Valid(Quaternion q) => Finite(new Vector3(q.x, q.y, q.z)) && !float.IsNaN(q.w) && !float.IsInfinity(q.w) &&
            Math.Abs(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w - 1) < 0.01;
        private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
    }
    private sealed class InteriorBox
    {
        public readonly Bounds Bounds;
        private readonly Vector3 center, half;
        private readonly Quaternion inverse;
        public InteriorBox(Vector3 center, Quaternion rotation, Vector3 half, Bounds bounds)
        { this.center = center; this.half = half; inverse = Quaternion.Inverse(rotation); Bounds = bounds; }
        public bool Contains(Vector3 point)
        {
            var local = inverse * (point - center);
            return Math.Abs(local.x) <= half.x && Math.Abs(local.y) <= half.y && Math.Abs(local.z) <= half.z;
        }
    }
    private static Exception Invalid(string reason, ZDO root) => new InvalidOperationException($"Odin's Hollow {root.m_uid}: {reason}; reset stopped before regeneration.");
}
