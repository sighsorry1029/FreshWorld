using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>A run-local location extent, shared by selection, protection and deletion.</summary>
internal sealed class LocationFootprint
{
    public Vector2s Anchor { get; }
    public Vector3 Center { get; }
    public float Radius { get; }
    public HashSet<Vector2s> Zones { get; }
    private readonly ZoneSystem.ZoneLocation _definition;
    private readonly string _id;

    public LocationFootprint(Vector2s anchor, ZoneSystem.LocationInstance location, float minimumRadius = 0)
    {
        Anchor = anchor;
        Center = location.m_position;
        _definition = location.m_location;
        _id = _definition.m_prefab.Name;
        Radius = Math.Max(minimumRadius, _definition.m_exteriorRadius);
        Zones = Sectors(Center, Radius);
        // The native proxy and elevated dungeon belong to the registered anchor.
        Zones.Add(anchor);
    }

    public bool Matches(ZoneSystem.LocationInstance location) =>
        ReferenceEquals(_definition, location.m_location) &&
        string.Equals(_id, location.m_location.m_prefab.Name, StringComparison.Ordinal) &&
        Center.x == location.m_position.x && Center.y == location.m_position.y && Center.z == location.m_position.z &&
        location.m_location.m_exteriorRadius >= 0 && location.m_location.m_exteriorRadius <= Radius;

    public bool Overlaps(LocationFootprint other)
    {
        var dx = (double)Center.x - other.Center.x;
        var dz = (double)Center.z - other.Center.z;
        var radius = (double)Radius + other.Radius;
        return dx * dx + dz * dz < radius * radius;
    }

    public bool Contains(ZDO zdo)
    {
        var position = zdo.GetPosition();
        // Adjacent sectors can contain another location's elevated dungeon.
        if (position.y > 4000f) return ZoneSystem.GetZone(position) == Anchor;
        var dx = (double)position.x - Center.x;
        var dz = (double)position.z - Center.z;
        return dx * dx + dz * dz < (double)Radius * Radius;
    }

    internal static HashSet<Vector2s> Sectors(Vector3 center, float radius)
    {
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0 ||
            float.IsNaN(center.x) || float.IsInfinity(center.x) ||
            float.IsNaN(center.y) || float.IsInfinity(center.y) ||
            float.IsNaN(center.z) || float.IsInfinity(center.z))
            throw new InvalidOperationException("Invalid location position or radius; maintenance cannot safely determine its extent.");
        var minX = Math.Floor(((double)center.x - radius + 32) / 64);
        var maxX = Math.Floor(((double)center.x + radius + 32) / 64);
        var minZ = Math.Floor(((double)center.z - radius + 32) / 64);
        var maxZ = Math.Floor(((double)center.z + radius + 32) / 64);
        if (minX < short.MinValue || maxX > short.MaxValue || minZ < short.MinValue || maxZ > short.MaxValue ||
            (maxX - minX + 1) * (maxZ - minZ + 1) > 4096)
            throw new InvalidOperationException("Location extent exceeds the supported sector range.");
        var result = new HashSet<Vector2s>();
        for (var x = (int)minX; x <= maxX; x++)
            for (var z = (int)minZ; z <= maxZ; z++)
            {
                var dx = Math.Max(0, Math.Abs((double)center.x - x * 64) - 32);
                var dz = Math.Max(0, Math.Abs((double)center.z - z * 64) - 32);
                if (dx * dx + dz * dz <= (double)radius * radius) result.Add(new Vector2s(x, z));
            }
        return result;
    }
}
