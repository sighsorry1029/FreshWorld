using System.Globalization;
using FreshWorld.Engine;
using UnityEngine;

internal static class DungeonScopeRegressions
{
    private static int H(string name) => name.GetStableHashCode();
    private static ZDO Add(long id, string prefab, Vector3 pos)
    { var zdo = new ZDO(id, prefab, pos); ZDOMan.instance.Add(zdo); return zdo; }
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } catch (DungeonResetBlockedException) { return; } throw new Exception("unsafe reset accepted"); }

    private static (OdinsHollowDungeons Policy, ZDO Root, ZDO Chest) Setup(float yaw = 0)
    {
        BepInEx.Bootstrap.Chainloader.PluginInfos["org.bepinex.plugins.odinshollow"] = new();
        var prefab = new GameObject(); prefab.AddComponent(new OdinsHollow.DungeonGen.OHDungeon());
        var definition = new ZoneSystem.ZoneLocation { m_prefab = new() { name = "OdinsHollowDungeon", Asset = prefab } };
        ZoneSystem.instance.AddLocation(definition);
        ZoneSystem.instance.m_locationInstances[new(0, 0)] = new() { m_placed = true, m_location = definition };
        ZoneSystem.instance.AddGenerated(new(0, 0)); ZoneSystem.instance.AddGenerated(new(1, 0));
        var root = Add(10, "LocationProxy", new()); root.Rotation = Quaternion.Euler(0, yaw, 0);
        root.Ints[H("location")] = H("OdinsHollowDungeon"); root.Ints[H("OH_DG_Id")] = 500;
        root.Strings[H("OH_DG_Layout")] = "OHDG1;1|room;90;2025;0;0;0;0;1";
        var chest = Add(11, "chest", root.Rotation * new Vector3(90, 2025, 0)); chest.Ints[H("OH_DG_Id")] = 500;
        var policy = new OdinsHollowDungeons(); var init = policy.Initialize(() => {});
        while (init.MoveNext()) { }
        policy.Capture(); return (policy, root, chest);
    }
    private static OperationParameters Args(OdinsHollowDungeons policy, Func<Vector2s, bool>? allowed = null) => new()
    {
        RetainObject = policy.IsRetainedObject,
        PrepareDungeonReset = anchors => policy.Prepare(anchors, allowed ?? (_ => true), _ => false)
    };

    public static void OhInvalidRegistration()
    {
        var registry = ZoneSystem.instance.m_locations;
        registry.Add(new() { m_prefab = new() { IsValid = false } });
        registry.Add(null!);
        var absent = new OdinsHollowDungeons();
        var init = absent.Initialize(() => {});
        while (init.MoveNext()) { }
        Check(!absent.IsAvailable, "unrelated entries activated OH");

        var (policy, _, chest) = Setup();
        Check(policy.IsAvailable && policy.IsRetainedObject(chest), "unrelated entries hid a valid OH entrance");
    }

    public static void OhInvalidPlacedLocation()
    {
        var (policy, root, chest) = Setup();
        var neighbor = new Vector2s(1, 0);
        ZoneSystem.instance.m_locationInstances[neighbor] = new()
        {
            m_placed = true, m_location = new() { m_prefab = new() { IsValid = false } }
        };
        var ground = Add(12, "stone", new(90, 0, 0));
        new ProbeReset(Args(policy)).Run(neighbor);
        Check(!ground.Valid && root.Valid && chest.Valid && chest.Owner == 0, "neighbor reset changed the OH interior");
    }

    public static void OhUnreadableEntrance()
    {
        BepInEx.Bootstrap.Chainloader.PluginInfos["org.bepinex.plugins.odinshollow"] = new();
        ZoneSystem.instance.m_locations.Add(new() { m_prefab = new() { name = "OdinsHollowDungeon", IsValid = false } });
        var init = new OdinsHollowDungeons().Initialize(() => {});
        try { while (init.MoveNext()) { } }
        catch (InvalidOperationException error) when (error.Message.Contains("readable registered location prefab")) { return; }
        throw new Exception("a known OH entrance with an unreadable prefab was skipped");
    }

    public static void OhMissingOwner()
    {
        var world = ZoneSystem.instance;
        Check(!OdinsHollowDungeons.IsInstalled, "unexpected plugin");
        var chest = Add(10, "chest", new()); chest.Ints[H("OH_DG_Id")] = 500;
        var policy = new OdinsHollowDungeons();
        Check(OdinsHollowDungeons.HasSavedData(chest), "missed tagged content");
        policy.Capture();
        Check(policy.IsAvailable && policy.IsRetainedObject(chest), "orphan chest lost its protection");
        var definition = new ZoneSystem.ZoneLocation { m_prefab = new() { name = "OdinsHollowDungeon" } };
        world.AddLocation(definition);
        OdinsHollowDungeons.CheckMissingOwner(world);
        world.m_locationInstances[new(0, 0)] = new() { m_placed = true, m_location = definition };
        Reject(() => OdinsHollowDungeons.CheckMissingOwner(world));
        var root = Add(11, "LocationProxy", new()); root.Ints[H("location")] = H("OdinsHollowDungeon");
        Check(OdinsHollowDungeons.HasSavedData(root), "missed saved native entrance");
        Reject(() => policy.Capture());
        Check(chest.Valid && root.Valid && ZDOMan.instance.Destroyed.Count == 0, "missing mod changed saved data");
    }

    public static void OhReset()
    {
        var (policy, root, chest) = Setup();
        var startDrop = Add(12, "drop", new(0, 2025, 0));
        var ground = Add(13, "player_chest", new(90, 0, 0)); ground.Strings[H("inventory")] = "valuable items";
        var sky = Add(14, "other_dungeon", new(90, 5000, 0));
        ZDOMan.instance.DeferDeletion = true;
        new ProbeReset(Args(policy)).Run(new(0, 0));
        Check(!root.Valid && !chest.Valid && !startDrop.Valid, "old dungeon content survived");
        Check(ground.Valid && ground.Strings[H("inventory")] == "valuable items" && sky.Valid, "foreign ground/sky deleted");
        Check(GameWorld.IsGenerated(new(1, 0)) && !GameWorld.IsGenerated(new(0, 0)), "neighbor generation released");
        Check(!ZoneSystem.instance.m_locationInstances[new(0, 0)].m_placed, "anchor was not released");
    }
    public static void OhRetain()
    {
        var (policy, root, chest) = Setup();
        var pack = Add(12, "mob", new(256, 2025, 0));
        chest.Strings[H("OH_Pack")] = "1:12"; policy.Capture();
        var ground = Add(13, "stone", new(90, 0, 0));
        new ProbeReset(Args(policy)).Run(new(1, 0));
        Check(root.Valid && chest.Valid && !ground.Valid, "foreign OH interior lost");
        ZoneSystem.instance.AddGenerated(new(4, 0));
        new ProbeReset(Args(policy)).Run(new(4, 0));
        Check(pack.Valid && pack.Owner == 0, "reindex lost outlying pack link");
        Check(ZoneSystem.instance.m_locationInstances[new(0, 0)].m_placed, "foreign OH placement changed");
    }
    public static void OhRotation()
    {
        var (policy, _, _) = Setup(45);
        var center = Quaternion.Euler(0,45,0) * new Vector3(90,2025,0);
        // A point inside the rotated room's AABB corner but outside its actual OBB.
        var corner = Add(20,"foreign",center+new Vector3(19,0,19));
        var inside = Add(21,"drop",center);
        Check(!policy.IsRetainedObject(corner) && policy.IsRetainedObject(inside), "AABB treated as ownership");
    }
    public static void OhPovOverlap()
    {
        var (hollow, entrance, chest) = Setup();
        BepInEx.Bootstrap.Chainloader.PluginInfos["treextr.pathofvalheiman"] = new();
        var map = PovDungeonRegressions.Root(100, true, 3, new(640, 4400, 0));
        chest.Strings[H("pt.hmap.run")] = "map-100";
        var pov = new PathOfValheimProtection(false, false);
        pov.CaptureControllers();
        try { hollow.Prepare(new() { new(0, 0) }, _ => true, pov.IsRetainedDungeonObject); }
        catch (DungeonResetBlockedException)
        {
            Check(entrance.Valid && chest.Valid && map.Valid && chest.Owner == 0 &&
                ZDOMan.instance.Destroyed.Count == 0 && GameWorld.IsGenerated(new(0, 0)), "overlap changed ownership or placement");
            return;
        }
        throw new Exception("OH selected PoV-linked content for deletion");
    }
    public static void OhBlocked()
    {
        var (policy, root, chest) = Setup();
        Reject(() => new ProbeReset(Args(policy, zone => zone != new Vector2s(1,0))).Run(new(0,0)));
        Check(root.Valid && chest.Valid && root.Owner == 0 && GameWorld.IsGenerated(new(0,0)), "blocked reset mutated");
        root.Strings[H("OH_DG_Layout")] = "OHDG1;1|missing;90;2025;0;0;0;0;1";
        Reject(() => policy.Capture());
        Check(root.Valid && chest.Valid && ZDOMan.instance.Destroyed.Count == 0, "missing room deleted content");
    }
    public static void OhResidue()
    {
        var (policy, root, chest) = Setup();
        ZDOMan.instance.DeferDeletion = true;
        ZDOMan.instance.AfterDestroy = zdo =>
        {
            if (zdo != chest) return;
            var late = Add(30,"new_chest",new(90,2025,0)); late.Ints[H("OH_DG_Id")] = 500;
        };
        Reject(() => new ProbeReset(Args(policy)).Run(new(0,0)));
        Check(GameWorld.IsGenerated(new(0,0)) && ZoneSystem.instance.m_locationInstances[new(0,0)].m_placed, "residue enabled regeneration");
    }
    public static void LateRetention()
    {
        var first=Add(1,"stone",new()); var next=Add(2,"chest",new(1,0,0));
        var retained=false; ZDOMan.instance.AfterDestroy = _ => retained=true;
        var plan=new GameWorld.ZoneDeletionPlan(new(){new(0,0)},false,false,null,zdo=>retained&&zdo==next);
        plan.Capture(); Reject(plan.Delete);
        Check(next.Valid && next.Owner==0,"late retention ignored");
    }
    public static void StrongProtection()
    {
        var player=Add(1,"Player",new(0,4400,0));
        var plan=new GameWorld.ZoneDeletionPlan(new(){new(0,0)},false,false,null,_=>true);
        Reject(plan.Capture); Check(player.Owner==0 && player.Valid,"retention bypassed player protection");
    }
    public static void SingleExclusion()
    {
        var denied=Add(1,"stone",new()); ZoneSystem.instance.AddGenerated(new(0,0));
        Reject(()=>new ProbeReset(new(){RetainObject=_=>false,CanDeleteObject=_=>false}).Run(new(0,0)));
        Check(denied.Valid && denied.Owner==0 && GameWorld.IsGenerated(new(0,0)),"single-zone predicate ignored");
    }
    public static void Release()
    {
        var zone=new Vector2s(0,0); GameWorld.PokeZone(zone);
        var controller=Add(1,"PoV_HallMapRun",new(0,4400,0)); var view=new ZNetView(controller); ZNetScene.instance.Add(controller,view);
        GameWorld.ReleaseZone(zone,zdo=>zdo==controller);
        Check(ZoneSystem.instance.HasRoot(zone) && !view.Reset && !view.gameObject.Destroyed,"retained scene unloaded");
        var pendingZone=new Vector2s(1,0); GameWorld.PokeZone(pendingZone);
        var proxy=Add(2,"LocationProxy",new(64,0,0)); ZoneSystem.instance.SetLoadingInZone(proxy);
        GameWorld.ReleaseZone(pendingZone,_=>false);
        var late=Add(3,"PoV_HallMapRun",new(64,4400,0)); var lateView=new ZNetView(late); ZNetScene.instance.Add(late,lateView);
        ZoneSystem.instance.UnsetLoadingInZone(proxy); GameWorld.ProcessDeferredReleases();
        Check(ZoneSystem.instance.HasRoot(pendingZone)&&!lateView.Reset,"deferred load lost policy");
    }
    public static void PovLayout()
    {
        BepInEx.Bootstrap.Chainloader.PluginInfos["treextr.pathofvalheiman"]=new();
        var field=typeof(PassiveTree.Encounters.Layout).GetField("cache",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        var cache=(Dictionary<string,PassiveTree.Encounters.Layout>)field.GetValue(null)!;
        foreach (var invalid in new[] { new Vector3(0,300,0), new Vector3(1000,0,0) })
        {
            var layout=new PassiveTree.Encounters.Layout(); layout.Parts.Add(new(){Pos=invalid}); cache["halls-cell-custom"]=layout;
            try { Reject(()=>new PathOfValheimProtection(false,false).CaptureControllers()); }
            finally { cache.Clear(); }
        }
        cache["halls-kit-missing"]=null!;
        try { new PathOfValheimProtection(false,false).CaptureControllers(); }
        finally { cache.Clear(); }
        var shared=new PassiveTree.Encounters.Layout(); shared.Parts.Add(new(){Pos=new(100,0,0)});
        var mapField=typeof(PassiveTree.Encounters.HallMaps.HallMapLayouts).GetField("cache",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        var maps=(Dictionary<string,PassiveTree.Encounters.Layout>)mapField.GetValue(null)!;
        cache["halls-room-shared"]=shared; maps["halls-room-shared"]=shared;
        try { Reject(()=>new PathOfValheimProtection(false,false).CaptureControllers()); }
        finally { cache.Clear(); maps.Clear(); }
        Check(ZDOMan.instance.Destroyed.Count==0,"bad layout mutated world");
    }
}

namespace OdinsHollow.DungeonGen
{
    internal struct Obb { public Vector3 Center, HalfExtents; public Quaternion Rotation; }
    internal sealed class RoomTemplate { public Obb Bounds=new(){HalfExtents=new(20,10,10),Rotation=Quaternion.identity}; }
    internal sealed class PlacedRoom { public RoomTemplate Room=new(); public Vector3 Position; public Quaternion Rotation; }
    internal static class DungeonManager
    {
        internal static bool TryDeserialize(string text,out int seed,out List<PlacedRoom> rooms)
        {
            seed=1; rooms=new();
            foreach(var line in text.Split('|').Skip(1))
            {
                var p=line.Split(';'); if(p.Length!=8)return false; if(p[0]=="missing")continue;
                float F(int i)=>float.Parse(p[i],CultureInfo.InvariantCulture);
                rooms.Add(new(){Position=new(F(1),F(2),F(3)),Rotation=new(F(4),F(5),F(6),F(7))});
            }
            return true;
        }
    }
    internal sealed class OHDungeon
    {
        private Transform FindStartConnector()=>new();
        private Obb? StartRoomBounds(Transform start)=>new Obb{Center=new(0,2025,0),HalfExtents=new(12,10,12),Rotation=Quaternion.identity};
    }
}
