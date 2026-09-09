// Tests only: production configuration validators are linked into this executable.
// This shim models Bind/Value/Save and unbound input; it does not simulate BepInEx deserialization or Unity.
using System.Collections.Generic;

namespace BepInEx.Configuration
{
    public sealed class ConfigFile
    {
        private readonly Dictionary<string, object> entries = new Dictionary<string, object>();
        private readonly Dictionary<string, object> unbound = new Dictionary<string, object>();
        public IEnumerable<string> BoundKeys => entries.Keys;
        public int SaveCount { get; private set; }
        public ConfigEntry<T> Bind<T>(string section, string key, T value, ConfigDescription description)
        {
            var combined = section + "." + key;
            if (entries.TryGetValue(combined, out var existing)) return (ConfigEntry<T>)existing;
            var entry = new ConfigEntry<T>(unbound.TryGetValue(combined, out var raw) ?
                raw is T typed ? typed : (T)TomlTypeConverter.ConvertToValue((string)raw, typeof(T)) : value,
                new ConfigDefinition(section, key), description);
            entries.Add(combined, entry);
            return entry;
        }
        public void Set<T>(string section, string key, T value)
        {
            var entry = (ConfigEntryBase)entries[section + "." + key];
            if (value is string raw) entry.SetSerializedValue(raw);
            else entry.BoxedValue = value!;
        }
        public void SeedUnbound<T>(string section, string key, T value) where T : notnull => unbound[section + "." + key] = value;
        public bool IsBound(string section, string key) => entries.ContainsKey(section + "." + key);
        public ConfigEntryBase Get(string section, string key) => (ConfigEntryBase)entries[section + "." + key];
        public void Save() => SaveCount++;
    }
}
