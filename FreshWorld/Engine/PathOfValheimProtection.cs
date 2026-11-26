using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Read-only PoV objective policy. Only FreshWorld deletion uses it; PoV owns event cleanup.</summary>
internal sealed class PathOfValheimProtection
{
    internal static readonly string[] RunicPrefabs =
    {
        "PT_VeinThunderCopper", "PT_VeinHeavyTin", "PT_VeinRunebearingCopper", "PT_VeinHotIron", "PT_VeinBogScrap",
        "PT_VeinBarrowSilver", "PT_VeinFrostIron", "PT_VeinArrowCopper", "PT_VeinWindTin", "PT_VeinWhisperSilver"
    };
    private static readonly HashSet<int> RunicHashes = new(Array.ConvertAll(RunicPrefabs, id => id.GetStableHashCode()));
    internal static readonly int RunicSeeded = "pt_rvein_seeded".GetStableHashCode();
    internal static bool IsRunicPrefab(int hash) => RunicHashes.Contains(hash);
    internal static bool IsNaturalRunicVein(ZDO zdo) => IsRunicPrefab(zdo.GetPrefab()) && zdo.GetBool(RunicSeeded);
    internal bool HasRunicVeins { get; private set; }
    private static readonly int WardenAltar = "PT_WardenAltar".GetStableHashCode();
    private static readonly int PowerAltar = "PT_PowerAltar".GetStableHashCode();
    private static readonly int Ragn = "PoV_Ragn".GetStableHashCode();
    private static readonly int Sigrid = "PoV_Sigrid".GetStableHashCode();
    private static readonly int Wanderer = "PoV_Wanderer".GetStableHashCode();
    private static readonly int TreasureCache = "PoV_TreasureCache".GetStableHashCode();
    private static readonly int TreasureId = "pt.tmap.cache".GetStableHashCode();
    private static readonly int TreasureState = "pt.enc.state".GetStableHashCode();
    private static readonly int Controller = "PT_CorruptionZone".GetStableHashCode();
    private static readonly int BlightAltar = "PT_BlightAltar".GetStableHashCode();
    private static readonly int Monolith = "PT_RuneMonolith".GetStableHashCode();
    private static readonly int Uid = "pt_blight_uid".GetStableHashCode();
    private static readonly int Zone = "pt_blight_zone".GetStableHashCode();
    private static readonly int State = "pt_blight_state".GetStableHashCode();
    private static readonly int Warden = "pt_blight_warden".GetStableHashCode();
    private readonly Dictionary<string, ZDO> _controllers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly HashSet<ZDOID> _protectedObjects = new();
    private readonly bool _protectAltars;
    private readonly bool _protectBlight;
    private readonly bool _protectMerchants;
    private readonly bool _protectTreasure;
    private readonly bool _protectDungeons;
    private PathOfValheimDungeons? _dungeons;
    public bool HasObjectiveProtection { get; private set; }
    private int _lastScanFrame = -1;

    public PathOfValheimProtection(bool protectAltars = true, bool protectBlight = true, bool protectDungeons = true,
        bool protectMerchants = true, bool protectTreasure = true)
    {
        _protectAltars = protectAltars; _protectBlight = protectBlight;
        _protectMerchants = protectMerchants; _protectTreasure = protectTreasure;
        _protectDungeons = protectDungeons;
        if (Chainloader.PluginInfos.ContainsKey("treextr.pathofvalheiman"))
        {
            HasRunicVeins = true;
            HasObjectiveProtection = protectAltars || protectBlight || protectMerchants || protectTreasure || protectDungeons;
            if (protectDungeons) _dungeons = new PathOfValheimDungeons();
        }
    }

    public bool IsRetainedDungeonObject(ZDO zdo) => _dungeons?.IsRetainedObject(zdo) == true;
    public bool HasDungeons => _dungeons?.IsAvailable == true;

    // Index before classifying objectives: their ZDO enumeration order is unrelated to the controller.
    public void CaptureControllers()
    {
        if (!_protectBlight && !_protectDungeons) return;
        _controllers.Clear();
        var objects = GameWorld.AllZDOs();
        foreach (var zdo in objects)
            if (zdo.IsValid()) IndexSavedObject(zdo);
        FinishCapture(objects);
    }

    public void IndexSavedObject(ZDO zdo)
    {
        var prefab = zdo.GetPrefab();
        if (!HasRunicVeins && IsNaturalRunicVein(zdo)) HasRunicVeins = true;
        if (_protectDungeons && PathOfValheimDungeons.IsRunPrefab(prefab))
            (_dungeons ??= new PathOfValheimDungeons()).RememberRoot(zdo);
        if (_protectBlight && prefab == Controller)
        {
            HasObjectiveProtection = true;
            RememberController(zdo);
        }
        if (!HasObjectiveProtection && (IsFixedObjective(prefab) || (_protectTreasure && prefab == TreasureCache)))
            HasObjectiveProtection = true;
    }

    public void FinishCapture(IEnumerable<ZDO> objects)
    {
        _lastScanFrame = Time.frameCount;
        _dungeons?.CaptureIndexed(objects);
    }

    private bool IsFixedObjective(int prefab) =>
        (_protectDungeons && PathOfValheimDungeons.ProtectsSurfaceZone(prefab)) ||
        (_protectAltars && (prefab == WardenAltar || prefab == PowerAltar)) ||
        (_protectMerchants && (prefab == Ragn || prefab == Sigrid || prefab == Wanderer));

    public bool IsProtectedObject(ZDO zdo) => IsProtectedObject(zdo, true);

    public bool IsProtectedSnapshotObject(ZDO zdo) => IsProtectedObject(zdo, false);

    private bool IsProtectedObject(ZDO zdo, bool refresh)
    {
        if (!zdo.IsValid()) return false;
        var prefab = zdo.GetPrefab();
        if (IsFixedObjective(prefab)) return true;
        if (_protectedObjects.Contains(zdo.m_uid)) return true;
        if (_protectTreasure && prefab == TreasureCache && !string.IsNullOrEmpty(zdo.GetString(TreasureId)))
        {
            // 2 means loot is still being dispensed, not finished. 3 is empty. Keep an observed
            // cache until this run ends even if PoV finishes dispensing or clears its map ID.
            var state = zdo.GetInt(TreasureState);
            if (state == 0 || state == 2) return Retain(zdo, true);
        }
        if (!_protectBlight) return false;
        if (prefab == Controller)
        {
            RememberController(zdo);
            return Retain(zdo, _active.Contains(zdo.GetString(Uid)));
        }

        // Borrowed natural objectives have own=false; ownership is not the link contract.
        // Ordinary Blight mobs and veins refill normally. The warden flag also covers PoV's fallback boss.
        var uid = prefab == BlightAltar ? zdo.GetString(Uid) :
            prefab == Monolith || zdo.GetBool(Warden) ? zdo.GetString(Zone) : "";
        if (string.IsNullOrEmpty(uid)) return false;
        if (_active.Contains(uid)) return Retain(zdo, true);
        if (_controllers.TryGetValue(uid, out var controller) && controller.IsValid() &&
            controller.GetPrefab() == Controller && controller.GetString(Uid) == uid)
        {
            RememberController(controller);
            if (_active.Contains(uid)) return Retain(zdo, true);
        }

        // Events can appear while a zone is loading, with the objective in a different sector.
        // Share misses for this frame only, not for the run. Otherwise each orphan objective
        // would allocate and scan the entire world again. Known controllers are read live above.
        if (refresh && _lastScanFrame != Time.frameCount) CaptureControllers();
        return Retain(zdo, _active.Contains(uid));
    }

    private bool Retain(ZDO zdo, bool protect)
    {
        if (protect) _protectedObjects.Add(zdo.m_uid);
        return protect;
    }

    private void RememberController(ZDO zdo)
    {
        if (!zdo.IsValid() || zdo.GetPrefab() != Controller) return;
        var uid = zdo.GetString(Uid);
        if (string.IsNullOrEmpty(uid)) return;
        _controllers[uid] = zdo;
        var state = zdo.GetInt(State);
        // PoV: 0 running, 1 ready, 2 summoned, 3 closed. Retain observations only for this run.
        if (state >= 0 && state <= 2) _active.Add(uid);
    }
}
