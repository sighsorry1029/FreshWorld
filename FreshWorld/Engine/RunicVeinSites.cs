using System;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>One stable site per PoV cell, independent of zone streaming order or blockers.</summary>
internal static class RunicVeinSites
{
    internal const int Size = 128;
    internal const int SettleDistance = 20;
    internal static bool CouldBelongToCell(Vector3 p, int x, int z) =>
        p.x >= x - SettleDistance && p.x < x + Size + SettleDistance &&
        p.z >= z - SettleDistance && p.z < z + Size + SettleDistance;

    internal static void Visit(int seed, Vector2s zone, Func<float, float, Heightmap.Biome> biomeAt,
        Func<Heightmap.Biome, float, object?> pick, Func<float, float, Heightmap.Biome, float?> groundAt,
        Action<object, Vector3, float, int, int> visit)
    {
        // Valheim centers 64 m zones on multiples of 64. A zone can intersect up to four cells.
        var center = ZoneSystem.GetZonePos(zone);
        for (var cx = Cell(center.x - 32); cx <= Cell(center.x + 31); ++cx)
        for (var cz = Cell(center.z - 32); cz <= Cell(center.z + 31); ++cz)
        {
            var x = cx * Size;
            var z = cz * Size;
            var midX = x + Size / 2;
            var midZ = z + Size / 2;
            if ((long)midX * midX + (long)midZ * midZ > 10200L * 10200) continue;
            var biome = biomeAt(midX, midZ);
            var rng = unchecked(((uint)seed * 2654435761u ^ (uint)(cx * 40503) ^
                (uint)(cz * -2048144777) ^ 0x5EEDu) | 1u);
            var definition = pick(biome, Roll(ref rng));
            if (definition == null) continue;
            for (var attempt = 0; attempt < 8; ++attempt)
            {
                var px = x + Roll(ref rng) * Size;
                var pz = z + Roll(ref rng) * Size;
                if (biomeAt(px, pz) != biome) continue;
                var y = groundAt(px, pz, biome);
                if (!y.HasValue) continue;
                var point = new Vector3(px, y.Value, pz);
                if (ZoneSystem.GetZone(point) == zone) visit(definition, point, Roll(ref rng) * 360, x, z);
                // A valid site belongs exclusively to its owner, even if that zone has not generated
                // yet or the visitor rejects it because of an existing deposit or building.
                break;
            }
        }
    }

    private static int Cell(float coordinate) => (int)Math.Floor(coordinate / Size);

    private static float Roll(ref uint state)
    {
        state = unchecked(state * 1664525u + 1013904223u);
        return (state >> 8) / 16777216f;
    }
}
