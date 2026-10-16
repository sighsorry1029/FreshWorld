using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FreshWorld.Engine;

/// <summary>Only one verified empty-chunk strategy may participate in a world save.</summary>
[HarmonyPatch(typeof(ZNet), "SaveWorld", new[] { typeof(bool) })]
internal static class EmptyChunkSaveCompatibility
{
    internal enum Strategy { FreshWorld, UpgradeWorld, Unsupported }

    private const string UpgradeOwner = "upgrade_world";
    // Upgrade World 1.85, SHA256 00E661ECD8CA6546525D61317C9DD4CF6416F5C360BEE9451650004EB4DA6FE4.
    // AssemblyVersion is 1.0.0.0, not the plugin version. Rebuilt/changed implementations need review.
    private static readonly Guid SupportedModule = new("fec95d98-f80f-4911-8929-b0dabb46f894");
    private static readonly (MethodInfo Target, string Type, string Method, bool Prefix)[] Targets =
    {
        (Required(typeof(ZDOMan), "AddObjectsPerChunk", typeof(int), typeof(byte), typeof(int[]),
            typeof(List<Tuple<ZoneSystem.ChunkIndex, List<ZDO>>>).MakeByRefType()), "UpgradeWorld.FindEmptiedChunks", "Prefix", true),
        (Required(typeof(ChunkSaveMapping), nameof(ChunkSaveMapping.Clone)), "UpgradeWorld.DropEmptiedChunks", "Postfix", false),
        (Required(typeof(ZDOMan), nameof(ZDOMan.DeleteOldChunks), typeof(string), typeof(FileHelpers.FileSource)),
            "UpgradeWorld.DeleteEmptiedChunkFiles", "Prefix", true)
    };
    private static string? lastReport;

    private static MethodInfo Required(Type type, string name, params Type[] arguments) =>
        AccessTools.DeclaredMethod(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name);

    // Cache only native method identities. Plugins can patch after FreshWorld.Awake or unpatch later.
    internal static Strategy Inspect(out string detail)
    {
        var found = new List<string>();
        var valid = 0;
        Assembly? implementation = null;
        var sameImplementation = true;
        foreach (var expected in Targets)
        {
            var info = Harmony.GetPatchInfo(expected.Target);
            if (info == null) continue;
            var relevant = 0;
            var matches = 0;
            foreach (var pair in new[] { ("Prefix", info.Prefixes), ("Postfix", info.Postfixes),
                         ("Transpiler", info.Transpilers), ("Finalizer", info.Finalizers) })
            foreach (var patch in pair.Item2)
            {
                var method = patch.PatchMethod;
                var assembly = method.DeclaringType?.Assembly;
                if (patch.owner != UpgradeOwner && assembly?.GetName().Name != "UpgradeWorld" &&
                    method.DeclaringType?.Namespace != "UpgradeWorld") continue;
                relevant++;
                found.Add($"{expected.Target.DeclaringType!.Name}.{expected.Target.Name}: {pair.Item1} {patch.owner}/" +
                    $"{method.DeclaringType?.FullName}.{method.Name} [{assembly?.FullName}; MVID={method.Module.ModuleVersionId}]");
                if (patch.owner == UpgradeOwner && method.DeclaringType?.FullName == expected.Type &&
                    method.Name == expected.Method && method.IsStatic && method.ReturnType == typeof(void) &&
                    pair.Item1 == (expected.Prefix ? "Prefix" : "Postfix") &&
                    assembly?.GetName().Name == "UpgradeWorld" && method.Module.ModuleVersionId == SupportedModule)
                {
                    matches++;
                    if (implementation == null) implementation = assembly;
                    else if (!ReferenceEquals(implementation, assembly)) sameImplementation = false;
                }
            }
            if (relevant == 1 && matches == 1) valid++;
        }
        detail = string.Join("; ", found);
        return found.Count == 0 ? Strategy.FreshWorld : found.Count == Targets.Length && valid == Targets.Length && sameImplementation
            ? Strategy.UpgradeWorld : Strategy.Unsupported;
    }

    internal static Strategy Check()
    {
        Strategy strategy;
        string detail;
        try { strategy = Inspect(out detail); }
        catch (Exception error) { strategy = Strategy.Unsupported; detail = error.GetType().Name + ": " + error.Message; }
        var report = strategy + ":" + detail;
        if (report != lastReport)
        {
            lastReport = report;
            var message = strategy == Strategy.UpgradeWorld
                ? "FreshWorld empty chunk compatibility: UpgradeWorld 1.85 strategy active; FreshWorld empty replacements are disabled."
                : strategy == Strategy.FreshWorld
                    ? "FreshWorld empty chunk compatibility: FreshWorld strategy active."
                    : "FreshWorld empty chunk compatibility: unsupported UpgradeWorld save patches. New world saves are BLOCKED while this state remains active. " +
                      "Stop play and resolve the mod combination before restarting; unsaved progress cannot be persisted safely. " + detail;
            FreshWorldPlugin.LogSaveCompatibility(message, strategy == Strategy.Unsupported);
        }
        return strategy;
    }

    // Includes delayed saves. Runs before WorldSaveStarted, PrepareSave and worker creation.
    // Do not throw from a save worker or change load-error/dirty/mapping state to enforce this guard.
    [HarmonyPrefix]
    internal static bool Prefix(ZNet __instance) => !__instance.IsServer() || Check() != Strategy.Unsupported;
}
