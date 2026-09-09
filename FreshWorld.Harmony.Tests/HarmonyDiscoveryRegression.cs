using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using FreshWorld.Engine;
using HarmonyLib;
using UnityEngine;

internal static class HarmonyDiscoveryRegression
{
    internal static void Run()
    {
        Require(ZNet.instance == null && ZoneSystem.instance == null && ZDOMan.instance == null && WorldGenerator.instance == null,
            "The regression must run before any world singleton exists.");
        var harmonyAssembly = typeof(HarmonyPatch).Assembly;
        Require(harmonyAssembly.GetName().Name == "0Harmony", "A test shim replaced the installed Harmony assembly.");
        var tools = harmonyAssembly.GetType("HarmonyLib.PatchTools", throwOnError: true)!;
        var getLifecycle = tools.GetMethod("GetPatchMethod", BindingFlags.Static | BindingFlags.NonPublic)!;
        var runPreparation = typeof(PatchClassProcessor).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "RunMethod" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 2)
            .MakeGenericMethod(typeof(HarmonyPrepare), typeof(bool));
        using var harmony = new Harmony("freshworld.startup.regression");
        // PatchAll processes unannotated types too. The editing helper container must now be ignored.
        Require(harmony.CreateClassProcessor(typeof(TerrainResetter)).Patch() == null,
            "The terrain-editing helper container is still treated as a Harmony patch class.");
        var patchTypes = typeof(TerrainResetter).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(type => type.IsDefined(typeof(HarmonyPatch), inherit: true)).ToArray();
        Require(patchTypes.Length > 0, "No terrain patch container was discovered.");
        var discovered = new System.Collections.Generic.List<(HarmonyPatchType Kind, HarmonyMethod Info)>();
        foreach (var type in patchTypes)
        {
            var processor = harmony.CreateClassProcessor(type);
            // Run the exact initial Prepare stage used by PatchClassProcessor.Patch, without installing a detour.
            Require((bool)runPreparation.Invoke(processor, new object?[] { true, false, null, Array.Empty<object>() })!,
                "Harmony rejected the terrain patch class during pre-world preparation.");
            // This is Harmony's real attribute-and-reserved-name lookup, including an unannotated Prepare helper.
            foreach (var lifecycle in new[] { typeof(HarmonyPrepare), typeof(HarmonyCleanup), typeof(HarmonyTargetMethod), typeof(HarmonyTargetMethods) })
            {
                var method = (MethodInfo?)getLifecycle.Invoke(null, new object[] { type, lifecycle.FullName! });
                Require(method == null, $"Harmony discovered unexpected startup lifecycle method {method?.DeclaringType?.FullName}.{method?.Name}.");
            }
            var patches = (IEnumerable)typeof(PatchClassProcessor).GetField("patchMethods", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(processor)!;
            foreach (var patch in patches)
            {
                var descriptor = patch.GetType();
                var info = (HarmonyMethod)descriptor.GetField("info", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(patch)!;
                var kind = (HarmonyPatchType)descriptor.GetField("type", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(patch)!;
                discovered.Add((kind, info));
            }
        }
        Require(discovered.Count == 2, "Harmony must discover exactly the two terrain generation hooks.");
        var groundData = discovered.Single(p => p.Kind == HarmonyPatchType.Postfix && p.Info.methodName == nameof(ZoneSystem.GetGroundData));
        var groundHeight = discovered.Single(p => p.Kind == HarmonyPatchType.Prefix && p.Info.methodName == nameof(ZoneSystem.GetGroundHeight));
        Require(groundData.Info.declaringType == typeof(ZoneSystem) && groundHeight.Info.declaringType == typeof(ZoneSystem),
            "Harmony did not merge the intended ZoneSystem target into both hook descriptors.");
        Require(groundHeight.Info.argumentTypes.SequenceEqual(new[] { typeof(Vector3) }), "Ground-height overload discovery changed.");
        // Both real production hooks must also tolerate the pre-world state. No native detour is installed.
        TerrainResetter.Active = false;
        var position = new Vector3(1, 2, 3);
        var dataArguments = new object[] { position };
        groundData.Info.method.Invoke(null, dataArguments);
        Require(((Vector3)dataArguments[0]).y == position.y, "Inactive ground-data hook changed input.");
        var heightArguments = new object[] { position, 42f };
        Require((bool)groundHeight.Info.method.Invoke(null, heightArguments)! && (float)heightArguments[1] == 42f,
            "Inactive ground-height hook bypassed the original.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
