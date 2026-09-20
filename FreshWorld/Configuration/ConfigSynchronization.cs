using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;

namespace FreshWorld.Configuration;

/// <summary>ServerSync transport with FreshWorld's administrator-only, host-validated edit policy.</summary>
internal sealed class ConfigSynchronization : IDisposable
{
    private const int MaxEditBytes = 128 * 1024;
    private static ConfigSynchronization? active;
    [ThreadStatic] private static ZRpc? receivingRpc;
    private static readonly MethodInfo PackageConfigs = Required(typeof(ConfigSync), "ConfigsToPackage");
    private static readonly MethodInfo ReadValue = Required(typeof(ConfigSync), "ReadValueWithTypeFromZPackage");
    private static readonly MethodInfo Broadcast = Required(typeof(ConfigSync), "Broadcast", typeof(long), typeof(ConfigEntryBase[]));
    private static readonly MethodInfo RegisterEntry = Required(typeof(ConfigSync), "AddConfigEntry");
    private static readonly FieldInfo Registrations = AccessTools.Field(typeof(ConfigSync), "configSyncs");
    private static readonly FieldInfo Versions = AccessTools.Field(typeof(VersionCheck), "versionChecks");
    private static readonly FieldInfo VersionOwner = AccessTools.Field(typeof(VersionCheck), "ConfigSync");
    private static readonly FieldInfo RoutedHandlers = AccessTools.Field(typeof(ZRoutedRpc), "m_functions");

    private readonly ConfigFile file;
    private readonly FreshWorldConfig settings;
    private readonly Action changed;
    private readonly Action<string> warn;
    private readonly Dictionary<ConfigDefinition, ConfigEntryBase> entries;
    private readonly ConfigEntryBase[] allEntries;
    private readonly ConfigSync sync;
    private bool disposed;

    internal ConfigSynchronization(ConfigFile file, FreshWorldConfig settings, Harmony harmony,
        Action changed, Action<string> warn)
    {
        this.file = file;
        this.settings = settings;
        this.changed = changed;
        this.warn = warn;
        entries = file.ToDictionary(pair => pair.Key, pair => pair.Value);
        allEntries = entries.Values.ToArray();
        sync = new ConfigSync(FreshWorldPlugin.ModGUID)
        {
            DisplayName = FreshWorldPlugin.ModName,
            CurrentVersion = FreshWorldPlugin.ModVersion,
            ModRequired = false,
            // Fixed policy: administrators may edit; ordinary clients never unlock world resets.
            IsLocked = true
        };
        try
        {
            active = this;
            Patch(harmony, Required(typeof(ZRpc), "HandlePackage"), nameof(EnterRpc), finalizer: nameof(LeaveRpc));
            Patch(harmony, Required(typeof(ConfigSync), "RPC_FromOtherClientConfigSync"), nameof(ReceiveClient), nameof(ClientApplied));
            Patch(harmony, Required(typeof(ConfigSync), "RPC_FromServerConfigSync"), nameof(ReceiveServer));
            Patch(harmony, Broadcast, nameof(SendChanges));
            Patch(harmony, Required(typeof(ConfigEntryBase), nameof(ConfigEntryBase.GetSerializedValue)), nameof(SaveLocalValue));
            foreach (var entry in allEntries)
                RegisterEntry.MakeGenericMethod(entry.SettingType).Invoke(sync, new object[] { entry });
            file.SettingChanged += OnSettingChanged;
            sync.SourceOfTruthChanged += OnSourceChanged;
        }
        catch { Dispose(); throw; }
    }

    private static MethodInfo Required(Type type, string name, params Type[] arguments) =>
        AccessTools.Method(type, name, arguments.Length == 0 ? null : arguments)
        ?? throw new MissingMethodException(type.FullName, name);

    private static void Patch(Harmony harmony, MethodInfo target, string prefix, string? postfix = null, string? finalizer = null)
    {
        HarmonyMethod Hook(string name) => new(Required(typeof(ConfigSynchronization), name));
        harmony.Patch(target, Hook(prefix), postfix == null ? null : Hook(postfix),
            finalizer: finalizer == null ? null : Hook(finalizer));
    }

    private void OnSettingChanged(object sender, SettingChangedEventArgs args) => changed();
    private void OnSourceChanged(bool isLocal) => changed();

    internal void ReloadFile()
    {
        var saving = file.SaveOnConfigSet;
        var receiving = ConfigSync.ProcessingServerUpdate;
        try
        {
            // Read the whole file before publishing. Never send half of a reload to clients.
            ConfigSync.ProcessingServerUpdate = true;
            file.SaveOnConfigSet = false;
            file.Reload();
        }
        finally
        {
            file.SaveOnConfigSet = saving;
            ConfigSync.ProcessingServerUpdate = receiving;
        }
    }

    internal void Publish(long target = 0)
    {
        if (!disposed && ZNet.instance != null && ZNet.instance.IsServer() && !ZNet.instance.HaveStopped)
            Broadcast.Invoke(sync, new object[] { target, allEntries });
    }

    private readonly struct RpcScope
    {
        internal readonly ZRpc? Previous;
        internal readonly bool Entered;
        internal RpcScope(ZRpc? previous) { Previous = previous; Entered = true; }
    }

    private static void EnterRpc(ZRpc __instance, out RpcScope __state)
    {
        __state = new RpcScope(receivingRpc);
        receivingRpc = __instance;
    }

    private static void LeaveRpc(RpcScope __state)
    {
        // A skipped prefix has default state and must not clear an outer RPC's identity.
        if (__state.Entered) receivingRpc = __state.Previous;
    }

    private static ZNetPeer? ConnectedPeer(ZNet network, ZRpc? rpc) => rpc == null ? null :
        network.GetPeers().FirstOrDefault(peer => ReferenceEquals(peer.m_rpc, rpc) &&
            ReferenceEquals(peer.m_socket, rpc.GetSocket()) && peer.m_socket.IsConnected());

    private static bool ReceiveClient(ConfigSync __instance, long sender, ZPackage package, out bool __state)
    {
        __state = false;
        var owner = active;
        if (owner == null || !ReferenceEquals(owner.sync, __instance)) return false;
        var net = ZNet.instance;
        // A routed sender ID alone is not identity. Match the actual RPC and live session first.
        if (net == null || !net.IsServer() || net.HaveStopped || ZNet.World == null) return false;
        var peer = ConnectedPeer(net, receivingRpc);
        if (peer == null || peer.m_server || !peer.IsReady() || peer.m_uid != sender) return false;
        var identity = peer.m_socket.GetHostName();
        if (string.IsNullOrWhiteSpace(identity) || !net.IsAdmin(identity))
        {
            owner.Publish(peer.m_uid);
            return false;
        }
        try
        {
            owner.ValidateEdit(package);
            __state = true;
            return true;
        }
        catch (Exception error)
        {
            owner.warn("Rejected remote configuration: " + error.GetBaseException().Message);
            owner.Publish(peer.m_uid);
            return false;
        }
    }

    private void ValidateEdit(ZPackage package)
    {
        var bytes = package.GetArray();
        if (bytes.Length > MaxEditBytes) throw new ArgumentException("Configuration edit is too large.");
        var input = new ZPackage(bytes);
        // Clients publish small partial config edits, never library control values, full snapshots,
        // compressed payloads, or fragments. Server-to-client transport retains ServerSync framing.
        if (input.ReadByte() != 1) throw new ArgumentException("Expected a partial configuration edit.");
        var count = input.ReadInt();
        if (count < 1 || count > entries.Count) throw new ArgumentException("Invalid setting count.");
        var edits = new Dictionary<ConfigEntryBase, object>();
        for (var index = 0; index < count; index++)
        {
            var key = new ConfigDefinition(input.ReadString(), input.ReadString());
            if (!entries.TryGetValue(key, out var entry) || edits.ContainsKey(entry))
                throw new ArgumentException("Unknown or repeated setting.");
            if (input.ReadString() != entry.SettingType.AssemblyQualifiedName)
                throw new ArgumentException("Setting type does not match the server.");
            // Only a registered, known type reaches ServerSync's reflection-based decoder.
            var value = ReadValue.Invoke(null, new object[] { input, entry.SettingType });
            if (value == null) throw new ArgumentException("Setting values cannot be null.");
            edits.Add(entry, value);
        }
        if (input.GetPos() != bytes.Length) throw new ArgumentException("Unexpected trailing configuration data.");
        settings.Capture(edits);
    }

    private static void ClientApplied(bool __state)
    {
        // Clients send only to the server. After acceptance it distributes its own saved values.
        if (__state) active?.Publish();
    }

    private static bool ReceiveServer(ConfigSync __instance, ZRpc rpc)
    {
        var net = ZNet.instance;
        if (active == null || !ReferenceEquals(active.sync, __instance) || net == null ||
            net.IsServer() || net.HaveStopped) return false;
        // Initial configs arrive before PeerInfo has made the server peer ready.
        return ConnectedPeer(net, rpc)?.m_server == true;
    }

    private static bool SendChanges(ConfigSync __instance, ConfigEntryBase[] configs)
    {
        var owner = active;
        if (owner == null || !ReferenceEquals(owner.sync, __instance)) return false;
        var net = ZNet.instance;
        if (net == null || net.HaveStopped) return false;
        if (net.IsServer()) return true;
        // Joining with a blank cfg, or restoring local settings on disconnect, cannot publish it.
        if (!__instance.InitialSyncDone || __instance.IsSourceOfTruth || __instance.IsLocked ||
            ConfigSync.ProcessingServerUpdate || ZRoutedRpc.instance == null) return false;
        // An admin-list control packet alone can set InitialSyncDone in ServerSync. Wait until
        // every setting has actually received a server value before allowing client publication.
        if (owner.allEntries.Any(entry => entry.Description.Tags.OfType<OwnConfigEntryBase>().Single().LocalBaseValue == null))
            return false;
        var server = net.GetPeers().FirstOrDefault(peer => peer.m_server && peer.IsReady() && peer.m_socket.IsConnected());
        if (server == null) return false;
        var package = (ZPackage)PackageConfigs.Invoke(null, new object?[] { configs, null, null, true });
        if (package.GetArray().Length > MaxEditBytes)
        {
            owner.warn("Configuration edit exceeds 128 KiB. Edit the server cfg instead.");
            return false;
        }
        // Target zero broadcasts through the server to other clients before validation.
        ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid, __instance.Name + " ConfigSync", package);
        return false;
    }

    private static bool SaveLocalValue(ConfigEntryBase __instance, ref string __result)
    {
        var owner = active;
        if (owner == null || !ReferenceEquals(__instance.ConfigFile, owner.file) || owner.sync.IsSourceOfTruth) return true;
        var entry = __instance.Description.Tags.OfType<OwnConfigEntryBase>().SingleOrDefault();
        if (entry?.LocalBaseValue == null) return true;
        // Preserve local fallback values on disk for administrators as well as ordinary clients.
        __result = TomlTypeConverter.ConvertToString(entry.LocalBaseValue, __instance.SettingType);
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        file.SettingChanged -= OnSettingChanged;
        sync.SourceOfTruthChanged -= OnSourceChanged;
        var rpcName = (sync.Name + " ConfigSync").GetStableHashCode();
        if (ZRoutedRpc.instance != null) ((IDictionary)RoutedHandlers.GetValue(ZRoutedRpc.instance)).Remove(rpcName);
        if (ZNet.instance != null)
            foreach (var peer in ZNet.instance.GetPeers())
                peer.m_rpc.Unregister(sync.Name + " ConfigSync");
        ((HashSet<ConfigSync>)Registrations.GetValue(null)).Remove(sync);
        ((HashSet<VersionCheck>)Versions.GetValue(null)).RemoveWhere(version => ReferenceEquals(VersionOwner.GetValue(version), sync));
        if (ReferenceEquals(active, this)) active = null;
    }
}
