using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>
/// Replays native vegetation placement for exact selected prefabs. The reset sequence follows
/// Upgrade World's public-domain ResetVegetation/VegetationOperation algorithms; no UW assembly is used.
/// </summary>
internal class ResetVegetation : ZoneOperation
{
    private readonly HashSet<string> _ids;
    private readonly HashSet<int> _hashes;
    private List<ZoneSystem.ZoneVegetation> _vegetation = new();
    private int _removed;
    private int _spawned;
    private bool _terrainTouched;

    public ResetVegetation(Action<string> log, HashSet<string> ids, OperationParameters args,
        HashSet<Vector2s>? candidates = null) : base(log, args, candidates)
    {
        _ids = NativePlacement.RequireIds(ids);
        _hashes = new HashSet<int>(_ids.Select(id => id.GetStableHashCode()));
        foreach (var id in _ids)
        {
            var fraction = id + "_frac";
            if (ZNetScene.instance.GetPrefab(fraction) != null) _hashes.Add(fraction.GetStableHashCode());
        }
    }

    protected override void OnStart()
    {
        base.OnStart();
        // Retain the whole ordered registry and alter cloned enable flags only. Native placement
        // therefore receives the same generation definitions without mutating another mod's registry.
        _vegetation = ZoneSystem.instance.m_vegetation.Select(vegetation =>
        {
            var clone = vegetation.Clone();
            clone.m_enable = clone.m_prefab != null && _ids.Contains(clone.m_prefab.name);
            return clone;
        }).ToList();
    }

    protected override bool ExecuteZone(Vector2s zone)
    {
        if (!ZoneSystem.instance.IsZoneLoaded(zone) || !GameWorld.TryGetRoot(zone, out var root))
        {
            GameWorld.PokeZone(zone);
            return false;
        }
        var heightmap = root.GetComponentInChildren<Heightmap>();
        if (heightmap == null) throw new InvalidOperationException("Loaded vegetation zone has no heightmap: " + zone);

        foreach (var zdo in GameWorld.GetZDOs(zone))
        {
            if (!_hashes.Contains(zdo.GetPrefab())) continue;
            GameWorld.RemoveZDO(zdo, Args.ProtectEpicLoot);
            _removed++;
        }

        var zones = ZoneSystem.instance;
        var originalVegetation = zones.m_vegetation;
        var originalTerrainActive = TerrainResetter.Active;
        var temporary = NativePlacement.TemporaryObjects(zones);
        var originalObjects = new HashSet<GameObject>(temporary);
        try
        {
            zones.m_vegetation = _vegetation;
            TerrainResetter.Active = Args.TerrainReset > 0;
            NativePlacement.PlaceVegetation(zones, zone, root.transform, heightmap, GetClearAreas(zone), temporary);
            foreach (var spawned in temporary.Where(obj => !originalObjects.Contains(obj)).ToArray())
            {
                if (spawned == null) continue;
                _spawned++;
                if (Args.TerrainReset > 0)
                {
                    _terrainTouched = true;
                    TerrainResetter.Execute(spawned.transform.position, Args.TerrainReset);
                }
            }
            NativePlacement.DestroyCreatedObjects(temporary, originalObjects);
        }
        finally
        {
            zones.m_vegetation = originalVegetation;
            TerrainResetter.Active = originalTerrainActive;
        }
        // The tracking wrapper releases only zones loaded by this maintenance operation.
        return true;
    }

    private static IList GetClearAreas(Vector2s zone)
    {
        var areas = NativePlacement.CreateClearAreas();
        if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out var location) &&
            location.m_location != null && location.m_location.m_clearArea)
            areas.Add(NativePlacement.CreateClearArea(location.m_position, location.m_location.m_exteriorRadius));
        return areas;
    }

    protected override void OnEnd()
    {
        base.OnEnd();
        if (_terrainTouched) GameWorld.RecalculateTerrain();
        Log($"Vegetation reset: {_removed} objects removed, {_spawned} vegetation objects created.");
    }
}
