namespace BepInEx.Bootstrap;

public static class Chainloader
{
    public static readonly Dictionary<string, PluginInfo> PluginInfos = new();
    public sealed class PluginInfo { public object Instance = new(); }
}
