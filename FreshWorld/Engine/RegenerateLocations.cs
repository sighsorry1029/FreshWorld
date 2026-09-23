using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>
/// Resets placed locations using native ghost placement. The radius/dungeon clearing policy follows
/// Upgrade World's public-domain RegenerateLocations and ClearZDOsWithinDistance implementation.
/// </summary>
internal class RegenerateLocations : ZoneOperation
{
    private static readonly int ZoneControlHash = "_ZoneCtrl".GetStableHashCode();
    private static readonly int TerrainCompilerHash = "_TerrainCompiler".GetStableHashCode();
    private readonly HashSet<string> _ids;
    private int _reset;
    private bool _terrainTouched;

    public RegenerateLocations(Action<string> log, HashSet<string> ids, OperationParameters args,
        HashSet<Vector2s>? candidates = null) : base(log, args, candidates) =>
        _ids = NativePlacement.RequireIds(ids);

    protected override string OnInit()
    {
        var locations = ZoneSystem.instance.m_locationInstances;
        ZonesToUpgrade = ZonesToUpgrade.Where(zone => locations.TryGetValue(zone, out var location) && IsSelected(location)).ToArray();
        return base.OnInit();
    }

    private bool IsSelected(ZoneSystem.LocationInstance location) =>
        location.m_placed && NativePlacement.IsValidLocationPrefab(location.m_location) &&
        _ids.Contains(location.m_location.m_prefab.Name);

    protected override bool ExecuteZone(Vector2s zone)
    {
        var zones = ZoneSystem.instance;
        if (!zones.m_locationInstances.TryGetValue(zone, out var location) || !IsSelected(location)) return true;
        if (!zones.IsZoneLoaded(zone) || !GameWorld.TryGetRoot(zone, out _) ||
            !NativePlacement.CanSpawnLocation(zones, location.m_location))
        {
            GameWorld.PokeZone(zone);
            return false;
        }
        if (ExecuteLocation(zone, location)) _reset++;
        return true;
    }

    protected virtual bool ExecuteLocation(Vector2s zone, ZoneSystem.LocationInstance location)
    {
        if (!IsSelected(location)) return false;
        if (!GameWorld.TryGetRoot(zone, out var root))
            throw new InvalidOperationException("Location zone became unavailable before reset: " + zone);
        var heightmap = root.GetComponentInChildren<Heightmap>();
        if (heightmap == null) throw new InvalidOperationException("Loaded location zone has no heightmap: " + zone);

        var zones = ZoneSystem.instance;
        var radius = location.m_location.m_exteriorRadius;
        ClearLocationObjects(zone, location.m_position, radius, Args.ProtectEpicLoot, Args.ProtectEpicLootBounties);
        var terrainRadius = Args.TerrainReset > 0 ? Args.TerrainReset : radius;
        if (terrainRadius > 0)
        {
            _terrainTouched = true;
            TerrainResetter.Execute(location.m_position, terrainRadius);
        }

        location.m_placed = false;
        zones.m_locationInstances[zone] = location;
        var temporary = NativePlacement.TemporaryObjects(zones);
        var originalObjects = new HashSet<GameObject>(temporary);
        var originalTerrainActive = TerrainResetter.Active;
        try
        {
            TerrainResetter.Active = terrainRadius > 0;
            NativePlacement.PlaceLocations(zones, zone, root.transform, heightmap, NativePlacement.CreateClearAreas(), temporary);
            NativePlacement.DestroyCreatedObjects(temporary, originalObjects);
        }
        finally
        {
            TerrainResetter.Active = originalTerrainActive;
        }
        return true;
    }

    private static void ClearLocationObjects(Vector2s zone, Vector3 center, float radius, bool protectEpicLoot, bool protectEpicLootBounties)
    {
        if (radius <= 0) return;
        var squaredRadius = radius * radius;
        // Dungeon rooms live far above the surface in the same sector. Preserve its control and
        // terrain records; GameWorld.RemoveZDO also excludes local and connected player characters.
        foreach (var zdo in GameWorld.GetZDOs(zone))
        {
            var prefab = zdo.GetPrefab();
            if (prefab == ZoneControlHash || prefab == TerrainCompilerHash) continue;
            var position = zdo.GetPosition();
            var dx = position.x - center.x;
            var dz = position.z - center.z;
            if (position.y > 4000f || dx * dx + dz * dz < squaredRadius)
                GameWorld.RemoveZDO(zdo, protectEpicLoot, protectEpicLootBounties);
        }
    }

    protected override void OnEnd()
    {
        base.OnEnd();
        if (_terrainTouched) GameWorld.RecalculateTerrain();
        Log($"Location reset: {_reset} locations regenerated.");
    }
}
