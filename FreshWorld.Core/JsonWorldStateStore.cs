using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace FreshWorld.Core
{
    public sealed class StateCorruptionException : IOException
    {
        public StateCorruptionException(string message) : base(message) { }
        public StateCorruptionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Atomic, per-world state. An exclusive lease prevents two schedulers from resetting the same world.</summary>
    public sealed class JsonWorldStateStore
    {
        private readonly string directory;
        public JsonWorldStateStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A state directory is required.", nameof(directory));
            this.directory = Path.GetFullPath(directory);
        }
        public string GetStatePath(string worldId)
        {
            ValidateWorldId(worldId);
            return Path.Combine(directory, ScheduleSettings.Hash(worldId) + ".json");
        }
        internal Lease Acquire(string worldId)
        {
            var path = GetStatePath(worldId);
            Directory.CreateDirectory(directory);
            return new Lease(path, new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        private static void ValidateWorldId(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId)) throw new ArgumentException("Use the persistent world UID, not its display name.", nameof(worldId));
        }

        internal sealed class Lease : IDisposable
        {
            private readonly string path;
            private readonly FileStream fileLock;
            private readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WorldState));
            public Lease(string path, FileStream fileLock) { this.path = path; this.fileLock = fileLock; }
            public WorldState? Read(string worldId)
            {
                if (!File.Exists(path)) return null;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (stream.Length > 4 * 1024 * 1024) throw new StateCorruptionException("FreshWorld state exceeds its size limit.");
                        var state = serializer.ReadObject(stream) as WorldState;
                        if (state == null) throw new StateCorruptionException("FreshWorld state is empty.");
                        state.Validate(worldId);
                        return state;
                    }
                }
                catch (StateCorruptionException) { throw; }
                catch (Exception ex) when (ex is System.Runtime.Serialization.SerializationException || ex is System.Xml.XmlException || ex is ArgumentException)
                { throw new StateCorruptionException("FreshWorld state is corrupt. Automatic reset is disabled; a stale backup is never restored automatically.", ex); }
            }
            public void Write(WorldState state)
            {
                state.Validate(state.WorldId);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        serializer.WriteObject(stream, state);
                        stream.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            public void Dispose() => fileLock.Dispose();
        }
    }
}
