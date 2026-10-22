using System;
using System.Collections.Generic;
using System.Linq;

namespace FreshWorld.Engine;

/// <summary>Absolute protection captured for one run, independent of base-marker ranges.</summary>
internal sealed class AlwaysProtectedObjects
{
    private readonly HashSet<int> _prefabs;
    private readonly Func<ZDO, bool>? _additionalProtection;
    private readonly HashSet<Vector2s> _observed = new();

    public AlwaysProtectedObjects(IEnumerable<string> prefabs, Func<ZDO, bool>? additionalProtection = null)
    {
        _prefabs = new HashSet<int>(prefabs.Select(id => id.GetStableHashCode()));
        _additionalProtection = additionalProtection;
    }

    public bool IsProtectedObject(ZDO zdo) => zdo.IsValid() &&
        (_prefabs.Contains(zdo.GetPrefab()) || _additionalProtection?.Invoke(zdo) == true);

    public int Capture()
    {
        if (_prefabs.Count == 0 && _additionalProtection == null) return 0;
        foreach (var zdo in GameWorld.AllZDOs()) Observe(zdo);
        return _observed.Count;
    }

    public bool CanResetZone(Vector2s zone)
    {
        if (_prefabs.Count == 0 && _additionalProtection == null) return true;
        if (_observed.Contains(zone)) return false;
        // Includes unloaded objects and sees arrivals while an operation was waiting for loading.
        foreach (var zdo in GameWorld.GetZDOs(zone))
            if (Observe(zdo)) return false;
        return true;
    }

    private bool Observe(ZDO zdo)
    {
        if (!IsProtectedObject(zdo)) return false;
        _observed.Add(ZoneSystem.GetZone(zdo.GetPosition()));
        return true;
    }
}
