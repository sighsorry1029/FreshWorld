// Minimal game-shaped command boundary. All patches and command authorization are production code.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public sealed class ZNet
{
    public static ZNet? instance;
    public static World? World;
    public bool HaveStopped;
    public bool Server = true;
    public bool Dedicated;
    public ZDOID LocalPlayerCharacterID;
    public readonly List<ZNetPeer> Peers = new();
    public readonly HashSet<string> Administrators = new(StringComparer.Ordinal);
    public readonly List<string> OriginalCalls = new();
    public readonly List<(ZRpc Rpc, string Message)> Replies = new();
    public Action<ZRpc?, string>? OriginalBody;
    public bool IsServer() => Server;
    public bool IsDedicated() => Dedicated;
    public long GetWorldUID() => World?.m_uid ?? 0;
    public List<ZNetPeer> GetPeers() => Peers;
    public bool IsAdmin(string identity) => Administrators.Contains(identity);
    public void RemotePrint(ZRpc rpc, string message) => Replies.Add((rpc, message));

    // Call the detoured native-shaped private instance method through an ordinary managed call.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void InvokeInternalCommand(ZRpc? rpc, string command) => InternalCommand(rpc, command);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InternalCommand(ZRpc? rpc, string command)
    {
        OriginalCalls.Add(command);
        OriginalBody?.Invoke(rpc, command);
    }
}

public sealed class World { public long m_uid = 17; }
public readonly struct ZDOID(long value) : IEquatable<ZDOID>
{
    private readonly long value = value;
    public static readonly ZDOID None = default;
    public bool Equals(ZDOID other) => value == other.value;
    public override bool Equals(object? other) => other is ZDOID id && Equals(id);
    public override int GetHashCode() => value.GetHashCode();
    public static bool operator ==(ZDOID left, ZDOID right) => left.Equals(right);
    public static bool operator !=(ZDOID left, ZDOID right) => !left.Equals(right);
}
public sealed class Player
{
    public static Player? m_localPlayer;
    public ZDOID Character = new(101);
    public ZDOID GetZDOID() => Character;
}
public sealed class CommandSocket(string identity)
{
    public string Identity = identity;
    public bool Connected = true;
    public string GetHostName() => Identity;
    public bool IsConnected() => Connected;
}
public sealed class ZRpc(CommandSocket socket)
{
    public CommandSocket Socket = socket;
    public CommandSocket GetSocket() => Socket;
}
public sealed class ZNetPeer(ZRpc rpc)
{
    public ZRpc m_rpc = rpc;
    public CommandSocket m_socket = rpc.GetSocket();
    public bool m_server;
    public bool Ready = true;
    public long m_uid = 29;
    public bool IsReady() => Ready;
}

public class Terminal
{
    // Field names and declaring type match the production reflection access.
    private static readonly Dictionary<string, ConsoleCommand> commands = new(StringComparer.Ordinal);
    private readonly List<string> m_commandList = new();
    public readonly List<string> Lines = new();
    public delegate void ConsoleEvent(ConsoleEventArgs args);
    public sealed class ConsoleEventArgs(string line, Terminal context)
    {
        public readonly string FullLine = line;
        public readonly Terminal Context = context;
    }
    public sealed class ConsoleCommand
    {
        internal readonly ConsoleEvent Action;
        public ConsoleCommand(string command, string description, ConsoleEvent action, bool isCheat,
            bool onlyServer, bool hideBehindDevCommands, Func<List<string>> optionsFetcher, bool remoteCommand)
        {
            Action = action;
            commands.Add(command, this);
        }
    }
    public void AddString(string message) => Lines.Add(message);
    public void InvokeFreshWorldCallback(string command) =>
        commands["freshworld"].Action(new ConsoleEventArgs(command, this));
    public int CachedCommandCount => m_commandList.Count;
}
public sealed class Console : Terminal { public static Console? instance; }
public sealed class Chat : Terminal { public static Chat? instance; }
