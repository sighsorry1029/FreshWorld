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
        public float StartupDelaySeconds => 30;
        internal RuntimeSettings(ScheduleSettings schedule, RunOptions options)
        { Schedule = schedule; Options = options; }
    }

    public sealed class RunOptions
    {
        public bool ZonesEnabled { get; }
        public int ZoneSafeZones { get; }
        private readonly string[] pieceBlacklist;
        public string[] PieceBlacklist => (string[])pieceBlacklist.Clone();
        private readonly string[] protectedObjects;
        public string[] ProtectedObjects => (string[])protectedObjects.Clone();
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
        public int LocationSafeZones { get; }
        public int MaxZonesPerFrame => 64;
        public double FrameBudgetMilliseconds => 8;
        public float SaveTimeoutSeconds => 180;

        internal RunOptions(bool zonesEnabled, int zoneSafeZones,
            string[] pieceBlacklist, string[] protectedObjects,
            bool vegetationEnabled, string[] vegetationIds, string[] terrainVegetationIds, float vegetationTerrainRadius,
            int vegetationSafeZones, bool locationsEnabled, string[] locationIds, int locationSafeZones)
        {
            ZonesEnabled = zonesEnabled; ZoneSafeZones = zoneSafeZones;
            this.pieceBlacklist = (string[])pieceBlacklist.Clone();
            this.protectedObjects = (string[])protectedObjects.Clone();
            VegetationEnabled = vegetationEnabled; this.vegetationIds = (string[])vegetationIds.Clone();
            this.terrainVegetationIds = (string[])terrainVegetationIds.Clone();
            VegetationTerrainRadius = vegetationTerrainRadius; VegetationSafeZones = vegetationSafeZones;
            LocationsEnabled = locationsEnabled; this.locationIds = (string[])locationIds.Clone();
            LocationSafeZones = locationSafeZones;
        }
    }

    public sealed class FreshWorldConfig
    {
        public const string DefaultPieceBlacklist = "fire_pit";
        public const string DefaultLocations = "Hildir_crypt,Hildir_cave,Hildir_plainsfortress,SunkenCrypt4,Crypt2,Crypt3,Crypt4,MountainCave02,Mistlands_Giant1,Mistlands_Excavation1,Mistlands_DvergrTownEntrance1,Mistlands_DvergrTownEntrance2,Mistlands_DvergrBossEntrance1,CharredFortress";
        private static readonly Regex ExactId = new Regex(@"^[A-Za-z0-9_]+(?::[A-Za-z0-9_]+)*$", RegexOptions.CultureInvariant);
        // Preserve raw scalar text: BepInEx's bool/enum deserializers can silently retain/default invalid input.
        // Capture parses every scalar so malformed config disables new work instead of changing its policy.
        private readonly ConfigEntry<string> enabled, zonesEnabled, vegetationEnabled, locationsEnabled;
        private readonly ConfigEntry<string> dailyTimes, gameDayInterval, vegetationIds, terrainVegetationIds, locationIds;
        private readonly ConfigEntry<ConfigChoice<ScheduleMode>> mode;
        private readonly ConfigEntry<string> pieceBlacklist, terrainRadius;
        private readonly ConfigEntry<ConfigChoice<SafeZoneRange>> zoneSafeZones, vegetationSafeZones, locationSafeZones;

        public FreshWorldConfig(ConfigFile config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            ConfigChoice<ScheduleMode>.RegisterConverter();
            ConfigChoice<SafeZoneRange>.RegisterConverter();
            enabled = Bind("General", "Enabled", "true",
                "Enable automatic runs. Set false for manual commands only; accepted manual requests and active runs continue. Re-enabling starts a new schedule without catching up.",
                400, ConfigPresentation.DrawToggle, "Automatic Runs");
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
                "Reset generated zones first, including their resources and locations. The resource and location switches below control only the extra restoration stages.",
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
                "Restore selected locations after resources, using the same candidate zone rules. This switch does not exclude locations from the zone reset.",
                200, ConfigPresentation.DrawToggle);
            locationIds = Bind("Reset", "LocationIds", DefaultLocations,
                "Comma-separated exact location IDs; :variant suffixes are supported. Unknown IDs are skipped with a warning; if none match, this stage is skipped. Use Locations=false to disable the stage instead of an empty list.",
                100);

            zoneSafeZones = Bind("Protection", "ZoneSafeZones", new ConfigChoice<SafeZoneRange>("1"),
                "Zone reset marker protection: 0 = No protection, 1 = Marker zone, 2 = 3x3 zones. Only 0, 1, or 2 is allowed. A value of 0 can allow player structures to be deleted. Player zones and their eight neighbors are always excluded from direct resets for the rest of the run; terrain and border edits from outside this area can still affect it.",
                400, choices: ConfigPresentation.SafeZoneChoices);
            vegetationSafeZones = Bind("Protection", "ResourceSafeZones", new ConfigChoice<SafeZoneRange>("0"),
                "Marker protection for both resource groups: 0 = No protection, 1 = Marker zone, 2 = 3x3 zones. Only 0, 1, or 2 is allowed. A value of 0 permits resource and terrain restoration inside base zones. Player zones and their eight neighbors are always excluded from direct resets for the rest of the run; terrain restoration from outside this area can still affect it.",
                300, choices: ConfigPresentation.SafeZoneChoices);
            locationSafeZones = Bind("Protection", "LocationSafeZones", new ConfigChoice<SafeZoneRange>("0"),
                "Location restoration marker protection: 0 = No protection, 1 = Marker zone, 2 = 3x3 zones. Only 0, 1, or 2 is allowed. A value of 0 bypasses base protection and can delete player pieces inside locations. Player zones and their eight neighbors are always excluded from direct resets for the rest of the run; terrain restoration from outside this area can still affect it.",
                200, choices: ConfigPresentation.SafeZoneChoices);
            pieceBlacklist = Bind("Protection", "PieceBlacklist", DefaultPieceBlacklist,
                "Comma-separated exact prefab IDs excluded from automatic base markers. Other Pieces with creator != 0 protect their zones when the stage's SafeZones > 0. Default fire_pit prevents lone campfires from protecting zones. Empty excludes no Pieces. Excluded Pieces can still share protection from other markers. Player_tombstone remains a separate marker unaffected by this list.",
                100);

            ConfigEntry<T> Bind<T>(string section, string key, T value, string description, int order,
                Action<ConfigEntryBase>? drawer = null, string? displayName = null, object[]? choices = null) =>
                config.Bind(section, key, value, new ConfigDescription(description, null, new ConfigurationManagerAttributes
                {
                    CategoryOrder = section == "General" ? 300 : section == "Reset" ? 200 : 100,
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

            var zoneProtection = ReadSafeZoneRange(Value(zoneSafeZones), "Protection.ZoneSafeZones");
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
                ReadBool(Value(locationsEnabled), "Reset.Locations"), locations, locationProtection);
            return new RuntimeSettings(schedule, options);
        }
        private static bool ReadBool(string text, string key)
        {
            if (!bool.TryParse(text, out var value)) throw new ArgumentException(key + " must be true or false.");
            return value;
        }
        private static int ReadSafeZoneRange(ConfigChoice<SafeZoneRange> entry, string key)
        {
            if (!int.TryParse(entry.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException(key + " must be an integer.");
            if (!Enum.IsDefined(typeof(SafeZoneRange), value))
                throw new ArgumentException(key + " must be 0 (No protection), 1 (Marker zone), or 2 (3x3 zones).");
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
