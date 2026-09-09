using FreshWorld.Commands;
using System.Reflection;

internal static class Program
{
    private static readonly List<(FreshWorldCommandAction Action, CommandRequestContext Context)> Received = new();
    private static readonly List<string> Warnings = new();

    private static int Main()
    {
        var tests = new (string Name, Action Test)[]
        {
            ("strict syntax and cfg override rejection", Syntax),
            ("native registration and autocomplete", Registration),
            ("idempotent registration and owned cleanup", OwnedCleanup),
            ("foreign registration fails without replacing it", ForeignRegistration),
            ("Harmony intercept targets exact native signature", PatchContract),
            ("verified local host works without administrator entry", LocalHost),
            ("missing or mismatched local character cannot gain host privilege", InvalidLocalCharacter),
            ("headless server has no local player privilege", DedicatedLocal),
            ("remote administrator runs and gets targeted replies", RemoteAdmin),
            ("same PC and claimed client flags cannot grant remote admin", SpoofedClient),
            ("remote nonadmin on a listen host cannot inherit host privilege", ListenHostRemote),
            ("unauthenticated and foreign RPCs are rejected", ForeignRpc),
            ("unready and server peers are rejected", UnreadyPeer),
            ("disconnected and mismatched sockets are rejected", InvalidSocket),
            ("authorization is rechecked after administrator removal", RevokedAdmin),
            ("pending request expires when the world or network changes", ChangedSession),
            ("reconnected identity never receives old replies", Reconnection),
            ("peer and socket identity changes invalidate pending requests", ChangedPeerIdentity),
            ("removed aliases and malformed remote commands cannot dispatch or reach game interpreter", InvalidRemoteArguments),
            ("unrelated commands pass through untouched", UnrelatedCommands),
            ("per-peer request flood is bounded independently", RateLimit),
            ("plugin unregister invalidates outstanding contexts", UnregisterInvalidates),
            ("reply failures cannot escape into maintenance", ReplyFailure),
            ("local player replacement invalidates pending requests", LocalReplacement),
            ("stopped and client worlds cannot dispatch", NonHostSession),
            ("remote alternate interpreter cannot inherit listen-host privilege", IndirectRemote),
            ("nested remote scopes and exceptions restore provenance", NestedProvenance),
        };
        var failed = 0;
        foreach (var test in tests)
        {
            try { Reset(); test.Test(); System.Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; System.Console.WriteLine("FAIL " + test.Name + ": " + error); }
        }
        FreshWorldCommands.Unregister();
        System.Console.WriteLine($"Command tests: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool value, string message = "Assertion failed")
    { if (!value) throw new InvalidOperationException(message); }

    private static void Reset()
    {
        FreshWorldCommands.Unregister();
        Terminal.Catalog.Clear();
        Received.Clear(); Warnings.Clear();
        ZNet.instance = new ZNet();
        ZNet.World = new object();
        Player.m_localPlayer = null;
        Console.instance = new Console(); Chat.instance = new Chat();
        FreshWorldCommands.Register((action, context) => Received.Add((action, context)), Warnings.Add);
    }

    private static ZNet Net => ZNet.instance!;
    private static Terminal.ConsoleCommand Command => Terminal.Catalog["freshworld"];
    private static void SendLocal(string text = "freshworld") => Command.Action(new Terminal.ConsoleEventArgs(text, Console.instance!));
    private static bool SendRemote(ZNetPeer peer, string text = "freshworld") => FreshWorldCommands.HandleInternalCommand(Net, peer.m_rpc, text);
    private static void HostPlayer()
    {
        Player.m_localPlayer = new Player { Id = new ZDOID(123) };
        Net.LocalPlayerCharacterID = Player.m_localPlayer.Id;
    }
    private static ZNetPeer Peer(bool admin = true, string identity = "Steam_76561198000000001")
    {
        var peer = new ZNetPeer(new FakeSocket { Identity = identity });
        Net.Peers.Add(peer);
        if (admin) Net.Admins.Add(identity);
        return peer;
    }

    private static void Syntax()
    {
        var valid = new[] { "freshworld", "freshworld  ", "FRESHWORLD", "freshworld status", "FRESHWORLD STATUS", "freshworld   status" };
        foreach (var line in valid) Check(CommandSyntax.TryParse(line, out _), line);
        var invalid = new[] { "freshworld run", "freshworld help", "freshworld RUN", "freshworld HELP",
            " freshworld", "freshworld force", "freshworld Force=true", "freshworld;save",
            "freshworld status; save", "freshworld\nstatus", "freshworld\tstatus", "freshworld status\0", "freshworld *",
            "freshworld\u00a0status", "freshworld " + new string(' ', 65), "freshworld status status", "freshworldｓｔａｔｕｓ" };
        foreach (var line in invalid) Check(!CommandSyntax.TryParse(line, out _), line);
        Check(!CommandSyntax.TryParse(null, out _));
        Check(CommandSyntax.TryParse("freshworld", out var action) && action == FreshWorldCommandAction.Run);
        Check(CommandSyntax.TryParse("freshworld status", out action) && action == FreshWorldCommandAction.Status);
    }

    private static void Registration()
    {
        Check(Command.OnlyServer && Command.RemoteCommand && !Command.IsCheat);
        Check(Command.GetTabOptions()!.SequenceEqual(new[] { "status" }));
        Command.GetTabOptions()!.Add("force");
        Check(!Command.GetTabOptions()!.Contains("force"));
        Console.instance!.Cache.Add("freshworld"); Chat.instance!.Cache.Add("freshworld");
        FreshWorldCommands.Unregister();
        Check(Console.instance.Cache.Count == 0 && Chat.instance.Cache.Count == 0);
    }

    private static void OwnedCleanup()
    {
        var original = Command;
        FreshWorldCommands.Register((action, context) => Received.Add((action, context)), Warnings.Add);
        Check(ReferenceEquals(original, Command));
        var replacement = new Terminal.ConsoleCommand("freshworld", "other mod", _ => { });
        FreshWorldCommands.Unregister();
        Check(ReferenceEquals(replacement, Command));
        FreshWorldCommands.Unregister();
        Check(ReferenceEquals(replacement, Command));
    }

    private static void ForeignRegistration()
    {
        FreshWorldCommands.Unregister();
        var foreign = new Terminal.ConsoleCommand("freshworld", "other", _ => { });
        var rejected = false;
        try { FreshWorldCommands.Register((_, _) => { }, Warnings.Add); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && ReferenceEquals(foreign, Command));
    }

    private static void PatchContract()
    {
        var patch = typeof(FreshWorldRemoteCommandPatch).GetCustomAttribute<HarmonyLib.HarmonyPatch>()!;
        Check(patch.TargetType == typeof(ZNet) && patch.TargetMethod == "InternalCommand");
        Check(patch.Arguments.SequenceEqual(new[] { typeof(ZRpc), typeof(string) }));
        var prefix = typeof(FreshWorldRemoteCommandPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(prefix.GetCustomAttribute<HarmonyLib.HarmonyPrefix>() != null);
        var peer = Peer();
        var arguments = new object[] { Net, peer.m_rpc, "freshworld", default(FreshWorldCommands.NativeCommandScope) };
        try { Check(!(bool)prefix.Invoke(null, arguments)! && Received.Count == 1); }
        finally { InvokeFinalizer((FreshWorldCommands.NativeCommandScope)arguments[3]); }
        var finalizer = typeof(FreshWorldRemoteCommandPatch).GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(finalizer.GetCustomAttribute<HarmonyLib.HarmonyFinalizer>() != null);
    }

    private static void LocalHost()
    {
        HostPlayer(); SendLocal();
        Check(Received.Count == 1 && Received[0].Action == FreshWorldCommandAction.Run);
        Check(Received[0].Context.IsAuthorizedNow && Net.AdminChecks == 0);
        Received[0].Context.Reply("accepted");
        Check(Console.instance!.Output.Single().Contains("accepted"));
        Check(Received[0].Context.WorldUid == Net.WorldUid && ReferenceEquals(Received[0].Context.Network, Net));
    }

    private static void InvalidLocalCharacter()
    {
        SendLocal(); Check(Received.Count == 0);
        HostPlayer(); Net.LocalPlayerCharacterID = new ZDOID(999); SendLocal(); Check(Received.Count == 0);
        Player.m_localPlayer!.Id = ZDOID.None; Net.LocalPlayerCharacterID = ZDOID.None;
        SendLocal(); Check(Received.Count == 0);
    }

    private static void DedicatedLocal()
    {
        Net.Dedicated = true; HostPlayer(); SendLocal(); Check(Received.Count == 0);
        Check(!FreshWorldCommands.HandleInternalCommand(Net, null, "freshworld"));
        Check(Received.Count == 0);
    }

    private static void RemoteAdmin()
    {
        Net.Dedicated = true;
        var peer = Peer(); Check(!SendRemote(peer));
        Check(Received.Count == 1 && Net.AdminChecks > 0);
        Received[0].Context.Reply("complete");
        Check(ReferenceEquals(Net.Messages.Single().Rpc, peer.m_rpc));
        Check(Net.Messages.Single().Message.Contains("complete"));
        Check(Console.instance!.Output.Count == 0);
    }

    private static void SpoofedClient()
    {
        Net.Dedicated = true;
        var peer = Peer(false);
        var socket = (FakeSocket)peer.m_socket;
        socket.ClientClaimsAdmin = true; socket.ClientClaimsSteamId = "Steam_Administrator";
        Net.Admins.Add(socket.ClientClaimsSteamId);
        Check(!SendRemote(peer) && Received.Count == 0);
        Check(Net.Messages.Single().Message.Contains("Permission denied"));
    }

    private static void ListenHostRemote()
    {
        HostPlayer(); var peer = Peer(false);
        Check(!SendRemote(peer) && Received.Count == 0);
        Check(Net.Messages.Count == 1 && Console.instance!.Output.Count == 0);
    }

    private static void ForeignRpc()
    {
        var registered = Peer();
        var forgedRpc = new ZRpc(registered.m_socket);
        Check(!FreshWorldCommands.HandleInternalCommand(Net, forgedRpc, "freshworld"));
        Check(Received.Count == 0 && Net.Messages.Count == 0);
        Net.Peers.Clear(); Check(!SendRemote(registered) && Received.Count == 0);
    }

    private static void UnreadyPeer()
    {
        var peer = Peer(); peer.m_uid = 0;
        Check(!SendRemote(peer) && Received.Count == 0);
        peer.m_uid = 7; peer.m_server = true;
        Check(!SendRemote(peer) && Received.Count == 0);
    }

    private static void InvalidSocket()
    {
        var peer = Peer(); ((FakeSocket)peer.m_socket).Connected = false;
        Check(!SendRemote(peer) && Received.Count == 0);
        ((FakeSocket)peer.m_socket).Connected = true;
        peer.m_rpc.Socket = new FakeSocket();
        Check(!SendRemote(peer) && Received.Count == 0);
    }

    private static void RevokedAdmin()
    {
        var peer = Peer(); SendRemote(peer); var context = Received.Single().Context;
        Check(context.IsAuthorizedNow); Net.Admins.Clear();
        Check(!context.IsAuthorizedNow);
        context.Reply("Permission revoked");
        Check(Net.Messages.Single().Message.Contains("revoked"));
    }

    private static void ChangedSession()
    {
        var peer = Peer(); SendRemote(peer); var context = Received.Single().Context;
        ZNet.World = new object();
        Check(!context.IsAuthorizedNow); context.Reply("stale"); Check(Net.Messages.Count == 0);
        Reset(); peer = Peer(); SendRemote(peer); context = Received.Single().Context;
        Net.WorldUid++;
        Check(!context.IsAuthorizedNow); context.Reply("stale"); Check(Net.Messages.Count == 0);
        Reset(); peer = Peer(); SendRemote(peer); context = Received.Single().Context;
        var original = Net; ZNet.instance = new ZNet();
        Check(!context.IsAuthorizedNow); context.Reply("stale"); Check(original.Messages.Count == 0);
    }

    private static void Reconnection()
    {
        var peer = Peer(); SendRemote(peer); var context = Received.Single().Context;
        ((FakeSocket)peer.m_socket).Connected = false;
        Check(!context.IsAuthorizedNow); context.Reply("stale"); Check(Net.Messages.Count == 0);
        Net.Peers.Clear(); var reconnected = Peer();
        Check(!context.IsAuthorizedNow); context.Reply("stale"); Check(Net.Messages.Count == 0);
        Check(!SendRemote(reconnected) && Received.Count == 2);
    }

    private static void ChangedPeerIdentity()
    {
        var peer = Peer(); SendRemote(peer); var context = Received.Single().Context;
        peer.m_uid++; Check(!context.IsAuthorizedNow);
        peer.m_uid--; ((FakeSocket)peer.m_socket).Identity = "Steam_SomeoneElse";
        Net.Admins.Add("Steam_SomeoneElse"); Check(!context.IsAuthorizedNow);
        context.Reply("stale"); Check(Net.Messages.Count == 0);
    }

    private static void InvalidRemoteArguments()
    {
        foreach (var line in new[] { "freshworld run", "freshworld help", "freshworld RUN", "freshworld HELP",
            "freshworld Force=true", "freshworld\nstatus" })
        {
            // A fresh authenticated peer ensures each rejection reaches parsing rather than the rate limit.
            Reset();
            var peer = Peer();
            Check(!SendRemote(peer, line) && Received.Count == 0, line + " dispatched or reached the native interpreter");
            Check(Net.AdminChecks > 0 && Net.Messages.Single().Message.Contains("Invalid arguments"),
                line + " must produce an argument rejection for an authorized remote administrator");
        }
    }

    private static void UnrelatedCommands()
    {
        var peer = Peer();
        foreach (var text in new[] { "save", "freshworld_other", "freshworldrun", "freshworld;save", " freshworld" })
            Check(SendRemote(peer, text));
        Check(Received.Count == 0 && Net.AdminChecks == 0);
    }

    private static void RateLimit()
    {
        var peer = Peer();
        for (var i = 0; i < 1000; i++) Check(!SendRemote(peer));
        Check(Received.Count == 1);
        var other = Peer(true, "Steam_76561198000000002");
        SendRemote(other, "freshworld status");
        Check(Received.Count == 2 && Received[1].Action == FreshWorldCommandAction.Status);
    }

    private static void UnregisterInvalidates()
    {
        var peer = Peer(); SendRemote(peer); var context = Received.Single().Context;
        FreshWorldCommands.Unregister();
        Check(!context.IsAuthorizedNow); context.Reply("stale"); Check(Net.Messages.Count == 0);
        FreshWorldCommands.Register((action, next) => Received.Add((action, next)), Warnings.Add);
        Check(!context.IsAuthorizedNow); context.Reply("stale"); Check(Net.Messages.Count == 0);
    }

    private static void ReplyFailure()
    {
        var peer = Peer(); SendRemote(peer); var context = Received.Single().Context;
        Net.ThrowOnPrint = true; context.Reply("complete");
        Check(context.IsAuthorizedNow);
        Net.ThrowOnPrint = false; context.Reply(new string('x', 3000));
        Check(Net.Messages.Single().Message.Length < 2100);
    }

    private static void LocalReplacement()
    {
        HostPlayer(); SendLocal(); var context = Received.Single().Context;
        HostPlayer(); Check(!context.IsAuthorizedNow);
        context.Reply("stale"); Check(Console.instance!.Output.Count == 0);
    }

    private static void NonHostSession()
    {
        var peer = Peer(); Net.HaveStopped = true;
        Check(!SendRemote(peer) && Received.Count == 0);
        Net.HaveStopped = false; Net.Server = false;
        Check(!SendRemote(peer) && Received.Count == 0);
        Net.Server = true; ZNet.World = null;
        Check(!SendRemote(peer) && Received.Count == 0);
    }

    private static void InvokeFinalizer(FreshWorldCommands.NativeCommandScope state) =>
        typeof(FreshWorldRemoteCommandPatch).GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { state });

    private static (bool RunOriginal, FreshWorldCommands.NativeCommandScope State) InvokePrefix(ZRpc? rpc, string text)
    {
        var arguments = new object?[] { Net, rpc, text, default(FreshWorldCommands.NativeCommandScope) };
        var run = (bool)typeof(FreshWorldRemoteCommandPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, arguments)!;
        return (run, (FreshWorldCommands.NativeCommandScope)arguments[3]!);
    }

    private static void IndirectRemote()
    {
        HostPlayer(); var peer = Peer(false);
        // Vanilla does not trim this text. Simulate an alternate interpreter doing so anyway.
        var entered = InvokePrefix(peer.m_rpc, " freshworld");
        Check(entered.RunOriginal, "Unrelated native command execution must stay unchanged");
        try
        {
            SendLocal("freshworld");
            Check(Received.Count == 0, "A remote context must never be classified as the local host");
        }
        finally { InvokeFinalizer(entered.State); }
        SendLocal(); Check(Received.Count == 1, "Local privilege must return outside the remote scope");
    }

    private static void NestedProvenance()
    {
        HostPlayer(); var peer = Peer();
        var outer = InvokePrefix(peer.m_rpc, "some_other_command");
        Check(outer.RunOriginal);
        try
        {
            // An alias can call native RemoteCommand on the server, which supplies null RPC.
            var inner = InvokePrefix(null, "freshworld");
            try { Check(!inner.RunOriginal && Received.Count == 0); }
            finally { InvokeFinalizer(inner.State); }
            // If another mod skipped our inner prefix, Harmony still runs the finalizer with default state.
            InvokeFinalizer(default);
            SendLocal(); Check(Received.Count == 0, "Nested scope exit must preserve remote outer provenance");
            throw new IOException("Simulated original interpreter failure");
        }
        catch (IOException) { }
        finally { InvokeFinalizer(outer.State); }
        SendLocal(); Check(Received.Count == 1, "Exception finalizer must restore true local context");
    }
}
