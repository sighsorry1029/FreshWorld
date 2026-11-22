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

/// <summary>Manual, missing-only recovery from PoV's authoritative altar and Halls entrance records.</summary>
internal sealed class PathOfValheimRepair
{
    private const float SaveTimeoutSeconds = 180f;
    private const string HallsGate = "PoV_HallsGate";
    private static readonly int GateSite = "h.gsite".GetStableHashCode();
    private static readonly int GateTheme = "h.gtheme".GetStableHashCode();
    private static readonly int GateVariant = "h.gvar".GetStableHashCode();
    private static readonly int EncounterLevel = "pt.enc.lvl".GetStableHashCode();

    // Include all reconstruction data in equality: changing a record during the save must skip it.
    private readonly struct Record : IEquatable<Record>
    {
        public readonly string Prefab, Site;
        public readonly int X, Z, Theme, Variant, Level, Mode;
        public Record(string prefab, int x, int z, string site = "", int theme = 0, int variant = 0, int level = 0, int mode = 0)
        { Prefab = prefab; X = x; Z = z; Site = site; Theme = theme; Variant = variant; Level = level; Mode = mode; }
        private (string, int, int, string, int, int, int, int) Identity => (Prefab, X, Z, Site, Theme, Variant, Level, Mode);
        public bool Equals(Record other) => Identity.Equals(other.Identity);
        public override bool Equals(object? obj) => obj is Record other && Equals(other);
        public override int GetHashCode() => Identity.GetHashCode();
    }
    private readonly ZNet network = ZNet.instance;
    private readonly ZoneSystem zones = ZoneSystem.instance;
    private readonly ZDOMan manager = ZDOMan.instance;
    private readonly ZNetScene scene = ZNetScene.instance;
    private readonly WorldGenerator generator = WorldGenerator.instance;
    private readonly long worldUid = ZNet.instance.GetWorldUID();
    private readonly MethodInfo lines;
    private readonly MethodInfo synced;
    private readonly bool hasEntranceSeating;
    private readonly Action requireAuthority;
    private readonly Action<string> log;
    private int restored, existing, skipped, invalid;

    public string Summary => $"PoV repair: restored={restored}, existing={existing}, skipped={skipped}, invalid records={invalid}.";

    public PathOfValheimRepair(Action requireAuthority, Action<string> log)
    {
        this.requireAuthority = requireAuthority;
        this.log = log;
        if (!Chainloader.PluginInfos.TryGetValue("treextr.pathofvalheiman", out var plugin))
            throw new InvalidOperationException("Path of Valheiman is not installed on this host.");
        var assembly = plugin.Instance.GetType().Assembly;
        hasEntranceSeating = assembly.GetType("PassiveTree.Encounters.Halls.HallsSeat") != null;
        var type = assembly.GetType("PassiveTree.WorldKeys");
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
        var candidates = new List<Record>();
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

        yield return MaintenanceSave.Run(network, zones, RequireWorld, log, "before PoV repair", SaveTimeoutSeconds);
        foreach (var record in candidates)
        {
            yield return WaitForWorld();
            var view = RequirePrefab(record.Prefab);
            // Match PoV's own placement in inactive zones. Do not generate a zone or load terrain.
            var height = generator.GetHeight(record.X, record.Z);
            if (float.IsNaN(height) || float.IsInfinity(height))
                throw new InvalidOperationException("PoV placement returned an invalid height: " + Label(record));
            var position = new Vector3(record.X, height, record.Z);
            var yaw = record.Prefab == "PT_WardenAltar"
                ? Math.Abs(record.X * 5.17f + record.Z * 2.31f) % 360f
                : Math.Abs(record.X * 7.31f + record.Z * 3.17f) % 360f;
            // Gate records have no yaw. Use a stable heading; PoV snaps height to loaded terrain.
            if (record.Prefab == HallsGate) yaw = (record.X * 37L + record.Z * 11L) % 16 * 22.5f;

            // PoV and other mods keep ticking during our save/yields. Never trust the planning snapshot.
            RequireWorld();
            var currentRecords = ReadRecords();
            var reason = currentRecords.Contains(record) ? Observe(record, currentRecords) : "record was removed";
            if (reason != null) { CountSkipped(record, reason); continue; }
            RequireWorld();
            if (network.IsSaving() || Time.timeScale <= 0f)
                throw new InvalidOperationException("World saving or pause state changed during PoV placement; repair stopped.");
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
                if (record.Prefab == HallsGate)
                {
                    created.Set(GateSite, record.Site);
                    created.Set(GateTheme, record.Theme);
                    created.Set(GateVariant, record.Variant);
                    created.Set(EncounterLevel, record.Level);
                }
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
            throw new InvalidOperationException("The PoV repair's hosting world or subsystems changed.");
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
                throw new TimeoutException("World saving did not finish before PoV repair.");
            yield return null;
        }
    }

    private ZNetView RequirePrefab(string name)
    {
        var prefab = scene.GetPrefab(name.GetStableHashCode());
        var view = prefab == null ? null : prefab.GetComponent<ZNetView>();
        if (view == null || !view.m_persistent)
            throw new InvalidOperationException("The persistent PoV prefab is unavailable: " + name);
        return view;
    }

    private string? Observe(Record record, HashSet<Record> records)
    {
        var center = ZoneSystem.GetZone(new Vector3(record.X, 0f, record.Z));
        var hash = record.Prefab.GetStableHashCode();
        // A gate may have moved far from its recorded position. Never create a second entrance
        // with the same identity (which is also part of per-character session keys).
        if (record.Prefab == HallsGate && GameWorld.AllZDOs().Any(obj => obj.IsValid() &&
            obj.GetPrefab() == hash && obj.GetString(GateSite) == record.Site)) return "already exists";
        // Placement records omit the fitted heading. Only PoV can seat this kind of gate and its terrain pads.
        if (record.Prefab == HallsGate && (hasEntranceSeating || scene.GetPrefab(PathOfValheimDungeons.GatePad) != null ||
            scene.GetPrefab(PathOfValheimDungeons.GateEdge) != null))
            return "this entrance needs PoV terrain placement; visit the original site so PoV can check it";
        var nearby = false;
        // 30m ambiguity radius is smaller than a 64m sector. Include inactive sector ZDOs.
        for (var x = center.x - 1; x <= center.x + 1; x++)
        for (var z = center.y - 1; z <= center.y + 1; z++)
        foreach (var obj in GameWorld.GetZDOs(new Vector2s(x, z)))
        {
            if (!obj.IsValid() || obj.GetPrefab() != hash) continue;
            var position = obj.GetPosition();
            var distance = Distance(record.X, record.Z, position.x, position.z);
            if (distance <= 4 && record.Prefab != HallsGate) return "already exists"; // Rounded X/Z.
            if (distance <= 900) nearby = true;
        }
        if (nearby) return "an object of the same prefab is within 30m; position is ambiguous";
        if (records.Any(other => other.Prefab == record.Prefab && !other.Equals(record) &&
            Distance(record.X, record.Z, other.X, other.Z) <= 900))
            return "another record for the same prefab is within 30m; position is ambiguous";
        return null;
    }

    private static double Distance(double x, double z, double otherX, double otherZ) =>
        (x - otherX) * (x - otherX) + (z - otherZ) * (z - otherZ);

    private HashSet<Record> ReadRecords()
    {
        if (synced.Invoke(null, null) is not true)
            throw new InvalidOperationException("Path of Valheiman world records are not ready; try again after loading finishes.");
        var source = lines.Invoke(null, null) as IReadOnlyList<string> ??
            throw new InvalidOperationException("PoV returned no world record collection.");
        var records = new HashSet<Record>();
        invalid = 0;
        var snapshot = source.ToArray();
        foreach (var line in snapshot)
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
            records.Add(new Record(prefab, x, z));
        }
        ReadGateRecords(snapshot, records);
        return records;
    }

    private void ReadGateRecords(string[] source, HashSet<Record> records)
    {
        // KnownSites/SpawnAt are not read-only APIs: they may place sites or assign a new identity.
        // Only reconstruct a natural entrance having both a unique site and a placement record.
        var linesByKey = source.Where(line => line != null).Select(line => line.Split(new[] { ' ' }, 2))
            .GroupBy(parts => parts[0], StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var sites = new Dictionary<string, (int X, int Z, int Theme, int Mode)>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        if (linesByKey.TryGetValue("pov_hallssites", out var siteLines) && siteLines.Length == 1 && siteLines[0].Length == 2)
        {
            foreach (var row in siteLines[0][1].Split('.'))
            {
                var parts = row.Split('_');
                if (parts.Length < 2 || !Integer(parts[0], out var x) || !Integer(parts[1], out var z) ||
                    x < short.MinValue || x > short.MaxValue || z < short.MinValue || z > short.MaxValue)
                { invalid++; continue; }
                var site = FormattableString.Invariant($"{x}_{z}");
                if (parts.Length != 4 || !Integer(parts[2], out var biome) || !Integer(parts[3], out var mode) || mode < 0 || mode > 2)
                { duplicates.Add(site); invalid++; continue; }
                var theme = biome switch { 1 => 0, 8 => 1, 2 => 2, 4 => 3, 16 => 4, 512 => 5, 32 => 6, 64 => 7, _ => -1 };
                if (theme < 0) { duplicates.Add(site); invalid++; continue; }
                if (sites.ContainsKey(site)) duplicates.Add(site);
                else sites.Add(site, (x, z, theme, mode));
            }
        }
        foreach (var pair in linesByKey.Where(pair => pair.Key.StartsWith("pov_hallsgate_", StringComparison.Ordinal)))
        {
            var site = pair.Key.Substring("pov_hallsgate_".Length).Replace("m", "-");
            if (pair.Key != "pov_hallsgate_" + site.Replace("-", "m") || pair.Value.Length != 1 || pair.Value[0].Length != 2 ||
                duplicates.Contains(site) || !sites.TryGetValue(site, out var data))
            { invalid++; continue; }
            var parts = pair.Value[0][1].Split('_');
            if (parts.Length != 3 || !Integer(parts[0], out var x) || !Integer(parts[1], out var z) ||
                !ValidCoordinate(x) || !ValidCoordinate(z) || !Integer(parts[2], out var level) || level <= 0)
            { invalid++; continue; }
            records.Add(new Record(HallsGate, x, z, site, data.Theme, (data.X + data.Z) & 1, level, data.Mode));
        }
    }

    private static bool Integer(string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool ValidCoordinate(int value)
    {
        var zone = Math.Floor((float)(((double)value + 32.0) / 64.0));
        return zone > short.MinValue && zone < short.MaxValue; // Also leave room for the neighbor query.
    }

    private void CountSkipped(Record record, string reason)
    {
        if (reason == "already exists") { existing++; return; }
        skipped++;
        log("Skipped " + Label(record) + ": " + reason + ".");
    }

    private static string Label(Record record) =>
        FormattableString.Invariant($"{record.Prefab} at X/Z=({record.X},{record.Z})") + (record.Site.Length == 0 ? "" : " site=" + record.Site);
}
