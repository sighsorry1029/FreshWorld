using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Run-local, read-only Halls protection. PoV alone owns sessions, rewards and cleanup.</summary>
internal sealed class PathOfValheimDungeons
{
    private static readonly int Gate = "PoV_HallsGate".GetStableHashCode();
    private static readonly int Session = "PoV_HallMapSession".GetStableHashCode();
    private static readonly int MapRun = "PoV_HallMapRun".GetStableHashCode();
    private static readonly int LegacyRun = "PoV_HallsRun".GetStableHashCode();


    private static readonly int[] Links = { Hash("pt.hmap.run"), Hash("pt.hmap.live"), Hash("pt.halls.run"), Hash("pt.halls.pick") };
    private static readonly Vector3 MissingSlot = new(float.NaN, float.NaN, float.NaN);
    private readonly Dictionary<ZDOID, ZDO> roots = new();
    private readonly Dictionary<ZDOID, (ZDO Object, uint Revision, RunState State)> states = new();
    private readonly HashSet<ZDOID> retainedRoots = new();
    private readonly HashSet<string> runIds = new(StringComparer.Ordinal);

    private readonly HashSet<ZDOID> retainedObjects = new();
    private readonly HashSet<(float X, float Y, float Z, bool Map)> retainedSlots = new();
    private readonly List<LiveRuns> live = new();
    private readonly List<(IDictionary Cache, string Context)> layouts = new();
    private readonly HashSet<(object Layout, string Role)> checkedLayouts = new();
    private FieldInfo? layoutParts, layoutAnchors, partPosition;
    private int layoutFrame = -1, layoutCount = -1;
    private readonly Action<ZDO> observeRoot;
    private bool bound;

    public PathOfValheimDungeons() { observeRoot = RememberRoot; }
    public bool IsAvailable => roots.Count > 0 || live.Count > 0;

    public void Capture(IEnumerable<ZDO> objects)
    {
        BindLiveRuns();
        var snapshot = objects as ZDO[] ?? objects.ToArray();
        foreach (var zdo in snapshot) RememberRoot(zdo);
        Refresh();
        // Remember loose rewards before physics or another mod moves them outside the slot.
        foreach (var zdo in snapshot) ObserveObject(zdo);
    }
    public bool IsProtectedObject(ZDO zdo, bool refresh = true)
    {
        if (!zdo.IsValid()) return false;
        var prefab = zdo.GetPrefab();
        // Ground entrances and personal session records still protect their small anchor sector.
        return prefab == Gate || prefab == Session;
    }

    public bool IsRetainedObject(ZDO zdo)
    {
        if (!zdo.IsValid()) return false;
        RememberRoot(zdo);
        Refresh();
        return ObserveObject(zdo);
    }

    private bool ObserveObject(ZDO zdo)
    {
        if (!zdo.IsValid()) return false;
        if (IsProtectedObject(zdo) || retainedRoots.Contains(zdo.m_uid) || retainedObjects.Contains(zdo.m_uid)) return true;
        foreach (var key in Links)
            if (runIds.Contains(zdo.GetString(key))) { retainedObjects.Add(zdo.m_uid); return true; }
        foreach (var slot in retainedSlots)
            if (Inside(new Vector3(slot.X, slot.Y, slot.Z), slot.Map, zdo.GetPosition()))
            { retainedObjects.Add(zdo.m_uid); return true; }
        return false;
    }

    private void Refresh()
    {
        BindLiveRuns();
        ValidateLayouts();
        // A small loaded-controller registry, not a world scan. Inspect it even in the same frame:
        // PoV can create a controller or reuse a closed one during a zone-loading callback.
        foreach (var registry in live)
            registry.Observe(observeRoot);
        foreach (var root in roots.Values)
        {
            if (!IsRoot(root)) continue;
            var state = ReadState(root);
            Retain(root, state); // PoV also owns completed-run cleanup and slot reuse.
        }
    }

    private void RememberRoot(ZDO zdo)
    {
        if (IsRoot(zdo)) roots[zdo.m_uid] = zdo;
    }

    private static bool IsRoot(ZDO zdo) => zdo.IsValid() && (zdo.GetPrefab() == MapRun || zdo.GetPrefab() == LegacyRun);


    private static Vector3 ReadSlot(ZDO root, out bool map)
    {
        map = root.GetPrefab() == MapRun;
        if (root.GetInt(Hash(map ? "hm.v" : "h.v")) != 1) throw Invalid(root, "unknown or uninitialized run schema");
        var slot = root.GetVec3(Hash(map ? "hm.slot" : "h.slot"), MissingSlot);
        // Do not guess from the controller position: KeepInReach moves it away from the slot.
        if (!Finite(slot.x) || !Finite(slot.y) || !Finite(slot.z) || slot.y <= 3000 ||
            Math.Abs(slot.x) > 1000000 || Math.Abs(slot.z) > 1000000)
            throw Invalid(root, "missing or invalid dungeon slot");
        return slot;
    }

    private RunState ReadState(ZDO root)
    {
        // Native Set/receive paths advance DataRevision, including same-frame relaunches.
        // Scope this cache to this maintenance instance and this exact ZDO object/identity.
        if (states.TryGetValue(root.m_uid, out var cached) && ReferenceEquals(cached.Object, root) && cached.Revision == root.DataRevision)
            return cached.State;
        var slot = ReadSlot(root, out var map);
        var status = root.GetInt(Hash(map ? "hm.status" : "h.status"), -1);
        if (status < 0 || status > (map ? 6 : 4)) throw Invalid(root, "unknown run state");


        var id = map ? root.GetString(Hash("hm.run")) : root.m_uid.UserID.ToString("x") + "-" + root.m_uid.ID.ToString("x");
        var state = new RunState(slot, map, id);
        states[root.m_uid] = (root, root.DataRevision, state);
        return state;
    }

    private void Retain(ZDO root, RunState state)
    {
        retainedRoots.Add(root.m_uid);
        if (!string.IsNullOrEmpty(state.Id)) runIds.Add(state.Id);
        var slot = state.Slot;
        retainedSlots.Add((slot.x, slot.y, slot.z, state.Map));
    }

    private readonly struct RunState
    {
        public readonly Vector3 Slot;
        public readonly bool Map;
        public readonly string Id;
        public RunState(Vector3 slot, bool map, string id) { Slot = slot; Map = map; Id = id; }
    }

    // Authored map/lobby/boss room origins fit this native slot envelope. Run-linked
    // objects are retained independently of position; ground and other sky layers are not.
    private static bool Inside(Vector3 slot, bool map, Vector3 p) => p.y > 3000 && Math.Abs(p.y - slot.y) <= 60 &&
        Math.Abs(p.x - slot.x) <= (map ? 320 : 145) && p.z >= slot.z - (map ? 130 : 145) && p.z <= slot.z + (map ? 360 : 145);

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static int Hash(string key) => key.GetStableHashCode();
    private static Exception Invalid(ZDO root, string reason) => new InvalidOperationException(
        $"PoV dungeon protection: {(root.GetPrefab() == MapRun ? "PoV_HallMapRun" : "PoV_HallsRun")} {root.m_uid}: {reason}. FreshWorld cannot safely reset this world; PoV data was not changed.");

    private void BindLiveRuns()
    {
        if (bound) return;
        if (!Chainloader.PluginInfos.TryGetValue("treextr.pathofvalheiman", out var plugin)) { bound = true; return; }
        var assembly = plugin.Instance.GetType().Assembly;
        foreach (var name in new[] { "PassiveTree.Encounters.HallMaps.HallMapRun", "PassiveTree.Encounters.Halls.HallsRun" })
        {
            var type = assembly.GetType(name);
            if (type == null) continue; // Earlier PoV editions do not contain Halls.
            if (name == "PassiveTree.Encounters.HallMaps.HallMapRun")
            {
                RequireReach(type, "ReachX", 320f);
                RequireReach(type, "ReachZBack", 130f);
                RequireReach(type, "ReachZFront", 360f);
            }
            var field = type.GetField("Runs", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            var getter = type.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)?.GetGetMethod(true);
            if (field?.IsInitOnly != true || field.FieldType != typeof(List<>).MakeGenericType(type) ||
                getter == null || getter.ReturnType != typeof(ZDO) || getter.GetParameters().Length != 0 || getter.IsStatic ||
                !(field.GetValue(null) is IList list))
                throw new InvalidOperationException($"PoV dungeon protection: {name} live-run contract is unavailable; FreshWorld reset stopped.");
            live.Add(new LiveRuns(list, getter));
        }
        if (live.Count > 0)
        {
            var type = assembly.GetType("PassiveTree.Encounters.Layout") ?? throw new InvalidOperationException("PoV dungeon layout metadata is unavailable.");
            layoutParts = type.GetField("Parts", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            layoutAnchors = type.GetField("Anchors", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (layoutParts == null || !layoutParts.FieldType.IsGenericType || layoutParts.FieldType.GetGenericTypeDefinition() != typeof(List<>) ||
                layoutAnchors?.FieldType != typeof(Dictionary<string, List<Vector3>>))
                throw new InvalidOperationException("PoV dungeon layout fields changed; reset stopped.");
            partPosition = layoutParts.FieldType.GetGenericArguments()[0].GetField("Pos", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (partPosition?.FieldType != typeof(Vector3)) throw new InvalidOperationException("PoV dungeon part coordinates are unavailable.");
            AddLayoutCache(type, "cache", type, true);
            foreach (var entry in new[] { ("PassiveTree.Encounters.HallMaps.HallMapLayouts", "cache"), ("PassiveTree.Encounters.Halls.HallsRooms", "fallback") })
                if (assembly.GetType(entry.Item1) is Type owner) AddLayoutCache(owner, entry.Item2, type, false);
        }
        bound = true;
    }

    private void AddLayoutCache(Type owner, string name, Type layout, bool hallsOnly)
    {
        var field = owner.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (field?.FieldType != typeof(Dictionary<,>).MakeGenericType(typeof(string), layout) || field.GetValue(null) is not IDictionary cache)
            throw new InvalidOperationException("PoV dungeon layout cache changed: " + owner.FullName + "." + name);
        layouts.Add((cache, hallsOnly ? "raw" : owner.Name));
    }

    private void ValidateLayouts()
    {
        var count = 0;
        foreach (var source in layouts) count += source.Cache.Count;
        if (layoutFrame == Time.frameCount && layoutCount == count) return;
        layoutFrame = Time.frameCount; layoutCount = count;
        foreach (var source in layouts)
            foreach (DictionaryEntry entry in source.Cache)
            {
                // PoV caches missing optional kits as null and uses a fallback. No geometry exists here.
                var layout = entry.Value;
                if (layout == null) continue;
                var role = LayoutRole(source.Context, entry.Key as string ?? "");
                if (role == null || checkedLayouts.Contains((layout, role))) continue;
                if (layoutParts!.GetValue(layout) is not IList parts || layoutAnchors!.GetValue(layout) is not Dictionary<string, List<Vector3>> anchors)
                    throw new InvalidOperationException("PoV dungeon layout cannot be inspected.");
                foreach (var part in parts) CheckPosition((Vector3)partPosition!.GetValue(part)!, role);
                foreach (var positions in anchors.Values) foreach (var position in positions) CheckPosition(position, role);
                checkedLayouts.Add((layout, role));
            }
    }

    private static string? LayoutRole(string context, string name)
    {
        if (context == "HallsRooms")
        {
            if (name.StartsWith("Lobby:", StringComparison.Ordinal)) return "lobby";
            if (name.StartsWith("Room:", StringComparison.Ordinal) || name.StartsWith("Boss:", StringComparison.Ordinal)) return "room";
            if (name.StartsWith("Treasury:", StringComparison.Ordinal)) return "treasury";
            if (name.StartsWith("Entrance:", StringComparison.Ordinal)) return null;
            throw new InvalidOperationException("PoV Halls fallback role changed: " + name);
        }
        foreach (var role in new[] { "cell", "arena", "hall", "seam" })
            if (name == "halls-" + role || name.StartsWith("halls-" + role + "-", StringComparison.Ordinal)) return role;
        // RoomDef.Layout accepts arbitrary names, including names also used by legacy Halls.
        if (context == "HallMapLayouts") return "cell";
        foreach (var role in new[] { "lobby", "room", "treasury" })
            if (name.StartsWith("halls-" + role + "-", StringComparison.Ordinal)) return role;
        if (!name.StartsWith("halls-", StringComparison.Ordinal) || name.StartsWith("halls-kit-", StringComparison.Ordinal) ||
            name.StartsWith("halls-entrance-", StringComparison.Ordinal)) return null;
        throw new InvalidOperationException("PoV dungeon layout role is unknown: " + name);
    }

    private static void CheckPosition(Vector3 position, string role)
    {
        // Native origins and seam yaw are fixed. Leave 5m for native mob/reward offsets.
        (float X, float Back, float Front) bounds = role switch
        {
            "cell" => (35f, 125f, 115f),
            "arena" => (35f, 15f, 95f),
            "hall" => (315f, 105f, 375f),
            "seam" => (15f, 15f, 15f),
            "lobby" => (55f, 124f, 156f),
            "room" => (140f, 60f, 220f),
            "treasury" => (140f, 216f, 64f),
            _ => throw new InvalidOperationException("Unknown PoV dungeon layout role.")
        };
        if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || Math.Abs(position.y) > 55 ||
            Math.Abs(position.x) > bounds.X || position.z < -bounds.Back || position.z > bounds.Front)
            throw new InvalidOperationException("PoV " + role + " layout exceeds the verified slot envelope; reset stopped before deleting its content.");
    }

    private static void RequireReach(Type type, string name, float expected)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field?.IsLiteral != true || field.FieldType != typeof(float) || !Equals(field.GetRawConstantValue(), expected))
            throw new InvalidOperationException($"PoV dungeon protection: {type.FullName}.{name} slot geometry changed; FreshWorld reset stopped.");
    }

    private sealed class LiveRuns
    {
        private readonly IList list;
        private readonly MethodInfo getter;
        private readonly Dictionary<object, ZDO?> known = new();
        private int frame = -1;
        public LiveRuns(IList list, MethodInfo getter) { this.list = list; this.getter = getter; }
        public void Observe(Action<ZDO> observe)
        {
            // Search metadata once; invoke each known component's read-only getter at most once
            // per frame. New instances are checked immediately, even within the same frame.
            if (frame != Time.frameCount) { known.Clear(); frame = Time.frameCount; }
            for (var i = 0; i < list.Count; i++)
            {
                var instance = list[i];
                if (instance == null) continue;
                if (!known.TryGetValue(instance, out var zdo) || zdo == null || !zdo.IsValid())
                    known[instance] = zdo = getter.Invoke(instance, null) as ZDO;
                if (zdo != null) observe(zdo);
            }
        }
    }
}
