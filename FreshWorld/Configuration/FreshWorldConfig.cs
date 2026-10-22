using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using FreshWorld.Core;

namespace FreshWorld.Configuration
{
    internal enum SafeZoneRange { NoProtection = 0, MarkerZone = 1, SurroundingZones = 2 }

    /// <summary>A validated snapshot: config reloads cannot change an operation in progress.</summary>
    public sealed class RuntimeSettings
    {
        public bool AutomaticEnabled => Schedule.AutomaticEnabled;
        public ScheduleSettings Schedule { get; }
        public RunOptions Options { get; }
        public bool PathOfValheimAzuriteVegetationEnabled { get; }
        public float StartupDelaySeconds => 30;
        internal RuntimeSettings(ScheduleSettings schedule, RunOptions options, bool azuriteVegetation)
        {
            Schedule = schedule; Options = options;
            PathOfValheimAzuriteVegetationEnabled = azuriteVegetation;
        }
    }

    public sealed class RunOptions
    {
        public int AutomaticResetPercent { get; }
        public bool ZonesEnabled { get; }
        public int ZoneSafeZones { get; }
        private readonly string[] pieceBlacklist;
        public string[] PieceBlacklist => (string[])pieceBlacklist.Clone();
        private readonly string[] protectedObjects;
        public string[] ProtectedObjects => (string[])protectedObjects.Clone();
        private readonly string[] alwaysProtectedPrefabs;
        public string[] AlwaysProtectedPrefabs => (string[])alwaysProtectedPrefabs.Clone();
        public bool VegetationEnabled { get; }
        private readonly string[] vegetationIds;
        public string[] VegetationIds => (string[])vegetationIds.Clone();
        private readonly string[] terrainVegetationIds;
        public string[] TerrainVegetationIds => (string[])terrainVegetationIds.Clone();
        public float VegetationTerrainRadius { get; }
        public int VegetationSafeZones { get; }
        public bool LocationsEnabled { get; }
        private readonly string[] locationIds;
        public string[] LocationIds => (string[])locationIds.Clone();
        private readonly Dictionary<string, float> locationResetRadii;
        public IReadOnlyDictionary<string, float> LocationResetRadii => new Dictionary<string, float>(locationResetRadii, StringComparer.Ordinal);
        public int LocationSafeZones { get; }
        public bool EpicLootProtectionEnabled { get; }
        public bool EpicLootBountyProtectionEnabled { get; }
        public bool PathOfValheimAltarProtectionEnabled { get; }
        public bool PathOfValheimBlightProtectionEnabled { get; }
        public int MaxZonesPerFrame => 64;
        public double FrameBudgetMilliseconds => 8;
        public float SaveTimeoutSeconds => 180;

        internal RunOptions(bool zonesEnabled, int zoneSafeZones,
            string[] pieceBlacklist, string[] protectedObjects,
            bool vegetationEnabled, string[] vegetationIds, string[] terrainVegetationIds, float vegetationTerrainRadius,
            int vegetationSafeZones, bool locationsEnabled, string[] locationIds, int locationSafeZones,
            bool epicLootProtectionEnabled, bool epicLootBountyProtectionEnabled, string[] alwaysProtectedPrefabs,
            int automaticResetPercent, IReadOnlyDictionary<string, float> locationResetRadii,
            bool pathOfValheimAltarProtectionEnabled, bool pathOfValheimBlightProtectionEnabled)
        {
            AutomaticResetPercent = automaticResetPercent;
            this.locationResetRadii = locationResetRadii.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            ZonesEnabled = zonesEnabled; ZoneSafeZones = zoneSafeZones;
            this.pieceBlacklist = (string[])pieceBlacklist.Clone();
            this.protectedObjects = (string[])protectedObjects.Clone();
            this.alwaysProtectedPrefabs = (string[])alwaysProtectedPrefabs.Clone();
            VegetationEnabled = vegetationEnabled; this.vegetationIds = (string[])vegetationIds.Clone();
            this.terrainVegetationIds = (string[])terrainVegetationIds.Clone();
            VegetationTerrainRadius = vegetationTerrainRadius; VegetationSafeZones = vegetationSafeZones;
            LocationsEnabled = locationsEnabled; this.locationIds = (string[])locationIds.Clone();
            LocationSafeZones = locationSafeZones;
            EpicLootProtectionEnabled = epicLootProtectionEnabled;
            EpicLootBountyProtectionEnabled = epicLootBountyProtectionEnabled;
            PathOfValheimAltarProtectionEnabled = pathOfValheimAltarProtectionEnabled;
            PathOfValheimBlightProtectionEnabled = pathOfValheimBlightProtectionEnabled;
        }
    }

    public sealed class FreshWorldConfig
    {
        public const string DefaultPieceBlacklist = "fire_pit";
        public const string DefaultAlwaysProtectedPrefabs = "Player_tombstone";
        public const string DefaultLocationResetRadii = "MWL_AshlandsFort4=40";
        public const string DefaultLocations = "Hildir_crypt,Hildir_cave,Hildir_plainsfortress,SunkenCrypt4,Crypt2,Crypt3,Crypt4,MountainCave02,Mistlands_Giant1,Mistlands_Excavation1,Mistlands_DvergrTownEntrance1,Mistlands_DvergrTownEntrance2,Mistlands_DvergrBossEntrance1,CharredFortress,TheHole01,MorkBorg";
        private static readonly Regex ExactId = new Regex(@"^[A-Za-z0-9_]+(?::[A-Za-z0-9_]+)*$", RegexOptions.CultureInvariant);
        // Preserve raw scalar text: BepInEx's bool/enum deserializers can silently retain/default invalid input.
        // Capture parses every scalar so malformed config disables new work instead of changing its policy.
        private readonly ConfigEntry<string> enabled, zonesEnabled, vegetationEnabled, locationsEnabled;
        private readonly ConfigEntry<string> dailyTimes, gameDayInterval, automaticResetPercent, vegetationIds, terrainVegetationIds, locationIds, locationResetRadii;
        private readonly ConfigEntry<ConfigChoice<ScheduleMode>> mode;
        private readonly ConfigEntry<string> pieceBlacklist, terrainRadius, epicLootProtection, epicLootBountyProtection, alwaysProtectedPrefabs;
        private readonly ConfigEntry<string> pathOfValheimAltarProtection, pathOfValheimBlightProtection;
        private readonly ConfigEntry<string> pathOfValheimAzuriteVegetation;
        private readonly ConfigEntry<ConfigChoice<SafeZoneRange>> zoneSafeZones, vegetationSafeZones, locationSafeZones;

        public FreshWorldConfig(ConfigFile config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            ConfigChoice<ScheduleMode>.RegisterConverter();
            ConfigChoice<SafeZoneRange>.RegisterConverter();
            enabled = Bind("General", "Enabled", "true",
                "Enable automatic runs. Set false for manual commands only; accepted manual requests and active runs continue. Re-enabling starts a new schedule without catching up.",
                400, ConfigPresentation.DrawToggle, "Automatic Runs");
            automaticResetPercent = Bind("General", "AutomaticResetPercent", "100",
                "Percentage of eligible generated zones randomly selected for each automatic run. Integer from 0 to 100; default 100 processes all candidates, while 0 selects none. Never-generated zones and reset zones not yet generated again are excluded from the count and draw. The count is rounded to the nearest whole zone, with halves rounded up. Each run draws again without tracking past selections: at 20%, even 5 or 10 runs can leave some zones untouched and select others repeatedly. All reset stages share one selection; later protection skips are not replaced. A connected location zone group counts as one candidate when Zones=true; with Zones=false, each reserved location counts once at its generated anchor. Group members are never drawn separately, so the affected sector percentage can differ from this setting. Manual freshworld commands always process all eligible zones. Terrain and border edits can still cross zone boundaries.",
                350, ConfigPresentation.DrawPercentage);
            mode = Bind("General", "Mode", new ConfigChoice<ScheduleMode>("GameDays"),
                "GameDays uses elapsed game days; DailyTimes uses host-local clock times. Used only when Enabled=true.",
                300, choices: ConfigPresentation.ModeChoices);
            gameDayInterval = Bind("General", "GameDayInterval", "24",
                "GameDays interval in game days; the first run waits this interval, which must be finite and greater than 0. One normal game day is 30 real minutes (0.5 hours), so 24 days is about 12 hours. Sleep, pauses and time changes affect elapsed real time.",
                200, ConfigPresentation.DrawPositiveNumber);
            dailyTimes = Bind("General", "DailyTimes", "05:35,17:35",
                "DailyTimes schedule as comma-separated HH:mm values in the world host's OS time zone. Every run includes all enabled stages. Used only when Enabled=true and Mode=DailyTimes.",
                100);

            zonesEnabled = Bind("Reset", "Zones", "true",
                "Reset generated zones first, including their resources and locations. Managed multi-zone locations reset with their neighboring sectors as one group using ZoneSafeZones; one protected sector skips that zone group. Selected locations in skipped groups can still be restored using LocationSafeZones. The resource and location switches below control only the extra restoration stages.",
                700, ConfigPresentation.DrawToggle);
            vegetationEnabled = Bind("Reset", "Resources", "true",
                "Restore both resource lists in zones retained by base protection. With Zones=false, both lists use all generated zones as candidates. If both lists are empty, this stage is skipped.",
                600, ConfigPresentation.DrawToggle);
            terrainVegetationIds = Bind("Reset", "TerrainResourceIds", "rock4_copper,silvervein",
                "Comma-separated exact prefab IDs restored with surrounding terrain before ResourceIds. Empty skips this group; both groups share ResourceSafeZones. Use unique IDs without wildcards or command arguments.",
                500);
            terrainRadius = Bind("Reset", "ResourceTerrainRadius", "20",
                "Terrain radius in metres around TerrainResourceIds only; 0 restores those resources without terrain changes. Must be finite and nonnegative. Terrain changes can affect building support even when pieces are not directly deleted.",
                400, ConfigPresentation.DrawNonnegativeNumber);
            vegetationIds = Bind("Reset", "ResourceIds", "",
                "Comma-separated exact prefab IDs restored without terrain changes, such as Beech1. Empty skips this group; IDs also in TerrainResourceIds are restored there once. Use unique IDs without wildcards or command arguments.",
                300);
            locationsEnabled = Bind("Reset", "Locations", "false",
                "Restore LocationIds after resources, using each location's full exterior radius. Every intersecting sector must pass LocationSafeZones. With Zones=true, selected locations in skipped zone groups are checked independently; locations already reset by the zone stage are excluded. Other single-zone locations keep the usual supplemental scope.",
                200, ConfigPresentation.DrawToggle);
            locationIds = Bind("Reset", "LocationIds", DefaultLocations,
                "Comma-separated exact location IDs; :variant suffixes are supported. Unknown IDs are skipped with a warning; if none match, this stage is skipped. Use Locations=false to disable the stage instead of an empty list.",
                100);
            locationResetRadii = Bind("Reset", "LocationResetRadii", DefaultLocationResetRadii,
                "Comma-separated exact location IDs with minimum exterior radii in metres, such as MWL_AshlandsFort4=40. Each radius must be greater than 0 and at most 512. The larger of this value and the resolved native radius is used. With Zones=true, these areas reset as whole zone groups if every member passes ZoneSafeZones, even when Locations=false or the ID is absent from LocationIds. With Zones=false, they remain reserved from resource resets; enable Locations and add the ID to LocationIds for radius restoration. Empty removes explicit entries. Unknown IDs have no effect. Shared sectors connect zone groups; overlapping radii are skipped only in radius restoration. This is a cleanup radius, not automatic mesh-bound detection; use a radius covering every spawned structure origin.",
                50);

            zoneSafeZones = Bind("Protection", "ZoneSafeZones", new ConfigChoice<SafeZoneRange>("1"),
                "Zone reset marker protection: 1 = Marker zone, 2 = 3x3 zones. Only 1 or 2 is allowed; 0 is rejected without conversion. Player zones and their eight neighbors are always excluded from direct resets for the rest of the run; terrain and border edits from outside this area can still affect it.",
                400, choices: ConfigPresentation.ZoneSafeZoneChoices);
            vegetationSafeZones = Bind("Protection", "ResourceSafeZones", new ConfigChoice<SafeZoneRange>("0"),
                "Marker protection for both resource groups: 0 = No protection, 1 = Marker zone, 2 = 3x3 zones. Only 0, 1, or 2 is allowed. A value of 0 permits resource and terrain restoration inside base zones. Player zones and their eight neighbors are always excluded from direct resets for the rest of the run; terrain restoration from outside this area can still affect it.",
                300, choices: ConfigPresentation.SafeZoneChoices);
            locationSafeZones = Bind("Protection", "LocationSafeZones", new ConfigChoice<SafeZoneRange>("0"),
                "Location restoration marker protection: 0 = No protection, 1 = Marker zone, 2 = 3x3 zones. Only 0, 1, or 2 is allowed. Every sector intersecting the location radius is checked; one protected sector skips the whole location. A value of 0 bypasses base protection and can delete player pieces inside locations, even when ZoneSafeZones blocked the earlier zone reset. Requires Locations=true and a matching LocationIds entry. Player zones and their eight neighbors, AlwaysProtectedPrefabs, Jotun objectives and enabled EpicLoot protections still apply.",
                200, choices: ConfigPresentation.SafeZoneChoices);
            alwaysProtectedPrefabs = Bind("Protection", "AlwaysProtectedPrefabs", DefaultAlwaysProtectedPrefabs,
                "Comma-separated exact prefab IDs that FreshWorld must preserve, regardless of SafeZones or PieceBlacklist. Each object's own zone is excluded from all direct reset stages, terrain restoration, and border repairs. No Piece component or creator is required. Objects are also protected from direct and recursive deletion. Observed zones stay protected until the run ends. Empty disables this list; ordinary marker protection still applies. Native placement from neighboring zones can cross the boundary. Changes apply to the next run.",
                180);
            pieceBlacklist = Bind("Protection", "PieceBlacklist", DefaultPieceBlacklist,
                "Comma-separated exact prefab IDs excluded from automatic base markers. Other Pieces with creator != 0 protect their zones when the stage's SafeZones > 0. Default fire_pit prevents lone campfires from protecting zones. Empty excludes no Pieces. Excluded Pieces can still share protection from other markers. Player_tombstone remains a separate base marker. AlwaysProtectedPrefabs overrides this blacklist.",
                100);

            epicLootProtection = Bind("EpicLoot", "EpicLootProtection", "true",
                "Protect unfound EpicLoot treasure chests and pending treasure controllers in their own zones, even when SafeZones=0. Also prevent FreshWorld from deleting those objects through a reset in another zone. Set false to disable both protections on the next run. EpicLoot is optional.",
                150, ConfigPresentation.DrawToggle);
            epicLootBountyProtection = Bind("EpicLoot", "EpicLootBountyProtection", "false",
                "Protect pending EpicLoot bounty controllers, targets, and their adds. Each object's current zone is excluded from direct resets, even when SafeZones=0, and FreshWorld cannot delete the object. Neighboring terrain edits can still reach it. Abandoned bounty objects remain protected while their tags exist. Changes apply to the next run. EpicLoot is optional.",
                140, ConfigPresentation.DrawToggle);

            pathOfValheimAltarProtection = Bind("Path of Valheiman", "AltarProtection", "true",
                "Protect PT_WardenAltar and PT_PowerAltar and their own zones from FreshWorld resets, terrain restoration, and direct or recursive deletion, even when SafeZones=0. Does not recreate missing altars. Other protection rules still apply when disabled. Changes apply to the next run. Path of Valheiman is optional.",
                200, ConfigPresentation.DrawToggle);
            pathOfValheimBlightProtection = Bind("Path of Valheiman", "BlightProtection", "true",
                "Protect active Blight controllers, linked altars, monoliths, and wardens in their own zones, even when SafeZones=0. Ordinary mobs and azurite veins are not protected by this rule; PoV replenishes them. Observed protection lasts until the run ends. Closed events and orphan objects gain no protection on a later run. PoV still controls event completion and cleanup. Other protection rules still apply when disabled. Changes apply to the next run. Path of Valheiman is optional.",
                100, ConfigPresentation.DrawToggle);
            pathOfValheimAzuriteVegetation = Bind("Path of Valheiman", "AzuriteVegetation", "false",
                "Use vegetation placement for natural PT_AzuriteVein on the host instead of PoV's initial world-wide placement and timed regeneration. Uses existing vegetation definitions, including disabled EWD entries, or adds defaults. Zones create new plain veins when generated again; for selected resource resets add PT_AzuriteVein to ResourceIds. Existing veins in or within 6m of a zone suppress its azurite placement. PoV promotion and Blight generation remain active. Existing veins are not moved or removed by enabling this option. PoV's original coordinates, respawn delay and 12m building avoidance are not reproduced. False restores PoV policy; on a world without its placement-complete key this can start native world-wide placement. Changes apply after current maintenance. Path of Valheiman is optional; EWD is not required.",
                90, ConfigPresentation.DrawToggle);

            ConfigEntry<T> Bind<T>(string section, string key, T value, string description, int order,
                Action<ConfigEntryBase>? drawer = null, string? displayName = null, object[]? choices = null) =>
                config.Bind(section, key, value, new ConfigDescription(description, null, new ConfigurationManagerAttributes
                {
                    CategoryOrder = section == "General" ? 300 : section == "Reset" ? 200 : section == "Protection" ? 100 : section == "EpicLoot" ? 75 : 50,
                    Order = order, DispName = displayName, CustomDrawer = drawer, AcceptableValues = choices
                }));
        }

        public RuntimeSettings Capture() => Capture(null);

        // Validate a remote edit against the complete host policy without changing live entries.
        internal RuntimeSettings Capture(IReadOnlyDictionary<ConfigEntryBase, object>? edits)
        {
            T Value<T>(ConfigEntry<T> entry) => edits != null && edits.TryGetValue(entry, out var value) ? (T)value : entry.Value;
            var automaticEnabled = ReadBool(Value(enabled), "General.Enabled");
            var scheduleMode = ParseEnum<ScheduleMode>(Value(mode).Raw, "General.Mode");
            ScheduleSettings schedule;
            try
            {
                // Only the active schedule's inputs are interpreted. An unused text field cannot
                // disable manual work or reset the current schedule's persisted baseline.
                schedule = new ScheduleSettings(scheduleMode, Value(dailyTimes), "Local",
                    automaticEnabled && scheduleMode == ScheduleMode.GameDays ? ReadDouble(Value(gameDayInterval), "General.GameDayInterval") : 24,
                    "*", false, automaticEnabled);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
            { throw new ArgumentException("Invalid [General] schedule: " + ex.Message, ex); }

            var zoneProtection = ReadSafeZoneRange(Value(zoneSafeZones), "Protection.ZoneSafeZones", allowZero: false);
            var vegetationProtection = ReadSafeZoneRange(Value(vegetationSafeZones), "Protection.ResourceSafeZones");
            var locationProtection = ReadSafeZoneRange(Value(locationSafeZones), "Protection.LocationSafeZones");
            var terrain = ReadFloat(Value(terrainRadius), "Reset.ResourceTerrainRadius");
            Finite(terrain, "Reset.ResourceTerrainRadius", true);
            var terrainVegetation = OptionalIds(Value(terrainVegetationIds), "Reset.TerrainResourceIds");
            var terrainIds = new HashSet<string>(terrainVegetation, StringComparer.Ordinal);
            var vegetation = OptionalIds(Value(vegetationIds), "Reset.ResourceIds")
                .Where(id => !terrainIds.Contains(id)).ToArray();
            var locations = ParseIds(Value(locationIds), "Reset.LocationIds");
            var blacklist = OptionalIds(Value(pieceBlacklist), "Protection.PieceBlacklist");
            var options = new RunOptions(ReadBool(Value(zonesEnabled), "Reset.Zones"),
                zoneProtection, blacklist, new[] { "Player_tombstone" },
                ReadBool(Value(vegetationEnabled), "Reset.Resources"), vegetation, terrainVegetation, terrain, vegetationProtection,
                ReadBool(Value(locationsEnabled), "Reset.Locations"), locations, locationProtection,
                ReadBool(Value(epicLootProtection), "EpicLoot.EpicLootProtection"),
                ReadBool(Value(epicLootBountyProtection), "EpicLoot.EpicLootBountyProtection"),
                OptionalIds(Value(alwaysProtectedPrefabs), "Protection.AlwaysProtectedPrefabs"),
                ReadPercentage(Value(automaticResetPercent)), ParseLocationRadii(Value(locationResetRadii)),
                ReadBool(Value(pathOfValheimAltarProtection), "Path of Valheiman.AltarProtection"),
                ReadBool(Value(pathOfValheimBlightProtection), "Path of Valheiman.BlightProtection"));
            return new RuntimeSettings(schedule, options,
                ReadBool(Value(pathOfValheimAzuriteVegetation), "Path of Valheiman.AzuriteVegetation"));
        }
        private static bool ReadBool(string text, string key)
        {
            if (!bool.TryParse(text, out var value)) throw new ArgumentException(key + " must be true or false.");
            return value;
        }
        private static IReadOnlyDictionary<string, float> ParseLocationRadii(string text)
        {
            const string key = "Reset.LocationResetRadii";
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var entry in text.Split(','))
            {
                var parts = entry.Split('=');
                if (parts.Length != 2 || !ExactId.IsMatch(parts[0].Trim()))
                    throw new ArgumentException(key + " requires exact ID=radius entries.");
                var id = parts[0].Trim();
                var radius = ReadFloat(parts[1].Trim(), key);
                Finite(radius, key, false);
                if (radius > 512) throw new ArgumentException(key + " radii must not exceed 512 metres.");
                if (result.ContainsKey(id)) throw new ArgumentException(key + " contains duplicate ID '" + id + "'.");
                result.Add(id, radius);
            }
            return result;
        }
        private static int ReadPercentage(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0 || value > 100)
                throw new ArgumentException("General.AutomaticResetPercent must be an integer from 0 to 100.");
            return value;
        }
        private static int ReadSafeZoneRange(ConfigChoice<SafeZoneRange> entry, string key, bool allowZero = true)
        {
            if (!int.TryParse(entry.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException(key + " must be an integer.");
            if (!Enum.IsDefined(typeof(SafeZoneRange), value) || (!allowZero && value == 0))
                throw new ArgumentException(key + (allowZero
                    ? " must be 0 (No protection), 1 (Marker zone), or 2 (3x3 zones)."
                    : " must be 1 (Marker zone) or 2 (3x3 zones). Change 0 explicitly before running maintenance."));
            return value;
        }
        private static float ReadFloat(string text, string key)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException(key + " must be a number using '.' as the decimal separator.");
            return value;
        }
        private static double ReadDouble(string text, string key)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException(key + " must be a number using '.' as the decimal separator.");
            return value;
        }

        private static T ParseEnum<T>(string value, string key) where T : struct
        {
            // Enum.TryParse also accepts numeric strings; require the actual option name.
            if (!Enum.GetNames(typeof(T)).Any(name => string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(key + " must be one of: " + string.Join(", ", Enum.GetNames(typeof(T))) + ".");
            return (T)Enum.Parse(typeof(T), value.Trim(), true);
        }
        private static void Finite(double value, string key, bool allowZero)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || (allowZero ? value < 0 : value <= 0))
                throw new ArgumentException(key + " must be finite and " + (allowZero ? ">= 0." : "> 0."));
        }
        private static string[] ParseIds(string value, string key)
        {
            var ids = value.Split(',').Select(id => id.Trim()).ToArray();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (!ExactId.IsMatch(id)) throw new ArgumentException(key + " contains an empty or invalid exact ID: '" + id + "'. Only letters, numbers, underscores and :variant suffixes are allowed.");
                if (!unique.Add(id)) throw new ArgumentException(key + " contains duplicate ID '" + id + "'.");
            }
            return ids;
        }

        private static string[] OptionalIds(string value, string key) =>
            string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : ParseIds(value, key);
    }
}
