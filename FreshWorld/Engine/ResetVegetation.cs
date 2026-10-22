using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>
/// Replays native vegetation placement for exact selected prefabs. The reset sequence follows
/// Upgrade World's public-domain ResetVegetation/VegetationOperation algorithms; no UW assembly is used.
/// </summary>
internal class ResetVegetation : ZoneOperation
{
    private readonly HashSet<string> _ids;
    private FieldInfo? _expandWorldPrefabs;
    private int _removed;
    private int _spawned;
    private bool _terrainTouched;

    public ResetVegetation(Action<string> log, HashSet<string> ids, OperationParameters args,
        HashSet<Vector2s>? candidates = null) : base(log, args, candidates)
    {
        _ids = NativePlacement.RequireIds(ids);
    }

    protected override void OnStart()
    {
        base.OnStart();
        // Cache only the access contract for this operation. EWD can replace its dictionary on reload.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var type = assemblies
            .Select(assembly => assembly.GetType("ExpandWorldData.Vegetation.VegetationSpawning"))
            .FirstOrDefault(candidate => candidate != null);
        if (type == null)
        {
            if (assemblies.Any(assembly => assembly.GetName().Name == "ExpandWorldData"))
                throw new InvalidOperationException("Cannot locate Expand World Data vegetation metadata; resource reset stopped before deletion.");
            return;
        }
        _expandWorldPrefabs = type.GetField("Prefabs", BindingFlags.Public | BindingFlags.Static);
        if (_expandWorldPrefabs?.FieldType != typeof(Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>>))
            throw new InvalidOperationException("Cannot verify Expand World Data vegetation variants; resource reset stopped before deletion.");
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

        PathOfValheimAzurite.EnsureVegetation(ZoneSystem.instance);

        // Keep original identities: EWD stores data, scale and clearance against these objects.
        // Native placement seeds by prefab, not registry index. Preserve relative order, and do
        // not enable disabled definitions or delete objects that have no enabled replacement.
        // Read again after loading retries so YAML reloads cannot leave stale metadata references.
        var vegetation = ZoneSystem.instance.m_vegetation
            .Where(entry => entry.m_enable && entry.m_prefab != null && _ids.Contains(entry.m_prefab.name)).ToList();
        ValidateVariants(vegetation);
        if (vegetation.Count == 0) return true;
        var hashes = new HashSet<int>();
        foreach (var entry in vegetation)
        {
            var id = entry.m_prefab.name;
            hashes.Add(id.GetStableHashCode());
            var fraction = id + "_frac";
            if (ZNetScene.instance.GetPrefab(fraction) != null) hashes.Add(fraction.GetStableHashCode());
        }
        foreach (var zdo in GameWorld.GetZDOs(zone))
        {
            if (!hashes.Contains(zdo.GetPrefab())) continue;
            GameWorld.RemoveZDO(zdo, Args.ProtectEpicLoot, Args.ProtectEpicLootBounties, Args.AlwaysProtected, Args.CanDeleteObject);
            _removed++;
        }

        var zones = ZoneSystem.instance;
        var originalVegetation = zones.m_vegetation;
        var originalTerrainActive = TerrainResetter.Active;
        var temporary = NativePlacement.TemporaryObjects(zones);
        var originalObjects = new HashSet<GameObject>(temporary);
        try
        {
            zones.m_vegetation = vegetation;
            TerrainResetter.Active = Args.TerrainReset > 0;
            NativePlacement.PlaceVegetation(zones, zone, root.transform, heightmap, GetClearAreas(zone), temporary);
            foreach (var spawned in temporary.Where(obj => !originalObjects.Contains(obj)).ToArray())
            {
                if (spawned == null) continue;
                _spawned++;
                if (Args.TerrainReset > 0)
                {
                    _terrainTouched = true;
                    TerrainResetter.Execute(spawned.transform.position, Args.TerrainReset, Args.CanResetTerrain);
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

    private void ValidateVariants(IEnumerable<ZoneSystem.ZoneVegetation> vegetation)
    {
        if (_expandWorldPrefabs == null) return;
        var variants = _expandWorldPrefabs.GetValue(null) as Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>>
            ?? throw new InvalidOperationException("Expand World Data vegetation variants are unavailable; resource reset stopped before deletion.");
        foreach (var entry in vegetation)
            if (variants.TryGetValue(entry, out var prefabs) &&
                (prefabs == null || prefabs.Count == 0 || prefabs.Any(prefab => prefab == null || prefab.name != entry.m_prefab.name)))
                throw new InvalidOperationException($"Resource reset cannot safely clear the Expand World Data multi-prefab entry '{entry.m_prefab.name}'. Use separate single-prefab vegetation entries. No objects in this zone were deleted.");
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
