using System.Collections.Generic;

namespace FreshWorld.Engine;

/// <summary>Saved EpicLoot treasure markers, independent of loaded components and online players.
/// One instance belongs to one maintenance run; observations survive movement/completion until it ends.</summary>
internal sealed class EpicLootProtection
{
    private static readonly int SpawnController = "EL_SpawnController".GetStableHashCode();
    private static readonly int TreasureBiome = "TreasureMapChest.Biome".GetStableHashCode();
    private static readonly int TreasureFound = "TreasureMapChest.HasBeenFound".GetStableHashCode();
    private static readonly int TreasureSpawn = "treasure_spawn".GetStableHashCode();
    private static readonly int IsBounty = "isBounty".GetStableHashCode();
    private static readonly int Placed = "placed".GetStableHashCode();
    private readonly HashSet<Vector2s> _observed = new();

    // The controller prefab is shared with bounties. Require a pending treasure payload;
    // isBounty=false alone also matches uninitialized controllers. Never deserialize the payload.
    internal static bool IsTreasureObject(ZDO zdo) => zdo.IsValid() &&
        ((zdo.GetPrefab() == SpawnController && !zdo.GetBool(IsBounty) && !zdo.GetBool(Placed) &&
          zdo.GetByteArray(TreasureSpawn) is { Length: > 0 }) ||
         (!string.IsNullOrEmpty(zdo.GetString(TreasureBiome)) && !zdo.GetBool(TreasureFound)));

    public int Capture()
    {
        foreach (var zdo in GameWorld.AllZDOs()) Observe(zdo);
        return _observed.Count;
    }

    public bool CanResetZone(Vector2s zone)
    {
        if (_observed.Contains(zone)) return false;
        foreach (var zdo in GameWorld.GetZDOs(zone))
            if (Observe(zdo)) return false;
        return true;
    }

    private bool Observe(ZDO zdo)
    {
        if (!IsTreasureObject(zdo)) return false;
        _observed.Add(ZoneSystem.GetZone(zdo.GetPosition()));
        return true;
    }
}
