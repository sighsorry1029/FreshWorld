using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

internal static class NativeSavePatchRegression
{
    internal static void RunPov(string managedDirectory, string pluginPath, string povPath)
    {
        ResolveEventHandler resolve = (_, request) =>
        {
            var name = new AssemblyName(request.Name).Name + ".dll";
            foreach (var folder in new[] { managedDirectory, Path.GetDirectoryName(povPath)! })
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        var harmony = new Harmony("sighsorry.FreshWorld.PoVAzurite");
        try
        {
            var game = Assembly.LoadFrom(Path.Combine(managedDirectory, "assembly_valheim.dll"));
            var plugin = Assembly.LoadFrom(Path.GetFullPath(pluginPath));
            var pov = Assembly.LoadFrom(Path.GetFullPath(povPath));
            var type = plugin.GetType("FreshWorld.Engine.PathOfValheimAzurite", true)!;
            type.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { pov, (Action<string>)System.Console.WriteLine });
            var targets = Harmony.GetAllPatchedMethods().Where(target => Harmony.GetPatchInfo(target).Owners.Contains("sighsorry.FreshWorld.PoVAzurite")).ToArray();
            if (targets.Length != 5 || targets.Count(target => target.DeclaringType?.Assembly == pov) != 3 ||
                targets.Count(target => target.DeclaringType?.Assembly == game) != 2 ||
                targets.Any(target => target.Name == "Struck" || target.Name == "SetKind" || target.Name == "ServerClaim" || target.Name == "ServerClose"))
                throw new Exception("Unexpected original PoV/game patch scope.");
            System.Console.WriteLine("PASS built vegetation adapter installs against original PoV and private game methods; promotion and Blight creation/cleanup remain unpatched.");
        }
        finally
        {
            // This process checks patch construction only; no Unity world or native engine exists.
            // Exercise the adapter's full Dispose lifecycle in the boundary tests instead.
            harmony.UnpatchSelf();
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }

    // Apply the built plugin's actual patch against an original game assembly with installed
    // Harmony. This verifies detour generation/private-field injection, not Unity save execution.
    internal static void Run(string managedDirectory, string pluginPath)
    {
        ResolveEventHandler resolve = (_, request) =>
        {
            var path = Path.Combine(managedDirectory, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        var harmony = new Harmony("FreshWorld.Tests.NativeEmptyChunkSave");
        try
        {
            var game = Assembly.LoadFrom(Path.Combine(managedDirectory, "assembly_valheim.dll"));
            var plugin = Assembly.LoadFrom(Path.GetFullPath(pluginPath));
            var patch = plugin.GetType("FreshWorld.Engine.EmptyChunkSavePatch", true)!;
            RuntimeHelpers.RunClassConstructor(patch.TypeHandle);
            var target = game.GetType("ZDOMan", true)!.GetMethod("GetSaveClonePerChunk",
                BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!;
            var patched = harmony.CreateClassProcessor(patch).Patch();
            if (patched == null || patched.Count != 1 ||
                Harmony.GetPatchInfo(target).Postfixes.Count(item => item.owner == harmony.Id) != 1)
                throw new Exception("The native save patch did not resolve exactly one target and postfix.");
            System.Console.WriteLine("PASS installed Harmony applies the built empty-chunk patch to the original private save method.");
        }
        finally
        {
            harmony.UnpatchSelf();
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }
}
