using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class Program
{
    private static Type adapterType = null!, syncType = null!;
    private static object adapter = null!, sync = null!;
    private static ConfigFile file = null!;
    private static int checks, broadcasts, routedSends;
    private static long lastTarget;
    private static ZPackage? sent;
    private static int changes;
    public static bool ObjectEquals(UnityEngine.Object a, UnityEngine.Object b) => ReferenceEquals(a, b);
    public static bool ObjectNotEquals(UnityEngine.Object a, UnityEngine.Object b) => !ReferenceEquals(a, b);
    public static bool ObjectAlive(UnityEngine.Object a) => !ReferenceEquals(a, null);
    public static bool Administrator(ZNet net, string identity) => identity == "Steam_111";
    public static UnityEngine.Coroutine IgnoreCoroutine(UnityEngine.MonoBehaviour owner, System.Collections.IEnumerator routine) => null!;
    public static bool NativeTarget(MethodInfo method) => method.DeclaringType!.Assembly == typeof(ZNet).Assembly;
    public static string[] Split(string value, char separator, StringSplitOptions options) => value.Split(new[] { separator }, options);
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static object? Call(Type type, object? instance, string name, params object?[] args) =>
        AccessTools.Method(type, name).Invoke(instance, args);
    private static void Set(Type type, object? instance, string name, object? value) => AccessTools.Field(type, name).SetValue(instance, value);
    private static object? Get(Type type, object? instance, string name) => AccessTools.Field(type, name).GetValue(instance);
    private static bool RecordBroadcast() { broadcasts++; return false; }
    private static bool RecordRouted(long targetPeerID, object[] parameters)
    { routedSends++; lastTarget = targetPeerID; sent = (ZPackage)parameters[0]; return false; }

    private static int Main(string[] args)
    {
        if (args.Length != 1) { System.Console.Error.WriteLine("Pass the final merged FreshWorld.dll path."); return 1; }
        var root = Path.Combine(Path.GetTempPath(), "freshworld-sync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { Run(Path.GetFullPath(args[0]), root); System.Console.WriteLine($"PASS {checks} merged-DLL config sync checks (isolated CLR, not an actual game session)."); return 0; }
        catch (Exception error) { System.Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (adapter is IDisposable disposable) disposable.Dispose();
            if (Path.GetDirectoryName(root) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(root).StartsWith("freshworld-sync-tests-", StringComparison.Ordinal)) Directory.Delete(root, true);
        }
    }

    private static void Run(string dll, string root)
    {
        // Inspect the shipped binary, replacing native-engine boundaries in an in-memory test copy.
        // No original DLL or member accessibility is modified. This is not a game runtime test.
        byte[] testImage;
        using (var module = ModuleDefinition.ReadModule(dll))
        {
            Require(!module.AssemblyReferences.Any(r => r.Name == "ServerSync"), "ServerSync was not merged.");
            Require(module.GetType("ServerSync.ConfigSync")?.IsNotPublic == true, "Merged library was not internalized.");
            var initializer = module.GetType("ServerSync.VersionCheck").Methods.Single(m => m.Name == ".cctor");
            var bootstrap = initializer.Body.Instructions.First(i => i.OpCode == OpCodes.Ldtoken &&
                i.Operand is TypeReference type && type.FullName == "BepInEx.ThreadingHelper");
            while (initializer.Body.Instructions.Contains(bootstrap)) initializer.Body.Instructions.RemoveAt(initializer.Body.Instructions.Count - 1);
            initializer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            // Unity logging ends in native engine calls, unavailable in this standalone CLR probe.
            foreach (var type in AllTypes(module.Types))
                foreach (var method in type.Methods.Where(method => method.HasBody))
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (!(instruction.Operand is MethodReference called)) continue;
                        string? replacement = null;
                        if (called.DeclaringType.FullName == "System.String" && called.Name == "Split" && called.Parameters.Count == 2 && called.Parameters[0].ParameterType.FullName == "System.Char") replacement = nameof(Split);
                        if (called.DeclaringType.FullName == "UnityEngine.Object")
                            replacement = called.Name == "op_Equality" ? nameof(ObjectEquals) : called.Name == "op_Inequality" ? nameof(ObjectNotEquals) : called.Name == "op_Implicit" ? nameof(ObjectAlive) : null;
                        if (called.DeclaringType.FullName == "ZNet" && called.Name == "IsAdmin") replacement = nameof(Administrator);
                        if (called.DeclaringType.FullName == "UnityEngine.MonoBehaviour" && called.Name == "StartCoroutine" && called.Parameters.Count == 1 && called.Parameters[0].ParameterType.FullName == "System.Collections.IEnumerator") replacement = nameof(IgnoreCoroutine);
                        if (replacement != null)
                        { instruction.OpCode = OpCodes.Call; instruction.Operand = module.ImportReference(typeof(Program).GetMethod(replacement)); }
                        else if (called.DeclaringType.FullName == "UnityEngine.Debug" && called.Name.StartsWith("Log", StringComparison.Ordinal) && called.Parameters.Count == 1)
                        { instruction.OpCode = OpCodes.Pop; instruction.Operand = null; }
                    }
            var patch = module.GetType("FreshWorld.Configuration.ConfigSynchronization").Methods.Single(m => m.Name == "Patch");
            var il = patch.Body.GetILProcessor(); var original = patch.Body.Instructions[0];
            il.InsertBefore(original, Instruction.Create(OpCodes.Ldarg_1));
            il.InsertBefore(original, Instruction.Create(OpCodes.Call, module.ImportReference(typeof(Program).GetMethod(nameof(NativeTarget)))));
            il.InsertBefore(original, Instruction.Create(OpCodes.Brfalse, original));
            il.InsertBefore(original, Instruction.Create(OpCodes.Ret));
            using (var output = new MemoryStream()) { module.Write(output); testImage = output.ToArray(); }
        }
        var assembly = Assembly.Load(testImage);
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            new AssemblyName(request.Name).Name == assembly.GetName().Name ? assembly : null;
        syncType = assembly.GetType("ServerSync.ConfigSync", true)!;
        adapterType = assembly.GetType("FreshWorld.Configuration.ConfigSynchronization", true)!;
        var configType = assembly.GetType("FreshWorld.Configuration.FreshWorldConfig", true)!;
        var harmony = new Harmony("freshworld.sync.regression");
        // Install the actual BepInEx serialization hooks; game-loop detours need the Unity runtime.
        harmony.PatchAll(syncType.GetNestedType("PreventSavingServerInfo", BindingFlags.NonPublic));
        harmony.PatchAll(syncType.GetNestedType("PreventConfigRereadChangingValues", BindingFlags.NonPublic));

        file = new ConfigFile(Path.Combine(root, "sighsorry.FreshWorld.cfg"), false);
        var config = Activator.CreateInstance(configType, file)!;
        adapter = Activator.CreateInstance(adapterType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { file, config, harmony, (Action)(() => changes++), (Action<string>)(message => System.Console.WriteLine(message)) }, null)!;
        sync = Get(adapterType, adapter, "sync")!;
        Require((bool)Get(syncType, sync, "ModRequired")! == false, "Unmodded clients became required.");
        var versionType = assembly.GetType("ServerSync.VersionCheck", true)!;
        var version = ((System.Collections.IEnumerable)Get(versionType, null, "versionChecks")!).Cast<object>().Single();
        Call(versionType, version, "Initialize");
        Require((bool)Call(versionType, version, "IsVersionOk")!, "A peer without a version handshake was rejected.");
        Require(((Array)Call(versionType, null, "GetFailedServer", new ZRpc(new TestSocket("Steam_222")))!).Length == 0,
            "The server requires an unmodded peer to complete FreshWorld's version handshake.");
        Require((bool)syncType.GetProperty("IsLocked")!.GetValue(sync)!, "Administrator-only policy is not fixed.");
        Require(file.Count == 15, "Integration added config keys.");
        var broadcast = AccessTools.Method(syncType, "Broadcast", new[] { typeof(long), typeof(ConfigEntryBase[]) });
        // Capture server sends without invoking Unity coroutines; run the real client-send prefix separately.
        harmony.Patch(broadcast, prefix: new HarmonyMethod(typeof(Program), nameof(RecordBroadcast)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(ZRoutedRpc), "InvokeRoutedRPC", new[] { typeof(long), typeof(string), typeof(object[]) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(RecordRouted)));

        var zone = file[new ConfigDefinition("Protection", "ZoneSafeZones")];
        var radius = file[new ConfigDefinition("Reset", "ResourceTerrainRadius")];
        var mode = file[new ConfigDefinition("General", "Mode")];
        var blacklist = file[new ConfigDefinition("Protection", "PieceBlacklist")];
        Require(blacklist.GetSerializedValue() == "fire_pit" &&
            !file.ContainsKey(new ConfigDefinition("Protection", "PlayerPlacedObjects")), "Blacklist did not replace the whitelist schema.");
        ZPackage Edit(ConfigEntryBase entry, string raw, bool initial = false)
        {
            var previous = entry.BoxedValue;
            Set(syncType, null, "ProcessingServerUpdate", true);
            try { entry.SetSerializedValue(raw); return Package(initial ? file.Select(pair => pair.Value).ToArray() : new[] { entry }, !initial); }
            finally { entry.BoxedValue = previous; Set(syncType, null, "ProcessingServerUpdate", false); }
        }
        var valid = Edit(zone, "2");
        Call(adapterType, adapter, "ValidateEdit", valid);
        Require(zone.GetSerializedValue() == "1", "Validation mutated live entries.");
        foreach (var entry in file.Select(pair => pair.Value)) Call(adapterType, adapter, "ValidateEdit", Edit(entry, entry.GetSerializedValue()));
        Require(true, "All registered setting types round trip through the real serializer.");
        Reject(Edit(zone, "3")); Reject(Edit(radius, "NaN")); Reject(Edit(mode, "ManualOnly"));
        Reject(Edit(blacklist, "fire_pit,*")); Reject(Edit(blacklist, "fire_pit,fire_pit"));
        var full = Package(new[] { zone }, false); Reject(full);
        var trailing = new ZPackage(valid.GetArray()); trailing.SetPos(trailing.Size()); trailing.Write(42); Reject(trailing);
        var control = new ZPackage(); control.Write((byte)1); control.Write(1); control.Write("Internal"); control.Write("lockexempt"); control.Write(typeof(bool).AssemblyQualifiedName); control.Write(true); Reject(control);
        var unexpectedType = new ZPackage(); unexpectedType.Write((byte)1); unexpectedType.Write(1); unexpectedType.Write("Protection"); unexpectedType.Write("ZoneSafeZones"); unexpectedType.Write(typeof(System.Text.StringBuilder).AssemblyQualifiedName); Reject(unexpectedType);

        var net = (ZNet)FormatterServices.GetUninitializedObject(typeof(ZNet));
        Set(typeof(ZNet), null, "m_instance", net);
        Set(typeof(ZNet), null, "m_isServer", true);
        Set(typeof(ZNet), null, "m_world", FormatterServices.GetUninitializedObject(typeof(World)));
        var admins = (SyncedList)FormatterServices.GetUninitializedObject(typeof(SyncedList));
        Set(typeof(SyncedList), admins, "m_list", new List<string> { "Steam_111" });
        Set(typeof(ZNet), net, "m_adminList", admins);
        var socket = new TestSocket("Steam_111");
        var peer = new ZNetPeer(socket, false) { m_uid = 22 };
        Set(typeof(ZNet), net, "m_peers", new List<ZNetPeer> { peer });
        Set(syncType, null, "isServer", true);
        Require(typeof(ZNet).GetMethod("IsAdmin", new[] { typeof(string) }) != null, "Native public admin API is missing.");
        Set(adapterType, null, "receivingRpc", null);
        ReceiveClient(peer.m_uid, valid);
        Require(zone.GetSerializedValue() == "1", "Missing RPC identity was accepted.");
        Set(adapterType, null, "receivingRpc", peer.m_rpc);
        ReceiveClient(23, valid);
        Require(zone.GetSerializedValue() == "1", "Spoofed sender was accepted.");
        socket.Identity = "Steam_222";
        ReceiveClient(peer.m_uid, valid);
        Require(zone.GetSerializedValue() == "1", "Non-administrator was accepted.");
        socket.Identity = "Steam_111"; socket.Connected = false;
        ReceiveClient(peer.m_uid, valid);
        Require(zone.GetSerializedValue() == "1", "Disconnected peer was accepted.");
        socket.Connected = true;
        var scopeType = adapterType.GetNestedType("RpcScope", BindingFlags.NonPublic)!;
        Call(adapterType, null, "LeaveRpc", Activator.CreateInstance(scopeType));
        Require(ReferenceEquals(Get(adapterType, null, "receivingRpc"), peer.m_rpc), "Skipped prefix cleared outer RPC identity.");
        var nestedRpc = new ZRpc(new TestSocket("Steam_222"));
        var scopeArguments = new object?[] { nestedRpc, null };
        Call(adapterType, null, "EnterRpc", scopeArguments);
        Require(ReferenceEquals(Get(adapterType, null, "receivingRpc"), nestedRpc), "Nested RPC identity was not captured.");
        Call(adapterType, null, "LeaveRpc", scopeArguments[1]);
        Require(ReferenceEquals(Get(adapterType, null, "receivingRpc"), peer.m_rpc), "Nested RPC did not restore outer identity.");
        ReceiveClient(peer.m_uid, Edit(zone, "3"));
        Require(zone.GetSerializedValue() == "1", "Invalid administrator edit was accepted.");
        ReceiveClient(peer.m_uid, valid);
        Require(zone.GetSerializedValue() == "2" && File.ReadAllText(file.ConfigFilePath).Contains("ZoneSafeZones = 2"), "Valid edit was not saved on the server.");
        Require(broadcasts > 0 && changes > 0, "Accepted values were not redistributed or queued for application.");
        ReceiveClient(peer.m_uid, Edit(blacklist, "fire_pit,woodwall"));
        Require(blacklist.GetSerializedValue() == "fire_pit,woodwall" &&
            File.ReadAllText(file.ConfigFilePath).Contains("PieceBlacklist = fire_pit,woodwall"), "Administrator blacklist edit was not saved.");
        var disk = File.ReadAllText(file.ConfigFilePath).Replace("ZoneSafeZones = 2", "ZoneSafeZones = 1")
            .Replace("PieceBlacklist = fire_pit,woodwall", "PieceBlacklist = ");
        File.WriteAllText(file.ConfigFilePath, disk);
        var oldBroadcasts = broadcasts;
        Call(adapterType, adapter, "ReloadFile");
        Require(zone.GetSerializedValue() == "1" && broadcasts == oldBroadcasts && file.SaveOnConfigSet, "Reload sent partial values or left saving disabled.");
        Require(blacklist.GetSerializedValue() == "", "Reload restored default blacklist entries over an empty value.");
        Call(adapterType, adapter, "Publish", 0L);
        Require(broadcasts == oldBroadcasts + 1, "Validated file reload did not publish.");

        // Simulate client and server sequentially in this isolated process, retaining native serializers.
        var serverValues = Edit(zone, "2", true);
        Set(typeof(ZNet), null, "m_isServer", false); Set(syncType, null, "isServer", false);
        peer.m_server = true; peer.m_uid = 99;
        new ZRoutedRpc(false);
        Set(syncType, null, "lockExempt", true); // Even stale admin state must not publish before initial sync.
        Call(adapterType, null, "SendChanges", sync, new[] { zone });
        Require(routedSends == 0, "Blank client config uploaded before the server handshake.");
        var unknownRpc = new ZRpc(new TestSocket("Steam_333"));
        Call(syncType, sync, "RPC_FromServerConfigSync", unknownRpc, new ZPackage(serverValues.GetArray()));
        Require(!(bool)syncType.GetProperty("InitialSyncDone")!.GetValue(sync)!, "Unknown RPC impersonated the server.");
        Call(syncType, sync, "RPC_FromServerConfigSync", peer.m_rpc, new ZPackage(control.GetArray()));
        Call(adapterType, null, "SendChanges", sync, new[] { zone });
        Require(routedSends == 0, "Admin control packet permitted publishing before the actual settings arrived.");
        Call(syncType, sync, "RPC_FromServerConfigSync", peer.m_rpc, new ZPackage(serverValues.GetArray()));
        Require(zone.BoxedValue.ToString()!.StartsWith("2 =", StringComparison.Ordinal), "Server choices were not applied.");
        Require(File.ReadAllText(file.ConfigFilePath).Contains("ZoneSafeZones = 1"), "Server values overwrote the administrator's local cfg.");
        Require(routedSends == 0, "Initial sync echoed defaults to the server.");
        zone.SetSerializedValue("0"); // Direct file reload updates the local fallback only.
        Require(zone.BoxedValue.ToString()!.StartsWith("2 =", StringComparison.Ordinal), "Local cfg reload replaced active server values.");
        var rawConstructor = zone.SettingType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null)!;
        zone.BoxedValue = rawConstructor.Invoke(new object[] { "1" });
        Require(routedSends == 1 && lastTarget == 99 && sent != null, "Administrator edit was broadcast to everyone or not sent to the server.");
        Set(syncType, null, "lockExempt", false);
        zone.BoxedValue = rawConstructor.Invoke(new object[] { "2" });
        Require(routedSends == 1, "Ordinary client published a change.");
        var shutDown = syncType.GetNestedType("ResetConfigsOnShutdown", BindingFlags.NonPublic)!;
        Call(shutDown, null, "Postfix");
        Require(zone.GetSerializedValue() == "0" && (bool)syncType.GetProperty("IsSourceOfTruth")!.GetValue(sync)!, "Disconnect did not restore local fallback.");
        Require(!(bool)syncType.GetProperty("InitialSyncDone")!.GetValue(sync)! && routedSends == 1, "Disconnect retained initial sync state or published local values.");
        ZRoutedRpc.instance.Register<ZPackage>("sighsorry.FreshWorld ConfigSync", (_, __) => { });
        peer.m_rpc.Register<ZPackage>("sighsorry.FreshWorld ConfigSync", (_, __) => { });
        var rpcKey = "sighsorry.FreshWorld ConfigSync".GetStableHashCode();
        ((IDisposable)adapter).Dispose();
        Require(!((System.Collections.IEnumerable)Get(syncType, null, "configSyncs")!).Cast<object>().Contains(sync), "Disposed sync retained registration.");
        Require(!((System.Collections.IDictionary)Get(typeof(ZRoutedRpc), ZRoutedRpc.instance, "m_functions")!).Contains(rpcKey) &&
            !((System.Collections.IDictionary)Get(typeof(ZRpc), peer.m_rpc, "m_functions")!).Contains(rpcKey), "Disposed sync retained live RPC handlers.");
    }

    private static ZPackage Package(ConfigEntryBase[] entries, bool partial) =>
        (ZPackage)Call(syncType, null, "ConfigsToPackage", entries, null, null, partial)!;
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }
    private static void ReceiveClient(long id, ZPackage package) =>
        Call(syncType, sync, "RPC_FromOtherClientConfigSync", id, new ZPackage(package.GetArray()));
    private static void Reject(ZPackage package)
    {
        try { Call(adapterType, adapter, "ValidateEdit", package); }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { checks++; return; }
        throw new Exception("Invalid configuration passed validation.");
    }
    private sealed class TestSocket : ISocket
    {
        internal string Identity; internal bool Connected = true;
        internal TestSocket(string identity) => Identity = identity;
        public bool IsConnected() => Connected;
        public void Send(ZPackage package) { }
        public ZPackage Recv() => null!;
        public int GetSendQueueSize() => 0;
        public int GetCurrentSendRate() => 0;
        public bool IsHost() => false;
        public void Dispose() { }
        public bool GotNewData() => false;
        public void Close() => Connected = false;
        public string GetEndPointString() => Identity;
        public void GetAndResetStats(out int sentBytes, out int receivedBytes) { sentBytes = receivedBytes = 0; }
        public void GetConnectionQuality(out float local, out float remote, out int ping, out float outgoing, out float incoming)
        { local = remote = outgoing = incoming = 0; ping = 0; }
        public ISocket Accept() => null!;
        public int GetHostPort() => 0;
        public bool Flush() => true;
        public string GetHostName() => Identity;
        public void VersionMatch() { }
    }
}
