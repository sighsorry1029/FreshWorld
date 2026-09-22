using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FreshWorld.Engine;

namespace FreshWorld.Backend;

/// <summary>Selection, mutation and skip results reported by the maintenance pipeline.</summary>
internal sealed class OperationResult
{
    public HashSet<Vector2s> SelectedZones { get; } = new();
    public HashSet<Vector2s> ChangedZones { get; } = new();
    public HashSet<Vector2s> SkippedZones { get; } = new();
    public HashSet<Vector2s> TimedOutZones { get; } = new();
    public double LoadWaitSeconds { get; internal set; }
    public int FailedCount { get; internal set; }
    public bool Started { get; internal set; }
    public bool Finished { get; internal set; }
}

internal interface ITrackedOperation
{
    void Init();
    IEnumerator Execute();
    OperationResult Result { get; }
    void Cleanup();
}

/// <summary>
/// Keeps retry/result bookkeeping independent of the scheduler. The caller must hold an
/// exclusive maintenance lease until execution and Cleanup both finish: generation uses shared game state.
/// </summary>
internal sealed class OperationTracker
{
    private const int DefaultLoadTimeoutMilliseconds = 30000;
    private readonly Func<Vector2s, bool>? _canProcess;
    private readonly Action<string>? _warn;
    private readonly HashSet<Vector2s> _pendingLoads = new();
    private readonly int _maxZonesPerFrame;
    private readonly double _frameBudgetMilliseconds;
    private readonly ZNet _worldNet;
    private readonly ZoneSystem _worldZones;
    private readonly long _worldUid;
    private readonly int _loadTimeoutMilliseconds;
    public OperationResult Result { get; } = new();

    public OperationTracker(Func<Vector2s, bool>? canProcess, int maxZonesPerFrame, double frameBudgetMilliseconds,
        Action<string>? warn = null, int loadTimeoutMilliseconds = DefaultLoadTimeoutMilliseconds)
    {
        if (maxZonesPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(maxZonesPerFrame));
        if (frameBudgetMilliseconds <= 0 || double.IsNaN(frameBudgetMilliseconds) || double.IsInfinity(frameBudgetMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(frameBudgetMilliseconds));
        if (loadTimeoutMilliseconds < 1) throw new ArgumentOutOfRangeException(nameof(loadTimeoutMilliseconds));
        _canProcess = canProcess;
        _warn = warn;
        _maxZonesPerFrame = maxZonesPerFrame;
        _frameBudgetMilliseconds = frameBudgetMilliseconds;
        _loadTimeoutMilliseconds = loadTimeoutMilliseconds;
        _worldNet = ZNet.instance;
        _worldZones = ZoneSystem.instance;
        _worldUid = _worldNet.GetWorldUID();
    }

    public static HashSet<UnityEngine.GameObject> SnapshotTemporaryObjects(ZoneSystem zones) =>
        new(NativePlacement.TemporaryObjects(zones));

    public static void CleanupNewTemporaryObjects(ZoneSystem zones, HashSet<UnityEngine.GameObject> original)
    {
        var temporary = NativePlacement.TemporaryObjects(zones);
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

    public IEnumerator Execute(Vector2s[] zones, Func<Vector2s, bool> execute)
    {
        Result.Started = true;
        var frame = Stopwatch.StartNew();
        var waiting = new Stopwatch();
        var attempts = 0;
        for (var index = 0; index < zones.Length; index++)
        {
            var zone = zones[index];
            waiting.Restart();
            var waitedForLoad = false;
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
                    if (waitedForLoad) Result.LoadWaitSeconds += waiting.Elapsed.TotalSeconds;
                    Release(zone);
                    break;
                }
                waitedForLoad = true;
                if (waiting.ElapsedMilliseconds >= _loadTimeoutMilliseconds)
                {
                    Result.LoadWaitSeconds += waiting.Elapsed.TotalSeconds;
                    Result.TimedOutZones.Add(zone);
                    Result.SkippedZones.Add(zone);
                    _warn?.Invoke($"Zone {zone.x},{zone.y} did not become ready after {waiting.Elapsed.TotalSeconds:F1}s " +
                        $"({GameWorld.DescribeZoneLoad(zone)}); skipped. Any FreshWorld-owned root will be cleaned up when safe.");
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

    public void Finish()
    {
        // Completion requires the execution loop to have started.
        Result.FailedCount = Result.Started ? 0 : 1;
        Result.Finished = Result.Started;
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
    public OperationResult Result => _tracker.Result;

    public TrackedResetZones(Action<string> log, OperationParameters args,
        HashSet<Vector2s>? candidates = null, Func<Vector2s, bool>? canProcess = null,
        int maxZonesPerFrame = 64,
        double frameBudgetMilliseconds = 8, Action<string>? warn = null) : base(log, args, candidates)
    {
        _tracker = new OperationTracker(canProcess, maxZonesPerFrame, frameBudgetMilliseconds, warn);
    }

    protected override string OnInit()
    {
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

    protected override IEnumerator OnExecute() => _tracker.Execute(ZonesToUpgrade, ExecuteZone);

    protected override void OnEnd()
    {
        _endAttempted = true;
        base.OnEnd();
        _tracker.Finish();
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
    public OperationResult Result => _tracker.Result;

    public TrackedResetVegetation(Action<string> log, HashSet<string> ids, OperationParameters args,
        HashSet<Vector2s>? candidates = null, Func<Vector2s, bool>? canProcess = null,
        int maxZonesPerFrame = 64,
        double frameBudgetMilliseconds = 8, Action<string>? warn = null) : base(log, RequireIds(ids), args, candidates)
    {
        _tracker = new OperationTracker(canProcess, maxZonesPerFrame, frameBudgetMilliseconds, warn);
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

    protected override IEnumerator OnExecute() => _tracker.Execute(ZonesToUpgrade, ExecuteZone);

    protected override void OnEnd()
    {
        base.OnEnd();
        _tracker.Finish();
    }

    public void Cleanup() => _tracker.Cleanup();
}

internal sealed class TrackedRegenerateLocations : RegenerateLocations, ITrackedOperation
{
    private readonly OperationTracker _tracker;
    private readonly HashSet<string> _ids;
    private readonly Dictionary<Vector2s, string> _selectedIds = new();
    public OperationResult Result => _tracker.Result;

    public TrackedRegenerateLocations(Action<string> log, HashSet<string> ids, OperationParameters args,
        HashSet<Vector2s>? candidates = null, Func<Vector2s, bool>? canProcess = null,
        int maxZonesPerFrame = 64,
        double frameBudgetMilliseconds = 8, Action<string>? warn = null) : base(log, RequireIds(ids), args, candidates)
    {
        _ids = new HashSet<string>(ids);
        _tracker = new OperationTracker(canProcess, maxZonesPerFrame, frameBudgetMilliseconds, warn);
    }

    private static HashSet<string> RequireIds(HashSet<string> ids)
    {
        if (ids == null || ids.Count == 0)
            throw new ArgumentException("At least one resolved location ID is required.", nameof(ids));
        return new HashSet<string>(ids);
    }

    protected override string OnInit()
    {
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

    protected override IEnumerator OnExecute() => _tracker.Execute(ZonesToUpgrade, ExecuteZone);

    protected override void OnEnd()
    {
        base.OnEnd();
        _tracker.Finish();
    }

    public void Cleanup() => _tracker.Cleanup();
}

