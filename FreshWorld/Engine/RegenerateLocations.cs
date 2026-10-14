using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>
/// Resets placed locations using native ghost placement. Extends Upgrade World's public-domain
/// radius/dungeon clearing policy across surface sectors, retaining anchor-only dungeon cleanup.
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
        var footprint = Args.LocationFootprints != null && Args.LocationFootprints.TryGetValue(zone, out var planned)
            ? planned : new LocationFootprint(zone, location);
        if (!footprint.Matches(location)) throw new InvalidOperationException("Location extent changed after planning: " + zone);
        var radius = footprint.Radius;
        ClearLocationObjects(footprint, Args.ProtectEpicLoot, Args.ProtectEpicLootBounties, Args.AlwaysProtected);
        var terrainRadius = Args.TerrainReset > 0 ? Args.TerrainReset : radius;
        if (terrainRadius > 0)
        {
            _terrainTouched = true;
            // Terrain paint samples extend slightly beyond a sector's geometric edge. Never
            // edit a neighboring tile that was not included in the full-location approval.
            TerrainResetter.Execute(location.m_position, terrainRadius,
                sector => footprint.Zones.Contains(sector) && (Args.CanResetTerrain == null || Args.CanResetTerrain(sector)));
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

    private static void ClearLocationObjects(LocationFootprint footprint, bool protectEpicLoot, bool protectEpicLootBounties,
        AlwaysProtectedObjects? alwaysProtected)
    {
        if (footprint.Radius <= 0) return;
        bool WithinScope(ZDO zdo)
        {
            var prefab = zdo.GetPrefab();
            return prefab != ZoneControlHash && prefab != TerrainCompilerHash && footprint.Contains(zdo);
        }
        // Neighbor sectors need not be generated: native location placement can put ZDOs there.
        // The predicate also constrains recursively connected Spawned objects.
        foreach (var sector in footprint.Zones)
            foreach (var zdo in GameWorld.GetZDOs(sector))
                GameWorld.RemoveZDO(zdo, protectEpicLoot, protectEpicLootBounties, alwaysProtected, WithinScope);
    }

    protected override void OnEnd()
    {
        base.OnEnd();
        if (_terrainTouched) GameWorld.RecalculateTerrain();
        Log($"Location reset: {_reset} locations regenerated.");
    }
}
