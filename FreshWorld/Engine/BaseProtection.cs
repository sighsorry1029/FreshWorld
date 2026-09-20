using System;
using System.Collections.Generic;
using System.Linq;

namespace FreshWorld.Engine;

/// <summary>Player-built Pieces outside the blacklist and tombstones protect zones without loading their scene objects.</summary>
internal static class BaseProtection
{
    private static HashSet<int> pieceBlacklist = new();
    private static HashSet<int> unconditionalObjects = new();
    private static HashSet<Vector2s> excluded = new();
    private static DateTime calculatedAt = DateTime.MinValue;
    private static int lastSize = -1;

    public static void Configure(IEnumerable<string> blacklistedPieces, IEnumerable<string> alwaysProtectedObjects)
    {
        pieceBlacklist = new HashSet<int>(blacklistedPieces.Select(id => id.GetStableHashCode()));
        unconditionalObjects = new HashSet<int>(alwaysProtectedObjects.Select(id => id.GetStableHashCode()));
        InvalidateCache();
    }

    public static HashSet<Vector2s> GetExcluded(int size)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (lastSize == size && DateTime.UtcNow - calculatedAt < TimeSpan.FromSeconds(10)) return excluded;
        var next = new HashSet<Vector2s>();
        if (size > 0)
        {
            var scene = ZNetScene.instance;
            if (scene == null) throw new InvalidOperationException("Base protection requires the world's prefab registry.");
            // Classify each prefab once per scan, including misses. A fresh scan observes registry
            // changes without retaining Unity objects or classifications across stages/worlds.
            // Seed exclusions as false so they never need prefab or creator lookups.
            var playerPrefabs = pieceBlacklist.ToDictionary(prefab => prefab, _ => false);
            bool IsPlayerPrefab(int prefab)
            {
                if (playerPrefabs.TryGetValue(prefab, out var isPiece)) return isPiece;
                var template = scene.GetPrefab(prefab);
                return playerPrefabs[prefab] = template != null && template.GetComponent<Piece>() != null;
            }
            var markerZones = new HashSet<Vector2s>();
            var adjacent = size - 1;
            foreach (var zdo in GameWorld.AllZDOs())
            {
                var prefab = zdo.GetPrefab();
                if (!unconditionalObjects.Contains(prefab) &&
                    !(IsPlayerPrefab(prefab) && zdo.GetLong(ZDOVars.s_creator) != 0L)) continue;
                var zone = ZoneSystem.GetZone(zdo.GetPosition());
                // A base may contain thousands of Pieces in one zone; expand its range only once.
                if (!markerZones.Add(zone)) continue;
                for (var x = Math.Max(short.MinValue, zone.x - adjacent); x <= Math.Min(short.MaxValue, zone.x + adjacent); x++)
                    for (var y = Math.Max(short.MinValue, zone.y - adjacent); y <= Math.Min(short.MaxValue, zone.y + adjacent); y++)
                        next.Add(new Vector2s(x, y));
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
        excluded = new HashSet<Vector2s>();
    }
}
