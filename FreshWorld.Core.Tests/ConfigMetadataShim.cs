// Test boundary only. Real BepInEx file binding is exercised separately by FreshWorld.Harmony.Tests.
using System;
using System.Collections.Generic;

namespace BepInEx.Configuration
{
    public sealed class TypeConverter
    {
        public Func<object, Type, string> ConvertToString { get; set; } = null!;
        public Func<string, Type, object> ConvertToObject { get; set; } = null!;
    }

    public static class TomlTypeConverter
    {
        private static readonly Dictionary<Type, TypeConverter> converters = new Dictionary<Type, TypeConverter>();
        public static TypeConverter? GetConverter(Type type) => converters.TryGetValue(type, out var converter) ? converter : null;
        public static bool AddConverter(Type type, TypeConverter converter)
        {
            if (converters.ContainsKey(type)) return false;
            converters.Add(type, converter); return true;
        }
        public static string ConvertToString(object value, Type type) => type == typeof(string) ? (string)value : converters[type].ConvertToString(value, type);
        public static object ConvertToValue(string value, Type type) => type == typeof(string) ? value : converters[type].ConvertToObject(value, type);
        public static T ConvertToValue<T>(string value) => (T)ConvertToValue(value, typeof(T));
    }

    public sealed class ConfigDescription
    {
        public string Description { get; }
        public object? AcceptableValues { get; }
        public object[] Tags { get; }
        public ConfigDescription(string description, object? acceptableValues = null, params object[] tags)
        { Description = description; AcceptableValues = acceptableValues; Tags = tags; }
    }

    public sealed class ConfigDefinition
    {
        public string Section { get; }
        public string Key { get; }
        public ConfigDefinition(string section, string key) { Section = section; Key = key; }
    }

    public abstract class ConfigEntryBase
    {
        public ConfigDescription Description { get; }
        public ConfigDefinition Definition { get; }
        public abstract object BoxedValue { get; set; }
        public abstract Type SettingType { get; }
        public string GetSerializedValue() => TomlTypeConverter.ConvertToString(BoxedValue, SettingType);
        public void SetSerializedValue(string value) => BoxedValue = TomlTypeConverter.ConvertToValue(value, SettingType);
        protected ConfigEntryBase(ConfigDefinition definition, ConfigDescription description)
        { Definition = definition; Description = description; }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T value;
        public int WriteCount { get; private set; }
        public T Value { get => value; set { this.value = value; WriteCount++; } }
        public override object BoxedValue { get => value!; set => Value = (T)value; }
        public override Type SettingType => typeof(T);
        public ConfigEntry(T value, ConfigDefinition? definition = null, ConfigDescription? description = null)
            : base(definition ?? new ConfigDefinition("Test", "Value"), description ?? new ConfigDescription("")) => this.value = value;
    }
}
