using System.Reflection;

// Minimal game boundary. Authorization and request routing themselves are source-linked unchanged.
public readonly record struct ZDOID(long Value)
{
    public static readonly ZDOID None = new(0);
}

public sealed class Player
{
    public static Player? m_localPlayer;
    public ZDOID Id;
    public ZDOID GetZDOID() => Id;
}

public interface ISocket
{
    bool IsConnected();
    string GetHostName();
}

public sealed class FakeSocket : ISocket
{
    public bool Connected = true;
    public string Identity = "Steam_76561198000000001";
    // These deliberately do not participate in server authorization.
    public bool ClientClaimsAdmin;
    public string ClientClaimsSteamId = "";
    public string Address = "127.0.0.1";
    public bool IsConnected() => Connected;
    public string GetHostName() => Identity;
}

public sealed class ZRpc
{
    public ISocket Socket;
    public ZRpc(ISocket socket) => Socket = socket;
    public ISocket GetSocket() => Socket;
}

public sealed class ZNetPeer
{
    public ZRpc m_rpc;
    public ISocket m_socket;
    public long m_uid = 7;
    public bool m_server;
    public ZNetPeer(ISocket socket) { m_socket = socket; m_rpc = new ZRpc(socket); }
    public bool IsReady() => m_uid != 0;
}

public sealed class ZNet
{
    public static ZNet? instance;
    public static object? World;
    public bool Server = true;
    public bool Dedicated;
    public bool HaveStopped;
    public long WorldUid = 91;
    public ZDOID LocalPlayerCharacterID;
    public readonly List<ZNetPeer> Peers = new();
    public readonly HashSet<string> Admins = new(StringComparer.Ordinal);
    public readonly List<(ZRpc Rpc, string Message)> Messages = new();
    public bool ThrowOnPrint;
    public int AdminChecks;
    public bool IsServer() => Server;
    public bool IsDedicated() => Dedicated;
    public long GetWorldUID() => WorldUid;
    public List<ZNetPeer> GetPeers() => Peers;
    public bool IsAdmin(string identity) { AdminChecks++; return Admins.Contains(identity); }
    public void RemotePrint(ZRpc rpc, string message)
    {
        if (ThrowOnPrint) throw new IOException("Socket closed");
        Messages.Add((rpc, message));
    }
}

public class Terminal
{
    protected static readonly Dictionary<string, ConsoleCommand> commands = new();
    private readonly List<string> m_commandList = new();
    public static Dictionary<string, ConsoleCommand> Catalog => commands;
    public List<string> Cache => m_commandList;
    public readonly List<string> Output = new();
    public void AddString(string message) => Output.Add(message);
    public delegate void ConsoleEvent(ConsoleEventArgs args);
    public delegate List<string> ConsoleOptionsFetcher();
    public sealed class ConsoleEventArgs
    {
        public string FullLine;
        public Terminal Context;
        public ConsoleEventArgs(string line, Terminal context) { FullLine = line; Context = context; }
    }
    public sealed class ConsoleCommand
    {
        public readonly ConsoleEvent Action;
        public readonly bool IsCheat;
        public readonly bool OnlyServer;
        public readonly bool RemoteCommand;
        public readonly bool HideBehindDevCommands;
        private readonly ConsoleOptionsFetcher? fetch;
        public ConsoleCommand(string command, string description, ConsoleEvent action,
            bool isCheat = false, bool isNetwork = false, bool onlyServer = false,
            bool isSecret = false, bool allowInDevBuild = false, bool hideBehindDevCommands = false,
            ConsoleOptionsFetcher? optionsFetcher = null, bool alwaysRefreshTabOptions = false,
            bool remoteCommand = false, bool onlyAdmin = false)
        {
            commands[command] = this;
            Action = action;
            IsCheat = isCheat;
            OnlyServer = onlyServer;
            RemoteCommand = remoteCommand;
            HideBehindDevCommands = hideBehindDevCommands;
            fetch = optionsFetcher;
        }
        public List<string>? GetTabOptions() => fetch?.Invoke();
    }
}

public sealed class Console : Terminal { public static Console? instance; }
public sealed class Chat : Terminal { public static Chat? instance; }

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static FieldInfo? Field(Type type, string name) =>
            type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
    }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public Type TargetType { get; }
        public string TargetMethod { get; }
        public Type[] Arguments { get; }
        public HarmonyPatch(Type type, string method, Type[] arguments)
        { TargetType = type; TargetMethod = method; Arguments = arguments; }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
    public static class Priority { public const int First = 800; }
}
