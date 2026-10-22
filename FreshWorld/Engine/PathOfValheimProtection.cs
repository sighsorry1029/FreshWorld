using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Read-only PoV objective policy. Only FreshWorld deletion uses it; PoV owns event cleanup.</summary>
internal sealed class PathOfValheimProtection
{
    private static readonly int WardenAltar = "PT_WardenAltar".GetStableHashCode();
    private static readonly int PowerAltar = "PT_PowerAltar".GetStableHashCode();
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
    private int _lastScanFrame = -1;

    public PathOfValheimProtection(bool protectAltars = true, bool protectBlight = true)
    { _protectAltars = protectAltars; _protectBlight = protectBlight; }

    // Index before classifying objectives: their ZDO enumeration order is unrelated to the controller.
    public void CaptureControllers()
    {
        if (!_protectBlight) return;
        _lastScanFrame = Time.frameCount;
        _controllers.Clear();
        foreach (var zdo in GameWorld.AllZDOs()) RememberController(zdo);
    }

    public bool IsProtectedObject(ZDO zdo)
    {
        if (!zdo.IsValid()) return false;
        var prefab = zdo.GetPrefab();
        if (_protectAltars && (prefab == WardenAltar || prefab == PowerAltar)) return true;
        if (!_protectBlight) return false;
        if (_protectedObjects.Contains(zdo.m_uid)) return true;
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
        if (_lastScanFrame != Time.frameCount) CaptureControllers();
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
