using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

internal static class NativeSavePatchRegression
{
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
