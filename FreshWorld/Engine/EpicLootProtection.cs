using System.Collections.Generic;

namespace FreshWorld.Engine;

/// <summary>Saved EpicLoot quest objects, independent of loaded components and online players.
/// One instance belongs to one maintenance run; observations survive movement/completion until it ends.</summary>
internal sealed class EpicLootProtection
{
    private static readonly int SpawnController = "EL_SpawnController".GetStableHashCode();
    private static readonly int TreasureBiome = "TreasureMapChest.Biome".GetStableHashCode();
    private static readonly int TreasureFound = "TreasureMapChest.HasBeenFound".GetStableHashCode();
    private static readonly int TreasureSpawn = "treasure_spawn".GetStableHashCode();
    private static readonly int BountySpawn = "bount_spawn".GetStableHashCode();
    private static readonly int BountyId = "BountyID".GetStableHashCode();
    private static readonly int IsBounty = "isBounty".GetStableHashCode();
    private static readonly int Placed = "placed".GetStableHashCode();
    private readonly HashSet<Vector2s> _observed = new();
    private readonly bool _protectTreasure;
    private readonly bool _protectBounties;

    internal EpicLootProtection(bool protectTreasure, bool protectBounties)
    {
        _protectTreasure = protectTreasure;
        _protectBounties = protectBounties;
    }

    // The controller prefab is shared with bounties. Require a pending treasure payload;
    // isBounty=false alone also matches uninitialized controllers. Never deserialize the payload.
    internal static bool IsTreasureObject(ZDO zdo) => zdo.IsValid() &&
        ((zdo.GetPrefab() == SpawnController && !zdo.GetBool(IsBounty) && !zdo.GetBool(Placed) &&
          zdo.GetByteArray(TreasureSpawn) is { Length: > 0 }) ||
         (!string.IsNullOrEmpty(zdo.GetString(TreasureBiome)) && !zdo.GetBool(TreasureFound)));

    // EpicLoot restores both leaders and adds from BountyID alone. The other fields can arrive
    // later, and abandoned contracts can leave this tag behind. Do not infer quest completion.
    internal static bool IsBountyObject(ZDO zdo) => zdo.IsValid() &&
        ((zdo.GetPrefab() == SpawnController && zdo.GetBool(IsBounty) && !zdo.GetBool(Placed) &&
          zdo.GetByteArray(BountySpawn) is { Length: > 0 }) ||
         !string.IsNullOrEmpty(zdo.GetString(BountyId)));

    internal static bool IsProtectedObject(ZDO zdo, bool protectTreasure, bool protectBounties) =>
        (protectTreasure && IsTreasureObject(zdo)) || (protectBounties && IsBountyObject(zdo));

    public int Capture()
    {
        if (!_protectTreasure && !_protectBounties) return 0;
        foreach (var zdo in GameWorld.AllZDOs()) Observe(zdo);
        return _observed.Count;
    }

    public bool CanResetZone(Vector2s zone)
    {
        if (!_protectTreasure && !_protectBounties) return true;
        if (_observed.Contains(zone)) return false;
        foreach (var zdo in GameWorld.GetZDOs(zone))
            if (Observe(zdo)) return false;
        return true;
    }

    private bool Observe(ZDO zdo)
    {
        if (!IsProtectedObject(zdo, _protectTreasure, _protectBounties)) return false;
        _observed.Add(ZoneSystem.GetZone(zdo.GetPosition()));
        return true;
    }
}
