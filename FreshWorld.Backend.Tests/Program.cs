using System.Diagnostics;
using FreshWorld.Backend;
using FreshWorld.Configuration;
using FreshWorld.Runtime;
using FreshWorld.Engine;

var tests = new (string Name, Action Body)[]
{
    ("retained scope excludes newly skipped zones", RetainedScope),
    ("disabling zone reset supplements all generated zones", WithoutZoneReset),
    ("occupied zones remain intact at SafeZones zero and are eligible on the next run", OccupiedZoneAndNextRun),
    ("the initial player snapshot uses positions after the world save finishes", PostSavePlayerSnapshot),
    ("both resource passes and locations retain player protection after departure with zones disabled", PlayerSnapshotSupplements),
    ("player movement after a zone yield protects the next target", MovementAfterZoneYield),
    ("a ready player arriving during a vegetation load retry skips mutation and releases the load", PlayerArrivalDuringVegetationLoad),
    ("a ready player arriving during a location load retry skips mutation and releases the load", PlayerArrivalDuringLocationLoad),
    ("a base-filtered resource stage still records players for the later location stage", BaseFilteredStageSnapshot),
    ("skipped targets are neither completed, changed nor failed in every tracking wrapper", SkippedOperationResults),
    ("each supplemental operation is created after the prior phase completes", SequentialStages),
    ("location safe zone zero bypasses protection independently of zone and resource settings", LocationProtection),
    ("maintenance always saves before restoration and never requests another save after", SaveBeforeOnly),
    ("terrain vegetation runs before vegetation-only with distinct terrain policies", VegetationGroups),
    ("overlapping vegetation IDs regenerate once with terrain group priority", VegetationOverlap),
    ("an empty terrain group leaves vegetation-only resets independent", EmptyTerrainGroup),
    ("empty vegetation groups skip without expanding scope or warning", EmptyVegetationGroups),
    ("unknown terrain IDs do not prevent valid vegetation-only reset", UnknownTerrainGroup),
    ("unknown vegetation-only IDs do not prevent valid terrain reset", UnknownVegetationGroup),
    ("both missing vegetation groups skip without expanding to the registry", BothVegetationGroupsMissing),
    ("zero terrain radius disables terrain policy in both vegetation passes", ZeroTerrainRadius),
    ("terrain refresh gets a complete frame before vegetation-only generation", TerrainRefreshBoundary),
    ("cancellation during the terrain refresh boundary never starts later stages", CancelTerrainRefreshBoundary),
    ("both vegetation groups use retained candidates and current protection", VegetationGroupProtection),
    ("the resource switch and scheduled vegetation selection control both groups", VegetationGroupSwitch),
    ("unknown IDs skip stages and empty vegetation never expands to all", MissingIds),
    ("zone failure aborts later stages and finalizes partial mutation once", FailedZone),
    ("startup failures propagate and abort later stages", FailedStart),
    ("location identity is revalidated immediately before resetting", ChangedLocation),
    ("vegetation exception restores shared settings and releases its load", VegetationFailure),
    ("location generation failure removes only its new temporary objects", LocationGhostFailure),
    ("canceling a nested load releases it without starting another stage", CancelLoad),
    ("canceling a partial zone reset finalizes its borders once", CancelZones),
    ("cleanup attempts every pending release even after a release error", CleanupAll),
    ("cancel cleanup failure still attempts terrain refresh", CleanupRefresh),
    ("candidate restriction is applied before upstream filtering", CandidateRestriction),
    ("save errors abort before mutation and remove event listeners", SaveFailure),
    ("a suppressed save request aborts before mutation", SaveDidNotStart),
    ("canceling a save removes event listeners", CancelSave),
    ("paused maintenance does not mutate another zone", PauseZones),
    ("pausing after the final zone delays terrain border finalization", PauseBeforeFinalization),
    ("paused save time does not consume the timeout", PauseSaveTimeout),
    ("gate refuses overlapping work and releases idempotently", GateExclusion)
};

var failures = 0;
foreach (var (name, body) in tests)
{
    Fake.Reset();
    ZoneSystem.instance.m_vegetation.Add(new("copper"));
    try { body(); Equal(0, Fake.UnexpectedErrors.Count); System.Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; System.Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
System.Console.WriteLine($"{tests.Length - failures}/{tests.Length} backend regression tests passed.");
return failures == 0 ? 0 : 1;

static void RetainedScope()
{
    SetSkippedScenario();
    var run = Run(new());
    True(run.Success);
    Sequence(["vegetation.change:0", "locations.change:0"], Fake.Calls.Where(IsSupplementChange));
    True(!Fake.Calls.Contains("zones.change:1"));
}

static void WithoutZoneReset()
{
    Fake.AddZone(0, marker: true);
    Fake.AddZone(1);
    var run = Run(new() { ZonesEnabled = false });
    True(run.Success);
    True(!Fake.Calls.Contains("zones.create"));
    Sequence(["vegetation.change:0", "vegetation.change:1", "locations.change:0", "locations.change:1"],
        Fake.Calls.Where(IsSupplementChange));
}

static void OccupiedZoneAndNextRun()
{
    Fake.AddZone(0); Fake.AddZone(1);
    Fake.PlayerZones.Add(new(0, 0));
    // Departure after planning must not expose the initially occupied zone during this run.
    Fake.OnStart = kind => { if (kind == "zones") Fake.PlayerZones.Clear(); };
    var options = new RunOptions { ZoneSafeZones = 0 };
    True(Run(options).Success);
    Sequence(["zones.change:1"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    True(ZoneSystem.instance.m_generatedZones.Contains(new(0, 0)));
    // Player-only skips must not expand the existing base-marker retained scope.
    True(!Fake.Calls.Any(IsSupplementChange));

    Fake.Calls.Clear();
    True(Run(options).Success);
    Sequence(["zones.change:0"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    True(!ZoneSystem.instance.m_generatedZones.Contains(new(0, 0)));
}

static void PostSavePlayerSnapshot()
{
    Fake.AddZone(0); Fake.AddZone(1);
    Fake.PlayerZones.Add(new(0, 0));
    Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZoneSafeZones = 0, VegetationEnabled = false, LocationsEnabled = false },
        completed, advancePastSave: false);
    True(runner.MoveNext());
    True(ZNet.instance.Saving && !Fake.Calls.Contains("zones.create"));
    Fake.PlayerZones.Clear();
    Fake.PlayerZones.Add(new(1, 0));
    ZNet.instance.Saving = false;
    Drain(runner);
    Sequence([true], completed);
    Sequence(["zones.change:0"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    True(ZoneSystem.instance.m_generatedZones.Contains(new(1, 0)));
}

static void PlayerSnapshotSupplements()
{
    Fake.AddZone(0); Fake.AddZone(1);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    Fake.PlayerZones.Add(new(0, 0));
    Fake.OnStart = kind => { if (kind == "vegetation") Fake.PlayerZones.Clear(); };
    True(Run(new() { ZonesEnabled = false, VegetationIds = ["raspberry"] }).Success);
    Equal(2, Fake.VegetationPasses.Count);
    True(Fake.VegetationPasses.All(pass => pass.Arguments.SafeZones == 0 && pass.ChangedZones.SequenceEqual([1])));
    Sequence(["locations.change:1"], Fake.Calls.Where(call => call.StartsWith("locations.change:")));
    True(!Fake.Calls.Contains("zones.create"));
}

static void MovementAfterZoneYield()
{
    Fake.AddZone(0); Fake.AddZone(1);
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZoneSafeZones = 0, VegetationEnabled = false, LocationsEnabled = false }, completed);
    True(runner.MoveNext());
    Sequence(["zones.change:0"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    Fake.PlayerZones.Add(new(1, 0));
    Drain(runner);
    Sequence([true], completed);
    Sequence(["zones.change:0"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    True(ZoneSystem.instance.m_generatedZones.Contains(new(1, 0)));
}

static void PlayerArrivalDuringVegetationLoad() => PlayerArrivalDuringLoad(vegetation: true);
static void PlayerArrivalDuringLocationLoad() => PlayerArrivalDuringLoad(vegetation: false);
static void PlayerArrivalDuringLoad(bool vegetation)
{
    Fake.AddZone(0);
    Fake.LoadOnPoke = false;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new()
    {
        ZonesEnabled = false, VegetationEnabled = vegetation, LocationsEnabled = !vegetation
    }, completed);
    True(runner.MoveNext());
    Equal(1, GameWorld.Pending.Count);
    True(!Fake.Calls.Any(IsSupplementChange));
    // The target is ready on the next retry, but now has a player: readiness cannot bypass protection.
    ZoneSystem.instance.Loaded.Add(new(0, 0));
    Fake.PlayerZones.Add(new(0, 0));
    Drain(runner);
    Sequence([true], completed);
    True(!Fake.Calls.Any(IsSupplementChange));
    Equal(0, GameWorld.Pending.Count);
    Equal(1, Fake.LoadReleases);
    True(ZoneSystem.instance.m_generatedZones.Contains(new(0, 0)));
}

static void BaseFilteredStageSnapshot()
{
    Fake.AddZone(0, marker: true);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    var starts = 0;
    Fake.OnStart = kind =>
    {
        if (kind == "vegetation" && ++starts == 2) Fake.PlayerZones.Clear();
    };
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new()
    {
        ZonesEnabled = false, VegetationIds = ["raspberry"], VegetationSafeZones = 1, LocationSafeZones = 0
    }, completed);
    AdvanceThroughTerrainPass(runner);
    // Arrival happens between the resource groups; neither pass executes this base-filtered target.
    Fake.PlayerZones.Add(new(0, 0));
    True(runner.MoveNext());
    Equal(1, Fake.VegetationPasses.Count);
    Drain(runner);
    Sequence([true], completed);
    Equal(2, starts);
    Equal(2, Fake.VegetationPasses.Count);
    True(Fake.VegetationPasses.All(pass => pass.ChangedZones.Count == 0));
    Equal(0, Fake.PlayerZones.Count);
    True(Fake.Calls.Contains("locations.start") && !Fake.Calls.Any(IsSupplementChange));
}

static void SkippedOperationResults()
{
    Fake.AddZone(0);
    using var lease = MaintenanceGate.Acquire();
    var factories = new Func<ITrackedOperation>[]
    {
        () => new TrackedResetZones(_ => { }, Parameters(), canProcess: _ => false),
        () => new TrackedResetVegetation(_ => { }, new(["copper"]), Parameters(), canProcess: _ => false),
        () => new TrackedRegenerateLocations(_ => { }, new(["cave"]), Parameters(), canProcess: _ => false)
    };
    foreach (var create in factories)
    {
        var operation = create();
        System.Collections.IEnumerator Execute()
        {
            operation.Executable.Init();
            yield return operation.Executable.Execute();
        }
        var completed = new List<bool>();
        using var runner = new GuardedCoroutine(Execute(), () => true, Fake.UnexpectedErrors.Add, completed.Add);
        Drain(runner);
        operation.Cleanup();
        Sequence([true], completed);
        Sequence([new Vector2s(0, 0)], operation.Result.SelectedZones);
        Sequence([new Vector2s(0, 0)], operation.Result.SkippedZones);
        Equal(0, operation.Result.CompletedZones.Count);
        Equal(0, operation.Result.ChangedZones.Count);
        Equal(0, operation.Result.FailedZones.Count);
        Equal(0, operation.Result.FailedCount);
        True(operation.Result.Finished);
    }
    True(!Fake.Calls.Any(call => call.StartsWith("zones.change:") || IsSupplementChange(call)));
}

static void SetSkippedScenario()
{
    Fake.AddZone(0, marker: true);
    Fake.AddZone(1);
    // A marker appears after initial target filtering. This skipped zone was never in initial protection.
    Fake.OnStart = kind => { if (kind == "zones") Fake.MarkerZones.Add(new(1, 0)); };
}

static bool IsSupplementChange(string value) => value.StartsWith("vegetation.change:") || value.StartsWith("locations.change:");

static void SequentialStages()
{
    Fake.AddZone(0, marker: true);
    Fake.AddZone(1);
    True(Run(new()).Success);
    Before("zones.end", "vegetation.create");
    Before("vegetation.end", "locations.create");
    Sequence(["vegetation.change:0", "locations.change:0"], Fake.Calls.Where(IsSupplementChange));
    True(!ZoneSystem.instance.m_generatedZones.Contains(new(1, 0)));
}

static void LocationProtection()
{
    Fake.AddZone(0, marker: true);
    var options = new RunOptions { ZonesEnabled = false, VegetationEnabled = false, LocationSafeZones = 3 };
    True(Run(options).Success);
    Equal(3, Fake.Arguments["locations"].SafeZones);
    True(!Fake.Calls.Contains("locations.change:0"));
    options.LocationSafeZones = 0;
    var unprotected = Run(options);
    True(unprotected.Success);
    True(unprotected.Warnings.Any(message => message.Contains("LocationSafeZones=0")));
    Equal(0, Fake.Arguments["locations"].SafeZones);
    True(Fake.Calls.Contains("locations.change:0"));
}

static void SaveBeforeOnly()
{
    Fake.AddZone(0, marker: true);
    Fake.AddZone(1);
    True(Run(new()).Success);
    Equal(1, Fake.Calls.Count(call => call == "save.start"));
    Before("save.start", "zones.create");
    Before("zones.end", "vegetation.create");
    Before("vegetation.end", "locations.create");
    True(Fake.Calls.Contains("locations.end"));
}

static void MissingIds()
{
    Fake.AddZone(0);
    var run = Run(new() { ZonesEnabled = false, TerrainVegetationIds = [], VegetationIds = ["not_installed"], LocationIds = ["missing_dungeon"] });
    True(run.Success);
    Equal(4, run.Warnings.Count);
    True(!Fake.Calls.Contains("vegetation.create") && !Fake.Calls.Contains("locations.create"));
    Throws<ArgumentException>(() => new TrackedResetVegetation(_ => { }, new(), Parameters()));
    True(!Fake.Calls.Contains("vegetation.create"));
}

static void VegetationGroups()
{
    Fake.AddZone(0, marker: true);
    Fake.AddZone(1);
    ZoneSystem.instance.m_vegetation.Add(new("silver"));
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    True(Run(new()
    {
        TerrainVegetationIds = ["copper", "silver"], VegetationIds = ["raspberry"], VegetationTerrainRadius = 17
    }).Success);
    Equal(2, Fake.VegetationPasses.Count);
    Sequence(["copper", "silver"], Fake.VegetationPasses[0].Ids);
    Sequence(["raspberry"], Fake.VegetationPasses[1].Ids);
    Equal(17f, Fake.VegetationPasses[0].Arguments.TerrainReset);
    Equal(0f, Fake.VegetationPasses[1].Arguments.TerrainReset);
    Before("zones.end", "vegetation.ids:copper,silver");
    Before("vegetation.end", "vegetation.ids:raspberry");
    True(Fake.Calls.LastIndexOf("vegetation.end") < Fake.Calls.IndexOf("locations.create"));
    Sequence([0], Fake.VegetationPasses[0].ChangedZones);
    Sequence([0], Fake.VegetationPasses[1].ChangedZones);
    Equal(1, Fake.Calls.Count(call => call == "save.start"));
}

static void VegetationOverlap()
{
    Fake.AddZone(0);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    True(Run(new()
    {
        ZonesEnabled = false, LocationsEnabled = false,
        TerrainVegetationIds = ["copper", "copper"], VegetationIds = ["copper", "raspberry", "raspberry"]
    }).Success);
    Equal(2, Fake.VegetationPasses.Count);
    Sequence(["copper"], Fake.VegetationPasses[0].Ids);
    Sequence(["raspberry"], Fake.VegetationPasses[1].Ids);
    Sequence(["copper", "raspberry"], Fake.VegetationPasses.SelectMany(pass => pass.Ids));
    Equal(20f, Fake.VegetationPasses[0].Arguments.TerrainReset);
}

static void EmptyTerrainGroup()
{
    Fake.AddZone(0);
    var run = Run(new()
    {
        ZonesEnabled = false, LocationsEnabled = false, TerrainVegetationIds = [], VegetationIds = ["copper"]
    });
    True(run.Success);
    Equal(0, run.Warnings.Count);
    Equal(1, Fake.VegetationPasses.Count);
    Equal(0f, Fake.VegetationPasses.Single().Arguments.TerrainReset);
    Sequence([0], Fake.VegetationPasses.Single().ChangedZones);
}

static void EmptyVegetationGroups()
{
    Fake.AddZone(0);
    var run = Run(new() { ZonesEnabled = false, TerrainVegetationIds = [], VegetationIds = [], LocationSafeZones = 1 });
    True(run.Success);
    Equal(0, run.Warnings.Count);
    Equal(0, Fake.VegetationPasses.Count);
    True(Fake.Calls.Contains("locations.change:0"));
    Equal(1, Fake.Calls.Count(call => call == "save.start"));
}

static void UnknownTerrainGroup()
{
    Fake.AddZone(0);
    var run = Run(new()
    {
        ZonesEnabled = false, LocationsEnabled = false,
        TerrainVegetationIds = ["missing_ore"], VegetationIds = ["copper"]
    });
    True(run.Success);
    Equal(2, run.Warnings.Count);
    True(run.Warnings.Any(message => message.Contains("missing_ore")));
    Equal(1, Fake.VegetationPasses.Count);
    Equal(0f, Fake.VegetationPasses.Single().Arguments.TerrainReset);
    Sequence(["copper"], Fake.VegetationPasses.Single().Ids);
}

static void UnknownVegetationGroup()
{
    Fake.AddZone(0);
    var run = Run(new()
    {
        ZonesEnabled = false, LocationsEnabled = false,
        TerrainVegetationIds = ["copper"], VegetationIds = ["missing_shrub"]
    });
    True(run.Success);
    Equal(2, run.Warnings.Count);
    True(run.Warnings.Any(message => message.Contains("missing_shrub")));
    Equal(1, Fake.VegetationPasses.Count);
    Equal(20f, Fake.VegetationPasses.Single().Arguments.TerrainReset);
    Sequence(["copper"], Fake.VegetationPasses.Single().Ids);
}

static void BothVegetationGroupsMissing()
{
    Fake.AddZone(0);
    var run = Run(new()
    {
        ZonesEnabled = false, LocationsEnabled = false,
        TerrainVegetationIds = ["missing_ore"], VegetationIds = ["missing_shrub"]
    });
    True(run.Success);
    Equal(4, run.Warnings.Count);
    Equal(0, Fake.VegetationPasses.Count);
    True(!Fake.Calls.Any(IsSupplementChange));
}

static void ZeroTerrainRadius()
{
    Fake.AddZone(0);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    True(Run(new()
    {
        ZonesEnabled = false, LocationsEnabled = false, TerrainVegetationIds = ["copper"],
        VegetationIds = ["copper", "raspberry"], VegetationTerrainRadius = 0
    }).Success);
    Equal(2, Fake.VegetationPasses.Count);
    True(Fake.VegetationPasses.All(pass => pass.Arguments.TerrainReset == 0));
    Sequence(["copper", "raspberry"], Fake.VegetationPasses.SelectMany(pass => pass.Ids));
    True(Fake.VegetationPasses.All(pass => pass.ChangedZones.SequenceEqual([0])));
}

static void VegetationGroupProtection()
{
    SetSkippedScenario();
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    True(Run(new() { LocationsEnabled = false, VegetationIds = ["raspberry"] }).Success);
    Equal(2, Fake.VegetationPasses.Count);
    True(Fake.VegetationPasses.All(pass => pass.ChangedZones.SequenceEqual([0])));
    True(Fake.VegetationPasses.All(pass => pass.Arguments.SafeZones == 0));

    // With zone reset disabled, both groups cover the remaining generated world but respect the same resource protection.
    Fake.OnStart = null;
    Fake.AddZone(2);
    Fake.VegetationPasses.Clear();
    True(Run(new() { ZonesEnabled = false, LocationsEnabled = false, VegetationIds = ["raspberry"], VegetationSafeZones = 2 }).Success);
    Equal(2, Fake.VegetationPasses.Count);
    True(Fake.VegetationPasses.All(pass => pass.Arguments.SafeZones == 2));
    True(Fake.VegetationPasses.All(pass => pass.ChangedZones.SequenceEqual([2])));
}

static void TerrainRefreshBoundary()
{
    Fake.AddZone(0);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZonesEnabled = false, VegetationIds = ["raspberry"] }, completed);
    AdvanceThroughTerrainPass(runner);
    // Completion of the terrain pass returned the first boundary; no second group exists yet.
    Equal(1, Fake.VegetationPasses.Count);
    True(runner.MoveNext());
    Equal(1, Fake.VegetationPasses.Count); // Second boundary still precedes any no-terrain generation.
    True(runner.MoveNext());
    Equal(2, Fake.VegetationPasses.Count);
    Drain(runner);
    Sequence([true], completed);
    Sequence([0], Fake.VegetationPasses[1].ChangedZones);
}

static void CancelTerrainRefreshBoundary()
{
    Fake.AddZone(0);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZonesEnabled = false, VegetationIds = ["raspberry"] }, completed);
    AdvanceThroughTerrainPass(runner);
    runner.Dispose();
    Sequence([false], completed);
    Equal(1, Fake.VegetationPasses.Count);
    Equal(0, GameWorld.Pending.Count);
    True(!TerrainResetter.Active);
    True(!Fake.Calls.Contains("locations.create"));
}

static void AdvanceThroughTerrainPass(GuardedCoroutine runner)
{
    var frames = 0;
    while (!Fake.Calls.Contains("vegetation.end"))
    {
        True(runner.MoveNext());
        if (++frames > 20) throw new Exception("Terrain vegetation pass did not finish.");
    }
}

static void VegetationGroupSwitch()
{
    Fake.AddZone(0);
    var options = new RunOptions
    {
        ZonesEnabled = false, LocationsEnabled = false, VegetationEnabled = false,
        TerrainVegetationIds = ["missing_ore"], VegetationIds = ["missing_shrub"]
    };
    var disabled = Run(options);
    True(disabled.Success);
    Equal(0, disabled.Warnings.Count);
    Equal(0, Fake.VegetationPasses.Count);
    options.VegetationEnabled = true;
    var excluded = Run(options, includeVegetation: false);
    True(excluded.Success);
    Equal(0, excluded.Warnings.Count);
    Equal(0, Fake.VegetationPasses.Count);
}

static void FailedZone()
{
    Fake.AddZone(0);
    Fake.ThrowOnZone = "zones";
    var run = Run(new());
    True(!run.Success && run.Errors.Count > 0);
    Equal(1, Fake.BorderRepairs);
    True(!Fake.Calls.Contains("vegetation.create") && !Fake.Calls.Contains("locations.create"));
}

static void FailedStart()
{
    Fake.AddZone(0);
    Fake.ThrowOnStart = "zones";
    var run = Run(new());
    True(!run.Success && run.Errors.Count > 0);
    True(!Fake.Calls.Contains("vegetation.create"));
}

static void ChangedLocation()
{
    Fake.AddZone(0);
    Fake.OnStart = kind =>
    {
        if (kind != "locations") return;
        ZoneSystem.instance.m_locationInstances[new(0, 0)] = new()
        {
            m_placed = true,
            m_location = new("replacement")
        };
    };
    True(Run(new() { ZonesEnabled = false, VegetationEnabled = false }).Success);
    True(!Fake.Calls.Contains("locations.change:0"));
}

static void VegetationFailure()
{
    Fake.AddZone(0);
    Fake.ThrowOnZone = "vegetation";
    var original = ZoneSystem.instance.m_vegetation;
    var previousTemporary = new UnityEngine.GameObject();
    ZoneSystem.instance.m_tempSpawnedObjects.Add(previousTemporary);
    var run = Run(new() { ZonesEnabled = false });
    True(!run.Success && run.Errors.Count > 0);
    True(ReferenceEquals(original, ZoneSystem.instance.m_vegetation));
    True(!TerrainResetter.Active);
    Equal(1, Fake.LoadReleases);
    True(!Fake.Calls.Contains("locations.create"));
    Equal(1, Fake.DestroyedGhosts.Count);
    True(!Fake.DestroyedGhosts.Contains(previousTemporary));
    Equal(0, ZoneSystem.instance.m_tempSpawnedObjects.Count);
}

static void LocationGhostFailure()
{
    Fake.AddZone(0);
    ZoneSystem.instance.Loaded.Add(new(0, 0));
    Fake.ThrowOnZone = "locations";
    var previousTemporary = new UnityEngine.GameObject();
    ZoneSystem.instance.m_tempSpawnedObjects.Add(previousTemporary);
    var run = Run(new() { ZonesEnabled = false, VegetationEnabled = false });
    True(!run.Success && run.Errors.Count > 0);
    Equal(1, Fake.DestroyedGhosts.Count);
    True(!Fake.DestroyedGhosts.Contains(previousTemporary));
    Equal(0, ZoneSystem.instance.m_tempSpawnedObjects.Count);
}

static void CancelLoad()
{
    Fake.AddZone(0);
    Fake.LoadOnPoke = false;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZonesEnabled = false }, completed);
    True(runner.MoveNext());
    Equal(1, GameWorld.Pending.Count);
    runner.Dispose();
    runner.Dispose();
    Equal(0, GameWorld.Pending.Count);
    Equal(1, Fake.LoadReleases);
    Sequence([false], completed);
    True(!Fake.Calls.Contains("locations.create"));
}

static void CancelZones()
{
    Fake.AddZone(0); Fake.AddZone(1);
    using var lease = MaintenanceGate.Acquire();
    using var runner = NewRunner(new(), new());
    True(runner.MoveNext());
    Equal(1, Fake.Calls.Count(value => value.StartsWith("zones.change:")));
    runner.Dispose(); runner.Dispose();
    Equal(1, Fake.BorderRepairs);
    True(!Fake.Calls.Contains("vegetation.create"));
}

static void CleanupAll()
{
    Fake.AddZone(0); Fake.AddZone(1);
    var tracker = new OperationTracker(null, null, 64, 8);
    foreach (var zone in ZoneSystem.instance.m_generatedZones)
    {
        tracker.MayLoad(zone);
        GameWorld.PokeZone(zone);
    }
    Fake.ReleaseFailures.Add(new(0, 0));
    Throws<AggregateException>(() => tracker.Cleanup());
    Equal(2, Fake.LoadReleases);
    Equal(0, GameWorld.Pending.Count);
    tracker.Cleanup();
    Equal(2, Fake.LoadReleases);
}

static void CleanupRefresh()
{
    Fake.AddZone(0);
    Fake.LoadOnPoke = false;
    using var lease = MaintenanceGate.Acquire();
    var errors = new List<Exception>();
    var completed = new List<bool>();
    using var runner = new GuardedCoroutine(new MaintenancePipeline(new() { ZonesEnabled = false }, true, _ => { }, _ => { }).Run(),
        () => true, errors.Add, completed.Add);
    True(runner.MoveNext()); // Mandatory pre-save yield, before any zone load.
    True(runner.MoveNext());
    Fake.ReleaseFailures.Add(new(0, 0));
    runner.Dispose();
    Equal(1, Fake.LoadReleases);
    True(Fake.Calls.Contains("terrain.refresh"));
    True(errors.Any(error => error is AggregateException));
    Sequence([false], completed);
    True(!Fake.Calls.Contains("locations.create"));
}

static void CandidateRestriction()
{
    Fake.AddZone(0); Fake.AddZone(1); Fake.AddZone(2, marker: true);
    var candidates = new HashSet<Vector2s>([new(1, 0), new(2, 0), new(3, 0)]);
    var operation = new TrackedResetZones(_ => { }, Parameters(1), candidates);
    // Planning owns its constructor-time snapshot, even if streaming or the caller changes before Init.
    Fake.AddZone(3);
    candidates.Clear();
    candidates.Add(new(0, 0));
    operation.Init();
    Equal(2, operation.Result.CandidateZones.Count);
    Sequence([new Vector2s(1, 0)], operation.Result.SelectedZones);
}

static void SaveFailure()
{
    Fake.AddZone(0);
    Fake.SaveError = "Error saving world! simulated disk failure";
    var run = Run(new());
    True(!run.Success && run.Errors.Count > 0);
    True(!Fake.Calls.Contains("zones.create"));
    Equal(0, UnityEngine.Application.ListenerCount);
    True(ZNet.WorldSaveStarted == null);
}

static void SaveDidNotStart()
{
    Fake.AddZone(0); Fake.IgnoreSave = true;
    True(!Run(new()).Success);
    True(!Fake.Calls.Contains("zones.create"));
    Equal(0, UnityEngine.Application.ListenerCount);
}

static void CancelSave()
{
    Fake.AddZone(0); Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire();
    using var runner = NewRunner(new(), new(), advancePastSave: false);
    True(runner.MoveNext());
    Equal(1, UnityEngine.Application.ListenerCount);
    runner.Dispose();
    Equal(0, UnityEngine.Application.ListenerCount);
    True(ZNet.WorldSaveStarted == null);
    True(!Fake.Calls.Contains("zones.create"));
}

static void PauseZones()
{
    Fake.AddZone(0); Fake.AddZone(1);
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { VegetationEnabled = false, LocationsEnabled = false }, completed);
    True(runner.MoveNext());
    UnityEngine.Time.timeScale = 0;
    var changes = Fake.Calls.Count(value => value.StartsWith("zones.change:"));
    True(runner.MoveNext()); True(runner.MoveNext());
    Equal(changes, Fake.Calls.Count(value => value.StartsWith("zones.change:")));
    UnityEngine.Time.timeScale = 1;
    Drain(runner);
    Sequence([true], completed);
    Equal(2, Fake.Calls.Count(value => value.StartsWith("zones.change:")));
}

static void PauseSaveTimeout()
{
    Fake.AddZone(0); Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new()
    {
        SaveTimeoutSeconds = 0.2f,
        ZonesEnabled = false, VegetationEnabled = false, LocationsEnabled = false
    }, completed, advancePastSave: false);
    True(runner.MoveNext());
    UnityEngine.Time.timeScale = 0;
    True(runner.MoveNext());
    Thread.Sleep(250);
    ZNet.instance.Saving = false;
    UnityEngine.Time.timeScale = 1;
    Drain(runner);
    Sequence([true], completed);
}

static void PauseBeforeFinalization()
{
    Fake.AddZone(0);
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { VegetationEnabled = false, LocationsEnabled = false }, completed);
    True(runner.MoveNext());
    UnityEngine.Time.timeScale = 0;
    True(runner.MoveNext());
    Equal(0, Fake.BorderRepairs);
    UnityEngine.Time.timeScale = 1;
    Drain(runner);
    Equal(1, Fake.BorderRepairs);
    Sequence([true], completed);
}

static void GateExclusion()
{
    True(MaintenanceGate.IsAvailable);
    var lease = MaintenanceGate.Acquire();
    True(MaintenanceGate.IsActive && !MaintenanceGate.IsAvailable);
    Throws<InvalidOperationException>(() => MaintenanceGate.Acquire());
    lease.Dispose(); lease.Dispose();
    True(MaintenanceGate.IsAvailable);

}

static (bool Success, List<Exception> Errors, List<string> Warnings) Run(RunOptions options, bool includeVegetation = true)
{
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    var errors = new List<Exception>();
    var warnings = new List<string>();
    using var runner = new GuardedCoroutine(new MaintenancePipeline(options, includeVegetation, _ => { }, warnings.Add).Run(),
        () => true, errors.Add, completed.Add);
    Drain(runner);
    Equal(1, completed.Count);
    return (completed[0], errors, warnings);
}

static GuardedCoroutine NewRunner(RunOptions options, List<bool> completed, bool advancePastSave = true)
{
    var runner = new GuardedCoroutine(new MaintenancePipeline(options, true, _ => { }, _ => { }).Run(), () => true,
        Fake.UnexpectedErrors.Add, completed.Add);
    // Lifecycle tests start after the mandatory pre-save yield. Save tests inspect that yield directly.
    if (advancePastSave) True(runner.MoveNext());
    return runner;
}

static void Drain(GuardedCoroutine runner)
{
    var frames = 0;
    while (runner.MoveNext())
    {
        True(runner.Current == null);
        if (++frames > 100) throw new Exception("Coroutine failed to finish within the test frame limit.");
    }
}

static OperationParameters Parameters(int safeZones = 0) =>
    new() { SafeZones = safeZones };

static void Before(string first, string second)
{
    var a = Fake.Calls.IndexOf(first); var b = Fake.Calls.IndexOf(second);
    True(a >= 0 && b > a, $"Expected {first} before {second}; actual: {string.Join(", ", Fake.Calls)}");
}

static void True(bool condition, string? message = null)
{
    if (!condition) throw new Exception(message ?? "Assertion failed.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; got {actual}.");
}

static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new Exception($"Expected [{string.Join(", ", expected)}]; got [{string.Join(", ", actual)}].");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

