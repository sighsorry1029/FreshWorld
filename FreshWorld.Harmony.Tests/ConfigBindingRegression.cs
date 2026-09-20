using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using FreshWorld.Configuration;
using FreshWorld.Core;

internal static class ConfigBindingRegression
{
    private static readonly string[] ExpectedKeys =
    {
        "General.Enabled", "General.Mode", "General.GameDayInterval", "General.DailyTimes",
        "Reset.Zones", "Reset.Resources", "Reset.Locations", "Reset.ResourceIds", "Reset.TerrainResourceIds",
        "Reset.LocationIds", "Reset.ResourceTerrainRadius", "Protection.ZoneSafeZones",
        "Protection.ResourceSafeZones", "Protection.LocationSafeZones", "Protection.PieceBlacklist"
    };

    internal static int Run()
    {
        Require(typeof(ConfigFile).Assembly.GetName().Name == "BepInEx", "A config shim replaced the installed BepInEx assembly.");
        var checks = new List<(string Name, Action Body)>
        {
            ("existing cfg values survive Bind and explicit Save", ValidBinding),
            ("changed cfg values survive Reload and produce the new snapshot", ValidReload),
            ("all 15 lossless entries expose native choice metadata without clamping rules", PresentationMetadata)
        };
        var malformed = new (string Key, string Value)[]
        {
            ("General.Enabled", "not-a-boolean"),
            ("Reset.Locations", "maybe"),
            ("General.Mode", "ManualOnly"),
            ("General.Mode", "1"),
            ("General.Mode", @"GameDays\nDailyTimes"),
            ("General.GameDayInterval", "not-a-number"),
            ("Reset.ResourceTerrainRadius", "NaN"),
            ("Protection.ZoneSafeZones", "3"),
            ("Protection.ZoneSafeZones", "-1"),
            ("Protection.ResourceSafeZones", "3"),
            ("Protection.ResourceSafeZones", "-1"),
            ("Protection.LocationSafeZones", "3"),
            ("Protection.LocationSafeZones", "-1")
        };
        foreach (var invalid in malformed)
        {
            var captured = invalid;
            checks.Add(($"{captured.Key}={captured.Value} stays raw and is rejected after Bind and Reload",
                () => MalformedBindingAndReload(captured.Key, captured.Value)));
        }
        foreach (var check in checks)
        {
            check.Body();
            System.Console.WriteLine("PASS installed BepInEx " + check.Name);
        }
        return checks.Count + InstalledManagerSmoke();
    }

    private static void ValidBinding()
    {
        var values = ValidValues();
        using var fixture = new Fixture(values);
        AssertRawValues(fixture.File, values);
        var settings = fixture.Settings.Capture();
        Require(settings.AutomaticEnabled && settings.Schedule.Mode == ScheduleMode.GameDays && settings.Schedule.GameDayInterval == 12.5,
            "The existing schedule was replaced by defaults during Bind.");
        Require(!settings.Options.ZonesEnabled && settings.Options.VegetationEnabled && settings.Options.LocationsEnabled,
            "The existing stage switches were replaced by defaults during Bind.");
        Require(settings.Options.ZoneSafeZones == 0 && settings.Options.VegetationSafeZones == 1 && settings.Options.LocationSafeZones == 2 &&
            settings.Options.VegetationTerrainRadius == 7.5f, "The existing protection or terrain values were changed.");
        Require(settings.Options.VegetationIds.SequenceEqual(new[] { "Beech1", "Birch1" }) &&
            settings.Options.TerrainVegetationIds.SequenceEqual(new[] { "rock4_copper" }) &&
            settings.Options.LocationIds.SequenceEqual(new[] { "Hildir_cave" }) &&
            settings.Options.PieceBlacklist.SequenceEqual(new[] { "piece_beehive", "piece_workbench" }),
            "The existing exact-ID lists were changed.");
        fixture.File.Save();
        AssertSavedValues(fixture.Path, values);
        fixture.File.Reload();
        AssertRawValues(fixture.File, values);
    }

    private static void ValidReload()
    {
        using var fixture = new Fixture(ValidValues());
        var before = fixture.Settings.Capture();
        var values = ValidValues();
        values["General.Mode"] = "DailyTimes";
        values["General.DailyTimes"] = "06:10,18:20";
        values["General.GameDayInterval"] = "36";
        values["Reset.Zones"] = "true";
        values["Reset.Resources"] = "false";
        values["Reset.Locations"] = "false";
        values["Reset.ResourceTerrainRadius"] = "0";
        values["Protection.ZoneSafeZones"] = "2";
        values["Protection.ResourceSafeZones"] = "0";
        values["Protection.LocationSafeZones"] = "1";
        values["Protection.PieceBlacklist"] = "";
        fixture.Reload(values);
        AssertRawValues(fixture.File, values);
        var after = fixture.Settings.Capture();
        Require(after.Schedule.Mode == ScheduleMode.DailyTimes &&
            after.Schedule.DailyTimes.SequenceEqual(new[] { new TimeSpan(6, 10, 0), new TimeSpan(18, 20, 0) }),
            "Reload did not apply the file's daily schedule.");
        Require(after.Options.ZonesEnabled && !after.Options.VegetationEnabled && !after.Options.LocationsEnabled &&
            after.Options.ZoneSafeZones == 2 && after.Options.VegetationSafeZones == 0 && after.Options.LocationSafeZones == 1 &&
            after.Options.VegetationTerrainRadius == 0 && after.Options.PieceBlacklist.Length == 0,
            "Reload clamped, defaulted, or retained earlier values.");
        Require(before.Schedule.Mode == ScheduleMode.GameDays && before.Options.LocationSafeZones == 2 &&
            before.Options.PieceBlacklist.Length == 2, "Reload mutated the already captured snapshot.");
        fixture.File.Save();
        AssertSavedValues(fixture.Path, values);
    }

    private static void PresentationMetadata()
    {
        using var fixture = new Fixture(ValidValues());
        var actual = fixture.File.Keys.Select(key => key.Section + "." + key.Key).OrderBy(key => key, StringComparer.Ordinal);
        Require(actual.SequenceEqual(ExpectedKeys.OrderBy(key => key, StringComparer.Ordinal)), "The 15-key config schema changed.");
        foreach (var definition in fixture.File.Keys)
        {
            var entry = fixture.File[definition];
            var key = definition.Section + "." + definition.Key;
            Require(entry.SettingType == ExpectedSettingType(key), definition + " has an unexpected storage type.");
            Require(entry.Description.AcceptableValues == null, definition + " can be clamped by BepInEx before validation.");
            var tags = entry.Description.Tags.Where(tag => tag.GetType().Name == "ConfigurationManagerAttributes").ToArray();
            Require(tags.Length == 1, definition + " must have one ConfigurationManagerAttributes tag.");
            var tag = tags[0];
            var type = tag.GetType();
            ReadPublicField(tag, "Order", typeof(int?));
            ReadPublicField(tag, "CategoryOrder", typeof(int?));
            ReadPublicField(tag, "DispName", typeof(string));
            var drawer = ReadPublicField(tag, "CustomDrawer", typeof(Action<ConfigEntryBase>));
            var choices = (object[]?)ReadPublicField(tag, "AcceptableValues", typeof(object[]));
            var expectsDrawer = key == "General.Enabled" || key == "General.GameDayInterval" ||
                key == "Reset.Zones" || key == "Reset.Resources" || key == "Reset.Locations" ||
                key == "Reset.ResourceTerrainRadius";
            Require(expectsDrawer ? drawer is Action<ConfigEntryBase> : drawer == null,
                key + " has an unexpected custom drawer contract.");
            if (IsChoiceKey(key)) AssertChoices(entry, choices, key);
            else Require(choices == null, key + " unexpectedly exposes a fixed choice list.");
            Require(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length >= 5,
                "Configuration Manager cannot inspect the tag's public fields.");
        }
        Require(Enum.GetValues(typeof(SafeZoneRange)).Cast<object>().Select(Convert.ToInt32).OrderBy(value => value)
            .SequenceEqual(new[] { 0, 1, 2 }), "SafeZoneRange must expose only 0, 1 and 2.");
    }

    private static bool IsChoiceKey(string key) => key == "General.Mode" || key.EndsWith("SafeZones", StringComparison.Ordinal);

    private static Type ExpectedSettingType(string key) => key == "General.Mode" ? typeof(ConfigChoice<ScheduleMode>) :
        key.EndsWith("SafeZones", StringComparison.Ordinal) ? typeof(ConfigChoice<SafeZoneRange>) : typeof(string);

    private static void AssertChoices(ConfigEntryBase entry, object[]? choices, string key)
    {
        var expected = key == "General.Mode" ? new[] { "GameDays", "DailyTimes" } : new[] { "0", "1", "2" };
        Require(choices != null && choices.All(choice => entry.SettingType.IsInstanceOfType(choice)) &&
            choices.Select(choice => TomlTypeConverter.ConvertToString(choice, entry.SettingType)).SequenceEqual(expected),
            key + " must expose native choices of its own lossless setting type.");
    }

    private static int InstalledManagerSmoke()
    {
        var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConfigManager.dll");
        if (!System.IO.File.Exists(path))
        {
            System.Console.WriteLine("SKIP installed ConfigManager metadata smoke: set ConfigManagerAssemblyPath to enable it.");
            return 0;
        }
        var assembly = Assembly.LoadFrom(path);
        var entryType = assembly.GetType("ConfigurationManager.ConfigSettingEntry", throwOnError: true)!;
        var constructor = entryType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(candidate => candidate.GetParameters().Length == 2 && candidate.GetParameters()[0].ParameterType == typeof(ConfigEntryBase));
        using var fixture = new Fixture(ValidValues());
        foreach (var key in ExpectedKeys.Where(IsChoiceKey))
        {
            var split = key.IndexOf('.');
            var entry = fixture.File[key.Substring(0, split), key.Substring(split + 1)];
            // Runs the installed constructor and its actual SetFromAttributes implementation, not a local copy.
            var setting = constructor.Invoke(new object?[] { entry, null });
            Require(ReferenceEquals(ReadManagerProperty(setting, "Entry"), entry) &&
                (Type)ReadManagerProperty(setting, "SettingType")! == entry.SettingType && entry.SettingType != typeof(string),
                "ConfigManager lost the custom choice type or would select its string editor.");
            AssertChoices(entry, (object[]?)ReadManagerProperty(setting, "AcceptableValues"), key);
            Require(ReadManagerProperty(setting, "CustomDrawer") == null &&
                ((KeyValuePair<object, object>)ReadManagerProperty(setting, "AcceptableValueRange")!).Key == null,
                "A custom drawer or range would intercept the native list path.");
        }

        // Verify the installed routing branch without invoking OnGUI or Unity's native rendering APIs.
        using var metadata = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        var draw = metadata.MainModule.Types.Single(type => type.FullName == "ConfigurationManager.SettingFieldDrawer")
            .Methods.Single(method => method.Name == "DrawSettingValue");
        var valuesRead = draw.Body.Instructions.Single(instruction => instruction.Operand is Mono.Cecil.MethodReference method &&
            method.Name == "get_AcceptableValues" && method.DeclaringType.Name == "SettingEntryBase");
        Require((valuesRead.Next.OpCode.Code == Mono.Cecil.Cil.Code.Brfalse || valuesRead.Next.OpCode.Code == Mono.Cecil.Cil.Code.Brfalse_S) &&
            valuesRead.Next.Next.OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_1 &&
            valuesRead.Next.Next.Next.Operand is Mono.Cecil.MethodReference list && list.Name == "DrawListField",
            "The installed ConfigManager no longer routes a nonnull choice list to DrawListField.");
        System.Console.WriteLine("PASS installed ConfigManager reads lossless choice metadata for its native list path (no GUI rendering)");
        return 1;
    }

    private static object? ReadManagerProperty(object setting, string name)
    {
        var property = setting.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Require(property != null, "The installed ConfigManager no longer exposes " + name + ".");
        return property!.GetValue(setting);
    }

    private static object? ReadPublicField(object tag, string name, Type expectedType)
    {
        var field = tag.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
        Require(field != null && field.FieldType == expectedType, "Missing or incompatible public metadata field: " + name);
        return field!.GetValue(tag);
    }

    private static void MalformedBindingAndReload(string key, string invalid)
    {
        var values = ValidValues();
        values[key] = invalid;
        using (var initiallyInvalid = new Fixture(values))
            RejectUnchanged(initiallyInvalid, values, key);
        using var reloadedInvalid = new Fixture(ValidValues());
        reloadedInvalid.Reload(values);
        RejectUnchanged(reloadedInvalid, values, key);
    }

    private static void RejectUnchanged(Fixture fixture, Dictionary<string, string> values, string invalidKey)
    {
        AssertRawValues(fixture.File, values);
        ArgumentException? rejected = null;
        try { fixture.Settings.Capture(); }
        catch (ArgumentException error) { rejected = error; }
        Require(rejected != null && rejected.Message.Contains(invalidKey),
            invalidKey + " was not rejected by FreshWorld's explicit validation.");
        AssertRawValues(fixture.File, values);
        fixture.File.Save();
        AssertSavedValues(fixture.Path, values);
        fixture.File.Reload();
        AssertRawValues(fixture.File, values);
    }

    private static void AssertRawValues(ConfigFile config, Dictionary<string, string> expected)
    {
        foreach (var pair in expected)
        {
            var split = pair.Key.IndexOf('.');
            var entry = config[pair.Key.Substring(0, split), pair.Key.Substring(split + 1)];
            Require(entry.SettingType == ExpectedSettingType(pair.Key) && entry.GetSerializedValue() == pair.Value,
                pair.Key + " was clamped, defaulted or replaced before Capture.");
            var raw = entry.BoxedValue is ConfigChoice<ScheduleMode> mode ? mode.Raw :
                entry.BoxedValue is ConfigChoice<SafeZoneRange> range ? range.Raw : (string)entry.BoxedValue;
            Require(raw == TomlTypeConverter.ConvertToValue<string>(pair.Value), pair.Key + " did not preserve BepInEx's decoded string value.");
            if (pair.Value == @"GameDays\nDailyTimes")
                Require(raw == "GameDays\nDailyTimes", "The escaped malformed choice did not decode its newline.");
        }
    }

    private static void AssertSavedValues(string path, Dictionary<string, string> expected)
    {
        var saved = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = "";
        foreach (var raw in System.IO.File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                section = line.Substring(1, line.Length - 2);
            else if (!line.StartsWith("#", StringComparison.Ordinal) && line.Contains("="))
            {
                var split = line.IndexOf('=');
                saved.Add(section + "." + line.Substring(0, split).Trim(), line.Substring(split + 1).Trim());
            }
        }
        Require(saved.Count == expected.Count && expected.All(pair => saved.TryGetValue(pair.Key, out var value) && value == pair.Value),
            "BepInEx Save changed a raw value or the visible config schema.");
    }

    private static Dictionary<string, string> ValidValues() => new(StringComparer.Ordinal)
    {
        ["General.Enabled"] = "true",
        ["General.Mode"] = "GameDays",
        ["General.GameDayInterval"] = "12.5",
        ["General.DailyTimes"] = "07:15,19:45",
        ["Reset.Zones"] = "false",
        ["Reset.Resources"] = "true",
        ["Reset.Locations"] = "true",
        ["Reset.ResourceIds"] = "Beech1,Birch1",
        ["Reset.TerrainResourceIds"] = "rock4_copper",
        ["Reset.LocationIds"] = "Hildir_cave",
        ["Reset.ResourceTerrainRadius"] = "7.5",
        ["Protection.ZoneSafeZones"] = "0",
        ["Protection.ResourceSafeZones"] = "1",
        ["Protection.LocationSafeZones"] = "2",
        ["Protection.PieceBlacklist"] = "piece_beehive,piece_workbench"
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreshWorld.ConfigBinding." + Guid.NewGuid().ToString("N"));
        internal readonly string Path;
        internal readonly ConfigFile File;
        internal readonly FreshWorldConfig Settings;

        internal Fixture(Dictionary<string, string> values)
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "sighsorry.FreshWorld.cfg");
            Write(values);
            File = new ConfigFile(Path, saveOnInit: false) { SaveOnConfigSet = false };
            Settings = new FreshWorldConfig(File);
        }

        internal void Reload(Dictionary<string, string> values)
        {
            Write(values);
            File.Reload();
        }

        private void Write(Dictionary<string, string> values)
        {
            var text = new StringBuilder();
            foreach (var section in new[] { "General", "Reset", "Protection" })
            {
                text.Append('[').Append(section).AppendLine("]");
                foreach (var key in ExpectedKeys.Where(key => key.StartsWith(section + ".", StringComparison.Ordinal)))
                    text.Append(key.Substring(section.Length + 1)).Append(" = ").AppendLine(values[key]);
                text.AppendLine();
            }
            System.IO.File.WriteAllText(Path, text.ToString(), new UTF8Encoding(false));
        }

        public void Dispose()
        {
            System.IO.File.Delete(Path);
            Directory.Delete(directory); // The fixture owns one file; no recursive deletion is needed.
        }
    }
}
