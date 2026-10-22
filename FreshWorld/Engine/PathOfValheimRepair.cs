using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using FreshWorld.Backend;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Manual, missing-only recovery from PoV's authoritative altar coordinate records.</summary>
internal sealed class PathOfValheimRepair
{
    private const float SaveTimeoutSeconds = 180f;
    private readonly ZNet network = ZNet.instance;
    private readonly ZoneSystem zones = ZoneSystem.instance;
    private readonly ZDOMan manager = ZDOMan.instance;
    private readonly ZNetScene scene = ZNetScene.instance;
    private readonly WorldGenerator generator = WorldGenerator.instance;
    private readonly long worldUid = ZNet.instance.GetWorldUID();
    private readonly MethodInfo lines;
    private readonly MethodInfo synced;
    private readonly Action requireAuthority;
    private readonly Action<string> log;
    private int restored, existing, skipped, invalid;

    public string Summary => $"PoV altar repair: restored={restored}, existing={existing}, skipped={skipped}, invalid records={invalid}.";

    public PathOfValheimRepair(Action requireAuthority, Action<string> log)
    {
        this.requireAuthority = requireAuthority;
        this.log = log;
        if (!Chainloader.PluginInfos.TryGetValue("treextr.pathofvalheiman", out var plugin))
            throw new InvalidOperationException("Path of Valheiman is not installed on this host.");
        var type = plugin.Instance.GetType().Assembly.GetType("PassiveTree.WorldKeys");
        var read = type?.GetMethod("Lines", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        var ready = type?.GetProperty("Synced", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
        if (read?.ReturnType != typeof(IReadOnlyList<string>) || ready?.ReturnType != typeof(bool) ||
            ready.GetParameters().Length != 0)
            throw new InvalidOperationException("PoV WorldKeys read contract is unavailable; no coordinates were inferred.");
        lines = read;
        synced = ready;
    }

    public IEnumerator Run()
    {
        RequireWorld();
        var records = ReadRecords();
        var candidates = new List<(string Prefab, int X, int Z)>();
        foreach (var record in records.OrderBy(r => r.Prefab, StringComparer.Ordinal).ThenBy(r => r.X).ThenBy(r => r.Z))
        {
            yield return WaitForWorld();
            var reason = Observe(record, records);
            if (reason == null)
            {
                RequirePrefab(record.Prefab); // Refuse missing registration before requesting a save.
                candidates.Add(record);
            }
            else CountSkipped(record, reason);
            yield return null;
        }
        if (candidates.Count == 0) yield break;

        yield return MaintenanceSave.Run(network, zones, RequireWorld, log, "before PoV altar repair", SaveTimeoutSeconds);
        foreach (var record in candidates)
        {
            yield return WaitForWorld();
            var view = RequirePrefab(record.Prefab);
            // Match PoV's own placement in inactive zones. Do not generate a zone or load terrain.
            var height = generator.GetHeight(record.X, record.Z);
            if (float.IsNaN(height) || float.IsInfinity(height))
                throw new InvalidOperationException("PoV altar placement returned an invalid height: " + Label(record));
            var position = new Vector3(record.X, height, record.Z);
            var yaw = record.Prefab == "PT_WardenAltar"
                ? Math.Abs(record.X * 5.17f + record.Z * 2.31f) % 360f
                : Math.Abs(record.X * 7.31f + record.Z * 3.17f) % 360f;

            // PoV and other mods keep ticking during our save/yields. Never trust the planning snapshot.
            RequireWorld();
            var currentRecords = ReadRecords();
            var reason = currentRecords.Contains(record) ? Observe(record, currentRecords) : "record was removed";
            if (reason != null) { CountSkipped(record, reason); continue; }
            RequireWorld();
            if (network.IsSaving() || Time.timeScale <= 0f)
                throw new InvalidOperationException("World saving or pause state changed during altar placement; repair stopped.");
            ZDO? created = null;
            try
            {
                // This is PoV's native persistent-ZDO creation path. SetPrefab is required even when
                // CreateNewZDO received a hash; SetRotation/SetDirtySector persist the completed record.
                created = manager.CreateNewZDO(position, record.Prefab.GetStableHashCode());
                created.Persistent = true;
                created.Type = view.m_type;
                created.Distant = false;
                created.SetPrefab(record.Prefab.GetStableHashCode());
                created.SetRotation(Quaternion.Euler(0f, yaw, 0f));
                manager.SetDirtySector(created);
            }
            catch
            {
                // The new ZDO may not yet pass IsValid. Only remove the object captured here;
                // native destruction is queued, so do not claim a transactional rollback.
                if (created != null) manager.DestroyZDO(created);
                throw;
            }
            restored++;
            log("Restored " + Label(record) + ".");
            yield return null;
        }
    }

    private void RequireWorld()
    {
        requireAuthority();
        if (!MaintenanceGate.IsActive || !ReferenceEquals(network, ZNet.instance) || !network.IsServer() ||
            network.HaveStopped || network.GetWorldUID() != worldUid || ZNet.m_loadError ||
            !ReferenceEquals(zones, ZoneSystem.instance) || !ReferenceEquals(manager, ZDOMan.instance) ||
            !ReferenceEquals(scene, ZNetScene.instance) || !ReferenceEquals(generator, WorldGenerator.instance))
            throw new InvalidOperationException("The altar repair's hosting world or subsystems changed.");
    }

    private IEnumerator WaitForWorld()
    {
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            RequireWorld();
            if (Time.timeScale <= 0f) { waiting.Stop(); yield return null; continue; }
            waiting.Start();
            if (!network.IsSaving()) yield break;
            if (waiting.Elapsed.TotalSeconds > SaveTimeoutSeconds)
                throw new TimeoutException("World saving did not finish before PoV altar repair.");
            yield return null;
        }
    }

    private ZNetView RequirePrefab(string name)
    {
        var prefab = scene.GetPrefab(name.GetStableHashCode());
        var view = prefab == null ? null : prefab.GetComponent<ZNetView>();
        if (view == null || !view.m_persistent)
            throw new InvalidOperationException("The persistent PoV altar prefab is unavailable: " + name);
        return view;
    }

    private string? Observe((string Prefab, int X, int Z) record, HashSet<(string Prefab, int X, int Z)> records)
    {
        var center = ZoneSystem.GetZone(new Vector3(record.X, 0f, record.Z));
        var hash = record.Prefab.GetStableHashCode();
        var nearby = false;
        // 30m ambiguity radius is smaller than a 64m sector. Include inactive sector ZDOs.
        for (var x = center.x - 1; x <= center.x + 1; x++)
        for (var z = center.y - 1; z <= center.y + 1; z++)
        foreach (var obj in GameWorld.GetZDOs(new Vector2s(x, z)))
        {
            if (!obj.IsValid() || obj.GetPrefab() != hash) continue;
            var position = obj.GetPosition();
            var distance = Distance(record.X, record.Z, position.x, position.z);
            if (distance <= 4) return "already exists"; // Stored X/Z are rounded by PoV.
            if (distance <= 900) nearby = true;
        }
        if (nearby) return "an altar of the same prefab is within 30m; position is ambiguous";
        if (records.Any(other => other.Prefab == record.Prefab && other != record &&
            Distance(record.X, record.Z, other.X, other.Z) <= 900))
            return "another record for the same prefab is within 30m; position is ambiguous";
        return null;
    }

    private static double Distance(double x, double z, double otherX, double otherZ) =>
        (x - otherX) * (x - otherX) + (z - otherZ) * (z - otherZ);

    private HashSet<(string Prefab, int X, int Z)> ReadRecords()
    {
        if (synced.Invoke(null, null) is not true)
            throw new InvalidOperationException("Path of Valheiman world records are not ready; try again after loading finishes.");
        var source = lines.Invoke(null, null) as IReadOnlyList<string> ??
            throw new InvalidOperationException("PoV returned no world record collection.");
        var records = new HashSet<(string Prefab, int X, int Z)>();
        invalid = 0;
        foreach (var line in source.ToArray())
        {
            if (line == null) continue;
            var key = line.Split(' ')[0];
            if (key.Equals("pov_power_at_done_1", StringComparison.OrdinalIgnoreCase)) continue;
            string? prefix = null;
            var prefab = "PT_WardenAltar";
            if (key.StartsWith("pov_warden_altars_2_", StringComparison.OrdinalIgnoreCase)) prefix = "pov_warden_altars_2_";
            else if (key.StartsWith("pov_warden_altar_1_", StringComparison.OrdinalIgnoreCase)) prefix = "pov_warden_altar_1_";
            else if (key.StartsWith("pov_power_at_", StringComparison.OrdinalIgnoreCase))
            { prefix = "pov_power_at_"; prefab = "PT_PowerAltar"; }
            if (prefix == null) continue;
            var parts = key.Substring(prefix.Length).Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z) ||
                !ValidCoordinate(x) || !ValidCoordinate(z))
            { invalid++; continue; }
            records.Add((prefab, x, z));
        }
        return records;
    }

    private static bool ValidCoordinate(int value)
    {
        var zone = Math.Floor((float)(((double)value + 32.0) / 64.0));
        return zone > short.MinValue && zone < short.MaxValue; // Also leave room for the neighbor query.
    }

    private void CountSkipped((string Prefab, int X, int Z) record, string reason)
    {
        if (reason == "already exists") { existing++; return; }
        skipped++;
        log("Skipped " + Label(record) + ": " + reason + ".");
    }

    private static string Label((string Prefab, int X, int Z) record) =>
        FormattableString.Invariant($"{record.Prefab} at X/Z=({record.X},{record.Z})");
}
