using System;
using System.Linq;
using BepInEx.Configuration;
using FreshWorld.Configuration;
using FreshWorld.Core;
using UnityEngine;

internal static class ConfigPresentationRegressions
{
    internal static int Run()
    {
        var checks = new (string Name, Action Body)[]
        {
            ("presentation metadata preserves three sections and orders related settings together", Order),
            ("drawing valid or malformed values never normalizes or writes them", DrawWithoutEdits),
            ("explicit toggle and selector edits apply only the chosen config values", EditControls),
            ("numeric edits remain raw and are rejected until corrected", EditNumbers),
            ("edit-window drawers change only their supplied copy", EditCopy),
            ("native choice labels preserve raw input and distinguish invalid values", ChoiceLabels)
        };
        foreach (var check in checks)
        {
            GUILayout.Reset();
            check.Body();
            Require(GUILayout.Depth == 0, "Unbalanced GUI layout.");
            Console.WriteLine("PASS config UI " + check.Name);
        }
        GUILayout.Reset();
        return checks.Length;
    }

    private static ConfigurationManagerAttributes Metadata(ConfigEntryBase entry) =>
        (ConfigurationManagerAttributes)entry.Description.Tags.Single();

    private static void Order()
    {
        var file = new ConfigFile(); _ = new FreshWorldConfig(file);
        var expected = new[]
        {
            ("General", new[] { "Enabled", "Mode", "GameDayInterval", "DailyTimes" }, 300),
            ("Reset", new[] { "Zones", "Resources", "TerrainResourceIds", "ResourceTerrainRadius", "ResourceIds", "Locations", "LocationIds" }, 200),
            ("Protection", new[] { "ZoneSafeZones", "ResourceSafeZones", "LocationSafeZones", "PlayerPlacedObjects" }, 100)
        };
        foreach (var (section, keys, categoryOrder) in expected)
        {
            var entries = keys.Select(key => file.Get(section, key)).ToArray();
            Require(entries.OrderByDescending(entry => Metadata(entry).Order).Select(entry => entry.Definition.Key).SequenceEqual(keys), section + " order");
            Require(entries.All(entry => Metadata(entry).CategoryOrder == categoryOrder && entry.Description.AcceptableValues == null), section + " metadata");
        }
        Require(Metadata(file.Get("General", "Enabled")).DispName == "Automatic Runs", "Display name must not rename the cfg key.");
        Require(file.BoundKeys.Count() == 15, "Unexpected new settings.");
    }

    private static void DrawWithoutEdits()
    {
        var cases = new (Action<ConfigEntryBase> Draw, string[] Values)[]
        {
            (ConfigPresentation.DrawToggle, new[] { "true", "FALSE", "", "not-a-bool" }),
            (ConfigPresentation.DrawPositiveNumber, new[] { "24", "1.25", "0", "NaN", "1,25" }),
            (ConfigPresentation.DrawNonnegativeNumber, new[] { "0", "20", "-1", "Infinity", "not-a-number" })
        };
        foreach (var (draw, values) in cases)
        foreach (var value in values)
        {
            var entry = new ConfigEntry<string>(value);
            draw(entry); draw(entry);
            Require(entry.Value == value && entry.WriteCount == 0, "Repainting altered " + value);
        }
    }

    private static void EditControls()
    {
        var file = new ConfigFile(); var config = new FreshWorldConfig(file);
        GUILayout.Toggles.Enqueue(false);
        Metadata(file.Get("General", "Enabled")).CustomDrawer!(file.Get("General", "Enabled"));
        Require(!config.Capture().AutomaticEnabled, "Toggle did not update the bound entry.");

        var mode = file.Get("General", "Mode");
        mode.BoxedValue = Metadata(mode).AcceptableValues![1];
        Require(config.Capture().Schedule.Mode == ScheduleMode.DailyTimes, "Mode choice did not apply.");
        Require(mode.GetSerializedValue() == "DailyTimes", "Mode saved its display label instead of its cfg value.");

        file.Set("Protection", "ZoneSafeZones", "3");
        var protection = file.Get("Protection", "ZoneSafeZones");
        protection.BoxedValue = Metadata(protection).AcceptableValues![2];
        var snapshot = config.Capture();
        Require(snapshot.Options.ZoneSafeZones == 2 && snapshot.Options.VegetationSafeZones == 0 && snapshot.Options.LocationSafeZones == 0,
            "Selecting protection changed another stage or failed to repair the invalid value.");
        Require(protection.GetSerializedValue() == "2", "SafeZones saved its display label instead of its cfg value.");
        Require(!snapshot.Options.LocationsEnabled && snapshot.Options.LocationIds.Length == 14, "Presentation altered unrelated defaults.");
    }

    private static void EditNumbers()
    {
        var file = new ConfigFile(); var config = new FreshWorldConfig(file);
        var interval = file.Get("General", "GameDayInterval");
        GUILayout.Text.Enqueue("not-a-number");
        Metadata(interval).CustomDrawer!(interval);
        Require((string)interval.BoxedValue == "not-a-number", "Invalid numeric input was substituted.");
        var rejected = false;
        try { config.Capture(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Malformed numeric input must disable new work.");
        GUILayout.Text.Enqueue("12.5");
        Metadata(interval).CustomDrawer!(interval);
        Require(config.Capture().Schedule.GameDayInterval == 12.5, "Corrected numeric input did not apply.");
    }

    private static void EditCopy()
    {
        var file = new ConfigFile(); _ = new FreshWorldConfig(file);
        var source = file.Get("General", "GameDayInterval");
        var copy = new ConfigEntry<string>((string)source.BoxedValue, source.Definition, source.Description);
        GUILayout.Text.Enqueue("12");
        Metadata(source).CustomDrawer!(copy);
        Require(copy.Value == "12" && (string)source.BoxedValue == "24", "Edit-window copy changed the live config before confirmation.");
    }

    private static void ChoiceLabels()
    {
        var file = new ConfigFile(); var config = new FreshWorldConfig(file);
        var cases = new[]
        {
            ("General", "Mode", new[] { "GameDays", "DailyTimes" },
                new[] { "1", "0", "ManualOnly", "" }, " dailytimes ", "DailyTimes"),
            ("Protection", "ZoneSafeZones", new[] { "0 = No protection", "1 = Marker zone", "2 = 3×3 zones" },
                new[] { "3", "-1", "MarkerZone", "GameDays", "0 = No protection", "" }, " 01 ", "1 = Marker zone")
        };
        foreach (var (section, key, labels, invalidValues, validRaw, validLabel) in cases)
        {
            var entry = file.Get(section, key);
            var metadata = Metadata(entry);
            Require(metadata.CustomDrawer == null && entry.Description.AcceptableValues == null,
                "Native choices must not have a FreshWorld drawer or a clamping BepInEx constraint.");
            Require(metadata.AcceptableValues!.All(entry.SettingType.IsInstanceOfType) &&
                metadata.AcceptableValues!.Select(value => value.ToString()).SequenceEqual(labels), "Native choice types or labels are incorrect.");
            foreach (var invalid in invalidValues)
            {
                entry.SetSerializedValue(invalid);
                Require(entry.BoxedValue.ToString() == "Invalid: " + invalid && entry.GetSerializedValue() == invalid,
                    "Displaying invalid input changed it or made it appear valid.");
                var rejected = false;
                try { config.Capture(); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "Display-only choices must not bypass invalid-input validation.");
            }
            entry.SetSerializedValue(validRaw);
            Require(entry.BoxedValue.ToString() == validLabel && entry.GetSerializedValue() == validRaw,
                "Displaying a valid alternative spelling normalized its cfg text.");
            _ = config.Capture();
        }
    }

    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
