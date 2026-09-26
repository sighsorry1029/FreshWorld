using System.Collections.Generic;
using System.Linq;

namespace FreshWorld.Engine;

/// <summary>Exact prefab protection captured for one run, independent of base markers and quest policies.</summary>
internal sealed class AlwaysProtectedObjects
{
    private readonly HashSet<int> _prefabs;
    private readonly HashSet<Vector2s> _observed = new();

    public AlwaysProtectedObjects(IEnumerable<string> prefabs) =>
        _prefabs = new HashSet<int>(prefabs.Select(id => id.GetStableHashCode()));

    public bool IsProtectedObject(ZDO zdo) => zdo.IsValid() && _prefabs.Contains(zdo.GetPrefab());

    public int Capture()
    {
        if (_prefabs.Count == 0) return 0;
        foreach (var zdo in GameWorld.AllZDOs()) Observe(zdo);
        return _observed.Count;
    }

    public bool CanResetZone(Vector2s zone)
    {
        if (_prefabs.Count == 0) return true;
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
