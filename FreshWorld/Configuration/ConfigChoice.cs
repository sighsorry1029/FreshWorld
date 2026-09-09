using System;
using System.Globalization;
using BepInEx.Configuration;
using FreshWorld.Core;

namespace FreshWorld.Configuration;

/// <summary>Preserves config text while supplying labels for Configuration Manager's native dropdown.</summary>
internal readonly struct ConfigChoice<T> : IEquatable<ConfigChoice<T>> where T : struct, Enum
{
    private readonly string? raw;
    internal string Raw => raw ?? string.Empty;

    internal ConfigChoice(string raw) => this.raw = raw;

    private static readonly TypeConverter Converter = new TypeConverter
    {
        // Use BepInEx's string escaping so arbitrary invalid text also survives a save/reload.
        ConvertToString = (value, _) => TomlTypeConverter.ConvertToString(((ConfigChoice<T>)value).Raw, typeof(string)),
        ConvertToObject = (value, _) => new ConfigChoice<T>(TomlTypeConverter.ConvertToValue<string>(value))
    };

    internal static void RegisterConverter()
    {
        var current = TomlTypeConverter.GetConverter(typeof(ConfigChoice<T>));
        if (ReferenceEquals(current, Converter)) return;
        if (current != null || !TomlTypeConverter.AddConverter(typeof(ConfigChoice<T>), Converter))
            throw new InvalidOperationException("A different config converter is already registered for " + typeof(ConfigChoice<T>).FullName + ".");
    }

    public bool Equals(ConfigChoice<T> other) => string.Equals(Raw, other.Raw, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ConfigChoice<T> other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Raw);

    public override string ToString()
    {
        var value = Raw.Trim();
        if (typeof(T) == typeof(ScheduleMode))
        {
            if (string.Equals(value, nameof(ScheduleMode.GameDays), StringComparison.OrdinalIgnoreCase)) return nameof(ScheduleMode.GameDays);
            if (string.Equals(value, nameof(ScheduleMode.DailyTimes), StringComparison.OrdinalIgnoreCase)) return nameof(ScheduleMode.DailyTimes);
        }
        else if (typeof(T) == typeof(SafeZoneRange) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var range))
        {
            switch (range)
            {
                case 0: return "0 = No protection";
                case 1: return "1 = Marker zone";
                case 2: return "2 = 3×3 zones";
            }
        }
        // The manager also compares displayed labels in its edit window. Keep invalid text
        // distinct from valid labels so selecting a valid option always enables Apply.
        return "Invalid: " + Raw;
    }
}
