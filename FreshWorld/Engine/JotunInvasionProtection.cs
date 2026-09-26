using System;
using System.Collections.Generic;

namespace FreshWorld.Engine;

/// <summary>Vanilla invasion areas observed during one maintenance run, independent of base markers.</summary>
internal sealed class JotunInvasionProtection
{
    private static readonly int Core = "BlackIce_Core".GetStableHashCode();
    private static readonly int Outer = "BlackIce_Core_outer".GetStableHashCode();
    private readonly HashSet<(float X, float Z, float Radius)> _observed = new();

    // Native destruction completes the event and creates its reward. FreshWorld's raw ZDO deletion
    // does neither. Keep this final guard even if event metadata is missing or a spawned link crosses zones.
    internal static bool IsProtectedObject(ZDO zdo) => zdo.GetPrefab() == Core || zdo.GetPrefab() == Outer;

    public int Capture()
    {
        var system = PersistentEventSystem.instance;
        if (system == null || system.m_activePersistentEvents?.list == null || system.m_possibleEvents == null)
            throw new InvalidOperationException("Jotun invasion protection requires the world's persistent event data.");

        foreach (var active in system.m_activePersistentEvents.list)
        {
            // The native internalName getter indexes the source list without checking its bounds.
            if (active == null || active.sourceEventId < 0 || active.sourceEventId >= system.m_possibleEvents.Count ||
                system.m_possibleEvents[active.sourceEventId] == null)
                throw new InvalidOperationException("Persistent event data contains an invalid source; maintenance cannot continue safely.");
            if (!string.Equals(system.m_possibleEvents[active.sourceEventId].internalName, "jotun_invasion", StringComparison.Ordinal))
                continue;
            var position = active.position;
            if (!IsFinite(position.x) || !IsFinite(position.z) || !IsFinite(active.radius) || active.radius <= 0)
                throw new InvalidOperationException("Jotun invasion has an invalid area; maintenance cannot continue safely.");

            // Store values, not mutable event objects. Completion or movement cannot release an area
            // halfway through this run; a new pipeline starts with a fresh set on the next run.
            _observed.Add((position.x, position.z, active.radius));
        }
        return _observed.Count;
    }

    public bool CanResetZone(Vector2s zone)
    {
        Capture(); // New invasions can start while the coroutine yields or waits for zone loading.
        foreach (var area in _observed)
        {
            // Circle against the entire 64m square, including touching edges/corners. Testing only
            // the zone center misses peripheral zones; double arithmetic avoids coordinate overflow.
            var dx = Math.Max(0, Math.Abs((double)area.X - zone.x * 64.0) - 32.0);
            var dz = Math.Max(0, Math.Abs((double)area.Z - zone.y * 64.0) - 32.0);
            if (dx * dx + dz * dz <= (double)area.Radius * area.Radius) return false;
        }
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
