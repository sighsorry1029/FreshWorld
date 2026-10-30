using FreshWorld.Backend;
using FreshWorld.Configuration;
using FreshWorld.Runtime;
using FreshWorld.Engine;

var tests = new (string Name, Action Body)[]
{
    ("PoV objectives override empty absolute lists and SafeZones zero across all reset stages", PovStages),
    ("PoV snapshot follows saving and late linked objectives stop loading retries", PovArrival),
    ("orphan Blight links share one controller scan per frame and detect a later event", PovIndexCost),
    ("PoV options control stage protection independently and disabled Blight does not scan controllers", PovOptions),
    ("metadata references are released when planning is cancelled or fails", LocationMetadataLifetime),
    ("progressing location metadata loads can take over 180 seconds in total", PlanningSlowProgress),
    ("an unready prefab still times out after 30 active seconds before any reset", PlanningPrefabTimeout),
    ("verified planning timeouts protect all instances and 3x3 neighbors across reset stages", PlanningPartialStages),
    ("planning exclusions expand through large and connected location footprints", PlanningPartialExtent),
    ("planning exclusions are omitted from random draws and retried on the next run", PlanningPartialSelection),
    ("a changed fallback definition or exclusion stops before further mutation", PlanningPartialChanges),
    ("partial plans allow completed zone height refreshes and native unique pruning", PlanningNativeChanges),
    ("metadata ready on the deadline-crossing poll is accepted", PlanningReadyAtDeadline),
    ("paused time does not consume the location metadata timeout", PlanningPauseTimeout),
    ("planning diagnostics identify native and named metadata failures without replacing them", PlanningFailureDiagnostics),
    ("planning cancellation reports once without logging during loading waits", PlanningCancellationDiagnostics),
    ("planning diagnostics cannot block cleanup or hide the original stop", PlanningDiagnosticSinkFailure),
    ("loading diagnostics are sampled sparsely and finish with current timeout state", PlanningLoadSamples),
    ("loading diagnostic failures retain the timeout and named references are not sampled", PlanningLoadDiagnosticFailure),
    ("each new prefab starts with independent loading diagnostics", PlanningLoadDiagnosticReset),
    ("planning reports definition changes and invalid bounds before reset", PlanningBoundsDiagnostics),
    ("successful planning adds no per-prefab log lines", PlanningSuccessDiagnostics),
    ("planning failures retain completed progress without carrying state into the next run", PlanningProgressDiagnostics),
    ("lazy neighboring bounds are resolved even outside the apparent reservation", HiddenNeighborLocation),
    ("replaced managed definitions abort before any reset", ReplacedPlanningDefinition),
    ("late mapped instances wait for the next reservation plan", LateMappedInstance),
    ("mapped locations group zone resets even when supplemental locations are disabled or unselected", MappedReservation),
    ("selected multi-zone locations use grouped zone resets or radius restoration", MultiZoneRouting),
    ("grouped zone resets reject pending roots without forcing loads", GroupPendingLoad),
    ("grouped zone resets retain late protection and registry changes for later stages", GroupLateChanges),
    ("unvisited mapped anchors do not make their neighboring zones new reset targets", GroupUngeneratedAnchor),
    ("shared-sector groups expand transitively while separate groups stay independent", GroupConnectedLocations),
    ("neighbor protection blocks the entire location including ungenerated sectors", MultiZoneProtection),
    ("protection appearing while a location loads prevents all mutation", MultiZoneLateProtection),
    ("shared-sector reservations preserve disjoint neighboring location exteriors", DisjointLocationReservation),
    ("neighboring unresolved definitions expand reservations before zone deletion", NeighborLocationResolution),
    ("overlapping locations reset together in zone mode and remain preserved in radius mode", MultiZoneOverlap),
    ("percentage sampling counts managed location anchors once without splitting their area", MultiZonePercent),
    ("protected zone groups use location switches and IDs for radius fallback", GroupLocationFallback),
    ("location fallback and zone resets share one percentage draw", GroupFallbackPercent),
    ("connected selected locations share one draw even with a single-sector neighbor", GroupNeighborFallback),
    ("an ungenerated managed owner does not block a selected generated neighbor's radius reset", UngeneratedOwnerFallback),
    ("successful zone groups never receive location fallback after regenerating", RegeneratedGroupNoFallback),
    ("failed zone groups abort rather than starting location fallback", FailedGroupNoFallback),
    ("resolved radii are reserved before zone deletion", MultiZoneResolvedRadius),
    ("changed location position or radius cannot expand the planned reset", MultiZoneChangedExtent),
    ("retained scope excludes newly skipped zones", RetainedScope),
    ("disabling zone reset supplements all generated zones", WithoutZoneReset),
    ("resource exclusions skip both groups at SafeZones zero while locations stay independent", ResourceExclusions),
    ("resource exclusions filter automatic candidates without suppressing other stages", ResourceExclusionPercent),
    ("resource exclusion guards retain reserved and absolute terrain protection", ResourceExclusionGuards),
    ("resource exclusions are captured once per run and apply in another world", ResourceExclusionSnapshot),
    ("partial resets select a fixed rounded count without duplicate targets", PercentageCounts),
    ("both resource groups and locations share one random selection", PercentageSharedSupplements),
    ("partial zone resets preserve retained supplement scope", PercentageRetainedScope),
    ("location-only sampling excludes absent, unplaced and unselected locations", PercentageLocationCandidates),
    ("unknown and disabled resource stages do not inflate the candidate count", PercentageInactiveResources),
    ("late protection skips a selected target without a replacement draw", PercentageLateProtection),
    ("partial selection freezes scope while full runs retain live supplements", PercentageNewZones),
    ("partial candidate snapshot occurs after saving", PercentageAfterSave),
    ("ungenerated zones and reset zones awaiting a new visit are not sampled", PercentageGeneratedScope),
    ("occupied zones remain intact at SafeZones zero and are eligible on the next run", OccupiedZoneAndNextRun),
    ("all stages protect the player's 3x3 area including diagonals at SafeZones zero", PlayerNeighborhood),
    ("player neighborhood protection clips native coordinate edges without wrapping", PlayerNeighborhoodEdges),
    ("the initial player snapshot uses positions after the world save finishes", PostSavePlayerSnapshot),
    ("both resource passes and locations retain player protection after departure with zones disabled", PlayerSnapshotSupplements),
    ("player movement after a zone yield protects an adjacent next target", MovementAfterZoneYield),
    ("a ready player arriving nearby during a vegetation load retry skips mutation and releases the load", PlayerArrivalDuringVegetationLoad),
    ("a ready player arriving nearby during a location load retry skips mutation and releases the load", PlayerArrivalDuringLocationLoad),
    ("a base-filtered resource stage still records players for the later location stage", BaseFilteredStageSnapshot),
    ("skipped targets remain unchanged and finish without failures in every tracking wrapper", SkippedOperationResults),
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
    ("a slow zone is logged and skipped without failing the operation", SlowZoneSkip),
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
    ("gate refuses overlapping work and releases idempotently", GateExclusion),
    ("treasure protects only its own sector in all stages and releases on the next run", TreasureStages),
    ("disabled EpicLoot protection permits direct resets in treasure sectors", TreasureDisabled),
    ("treasure snapshot is taken after the initial world save", TreasureAfterSave),
    ("load retries protect new treasures only in the target sector", TreasureDuringLoad),
    ("neighbor treasure blocks full location extents but not resource terrain/group extents", TreasureNeighbors),
    ("bounty protection independently skips its sectors in every stage", BountyStages),
    ("bounties created during loading stop only their own sector and release pending loads", BountyDuringLoad),
    ("invasion objective zones override SafeZones zero for all stages and remain protected for one run", InvasionStages),
    ("invasion objective snapshot is taken after saving and also supplies the terrain guard", InvasionAfterSave),
    ("invasion objectives arriving during resource or location loading cancel only their own zone", InvasionDuringLoad),
    ("missing invasion event data does not prevent object-based protection or reset other zones", InvasionUnavailable),
    ("always-protected zones survive every stage and terrain requests independently of other policies", AlwaysProtectedStages),
    ("always-protected targets arriving during loading cancel mutation and release the load", AlwaysProtectedDuringLoad),
    ("always-protected snapshot uses world objects after the save completes", AlwaysProtectedAfterSave)
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

static void PlanningSlowProgress()
{
    using var time = Stopwatch.UseManualTime();
    for (var i = 0; i < 10; i++) Fake.AddZone(i * 5, location: i == 0 ? "cave" : "cave" + i);
    var loaded = 0;
    var polls = 0;
    Fake.OnMetadataLoad = () => { loaded++; polls = 0; Fake.MetadataReady = false; };
    Fake.OnMetadataReady = () =>
    {
        if (++polls == 1) time.Advance(TimeSpan.FromSeconds(20));
        else Fake.MetadataReady = true;
    };
    var logs = new List<string>();
    var options = MultiZoneOptions();
    options.VegetationEnabled = false;
    options.LocationsEnabled = false;
    var run = Run(options, logs: logs);
    True(run.Success, string.Join("; ", run.Errors.Select(error => error.Message)));
    Equal(10, loaded); Equal(0, Fake.MetadataReferences);
    True(logs.Single(message => message.StartsWith("Location plan:")).Contains("10 definitions in 200"));
    Equal(0, run.Warnings.Count);
    // Restoration is admitted only after every definition has resolved.
    Equal(10, Changed("zones").Count);
}

static void PlanningPrefabTimeout()
{
    foreach (var named in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.NamedMetadata = named; Fake.MetadataReady = false;
        using var time = Stopwatch.UseManualTime();
        using var lease = MaintenanceGate.Acquire();
        var completed = new List<bool>(); var errors = new List<Exception>(); var warnings = new List<string>();
        using var runner = new GuardedCoroutine(new MaintenancePipeline(MultiZoneOptions(), true, _ => { }, warnings.Add).Run(),
            () => true, errors.Add, completed.Add);
        True(runner.MoveNext()); // Save boundary, before any metadata load.
        True(runner.MoveNext());
        time.Advance(TimeSpan.FromSeconds(29)); True(runner.MoveNext()); Equal(0, errors.Count);
        time.Advance(TimeSpan.FromSeconds(1)); True(!runner.MoveNext());
        Sequence([false], completed); True(errors.Single() is TimeoutException);
        True(errors[0].Message.Contains("cave")); Equal(0, Fake.MetadataReferences);
        var diagnostic = warnings.Single();
        True(diagnostic.Contains("stage=WaitForPrefab")); True(diagnostic.Contains("prefab='cave'"));
        True(diagnostic.Contains("lastReady=False"));
        True(!Fake.Calls.Any(call => call.Contains(".change:")));
    }
}

static void PlanningPartialStages()
{
    foreach (var zones in new[] { false, true })
    {
        Fake.Reset();
        foreach (var center in new[] { 0, 10 })
        {
            Fake.AddZone(center, location: "broken");
            ZoneSystem.instance.m_locationInstances[new(center, 0)].m_location.m_exteriorRadius = 10;
            for (var x = center - 1; x <= center + 1; x++)
                for (var y = -1; y <= 1; y++) ZoneSystem.instance.m_generatedZones.Add(new(x, y));
        }
        ZoneSystem.instance.m_generatedZones.Remove(new(10, 0));
        var ungenerated = ZoneSystem.instance.m_locationInstances[new(10, 0)];
        ungenerated.m_placed = false;
        ZoneSystem.instance.m_locationInstances[new(10, 0)] = ungenerated;
        Fake.AddZone(3); Fake.AddZone(20);
        Fake.TrustedMetadata.Add("broken");
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        Fake.OnResolve = definition =>
        {
            if (definition.m_prefab.Name == "broken") throw new Exception("A timed-out asset must not be resolved synchronously.");
        };
        using var time = Stopwatch.UseManualTime();
        Fake.OnMetadataReady = () =>
        {
            Fake.MetadataReady = Fake.LoadingMetadata != "broken";
            if (!Fake.MetadataReady) time.Advance(TimeSpan.FromSeconds(31));
        };
        var options = new RunOptions { ZonesEnabled = zones, LocationSafeZones = 0, VegetationSafeZones = 0, LocationIds = ["cave", "broken"] };
        var run = Run(options);
        True(run.Success, string.Join(";", run.Errors)); Equal(0, Fake.MetadataReferences);
        True(run.Warnings.Any(message => message.Contains("1 timed-out location prefab types, 2 instances and 18 sectors")));
        True(!run.Warnings.Any(message => message.Contains("planning stopped")));
        Sequence([3, 20], Changed(zones ? "zones" : "locations"));
        if (!zones) Sequence([3, 20], Changed("vegetation"));
        foreach (var args in Fake.Arguments.Values)
        {
            foreach (var center in new[] { 0, 10 })
                for (var x = center - 1; x <= center + 1; x++)
                    for (var y = -1; y <= 1; y++)
                    {
                        True(!args.CanResetTerrain!(new(x, y)));
                        True(!args.CanDeleteObject!(new ZDO { Zone = new(x, y) }));
                    }
            True(args.CanResetTerrain!(new(20, 0)));
        }
    }
}

static void PlanningPartialExtent()
{
    Fake.AddZone(0, location: "broken");
    ZoneSystem.instance.m_locationInstances[new(0, 0)].m_location.m_exteriorRadius = 150;
    Fake.AddZone(4, location: "wide");
    ZoneSystem.instance.m_locationInstances[new(4, 0)].m_location.m_exteriorRadius = 100;
    Fake.AddZone(7, location: "wide2");
    ZoneSystem.instance.m_locationInstances[new(7, 0)].m_location.m_exteriorRadius = 100;
    Fake.AddZone(30);
    for (var i = -2; i <= 9; i++) ZoneSystem.instance.m_generatedZones.Add(new(i, 0));
    Fake.TrustedMetadata.Add("broken");
    using var time = Stopwatch.UseManualTime();
    Fake.OnMetadataReady = () =>
    {
        Fake.MetadataReady = Fake.LoadingMetadata != "broken";
        if (!Fake.MetadataReady) time.Advance(TimeSpan.FromSeconds(31));
    };
    var result = Run(new RunOptions { LocationIds = ["cave", "broken", "wide", "wide2"] });
    True(result.Success, string.Join(";", result.Errors));
    Sequence([30], Changed("zones"));
    True(!Fake.Arguments["zones"].CanResetTerrain!(new(9, 0)));
    Equal(0, Changed("locations").Count);
}

static void PlanningPartialSelection()
{
    Fake.AddZone(0, location: "broken"); Fake.AddZone(1); Fake.AddZone(10); Fake.AddZone(20);
    ZoneSystem.instance.m_locationInstances[new(0, 0)].m_location.m_exteriorRadius = 10;
    Fake.TrustedMetadata.Add("broken");
    using var time = Stopwatch.UseManualTime();
    Fake.OnMetadataReady = () =>
    {
        Fake.MetadataReady = Fake.LoadingMetadata != "broken";
        if (!Fake.MetadataReady) time.Advance(TimeSpan.FromSeconds(31));
    };
    var options = new RunOptions { ZonesEnabled = false, VegetationEnabled = false, LocationIds = ["cave", "broken"] };
    var logs = new List<string>();
    var first = Run(options, resetPercent: 50, logs: logs);
    True(first.Success); Equal(1, Changed("locations").Count);
    True(logs.Any(message => message.StartsWith("Automatic reset selection: 1/2 candidates")));
    Fake.Calls.Clear(); Fake.MetadataReady = true; Fake.OnMetadataReady = null;
    var next = Run(options); True(next.Success); True(!next.Warnings.Any(message => message.Contains("planning")));
    Sequence([0, 1, 10, 20], Changed("locations"));
}

static void PlanningPartialChanges()
{
    foreach (var change in new[] { "radius", "position", "height", "registry", "replacement" })
    {
        Fake.Reset(); Fake.AddZone(0, location: "broken"); Fake.AddZone(10);
        ZoneSystem.instance.m_locationInstances[new(0, 0)].m_location.m_exteriorRadius = 10;
        Fake.TrustedMetadata.Add("broken");
        using var time = Stopwatch.UseManualTime();
        Fake.OnMetadataReady = () =>
        {
            Fake.MetadataReady = Fake.LoadingMetadata != "broken";
            if (!Fake.MetadataReady) time.Advance(TimeSpan.FromSeconds(31));
        };
        Fake.OnStart = _ =>
        {
            var instance = ZoneSystem.instance.m_locationInstances[new(0, 0)];
            if (change == "radius") instance.m_location.m_exteriorRadius = 150;
            else if (change == "position")
            {
                instance.m_position = new(800, 0, 0);
                ZoneSystem.instance.m_locationInstances[new(0, 0)] = instance;
            }
            else if (change == "replacement") Fake.AddZone(10, location: "broken");
            else if (change == "height")
            {
                instance.m_position = new(0, 20, 0);
                ZoneSystem.instance.m_locationInstances[new(0, 0)] = instance;
            }
            else Fake.AddZone(11, location: "broken");
        };
        var run = Run(new RunOptions { LocationIds = ["cave", "broken"] });
        True(!run.Success); True(!Fake.Calls.Any(call => call.Contains(".change:"))); Equal(0, Fake.MetadataReferences);
    }
}

static void PlanningNativeChanges()
{
    foreach (var zones in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0, location: "broken"); Fake.AddZone(10); Fake.AddZone(20); Fake.AddZone(40);
        ZoneSystem.instance.m_locationInstances[new(0, 0)].m_location.m_exteriorRadius = 10;
        Fake.TrustedMetadata.Add("broken");
        using var time = Stopwatch.UseManualTime();
        Fake.OnMetadataReady = () =>
        {
            Fake.MetadataReady = Fake.LoadingMetadata != "broken";
            if (!Fake.MetadataReady) time.Advance(TimeSpan.FromSeconds(31));
        };
        Fake.ResetGroundHeight = 25;
        if (!zones)
        {
            var unplaced = ZoneSystem.instance.m_locationInstances[new(40, 0)];
            unplaced.m_placed = false; ZoneSystem.instance.m_locationInstances[new(40, 0)] = unplaced;
            Fake.OnLocationPlaced = _ => ZoneSystem.instance.m_locationInstances.Remove(new(40, 0));
        }
        var result = Run(new RunOptions { ZonesEnabled = zones, VegetationEnabled = false, LocationIds = ["cave", "broken"] });
        True(result.Success, string.Join(";", result.Errors));
        if (zones) Sequence([10, 20, 40], Changed("zones"));
        else Sequence([10, 20], Changed("locations"));
    }
}

static void PlanningReadyAtDeadline()
{
    foreach (var named in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.NamedMetadata = named; Fake.MetadataReady = false;
        using var time = Stopwatch.UseManualTime();
        using var lease = MaintenanceGate.Acquire();
        var completed = new List<bool>();
        using var runner = NewRunner(MultiZoneOptions(), completed);
        True(runner.MoveNext());
        time.Advance(TimeSpan.FromSeconds(31)); Fake.MetadataReady = true;
        Drain(runner);
        Sequence([true], completed); Equal(0, Fake.MetadataReferences);
        Sequence([0], Changed("zones"));
    }
}

static void PlanningPauseTimeout()
{
    foreach (var named in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.NamedMetadata = named; Fake.MetadataReady = false;
        using var time = Stopwatch.UseManualTime();
        using var lease = MaintenanceGate.Acquire();
        var completed = new List<bool>(); var errors = new List<Exception>(); var warnings = new List<string>();
        using var runner = new GuardedCoroutine(new MaintenancePipeline(MultiZoneOptions(), true, _ => { }, warnings.Add).Run(),
            () => true, errors.Add, completed.Add);
        True(runner.MoveNext()); True(runner.MoveNext());
        time.Advance(TimeSpan.FromSeconds(20)); True(runner.MoveNext());
        UnityEngine.Time.timeScale = 0; True(runner.MoveNext());
        time.Advance(TimeSpan.FromMinutes(5)); True(runner.MoveNext());
        UnityEngine.Time.timeScale = 1; True(runner.MoveNext()); Equal(0, errors.Count);
        time.Advance(TimeSpan.FromSeconds(9)); True(runner.MoveNext());
        time.Advance(TimeSpan.FromSeconds(1)); True(!runner.MoveNext());
        Sequence([false], completed); True(errors.Single() is TimeoutException);
        True(warnings.Single().Contains("elapsed=30.00s")); Equal(0, Fake.MetadataReferences);
        True(!Fake.Calls.Any(call => call.Contains(".change:")));
    }
}

static void LocationMetadataLifetime()
{
    Fake.AddZone(0); Fake.MetadataReady = false;
    using (var lease = MaintenanceGate.Acquire())
    {
        var completed = new List<bool>();
        using var runner = NewRunner(MultiZoneOptions(), completed);
        True(runner.MoveNext()); Equal(1, Fake.MetadataReferences);
        runner.Dispose(); Equal(0, Fake.MetadataReferences);
        True(!Fake.Calls.Any(call => call.Contains(".change:")));
    }
    foreach (var throwDuringLoad in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0);
        if (throwDuringLoad) Fake.OnMetadataLoad = () => throw new InvalidOperationException("load callback failure");
        else Fake.OnResolve = _ => throw new InvalidOperationException("metadata hook failure");
        var run = Run(MultiZoneOptions()); True(!run.Success); Equal(0, Fake.MetadataReferences);
        True(!Fake.Calls.Any(call => call.Contains(".change:")));
    }
}

static void PlanningFailureDiagnostics()
{
    foreach (var stage in new[] { "LoadAsync", "WaitForPrefab", "ResolveAsset", "ReleasePrefab", "NamedReadiness" })
    {
        Fake.Reset(); Fake.AddZone(0);
        var failure = new InvalidOperationException("fixture " + stage);
        switch (stage)
        {
            case "LoadAsync": Fake.OnMetadataLoad = () => throw failure; break;
            case "WaitForPrefab": Fake.OnMetadataReady = () => throw failure; break;
            case "ResolveAsset": Fake.OnResolve = _ => throw failure; break;
            case "ReleasePrefab": Fake.OnMetadataRelease = () => throw failure; break;
            case "NamedReadiness": Fake.NamedMetadata = true; Fake.OnMetadataReady = () => throw failure; break;
        }
        var run = Run(MultiZoneOptions());
        True(!run.Success); True(ReferenceEquals(failure, run.Errors.Single()));
        var diagnostic = run.Warnings.Single(message => message.StartsWith("Location planning stopped:"));
        True(diagnostic.Contains("stage=" + (stage == "NamedReadiness" ? "WaitForPrefab" : stage) + ";"));
        True(diagnostic.Contains("prefab='cave'")); True(diagnostic.Contains("definitions=0/1"));
        True(diagnostic.Contains("elapsed=")); True(diagnostic.Contains("stageElapsed="));
        True(diagnostic.Contains("prefabElapsed="));
        if (stage == "NamedReadiness") True(diagnostic.Contains("readiness=CanSpawnLocation"));
        if (stage == "WaitForPrefab") True(diagnostic.Contains("readiness=IsLoaded"));
        Equal(0, Fake.MetadataReferences);
        True(!Fake.Calls.Any(call => call.Contains(".change:")));
    }
}

static void PlanningCancellationDiagnostics()
{
    Fake.AddZone(0); Fake.MetadataReady = false;
    var warnings = new List<string>(); var logs = new List<string>(); var completed = new List<bool>();
    using var lease = MaintenanceGate.Acquire();
    using var runner = new GuardedCoroutine(new MaintenancePipeline(MultiZoneOptions(), true, logs.Add, warnings.Add).Run(),
        () => true, Fake.UnexpectedErrors.Add, completed.Add);
    True(runner.MoveNext()); // Mandatory save boundary.
    True(runner.MoveNext());
    var waitingLogCount = logs.Count;
    for (var i = 0; i < 20; i++) True(runner.MoveNext());
    Equal(waitingLogCount, logs.Count); Equal(0, warnings.Count); Equal(1, Fake.MetadataReferences);
    runner.Dispose(); runner.Dispose();
    Equal(1, completed.Count); True(!completed[0]); Equal(0, Fake.MetadataReferences);
    Equal(1, warnings.Count);
    True(warnings[0].Contains("stage=WaitForPrefab")); True(warnings[0].Contains("lastReady=False"));
    True(!Fake.Calls.Any(call => call.Contains(".change:")));
}

static void PlanningDiagnosticSinkFailure()
{
    Fake.AddZone(0);
    var failure = new InvalidOperationException("asset failure");
    Fake.OnResolve = _ => throw failure;
    var errors = new List<Exception>(); var completed = new List<bool>(); var warningCalls = 0;
    using (MaintenanceGate.Acquire())
    {
        using var runner = new GuardedCoroutine(new MaintenancePipeline(MultiZoneOptions(), true, _ => { }, _ =>
        {
            warningCalls++;
            throw new InvalidOperationException("diagnostic sink failure");
        }).Run(), () => true, errors.Add, completed.Add);
        Drain(runner);
        True(!completed.Single()); True(ReferenceEquals(failure, errors.Single()));
        Equal(1, warningCalls); Equal(0, Fake.MetadataReferences);
        True(!Fake.Calls.Any(call => call.Contains(".change:")));
    }

    Fake.Reset(); Fake.AddZone(0);
    Fake.OnResolve = _ => throw failure;
    Fake.OnMetadataRelease = () => throw new InvalidOperationException("cleanup failure");
    var run = Run(MultiZoneOptions());
    True(!run.Success); Equal(0, Fake.MetadataReferences);
    True(run.Warnings.Single().Contains("stage=ResolveAsset")); // Cleanup must not replace the recorded stage.
    True(run.Errors.Single().Message.Contains("cleanup failure")); // Preserve existing exception behavior.
}

static void PlanningLoadSamples()
{
    Fake.AddZone(0); Fake.MetadataReady = false; Fake.MetadataLoading = true; Fake.MetadataProgress = 0.25f;
    var warnings = new List<string>(); var errors = new List<Exception>(); var completed = new List<bool>();
    using var time = Stopwatch.UseManualTime();
    using var lease = MaintenanceGate.Acquire();
    using var runner = new GuardedCoroutine(new MaintenancePipeline(MultiZoneOptions(), true, _ => { }, warnings.Add).Run(),
        () => true, errors.Add, completed.Add);
    True(runner.MoveNext()); True(runner.MoveNext()); Equal(1, Fake.MetadataDiagnosticSamples);
    for (var i = 0; i < 20; i++) { time.Advance(TimeSpan.FromMilliseconds(10)); True(runner.MoveNext()); }
    Equal(1, Fake.MetadataDiagnosticSamples); Equal(0, warnings.Count);
    time.Advance(TimeSpan.FromSeconds(9.8)); Fake.MetadataProgress = 0.5f; True(runner.MoveNext());
    Equal(2, Fake.MetadataDiagnosticSamples);
    time.Advance(TimeSpan.FromSeconds(19.5)); True(runner.MoveNext());
    time.Advance(TimeSpan.FromSeconds(0.54)); True(!runner.MoveNext());
    Equal(4, Fake.MetadataDiagnosticSamples); Equal(0, Fake.MetadataReferences);
    True(errors.Single() is TimeoutException);
    var message = warnings.Single();
    True(message.Contains("isLoading=True; loadProgress=0.500; loadSamples=4"));
    True(message.Contains("loadSampleAge=0.00s; progressUnchangedFor=20.04s"));
}

static void PlanningLoadDiagnosticFailure()
{
    foreach (var named in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.NamedMetadata = named; Fake.MetadataReady = false;
        Fake.OnMetadataDiagnostics = () => throw new InvalidOperationException("diagnostic accessor unavailable");
        using var time = Stopwatch.UseManualTime();
        Fake.OnMetadataReady = () => time.Advance(TimeSpan.FromSeconds(31));
        var run = Run(MultiZoneOptions());
        True(!run.Success); True(run.Errors.Single() is TimeoutException); Equal(0, Fake.MetadataReferences);
        Equal(named ? 0 : 1, Fake.MetadataDiagnosticSamples);
        True(run.Warnings.Single().Contains("isLoading=unknown; loadProgress=unknown"));
    }
}

static void PlanningLoadDiagnosticReset()
{
    Fake.AddZone(0); Fake.AddZone(5, location: "other");
    Fake.MetadataLoading = true; Fake.MetadataProgress = 0.8f;
    using var time = Stopwatch.UseManualTime();
    var polls = 0;
    Fake.OnMetadataReady = () =>
    {
        if (Fake.LoadingMetadata == "cave") { Fake.MetadataReady = ++polls > 1; return; }
        Fake.MetadataReady = false; time.Advance(TimeSpan.FromSeconds(31));
        Fake.MetadataLoading = false; Fake.MetadataProgress = 0;
    };
    var run = Run(MultiZoneOptions()); True(!run.Success);
    True(run.Warnings.Single().Contains("prefab='other'"));
    True(run.Warnings.Single().Contains("isLoading=False; loadProgress=0.000; loadSamples=1"));
    Equal(2, Fake.MetadataDiagnosticSamples); Equal(0, Fake.MetadataReferences);
}

static void PlanningBoundsDiagnostics()
{
    foreach (var invalidBounds in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0);
        Fake.OnResolve = definition =>
        {
            if (invalidBounds) definition.m_exteriorRadius = float.NaN;
            else
            {
                var location = ZoneSystem.instance.m_locationInstances[new(0, 0)];
                location.m_location = new("changed");
                ZoneSystem.instance.m_locationInstances[new(0, 0)] = location;
            }
        };
        var run = Run(MultiZoneOptions()); True(!run.Success);
        var diagnostic = run.Warnings.Single();
        True(diagnostic.Contains("stage=" + (invalidBounds ? "BuildFootprints" : "ValidateDefinitions")));
        True(diagnostic.Contains("prefab='" + (invalidBounds ? "cave" : "changed") + "'"));
        True(diagnostic.Contains("definitions=1/1"));
        if (invalidBounds) True(diagnostic.Contains("anchor=0,0"));
        Equal(0, Fake.MetadataReferences); True(!Fake.Calls.Any(call => call.Contains(".change:")));
    }
}

static void PlanningSuccessDiagnostics()
{
    Fake.AddZone(0); Fake.AddZone(5, location: "other");
    var logs = new List<string>();
    var run = Run(MultiZoneOptions(), logs: logs);
    True(run.Success); True(!run.Warnings.Any(message => message.Contains("planning stopped")));
    var planLogs = logs.Where(message => message.StartsWith("Location plan:")).ToArray();
    Equal(1, planLogs.Length); True(planLogs[0].Contains("2 definitions in "));
    True(!logs.Any(message => message.Contains("stage=") || message.Contains("prefab='")));
    Sequence([0, 5], Changed("zones")); Equal(0, Fake.MetadataReferences);
}

static void PlanningProgressDiagnostics()
{
    Fake.AddZone(0); Fake.AddZone(5, location: "other");
    var requests = 0;
    Fake.OnMetadataLoad = () => { if (++requests == 2) throw new InvalidOperationException("second prefab failure"); };
    var run = Run(MultiZoneOptions()); True(!run.Success);
    var diagnostic = run.Warnings.Single();
    True(diagnostic.Contains("prefab='other'")); True(diagnostic.Contains("definitions=1/2"));
    True(diagnostic.Contains("slowestCompleted='cave'")); Equal(0, Fake.MetadataReferences);
    True(!Fake.Calls.Any(call => call.Contains(".change:")));
    Fake.OnMetadataLoad = () => throw new InvalidOperationException("first prefab failure");
    var next = Run(MultiZoneOptions()); True(!next.Success);
    True(next.Warnings.Single().Contains("definitions=0/2"));
    True(next.Warnings.Single().Contains("slowestCompleted='<none>'"));
    Equal(0, Fake.MetadataReferences);
}

static void HiddenNeighborLocation()
{
    Fake.AddZone(0); Fake.AddZone(2, location: "other"); Fake.AddZone(3, location: null); Fake.AddZone(6, location: null);
    Fake.OnResolve = definition => { if (definition.m_prefab.Name == "other") definition.m_exteriorRadius = 100; };
    var run = Run(MultiZoneOptions()); True(run.Success); Equal(0, Fake.MetadataReferences);
    Sequence([0, 6], Changed("zones")); Equal(0, Changed("locations").Count);
    True(!GameWorld.IsGenerated(new(3, 0)));
}
static void ReplacedPlanningDefinition()
{
    MultiZoneWorld();
    Fake.OnResolve = _ =>
    {
        var instance = ZoneSystem.instance.m_locationInstances[new(0, 0)];
        instance.m_location = new("cave"); ZoneSystem.instance.m_locationInstances[new(0, 0)] = instance;
    };
    var run = Run(MultiZoneOptions());
    True(!run.Success); True(run.Errors.Any(error => error.Message.Contains("definitions changed")));
    True(!Fake.Calls.Any(call => call.Contains(".change:")));
}
static void LateMappedInstance()
{
    Fake.AddZone(0, location: null); ZoneSystem.instance.m_vegetation.Add(new("copper"));
    Fake.OnStart = kind => { if (kind == "vegetation") Fake.AddZone(0); };
    var options = MultiZoneOptions(); options.ZonesEnabled = false;
    True(Run(options).Success); Equal(0, Changed("locations").Count);
}
static RunOptions MultiZoneOptions(bool mapped = true) => new()
{
    LocationResetRadii = mapped ? new Dictionary<string, float> { ["cave"] = 40 } : new Dictionary<string, float>(),
    LocationSafeZones = 1
};
static void MultiZoneWorld()
{
    Fake.AddZone(0);
    foreach (var x in new[] { -1, 1, 4 }) Fake.AddZone(x, location: null);
    ZoneSystem.instance.m_generatedZones.Add(new(0, 1));
    ZoneSystem.instance.m_generatedZones.Add(new(0, -1));
}
static void MappedReservation()
{
    foreach (var selected in new[] { false, true })
    {
        Fake.Reset(); ZoneSystem.instance.m_vegetation.Add(new("copper")); MultiZoneWorld();
        var options = MultiZoneOptions(); options.LocationsEnabled = selected;
        if (selected) options.LocationIds = ["missing"];
        True(Run(options).Success);
        Sequence([0, 4], Changed("zones")); Equal(0, Changed("locations").Count);
        var args = Fake.Arguments["zones"];
        foreach (var sector in new[] { new Vector2s(0, 0), new(-1, 0), new(1, 0), new(0, 1), new(0, -1) })
        {
            Equal(sector == new Vector2s(0, 0), Fake.Candidates["zones"].Contains(sector));
            True(args.ZoneGroups![new(0, 0)].Contains(sector));
            True(!GameWorld.IsGenerated(sector));
            True(!args.CanResetTerrain!(sector));
            True(!args.CanDeleteObject!(new ZDO { Zone = sector }));
        }
    }
}
static void MultiZoneRouting()
{
    foreach (var mapped in new[] { false, true })
    foreach (var zones in new[] { false, true })
    {
        Fake.Reset(); ZoneSystem.instance.m_vegetation.Add(new("copper")); MultiZoneWorld();
        if (!mapped) ZoneSystem.instance.m_locationInstances[new(0, 0)].m_location.m_exteriorRadius = 40;
        var options = MultiZoneOptions(mapped); options.ZonesEnabled = zones;
        True(Run(options).Success);
        if (zones)
        {
            Sequence([0, 4], Changed("zones")); Equal(0, Changed("locations").Count); Equal(0, Changed("vegetation").Count);
            Equal(5, Fake.Arguments["zones"].ZoneGroups![new(0, 0)].Count);
        }
        else
        {
            Equal(0, Changed("zones").Count); Sequence([0], Changed("locations")); Sequence([4], Changed("vegetation"));
            Equal(5, Fake.Arguments["locations"].LocationFootprints![new(0, 0)].Zones.Count);
        }
    }
}
static void MultiZoneProtection()
{
    foreach (var guard in new[] { "base", "player", "treasure", "bounty", "invasion", "tombstone" })
    foreach (var safe in new[] { 0, 1, 2 })
    foreach (var zones in new[] { false, true })
    {
        Fake.Reset(); Fake.AddZone(0); // The neighboring sector is deliberately ungenerated.
        var options = MultiZoneOptions(); options.LocationSafeZones = safe; options.EpicLootBountyProtectionEnabled = true; options.ZonesEnabled = zones;
        switch (guard)
        {
            case "base": Fake.MarkerZones.Add(new(1, 0)); break;
            case "player": Fake.PlayerZones.Add(new(2, 0)); break; // Only the outer sector is in the player's 3x3.
            case "treasure": ZDOMan.instance.Objects.Add(new() { Zone = new(1, 0) }); break;
            case "bounty": ZDOMan.instance.Objects.Add(new() { Zone = new(1, 0), IsBounty = true }); break;
            case "invasion": ZDOMan.instance.Objects.Add(new() { Zone = new(1, 0), Prefab = "BlackIce_Core" }); break;
            case "tombstone": ZDOMan.instance.Objects.Add(new() { Zone = new(1, 0), Prefab = "Player_tombstone" }); break;
        }
        True(Run(options).Success);
        Equal(guard == "base" && safe == 0, Changed("locations").Contains(0));
        Equal(0, Changed("zones").Count);
    }
}
static void GroupPendingLoad()
{
    foreach (var loaded in new[] { false, true })
    foreach (var rootless in new[] { false, true })
    {
        Fake.Reset(); MultiZoneWorld();
        if (rootless) GameWorld.RootlessLoads.Add(new(1, 0));
        else
        {
            GameWorld.Pending.Add(new(1, 0));
            if (loaded) ZoneSystem.instance.Loaded.Add(new(1, 0));
        }
        var options = MultiZoneOptions(); options.LocationSafeZones = 0;
        True(Run(options).Success);
        Equal(loaded && !rootless, Changed("zones").Contains(0)); Equal(0, Changed("locations").Count);
        Equal(!loaded || rootless, GameWorld.IsGenerated(new(-1, 0)));
        True(!GameWorld.Pending.Contains(new(0, 0))); // No group member was forcibly loaded.
    }
}
static void GroupLateChanges()
{
    foreach (var change in new[] { "base", "player", "tombstone", "new", "replaced", "removed" })
    {
        Fake.Reset(); MultiZoneWorld(); ZoneSystem.instance.m_vegetation.Add(new("copper"));
        Fake.OnStart = kind =>
        {
            if (kind != "zones") return;
            if (change == "base") Fake.MarkerZones.Add(new(1, 0));
            else if (change == "player") Fake.PlayerZones.Add(new(2, 0));
            else if (change == "tombstone") ZDOMan.instance.Objects.Add(new() { Zone = new(1, 0), Prefab = "Player_tombstone" });
            else if (change == "removed") ZoneSystem.instance.m_locationInstances.Remove(new(0, 0));
            else Fake.AddZone(change == "new" ? 1 : 0, location: "replacement");
        };
        var options = MultiZoneOptions(); options.LocationSafeZones = 0;
        True(Run(options).Success);
        True(!Changed("zones").Contains(0)); True(GameWorld.IsGenerated(new(-1, 0)));
        Equal(change == "base" ? 1 : 0, Changed("locations").Count); Equal(0, Changed("vegetation").Count);
    }
}
static void GroupUngeneratedAnchor()
{
    foreach (var percent in new[] { 50, 100 })
    {
        Fake.Reset(); MultiZoneWorld();
        ZoneSystem.instance.m_generatedZones.Remove(new(0, 0));
        var options = MultiZoneOptions(); options.LocationsEnabled = false;
        True(Run(options, resetPercent: percent).Success);
        Sequence([4], Changed("zones")); True(GameWorld.IsGenerated(new(1, 0)));
        Equal(0, Fake.Arguments["zones"].ZoneGroups!.Count);
    }
}
static void GroupConnectedLocations()
{
    Fake.AddZone(0); Fake.AddZone(1, location: "other"); Fake.AddZone(2, location: "other2"); Fake.AddZone(10);
    ZoneSystem.instance.m_locationInstances[new(1, 0)].m_location.m_exteriorRadius = 40;
    ZoneSystem.instance.m_locationInstances[new(2, 0)].m_location.m_exteriorRadius = 40;
    Fake.MarkerZones.Add(new(3, 0));
    var options = MultiZoneOptions(); options.LocationsEnabled = false;
    True(Run(options).Success);
    Sequence([10], Changed("zones"));
    var groups = Fake.Arguments["zones"].ZoneGroups!;
    Equal(2, groups.Count); True(groups[new(0, 0)].Contains(new(3, 0)));
    True(GameWorld.IsGenerated(new(1, 0)) && GameWorld.IsGenerated(new(2, 0)));
}
static void MultiZoneLateProtection()
{
    Fake.AddZone(0);
    using var lease = MaintenanceGate.Acquire(); var completed = new List<bool>();
    var options = MultiZoneOptions(); options.ZonesEnabled = false;
    using var runner = NewRunner(options, completed);
    for (var frame = 0; !GameWorld.Pending.Contains(new(0, 0)); frame++)
    { True(frame < 20); True(runner.MoveNext()); }
    ZDOMan.instance.Objects.Add(new() { Zone = new(1, 0), Prefab = "Player_tombstone" });
    Drain(runner); Sequence([true], completed); Equal(0, Changed("locations").Count); Equal(0, GameWorld.Pending.Count);
}
static void DisjointLocationReservation()
{
    Fake.AddZone(0); Fake.AddZone(1, location: "other"); Fake.AddZone(2, location: null); Fake.AddZone(4, location: null);
    var neighbor = ZoneSystem.instance.m_locationInstances[new(1, 0)];
    neighbor.m_position = new(90, 0, 20); neighbor.m_location.m_exteriorRadius = 10;
    ZoneSystem.instance.m_locationInstances[new(1, 0)] = neighbor;
    var run = Run(MultiZoneOptions()); True(run.Success);
    Sequence([0, 4], Changed("zones")); Equal(0, Changed("locations").Count);
    True(!GameWorld.IsGenerated(new(2, 0)));
    True(!run.Warnings.Any(message => message.Contains("overlapping")));
    True(!Fake.Arguments["zones"].CanResetTerrain!(new(2, 0)));
}
static void NeighborLocationResolution()
{
    Fake.AddZone(0); Fake.AddZone(1, location: "other"); Fake.AddZone(3, location: null); Fake.AddZone(4, location: null);
    var neighbor = ZoneSystem.instance.m_locationInstances[new(1, 0)];
    neighbor.m_position = new(90, 0, 20); neighbor.m_location.m_exteriorRadius = 10;
    ZoneSystem.instance.m_locationInstances[new(1, 0)] = neighbor;
    var resolved = new List<string>();
    Fake.OnResolve = definition =>
    {
        resolved.Add(definition.m_prefab.Name);
        if (definition.m_prefab.Name == "other") definition.m_exteriorRadius = 110;
    };
    var run = Run(MultiZoneOptions()); True(run.Success);
    Sequence(new[] { "cave", "other" }, resolved);
    Sequence([0, 4], Changed("zones")); Equal(0, Changed("locations").Count);
    True(!GameWorld.IsGenerated(new(3, 0)));
    True(!Fake.Arguments["zones"].CanResetTerrain!(new(3, 0)));
}
static void MultiZoneOverlap()
{
    foreach (var zones in new[] { false, true })
    {
        Fake.Reset();
        Fake.AddZone(0); Fake.AddZone(1, location: "other"); Fake.AddZone(2, location: null); Fake.AddZone(4, location: null);
        ZoneSystem.instance.m_locationInstances[new(1, 0)].m_location.m_exteriorRadius = 40;
        var options = MultiZoneOptions(); options.ZonesEnabled = zones;
        var result = Run(options); True(result.Success);
        Sequence(zones ? new[] { 0, 4 } : [], Changed("zones")); Equal(0, Changed("locations").Count);
        Equal(!zones, result.Warnings.Any(message => message.Contains("overlapping")));
        Equal(!zones, GameWorld.IsGenerated(new(2, 0)));
    }
}
static void MultiZonePercent()
{
    foreach (var percent in new[] { 0, 20, 50, 100 })
    foreach (var zones in new[] { false, true })
    {
        Fake.Reset();
        for (var x = 0; x < 100; x += 10) Fake.AddZone(x);
        var options = MultiZoneOptions(); options.VegetationEnabled = false; options.ZonesEnabled = zones;
        True(Run(options, resetPercent: percent).Success);
        Equal(zones ? percent / 10 : 0, Changed("zones").Count); Equal(zones ? 0 : percent / 10, Changed("locations").Count);
        foreach (var x in Changed(zones ? "zones" : "locations"))
            Equal(5, zones ? Fake.Arguments["zones"].ZoneGroups![new(x, 0)].Count : Fake.Arguments["locations"].LocationFootprints![new(x, 0)].Zones.Count);
    }
}
static void GroupLocationFallback()
{
    foreach (var enabled in new[] { false, true })
    foreach (var selected in new[] { false, true })
    foreach (var safe in new[] { 0, 1, 2 })
    {
        Fake.Reset(); MultiZoneWorld(); Fake.MarkerZones.Add(new(1, 0));
        var options = MultiZoneOptions(); options.LocationsEnabled = enabled;
        options.LocationIds = selected ? ["cave"] : ["other"];
        options.LocationSafeZones = safe;
        True(Run(options).Success);
        Sequence([4], Changed("zones"));
        Equal(enabled && selected && safe == 0, Changed("locations").Contains(0));
        True(GameWorld.IsGenerated(new(-1, 0))); // Radius restoration does not reset the neighboring sector.
    }
}
static void GroupFallbackPercent()
{
    foreach (var percent in new[] { 0, 20, 50, 100 })
    foreach (var allProtected in new[] { false, true })
    {
        Fake.Reset();
        for (var x = 0; x < 100; x += 10)
        {
            Fake.AddZone(x);
            if (allProtected || x % 20 == 0) Fake.MarkerZones.Add(new(x + 1, 0));
        }
        var options = MultiZoneOptions(); options.VegetationEnabled = false; options.LocationSafeZones = 0;
        True(Run(options, resetPercent: percent).Success);
        var zones = Changed("zones"); var locations = Changed("locations");
        Equal(percent / 10, zones.Count + locations.Count);
        True(!zones.Overlaps(locations));
        True(locations.All(x => Fake.MarkerZones.Contains(new(x + 1, 0))));
        True(zones.All(x => !Fake.MarkerZones.Contains(new(x + 1, 0))));
        if (allProtected) Equal(0, zones.Count);
    }
}
static void GroupNeighborFallback()
{
    foreach (var percent in new[] { 0, 50, 100 })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.AddZone(1, location: "other"); Fake.AddZone(10);
        var neighbor = ZoneSystem.instance.m_locationInstances[new(1, 0)];
        neighbor.m_position = new(90, 0, 20); neighbor.m_location.m_exteriorRadius = 5;
        ZoneSystem.instance.m_locationInstances[new(1, 0)] = neighbor;
        Fake.MarkerZones.Add(new(1, 0)); Fake.MarkerZones.Add(new(11, 0));
        var options = MultiZoneOptions(); options.LocationIds = ["cave", "other"];
        options.LocationSafeZones = 0; options.VegetationEnabled = false;
        True(Run(options, resetPercent: percent).Success);
        Equal(0, Changed("zones").Count);
        var restored = Changed("locations");
        Equal(restored.Contains(0), restored.Contains(1));
        Equal(percent / 50, restored.Select(x => x == 1 ? 0 : x).Distinct().Count());
    }
}
static void UngeneratedOwnerFallback()
{
    foreach (var percent in new[] { 50, 100 })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.AddZone(1, location: "other");
        ZoneSystem.instance.m_generatedZones.Remove(new(0, 0));
        var owner = ZoneSystem.instance.m_locationInstances[new(0, 0)]; owner.m_placed = false;
        ZoneSystem.instance.m_locationInstances[new(0, 0)] = owner;
        var neighbor = ZoneSystem.instance.m_locationInstances[new(1, 0)];
        neighbor.m_position = new(90, 0, 20); neighbor.m_location.m_exteriorRadius = 5;
        ZoneSystem.instance.m_locationInstances[new(1, 0)] = neighbor;
        var options = MultiZoneOptions(); options.LocationIds = ["other"]; options.VegetationEnabled = false;
        True(Run(options, resetPercent: percent).Success);
        Equal(0, Changed("zones").Count); Sequence([1], Changed("locations"));
        True(!GameWorld.IsGenerated(new(0, 0)));
    }
}
static void RegeneratedGroupNoFallback()
{
    foreach (var percent in new[] { 50, 100 })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.AddZone(1, location: "other");
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        var neighbor = ZoneSystem.instance.m_locationInstances[new(1, 0)];
        neighbor.m_position = new(90, 0, 20); neighbor.m_location.m_exteriorRadius = 5;
        ZoneSystem.instance.m_locationInstances[new(1, 0)] = neighbor;
        Fake.OnStart = kind =>
        {
            if (kind != "vegetation") return;
            foreach (var x in new[] { 0, 1 })
            {
                ZoneSystem.instance.m_generatedZones.Add(new(x, 0));
                var location = ZoneSystem.instance.m_locationInstances[new(x, 0)]; location.m_placed = true;
                ZoneSystem.instance.m_locationInstances[new(x, 0)] = location;
            }
        };
        var options = MultiZoneOptions(); options.LocationIds = ["cave", "other"]; options.LocationSafeZones = 0;
        True(Run(options, resetPercent: percent).Success);
        Sequence([0], Changed("zones")); Equal(0, Changed("locations").Count);
        True(GameWorld.IsGenerated(new(0, 0)) && GameWorld.IsGenerated(new(1, 0)));
        True(ZoneSystem.instance.m_locationInstances[new(0, 0)].m_placed);
    }
}
static void FailedGroupNoFallback()
{
    Fake.AddZone(0); Fake.ThrowOnZone = "zones";
    var options = MultiZoneOptions(); options.LocationSafeZones = 0;
    var run = Run(options);
    True(!run.Success); True(run.Errors.Count > 0);
    True(!Fake.Calls.Contains("locations.create"));
}
static void MultiZoneResolvedRadius()
{
    MultiZoneWorld(); Fake.AddZone(2, location: null);
    Fake.OnResolve = definition => definition.m_exteriorRadius = 100;
    True(Run(MultiZoneOptions()).Success);
    True(!Changed("zones").Contains(2)); Equal(0, Changed("locations").Count);
    True(!GameWorld.IsGenerated(new(2, 0))); True(Fake.Arguments["zones"].ZoneGroups![new(0, 0)].Contains(new(2, 0)));
}
static void MultiZoneChangedExtent()
{
    foreach (var changeRadius in new[] { false, true })
    foreach (var zones in new[] { false, true })
    {
        Fake.Reset(); MultiZoneWorld();
        Fake.OnStart = kind =>
        {
            if (kind != (zones ? "zones" : "locations")) return;
            var location = ZoneSystem.instance.m_locationInstances[new(0, 0)];
            if (changeRadius) location.m_location.m_exteriorRadius = 60;
            else location.m_position = new(10, 0, 0);
            ZoneSystem.instance.m_locationInstances[new(0, 0)] = location;
        };
        var options = MultiZoneOptions(); options.ZonesEnabled = zones;
        True(Run(options).Success); Equal(0, Changed("locations").Count); True(!Changed("zones").Contains(0));
    }
}
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

static void ResourceExclusions()
{
    foreach (var zoneReset in new[] { false, true })
    {
        Fake.Reset();
        for (var i = 0; i < 4; i++) Fake.AddZone(i, marker: zoneReset);
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
        var options = new RunOptions { ZonesEnabled = zoneReset, VegetationIds = ["raspberry"],
            ResourceExcludedZones = [(0, 0), (2, 0)] };
        True(Run(options).Success);
        Equal(2, Fake.VegetationPasses.Count);
        foreach (var pass in Fake.VegetationPasses) Sequence([1, 3], pass.ChangedZones);
        True(Changed("locations").SetEquals([0, 1, 2, 3]));
        // No loading attempt is admitted for an excluded resource target.
        True(Fake.Candidates["vegetation"].SetEquals([new(1, 0), new(3, 0)]));
        True(Fake.Arguments["locations"].CanResetTerrain!(new(0, 0)));
        if (zoneReset) True(Fake.Arguments["zones"].CanResetTerrain!(new(0, 0)));
    }
}

static void ResourceExclusionPercent()
{
    foreach (var stage in new[] { "resources", "locations", "zones" })
    {
        Fake.Reset();
        for (var i = 0; i < 10; i++) Fake.AddZone(i);
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
        var logs = new List<string>();
        var options = new RunOptions { ZonesEnabled = stage == "zones", LocationsEnabled = stage == "locations",
            VegetationIds = ["raspberry"], ResourceExcludedZones = [(0, 0), (1, 0), (2, 0), (3, 0), (99, 0)] };
        True(Run(options, resetPercent: 50, logs: logs).Success);
        var selection = logs.Single(line => line.StartsWith("Automatic reset selection:"));
        True(selection.Contains(stage == "resources" ? "3/6 candidates" : "5/10 candidates"));
        var resourceChanges = Changed("vegetation");
        True(resourceChanges.All(x => x >= 4));
        Sequence(Fake.VegetationPasses[0].ChangedZones, Fake.VegetationPasses[1].ChangedZones);
        if (stage == "resources") Equal(3, resourceChanges.Count);
        else Equal(5, Changed(stage).Count);
    }
}

static void ResourceExclusionGuards()
{
    Fake.AddZone(0, location: null); Fake.AddZone(1, location: null); Fake.AddZone(2, location: null);
    Fake.AddZone(10, location: "fort");
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    ZDOMan.instance.Objects.Add(new() { Prefab = "custom_marker", Zone = new(2, 0) });
    var options = new RunOptions { ZonesEnabled = false, LocationsEnabled = false, VegetationIds = ["raspberry"],
        ResourceExcludedZones = [(0, 0)], AlwaysProtectedPrefabs = ["custom_marker"],
        LocationResetRadii = new Dictionary<string, float> { ["fort"] = 40 } };
    True(Run(options).Success);
    foreach (var pass in Fake.VegetationPasses)
    {
        Sequence([1], pass.ChangedZones);
        // A request originating outside an exclusion must still refuse the excluded terrain tile.
        True(!pass.Arguments.CanResetTerrain!(new(0, 0)));
        True(pass.Arguments.CanResetTerrain(new(1, 0)));
        True(!pass.Arguments.CanResetTerrain(new(2, 0)));
        True(!pass.Arguments.CanResetTerrain(new(10, 0)));
        // The same predicate is forwarded through recursive Spawned deletion by GameWorld.
        True(!pass.Arguments.CanDeleteObject!(new() { Zone = new(0, 0) }));
        True(pass.Arguments.CanDeleteObject(new() { Zone = new(1, 0) }));
        True(!pass.Arguments.CanDeleteObject(new() { Zone = new(10, 0) }));
    }
}

static void ResourceExclusionSnapshot()
{
    Fake.AddZone(0); Fake.AddZone(1);
    var coordinates = new (short X, short Z)[] { (0, 0) };
    var options = new RunOptions { ZonesEnabled = false, LocationsEnabled = false, ResourceExcludedZones = coordinates };
    using (var lease = MaintenanceGate.Acquire())
    {
        var completed = new List<bool>();
        using var runner = NewRunner(options, completed, advancePastSave: false);
        coordinates[0] = (1, 0);
        Drain(runner); Sequence([true], completed);
        Sequence([1], Changed("vegetation"));
    }
    Fake.Calls.Clear();
    ZNet.World.m_uid++;
    True(Run(options).Success);
    Sequence([0], Changed("vegetation"));
}

static void PercentageCounts()
{
    foreach (var (total, percent, expected) in new[] { (10, 20, 2), (5, 50, 3), (2, 20, 0), (0, 20, 0), (10, 0, 0), (10, 100, 10) })
    {
        Fake.Reset();
        for (var i = 0; i < total; i++) Fake.AddZone(i);
        True(Run(new() { VegetationEnabled = false, LocationsEnabled = false }, resetPercent: percent).Success);
        var changes = Fake.Calls.Where(call => call.StartsWith("zones.change:")).ToArray();
        Equal(expected, changes.Length);
        Equal(expected, changes.Distinct().Count());
        Equal(total - expected, ZoneSystem.instance.m_generatedZones.Count);
    }
}

static void PercentageSharedSupplements()
{
    for (var i = 0; i < 10; i++) Fake.AddZone(i);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    // No resource ZDOs exist, but depleted resources must still be candidates for restoration.
    True(Run(new() { ZonesEnabled = false, VegetationIds = ["raspberry"] }, resetPercent: 20).Success);
    Equal(2, Fake.VegetationPasses.Count);
    var first = Fake.VegetationPasses[0].ChangedZones;
    Equal(2, first.Count);
    Sequence(first, Fake.VegetationPasses[1].ChangedZones);
    Sequence(first.Select(zone => "locations.change:" + zone), Fake.Calls.Where(call => call.StartsWith("locations.change:")));
}

static void PercentageRetainedScope()
{
    for (var i = 0; i < 10; i++) Fake.AddZone(i, marker: i < 5);
    True(Run(new(), resetPercent: 50).Success);
    var zoneChanges = Changed("zones");
    var resourceChanges = Changed("vegetation");
    True(zoneChanges.All(zone => zone >= 5));
    True(resourceChanges.All(zone => zone < 5));
    True(resourceChanges.SetEquals(Changed("locations")));
    zoneChanges.UnionWith(resourceChanges);
    Equal(5, zoneChanges.Count); // Union is sampled once, not 50% again per stage.
}

static void PercentageLocationCandidates()
{
    for (var i = 0; i < 10; i++) Fake.AddZone(i, location: i < 5 ? "cave" : i == 5 ? "other" : null);
    var unplaced = ZoneSystem.instance.m_locationInstances[new(4, 0)];
    unplaced.m_placed = false;
    ZoneSystem.instance.m_locationInstances[new(4, 0)] = unplaced;
    // Protect one otherwise eligible cave, leaving three candidates, rounded to two at 50%.
    Fake.MarkerZones.Add(new(3, 0));
    True(Run(new() { ZonesEnabled = false, VegetationEnabled = false, LocationSafeZones = 1 }, resetPercent: 50).Success);
    var locations = Changed("locations");
    Equal(2, locations.Count);
    True(locations.All(zone => zone < 3));
}

static void PercentageInactiveResources()
{
    foreach (var ids in new[] { Array.Empty<string>(), new[] { "missing" } })
    foreach (var includeVegetation in new[] { true, false })
    {
        Fake.Reset();
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        for (var i = 0; i < 10; i++) Fake.AddZone(i, location: i < 2 ? "cave" : null);
        True(Run(new() { ZonesEnabled = false, TerrainVegetationIds = ids }, includeVegetation, 50).Success);
        Equal(1, Changed("locations").Count);
        Equal(0, Changed("vegetation").Count);
    }
}

static void PercentageLateProtection()
{
    for (var i = 0; i < 6; i++) Fake.AddZone(i);
    var protectedAfterDraw = -1;
    Fake.OnStart = kind =>
    {
        if (kind != "zones") return;
        var target = Fake.Candidates[kind].First();
        protectedAfterDraw = target.x;
        Fake.MarkerZones.Add(target);
    };
    True(Run(new() { VegetationEnabled = false, LocationsEnabled = false }, resetPercent: 50).Success);
    Equal(3, Fake.Candidates["zones"].Count);
    Equal(2, Changed("zones").Count);
    True(!Changed("zones").Contains(protectedAfterDraw));
    True(Changed("zones").All(zone => Fake.Candidates["zones"].Contains(new(zone, 0))));
    // Percentage exclusion is not terrain protection; preserve existing border repair policy.
    True(Fake.Arguments["zones"].CanResetTerrain!(new(protectedAfterDraw, 0)));
}

static void PercentageNewZones()
{
    foreach (var percent in new[] { 50, 100 })
    {
        Fake.Reset();
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
        for (var i = 0; i < 4; i++) Fake.AddZone(i);
        Fake.OnStart = kind => { if (kind == "vegetation") Fake.AddZone(100); };
        True(Run(new() { ZonesEnabled = false, VegetationIds = ["raspberry"] }, resetPercent: percent).Success);
        Equal(percent == 100 ? 4 : 2, Fake.VegetationPasses[0].ChangedZones.Count);
        Equal(percent == 100 ? 5 : 2, Fake.VegetationPasses[1].ChangedZones.Count);
        Equal(percent == 100, Changed("locations").Contains(100));
    }
}

static void PercentageAfterSave()
{
    Fake.AddZone(0);
    Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { VegetationEnabled = false, LocationsEnabled = false }, completed,
        advancePastSave: false, resetPercent: 50);
    True(runner.MoveNext());
    True(ZNet.instance.Saving);
    Fake.AddZone(1); Fake.AddZone(2); Fake.AddZone(3);
    ZNet.instance.Saving = false;
    Drain(runner);
    Sequence([true], completed);
    Equal(2, Changed("zones").Count);
}

static void PercentageGeneratedScope()
{
    Fake.AddZone(0); Fake.AddZone(1);
    ZoneSystem.instance.m_generatedZones.Remove(new(1, 0)); // Location metadata alone is not a generated zone.
    True(Run(new() { ZonesEnabled = false, VegetationEnabled = false }, resetPercent: 50).Success);
    True(Changed("locations").SetEquals([0]));
    True(Run(new() { VegetationEnabled = false, LocationsEnabled = false }).Success);
    Equal(0, ZoneSystem.instance.m_generatedZones.Count);
    Fake.Calls.Clear();
    True(Run(new() { ZonesEnabled = false, VegetationEnabled = false }, resetPercent: 50).Success);
    Equal(0, Changed("locations").Count);
    Fake.AddZone(0); // Visiting regenerates a reset zone and makes it eligible again.
    True(Run(new() { ZonesEnabled = false, VegetationEnabled = false }, resetPercent: 50).Success);
    True(Changed("locations").SetEquals([0]));
}

static HashSet<int> Changed(string stage) => Fake.Calls
    .Where(call => call.StartsWith(stage + ".change:"))
    .Select(call => int.Parse(call.Substring(call.IndexOf(':') + 1))).ToHashSet();

static void OccupiedZoneAndNextRun()
{
    Fake.AddZone(-1); Fake.AddZone(0); Fake.AddZone(1); Fake.AddZone(2);
    Fake.PlayerZones.Add(new(0, 0));
    // Departure after planning must not expose the initially occupied zone during this run.
    Fake.OnStart = kind => { if (kind == "zones") Fake.PlayerZones.Clear(); };
    var options = new RunOptions { ZoneSafeZones = 0 };
    True(Run(options).Success);
    Sequence(["zones.change:2"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    True(ZoneSystem.instance.m_generatedZones.SetEquals([new(-1, 0), new(0, 0), new(1, 0)]));
    // Player-only skips must not expand the existing base-marker retained scope.
    True(!Fake.Calls.Any(IsSupplementChange));

    Fake.Calls.Clear();
    True(Run(options).Success);
    Sequence(["zones.change:-1", "zones.change:0", "zones.change:1"],
        Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    Equal(0, ZoneSystem.instance.m_generatedZones.Count);
}

static void PlayerNeighborhood()
{
    (Vector2s PlayerZone, bool Protected)[] cases =
    [
        (new(-1, -1), true), (new(0, -1), true), (new(1, -1), true),
        (new(-1, 0), true), (new(0, 0), true), (new(1, 0), true),
        (new(-1, 1), true), (new(0, 1), true), (new(1, 1), true),
        (new(-2, 0), false), (new(2, 0), false), (new(0, -2), false), (new(0, 2), false),
        (new(-2, -2), false), (new(2, 2), false)
    ];
    foreach (var (playerZone, protectedTarget) in cases)
        foreach (var zonesEnabled in new[] { true, false })
        {
            Fake.Reset();
            Fake.AddZone(0);
            ZoneSystem.instance.m_vegetation.Add(new("copper"));
            ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
            Fake.PlayerZones.Add(playerZone);
            True(Run(new()
            {
                ZonesEnabled = zonesEnabled, ZoneSafeZones = 0,
                VegetationIds = ["raspberry"], VegetationSafeZones = 0, LocationSafeZones = 0
            }).Success);
            if (protectedTarget)
                True(!Fake.Calls.Any(call => call.StartsWith("zones.change:") || IsSupplementChange(call)),
                    $"Protected target reset with player at {playerZone}, zones={zonesEnabled}.");
            else if (zonesEnabled)
                Sequence(["zones.change:0"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
            else
                Sequence(["vegetation.change:0", "vegetation.change:0", "locations.change:0"], Fake.Calls.Where(IsSupplementChange));
        }
}

static void PlayerNeighborhoodEdges()
{
    (Vector2s Target, Vector2s PlayerZone, bool Protected)[] cases =
    [
        (new(short.MaxValue, short.MaxValue), new(short.MinValue, short.MaxValue), false),
        (new(short.MaxValue, short.MaxValue), new(short.MaxValue, short.MinValue), false),
        (new(short.MaxValue, short.MaxValue), new(short.MaxValue - 1, short.MaxValue - 1), true),
        (new(short.MinValue, short.MinValue), new(short.MaxValue, short.MinValue), false),
        (new(short.MinValue, short.MinValue), new(short.MinValue, short.MaxValue), false),
        (new(short.MinValue, short.MinValue), new(short.MinValue + 1, short.MinValue + 1), true)
    ];
    foreach (var (target, playerZone, protectedTarget) in cases)
    {
        Fake.Reset();
        ZoneSystem.instance.m_generatedZones.Add(target);
        Fake.PlayerZones.Add(playerZone);
        True(Run(new() { ZoneSafeZones = 0, VegetationEnabled = false, LocationsEnabled = false }).Success);
        Equal(protectedTarget, ZoneSystem.instance.m_generatedZones.Contains(target));
    }
}

static void PostSavePlayerSnapshot()
{
    Fake.AddZone(0); Fake.AddZone(2);
    Fake.PlayerZones.Add(new(0, 0));
    Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZoneSafeZones = 0, VegetationEnabled = false, LocationsEnabled = false },
        completed, advancePastSave: false);
    True(runner.MoveNext());
    True(ZNet.instance.Saving && !Fake.Calls.Contains("zones.create"));
    Fake.PlayerZones.Clear();
    Fake.PlayerZones.Add(new(2, 0));
    ZNet.instance.Saving = false;
    Drain(runner);
    Sequence([true], completed);
    Sequence(["zones.change:0"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    True(ZoneSystem.instance.m_generatedZones.Contains(new(2, 0)));
}

static void PlayerSnapshotSupplements()
{
    Fake.AddZone(0); Fake.AddZone(1); Fake.AddZone(2);
    ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
    Fake.PlayerZones.Add(new(0, 0));
    Fake.OnStart = kind => { if (kind == "vegetation") Fake.PlayerZones.Clear(); };
    True(Run(new() { ZonesEnabled = false, VegetationIds = ["raspberry"] }).Success);
    Equal(2, Fake.VegetationPasses.Count);
    True(Fake.VegetationPasses.All(pass => pass.Arguments.SafeZones == 0 && pass.ChangedZones.SequenceEqual([2])));
    Sequence(["locations.change:2"], Fake.Calls.Where(call => call.StartsWith("locations.change:")));
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
    Fake.PlayerZones.Add(new(2, 0));
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
    // The target is ready on the next retry, but a player is now nearby: readiness cannot bypass protection.
    ZoneSystem.instance.Loaded.Add(new(0, 0));
    Fake.PlayerZones.Add(new(1, 0));
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
            operation.Init();
            yield return operation.Execute();
        }
        var completed = new List<bool>();
        using var runner = new GuardedCoroutine(Execute(), () => true, Fake.UnexpectedErrors.Add, completed.Add);
        Drain(runner);
        operation.Cleanup();
        Sequence([true], completed);
        Sequence([new Vector2s(0, 0)], operation.Result.SelectedZones);
        Sequence([new Vector2s(0, 0)], operation.Result.SkippedZones);
        Equal(0, operation.Result.ChangedZones.Count);
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

static void SlowZoneSkip()
{
    Fake.AddZone(0);
    Fake.LoadOnPoke = false;
    var zone = new Vector2s(0, 0);
    var warnings = new List<string>();
    var tracker = new OperationTracker(null, 64, 8, warnings.Add, loadTimeoutMilliseconds: 30);
    tracker.Selected([zone]);
    tracker.MayLoad(zone);
    var attempts = 0;
    var execution = tracker.Execute([zone], target =>
    {
        attempts++;
        GameWorld.PokeZone(target);
        return false;
    });
    True(execution.MoveNext());
    Thread.Sleep(50);
    while (execution.MoveNext()) { }
    tracker.Finish();
    tracker.Cleanup();
    Equal(2, attempts);
    Equal(0, tracker.Result.FailedCount);
    Sequence([zone], tracker.Result.TimedOutZones);
    Sequence([zone], tracker.Result.SkippedZones);
    True(tracker.Result.LoadWaitSeconds >= 0.03);
    True(warnings.Single().Contains("Zone 0,0") && warnings[0].Contains("loadingObjects="));
    Equal(0, GameWorld.Pending.Count);
    Equal(1, Fake.LoadReleases);
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
    var tracker = new OperationTracker(null, 64, 8);
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
    using var lease = MaintenanceGate.Acquire();
    var candidates = new HashSet<Vector2s>([new(1, 0), new(2, 0), new(3, 0)]);
    var operation = new TrackedResetZones(_ => { }, Parameters(1), candidates);
    // Planning owns its constructor-time snapshot, even if streaming or the caller changes before Init.
    Fake.AddZone(3);
    candidates.Clear();
    candidates.Add(new(0, 0));
    operation.Init();
    Sequence([new Vector2s(1, 0)], operation.Result.SelectedZones);
    var completed = new List<bool>();
    using var runner = new GuardedCoroutine(operation.Execute(), () => true, Fake.UnexpectedErrors.Add, completed.Add);
    Drain(runner);
    operation.Cleanup();
    Sequence([true], completed);
    Sequence(["zones.change:1"], Fake.Calls.Where(call => call.StartsWith("zones.change:")));
    True(ZoneSystem.instance.m_generatedZones.SetEquals([new(0, 0), new(2, 0), new(3, 0)]));
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

static void TreasureStages()
{
    foreach (var zoneReset in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.AddZone(1); Fake.AddZone(2);
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
        ZDOMan.instance.Objects.Add(new() { Zone = new(0, 0) });
        // A target completed/removed after planning remains protected until this run ends.
        Fake.OnStart = _ => ZDOMan.instance.Objects.Clear();
        var options = new RunOptions { ZonesEnabled = zoneReset, ZoneSafeZones = 0, VegetationIds = ["raspberry"] };
        True(Run(options).Success);
        True(!Fake.Calls.Any(call => (call.StartsWith("zones.change:") || IsSupplementChange(call)) &&
            call.EndsWith(":0")));
        True(Fake.Calls.Any(call => call.EndsWith("change:1")));
        True(Fake.Calls.Any(call => call.EndsWith("change:2")));
        Fake.Calls.Clear();
        True(Run(options).Success);
        True(Fake.Calls.Any(call => call.EndsWith("change:0")));
    }
}

static void TreasureDisabled()
{
    foreach (var zoneReset in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0);
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZDOMan.instance.Objects.Add(new() { Zone = new(0, 0) });
        var options = new RunOptions
        {
            ZonesEnabled = zoneReset, ZoneSafeZones = 0, VegetationSafeZones = 0,
            LocationSafeZones = 0, EpicLootProtectionEnabled = false
        };
        True(Run(options).Success);
        True(Fake.Calls.Any(call => call.EndsWith("change:0")),
            "Disabled EpicLoot protection blocked a reset stage.");
    }
}

static void TreasureAfterSave()
{
    Fake.AddZone(0); Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZoneSafeZones = 0, VegetationEnabled = false, LocationsEnabled = false },
        completed, advancePastSave: false);
    True(runner.MoveNext());
    ZDOMan.instance.Objects.Add(new() { Zone = new(0, 0) });
    ZNet.instance.Saving = false;
    Fake.OnStart = _ => ZDOMan.instance.Objects.Clear();
    Drain(runner); Sequence([true], completed);
    True(!Fake.Calls.Contains("zones.change:0"));
}

static void TreasureDuringLoad()
{
    foreach (var resources in new[] { true, false })
    foreach (var sameSector in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); ZoneSystem.instance.m_vegetation.Add(new("copper"));
        using var lease = MaintenanceGate.Acquire();
        var completed = new List<bool>();
        using var runner = NewRunner(new() { ZonesEnabled = false, VegetationEnabled = resources, LocationsEnabled = !resources }, completed);
        True(runner.MoveNext()); True(GameWorld.Pending.Contains(new(0, 0)));
        ZDOMan.instance.Objects.Add(new() { Zone = sameSector ? new(0, 0) : new(1, 1) });
        Drain(runner); Sequence([true], completed);
        Equal(!sameSector, Fake.Calls.Any(IsSupplementChange));
        Equal(0, GameWorld.Pending.Count);
    }
}

static void TreasureNeighbors()
{
    foreach (var kind in new[] { "terrain", "group", "exterior", "interior" })
    {
        Fake.Reset(); Fake.AddZone(0); ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZDOMan.instance.Objects.Add(new() { Zone = new(1, 0) });
        var location = ZoneSystem.instance.m_locationInstances[new(0, 0)].m_location;
        if (kind == "group") ZoneSystem.instance.m_vegetation[0].m_groupRadius = 100;
        if (kind == "exterior") location.m_exteriorRadius = 128;
        if (kind == "interior") location.m_interiorRadius = 128;
        True(Run(new()
        {
            ZonesEnabled = false, VegetationEnabled = kind is "terrain" or "group",
            LocationsEnabled = kind is "exterior" or "interior", VegetationTerrainRadius = kind == "terrain" ? 128 : 0
        }).Success);
        Equal(kind != "exterior", Fake.Calls.Any(IsSupplementChange));
    }
}

static void BountyStages()
{
    foreach (var zoneReset in new[] { true, false })
    foreach (var prefab in new[] { "EL_SpawnController", "Troll", "Greydwarf" })
    foreach (var treasure in new[] { true, false })
    foreach (var bounty in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.AddZone(1);
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
        // A leftover treasure payload on a bounty controller must not join the treasure policy.
        ZDOMan.instance.Objects.Add(new() { Zone = new(0, 0), Prefab = prefab, IsBounty = true });
        True(Run(new()
        {
            ZonesEnabled = zoneReset, ZoneSafeZones = 0, VegetationIds = ["raspberry"],
            EpicLootProtectionEnabled = treasure, EpicLootBountyProtectionEnabled = bounty
        }).Success);
        Equal(!bounty, Fake.Calls.Any(call => call.EndsWith("change:0")));
        True(Fake.Calls.Any(call => call.EndsWith("change:1")));
    }
}

static void BountyDuringLoad()
{
    foreach (var resources in new[] { true, false })
    foreach (var sameSector in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); ZoneSystem.instance.m_vegetation.Add(new("copper"));
        using var lease = MaintenanceGate.Acquire();
        var completed = new List<bool>();
        using var runner = NewRunner(new()
        {
            ZonesEnabled = false, VegetationEnabled = resources, LocationsEnabled = !resources,
            EpicLootProtectionEnabled = false, EpicLootBountyProtectionEnabled = true
        }, completed);
        True(runner.MoveNext()); True(GameWorld.Pending.Contains(new(0, 0)));
        ZDOMan.instance.Objects.Add(new() { Zone = sameSector ? new(0, 0) : new(1, 1), Prefab = "Greydwarf", IsBounty = true });
        Drain(runner); Sequence([true], completed);
        Equal(!sameSector, Fake.Calls.Any(IsSupplementChange));
        Equal(0, GameWorld.Pending.Count);
    }
}

static void InvasionStages()
{
    foreach (var zoneReset in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.AddZone(2); Fake.AddZone(3);
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
        PersistentEventSystem.instance.m_activePersistentEvents.list.Add(new() { position = new(), radius = 100 });
        ZDOMan.instance.Objects.Add(new() { Prefab = "BlackIce_Core", Zone = new(0, 0) });
        ZDOMan.instance.Objects.Add(new() { Prefab = "BlackIce_Core_outer", Zone = new(2, 0) });
        Fake.OnStart = _ => ZDOMan.instance.Objects.Clear();
        var options = new RunOptions
        {
            ZonesEnabled = zoneReset, ZoneSafeZones = 0, VegetationSafeZones = 0, LocationSafeZones = 0,
            VegetationIds = ["raspberry"], EpicLootProtectionEnabled = false, EpicLootBountyProtectionEnabled = false
        };
        True(Run(options).Success);
        True(!Fake.Calls.Any(call => call.EndsWith("change:0") || call.EndsWith("change:2")));
        True(Fake.Calls.Any(call => call.EndsWith("change:3")));
        foreach (var args in Fake.Arguments.Values)
        {
            True(args.CanResetTerrain != null);
            True(!args.CanResetTerrain!(new(2, 0))); // Removed ice remains protected through cleanup.
            True(args.CanResetTerrain(new(3, 0)));
        }
        Fake.Calls.Clear();
        True(Run(options).Success);
        True(Fake.Calls.Any(call => call.EndsWith("change:0"))); // Lingering event metadata does not protect empty zones.
        True(Fake.Calls.Any(call => call.EndsWith("change:2")));
    }
}

static void InvasionAfterSave()
{
    Fake.AddZone(0); Fake.AddZone(1); Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    using var runner = NewRunner(new() { ZoneSafeZones = 0, VegetationEnabled = false, LocationsEnabled = false },
        completed, advancePastSave: false);
    True(runner.MoveNext());
    ZDOMan.instance.Objects.Add(new() { Prefab = "BlackIce_Core", Zone = new(0, 0) });
    ZNet.instance.Saving = false;
    Fake.OnStart = _ => ZDOMan.instance.Objects.Clear();
    Drain(runner); Sequence([true], completed);
    True(!Fake.Calls.Contains("zones.change:0"));
    True(Fake.Calls.Contains("zones.change:1"));
    True(!Fake.Arguments["zones"].CanResetTerrain!(new(0, 0)));
    True(Fake.Arguments["zones"].CanResetTerrain!(new(1, 0)));
}

static void InvasionDuringLoad()
{
    foreach (var resources in new[] { true, false })
    foreach (var intersects in new[] { true, false })
    foreach (var prefab in new[] { "BlackIce_Core", "BlackIce_Core_outer" })
    {
        Fake.Reset(); Fake.AddZone(0); ZoneSystem.instance.m_vegetation.Add(new("copper"));
        using var lease = MaintenanceGate.Acquire();
        var completed = new List<bool>();
        using var runner = NewRunner(new() { ZonesEnabled = false, VegetationEnabled = resources, LocationsEnabled = !resources }, completed);
        True(runner.MoveNext()); True(GameWorld.Pending.Contains(new(0, 0)));
        // A new objective cancels only its own zone, including the terrain reset group.
        ZDOMan.instance.Objects.Add(new() { Prefab = prefab, Zone = new(intersects ? 0 : 1, 0) });
        Drain(runner); Sequence([true], completed);
        Equal(!intersects, Fake.Calls.Any(IsSupplementChange));
        Equal(0, GameWorld.Pending.Count);
    }
}

static void InvasionUnavailable()
{
    Fake.AddZone(0); Fake.AddZone(1);
    PersistentEventSystem.instance = null!;
    ZDOMan.instance.Objects.Add(new() { Prefab = "BlackIce_Core", Zone = new(0, 0) });
    var result = Run(new() { ZoneSafeZones = 0 });
    True(result.Success); Equal(0, result.Errors.Count);
    True(!Fake.Calls.Any(call => call.EndsWith("change:0")));
    True(Fake.Calls.Contains("zones.change:1"));
}

static ZDO AddPov(string prefab, int sector, string? uid = null, string key = "pt_blight_zone")
{
    var zdo = new ZDO { Prefab = prefab, Zone = new(sector, 0), HasTreasure = false };
    if (uid != null) zdo.Strings[key.GetStableHashCode()] = uid;
    ZDOMan.instance.Objects.Add(zdo);
    return zdo;
}

static void PovStages()
{
    foreach (var zoneReset in new[] { true, false })
    {
        Fake.Reset();
        for (var i = 0; i < 7; i++) Fake.AddZone(i, marker: !zoneReset);
        ZoneSystem.instance.m_vegetation.Add(new("copper"));
        AddPov("PT_WardenAltar", 0); AddPov("PT_PowerAltar", 1);
        var controller = AddPov("PT_CorruptionZone", 2, "active", "pt_blight_uid");
        AddPov("PT_BlightAltar", 3, "active", "pt_blight_uid"); AddPov("PT_RuneMonolith", 4, "active");
        AddPov("PT_AzuriteVein", 5, "orphan");
        Fake.OnStart = _ => controller.Ints["pt_blight_state".GetStableHashCode()] = 3;
        var options = new RunOptions { ZonesEnabled = zoneReset, ZoneSafeZones = 0, VegetationSafeZones = 0,
            LocationSafeZones = 0, AlwaysProtectedPrefabs = [], EpicLootProtectionEnabled = false };
        True(Run(options).Success);
        for (var i = 0; i < 5; i++) True(!Fake.Calls.Any(call => call.EndsWith("change:" + i)));
        True(Fake.Calls.Any(call => call.EndsWith("change:5")));
        True(Fake.Calls.Any(call => call.EndsWith("change:6")));
        foreach (var args in Fake.Arguments.Values)
        {
            for (var i = 0; i < 5; i++) True(!args.CanResetTerrain!(new(i, 0)));
            True(args.CanResetTerrain!(new(6, 0)));
            True(args.AlwaysProtected!.IsProtectedObject(ZDOMan.instance.Objects.First(z => z.Prefab == "PT_BlightAltar")));
        }
    }
}

static void PovIndexCost()
{
    for (var i = 0; i < 500; i++) AddPov("PT_RuneMonolith", i, "orphan-" + i);
    var pov = new PathOfValheimProtection();
    var before = GameWorld.AllSnapshotCount; pov.CaptureControllers();
    foreach (var zdo in ZDOMan.instance.Objects) True(!pov.IsProtectedObject(zdo));
    Equal(before + 1, GameWorld.AllSnapshotCount);
    UnityEngine.Time.frameCount++;
    foreach (var zdo in ZDOMan.instance.Objects) True(!pov.IsProtectedObject(zdo));
    Equal(before + 2, GameWorld.AllSnapshotCount);
    AddPov("PT_CorruptionZone", 900, "orphan-0", "pt_blight_uid");
    UnityEngine.Time.frameCount++;
    True(pov.IsProtectedObject(ZDOMan.instance.Objects[0]));
    Equal(before + 3, GameWorld.AllSnapshotCount);
}

static void PovOptions()
{
    foreach (var altars in new[] { true, false })
    foreach (var blight in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); Fake.AddZone(1);
        AddPov("PT_WardenAltar", 0); AddPov("PT_CorruptionZone", 1, "active", "pt_blight_uid");
        var options = new RunOptions { ZoneSafeZones = 1, VegetationEnabled = false, LocationsEnabled = false,
            AlwaysProtectedPrefabs = [], PathOfValheimAltarProtectionEnabled = altars, PathOfValheimBlightProtectionEnabled = blight };
        True(Run(options).Success);
        Equal(!altars, Changed("zones").Contains(0)); Equal(!blight, Changed("zones").Contains(1));
    }
    Fake.Reset();
    var controller = AddPov("PT_CorruptionZone", 0, "active", "pt_blight_uid");
    var disabled = new PathOfValheimProtection(false, false);
    var scans = GameWorld.AllSnapshotCount;
    disabled.CaptureControllers(); True(!disabled.IsProtectedObject(controller));
    Equal(scans, GameWorld.AllSnapshotCount);
}

static void PovArrival()
{
    foreach (var resources in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); ZoneSystem.instance.m_vegetation.Add(new("copper"));
        using var lease = MaintenanceGate.Acquire(); var completed = new List<bool>();
        using var runner = NewRunner(new() { ZonesEnabled = false, VegetationEnabled = resources,
            LocationsEnabled = !resources, AlwaysProtectedPrefabs = [] }, completed);
        True(runner.MoveNext()); True(GameWorld.Pending.Contains(new(0, 0)));
        AddPov("PT_BlightAltar", 0, "late", "pt_blight_uid");
        AddPov("PT_CorruptionZone", 10, "late", "pt_blight_uid");
        UnityEngine.Time.frameCount++;
        Drain(runner); Sequence([true], completed);
        True(!Fake.Calls.Any(IsSupplementChange)); Equal(0, GameWorld.Pending.Count);
    }
    Fake.Reset(); Fake.AddZone(0); Fake.AddZone(1); Fake.HoldSave = true;
    using var saveLease = MaintenanceGate.Acquire(); var finished = new List<bool>();
    using var saveRunner = NewRunner(new() { VegetationEnabled = false, LocationsEnabled = false,
        AlwaysProtectedPrefabs = [] }, finished, advancePastSave: false);
    True(saveRunner.MoveNext()); AddPov("PT_PowerAltar", 0); ZNet.instance.Saving = false;
    Fake.OnStart = _ => ZDOMan.instance.Objects.Clear();
    Drain(saveRunner); Sequence([true], finished);
    True(!Fake.Calls.Contains("zones.change:0")); True(Fake.Calls.Contains("zones.change:1"));
}

static void AlwaysProtectedStages()
{
    foreach (var zoneReset in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0, marker: true); Fake.AddZone(1, marker: true); Fake.AddZone(4);
        ZoneSystem.instance.m_vegetation.Add(new("copper")); ZoneSystem.instance.m_vegetation.Add(new("raspberry"));
        ZDOMan.instance.Objects.Add(new() { Prefab = "Player_tombstone", Zone = new(0, 0) });
        Fake.OnStart = _ => ZDOMan.instance.Objects.Clear();
        var options = new RunOptions
        {
            ZonesEnabled = zoneReset, ZoneSafeZones = 1, VegetationSafeZones = 0, LocationSafeZones = 0,
            VegetationIds = ["raspberry"], PieceBlacklist = ["Player_tombstone"],
            EpicLootProtectionEnabled = false, EpicLootBountyProtectionEnabled = false
        };
        True(Run(options).Success);
        True(!Fake.Calls.Any(call => call.EndsWith("change:0")));
        True(Fake.Calls.Contains("locations.change:1"));
        True(Fake.VegetationPasses.All(pass => pass.ChangedZones.Contains(1) && !pass.ChangedZones.Contains(0)));
        foreach (var args in Fake.Arguments.Values)
        {
            True(args.AlwaysProtected != null && !args.CanResetTerrain!(new(0, 0)));
            True(args.CanResetTerrain!(new(1, 0))); // Ordinary base-marker terrain policy is unchanged.
        }
        Fake.Calls.Clear(); True(Run(options).Success);
        True(Fake.Calls.Contains("locations.change:0"));
    }
}

static void AlwaysProtectedDuringLoad()
{
    foreach (var resources in new[] { true, false })
    foreach (var sameZone in new[] { true, false })
    {
        Fake.Reset(); Fake.AddZone(0); ZoneSystem.instance.m_vegetation.Add(new("copper"));
        using var lease = MaintenanceGate.Acquire(); var completed = new List<bool>();
        using var runner = NewRunner(new()
        {
            ZonesEnabled = false, VegetationEnabled = resources, LocationsEnabled = !resources,
            AlwaysProtectedPrefabs = ["custom_marker"]
        }, completed);
        True(runner.MoveNext()); True(GameWorld.Pending.Contains(new(0, 0)));
        ZDOMan.instance.Objects.Add(new() { Prefab = "custom_marker", Zone = sameZone ? new(0, 0) : new(1, 0) });
        Drain(runner); Sequence([true], completed);
        Equal(!sameZone, Fake.Calls.Any(IsSupplementChange)); Equal(0, GameWorld.Pending.Count);
        var args = Fake.Arguments[resources ? "vegetation" : "locations"];
        True(!args.CanResetTerrain!(sameZone ? new(0, 0) : new(1, 0))); // New neighboring object also blocks terrain writes.
    }
}

static void AlwaysProtectedAfterSave()
{
    Fake.AddZone(0); Fake.HoldSave = true;
    using var lease = MaintenanceGate.Acquire(); var completed = new List<bool>();
    using var runner = NewRunner(new() { VegetationEnabled = false, LocationsEnabled = false }, completed, advancePastSave: false);
    True(runner.MoveNext());
    ZDOMan.instance.Objects.Add(new() { Prefab = "Player_tombstone", Zone = new(0, 0) });
    ZNet.instance.Saving = false;
    Fake.OnStart = _ => ZDOMan.instance.Objects.Clear();
    Drain(runner); Sequence([true], completed);
    True(!Fake.Calls.Contains("zones.change:0"));
}

static (bool Success, List<Exception> Errors, List<string> Warnings) Run(RunOptions options, bool includeVegetation = true, int resetPercent = 100,
    List<string>? logs = null)
{
    using var lease = MaintenanceGate.Acquire();
    var completed = new List<bool>();
    var errors = new List<Exception>();
    var warnings = new List<string>();
    using var runner = new GuardedCoroutine(new MaintenancePipeline(options, includeVegetation, message => logs?.Add(message), warnings.Add, resetPercent).Run(),
        () => true, errors.Add, completed.Add);
    Drain(runner);
    Equal(1, completed.Count);
    return (completed[0], errors, warnings);
}

static GuardedCoroutine NewRunner(RunOptions options, List<bool> completed, bool advancePastSave = true, int resetPercent = 100)
{
    var runner = new GuardedCoroutine(new MaintenancePipeline(options, true, _ => { }, _ => { }, resetPercent).Run(), () => true,
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

