using System;
using System.Globalization;
using BepInEx.Configuration;
using FreshWorld.Core;
using UnityEngine;

namespace FreshWorld.Configuration;

// Configuration Manager discovers these names through ConfigDescription.Tags.
internal sealed class ConfigurationManagerAttributes
{
    public int? Order;
    public int? CategoryOrder;
    public string? DispName;
    public Action<ConfigEntryBase>? CustomDrawer;
    public object[]? AcceptableValues;
}

/// <summary>Optional IMGUI controls over raw values; runtime validation remains in Capture.</summary>
internal static class ConfigPresentation
{
    // Metadata only: BepInEx receives no acceptable-value constraint and never clamps raw input.
    internal static object[] ModeChoices => new object[] { new ConfigChoice<ScheduleMode>("GameDays"), new ConfigChoice<ScheduleMode>("DailyTimes") };
    internal static object[] SafeZoneChoices => new object[] { new ConfigChoice<SafeZoneRange>("0"), new ConfigChoice<SafeZoneRange>("1"), new ConfigChoice<SafeZoneRange>("2") };

    internal static void DrawToggle(ConfigEntryBase entry)
    {
        GUILayout.BeginVertical();
        try
        {
            var raw = ReadRaw(entry);
            if (bool.TryParse(raw, out var value))
            {
                var edited = GUILayout.Toggle(value, value ? "Enabled" : "Disabled");
                if (edited != value) WriteEdited(entry, edited ? "true" : "false");
            }
            else if (!bool.TryParse(DrawRawText(entry), out _))
                GUILayout.Label("Enter true or false.");
        }
        finally { GUILayout.EndVertical(); }
    }

    internal static void DrawPositiveNumber(ConfigEntryBase entry)
    {
        GUILayout.BeginVertical();
        try
        {
            var raw = DrawRawText(entry);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                GUILayout.Label("Enter a finite number greater than 0, using '.' for decimals.");
        }
        finally { GUILayout.EndVertical(); }
    }

    internal static void DrawNonnegativeNumber(ConfigEntryBase entry)
    {
        GUILayout.BeginVertical();
        try
        {
            var raw = DrawRawText(entry);
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                GUILayout.Label("Enter a finite number of 0 or greater, using '.' for decimals.");
        }
        finally { GUILayout.EndVertical(); }
    }

    private static string ReadRaw(ConfigEntryBase entry) => entry.BoxedValue as string ?? string.Empty;

    private static string DrawRawText(ConfigEntryBase entry)
    {
        var raw = ReadRaw(entry);
        var edited = GUILayout.TextField(raw);
        if (!string.Equals(edited, raw, StringComparison.Ordinal)) WriteEdited(entry, edited);
        return edited;
    }

    private static void WriteEdited(ConfigEntryBase entry, string value)
    {
        // Use the entry supplied by the drawer, including Configuration Manager's edit-window copy.
        entry.BoxedValue = value;
    }
}
