using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FreshWorld.Configuration;
using UnityEngine;
using FreshWorld.Engine;

namespace FreshWorld.Backend;

/// <summary>
/// Runs fresh, sequential host operations. The owner supplies
/// the gate lease and a coroutine runner that flattens nested enumerators and disposes them on failure.
/// </summary>
internal sealed class MaintenancePipeline
{
    private readonly RunOptions _options;
    private readonly bool _includeVegetation;
    private readonly int _resetPercent;
    private readonly string _runId;
    private HashSet<Vector2s>? _selectedZones;
    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly ZNet _network;
    private readonly ZoneSystem _zones;
    private readonly long _worldUid;
    private readonly HashSet<Vector2s> _playerZones = new();
    private readonly HashSet<Vector2s> _resourceExcludedZones;
    private readonly EpicLootProtection _epicLoot;
    private readonly JotunInvasionProtection _invasions = new();
    private readonly PathOfValheimProtection _pathOfValheim;
    private readonly AlwaysProtectedObjects _alwaysProtected;
    private ITrackedOperation? _activeOperation;
    private readonly Dictionary<Vector2s, LocationFootprint> _locationFootprints = new();
    private readonly HashSet<Vector2s> _managedLocations = new();
    private readonly HashSet<Vector2s> _reservedLocationZones = new();
    private readonly HashSet<Vector2s> _overlappingLocations = new();
    private readonly Dictionary<Vector2s, HashSet<Vector2s>> _locationZoneGroups = new();
    private readonly Dictionary<Vector2s, Vector2s> _locationGroupAnchors = new();
    private readonly HashSet<Vector2s> _completedZoneResets = new();
    private readonly Dictionary<ZoneSystem.ZoneLocation, LocationMetadataFallback> _metadataFallbacks = new();
    private readonly HashSet<string> _skippedLocationPrefabs = new(StringComparer.Ordinal);
    private readonly HashSet<Vector2s> _planningExcludedZones = new();
    private readonly HashSet<Vector2s> _planningExcludedAnchors = new();
    private int _skippedLocationInstances;

    public string? PlanningExclusionSummary => _skippedLocationPrefabs.Count == 0 ? null :
        $"{_skippedLocationPrefabs.Count} timed-out location prefab types, {_skippedLocationInstances} instances and {_planningExcludedZones.Count} sectors excluded for this run; eligible again on the next run.";

    public MaintenancePipeline(RunOptions options, bool includeVegetation, Action<string> log, Action<string> warn,
        int resetPercent = 100, string runId = "unknown")
    {
        if (resetPercent < 0 || resetPercent > 100) throw new ArgumentOutOfRangeException(nameof(resetPercent));
        _resetPercent = resetPercent;
        _runId = runId;
        _options = options;
        _resourceExcludedZones = new HashSet<Vector2s>(options.ResourceExcludedZones.Select(zone => new Vector2s(zone.X, zone.Z)));
        _pathOfValheim = new PathOfValheimProtection(options.PathOfValheimAltarProtectionEnabled, options.PathOfValheimBlightProtectionEnabled);
        _alwaysProtected = new AlwaysProtectedObjects(options.AlwaysProtectedPrefabs,
            options.PathOfValheimAltarProtectionEnabled || options.PathOfValheimBlightProtectionEnabled
                ? _pathOfValheim.IsProtectedObject : null);
        _epicLoot = new EpicLootProtection(options.EpicLootProtectionEnabled, options.EpicLootBountyProtectionEnabled);
        _includeVegetation = includeVegetation;
        _log = log;
        _warn = warn;
        _network = ZNet.instance;
        _zones = ZoneSystem.instance;
        _worldUid = _network.GetWorldUID();
    }

    public IEnumerator Run()
    {
        RequireWorld();
        if (!MaintenanceGate.IsActive)
            throw new InvalidOperationException("FreshWorld requires an exclusive maintenance lease.");
        try
        {
            yield return WaitUntilUnpaused();
            BaseProtection.Configure(_options.PieceBlacklist, _options.ProtectedObjects);
            yield return MaintenanceSave.Run(_network, _zones, RequireWorld, _log, "before maintenance", _options.SaveTimeoutSeconds);
            yield return PlanLocations();

            RequireWorld();
            _playerZones.Clear();
            GameWorld.CollectPlayerZones(_playerZones);
            var generated = GameWorld.GeneratedSetSnapshot();
            var protectedZones = ProtectedSnapshot(_options.ZoneSafeZones, generated);
            var epicLootZones = _epicLoot.Capture();
            var invasionZones = _invasions.Capture();
            _pathOfValheim.CaptureControllers();
            var alwaysProtectedZones = _alwaysProtected.Capture();
            _log($"Maintenance plan: {generated.Count} generated zones; {protectedZones.Count} protected by base markers; {_playerZones.Count} observed player zones with 3x3 protection; {epicLootZones} EpicLoot zones with 1x1 protection; {invasionZones} Jotun objective zones with 1x1 protection; {alwaysProtectedZones} zones protected by AlwaysProtectedPrefabs or Path of Valheiman objectives.");

            // The default and manual path retain the existing live supplemental scope.
            if (_resetPercent < 100)
                _selectedZones = SelectResetZones(generated, protectedZones);

            if (_options.ZonesEnabled)
            {
                var args = Parameters(_options.ZoneSafeZones);
                args.ZoneGroups = _locationZoneGroups;
                // Retain the initial protection set, and recheck the current set as the batch advances.
                var candidates = _selectedZones == null ? generated : new HashSet<Vector2s>(generated);
                if (_selectedZones != null) candidates.IntersectWith(_selectedZones);
                candidates.ExceptWith(_reservedLocationZones);
                candidates.UnionWith(_locationZoneGroups.Keys.Where(anchor => _selectedZones == null || _selectedZones.Contains(anchor)));
                var operation = new TrackedResetZones(_log, args, candidates,
                    zone => _locationZoneGroups.TryGetValue(zone, out var group)
                        ? CanResetZoneGroup(zone, group, protectedZones)
                        : CanResetZone(zone, _options.ZoneSafeZones, protectedZones),
                    maxZonesPerFrame: _options.MaxZonesPerFrame,
                    frameBudgetMilliseconds: _options.FrameBudgetMilliseconds, warn: _warn);
                yield return Execute("Zone reset", operation);
                _completedZoneResets.UnionWith(operation.Result.ChangedZones);
            }

            if (_includeVegetation && _options.VegetationEnabled)
            {
                yield return WaitUntilUnpaused();
                RequireWorld();
                var registry = _zones.m_vegetation.Where(vegetation => vegetation.m_prefab != null)
                    .Select(vegetation => vegetation.m_prefab.name).ToArray();
                var terrainIds = ResolveIds("terrain vegetation", _options.TerrainVegetationIds, registry);
                var vegetationIds = ResolveIds("vegetation", _options.VegetationIds, registry);
                // A duplicated prefab follows the terrain group's policy and is regenerated only once.
                vegetationIds.ExceptWith(terrainIds);
                yield return ResetVegetationGroup("Vegetation and terrain reset", terrainIds,
                    _options.VegetationTerrainRadius, protectedZones);
                if (_options.VegetationTerrainRadius > 0 && terrainIds.Count > 0 && vegetationIds.Count > 0)
                {
                    // TerrainComp.Update and Heightmap.LateUpdate must finish before vegetation reads the restored ground.
                    // Two boundaries leave one complete frame between passes regardless of this plugin's Update order.
                    yield return null;
                    yield return null;
                }
                yield return ResetVegetationGroup("Vegetation reset", vegetationIds, 0, protectedZones);
            }

            if (_options.LocationsEnabled)
            {
                yield return WaitUntilUnpaused();
                RequireWorld();
                var ids = ResolveIds("location", _options.LocationIds,
                    _zones.m_locations.Where(location => location != null && NativePlacement.IsValidLocationPrefab(location))
                        .Select(location => location.m_prefab.Name));
                if (ids.Count > 0)
                {
                    var candidates = LocationCandidates(protectedZones);
                    var safeZones = _options.LocationSafeZones;
                    var args = Parameters(safeZones);
                    args.LocationFootprints = _locationFootprints;
                    args.CanResetTerrain = CanResetTerrain;
                    args.CanDeleteObject = zdo => !_planningExcludedZones.Contains(ZoneSystem.GetZone(zdo.GetPosition()));
                    if (safeZones == 0)
                        _warn("LocationSafeZones=0: configured locations may remove player pieces inside their reset area.");
                    yield return Execute("Location reset", new TrackedRegenerateLocations(_log, ids, args,
                          candidates, CanResetLocation,
                          maxZonesPerFrame: _options.MaxZonesPerFrame,
                          frameBudgetMilliseconds: _options.FrameBudgetMilliseconds, warn: _warn));
                }
            }

            // Subsequent persistence is owned by the game's normal saves, not another forced save.
        }
        finally
        {
            try
            {
                _activeOperation?.Cleanup();
            }
            finally
            {
                _activeOperation = null;
                if (IsSameWorld()) InvalidateCaches();
            }
        }
    }

    private IEnumerator ResetVegetationGroup(string name, HashSet<string> ids, float terrainRadius,
        HashSet<Vector2s> protectedZones)
    {
        if (ids.Count == 0) yield break;
        yield return WaitUntilUnpaused();
        RequireWorld();
        var candidates = SupplementCandidates(protectedZones);
        candidates.ExceptWith(_resourceExcludedZones);
        var args = Parameters(_options.VegetationSafeZones);
        // Keep this policy local to resources: zone and location resets remain independent.
        // Compose the existing guards so reserved locations and absolute protections still apply.
        var canResetTerrain = args.CanResetTerrain;
        var canDeleteObject = args.CanDeleteObject;
        args.CanResetTerrain = zone => !_resourceExcludedZones.Contains(zone) && (canResetTerrain == null || canResetTerrain(zone));
        args.CanDeleteObject = zdo => !_resourceExcludedZones.Contains(ZoneSystem.GetZone(zdo.GetPosition())) &&
            (canDeleteObject == null || canDeleteObject(zdo));
        // Zero also disables the engine's temporary unmodified-ground generation override.
        args.TerrainReset = terrainRadius;
        yield return Execute(name, new TrackedResetVegetation(_log, ids, args,
            candidates, CanResetResourceZone,
            maxZonesPerFrame: _options.MaxZonesPerFrame,
            frameBudgetMilliseconds: _options.FrameBudgetMilliseconds, warn: _warn));
    }

    private IEnumerator Execute(string name, ITrackedOperation operation)
    {
        ValidatePlanningExclusions(force: true);
        RequireWorld();
        // Refresh even when base protection filters out every target in a stage.
        GameWorld.CollectPlayerZones(_playerZones);
        _activeOperation = operation;
        var elapsed = Stopwatch.StartNew();
        try
        {
            operation.Init();
            _log($"{name}: selected {operation.Result.SelectedZones.Count} zones.");
            yield return operation.Execute();
            var result = operation.Result;
            if (!result.Finished || result.FailedCount > 0)
                throw new InvalidOperationException($"{name} did not finish successfully ({result.FailedCount} failed zones).");
            _log($"{name}: {result.ChangedZones.Count} changed, {result.SkippedZones.Count} skipped " +
                $"({result.TimedOutZones.Count} timed out); {elapsed.Elapsed.TotalSeconds:F1}s total, " +
                $"{result.LoadWaitSeconds:F1}s waiting for zone or asset loading.");
        }
        finally
        {
            try
            {
                List<Exception>? cleanupErrors = null;
                try { operation.Cleanup(); }
                catch (Exception error) { (cleanupErrors ??= new()).Add(error); }
                try
                {
                    if (!operation.Result.Finished && IsSameWorld()) GameWorld.RecalculateTerrain();
                }
                catch (Exception error) { (cleanupErrors ??= new()).Add(error); }
                if (cleanupErrors != null)
                    throw new AggregateException("Could not finish maintenance cleanup.", cleanupErrors);
            }
            finally
            {
                _activeOperation = null;
                if (IsSameWorld()) InvalidateCaches();
            }
        }
    }

    private HashSet<Vector2s> SupplementCandidates(HashSet<Vector2s> protectedZones)
    {
        var current = GameWorld.GeneratedSetSnapshot();
        // After zone reset, supplement only the initially protected zones that still exist.
        // With zone reset disabled, selected resources/locations remain independently useful world-wide.
        if (_options.ZonesEnabled)
            current.IntersectWith(protectedZones);
        if (_selectedZones != null) current.IntersectWith(_selectedZones);
        current.ExceptWith(_reservedLocationZones);
        current.ExceptWith(_planningExcludedZones);
        return current;
    }

    private HashSet<Vector2s> LocationCandidates(HashSet<Vector2s> protectedZones)
    {
        var current = SupplementCandidates(protectedZones);
        IEnumerable<Vector2s> managed = _options.ZonesEnabled ? _locationGroupAnchors.Keys : _managedLocations;
        current.UnionWith(managed.Where(anchor => GameWorld.IsGenerated(anchor) &&
            (_selectedZones == null || _selectedZones.Contains(LocationSelectionKey(anchor)))));
        current.ExceptWith(_completedZoneResets);
        current.ExceptWith(_planningExcludedAnchors);
        current.ExceptWith(_planningExcludedZones);
        return current;
    }

    private Vector2s LocationSelectionKey(Vector2s anchor) =>
        _locationGroupAnchors.TryGetValue(anchor, out var representative) ? representative : anchor;

    private IEnumerator PlanLocations()
    {
        var overrides = _options.LocationResetRadii;
        var managedIds = new HashSet<string>(overrides.Keys, StringComparer.Ordinal);
        if (_options.LocationsEnabled) managedIds.UnionWith(_options.LocationIds);
        if (managedIds.Count == 0) yield break;

        var progress = new LocationPlanningProgress(_zones.m_locationInstances.Count);
        var completed = false;
        try
        {
            // Resolve every type represented in this world before comparing bounds. An unresolved
            // custom location outside the apparent neighborhood can still extend into it. Request
            // just its prefab, not every possible dungeon room, and release our reference each time.
            var definitions = _zones.m_locationInstances.Values
                .Where(location => location.m_location != null && NativePlacement.IsValidLocationPrefab(location.m_location))
                .Select(location => location.m_location).Distinct().ToArray();
            progress.Definitions = definitions.Length;
            if (!definitions.Any(definition => managedIds.Contains(definition.m_prefab.Name)))
            {
                completed = true;
                yield break;
            }
            var resolved = new Dictionary<ZoneSystem.ZoneLocation, string>();
            var frameTime = Stopwatch.StartNew();
            progress.Enter(PlanningStage.BetweenPrefabs);
            foreach (var definition in definitions)
            {
                if (frameTime.Elapsed.TotalMilliseconds >= _options.FrameBudgetMilliseconds)
                {
                    yield return null;
                    frameTime.Restart();
                }
                var reference = definition.m_prefab;
                var id = reference.Name;
                var acquired = false;
                var metadataResolved = false;
                var skipped = false;
                var registeredRadius = definition.m_exteriorRadius;
                var waiting = Stopwatch.StartNew();
                progress.BeginPrefab(id);
                try
                {
                    if (reference.IsValid)
                    {
                        // Native LoadAsync increments the reference before invoking callbacks,
                        // including callbacks that can run synchronously and throw.
                        acquired = true;
                        reference.LoadAsync();
                    }
                    progress.Readiness = acquired ? "IsLoaded" : "CanSpawnLocation";
                    progress.Enter(PlanningStage.WaitForPrefab);
                    while (true)
                    {
                        yield return WaitUntilUnpaused(waiting, progress.Timer);
                        RequireWorld();
                        // Named mod references may instead provide their own native readiness hook.
                        var ready = acquired ? reference.IsLoaded : NativePlacement.CanSpawnLocation(_zones, definition);
                        progress.LastReady = ready;
                        if (ready) break;
                        var elapsed = waiting.Elapsed.TotalSeconds;
                        if (acquired) progress.SampleLoading(definition, elapsed, force: elapsed >= 30);
                        // Each prefab has its own deadline; completed loads do not consume later prefabs' time.
                        if (elapsed >= 30)
                        {
                            var reason = "named reference has no verified serialized bounds";
                            LocationMetadataFallback? fallback = null;
                            if (!acquired || !LocationMetadataFallback.TryCapture(_zones, definition, registeredRadius,
                                    out fallback, out reason))
                                throw new TimeoutException("Location metadata did not load before planning: " + id +
                                    ". Cannot safely exclude its extent: " + reason);
                            _metadataFallbacks.Add(definition, fallback!);
                            _skippedLocationPrefabs.Add(id);
                            skipped = true;
                            _warn(FormattableString.Invariant($"Location planning timeout: prefab='{id}'; stage=WaitForPrefab; waited={waiting.Elapsed.TotalSeconds:F2}s; registeredRadius={registeredRadius:F2}m; {progress.LoadDiagnostics}. Its instances will be excluded with at least 3x3 protection; planning continues."));
                            break;
                        }
                        yield return null;
                    }
                    progress.Enter(PlanningStage.ResolveAsset);
                    if (acquired && !skipped) _ = reference.Asset; // Never force a timed-out asset to load synchronously.
                    resolved[definition] = id;
                    metadataResolved = true;
                }
                finally
                {
                    // Capture the original stop before cleanup can change its stage or throw.
                    // Disposal also reaches this path, so do not label every stop as a failure.
                    if (!metadataResolved) progress.ReportStopped(_warn);
                    if (acquired)
                    {
                        if (metadataResolved) progress.Enter(PlanningStage.ReleasePrefab);
                        reference.Release();
                    }
                }
                progress.CompletePrefab(skipped);
            }
            progress.Enter(PlanningStage.ValidateDefinitions);
            progress.Prefab = null;
            RequireWorld();
            foreach (var location in _zones.m_locationInstances.Values)
            {
                progress.Prefab = null;
                var definition = location.m_location;
                if (definition == null || !NativePlacement.IsValidLocationPrefab(definition)) continue;
                progress.Prefab = definition.m_prefab.Name;
                if (!resolved.TryGetValue(definition, out var id) || !string.Equals(id, progress.Prefab, StringComparison.Ordinal))
                    throw new InvalidOperationException("Location definitions changed during planning; retry maintenance: " + progress.Prefab);
            }
            ReserveLocationFootprints(managedIds, overrides, progress);
            ValidatePlanningExclusions(force: true);
            if (PlanningExclusionSummary != null) _warn("Location planning exclusions: " + PlanningExclusionSummary);
            completed = true;
            if (!_options.ZonesEnabled && _overlappingLocations.Count > 0)
            {
                var examples = _overlappingLocations.Take(8).Select(anchor =>
                    _zones.m_locationInstances[anchor].m_location.m_prefab.Name + " at " + anchor);
                _warn($"Location reset: {_overlappingLocations.Count} overlapping location footprints preserved ({string.Join(", ", examples)}). Their zones are reserved; review location placement and LocationResetRadii before retrying.");
            }
            _log($"Location plan: {_managedLocations.Count} managed locations; {_reservedLocationZones.Count} sectors excluded from individual resets; {_locationZoneGroups.Count} zone groups; {progress.Definitions} definitions in {progress.Timer.Elapsed.TotalSeconds:F1}s.");
        }
        finally
        {
            if (!completed) progress.ReportStopped(_warn);
        }
    }

    private void ReserveLocationFootprints(HashSet<string> managedIds, IReadOnlyDictionary<string, float> overrides,
        LocationPlanningProgress progress)
    {
        progress.Enter(PlanningStage.BuildFootprints);
        progress.Prefab = null;
        _locationFootprints.Clear();
        _managedLocations.Clear();
        _reservedLocationZones.Clear();
        _overlappingLocations.Clear();
        _locationZoneGroups.Clear();
        _locationGroupAnchors.Clear();
        foreach (var pair in _zones.m_locationInstances)
        {
            progress.Anchor = pair.Key;
            progress.Prefab = null;
            var location = pair.Value;
            if (location.m_location == null || !NativePlacement.IsValidLocationPrefab(location.m_location)) continue;
            var id = location.m_location.m_prefab.Name;
            progress.Prefab = id;
            overrides.TryGetValue(id, out var minimumRadius);
            var footprint = new LocationFootprint(pair.Key, location, minimumRadius);
            _locationFootprints.Add(pair.Key, footprint);
            if (overrides.ContainsKey(id) || (managedIds.Contains(id) && footprint.Zones.Count > 1))
                _managedLocations.Add(pair.Key);
        }
        // Every instance of a failed prefab is excluded, even if ungenerated or not selected.
        // Seed at least 3x3, then expand through full footprints and shared-sector connections.
        var excludedSeeds = new HashSet<Vector2s>();
        foreach (var footprint in _locationFootprints.Values)
        {
            if (!_zones.m_locationInstances.TryGetValue(footprint.Anchor, out var instance) ||
                !_skippedLocationPrefabs.Contains(instance.m_location.m_prefab.Name)) continue;
            _skippedLocationInstances++;
            excludedSeeds.UnionWith(footprint.Zones);
            for (var x = Math.Max(short.MinValue, footprint.Anchor.x - 1); x <= Math.Min(short.MaxValue, footprint.Anchor.x + 1); x++)
                for (var y = Math.Max(short.MinValue, footprint.Anchor.y - 1); y <= Math.Min(short.MaxValue, footprint.Anchor.y + 1); y++)
                    excludedSeeds.Add(new Vector2s(x, y));
        }
        if (_managedLocations.Count == 0 && excludedSeeds.Count == 0) return;
        progress.Enter(PlanningStage.GroupFootprints);
        progress.Prefab = null;
        progress.Anchor = null;
        var bySector = new Dictionary<Vector2s, List<LocationFootprint>>();
        foreach (var footprint in _locationFootprints.Values)
        {
            progress.Anchor = footprint.Anchor;
            foreach (var sector in footprint.Zones)
            {
                if (!bySector.TryGetValue(sector, out var entries)) bySector.Add(sector, entries = new());
                entries.Add(footprint);
            }
        }
        var excludedPending = new Queue<Vector2s>(excludedSeeds);
        _planningExcludedZones.UnionWith(excludedSeeds);
        while (excludedPending.Count > 0)
        {
            if (!bySector.TryGetValue(excludedPending.Dequeue(), out var touching)) continue;
            foreach (var footprint in touching)
            {
                if (!_planningExcludedAnchors.Add(footprint.Anchor)) continue;
                foreach (var sector in footprint.Zones)
                    if (_planningExcludedZones.Add(sector)) excludedPending.Enqueue(sector);
            }
        }
        _reservedLocationZones.UnionWith(_planningExcludedZones);
        var reservedAnchors = new HashSet<Vector2s>();
        foreach (var start in _managedLocations.OrderBy(zone => zone.x).ThenBy(zone => zone.y))
        {
            if (!reservedAnchors.Add(start)) continue;
            var pending = new Queue<Vector2s>();
            pending.Enqueue(start);
            var group = new HashSet<Vector2s>();
            var anchors = new HashSet<Vector2s>();
            while (pending.Count > 0)
            {
                var anchor = pending.Dequeue();
                progress.Anchor = anchor;
                anchors.Add(anchor);
                var footprint = _locationFootprints[anchor];
                group.UnionWith(footprint.Zones);
                foreach (var sector in footprint.Zones)
                    foreach (var other in bySector[sector])
                    {
                        if (other.Anchor == anchor) continue;
                        // Whole-sector deletion includes every connected location, even if
                        // their circles do not overlap. Radius-only restoration still skips overlap.
                        if (reservedAnchors.Add(other.Anchor)) pending.Enqueue(other.Anchor);
                        if (!footprint.Overlaps(other)) continue;
                        _overlappingLocations.Add(anchor);
                        _overlappingLocations.Add(other.Anchor);
                    }
            }
            _reservedLocationZones.UnionWith(group);
            if (!_options.ZonesEnabled) continue;
            // A generated single-sector neighbor may still qualify for radius restoration
            // when the managed owner is ungenerated. Both stages share this selection key.
            var eligibleAnchors = anchors.Where(anchor => GameWorld.IsGenerated(anchor) && _zones.m_locationInstances[anchor].m_placed)
                .OrderBy(anchor => anchor.x).ThenBy(anchor => anchor.y).ToArray();
            if (eligibleAnchors.Length == 0) continue;
            var representative = eligibleAnchors[0];
            _locationZoneGroups.Add(representative, group);
            foreach (var anchor in anchors) _locationGroupAnchors.Add(anchor, representative);
        }
    }

    private bool CanResetZoneGroup(Vector2s anchor, HashSet<Vector2s> group, HashSet<Vector2s> initialProtection)
    {
        RequireWorld();
        if (!GameWorld.IsGenerated(anchor) || !_zones.m_locationInstances.TryGetValue(anchor, out var owner) || !owner.m_placed)
            return false;
        if (!group.Any(sector => _managedLocations.Contains(sector) && GameWorld.IsGenerated(sector) &&
            _zones.m_locationInstances.TryGetValue(sector, out var managed) && managed.m_placed)) return false;
        if (!HasUnchangedGroupLocations(group)) return false;
        foreach (var sector in group)
        {
            // A pending proxy can finish after deletion. Do not tear down a partially loaded root.
            if (GameWorld.HasPendingZoneLoad(sector) || (GameWorld.TryGetRoot(sector, out _) && !_zones.IsZoneLoaded(sector))) return false;
            if (!CanResetZone(sector, _options.ZoneSafeZones, initialProtection)) return false;
        }
        return true;
    }

    private bool HasUnchangedGroupLocations(HashSet<Vector2s> group)
    {
        foreach (var sector in group)
        {
            if (_locationFootprints.TryGetValue(sector, out var footprint))
            {
                if (!_zones.m_locationInstances.TryGetValue(sector, out var location) || !footprint.Matches(location)) return false;
            }
            else if (_zones.m_locationInstances.ContainsKey(sector)) return false;
        }
        return true;
    }

    private bool CanResetLocation(Vector2s anchor)
    {
        RequireWorld();
        if (_planningExcludedAnchors.Contains(anchor) || _planningExcludedZones.Contains(anchor)) return false;
        if (_completedZoneResets.Contains(anchor)) return false;
        if (_locationGroupAnchors.TryGetValue(anchor, out var representative) &&
            !HasUnchangedGroupLocations(_locationZoneGroups[representative])) return false;
        if (_overlappingLocations.Contains(anchor) ||
            !_zones.m_locationInstances.TryGetValue(anchor, out var location) || !location.m_placed || location.m_location == null ||
            _skippedLocationPrefabs.Contains(location.m_location.m_prefab.Name)) return false;
        if (!_locationFootprints.TryGetValue(anchor, out var footprint))
        {
            // Full runs retain live single-zone supplements. A newly introduced multi-zone
            // location must wait for the next reservation plan, never expand this run's scope.
            if (_options.LocationResetRadii.ContainsKey(location.m_location.m_prefab.Name)) return false;
            footprint = new LocationFootprint(anchor, location);
            if (footprint.Zones.Count != 1 || _reservedLocationZones.Contains(anchor)) return false;
            _locationFootprints.Add(anchor, footprint);
        }
        if (!footprint.Matches(location) || footprint.Zones.Overlaps(_completedZoneResets))
            return false;
        foreach (var sector in footprint.Zones)
        {
            // The anchor's native loading is awaited by the operation. Neighboring proxy
            // loads must finish before a radius reset can safely delete their objects.
            if (sector != anchor && (GameWorld.HasPendingZoneLoad(sector) ||
                (GameWorld.TryGetRoot(sector, out _) && !_zones.IsZoneLoaded(sector)))) return false;
            if (!CanResetZone(sector, _options.LocationSafeZones)) return false;
        }
        return true;
    }

    private HashSet<Vector2s> SelectResetZones(HashSet<Vector2s> generated, HashSet<Vector2s> protectedZones)
    {
        var eligible = new HashSet<Vector2s>();
        // Separate stage passes avoid alternating the base-protection cache's SafeZones key per zone.
        if (_options.ZonesEnabled)
        {
            foreach (var zone in generated)
                if (!_reservedLocationZones.Contains(zone) && CanResetZone(zone, _options.ZoneSafeZones, protectedZones)) eligible.Add(zone);
            foreach (var pair in _locationZoneGroups)
                if (CanResetZoneGroup(pair.Key, pair.Value, protectedZones)) eligible.Add(pair.Key);
        }

        var supplements = _options.ZonesEnabled ? protectedZones : generated;
        if (_includeVegetation && _options.VegetationEnabled)
        {
            var resourceIds = new HashSet<string>(_options.TerrainVegetationIds, StringComparer.Ordinal);
            resourceIds.UnionWith(_options.VegetationIds);
            // Depleted resources are candidates too: presence of a current resource ZDO is not required.
            if (_zones.m_vegetation.Any(vegetation => vegetation.m_prefab != null && resourceIds.Contains(vegetation.m_prefab.name)))
                foreach (var zone in supplements)
                    if (!_reservedLocationZones.Contains(zone) && CanResetResourceZone(zone)) eligible.Add(zone);
        }
        if (_options.LocationsEnabled)
        {
            var locationIds = new HashSet<string>(_options.LocationIds, StringComparer.Ordinal);
            locationIds.IntersectWith(_zones.m_locations
                .Where(location => location != null && NativePlacement.IsValidLocationPrefab(location))
                .Select(location => location.m_prefab.Name));
            var locationCandidates = new HashSet<Vector2s>(supplements);
            locationCandidates.ExceptWith(_reservedLocationZones);
            locationCandidates.UnionWith(_options.ZonesEnabled ? (IEnumerable<Vector2s>)_locationGroupAnchors.Keys : _managedLocations);
            foreach (var zone in locationCandidates)
                if (generated.Contains(zone) && _zones.m_locationInstances.TryGetValue(zone, out var location) && location.m_placed &&
                    NativePlacement.IsValidLocationPrefab(location.m_location) &&
                    locationIds.Contains(location.m_location.m_prefab.Name) && CanResetLocation(zone))
                    eligible.Add(LocationSelectionKey(zone));
        }

        var candidates = eligible.ToArray();
        var count = (int)((candidates.Length * (long)_resetPercent + 50) / 100);
        var random = new System.Random(); // Never change Unity's world-generation random state.
        var selected = new HashSet<Vector2s>();
        for (var i = 0; i < count; i++)
        {
            var next = random.Next(i, candidates.Length);
            (candidates[i], candidates[next]) = (candidates[next], candidates[i]);
            selected.Add(candidates[i]);
        }
        _log($"Automatic reset selection: {selected.Count}/{candidates.Length} candidates ({_resetPercent}%; each connected zone group counts once). Later protection skips are not replaced.");
        return selected;
    }

    private bool CanResetResourceZone(Vector2s zone) =>
        !_resourceExcludedZones.Contains(zone) && CanResetZone(zone, _options.VegetationSafeZones);

    private static HashSet<Vector2s> ProtectedSnapshot(int size, HashSet<Vector2s> generated)
    {
        BaseProtection.InvalidateCache();
        var protectedZones = new HashSet<Vector2s>(BaseProtection.GetExcluded(size));
        protectedZones.IntersectWith(generated);
        return protectedZones;
    }

    private bool CanResetZone(Vector2s zone, int safeZones, HashSet<Vector2s>? initialProtection = null)
    {
        RequireWorld();
        // Called before every attempt, including retries after loading. Once observed, a player's
        // zone and its eight neighbors stay excluded for this run, even after the player moves.
        GameWorld.CollectPlayerZones(_playerZones);
        if (!CanResetTerrain(zone)) return false;
        // Keep recorded positions separate from this fixed protection policy. Probe at most nine
        // coordinates without allocating a larger set, and never wrap the native short boundaries.
        for (var x = Math.Max(short.MinValue, zone.x - 1); x <= Math.Min(short.MaxValue, zone.x + 1); x++)
            for (var y = Math.Max(short.MinValue, zone.y - 1); y <= Math.Min(short.MaxValue, zone.y + 1); y++)
                if (_playerZones.Contains(new Vector2s(x, y))) return false;
        if (!_epicLoot.CanResetZone(zone)) return false;
        if (initialProtection != null && initialProtection.Contains(zone)) return false;
        if (safeZones <= 0) return true;
        // Cache the world scan for ten seconds rather than scanning all objects for each zone.
        return !BaseProtection.GetExcluded(safeZones).Contains(zone);
    }

    // Only these policies also protect terrain tiles. Keep ordinary base/player/EpicLoot rules separate.
    private bool CanResetTerrain(Vector2s zone) =>
        !_planningExcludedZones.Contains(zone) && _invasions.CanResetZone(zone) && _alwaysProtected.CanResetZone(zone);

    private OperationParameters Parameters(int safeZones) =>
        new()
        {
            RunId = _runId,
            Warn = _warn,
            SafeZones = safeZones,
            ProtectEpicLoot = _options.EpicLootProtectionEnabled,
            ProtectEpicLootBounties = _options.EpicLootBountyProtectionEnabled,
            CanResetTerrain = zone => !_reservedLocationZones.Contains(zone) && CanResetTerrain(zone),
            CanDeleteObject = zdo => !_reservedLocationZones.Contains(ZoneSystem.GetZone(zdo.GetPosition())) &&
                !_planningExcludedZones.Contains(ZoneSystem.GetZone(zdo.GetPosition())),
            AlwaysProtected = _alwaysProtected
        };

    private HashSet<string> ResolveIds(string kind, IEnumerable<string> configured, IEnumerable<string> available)
    {
        var registry = new HashSet<string>(available, StringComparer.Ordinal);
        var requested = new HashSet<string>(configured, StringComparer.Ordinal);
        if (requested.Count == 0) return requested;
        var missing = requested.Where(id => !registry.Contains(id)).OrderBy(id => id).ToArray();
        if (missing.Length > 0)
            _warn($"Unknown {kind} IDs skipped (check installed world mods): {string.Join(", ", missing)}.");
        requested.IntersectWith(registry);
        if (requested.Count == 0) _warn($"{kind} reset skipped: no configured IDs exist in this world's registry.");
        return requested;
    }

    private IEnumerator WaitUntilUnpaused(Stopwatch? timer = null, Stopwatch? diagnosticTimer = null)
    {
        while (Time.timeScale <= 0f)
        {
            RequireWorld();
            timer?.Stop();
            diagnosticTimer?.Stop();
            yield return null;
        }
        timer?.Start();
        diagnosticTimer?.Start();
    }

    private enum PlanningStage
    {
        CollectDefinitions, BetweenPrefabs, LoadAsync, WaitForPrefab, ResolveAsset,
        ReleasePrefab, ValidateDefinitions, BuildFootprints, GroupFootprints
    }

    // Bounded, run-local diagnostics. No per-frame formatting, logging, history, or extra asset queries.
    private sealed class LocationPlanningProgress
    {
        public readonly Stopwatch Timer = Stopwatch.StartNew();
        public int Definitions;
        public string? Prefab;
        public Vector2s? Anchor;
        public string? Readiness;
        public bool? LastReady;
        private readonly int _instances;
        private readonly long[] _ticks = new long[(int)PlanningStage.GroupFootprints + 1];
        private PlanningStage _stage;
        private long _stageStart;
        private long _prefabStart = -1;
        private int _completed;
        private long _slowestTicks;
        private string? _slowestPrefab;
        private bool _reported;
        private bool? _isLoading;
        private float? _loadProgress;
        private double _sampleAt = -1;
        private long _sampleTicks;
        private double _progressChangedAt;
        private int _loadSamples;

        public string LoadDiagnostics => FormattableString.Invariant(
            $"isLoading={_isLoading?.ToString() ?? "unknown"}; loadProgress={(_loadProgress.HasValue ? _loadProgress.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "unknown")}; loadSamples={_loadSamples}; loadSampleAge={(_loadSamples > 0 ? ((double)(Timer.ElapsedTicks - _sampleTicks) / Stopwatch.Frequency).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s" : "unknown")}; progressUnchangedFor={(_loadProgress.HasValue ? (_sampleAt - _progressChangedAt).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s" : "unknown")}");

        public void SampleLoading(ZoneSystem.ZoneLocation definition, double elapsed, bool force = false)
        {
            if (_sampleAt >= 0 && (!force && elapsed - _sampleAt < 1 || elapsed == _sampleAt)) return;
            bool? loading = null;
            float? value = null;
            try { LocationMetadataFallback.ReadLoadState(definition, out loading, out value); }
            catch (Exception) { } // Also tolerate a failing optional integration accessor.
            if (!value.HasValue || value != _loadProgress) _progressChangedAt = elapsed;
            _isLoading = loading;
            _loadProgress = value;
            _sampleAt = elapsed;
            _sampleTicks = Timer.ElapsedTicks;
            _loadSamples++;
        }

        public LocationPlanningProgress(int instances) => _instances = instances;

        public void Enter(PlanningStage stage)
        {
            var now = Timer.ElapsedTicks;
            _ticks[(int)_stage] += now - _stageStart;
            _stageStart = now;
            _stage = stage;
        }

        public void BeginPrefab(string id)
        {
            Prefab = id;
            Readiness = null;
            LastReady = null;
            _isLoading = null;
            _loadProgress = null;
            _sampleAt = -1;
            _progressChangedAt = 0;
            _loadSamples = 0;
            _prefabStart = Timer.ElapsedTicks;
            Enter(PlanningStage.LoadAsync);
        }

        public void CompletePrefab(bool skipped = false)
        {
            var elapsed = Timer.ElapsedTicks - _prefabStart;
            if (!skipped && (_slowestPrefab == null || elapsed > _slowestTicks))
            {
                _slowestTicks = elapsed;
                _slowestPrefab = Prefab;
            }
            if (!skipped) _completed++;
            Enter(PlanningStage.BetweenPrefabs);
            Prefab = null;
            _prefabStart = -1;
            Readiness = null;
            LastReady = null;
            _isLoading = null;
            _loadProgress = null;
            _sampleAt = -1;
            _loadSamples = 0;
        }

        public void ReportStopped(Action<string> warn)
        {
            if (_reported) return;
            _reported = true;
            try
            {
                var now = Timer.ElapsedTicks;
                double Seconds(long ticks) => (double)ticks / Stopwatch.Frequency;
                double StageSeconds(PlanningStage stage) =>
                    Seconds(_ticks[(int)stage] + (_stage == stage ? now - _stageStart : 0));
                var prefabTime = _prefabStart >= 0 ? FormattableString.Invariant($"{Seconds(now - _prefabStart):F2}s") : "n/a";
                var anchor = Anchor is { } zone ? $"{zone.x},{zone.y}" : "n/a";
                warn(FormattableString.Invariant($"Location planning stopped: stage={_stage}; prefab='{Prefab ?? "<none>"}'; anchor={anchor}; definitions={_completed}/{Definitions}; instances={_instances}; elapsed={Seconds(now):F2}s; stageElapsed={Seconds(now - _stageStart):F2}s; prefabElapsed={prefabTime}; readiness={Readiness ?? "n/a"}; lastReady={LastReady?.ToString() ?? "unknown"}; {LoadDiagnostics}; collect={StageSeconds(PlanningStage.CollectDefinitions):F2}s, between={StageSeconds(PlanningStage.BetweenPrefabs):F2}s, load={StageSeconds(PlanningStage.LoadAsync):F2}s, wait={StageSeconds(PlanningStage.WaitForPrefab):F2}s, asset={StageSeconds(PlanningStage.ResolveAsset):F2}s, release={StageSeconds(PlanningStage.ReleasePrefab):F2}s, validate={StageSeconds(PlanningStage.ValidateDefinitions):F2}s, footprints={StageSeconds(PlanningStage.BuildFootprints):F2}s, groups={StageSeconds(PlanningStage.GroupFootprints):F2}s; slowestCompleted='{_slowestPrefab ?? "<none>"}' ({Seconds(_slowestTicks):F2}s). No reset stage started in this run."));
            }
            catch (Exception)
            {
                // A diagnostic sink must not replace the original failure or prevent reference cleanup.
            }
        }
    }

    private bool IsSameWorld() => ReferenceEquals(ZNet.instance, _network) &&
        ReferenceEquals(ZoneSystem.instance, _zones) && _network != null && _zones != null &&
        ZNet.World != null && ZNet.World.m_uid == _worldUid;

    private void RequireWorld()
    {
        if (!IsSameWorld() || !_network.IsServer() || _network.HaveStopped || ZNetScene.instance == null ||
            ZDOMan.instance == null || WorldGenerator.instance == null)
            throw new InvalidOperationException("The hosting world is no longer ready for maintenance.");
        ValidatePlanningExclusions();
    }

    private void ValidatePlanningExclusions(bool force = false)
    {
        if (_planningExcludedZones.Count == 0) return;
        if (_metadataFallbacks.Values.Any(bounds => force ? !bounds.Revalidate() : !bounds.IsCurrent()))
            throw new InvalidOperationException("Location definitions changed after a planning timeout; maintenance stopped.");
        // A same-count replacement outside the exclusion can introduce another failed prefab or
        // a new overlapping footprint. Check the run snapshot even within one frame: a synchronous
        // readiness callback can change it between the tracking wrapper's two safety checks.
        // This scan is only needed for partial plans; loader/Harmony inspection stays at stage boundaries.
        foreach (var pair in _zones.m_locationInstances)
        {
            var definition = pair.Value.m_location;
            if (definition == null || !NativePlacement.IsValidLocationPrefab(definition)) continue;
            // Native unique-location placement can prune unplaced entries. Their old exclusions
            // remain conservative; only surviving/new entries need validation against the plan.
            var zoneReset = _completedZoneResets.Contains(pair.Key) ||
                _activeOperation is TrackedResetZones active && active.Result.ChangedZones.Contains(pair.Key);
            if (!_locationFootprints.TryGetValue(pair.Key, out var footprint) || !footprint.Matches(pair.Value, zoneReset))
                throw new InvalidOperationException("A location changed after a planning timeout; maintenance stopped: " + pair.Key);
        }
    }

    private static void InvalidateCaches()
    {
        TerrainResetter.InvalidateCache();
        BaseProtection.InvalidateCache();
    }
}

