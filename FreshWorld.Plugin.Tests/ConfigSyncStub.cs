using BepInEx.Configuration;

namespace FreshWorld.Configuration;

// Controller tests isolate transport. The merged-DLL probe exercises the real ServerSync adapter.
internal sealed class ConfigSynchronization : IDisposable
{
    private readonly ConfigFile file;
    internal ConfigSynchronization(ConfigFile file, FreshWorldConfig settings, HarmonyLib.Harmony harmony,
        Action changed, Action<string> warning) => this.file = file;
    internal void ReloadFile()
    {
        var save = file.SaveOnConfigSet;
        try { file.SaveOnConfigSet = false; file.Reload(); }
        finally { file.SaveOnConfigSet = save; }
    }
    internal void Publish(long target = 0) { }
    public void Dispose() { }
}
