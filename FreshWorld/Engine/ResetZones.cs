using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Returns generated zones to their ungenerated state; ordinary world streaming regenerates them later.</summary>
internal class ResetZones : ZoneOperation
{
    private static readonly Action<Minimap, float> UpdateLocationPins =
        AccessTools.MethodDelegate<Action<Minimap, float>>(AccessTools.Method(typeof(Minimap), "UpdateLocationPins"));
    private readonly Dictionary<Vector2s, BorderDirection> _borders = new();
    private int _reset;

    public ResetZones(Action<string> log, OperationParameters args, HashSet<Vector2s>? candidates = null) : base(log, args, candidates) { }

    protected override bool ExecuteZone(Vector2s zone)
    {
        var world = ZoneSystem.instance;
        // Deleting a TerrainCompiler may already change terrain before a later deletion/mod hook
        // fails. Record neighbors before the first destructive call so partial cleanup repairs them.
        AddBorder(zone.x, zone.y - 1, BorderDirection.North);
        AddBorder(zone.x - 1, zone.y, BorderDirection.East);
        AddBorder(zone.x, zone.y + 1, BorderDirection.South);
        AddBorder(zone.x + 1, zone.y, BorderDirection.West);
        AddBorder(zone.x + 1, zone.y - 1, BorderDirection.NorthWest);
        AddBorder(zone.x - 1, zone.y - 1, BorderDirection.NorthEast);
        AddBorder(zone.x + 1, zone.y + 1, BorderDirection.SouthWest);
        AddBorder(zone.x - 1, zone.y + 1, BorderDirection.SouthEast);

        foreach (var zdo in GameWorld.GetZDOs(zone))
        {
            if (zdo == null || !zdo.IsValid()) continue;
            if (ZoneSystem.GetZone(zdo.GetPosition()) == zone) GameWorld.RemoveZDO(zdo, Args.ProtectEpicLoot, Args.ProtectEpicLootBounties);
        }

        if (world.m_locationInstances.TryGetValue(zone, out var location))
        {
            location.m_placed = false;
            var position = location.m_position;
            position.y = WorldGenerator.instance.GetHeight(position.x, position.z);
            location.m_position = position;
            world.m_locationInstances[zone] = location;
        }

        GameWorld.RemoveGeneratedZone(zone);
        _reset++;
        return true;
    }

    private void AddBorder(int x, int y, BorderDirection direction)
    {
        if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue) return;
        var zone = new Vector2s(x, y);
        if (_borders.TryGetValue(zone, out var existing)) direction |= existing;
        _borders[zone] = direction;
    }

    protected override void OnEnd()
    {
        var borders = _borders.Where(entry => GameWorld.IsGenerated(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        if (borders.Count > 0) TerrainResetter.ResetBorders(borders);
        ClutterSystem.instance?.ClearAll();
        GameWorld.RecalculateTerrain();
        if (Minimap.instance != null) UpdateLocationPins(Minimap.instance, 1000);
        Log($"Zone reset finished: {_reset} zones reset, 0 failed.");
    }
}
