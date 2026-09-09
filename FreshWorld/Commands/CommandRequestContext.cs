using System;

namespace FreshWorld.Commands;

/// <summary>Authority and replies stay bound to the original world and actual connection.</summary>
internal sealed class CommandRequestContext
{
    private readonly Func<bool> authorized;
    private readonly Func<bool> current;
    private readonly Action<string> reply;

    public ZNet Network { get; }
    public long WorldUid { get; }
    public string Actor { get; }

    internal CommandRequestContext(ZNet network, long worldUid, string actor,
        Func<bool> authorized, Func<bool> current, Action<string> reply)
    {
        Network = network;
        WorldUid = worldUid;
        Actor = actor;
        this.authorized = authorized;
        this.current = current;
        this.reply = reply;
    }

    public bool IsAuthorizedNow
    {
        get
        {
            try { return current() && authorized(); }
            catch { return false; }
        }
    }

    public void Reply(string message)
    {
        try
        {
            // Revocation can still receive a rejection on its original connection. A disconnect,
            // reconnect, new world, or plugin reload can never receive an earlier run's replies.
            if (!current()) return;
            if (message.Length > 2048) message = message.Substring(0, 2048);
            reply("[FreshWorld] " + message);
        }
        catch { /* A lost console/socket must not fail or cancel world maintenance. */ }
    }
}
