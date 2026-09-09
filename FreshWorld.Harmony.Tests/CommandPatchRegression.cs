using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FreshWorld.Commands;
using HarmonyLib;

internal static class CommandPatchRegression
{
    private static readonly MethodInfo NativeCommand = typeof(ZNet).GetMethod("InternalCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly InvalidOperationException PrefixFailure = new("Competing prefix failed.");

    internal static int Run()
    {
        using var harmony = new Harmony("freshworld.command.detour.regression");
        var patched = harmony.CreateClassProcessor(typeof(FreshWorldRemoteCommandPatch)).Patch();
        Require(patched != null && patched.Count == 1, "The production command patch did not produce one detoured method.");
        var info = Harmony.GetPatchInfo(NativeCommand);
        Require(info != null && info.Prefixes.Any(patch => patch.owner == harmony.Id) && info.Finalizers.Any(patch => patch.owner == harmony.Id),
            "Both the production prefix and finalizer must be installed by the real Harmony assembly.");

        var checks = new (string Name, Action Body)[]
        {
            ("direct remote administrator dispatch bypasses the native interpreter", DirectRemote),
            ("removed run and help aliases are rejected without native fallback", RemovedAliases),
            ("direct nonadministrator command is intercepted and denied", DeniedRemote),
            ("direct null-RPC command requires the actual local host", DirectLocal),
            ("nested remote to null-RPC dispatch cannot inherit local authority", NestedRemote),
            ("nested and outer native exceptions restore their respective scopes", NativeExceptions),
            ("an earlier skipping prefix leaves default state without clearing outer provenance", SkippingPrefix),
            ("an earlier throwing prefix leaves default state without clearing outer provenance", ThrowingPrefix)
        };
        foreach (var check in checks)
        {
            check.Body();
            System.Console.WriteLine("PASS installed Harmony " + check.Name);
        }
        return checks.Length;
    }

    private static void DirectRemote()
    {
        using var fixture = new Fixture();
        fixture.Network.InvokeInternalCommand(fixture.Remote, "freshworld status");
        Require(fixture.Requests.Count == 1 && fixture.Requests[0].Action == FreshWorldCommandAction.Status,
            "Authenticated direct dispatch did not invoke the production handler.");
        var context = fixture.Requests[0].Context;
        Require(context.IsAuthorizedNow && context.Actor == "remote administrator steam-admin", "Remote origin was lost.");
        context.Reply("accepted");
        Require(fixture.Network.Replies.Count == 1 && ReferenceEquals(fixture.Network.Replies[0].Rpc, fixture.Remote),
            "The reply was not bound to the original remote RPC.");
        Require(fixture.Network.OriginalCalls.Count == 0, "A direct FreshWorld command reached the native interpreter.");
    }

    private static void RemovedAliases()
    {
        foreach (var command in new[] { "freshworld run", "freshworld help" })
        {
            // Reset the peer and rate-limit state so both aliases must reach the production parser.
            using var fixture = new Fixture();
            fixture.Network.InvokeInternalCommand(fixture.Remote, command);
            Require(fixture.Requests.Count == 0 && fixture.Network.OriginalCalls.Count == 0,
                command + " was dispatched or fell through to the native interpreter.");
            Require(fixture.Network.Replies.Single().Message.Contains("Invalid arguments"),
                command + " did not receive an argument rejection on the authenticated remote connection.");

            fixture.Terminal.InvokeFreshWorldCallback("freshworld");
            Require(fixture.Requests.Count == 1 && fixture.Requests[0].Action == FreshWorldCommandAction.Run &&
                fixture.Requests[0].Context.Actor == "local host",
                "Rejecting a removed alias did not restore local command provenance.");
        }
    }

    private static void DeniedRemote()
    {
        using var fixture = new Fixture();
        fixture.Network.Administrators.Clear();
        fixture.Network.InvokeInternalCommand(fixture.Remote, "freshworld");
        Require(fixture.Requests.Count == 0 && fixture.Network.OriginalCalls.Count == 0, "An unauthorized command was dispatched or passed through.");
        Require(fixture.Network.Replies.Single().Message.Contains("Permission denied"), "The verified remote connection did not receive denial.");
    }

    private static void DirectLocal()
    {
        using var fixture = new Fixture();
        fixture.Network.Dedicated = true;
        fixture.Network.InvokeInternalCommand(null, "freshworld");
        Require(fixture.Requests.Count == 0, "A null-RPC command on a dedicated server acquired local authority.");
        fixture.Network.Dedicated = false;
        fixture.Network.InvokeInternalCommand(null, "freshworld");
        Require(fixture.Requests.Count == 1 && fixture.Requests[0].Action == FreshWorldCommandAction.Run &&
            fixture.Requests[0].Context.Actor == "local host", "The verified listen host could not dispatch directly.");
        Require(fixture.Network.OriginalCalls.Count == 0, "FreshWorld direct commands reached the original method.");
    }

    private static void NestedRemote()
    {
        using var fixture = new Fixture();
        fixture.Network.OriginalBody = (_, command) =>
        {
            Require(command == "relay", "An intercepted nested command unexpectedly reached the original.");
            fixture.Network.InvokeInternalCommand(null, "freshworld");
            fixture.Terminal.InvokeFreshWorldCallback("freshworld");
            Require(fixture.Requests.Count == 0, "Nested null-RPC or terminal dispatch inherited host authority.");
        };
        fixture.Network.InvokeInternalCommand(fixture.Remote, "relay");
        Require(fixture.Network.OriginalCalls.SequenceEqual(new[] { "relay" }), "The unrelated remote command was not passed through exactly once.");
        RequireBlockedThenLocalAllowed(fixture);
    }

    private static void NativeExceptions()
    {
        using var fixture = new Fixture();
        var innerFailure = new InvalidOperationException("Inner native failure.");
        var outerFailure = new InvalidOperationException("Outer native failure.");
        fixture.Network.OriginalBody = (_, command) =>
        {
            if (command == "inner-throws") throw innerFailure;
            Require(command == "outer-throws", "Unexpected native command.");
            var caught = Catch(() => fixture.Network.InvokeInternalCommand(null, "inner-throws"));
            Require(ReferenceEquals(caught, innerFailure), "The inner finalizer replaced or suppressed the native exception.");
            fixture.Terminal.InvokeFreshWorldCallback("freshworld");
            Require(fixture.Requests.Count == 0, "The inner finalizer cleared its outer remote scope.");
            throw outerFailure;
        };
        var error = Catch(() => fixture.Network.InvokeInternalCommand(fixture.Remote, "outer-throws"));
        Require(ReferenceEquals(error, outerFailure), "The outer finalizer replaced or suppressed the native exception.");
        RequireBlockedThenLocalAllowed(fixture);
    }

    private static void SkippingPrefix() => CompetingPrefix("skip-before-freshworld", shouldThrow: false);
    private static void ThrowingPrefix() => CompetingPrefix("throw-before-freshworld", shouldThrow: true);

    private static void CompetingPrefix(string innerCommand, bool shouldThrow)
    {
        using var competing = new Harmony("freshworld.command.competing-prefix.regression");
        competing.Patch(NativeCommand, prefix: new HarmonyMethod(typeof(CommandPatchRegression).GetMethod(nameof(EarlierPrefix), BindingFlags.Static | BindingFlags.NonPublic)!)
        {
            priority = Priority.First + 100
        });
        using var fixture = new Fixture();
        fixture.Network.OriginalBody = (_, command) =>
        {
            Require(command == "outer", "The skipped or throwing native command was executed.");
            var error = Catch(() => fixture.Network.InvokeInternalCommand(null, innerCommand));
            Require(shouldThrow ? ReferenceEquals(error, PrefixFailure) : error == null,
                "The competing prefix's skip/exception behavior changed.");
            fixture.Terminal.InvokeFreshWorldCallback("freshworld");
            Require(fixture.Requests.Count == 0, "A default __state from a skipped FreshWorld prefix cleared the outer remote scope.");
        };
        fixture.Network.InvokeInternalCommand(fixture.Remote, "outer");
        Require(fixture.Network.OriginalCalls.SequenceEqual(new[] { "outer" }), "The competing prefix did not prevent the original inner method.");
        RequireBlockedThenLocalAllowed(fixture);
    }

    // This is intentionally before FreshWorld's Priority.First prefix and can prevent its __state assignment.
    private static bool EarlierPrefix(string command)
    {
        if (command == "throw-before-freshworld") throw PrefixFailure;
        return command != "skip-before-freshworld";
    }

    private static void RequireBlockedThenLocalAllowed(Fixture fixture)
    {
        Require(fixture.Terminal.Lines.Any(line => line.Contains("Indirect remote execution rejected")),
            "The nested terminal callback did not observe remote provenance.");
        fixture.Terminal.InvokeFreshWorldCallback("freshworld");
        Require(fixture.Requests.Count == 1 && fixture.Requests[0].Action == FreshWorldCommandAction.Run &&
            fixture.Requests[0].Context.Actor == "local host", "The outer finalizer did not restore local dispatch after completion.");
    }

    private static Exception? Catch(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ZNet Network = new();
        internal readonly Console Terminal = new();
        internal readonly ZRpc Remote = new(new CommandSocket("steam-admin"));
        internal readonly List<(FreshWorldCommandAction Action, CommandRequestContext Context)> Requests = new();
        private readonly List<string> warnings = new();

        internal Fixture()
        {
            FreshWorldCommands.Unregister();
            ZNet.instance = Network;
            ZNet.World = new World();
            Player.m_localPlayer = new Player();
            Network.LocalPlayerCharacterID = Player.m_localPlayer.GetZDOID();
            Network.Peers.Add(new ZNetPeer(Remote));
            Network.Administrators.Add("steam-admin");
            Console.instance = Terminal;
            Chat.instance = null;
            FreshWorldCommands.Register((action, context) => Requests.Add((action, context)), warnings.Add);
        }

        public void Dispose()
        {
            FreshWorldCommands.Unregister();
            ZNet.instance = null;
            ZNet.World = null;
            Player.m_localPlayer = null;
            Console.instance = null;
            Chat.instance = null;
            Require(warnings.Count == 0, "The command boundary logged an unexpected exception: " + string.Join("; ", warnings));
        }
    }
}
