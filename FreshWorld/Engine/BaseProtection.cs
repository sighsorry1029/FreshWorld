using System;
using System.Collections.Generic;
using System.Linq;

namespace FreshWorld.Engine;

/// <summary>Base marker rules adapted from Upgrade World; no external mod/config is consulted.</summary>
internal static class BaseProtection
{
    private static HashSet<int> playerObjects = new();
    private static HashSet<int> unconditionalObjects = new();
    private static HashSet<Vector2i> excluded = new();
    private static DateTime calculatedAt = DateTime.MinValue;
    private static int lastSize = -1;

    public static void Configure(IEnumerable<string> placedObjects, IEnumerable<string> alwaysProtectedObjects)
    {
        playerObjects = new HashSet<int>(placedObjects.Select(id => id.GetStableHashCode()));
        unconditionalObjects = new HashSet<int>(alwaysProtectedObjects.Select(id => id.GetStableHashCode()));
        InvalidateCache();
    }

    public static HashSet<Vector2i> GetExcluded(int size)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (lastSize == size && DateTime.UtcNow - calculatedAt < TimeSpan.FromSeconds(10)) return excluded;
        var next = new HashSet<Vector2i>();
        if (size > 0)
        {
            var adjacent = size - 1;
            foreach (var zdo in GameWorld.AllZDOs())
            {
                var prefab = zdo.GetPrefab();
                if (!unconditionalObjects.Contains(prefab) &&
                    !(playerObjects.Contains(prefab) && zdo.GetLong(ZDOVars.s_creator) != 0L)) continue;
                var zone = ZoneSystem.GetZone(zdo.GetPosition());
                for (var x = -adjacent; x <= adjacent; x++)
                    for (var y = -adjacent; y <= adjacent; y++)
                        next.Add(new Vector2i(zone.x + x, zone.y + y));
            }
        }
        excluded = next;
        lastSize = size;
        calculatedAt = DateTime.UtcNow;
        return excluded;
    }

    public static void InvalidateCache()
    {
        calculatedAt = DateTime.MinValue;
        lastSize = -1;
        excluded = new HashSet<Vector2i>();
    }
}
