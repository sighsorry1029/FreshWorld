using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using FreshWorld.Engine;

namespace FreshWorld.Backend;

/// <summary>Results refer to exact zone coordinates, so a later phase can avoid repeating successful resets.</summary>
internal sealed class OperationResult
{
    public HashSet<Vector2s> CandidateZones { get; } = new();
    public HashSet<Vector2s> SelectedZones { get; } = new();
    public HashSet<Vector2s> CompletedZones { get; } = new();
    public HashSet<Vector2s> ChangedZones { get; } = new();
    public HashSet<Vector2s> SkippedZones { get; } = new();
    public HashSet<Vector2s> FailedZones { get; } = new();
    public int FailedCount { get; internal set; }
    public bool Started { get; internal set; }
    public bool Finished { get; internal set; }
}

internal interface ITrackedOperation
{
    ExecutedOperation Executable { get; }
    OperationResult Result { get; }
    void Cleanup();
}

/// <summary>
/// Keeps retry/result bookkeeping independent of the scheduler. The caller must hold an
/// exclusive maintenance lease until execution and Cleanup both finish: generation uses shared game state.
/// </summary>
internal sealed class OperationTracker
{
    private static readonly AccessTools.FieldRef<ZoneSystem, List<UnityEngine.GameObject>> TemporaryObjects =
        AccessTools.FieldRefAccess<ZoneSystem, List<UnityEngine.GameObject>>("m_tempSpawnedObjects");
    private readonly HashSet<Vector2s>? _candidates;
    private readonly Func<Vector2s, bool>? _canProcess;
    private readonly Action<OperationResult>? _completed;
    private readonly HashSet<Vector2s> _pendingLoads = new();
    private readonly int _maxZonesPerFrame;
    private readonly double _frameBudgetMilliseconds;
    private readonly ZNet _worldNet;
    private readonly ZoneSystem _worldZones;
    private readonly long _worldUid;
    public OperationResult Result { get; } = new();

    public OperationTracker(HashSet<Vector2s>? candidates, Func<Vector2s, bool>? canProcess,
        Action<OperationResult>? completed, int maxZonesPerFrame, double frameBudgetMilliseconds)
    {
        if (maxZonesPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(maxZonesPerFrame));
        if (frameBudgetMilliseconds <= 0 || double.IsNaN(frameBudgetMilliseconds) || double.IsInfinity(frameBudgetMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(frameBudgetMilliseconds));
        _candidates = candidates == null ? null : new HashSet<Vector2s>(candidates);
        _canProcess = canProcess;
        _completed = completed;
        _maxZonesPerFrame = maxZonesPerFrame;
        _frameBudgetMilliseconds = frameBudgetMilliseconds;
        _worldNet = ZNet.instance;
        _worldZones = ZoneSystem.instance;
        _worldUid = _worldNet.GetWorldUID();
    }

    public static HashSet<UnityEngine.GameObject> SnapshotTemporaryObjects(ZoneSystem zones) =>
        new(TemporaryObjects(zones));

    public static void CleanupNewTemporaryObjects(ZoneSystem zones, HashSet<UnityEngine.GameObject> original)
    {
        var temporary = TemporaryObjects(zones);
        List<Exception>? errors = null;
        foreach (var obj in temporary.Where(obj => !original.Contains(obj)).ToArray())
        {
            try
            {
                if (obj != null) UnityEngine.Object.Destroy(obj);
                temporary.Remove(obj!);
            }
            catch (Exception error) { (errors ??= new()).Add(error); }
        }
        if (errors != null) throw new AggregateException("Could not clean up temporary maintenance objects.", errors);
    }
    public bool IsSameWorld => ReferenceEquals(ZNet.instance, _worldNet) &&
        ReferenceEquals(ZoneSystem.instance, _worldZones) && ZNet.World != null &&
        ZNet.World.m_uid == _worldUid && _worldNet != null && _worldZones != null;

    public Vector2s[] Restrict(Vector2s[] zones)
    {
        var result = _candidates == null ? zones : zones.Where(_candidates.Contains).ToArray();
        Result.CandidateZones.UnionWith(result);
        return result;
    }

    public void Selected(Vector2s[] zones) => Result.SelectedZones.UnionWith(zones);

    public bool CanProcess(Vector2s zone)
    {
        if (_canProcess == null || _canProcess(zone)) return true;
        Skip(zone);
        return false;
    }

    public void Skip(Vector2s zone)
    {
        Result.SkippedZones.Add(zone);
        Release(zone);
    }

    // Called immediately before an generation operation may poke this zone, never for a zone already loaded.
    public void MayLoad(Vector2s zone)
    {
        if (!ZoneSystem.instance.IsZoneLoaded(zone)) _pendingLoads.Add(zone);
    }

    public void Changed(Vector2s zone) => Result.ChangedZones.Add(zone);

    public IEnumerator Execute(Vector2s[] zones, Func<Vector2s, bool> execute, Action failed)
    {
        var frame = Stopwatch.StartNew();
        var attempts = 0;
        for (var index = 0; index < zones.Length; index++)
        {
            var zone = zones[index];
            var waiting = Stopwatch.StartNew();
            while (true)
            {
                while (UnityEngine.Time.timeScale <= 0f)
                {
                    waiting.Stop();
                    frame.Stop();
                    yield return null;
                }
                waiting.Start();
                frame.Start();
                attempts++;
                if (execute(zone))
                {
                    if (!Result.SkippedZones.Contains(zone)) Result.CompletedZones.Add(zone);
                    Release(zone);
                    break;
                }
                // Match UW's existing timeout, but expose the exact failed zone to the next phase.
                if (waiting.ElapsedMilliseconds >= 10000)
                {
                    Result.FailedZones.Add(zone);
                    failed();
                    Release(zone);
                    break;
                }
                yield return null;
                frame.Restart();
                attempts = 0;
            }
            // Keep zone resets fast, while bounding batches. A single zone's generation is indivisible.
            if (attempts >= _maxZonesPerFrame || frame.Elapsed.TotalMilliseconds >= _frameBudgetMilliseconds)
            {
                yield return null;
                frame.Restart();
                attempts = 0;
            }
        }
        // OnEnd may repair terrain borders. Do not enter it while a local host is paused.
        while (UnityEngine.Time.timeScale <= 0f) yield return null;
    }

    public void Finish(int failed)
    {
        // Completion requires the execution loop to have started.
        Result.FailedCount = failed + (Result.Started ? 0 : 1);
        Result.Finished = Result.Started;
        _completed?.Invoke(Result);
    }

    private void Release(Vector2s zone)
    {
        if (!_pendingLoads.Remove(zone)) return;
        if (IsSameWorld && ZNetScene.instance != null && ZDOMan.instance != null)
            GameWorld.ReleaseZone(zone);
    }

    public void Cleanup()
    {
        List<Exception>? errors = null;
        foreach (var zone in _pendingLoads.ToArray())
        {
            try { Release(zone); }
            catch (Exception error) { (errors ??= new()).Add(error); }
        }
        if (errors != null) throw new AggregateException("Could not release all maintenance zones.", errors);
    }
}

internal sealed class TrackedResetZones : ResetZones, ITrackedOperation
{
    private readonly OperationTracker _tracker;
    private bool _endAttempted;
    private bool _mutationAttempted;
    public ExecutedOperation Executable => this;
    public OperationResult Result => _tracker.Result;

    public TrackedResetZones(Action<string> log, OperationParameters args,
        HashSet<Vector2s>? candidates = null, Func<Vector2s, bool>? canProcess = null,
        Action<OperationResult>? completed = null, int maxZonesPerFrame = 64,
        double frameBudgetMilliseconds = 8) : base(log, args, candidates)
    {
        _tracker = new OperationTracker(candidates, canProcess, completed, maxZonesPerFrame, frameBudgetMilliseconds);
    }

    protected override string OnInit()
    {
        ZonesToUpgrade = _tracker.Restrict(ZonesToUpgrade);
        var text = base.OnInit();
        _tracker.Selected(ZonesToUpgrade);
        return text;
    }

    protected override bool ExecuteZone(Vector2s zone)
    {
        if (!_tracker.CanProcess(zone)) return true;
        // It may have disappeared since planning; do not count that as a FreshWorld reset.
        if (!GameWorld.IsGenerated(zone))
        {
            _tracker.Skip(zone);
            return true;
        }
        _mutationAttempted = true;
        var success = base.ExecuteZone(zone);
        if (success) _tracker.Changed(zone);
        return success;
    }

    protected override IEnumerator OnExecute()
    {
        Result.Started = true;
        return _tracker.Execute(ZonesToUpgrade, ExecuteZone, () => Failed++);
    }

    protected override void OnEnd()
    {
        _endAttempted = true;
        base.OnEnd();
        _tracker.Finish(Failed);
    }

    public void Cleanup()
    {
        try
        {
            // Complete border repair after a partial abort, only in the world we actually changed.
            if (!_endAttempted && _mutationAttempted && _tracker.IsSameWorld)
            {
                _endAttempted = true;
                base.OnEnd();
            }
        }
        finally
        {
            _tracker.Cleanup();
        }
    }
}

internal sealed class TrackedResetVegetation : ResetVegetation, ITrackedOperation
{
    private readonly OperationTracker _tracker;
    public ExecutedOperation Executable => this;
    public OperationResult Result => _tracker.Result;

    public TrackedResetVegetation(Action<string> log, HashSet<string> ids, OperationParameters args,
        HashSet<Vector2s>? candidates = null, Func<Vector2s, bool>? canProcess = null,
        Action<OperationResult>? completed = null, int maxZonesPerFrame = 64,
        double frameBudgetMilliseconds = 8) : base(log, RequireIds(ids), args, candidates)
    {
        _tracker = new OperationTracker(candidates, canProcess, completed, maxZonesPerFrame, frameBudgetMilliseconds);
    }

    private static HashSet<string> RequireIds(HashSet<string> ids)
    {
        // UW treats an empty vegetation set as "all". A missing optional mod must never expand scope.
        if (ids == null || ids.Count == 0)
            throw new ArgumentException("At least one resolved vegetation ID is required.", nameof(ids));
        return new HashSet<string>(ids);
    }

    protected override string OnInit()
    {
        ZonesToUpgrade = _tracker.Restrict(ZonesToUpgrade);
        var text = base.OnInit();
        _tracker.Selected(ZonesToUpgrade);
        return text;
    }

    protected override bool ExecuteZone(Vector2s zone)
    {
        if (!_tracker.CanProcess(zone)) return true;
        if (!GameWorld.IsGenerated(zone))
        {
            _tracker.Skip(zone);
            return true;
        }
        var zs = ZoneSystem.instance;
        var originalVegetation = zs.m_vegetation;
        var originalTerrainActive = TerrainResetter.Active;
        var originalTemporaryObjects = zs.IsZoneLoaded(zone) ? OperationTracker.SnapshotTemporaryObjects(zs) : null;
        _tracker.MayLoad(zone);
        try
        {
            var success = base.ExecuteZone(zone);
            if (success) _tracker.Changed(zone);
            return success;
        }
        catch (Exception error)
        {
            try
            {
                if (originalTemporaryObjects != null) OperationTracker.CleanupNewTemporaryObjects(zs, originalTemporaryObjects);
            }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
        finally
        {
            // Generation may fail inside another mod. Restore global generation settings if a mod throws.
            zs.m_vegetation = originalVegetation;
            TerrainResetter.Active = originalTerrainActive;
        }
    }

    protected override IEnumerator OnExecute()
    {
        Result.Started = true;
        return _tracker.Execute(ZonesToUpgrade, ExecuteZone, () => Failed++);
    }

    protected override void OnEnd()
    {
        base.OnEnd();
        _tracker.Finish(Failed);
    }

    public void Cleanup() => _tracker.Cleanup();
}

internal sealed class TrackedRegenerateLocations : RegenerateLocations, ITrackedOperation
{
    private readonly OperationTracker _tracker;
    private readonly HashSet<string> _ids;
    private readonly Dictionary<Vector2s, string> _selectedIds = new();
    public ExecutedOperation Executable => this;
    public OperationResult Result => _tracker.Result;

    public TrackedRegenerateLocations(Action<string> log, HashSet<string> ids, OperationParameters args,
        HashSet<Vector2s>? candidates = null, Func<Vector2s, bool>? canProcess = null,
        Action<OperationResult>? completed = null, int maxZonesPerFrame = 64,
        double frameBudgetMilliseconds = 8) : base(log, RequireIds(ids), args, candidates)
    {
        _ids = new HashSet<string>(ids);
        _tracker = new OperationTracker(candidates, canProcess, completed, maxZonesPerFrame, frameBudgetMilliseconds);
    }

    private static HashSet<string> RequireIds(HashSet<string> ids)
    {
        if (ids == null || ids.Count == 0)
            throw new ArgumentException("At least one resolved location ID is required.", nameof(ids));
        return new HashSet<string>(ids);
    }

    protected override string OnInit()
    {
        ZonesToUpgrade = _tracker.Restrict(ZonesToUpgrade);
        var text = base.OnInit();
        _tracker.Selected(ZonesToUpgrade);
        foreach (var zone in ZonesToUpgrade)
        {
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out var location))
            {
                var name = location.m_location?.m_prefab.Name;
                if (name != null && _ids.Contains(name)) _selectedIds[zone] = name;
            }
        }
        return text;
    }

    private bool IsStillSelected(Vector2s zone, ZoneSystem.LocationInstance location) =>
        location.m_placed && location.m_location?.m_prefab != null &&
        _selectedIds.TryGetValue(zone, out var expected) &&
        string.Equals(expected, location.m_location.m_prefab.Name, StringComparison.Ordinal);

    protected override bool ExecuteZone(Vector2s zone)
    {
        if (!_tracker.CanProcess(zone)) return true;
        if (!GameWorld.IsGenerated(zone) ||
            !ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out var location) ||
            !IsStillSelected(zone, location))
        {
            _tracker.Skip(zone);
            return true;
        }
        _tracker.MayLoad(zone);
        var zs = ZoneSystem.instance;
        var originalTemporaryObjects = zs.IsZoneLoaded(zone) ? OperationTracker.SnapshotTemporaryObjects(zs) : null;
        try
        {
            return base.ExecuteZone(zone);
        }
        catch (Exception error)
        {
            try
            {
                if (originalTemporaryObjects != null) OperationTracker.CleanupNewTemporaryObjects(zs, originalTemporaryObjects);
            }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }

    protected override bool ExecuteLocation(Vector2s zone, ZoneSystem.LocationInstance location)
    {
        if (!IsStillSelected(zone, location))
        {
            _tracker.Skip(zone);
            return false;
        }
        var changed = base.ExecuteLocation(zone, location);
        if (changed) _tracker.Changed(zone);
        return changed;
    }

    protected override IEnumerator OnExecute()
    {
        Result.Started = true;
        return _tracker.Execute(ZonesToUpgrade, ExecuteZone, () => Failed++);
    }

    protected override void OnEnd()
    {
        base.OnEnd();
        _tracker.Finish(Failed);
    }

    public void Cleanup() => _tracker.Cleanup();
}

