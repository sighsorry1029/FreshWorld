using FreshWorld.Engine;
using UnityEngine;

internal static class PovDungeonRegressions
{
    public static void Entries()
    {
        var gate = Add(100, "PoV_HallsGate", new(6400, 0, 0));
        var session = Add(101, "PoV_HallMapSession", new(6528, 0, 0));
        var pad = Add(102, "PoV_HallsPad", new(6433, 0, 0));
        var edge = Add(103, "PoV_HallsPadEdge", new(6400, 0, 33));
        var policy = new PathOfValheimProtection(false, false, true); policy.CaptureControllers();
        var absolute = new AlwaysProtectedObjects([], policy.IsProtectedObject); absolute.Capture();
        Check(!absolute.CanResetZone(new(100, 0)) && !absolute.CanResetZone(new(102, 0)), "entry/session zones");
        Check(!absolute.CanResetZone(new(101, 0)) && !absolute.CanResetZone(new(100, 1)) &&
            absolute.CanResetZone(new(101, 1)), "pad sectors should protect only their own zone");
        GameWorld.RemoveZDO(session, false, false, absolute);
        Check(session.Valid && session.Owner == 0, "session changed");
        var disabled = new PathOfValheimProtection(false, false, false);
        Check(!disabled.IsProtectedObject(gate) && !disabled.IsProtectedObject(pad) && !disabled.IsProtectedObject(edge), "disabled option");
    }

    public static void Footprints()
    {
        var map = Root(100, true, 3, new(640, 4400, 1280));
        map.SetPosition(new(12800, 4400, 0));
        var legacy = Root(101, false, 1, new(-640, 5450, 0));
        var policy = new PathOfValheimProtection(false, false); policy.CaptureControllers();
        var ground = Add(102, "stone", new(640, 0, 1280));
        var own = Add(103, "chest", new(640, 4400, 1280));
        var vanilla = Add(104, "chest", new(640, 5000, 1280));
        Check(policy.IsRetainedDungeonObject(map) && policy.IsRetainedDungeonObject(legacy), "moving roots");
        Check(policy.IsRetainedDungeonObject(own) && !policy.IsRetainedDungeonObject(ground) && !policy.IsRetainedDungeonObject(vanilla), "XYZ separation");
        var absolute = new AlwaysProtectedObjects([], policy.IsProtectedObject); absolute.Capture();
        Check(absolute.CanResetZone(new(10, 20)), "sky dungeon blocked ground");
        var plan = new GameWorld.ZoneDeletionPlan(new() { new(10, 20) }, false, false, absolute, policy.IsRetainedDungeonObject);
        plan.Capture(); plan.Delete();
        Check(!ground.Valid && !vanilla.Valid && own.Valid && own.Owner == 0 && map.Valid, "same-sector native reset damaged PoV");
    }

    public static void States()
    {
        foreach (var map in new[] { true, false })
        for (var status = 0; status <= (map ? 6 : 4); status++)
        foreach (var closed in new[] { true, false })
        {
            ZDOMan.instance = new();
            var root = Root(100, map, status, new(640, map ? 4400 : 5450, 0)); root.Bools[H("hm.closed")] = closed;
            var loot = Add(101, "loot", new(640, map ? 4401 : 5451, 0));
            var policy = new PathOfValheimProtection(false, false); policy.CaptureControllers();
            Check(policy.IsRetainedDungeonObject(root) && policy.IsRetainedDungeonObject(loot), $"state {map}/{status}/{closed}");
        }
    }

    public static void LinksAndLifetime()
    {
        var root = Root(100, true, 3, new(640, 4400, 0)); Root(101, false, 1, new(-640, 5450, 0));
        var initialLoot = Add(119, "drop", new(640,4400,0));
        var policy = new PathOfValheimProtection(false, false); policy.CaptureControllers();
        initialLoot.SetPosition(new(640,100,0)); Check(policy.IsRetainedDungeonObject(initialLoot), "initial snapshot lost moving reward");
        var names = new[] { "pt.hmap.run", "pt.hmap.live", "pt.halls.run", "pt.halls.pick" };
        for (var i = 0; i < names.Length; i++)
        {
            var child = Add(110 + i, "wood", new((200 + i) * 64, 0, 0)); child.Strings[H(names[i])] = i < 2 ? "map-100" : "1-65";
            Check(policy.IsRetainedDungeonObject(child) && !policy.IsProtectedObject(child), "outlying linked object: " + names[i]);
        }
        var loot = Add(120, "drop", new(640, 4400, 0)); Check(policy.IsRetainedDungeonObject(loot), "initial loot");
        loot.SetPosition(new(640, 100, 0)); Check(policy.IsRetainedDungeonObject(loot), "observed loot UID lost after falling");
        root.Ints[H("hm.status")] = 6; root.Bools[H("hm.closed")] = true; root.DataRevision++;
        var next = new PathOfValheimProtection(false, false); next.CaptureControllers();
        Check(next.IsRetainedDungeonObject(root), "completed controller belongs to PoV cleanup");
        ZDOMan.instance.DestroyZDO(root); Check(!root.Valid, "native cleanup blocked");
    }

    public static void RecoveryAndRelaunch()
    {
        var root = Root(100, true, 6, new(640, 4400, 0)); root.Bools[H("hm.closed")] = true;
        var policy = new PathOfValheimProtection(false, false); policy.CaptureControllers();
        var grave = Add(102, "Player_tombstone", new(900, 4400, 300));
        Check(policy.IsRetainedDungeonObject(grave), "closed-run grave");
        root.Ints[H("hm.status")] = 2; root.Strings[H("hm.run")] = "new-map"; root.DataRevision++;
        var linked = Add(103, "chest", new(6400, 4400, 0)); linked.Strings[H("pt.hmap.live")] = "new-map";
        Check(policy.IsRetainedDungeonObject(linked), "same-frame relaunch ID");
        var parent = Add(104, "spawner", new(6400, 0, 0)); parent.Spawned = linked.m_uid;
        GameWorld.RemoveZDO(parent, false, false, null, zdo => !policy.IsRetainedDungeonObject(zdo));
        Check(!parent.Valid && linked.Valid && linked.Owner == 0, "recursive retained child");
    }

    public static void LateLiveRuns()
    {
        BepInEx.Bootstrap.Chainloader.PluginInfos["treextr.pathofvalheiman"] = new();
        var policy = new PathOfValheimProtection(false, false); policy.CaptureControllers();
        var child = Add(104, "loot", new(640, 4400, 0)); Check(!policy.IsRetainedDungeonObject(child), "empty world");
        var root = Root(100, true, 1, new(640, 4400, 0));
        PassiveTree.Encounters.HallMaps.HallMapRun.Runs.Add(new(root));
        Check(policy.IsRetainedDungeonObject(child), "new same-frame controller");
        var calls = PassiveTree.Encounters.HallMaps.HallMapRun.Reads;
        for (var i = 0; i < 100; i++) Check(policy.IsRetainedDungeonObject(child), "stable registry");
        Check(PassiveTree.Encounters.HallMaps.HallMapRun.Reads == calls, "repeated getters");
        PassiveTree.Encounters.HallMaps.HallMapRun.Runs.Clear();
        var replacement = Root(101, true, 2, new(-640, 4400, 0)); PassiveTree.Encounters.HallMaps.HallMapRun.Runs.Add(new(replacement));
        Check(policy.IsRetainedDungeonObject(Add(105, "loot", new(-640, 4400, 0))), "same-count replacement");
    }

    public static void InvalidData()
    {
        foreach (var field in new[] { "schema", "slot", "status" })
        {
            ZDOMan.instance = new();
            var root = Root(100, true, 3, new(640, 4400, 0));
            if (field == "schema") root.Ints[H("hm.v")] = 2;
            if (field == "slot") root.Vectors.Clear();
            if (field == "status") root.Ints[H("hm.status")] = 9;
            try { new PathOfValheimProtection(false, false).CaptureControllers(); throw new Exception("accepted " + field); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("PoV_HallMapRun"), "missing diagnostic"); }
            Check(root.Valid && root.Owner == 0 && ZDOMan.instance.Destroyed.Count == 0, "failure mutated world");
        }
    }

    public static void SnapshotCost()
    {
        Root(100, true, 6, new(640, 4400, 0));
        for (var i = 0; i < 1000; i++) Add(1000 + i, "tree", new(640, 0, 0));
        var policy = new PathOfValheimProtection(false, false); policy.CaptureControllers();
        var absolute = new AlwaysProtectedObjects([], policy.IsProtectedObject);
        var reads = ZDOMan.instance.SectorReads; absolute.Capture(policy.IsProtectedSnapshotObject);
        Check(ZDOMan.instance.SectorReads == reads, "repeated sector scans");
        Check(absolute.CanResetZone(new(10, 0)), "ground unexpectedly protected");
    }

    internal static ZDO Root(long id, bool map, int status, Vector3 slot)
    {
        var root = Add(id, map ? "PoV_HallMapRun" : "PoV_HallsRun", slot);
        var prefix = map ? "hm." : "h.";
        root.Ints[H(prefix + "v")] = 1; root.Ints[H(prefix + "status")] = status; root.Vectors[H(prefix + "slot")] = slot;
        if (map) root.Strings[H("hm.run")] = "map-" + id;
        return root;
    }
    private static ZDO Add(long id, string prefab, Vector3 position) { var zdo = new ZDO(id, prefab, position); ZDOMan.instance.Add(zdo); return zdo; }
    private static int H(string key) => key.GetStableHashCode();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
namespace PassiveTree.Encounters.HallMaps
{
    internal static class HallMapLayouts { private static readonly Dictionary<string, PassiveTree.Encounters.Layout> cache = new(); }
    public sealed class HallMapRun(ZDO zdo)
    {
        public const float ReachX = 320f, ReachZBack = 130f, ReachZFront = 360f;
        public static readonly List<HallMapRun> Runs = new();
        public static int Reads;
        internal ZDO Z { get { Reads++; return zdo; } }
    }
}
namespace PassiveTree.Encounters.Halls
{
    internal static class HallsRooms { private static readonly Dictionary<string, PassiveTree.Encounters.Layout> fallback = new(); }
    public sealed class HallsRun(ZDO zdo)
    {
        public static readonly List<HallsRun> Runs = new();
        internal ZDO Z => zdo;
    }
}

namespace PassiveTree.Encounters
{
    public sealed class Layout
    {
        private static readonly Dictionary<string, Layout> cache = new();
        public readonly List<Part> Parts = new();
        public readonly Dictionary<string, List<Vector3>> Anchors = new();
        public sealed class Part { public Vector3 Pos; }
    }
}
