using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
    private HashSet<Vector2s>? _selectedZones;
    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly ZNet _network;
    private readonly ZoneSystem _zones;
    private readonly long _worldUid;
    private readonly HashSet<Vector2s> _playerZones = new();
    private readonly EpicLootProtection _epicLoot;
    private readonly JotunInvasionProtection _invasions = new();
    private readonly AlwaysProtectedObjects _alwaysProtected;
    private ITrackedOperation? _activeOperation;
    private readonly Dictionary<Vector2s, LocationFootprint> _locationFootprints = new();
    private readonly HashSet<Vector2s> _managedLocations = new();
    private readonly HashSet<Vector2s> _reservedLocationZones = new();
    private readonly HashSet<Vector2s> _overlappingLocations = new();
    private readonly Dictionary<Vector2s, HashSet<Vector2s>> _locationZoneGroups = new();
    private readonly Dictionary<Vector2s, Vector2s> _locationGroupAnchors = new();
    private readonly HashSet<Vector2s> _completedZoneResets = new();

    public MaintenancePipeline(RunOptions options, bool includeVegetation, Action<string> log, Action<string> warn,
        int resetPercent = 100)
    {
        if (resetPercent < 0 || resetPercent > 100) throw new ArgumentOutOfRangeException(nameof(resetPercent));
        _resetPercent = resetPercent;
        _options = options;
        _alwaysProtected = new AlwaysProtectedObjects(options.AlwaysProtectedPrefabs);
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
            yield return SaveWorld("before maintenance");
            yield return PlanLocations();

            RequireWorld();
            _playerZones.Clear();
            GameWorld.CollectPlayerZones(_playerZones);
            var generated = GameWorld.GeneratedSetSnapshot();
            var protectedZones = ProtectedSnapshot(_options.ZoneSafeZones, generated);
            var epicLootZones = _epicLoot.Capture();
            var invasionZones = _invasions.Capture();
            var alwaysProtectedZones = _alwaysProtected.Capture();
            _log($"Maintenance plan: {generated.Count} generated zones; {protectedZones.Count} protected by base markers; {_playerZones.Count} observed player zones with 3x3 protection; {epicLootZones} EpicLoot zones with 1x1 protection; {invasionZones} Jotun objective zones with 1x1 protection; {alwaysProtectedZones} zones protected by AlwaysProtectedPrefabs.");

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
                    args.CanDeleteObject = null;
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
        var args = Parameters(_options.VegetationSafeZones);
        // Zero also disables the engine's temporary unmodified-ground generation override.
        args.TerrainReset = terrainRadius;
        yield return Execute(name, new TrackedResetVegetation(_log, ids, args,
            candidates, zone => CanResetZone(zone, _options.VegetationSafeZones),
            maxZonesPerFrame: _options.MaxZonesPerFrame,
            frameBudgetMilliseconds: _options.FrameBudgetMilliseconds, warn: _warn));
    }

    private IEnumerator Execute(string name, ITrackedOperation operation)
    {
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
        return current;
    }

    private HashSet<Vector2s> LocationCandidates(HashSet<Vector2s> protectedZones)
    {
        var current = SupplementCandidates(protectedZones);
        IEnumerable<Vector2s> managed = _options.ZonesEnabled ? _locationGroupAnchors.Keys : _managedLocations;
        current.UnionWith(managed.Where(anchor => GameWorld.IsGenerated(anchor) &&
            (_selectedZones == null || _selectedZones.Contains(LocationSelectionKey(anchor)))));
        current.ExceptWith(_completedZoneResets);
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

        // Resolve every type represented in this world before comparing bounds. An unresolved
        // custom location outside the apparent neighborhood can still extend into it. Request
        // just its prefab, not every possible dungeon room, and release our reference each time.
        var definitions = _zones.m_locationInstances.Values
            .Where(location => location.m_location != null && NativePlacement.IsValidLocationPrefab(location.m_location))
            .Select(location => location.m_location).Distinct().ToArray();
        if (!definitions.Any(definition => managedIds.Contains(definition.m_prefab.Name))) yield break;
        var resolved = new Dictionary<ZoneSystem.ZoneLocation, string>();
        var planningTime = Stopwatch.StartNew();
        var frameTime = Stopwatch.StartNew();
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
            var waiting = Stopwatch.StartNew();
            try
            {
                if (reference.IsValid)
                {
                    // Native LoadAsync increments the reference before invoking callbacks,
                    // including callbacks that can run synchronously and throw.
                    acquired = true;
                    reference.LoadAsync();
                }
                while (true)
                {
                    yield return WaitUntilUnpaused(waiting, planningTime);
                    RequireWorld();
                    if (planningTime.Elapsed.TotalSeconds >= 180)
                        throw new TimeoutException("Location reservation planning did not finish within 180 seconds.");
                    // Named mod references may instead provide their own native readiness hook.
                    if (acquired ? reference.IsLoaded : NativePlacement.CanSpawnLocation(_zones, definition)) break;
                    if (waiting.Elapsed.TotalSeconds >= 30)
                        throw new TimeoutException("Location metadata did not load before planning: " + id);
                    yield return null;
                }
                if (acquired) _ = reference.Asset; // Allow asset-resolution hooks to finalize metadata.
                resolved[definition] = id;
            }
            finally
            {
                if (acquired) reference.Release();
            }
        }
        RequireWorld();
        foreach (var location in _zones.m_locationInstances.Values)
        {
            var definition = location.m_location;
            if (definition == null || !NativePlacement.IsValidLocationPrefab(definition)) continue;
            if (!resolved.TryGetValue(definition, out var id) || !string.Equals(id, definition.m_prefab.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("Location definitions changed during planning; retry maintenance: " + definition.m_prefab.Name);
        }
        ReserveLocationFootprints(managedIds, overrides);
        if (!_options.ZonesEnabled && _overlappingLocations.Count > 0)
        {
            var examples = _overlappingLocations.Take(8).Select(anchor =>
                _zones.m_locationInstances[anchor].m_location.m_prefab.Name + " at " + anchor);
            _warn($"Location reset: {_overlappingLocations.Count} overlapping location footprints preserved ({string.Join(", ", examples)}). Their zones are reserved; review location placement and LocationResetRadii before retrying.");
        }
        _log($"Location plan: {_managedLocations.Count} managed locations; {_reservedLocationZones.Count} sectors excluded from individual resets; {_locationZoneGroups.Count} zone groups.");
    }

    private void ReserveLocationFootprints(HashSet<string> managedIds, IReadOnlyDictionary<string, float> overrides)
    {
        _locationFootprints.Clear();
        _managedLocations.Clear();
        _reservedLocationZones.Clear();
        _overlappingLocations.Clear();
        _locationZoneGroups.Clear();
        _locationGroupAnchors.Clear();
        foreach (var pair in _zones.m_locationInstances)
        {
            var location = pair.Value;
            if (location.m_location == null || !NativePlacement.IsValidLocationPrefab(location.m_location)) continue;
            var id = location.m_location.m_prefab.Name;
            overrides.TryGetValue(id, out var minimumRadius);
            var footprint = new LocationFootprint(pair.Key, location, minimumRadius);
            _locationFootprints.Add(pair.Key, footprint);
            if (overrides.ContainsKey(id) || (managedIds.Contains(id) && footprint.Zones.Count > 1))
                _managedLocations.Add(pair.Key);
        }
        if (_managedLocations.Count == 0) return;
        var bySector = new Dictionary<Vector2s, List<LocationFootprint>>();
        foreach (var footprint in _locationFootprints.Values)
            foreach (var sector in footprint.Zones)
            {
                if (!bySector.TryGetValue(sector, out var entries)) bySector.Add(sector, entries = new());
                entries.Add(footprint);
            }
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
        if (_completedZoneResets.Contains(anchor)) return false;
        if (_locationGroupAnchors.TryGetValue(anchor, out var representative) &&
            !HasUnchangedGroupLocations(_locationZoneGroups[representative])) return false;
        if (_overlappingLocations.Contains(anchor) ||
            !_zones.m_locationInstances.TryGetValue(anchor, out var location) || !location.m_placed) return false;
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
                    if (!_reservedLocationZones.Contains(zone) && CanResetZone(zone, _options.VegetationSafeZones)) eligible.Add(zone);
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
        _invasions.CanResetZone(zone) && _alwaysProtected.CanResetZone(zone);

    private OperationParameters Parameters(int safeZones) =>
        new()
        {
            SafeZones = safeZones,
            ProtectEpicLoot = _options.EpicLootProtectionEnabled,
            ProtectEpicLootBounties = _options.EpicLootBountyProtectionEnabled,
            CanResetTerrain = zone => !_reservedLocationZones.Contains(zone) && CanResetTerrain(zone),
            CanDeleteObject = zdo => !_reservedLocationZones.Contains(ZoneSystem.GetZone(zdo.GetPosition())),
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

    private IEnumerator SaveWorld(string reason)
    {
        RequireWorld();
        var waiting = Stopwatch.StartNew();
        yield return WaitUntilUnpaused(waiting);
        while (_network.IsSaving())
        {
            yield return WaitUntilUnpaused(waiting);
            RequireWorld();
            CheckSaveTimeout(waiting);
            yield return null;
        }
        if (ZNet.m_loadError || _zones.SkipSaving() || DungeonDB.instance == null || DungeonDB.instance.SkipSaving())
            throw new InvalidOperationException("World saving is disabled or the game reported a load error; maintenance cannot continue.");

        string? saveError = null;
        var observedStart = false;
        Action started = () => observedStart = true;
        Application.LogCallback logged = (message, trace, type) =>
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
                message.IndexOf("sav", StringComparison.OrdinalIgnoreCase) >= 0)
                Interlocked.CompareExchange(ref saveError, message, null);
        };
        ZNet.WorldSaveStarted += started;
        Application.logMessageReceivedThreaded += logged;
        try
        {
            _log($"Requesting world save {reason}.");
            var previousDoneTime = _network.SaveDoneTime;
            _network.Save(false);
            if (!observedStart && !_network.IsSaving() && _network.SaveDoneTime == previousDoneTime)
                throw new InvalidOperationException("The world save request did not start.");
            waiting.Restart();
            do
            {
                yield return WaitUntilUnpaused(waiting);
                RequireWorld();
                var error = Volatile.Read(ref saveError);
                if (error != null) throw new InvalidOperationException("World save reported an error: " + error);
                CheckSaveTimeout(waiting);
                yield return null;
            } while (_network.IsSaving());
            var finalError = Volatile.Read(ref saveError);
            if (finalError != null) throw new InvalidOperationException("World save reported an error: " + finalError);
            _log($"World save worker finished {reason} ({waiting.Elapsed.TotalSeconds:F1}s).");
        }
        finally
        {
            Application.logMessageReceivedThreaded -= logged;
            ZNet.WorldSaveStarted -= started;
        }
    }

    private void CheckSaveTimeout(Stopwatch waiting)
    {
        if (waiting.Elapsed.TotalSeconds > _options.SaveTimeoutSeconds)
            throw new TimeoutException($"World save did not finish within {_options.SaveTimeoutSeconds}s.");
    }

    private IEnumerator WaitUntilUnpaused(Stopwatch? timer = null, Stopwatch? additionalTimer = null)
    {
        while (Time.timeScale <= 0f)
        {
            RequireWorld();
            timer?.Stop();
            additionalTimer?.Stop();
            yield return null;
        }
        timer?.Start();
        additionalTimer?.Start();
    }

    private bool IsSameWorld() => ReferenceEquals(ZNet.instance, _network) &&
        ReferenceEquals(ZoneSystem.instance, _zones) && _network != null && _zones != null &&
        ZNet.World != null && ZNet.World.m_uid == _worldUid;

    private void RequireWorld()
    {
        if (!IsSameWorld() || !_network.IsServer() || _network.HaveStopped || ZNetScene.instance == null ||
            ZDOMan.instance == null || WorldGenerator.instance == null)
            throw new InvalidOperationException("The hosting world is no longer ready for maintenance.");
    }

    private static void InvalidateCaches()
    {
        TerrainResetter.InvalidateCache();
        BaseProtection.InvalidateCache();
    }
}

