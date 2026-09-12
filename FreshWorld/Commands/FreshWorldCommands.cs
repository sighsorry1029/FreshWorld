using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace FreshWorld.Commands;

internal static class FreshWorldCommands
{
    private static readonly FieldInfo CommandsField = AccessTools.Field(typeof(Terminal), "commands")
        ?? throw new MissingFieldException(typeof(Terminal).FullName, "commands");
    private static readonly FieldInfo CommandListField = AccessTools.Field(typeof(Terminal), "m_commandList")
        ?? throw new MissingFieldException(typeof(Terminal).FullName, "m_commandList");
    private static readonly Dictionary<ZRpc, double> RemoteRequests = new();
    private static Action<FreshWorldCommandAction, CommandRequestContext>? handler;
    private static Action<string>? warning;
    private static Terminal.ConsoleCommand? owned;
    private static ZNet? rateLimitNetwork;
    private static double lastLocalRequest = double.NegativeInfinity;
    private static long generation;
    [ThreadStatic] private static int remoteExecutionDepth;

    private static Dictionary<string, Terminal.ConsoleCommand> Commands =>
        (Dictionary<string, Terminal.ConsoleCommand>)CommandsField.GetValue(null)!;

    public static void Register(Action<FreshWorldCommandAction, CommandRequestContext> callback, Action<string> warn)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (warn == null) throw new ArgumentNullException(nameof(warn));
        if (owned != null)
        {
            if (!Commands.TryGetValue(CommandSyntax.Name, out var existing) || !ReferenceEquals(existing, owned))
                throw new InvalidOperationException("Another plugin replaced the freshworld command.");
            handler = callback;
            warning = warn;
            return;
        }
        if (Commands.ContainsKey(CommandSyntax.Name))
            throw new InvalidOperationException("The freshworld command is already registered by another plugin.");
        handler = callback;
        warning = warn;
        generation++;
        // Valheim forwards a remote command from a client to the server even though OnlyServer
        // prevents the local client action. No client cheat or administrator flag grants access.
        owned = new Terminal.ConsoleCommand(CommandSyntax.Name, CommandSyntax.Usage,
            (Terminal.ConsoleEvent)HandleLocal, isCheat: false, onlyServer: true, hideBehindDevCommands: false,
            optionsFetcher: () => new List<string> { "status" }, remoteCommand: true);
        ClearAutocomplete();
    }

    public static void Unregister()
    {
        generation++;
        handler = null;
        warning = null;
        if (owned != null && Commands.TryGetValue(CommandSyntax.Name, out var existing) && ReferenceEquals(existing, owned))
            Commands.Remove(CommandSyntax.Name);
        owned = null;
        RemoteRequests.Clear();
        rateLimitNetwork = null;
        lastLocalRequest = double.NegativeInfinity;
        ClearAutocomplete();
    }

    private static void ClearAutocomplete()
    {
        // Existing terminals cache command names; new terminals start with an empty cache.
        if (Console.instance != null) ((List<string>)CommandListField.GetValue(Console.instance)!).Clear();
        if (Chat.instance != null) ((List<string>)CommandListField.GetValue(Chat.instance)!).Clear();
    }

    private static bool CurrentWorld(ZNet network, object world, long uid, long epoch) =>
        handler != null && generation == epoch && ReferenceEquals(ZNet.instance, network) && network != null &&
        network.IsServer() && !network.HaveStopped && ReferenceEquals(ZNet.World, world) && network.GetWorldUID() == uid;

    private static CommandRequestContext? LocalContext(ZNet? network, Terminal? terminal)
    {
        if (network == null || !network.IsServer() || network.IsDedicated() || network.HaveStopped || ZNet.World == null)
            return null;
        var player = Player.m_localPlayer;
        if (player == null) return null;
        var character = player.GetZDOID();
        if (character == ZDOID.None || network.LocalPlayerCharacterID != character) return null;
        var world = ZNet.World;
        var uid = network.GetWorldUID();
        var epoch = generation;
        bool Current() => CurrentWorld(network, world, uid, epoch) && !network.IsDedicated() &&
            ReferenceEquals(Player.m_localPlayer, player) && player != null &&
            player.GetZDOID() == character && network.LocalPlayerCharacterID == character;
        return new CommandRequestContext(network, uid, "local host", () => true, Current,
            message => { if (terminal != null) terminal.AddString(message); });
    }

    private static CommandRequestContext? DedicatedConsoleContext(ZNet? network, Terminal? terminal)
    {
        if (network == null || !ReferenceEquals(network, ZNet.instance) || !network.IsServer() ||
            !network.IsDedicated() || network.HaveStopped || ZNet.World == null) return null;
        var world = ZNet.World;
        var uid = network.GetWorldUID();
        var epoch = generation;
        bool Current() => CurrentWorld(network, world, uid, epoch) && network.IsDedicated();
        // RCON providers execute authenticated commands inside the dedicated server process but
        // normally do not expose their remote identity as a Valheim player ZRpc. Authentication
        // remains the provider's responsibility; FreshWorld retains its world/session validation.
        return new CommandRequestContext(network, uid, "dedicated server console", () => true, Current,
            message => { if (terminal != null) terminal.AddString(message); });
    }

    private static CommandRequestContext? RemoteContext(ZNet network, ZRpc rpc)
    {
        if (network == null || !ReferenceEquals(network, ZNet.instance) || !network.IsServer() ||
            network.HaveStopped || ZNet.World == null || rpc == null) return null;
        ZNetPeer? peer = null;
        foreach (var candidate in network.GetPeers())
            if (ReferenceEquals(candidate.m_rpc, rpc)) { peer = candidate; break; }
        if (peer == null || peer.m_server || !peer.IsReady()) return null;
        var socket = rpc.GetSocket();
        if (socket == null || !ReferenceEquals(socket, peer.m_socket) || !socket.IsConnected()) return null;
        var identity = socket.GetHostName();
        if (string.IsNullOrWhiteSpace(identity)) return null;
        var world = ZNet.World;
        var worldUid = network.GetWorldUID();
        var peerUid = peer.m_uid;
        var epoch = generation;
        bool Current() => CurrentWorld(network, world, worldUid, epoch) &&
            network.GetPeers().Contains(peer) && peer.IsReady() && !peer.m_server && peer.m_uid == peerUid &&
            ReferenceEquals(peer.m_rpc, rpc) && ReferenceEquals(peer.m_socket, socket) &&
            ReferenceEquals(rpc.GetSocket(), socket) && socket.IsConnected() &&
            string.Equals(socket.GetHostName(), identity, StringComparison.Ordinal);
        return new CommandRequestContext(network, worldUid, "remote administrator " + identity,
            () => network.IsAdmin(identity), Current, message => network.RemotePrint(rpc, message));
    }

    private static bool TakeRateLimit(ZNet network, ZRpc? rpc)
    {
        if (!ReferenceEquals(rateLimitNetwork, network))
        {
            rateLimitNetwork = network;
            RemoteRequests.Clear();
            lastLocalRequest = double.NegativeInfinity;
        }
        var now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        if (rpc == null)
        {
            if (now - lastLocalRequest < 1) return false;
            lastLocalRequest = now;
            return true;
        }
        if (RemoteRequests.TryGetValue(rpc, out var previous))
        {
            if (now - previous < 1) return false;
            RemoteRequests[rpc] = now;
            return true;
        }
        // Bound retained state by live peers; never allocate a new entry for an unverified RPC.
        var stale = new List<ZRpc>();
        foreach (var entry in RemoteRequests)
        {
            var found = false;
            foreach (var peer in network.GetPeers())
                if (ReferenceEquals(peer.m_rpc, entry.Key)) { found = true; break; }
            if (!found) stale.Add(entry.Key);
        }
        foreach (var entry in stale) RemoteRequests.Remove(entry);
        if (RemoteRequests.Count >= 128) return false;
        RemoteRequests.Add(rpc, now);
        return true;
    }

    private static void Dispatch(string line, CommandRequestContext context, ZRpc? rpc)
    {
        if (!context.IsAuthorizedNow)
        {
            context.Reply("Permission denied. Remote players must be administrators verified by the server.");
            return;
        }
        if (!TakeRateLimit(context.Network, rpc)) return;
        if (!CommandSyntax.TryParse(line, out var action))
        {
            context.Reply("Invalid arguments. " + CommandSyntax.Usage);
            return;
        }
        handler?.Invoke(action, context);
    }

    private static void HandleLocal(Terminal.ConsoleEventArgs args)
    {
        try
        {
            // The native interpreter currently does not trim or expand aliases, but other mods
            // can do so. A callback reached indirectly during any remote command cannot inherit
            // the listen host's local-player privilege. Only our authenticated direct path runs it.
            if (remoteExecutionDepth != 0)
            {
                args.Context.AddString("[FreshWorld] Indirect remote execution rejected. Send freshworld directly.");
                return;
            }
            var network = ZNet.instance;
            var context = network != null && network.IsDedicated()
                ? DedicatedConsoleContext(network, args.Context)
                : LocalContext(network, args.Context);
            if (context == null)
            {
                args.Context.AddString("[FreshWorld] A verified local world host or active dedicated server console is required. Connected players must be server administrators.");
                return;
            }
            Dispatch(args.FullLine, context, null);
        }
        catch (Exception error)
        {
            warning?.Invoke("FreshWorld command failed: " + error.Message);
            args.Context.AddString("[FreshWorld] Command failed; see the host log.");
        }
    }

    // Return false only for our command so no general interpreter can discard the remote sender.
    internal static bool HandleInternalCommand(ZNet network, ZRpc? rpc, string line)
    {
        if (handler == null || !CommandSyntax.IsFreshWorld(line)) return true;
        try
        {
            if (rpc == null)
            {
                // Native server-console and RCON execution carries no Valheim player identity.
                // A remote player command cannot enter this path while its outer RPC scope is active.
                if (remoteExecutionDepth != 0) return false;
                var local = network.IsDedicated()
                    ? DedicatedConsoleContext(network, Console.instance)
                    : LocalContext(network, Console.instance);
                if (local != null) Dispatch(line, local, null);
            }
            else
            {
                var remote = RemoteContext(network, rpc);
                if (remote != null) Dispatch(line, remote, rpc);
            }
        }
        catch (Exception error) { warning?.Invoke("FreshWorld remote command rejected: " + error.Message); }
        return false;
    }

    internal readonly struct NativeCommandScope
    {
        internal readonly int PreviousDepth;
        internal readonly bool Entered;
        internal NativeCommandScope(int previousDepth) { PreviousDepth = previousDepth; Entered = true; }
    }

    internal static NativeCommandScope EnterNativeCommand(ZRpc? rpc)
    {
        var previous = remoteExecutionDepth;
        if (rpc != null) remoteExecutionDepth++;
        return new NativeCommandScope(previous);
    }

    internal static void ExitNativeCommand(NativeCommandScope scope)
    {
        // A different prefix can skip ours; its default __state must not clear an outer scope.
        if (scope.Entered) remoteExecutionDepth = scope.PreviousDepth;
    }
}

[HarmonyPatch(typeof(ZNet), "InternalCommand", new[] { typeof(ZRpc), typeof(string) })]
internal static class FreshWorldRemoteCommandPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ZNet __instance, ZRpc rpc, string command, out FreshWorldCommands.NativeCommandScope __state)
    {
        // Keep provenance around the entire native call, including unrelated commands. A finalizer
        // restores nesting even when the original, our prefix, or another prefix throws/skips it.
        __state = FreshWorldCommands.EnterNativeCommand(rpc);
        return FreshWorldCommands.HandleInternalCommand(__instance, rpc, command);
    }

    [HarmonyFinalizer]
    private static void Finalizer(FreshWorldCommands.NativeCommandScope __state) => FreshWorldCommands.ExitNativeCommand(__state);
}
