using System.Collections;
using FreshWorld.Backend;
using FreshWorld.Engine;
using FreshWorld.Runtime;
using UnityEngine;

var tests = new (string Name, Action Test)[]
{
    ("vegetation waits for loaded zone even when its root already exists", VegetationWaitsForLoad),
    ("locations wait for loaded zone even when its root already exists", LocationWaitsForLoad),
    ("location asset readiness is checked before object removal", LocationWaitsForAsset),
    ("exact vegetation and fractions reset with original metadata identities and old ghosts preserved", VegetationScope),
    ("disabled vegetation and its fractions survive without placement or terrain edits", DisabledVegetation),
    ("vegetation metadata follows registry replacement during loading", VegetationReload),
    ("multi-prefab EWD entries fail before deletion rather than duplicating uncleared variants", VegetationVariants),
    ("vegetation-only placement disables original terrain height and preserves previous hook state", VegetationWithoutTerrain),
    ("dense vegetation-only pass after ores performs no additional terrain restoration", VegetationGroups),
    ("native vegetation failure restores registry RNG ghost mode and tracked temporary objects", VegetationFailure),
    ("native location failure restores RNG ghost mode and tracked temporary objects", LocationFailure),
    ("native location failure restores loaded source template transforms and child activation", LocationTemplateFailure),
    ("named modded references remain eligible and unnamed invalid references stay excluded", NamedLocations),
    ("location clearing preserves controls and selects radius plus high dungeon objects", LocationClearScope),
    ("location footprint follows circle-sector intersections and rejects invalid extents", FootprintGeometry),
    ("location clearing crosses ungenerated sectors without deleting adjacent elevated dungeons", LocationCrossSector),
    ("resource clearing forwards reserved-object deletion policy", ResourceReservedObject),
    ("terrain paint cannot reach an unapproved neighboring sector", LocationTerrainBoundary),
    ("native asset callbacks cannot bypass the final location guard", LocationFinalGuard),
    ("planned location identity and position cannot change before deletion", LocationPlanIdentity),
    ("canceling an unloaded native operation releases its pending load", Cancellation),
    ("protection refusal prevents native object terrain and placement changes before and after loading", ProtectedGeneration),
    ("empty IDs cannot expand a native reset", EmptyIds),
    ("resource and location terrain restoration forward the invasion guard", TerrainGuardForwarding),
    ("resource and location clearing forward configured object deletion protection", ObjectGuardForwarding),
};
int failures = 0;
foreach (var test in tests)
{
    Reset();
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} native generation regression checks passed.");
return failures == 0 ? 0 : 1;

static Vector2s Zone() => new(0, 0);
static void Reset()
{
    ZoneSystem.instance = new(); ZNet.instance = new(); ZNet.World = new();
    ZDOMan.instance = new(); ZNetScene.instance = new();
    GameWorld.Objects.Clear(); GameWorld.Removed.Clear();
    GameWorld.Pokes = GameWorld.Releases = GameWorld.Recalculations = 0;
    TerrainResetter.Active = false; TerrainResetter.Restored.Clear(); TerrainResetter.LastTerrainFilter = null;
    UnityEngine.Random.state = new(77); ZNetView.FinishGhostInit(); WearNTear.RandomDamage = false; PrefabReference.AssetReads = 0;
    UnityEngine.Time.timeScale = 1;
    var zone = Zone();
    ZoneSystem.instance.Generated.Add(zone);
    ZoneSystem.instance.Roots.Add(zone, new GameObject("root") { Heightmap = new Heightmap() });
    ZoneSystem.instance.Loaded.Add(zone);
    ExpandWorldData.Vegetation.VegetationSpawning.Prefabs = new();
    ExpandWorldData.Vegetation.VegetationSpawning.Extra = new();
    ZoneSystem.instance.m_vegetation.Add(new() { m_prefab = new("rock4_copper"), m_enable = true });
    ZoneSystem.instance.m_vegetation.Add(new() { m_prefab = new("silvervein"), m_enable = true });
    AddLocation("Hildir_cave");
}
static void AddLocation(string name, bool valid = true)
{
    ZoneSystem.instance.m_locationInstances[Zone()] = new()
    {
        m_location = new() { m_prefab = new(valid, name), m_exteriorRadius = 10 },
        m_position = new(0, 0, 0), m_placed = true
    };
}
static TrackedResetVegetation Vegetation() => new(_ => { }, new() { "rock4_copper" }, new() { TerrainReset = 20 });
static TrackedRegenerateLocations Locations(string id = "Hildir_cave") => new(_ => { }, new() { id }, new());
static GuardedCoroutine Start(ITrackedOperation operation, List<Exception> errors, List<bool> finishes)
{
    return new GuardedCoroutine(Execute(), () => true, errors.Add, finishes.Add);
    IEnumerator Execute()
    {
        try
        {
            operation.Init();
            yield return operation.Execute();
        }
        finally { operation.Cleanup(); }
    }
}
static void Drain(IEnumerator runner)
{
    for (var steps = 0; steps < 100; steps++) if (!runner.MoveNext()) return;
    throw new Exception("Native operation did not finish.");
}
static void VegetationWaitsForLoad()
{
    var zones = ZoneSystem.instance; zones.Loaded.Clear();
    GameWorld.Objects[Zone()] = new() { new("rock4_copper", new()) };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Vegetation(), errors, finishes);
    Equal(true, runner.MoveNext());
    Equal(0, GameWorld.Removed.Count); Equal(0, zones.VegetationCalls); Equal(1, GameWorld.Pokes);
    zones.Loaded.Add(Zone()); Drain(runner);
    Equal(1, zones.VegetationCalls); Equal(1, GameWorld.Removed.Count);
    Success(errors, finishes);
}

static void ProtectedGeneration()
{
    foreach (var resources in new[] { true, false })
        foreach (var waitForLoad in new[] { true, false })
        {
            Reset();
            var zones = ZoneSystem.instance;
            var allowed = waitForLoad;
            if (waitForLoad) zones.Loaded.Clear();
            GameWorld.Objects[Zone()] = new() { new("rock4_copper", new()), new("dungeon", new(0, 4500, 0)) };
            var errors = new List<Exception>(); var finishes = new List<bool>();
            ITrackedOperation operation = resources
                ? new TrackedResetVegetation(_ => { }, new() { "rock4_copper" }, new() { TerrainReset = 20 }, canProcess: _ => allowed)
                : new TrackedRegenerateLocations(_ => { }, new() { "Hildir_cave" }, new(), canProcess: _ => allowed);
            using var runner = Start(operation, errors, finishes);
            if (waitForLoad)
            {
                Equal(true, runner.MoveNext());
                zones.Loaded.Add(Zone());
                allowed = false;
            }
            Drain(runner); Success(errors, finishes);
            Equal(0, GameWorld.Removed.Count); Equal(0, TerrainResetter.Restored.Count);
            Equal(0, zones.VegetationCalls); Equal(0, zones.LocationCalls);
            Equal(true, zones.m_locationInstances[Zone()].m_placed);
            Equal(0, operation.Result.ChangedZones.Count); Equal(1, operation.Result.SkippedZones.Count);
        }
}
static void LocationWaitsForLoad()
{
    var zones = ZoneSystem.instance; zones.Loaded.Clear();
    GameWorld.Objects[Zone()] = new() { new("dungeon", new()) };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Locations(), errors, finishes);
    Equal(true, runner.MoveNext());
    Equal(0, GameWorld.Removed.Count); Equal(0, zones.LocationCalls); Equal(0, zones.AssetChecks);
    Equal(true, zones.m_locationInstances[Zone()].m_placed);
    zones.Loaded.Add(Zone()); Drain(runner);
    Equal(1, zones.LocationCalls); Success(errors, finishes);
}
static void LocationWaitsForAsset()
{
    var zones = ZoneSystem.instance; zones.AssetReady = false;
    GameWorld.Objects[Zone()] = new() { new("dungeon", new()) };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Locations(), errors, finishes);
    Equal(true, runner.MoveNext()); Equal(0, GameWorld.Removed.Count); Equal(0, zones.LocationCalls);
    Equal(true, zones.m_locationInstances[Zone()].m_placed);
    zones.AssetReady = true; Drain(runner); Success(errors, finishes);
}
static void VegetationScope()
{
    var zones = ZoneSystem.instance;
    var original = zones.m_vegetation;
    var secondCopper = new ZoneSystem.ZoneVegetation { m_prefab = new("rock4_copper"), m_enable = true };
    original.Add(secondCopper);
    ExpandWorldData.Vegetation.VegetationSpawning.Extra[original[0]] = "custom data";
    ExpandWorldData.Vegetation.VegetationSpawning.Extra[secondCopper] = "custom scale and clearance";
    var oldGhost = new GameObject("existing"); var newGhost = new GameObject("copper");
    zones.Temporary.Add(oldGhost);
    ZNetScene.instance.Prefabs["rock4_copper_frac"] = new();
    GameWorld.Objects[Zone()] = new()
    {
        new("rock4_copper", new()), new("rock4_copper_frac", new()), new("silvervein", new()), new("piece", new())
    };
    zones.VegetationPlacement = (areas, objects) =>
    {
        Equal(false, ReferenceEquals(original, zones.m_vegetation));
        Sequence(new[] { original[0], secondCopper }, zones.m_vegetation);
        Sequence(new[] { "custom data", "custom scale and clearance" },
            zones.m_vegetation.Select(v => ExpandWorldData.Vegetation.VegetationSpawning.Extra[v]));
        Equal(1, areas.Count); Equal(true, TerrainResetter.Active);
        objects.Add(newGhost); UnityEngine.Random.state = new(999); ZNetView.StartGhostInit(); WearNTear.RandomDamage = true;
    };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Vegetation(), errors, finishes); Drain(runner);
    Sequence(new[] { "rock4_copper", "rock4_copper_frac" }, GameWorld.Removed.Select(zdo => zdo.Name));
    Equal(true, ReferenceEquals(original, zones.m_vegetation)); Equal(true, original[0].m_enable);
    Equal(true, original[1].m_enable); Equal(false, TerrainResetter.Active);
    Equal(new UnityEngine.Random.State(77), UnityEngine.Random.state); Equal(false, ZNetView.Ghost);
    Equal(false, WearNTear.RandomDamage);
    Equal(true, newGhost.Destroyed); Equal(false, oldGhost.Destroyed);
    Sequence(new[] { oldGhost }, zones.Temporary); Equal(1, TerrainResetter.Restored.Count);
    Success(errors, finishes);
}
static void VegetationWithoutTerrain()
{
    var zones = ZoneSystem.instance;
    zones.m_vegetation.Add(new() { m_prefab = new("Beech1"), m_enable = true });
    var original = zones.m_vegetation;
    var tree = new GameObject("Beech1");
    TerrainResetter.Active = true;
    zones.VegetationPlacement = (_, objects) =>
    {
        Sequence(new[] { "Beech1" }, zones.m_vegetation.Where(v => v.m_enable).Select(v => v.m_prefab.name));
        // The normal native ground query is used even if the caller previously had an active override.
        Equal(false, TerrainResetter.Active);
        objects.Add(tree);
    };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(new TrackedResetVegetation(_ => { }, new() { "Beech1" }, new() { TerrainReset = 0 }), errors, finishes);
    Drain(runner);
    Success(errors, finishes);
    Equal(0, TerrainResetter.Restored.Count); Equal(0, GameWorld.Recalculations);
    Equal(true, tree.Destroyed); Equal(true, TerrainResetter.Active);
    Equal(true, ReferenceEquals(original, zones.m_vegetation));
}
static void VegetationGroups()
{
    var zones = ZoneSystem.instance;
    zones.m_vegetation.Add(new() { m_prefab = new("Beech1"), m_enable = true });
    var original = zones.m_vegetation;
    ZNetScene.instance.Prefabs["rock4_copper_frac"] = new();
    GameWorld.Objects[Zone()] = new()
    {
        new("rock4_copper", new()), new("rock4_copper_frac", new()), new("Beech1", new()), new("piece", new())
    };
    var created = new List<GameObject>();
    zones.VegetationPlacement = (_, objects) =>
    {
        bool terrain = zones.m_vegetation[0].m_prefab.name == "rock4_copper";
        Sequence(new[] { terrain ? "rock4_copper" : "Beech1" },
            zones.m_vegetation.Where(v => v.m_enable).Select(v => v.m_prefab.name));
        Equal(terrain, TerrainResetter.Active);
        Equal(terrain ? 0 : 1, GameWorld.Recalculations);
        for (int i = 0; i < (terrain ? 1 : 50); i++)
        {
            var spawned = new GameObject("generated-child");
            created.Add(spawned); objects.Add(spawned);
        }
    };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using (var ores = Start(Vegetation(), errors, finishes)) Drain(ores);
    Success(errors, finishes);
    errors.Clear(); finishes.Clear();
    using (var trees = Start(new TrackedResetVegetation(_ => { }, new() { "Beech1" }, new()), errors, finishes)) Drain(trees);
    Success(errors, finishes);
    Sequence(new[] { "rock4_copper", "rock4_copper_frac", "Beech1" }, GameWorld.Removed.Select(zdo => zdo.Name));
    Equal(2, zones.VegetationCalls); Equal(1, TerrainResetter.Restored.Count); Equal(1, GameWorld.Recalculations);
    Equal(51, created.Count); Equal(true, created.All(obj => obj.Destroyed)); Equal(0, zones.Temporary.Count);
    Equal(true, ReferenceEquals(original, zones.m_vegetation)); Equal(false, TerrainResetter.Active);
}
static void VegetationFailure() => PlacementFailure(true);

static void DisabledVegetation()
{
    var zones = ZoneSystem.instance;
    zones.m_vegetation[0].m_enable = false;
    ZNetScene.instance.Prefabs["rock4_copper_frac"] = new();
    GameWorld.Objects[Zone()] = new() { new("rock4_copper", new()), new("rock4_copper_frac", new()) };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Vegetation(), errors, finishes); Drain(runner);
    Success(errors, finishes);
    Equal(0, GameWorld.Removed.Count); Equal(0, zones.VegetationCalls); Equal(0, TerrainResetter.Restored.Count);
    Equal(false, zones.m_vegetation[0].m_enable);
}

static void VegetationReload()
{
    var zones = ZoneSystem.instance; zones.Loaded.Clear();
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Vegetation(), errors, finishes);
    Equal(true, runner.MoveNext());
    var replacement = new ZoneSystem.ZoneVegetation { m_prefab = new("rock4_copper"), m_enable = true };
    var registry = new List<ZoneSystem.ZoneVegetation> { replacement };
    zones.m_vegetation = registry;
    ExpandWorldData.Vegetation.VegetationSpawning.Prefabs = new();
    ExpandWorldData.Vegetation.VegetationSpawning.Extra = new() { [replacement] = "reloaded data" };
    zones.VegetationPlacement = (_, _) => Equal("reloaded data",
        ExpandWorldData.Vegetation.VegetationSpawning.Extra[zones.m_vegetation.Single()]);
    zones.Loaded.Add(Zone()); Drain(runner); Success(errors, finishes);
    Equal(true, ReferenceEquals(registry, zones.m_vegetation)); Equal(1, zones.VegetationCalls);
}

static void VegetationVariants()
{
    foreach (var duringLoad in new[] { false, true })
    {
        Reset(); var zones = ZoneSystem.instance;
        if (duringLoad) zones.Loaded.Clear();
        GameWorld.Objects[Zone()] = new() { new("rock4_copper", new()), new("silvervein", new()) };
        var errors = new List<Exception>(); var finishes = new List<bool>();
        using var runner = Start(Vegetation(), errors, finishes);
        if (duringLoad) Equal(true, runner.MoveNext());
        var entry = zones.m_vegetation[0];
        ExpandWorldData.Vegetation.VegetationSpawning.Prefabs = new()
            { [entry] = new() { entry.m_prefab, new("silvervein") } };
        zones.Loaded.Add(Zone()); Drain(runner);
        Equal(1, errors.Count); Equal(true, errors[0].Message.Contains("multi-prefab entry 'rock4_copper'"));
        Sequence(new[] { false }, finishes);
        Equal(0, GameWorld.Removed.Count); Equal(0, zones.VegetationCalls); Equal(0, TerrainResetter.Restored.Count);
    }
}

static void LocationFailure() => PlacementFailure(false);
static void PlacementFailure(bool vegetation)
{
    var zones = ZoneSystem.instance;
    var original = zones.m_vegetation;
    var oldGhost = new GameObject("existing"); var failedGhost = new GameObject("failed");
    var failure = new InvalidOperationException("native placement hook failed");
    zones.Temporary.Add(oldGhost);
    ZNetView.StartGhostInit(); // Restoration must preserve the prior value, including true.
    Action<IList, List<GameObject>> fail = (_, objects) =>
    {
        objects.Add(failedGhost); UnityEngine.Random.state = new(999); ZNetView.FinishGhostInit(); WearNTear.RandomDamage = true; throw failure;
    };
    if (vegetation) zones.VegetationPlacement = fail; else zones.LocationPlacement = fail;
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(vegetation ? Vegetation() : Locations(), errors, finishes); Drain(runner);
    Sequence(new[] { failure }, errors); Sequence(new[] { false }, finishes);
    Equal(true, ReferenceEquals(original, zones.m_vegetation)); Equal(false, TerrainResetter.Active);
    Equal(new UnityEngine.Random.State(77), UnityEngine.Random.state); Equal(true, ZNetView.Ghost);
    Equal(false, WearNTear.RandomDamage);
    Equal(true, failedGhost.Destroyed); Equal(false, oldGhost.Destroyed); Sequence(new[] { oldGhost }, zones.Temporary);
}
static void LocationTemplateFailure()
{
    var source = new GameObject("source");
    var interior = new GameObject("interior");
    var generator = new GameObject("generator");
    source.Children.AddRange(new[] { interior.transform, generator.transform });
    source.transform.position = new(5, 6, 7); source.transform.rotation = new(19);
    interior.transform.localPosition = new(1, 2, 3); interior.transform.localRotation = new(7);
    generator.transform.localPosition = new(4, 5, 6); generator.transform.localScale = new(2, 3, 4);
    generator.SetActive(false);
    ZoneSystem.instance.m_locationInstances[Zone()].m_location.m_prefab = new(true, "Hildir_cave", source);
    ZoneSystem.instance.LocationPlacement = (_, __) =>
    {
        source.transform.position = new(); source.transform.rotation = new();
        interior.transform.localPosition = new(); interior.transform.localRotation = new(); interior.SetActive(false);
        generator.transform.localPosition = new(); generator.transform.localScale = new(); generator.SetActive(true);
        throw new InvalidOperationException("interrupted native template use");
    };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Locations(), errors, finishes); Drain(runner);
    Equal(1, errors.Count); Sequence(new[] { false }, finishes);
    Equal(new Vector3(5, 6, 7), source.transform.position); Equal(new Quaternion(19), source.transform.rotation);
    Equal(new Vector3(1, 2, 3), interior.transform.localPosition); Equal(new Quaternion(7), interior.transform.localRotation);
    Equal(new Vector3(4, 5, 6), generator.transform.localPosition); Equal(new Vector3(2, 3, 4), generator.transform.localScale);
    Equal(true, interior.activeSelf); Equal(false, generator.activeSelf); Equal(1, PrefabReference.AssetReads);
}
static void NamedLocations()
{
    var unnamed = new ZoneSystem.ZoneLocation { m_prefab = new(false, null) };
    Equal(false, NativePlacement.IsValidLocationPrefab(unnamed));
    AddLocation("Mistlands_Giant1:dark", false);
    Equal(true, NativePlacement.IsValidLocationPrefab(ZoneSystem.instance.m_locationInstances[Zone()].m_location));
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Locations("Mistlands_Giant1:dark"), errors, finishes); Drain(runner);
    Equal(1, ZoneSystem.instance.LocationCalls); Equal(0, PrefabReference.AssetReads); Success(errors, finishes);
}
static void LocationClearScope()
{
    GameWorld.Objects[Zone()] = new()
    {
        new("_ZoneCtrl", new()), new("_TerrainCompiler", new()), new("nearby", new(1, 0, 0)),
        new("dungeon", new(30, 5000, 30)), new("outside", new(20, 0, 20)), new("boundary", new(10, 0, 0))
    };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Locations(), errors, finishes); Drain(runner);
    Sequence(new[] { "nearby", "dungeon" }, GameWorld.Removed.Select(zdo => zdo.Name));
    Success(errors, finishes);
}
static void FootprintGeometry()
{
    var cross = LocationFootprint.Sectors(new(), 40);
    Equal(5, cross.Count);
    Equal(true, cross.Contains(new(1, 0))); Equal(false, cross.Contains(new(1, 1)));
    var offset = LocationFootprint.Sectors(new(31, 0, 31), 40);
    Equal(4, offset.Count); Equal(true, offset.Contains(new(1, 1)));
    Equal(1, LocationFootprint.Sectors(new(-64, 0, -64), 10).Count);
    Throws<InvalidOperationException>(() => LocationFootprint.Sectors(new(), float.NaN));
    Throws<InvalidOperationException>(() => LocationFootprint.Sectors(new(float.PositiveInfinity, 0, 0), 10));
    Throws<InvalidOperationException>(() => LocationFootprint.Sectors(new(short.MaxValue * 64, 0, 0), 40));
    Throws<InvalidOperationException>(() => LocationFootprint.Sectors(new(), float.MaxValue));
}
static void LocationCrossSector()
{
    var instance = ZoneSystem.instance.m_locationInstances[Zone()];
    var footprint = new LocationFootprint(Zone(), instance, 40);
    GameWorld.Objects[Zone()] = new() { new("center", new()), new("own_dungeon", new(30, 5000, 30)) };
    GameWorld.Objects[new(1, 0)] = new()
    {
        new("outer_wall", new(35, 0, 0)), new("outside", new(50, 0, 0)),
        new("neighbor_dungeon", new(35, 5000, 0)), new("_ZoneCtrl", new(35, 0, 0)),
        new("_TerrainCompiler", new(35, 0, 0))
    };
    Equal(false, GameWorld.IsGenerated(new(1, 0)));
    ZoneSystem.instance.LocationPlacement = (_, _) =>
        Sequence(new[] { "center", "outer_wall", "own_dungeon" }, GameWorld.Removed.Select(zdo => zdo.Name).OrderBy(name => name));
    var args = new OperationParameters { LocationFootprints = new Dictionary<Vector2s, LocationFootprint> { [Zone()] = footprint } };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(new TrackedRegenerateLocations(_ => { }, new() { "Hildir_cave" }, args), errors, finishes);
    Drain(runner); Success(errors, finishes); Equal(1, ZoneSystem.instance.LocationCalls);
}
static void ResourceReservedObject()
{
    var target = new ZDO("rock4_copper", new()); GameWorld.Objects[Zone()] = new() { target };
    var args = new OperationParameters { CanDeleteObject = _ => false };
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(new TrackedResetVegetation(_ => { }, new() { "rock4_copper" }, args), errors, finishes);
    Drain(runner); Success(errors, finishes); Equal(0, GameWorld.Removed.Count);
}
static void LocationTerrainBoundary()
{
    var location = ZoneSystem.instance.m_locationInstances[Zone()];
    location.m_position = new(0.25f, 0, 0); location.m_location.m_exteriorRadius = 32;
    ZoneSystem.instance.m_locationInstances[Zone()] = location;
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Locations(), errors, finishes); Drain(runner);
    Success(errors, finishes); Equal(true, TerrainResetter.LastTerrainFilter!(new(0, 0)));
    Equal(false, TerrainResetter.LastTerrainFilter!(new(-1, 0)));
}
static void LocationFinalGuard()
{
    GameWorld.Objects[Zone()] = new() { new("wall", new()) };
    var allowed = true;
    ZoneSystem.instance.OnAssetCheck = () => allowed = false;
    var errors = new List<Exception>(); var finishes = new List<bool>();
    var operation = new TrackedRegenerateLocations(_ => { }, new() { "Hildir_cave" }, new(), canProcess: _ => allowed);
    using var runner = Start(operation, errors, finishes); Drain(runner);
    Success(errors, finishes); Equal(0, GameWorld.Removed.Count); Equal(0, TerrainResetter.Restored.Count);
    Equal(0, ZoneSystem.instance.LocationCalls); Equal(1, operation.Result.SkippedZones.Count);
}
static void LocationPlanIdentity()
{
    var instance = ZoneSystem.instance.m_locationInstances[Zone()];
    var footprint = new LocationFootprint(Zone(), instance);
    Equal(true, footprint.Matches(instance));
    instance.m_position = new(1, 0, 0); Equal(false, footprint.Matches(instance));
    instance.m_position = new(); instance.m_location.m_exteriorRadius = 11; Equal(false, footprint.Matches(instance));
    instance.m_location = new() { m_prefab = new(true, "Hildir_cave"), m_exteriorRadius = 10 };
    Equal(false, footprint.Matches(instance));
}
static void Cancellation()
{
    ZoneSystem.instance.Loaded.Clear(); ZoneSystem.instance.Roots.Clear();
    var errors = new List<Exception>(); var finishes = new List<bool>();
    using var runner = Start(Vegetation(), errors, finishes);
    Equal(true, runner.MoveNext()); runner.Dispose();
    Equal(1, GameWorld.Releases); Equal(0, ZoneSystem.instance.VegetationCalls);
    Equal(0, errors.Count); Sequence(new[] { false }, finishes);
}
static void EmptyIds()
{
    Throws<ArgumentException>(() => new TrackedResetVegetation(_ => { }, new(), new()));
    Throws<ArgumentException>(() => new TrackedRegenerateLocations(_ => { }, new(), new()));
    Throws<ArgumentException>(() => new TrackedResetVegetation(_ => { }, new() { " " }, new()));
    Equal(0, ZoneSystem.instance.VegetationCalls); Equal(0, ZoneSystem.instance.LocationCalls);
}
static void TerrainGuardForwarding()
{
    foreach (var vegetation in new[] { true, false })
    {
        Reset();
        Func<Vector2s, bool> filter = zone => zone.x != 1;
        var args = new OperationParameters { TerrainReset = 20, CanResetTerrain = filter };
        ZoneSystem.instance.VegetationPlacement = (_, objects) => objects.Add(new GameObject("ore"));
        ITrackedOperation operation = vegetation
            ? new TrackedResetVegetation(_ => { }, new() { "rock4_copper" }, args)
            : new TrackedRegenerateLocations(_ => { }, new() { "Hildir_cave" }, args);
        var errors = new List<Exception>(); var finishes = new List<bool>();
        using var runner = Start(operation, errors, finishes); Drain(runner);
        Success(errors, finishes);
        Equal(true, TerrainResetter.LastTerrainFilter!(new(0, 0)));
        Equal(false, TerrainResetter.LastTerrainFilter!(new(1, 0)));
        Equal(vegetation, TerrainResetter.LastTerrainFilter!(new(2, 0)));
    }
}

static void ObjectGuardForwarding()
{
    foreach (var vegetation in new[] { true, false })
    {
        Reset();
        var target = new ZDO("rock4_copper", new());
        GameWorld.Objects[Zone()] = new() { target };
        var args = new OperationParameters { AlwaysProtected = new AlwaysProtectedObjects(["rock4_copper"]) };
        ITrackedOperation operation = vegetation
            ? new TrackedResetVegetation(_ => { }, new() { "rock4_copper" }, args)
            : new TrackedRegenerateLocations(_ => { }, new() { "Hildir_cave" }, args);
        var errors = new List<Exception>(); var finishes = new List<bool>();
        using var runner = Start(operation, errors, finishes); Drain(runner);
        Success(errors, finishes); Equal(false, GameWorld.Removed.Contains(target));
    }
}

static void Success(List<Exception> errors, List<bool> finishes)
{
    Equal(0, errors.Count); Sequence(new[] { true }, finishes);
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}.");
}
static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual)) throw new Exception("Unexpected sequence.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
