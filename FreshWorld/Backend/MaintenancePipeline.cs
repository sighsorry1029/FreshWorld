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
    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly ZNet _network;
    private readonly ZoneSystem _zones;
    private readonly long _worldUid;
    private readonly HashSet<Vector2i> _playerZones = new();
    private ITrackedOperation? _activeOperation;

    public MaintenancePipeline(RunOptions options, bool includeVegetation, Action<string> log, Action<string> warn)
    {
        _options = options;
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
            BaseProtection.Configure(_options.ProtectedPlayerObjects, _options.ProtectedObjects);
            yield return SaveWorld("before maintenance");

            RequireWorld();
            _playerZones.Clear();
            GameWorld.CollectPlayerZones(_playerZones);
            var generated = GameWorld.GeneratedSetSnapshot();
            var protectedZones = ProtectedSnapshot(_options.ZoneSafeZones, generated);
            _log($"Maintenance plan: {generated.Count} generated zones; {protectedZones.Count} protected by base markers; {_playerZones.Count} player zones excluded from direct resets.");

            if (_options.ZonesEnabled)
            {
                var args = Parameters(_options.ZoneSafeZones);
                // Retain the initial protection set, and recheck the current set as the batch advances.
                yield return Execute("Zone reset", new TrackedResetZones(_log, args, generated,
                    zone => CanResetZone(zone, _options.ZoneSafeZones, protectedZones),
                    maxZonesPerFrame: _options.MaxZonesPerFrame,
                    frameBudgetMilliseconds: _options.FrameBudgetMilliseconds));
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
                    var candidates = SupplementCandidates(protectedZones);
                    var safeZones = _options.LocationSafeZones;
                    var args = Parameters(safeZones);
                    if (safeZones == 0)
                        _warn("LocationSafeZones=0: configured locations may remove player pieces inside their reset area.");
                    yield return Execute("Location reset", new TrackedRegenerateLocations(_log, ids, args,
                        candidates, zone => CanResetZone(zone, safeZones),
                        maxZonesPerFrame: _options.MaxZonesPerFrame,
                        frameBudgetMilliseconds: _options.FrameBudgetMilliseconds));
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
        HashSet<Vector2i> protectedZones)
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
            frameBudgetMilliseconds: _options.FrameBudgetMilliseconds));
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
            operation.Executable.Init();
            _log($"{name}: selected {operation.Result.SelectedZones.Count} zones.");
            yield return operation.Executable.Execute();
            var result = operation.Result;
            if (!result.Finished || result.FailedCount > 0)
                throw new InvalidOperationException($"{name} did not finish successfully ({result.FailedCount} failed zones).");
            _log($"{name}: {result.ChangedZones.Count} changed, {result.SkippedZones.Count} skipped; {elapsed.Elapsed.TotalSeconds:F1}s.");
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

    private HashSet<Vector2i> SupplementCandidates(HashSet<Vector2i> protectedZones)
    {
        var current = GameWorld.GeneratedSetSnapshot();
        // After zone reset, supplement only the initially protected zones that still exist.
        // With zone reset disabled, selected resources/locations remain independently useful world-wide.
        if (_options.ZonesEnabled)
            current.IntersectWith(protectedZones);
        return current;
    }

    private static HashSet<Vector2i> ProtectedSnapshot(int size, HashSet<Vector2i> generated)
    {
        BaseProtection.InvalidateCache();
        var protectedZones = new HashSet<Vector2i>(BaseProtection.GetExcluded(size));
        protectedZones.IntersectWith(generated);
        return protectedZones;
    }

    private bool CanResetZone(Vector2i zone, int safeZones, HashSet<Vector2i>? initialProtection = null)
    {
        RequireWorld();
        // Called before every attempt, including retries after loading. Once observed, a player's
        // zone stays excluded for this run even if they move before a later restoration stage.
        GameWorld.CollectPlayerZones(_playerZones);
        if (_playerZones.Contains(zone)) return false;
        if (initialProtection != null && initialProtection.Contains(zone)) return false;
        if (safeZones <= 0) return true;
        // Cache the world scan for ten seconds rather than scanning all objects for each zone.
        return !BaseProtection.GetExcluded(safeZones).Contains(zone);
    }

    private OperationParameters Parameters(int safeZones) =>
        new()
        {
            SafeZones = safeZones
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

    private IEnumerator WaitUntilUnpaused(Stopwatch? timer = null)
    {
        while (Time.timeScale <= 0f)
        {
            RequireWorld();
            timer?.Stop();
            yield return null;
        }
        timer?.Start();
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

