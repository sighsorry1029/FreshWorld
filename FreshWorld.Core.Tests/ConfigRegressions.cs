using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using FreshWorld.Configuration;
using FreshWorld.Core;

internal static class ConfigRegressions
{
    public static int Run()
    {
        var count = 0;
        void Check(string name, Action test) { test(); count++; Console.WriteLine("PASS config " + name); }
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        void Invalid(Action test)
        {
            try { test(); }
            catch (ArgumentException) { return; }
            throw new Exception("Invalid config should throw ArgumentException");
        }

        Check("exact sixteen bindings in three sections and game-day defaults", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file); var snapshot = cfg.Capture();
            var keys = new[] { "General.Enabled", "General.Mode", "General.GameDayInterval", "General.DailyTimes",
                "Reset.Zones", "Reset.Resources", "Reset.Locations", "Reset.ResourceIds", "Reset.TerrainResourceIds", "Reset.LocationIds", "Reset.ResourceTerrainRadius",
                "Protection.ZoneSafeZones", "Protection.ResourceSafeZones", "Protection.LocationSafeZones", "Protection.EpicLootProtection", "Protection.PieceBlacklist" };
            Assert(file.BoundKeys.OrderBy(x => x).SequenceEqual(keys.OrderBy(x => x)), "exposed cfg is not the sixteen-key design");
            Assert(snapshot.AutomaticEnabled && snapshot.Schedule.AutomaticEnabled && snapshot.Schedule.Mode == ScheduleMode.GameDays &&
                snapshot.Schedule.GameDayInterval == 24, "automatic 24 game-day mode");
            Assert(snapshot.Options.VegetationIds.Length == 0 && snapshot.Options.TerrainVegetationIds.SequenceEqual(new[] { "rock4_copper", "silvervein" }) &&
                snapshot.Options.LocationIds.Length == 14 && snapshot.Options.LocationIds.Contains("Mistlands_Giant1") &&
                snapshot.Options.LocationIds.Contains("CharredFortress") &&
                !snapshot.Options.LocationIds.Intersect(new[] { "Mistlands_Giant1:dark", "FortressRuins", "AshlandRuins" }).Any(), "reset target defaults");
            Assert(snapshot.Options.ZonesEnabled && snapshot.Options.VegetationEnabled && !snapshot.Options.LocationsEnabled, "stage defaults");
            Assert(snapshot.Options.ZoneSafeZones == 1 && snapshot.Options.VegetationSafeZones == 0 && snapshot.Options.LocationSafeZones == 0, "independent protection defaults");
            Assert(snapshot.Options.PieceBlacklist.SequenceEqual(new[] { "fire_pit" }),
                "only campfires should be excluded from automatic Piece markers by default");
            Assert(snapshot.Options.ProtectedObjects.SequenceEqual(new[] { "Player_tombstone" }), "fixed tombstone marker changed");
            Assert(snapshot.Options.EpicLootProtectionEnabled, "EpicLoot protection default changed");
            Assert(snapshot.Options.VegetationTerrainRadius == 20 && !snapshot.Schedule.RunMissedOnWorldStart, "terrain and missed-run policy");
            Assert(file.SaveCount == 0, "Capture must not mutate/save config");
        });
        Check("startup and performance remain fixed and removed keys stay unbound", () =>
        {
            var file = new ConfigFile();
            var removed = new[] { ("Pipeline", "SaveBefore", "false"), ("Pipeline", "SaveAfter", "true"),
                ("General", "StartupDelaySeconds", "NaN"), ("Performance", "MaxZonesPerFrame", "0"),
                ("Performance", "FrameBudgetMilliseconds", "Infinity"), ("Performance", "SaveTimeoutSeconds", "invalid"),
                ("General", "RunNow", "true"), ("General", "CheckIntervalSeconds", "10"), ("Locations", "Force", "true"),
                ("Protection", "AdditionalPlayerPlacedObjects", "obsolete_marker"),
                ("Protection", "PlayerPlacedObjects", "piece_workbench,woodwall") };
            foreach (var item in removed) file.SeedUnbound(item.Item1, item.Item2, item.Item3);
            file.SeedUnbound("Protection", "LocationSafeZones", "1");
            var snapshot = new FreshWorldConfig(file).Capture();
            Assert(snapshot.StartupDelaySeconds == 30 && snapshot.Options.MaxZonesPerFrame == 64 &&
                snapshot.Options.FrameBudgetMilliseconds == 8 && snapshot.Options.SaveTimeoutSeconds == 180, "internal values changed");
            Assert(snapshot.Options.LocationSafeZones == 1, "removed Force must not translate or override new protection");
            Assert(snapshot.Options.PieceBlacklist.SequenceEqual(new[] { "fire_pit" }),
                "removed marker lists must not become exclusions");
            Assert(removed.All(x => !file.IsBound(x.Item1, x.Item2)), "removed option was rebound");
        });
        Check("snapshots remain immutable across caller edits and new captures", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
            file.Set("Reset", "ResourceIds", "Beech1,Birch1");
            file.Set("Protection", "PieceBlacklist", "piece_workbench,custom_marker"); var first = cfg.Capture();
            first.Options.VegetationIds[0] = "changed";
            first.Options.TerrainVegetationIds[0] = "changed";
            first.Options.LocationIds[0] = "changed";
            first.Options.PieceBlacklist[0] = "changed";
            first.Options.ProtectedObjects[0] = "changed";
            file.Set("Reset", "ResourceIds", "Beech1"); file.Set("Reset", "TerrainResourceIds", "silvervein");
            file.Set("Protection", "PieceBlacklist", "portal_wood");
            var second = cfg.Capture();
            Assert(first.Options.VegetationIds[0] == "Beech1" && first.Options.VegetationIds.Length == 2, "old resource snapshot changed");
            Assert(first.Options.TerrainVegetationIds[0] == "rock4_copper" && first.Options.TerrainVegetationIds.Length == 2 &&
                second.Options.TerrainVegetationIds.SequenceEqual(new[] { "silvervein" }), "terrain resource snapshots changed or failed to reload");
            Assert(first.Options.LocationIds[0] == "Hildir_crypt" && first.Options.ProtectedObjects[0] == "Player_tombstone", "other snapshot arrays changed");
            Assert(first.Options.PieceBlacklist.SequenceEqual(new[] { "piece_workbench", "custom_marker" }) &&
                second.Options.PieceBlacklist.SequenceEqual(new[] { "portal_wood" }) && second.Options.VegetationIds.Length == 1,
                "blacklist snapshot changed or list replacement failed");
        });
        Check("either resource group or both may be empty without expanding targets", () =>
        {
            foreach (var groups in new[] { ("", ""), ("Beech1", ""), ("", "silvervein"), ("  ", "  ") })
            {
                var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
                file.Set("Reset", "ResourceIds", groups.Item1); file.Set("Reset", "TerrainResourceIds", groups.Item2);
                var options = cfg.Capture().Options;
                Assert(options.VegetationIds.Length == (string.IsNullOrWhiteSpace(groups.Item1) ? 0 : 1) &&
                    options.TerrainVegetationIds.Length == (string.IsNullOrWhiteSpace(groups.Item2) ? 0 : 1), "empty group expanded into default targets");
                Assert(options.VegetationEnabled && options.ZonesEnabled && !options.LocationsEnabled, "empty group altered stage switches");
            }
        });
        Check("cross-list overlap uses terrain priority once without rewriting configured IDs", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
            file.Set("Reset", "ResourceIds", "Beech1,silvervein,rock4_copper");
            file.Set("Reset", "TerrainResourceIds", "rock4_copper,silvervein");
            var first = cfg.Capture().Options;
            Assert(first.VegetationIds.SequenceEqual(new[] { "Beech1" }) &&
                first.TerrainVegetationIds.SequenceEqual(new[] { "rock4_copper", "silvervein" }), "terrain priority failed");
            file.Set("Reset", "ResourceTerrainRadius", "0");
            var zeroRadius = cfg.Capture().Options;
            Assert(zeroRadius.VegetationIds.SequenceEqual(first.VegetationIds) &&
                zeroRadius.TerrainVegetationIds.SequenceEqual(first.TerrainVegetationIds), "zero radius duplicated or discarded IDs");
            file.Set("Reset", "TerrainResourceIds", "");
            Assert(cfg.Capture().Options.VegetationIds.SequenceEqual(new[] { "Beech1", "silvervein", "rock4_copper" }), "Capture mutated source list while resolving overlap");
            Assert(file.SaveCount == 0, "overlap capture must not save/rewrite config");
        });
        Check("blacklist preserves edits and empty values without restoring default exclusions", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
            file.Set("Protection", "PieceBlacklist", FreshWorldConfig.DefaultPieceBlacklist + ",custom_marker");
            var snapshot = cfg.Capture();
            Assert(snapshot.Options.PieceBlacklist.SequenceEqual(FreshWorldConfig.DefaultPieceBlacklist.Split(',').Concat(new[] { "custom_marker" })),
                "editing the blacklist failed to retain the selected exclusions");
            Assert(snapshot.Options.ProtectedObjects.SequenceEqual(new[] { "Player_tombstone" }), "unconditional marker default changed");
            file.Set("Protection", "PieceBlacklist", "custom_marker");
            Assert(cfg.Capture().Options.PieceBlacklist.SequenceEqual(new[] { "custom_marker" }), "removed default exclusions were added back implicitly");
            foreach (var empty in new[] { "", "  " })
            {
                file.Set("Protection", "PieceBlacklist", empty);
                var options = cfg.Capture().Options;
                Assert(options.PieceBlacklist.Length == 0, "empty blacklist restored defaults");
                Assert(options.ProtectedObjects.SequenceEqual(new[] { "Player_tombstone" }), "empty blacklist removed fixed tombstone protection");
            }
        });
        Check("three protection ranges are independent and zero disables only selected protection", () =>
        {
            var file = new ConfigFile();
            file.SeedUnbound("Reset", "Locations", "true");
            var cfg = new FreshWorldConfig(file);
            file.Set("Protection", "ZoneSafeZones", "2"); file.Set("Protection", "ResourceSafeZones", "1");
            file.Set("Protection", "LocationSafeZones", "0"); file.Set("Reset", "ResourceTerrainRadius", "0");
            var options = cfg.Capture().Options;
            Assert(options.ZoneSafeZones == 2 && options.VegetationSafeZones == 1 && options.LocationSafeZones == 0, "protection ranges interfered");
            Assert(options.VegetationTerrainRadius == 0 && options.VegetationEnabled && options.LocationsEnabled, "zero disabled a stage");
        });
        Check("stage switches and automatic scheduling toggle preserve configured lists", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
            file.Set("Reset", "ResourceIds", "Beech1");
            file.Set("General", "Enabled", "false"); file.Set("Reset", "Zones", "false");
            file.Set("Reset", "Resources", "false"); file.Set("Reset", "Locations", "false");
            var snapshot = cfg.Capture();
            Assert(!snapshot.AutomaticEnabled && !snapshot.Schedule.AutomaticEnabled && !snapshot.Options.ZonesEnabled &&
                !snapshot.Options.VegetationEnabled && !snapshot.Options.LocationsEnabled, "false switches not respected");
            Assert(snapshot.Options.VegetationIds.SequenceEqual(new[] { "Beech1" }) && snapshot.Options.TerrainVegetationIds.Length == 2 &&
                snapshot.Options.LocationIds.Length == 14, "disabled stage targets lost");
        });
        Check("wildcards commands empty entries duplicates and invalid enum names rejected", () =>
        {
            foreach (var key in new[] { ("Reset", "ResourceIds"), ("Reset", "TerrainResourceIds"), ("Reset", "LocationIds"), ("Protection", "PieceBlacklist") })
            foreach (var value in new[] { "*", "rock4_*", "rock4_copper start", "rock4_copper;save", "rock4_copper,", "silvervein,silvervein" })
            {
                var file = new ConfigFile(); var cfg = new FreshWorldConfig(file); file.Set(key.Item1, key.Item2, value);
                Invalid(() => cfg.Capture());
            }
            var emptyLocations = new ConfigFile(); var emptyLocationsCfg = new FreshWorldConfig(emptyLocations);
            emptyLocations.Set("Reset", "LocationIds", ""); Invalid(() => emptyLocationsCfg.Capture());
            foreach (var mode in new[] { "0", "2", "ManualOnly", "Everything", "" })
            {
                var file = new ConfigFile(); var cfg = new FreshWorldConfig(file); file.Set("General", "Mode", mode); Invalid(() => cfg.Capture());
            }
        });
        Check("malformed active scalar values are never defaulted or clamped", () =>
        {
            foreach (var invalid in new[] { ("General", "Enabled", ""), ("Reset", "Zones", "1"),
                ("Reset", "Resources", "yes"), ("Reset", "Locations", "off"),
                ("Protection", "ZoneSafeZones", "-1"), ("Protection", "ResourceSafeZones", "one"),
                ("Protection", "LocationSafeZones", "2147483648"), ("Reset", "ResourceTerrainRadius", "NaN"),
                ("Reset", "ResourceTerrainRadius", "Infinity"), ("Reset", "ResourceTerrainRadius", "-1"),
                ("Reset", "ResourceTerrainRadius", "20 metres"), ("Reset", "ResourceTerrainRadius", "8,5"),
                ("General", "GameDayInterval", "0"), ("General", "GameDayInterval", "NaN") })
            {
                var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
                file.Set(invalid.Item1, invalid.Item2, invalid.Item3); Invalid(() => cfg.Capture());
                Assert(file.SaveCount == 0, "invalid Capture rewrote config");
            }
        });
        Check("disabled automation ignores unused schedule fields and retains manual reset settings", () =>
        {
            foreach (var mode in new[] { "GameDays", "DailyTimes" })
            {
                var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
                file.Set("General", "Enabled", "false"); file.Set("General", "Mode", mode);
                file.Set("General", "GameDayInterval", "not a number"); file.Set("General", "DailyTimes", "bad time");
                file.Set("Reset", "ResourceIds", "Beech1"); file.Set("Reset", "TerrainResourceIds", "silvervein");
                file.Set("Reset", "Locations", "true");
                var snapshot = cfg.Capture();
                Assert(!snapshot.AutomaticEnabled && !snapshot.Schedule.AutomaticEnabled && snapshot.Schedule.Mode.ToString() == mode,
                    "disabled automation must retain the selected schedule mode");
                Assert(snapshot.Options.ZonesEnabled && snapshot.Options.VegetationEnabled && snapshot.Options.LocationsEnabled &&
                    snapshot.Options.VegetationIds.SequenceEqual(new[] { "Beech1" }) &&
                    snapshot.Options.TerrainVegetationIds.SequenceEqual(new[] { "silvervein" }), "automatic toggle disabled or changed manual reset options");
                file.Set("Protection", "LocationSafeZones", "-1"); Invalid(() => cfg.Capture());
                file.Set("Protection", "LocationSafeZones", "1"); file.Set("General", "Enabled", "true");
                Invalid(() => cfg.Capture()); // Re-enabling must validate this mode's previously unused schedule.
            }
        });
        Check("disabled automation rejects removed mode instead of treating it as an alias", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
            file.Set("General", "Enabled", "false"); file.Set("General", "Mode", "ManualOnly");
            Invalid(() => cfg.Capture());
        });
        Check("game-day mode ignores wall clock input and validates interval", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
            file.Set("General", "DailyTimes", "bad time"); file.Set("General", "GameDayInterval", "12.5");
            Assert(cfg.Capture().Schedule.GameDayInterval == 12.5, "active interval not preserved");
            file.Set("General", "GameDayInterval", "invalid"); Invalid(() => cfg.Capture());
        });
        Check("daily mode ignores interval uses host timezone and runs resources at every slot", () =>
        {
            var file = new ConfigFile(); var cfg = new FreshWorldConfig(file);
            file.Set("General", "Mode", "DailyTimes"); file.Set("General", "GameDayInterval", "invalid");
            var schedule = cfg.Capture().Schedule;
            Assert(schedule.TimeZone.Id == TimeZoneInfo.Local.Id && schedule.DailyTimes.Count == 2, "daily host timezone or times wrong");
            // Inspect through the public scheduler behavior; the schedule does not expose per-slot flags.
            var directory = Path.Combine(Path.GetTempPath(), "freshworld-config-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var now = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
                var start = new ScheduleClock(now, 1);
                using var scheduler = FreshWorldScheduler.Open(schedule, new JsonWorldStateStore(directory), "all-resources", start);
                var due = scheduler.GetDue(new ScheduleClock(now.AddDays(1), 2));
                Assert(due != null && due.IncludeVegetation, "daily resource supplement missing");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            file.Set("General", "DailyTimes", "25:00"); Invalid(() => cfg.Capture());
        });
        return count;
    }
}
