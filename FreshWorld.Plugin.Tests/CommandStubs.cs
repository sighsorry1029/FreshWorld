namespace FreshWorld.Commands;

// Transport and Steam/admin identity checks have their own source-linked harness. Here a mutable
// authority models an authenticated request that can lose authorization while the controller waits.
internal enum FreshWorldCommandAction { Run, Status }

internal sealed class CommandRequestContext(ZNet network, long worldUid, string actor = "remote admin")
{
    public ZNet Network { get; } = network;
    public long WorldUid { get; } = worldUid;
    public string Actor { get; } = actor;
    public bool Authorized = true;
    public bool IsAuthorizedNow => Authorized;
    public List<string> Replies { get; } = new();
    public void Reply(string message) => Replies.Add(message);
}

internal static class FreshWorldCommands
{
    public static Action<FreshWorldCommandAction, CommandRequestContext>? Handler;
    public static int Registrations;
    public static void Register(Action<FreshWorldCommandAction, CommandRequestContext> handler, Action<string> log)
    { Handler = handler; ++Registrations; }
    public static void Unregister() => Handler = null;
}
