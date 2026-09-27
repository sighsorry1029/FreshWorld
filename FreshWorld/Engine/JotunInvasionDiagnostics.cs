using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>On-demand, read-only reporting; never used by per-target protection checks.</summary>
internal static class JotunInvasionDiagnostics
{
    public static IReadOnlyList<string> Describe()
    {
        var system = PersistentEventSystem.instance;
        if (system == null || system.m_activePersistentEvents?.list == null || system.m_possibleEvents == null)
            throw new InvalidOperationException("Persistent event data is unavailable.");

        var events = new List<PersistentEventSystem.ActivePersistentEvent>();
        foreach (var active in system.m_activePersistentEvents.list)
        {
            // Do not use internalName: its native getter indexes an unchecked sourceEventId.
            if (active == null || active.sourceEventId < 0 || active.sourceEventId >= system.m_possibleEvents.Count ||
                system.m_possibleEvents[active.sourceEventId] == null)
                throw new InvalidOperationException("Persistent event data contains an invalid source.");
            if (system.m_possibleEvents[active.sourceEventId].internalName != "jotun_invasion") continue;
            if (!Finite(active.position) || !Finite(active.radius) || active.radius <= 0)
                throw new InvalidOperationException("Jotun invasion " + active.eventId + " has an invalid area.");
            events.Add(active);
        }

        // The ready host's ZDO registry includes objects in zones without scene instances.
        // Take one snapshot, then examine only the two objective prefabs. No loading or ownership changes.
        var ice = GameWorld.AllZDOs().Where(zdo => zdo != null && zdo.IsValid() &&
            JotunInvasionProtection.IsProtectedObject(zdo)).ToArray();
        if (ice.Any(zdo => !Finite(zdo.GetPosition())))
            throw new InvalidOperationException("Invasion ice has an invalid position; spatial counts are unavailable.");
        var cores = ice.Where(zdo => zdo.GetPrefab() == "BlackIce_Core".GetStableHashCode()).ToArray();
        var outers = ice.Where(zdo => zdo.GetPrefab() == "BlackIce_Core_outer".GetStableHashCode()).ToArray();
        var lines = new List<string>
        {
            $"Jotun invasion snapshot: active={events.Count}, world core ZDOs={cores.Length}, outer ZDOs={outers.Length}.",
            "Counts use horizontal distance, not event ownership. Overlapping areas may count the same ice. " +
            "Scene counts refer to this host, not client visibility; ZDO presence does not prove a usable objective."
        };
        foreach (var active in events.OrderBy(active => active.eventId))
        {
            var nearbyCores = cores.Where(zdo => Inside(zdo, active)).ToArray();
            var nearbyOuters = outers.Where(zdo => Inside(zdo, active)).ToArray();
            lines.Add(FormattableString.Invariant($"Invasion {active.eventId}: center={Position(active.position)}, radius={active.radius:0.##}m; core ZDOs={nearbyCores.Length} (scene={nearbyCores.Count(InScene)}), outer ZDOs={nearbyOuters.Length} (scene={nearbyOuters.Count(InScene)})."));
            if (nearbyCores.Length == 0)
                lines.Add($"Invasion {active.eventId}: no core ZDO found inside the area. Inspect before repairing or ending the event; nothing was changed.");
            DescribeNearest(lines, active, cores, "core");
            DescribeNearest(lines, active, outers, "outer");
        }
        lines.Add($"Ice outside all active invasion areas: core ZDOs={cores.Count(zdo => !events.Any(active => Inside(zdo, active)))}, " +
            $"outer ZDOs={outers.Count(zdo => !events.Any(active => Inside(zdo, active)))}. No automatic cleanup is performed.");
        return lines;
    }

    private static void DescribeNearest(List<string> lines, PersistentEventSystem.ActivePersistentEvent active, ZDO[] objects, string kind)
    {
        var nearest = objects.OrderBy(zdo => DistanceSquared(zdo.GetPosition(), active.position)).FirstOrDefault();
        if (nearest == null) return;
        var position = nearest.GetPosition();
        var zone = ZoneSystem.GetZone(position);
        lines.Add(FormattableString.Invariant($"Invasion {active.eventId}: nearest {kind} ZDO={nearest.m_uid}, position={Position(position)}, zone=({zone.x},{zone.y}), distance={Math.Sqrt(DistanceSquared(position, active.position)):0.##}m, inside area={Inside(nearest, active)}, scene present={InScene(nearest)}."));
    }

    private static bool InScene(ZDO zdo) => ZNetScene.instance.FindInstance(zdo) != null;
    private static bool Inside(ZDO zdo, PersistentEventSystem.ActivePersistentEvent active) =>
        DistanceSquared(zdo.GetPosition(), active.position) <= (double)active.radius * active.radius;
    private static double DistanceSquared(Vector3 left, Vector3 right)
    {
        var x = (double)left.x - right.x;
        var z = (double)left.z - right.z;
        return x * x + z * z;
    }
    private static string Position(Vector3 value) => FormattableString.Invariant($"({value.x:0.##},{value.y:0.##},{value.z:0.##})");
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
