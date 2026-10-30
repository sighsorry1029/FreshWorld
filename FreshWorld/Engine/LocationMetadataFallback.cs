using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Run-local proof that a timed-out reference already has serialized, non-lazy bounds.</summary>
internal sealed class LocationMetadataFallback
{
    private readonly ZoneSystem.ZoneLocation _definition, _source;
    private readonly AssetID _asset;
    private readonly string _name;
    private readonly float _exterior, _interior;

    private LocationMetadataFallback(ZoneSystem.ZoneLocation definition, ZoneSystem.ZoneLocation source)
    {
        _definition = definition; _source = source; _asset = definition.m_prefab.m_assetID;
        _name = definition.m_prefab.Name; _exterior = definition.m_exteriorRadius; _interior = definition.m_interiorRadius;
    }

    internal bool IsCurrent() => Matches(_definition) && Matches(_source);
    internal bool Revalidate() => IsCurrent() && LoaderHasStableMetadata(_asset, out _);
    private bool Matches(ZoneSystem.ZoneLocation definition) => definition.m_prefab.m_assetID.Equals(_asset) &&
        definition.m_prefab.Name == _name && definition.m_exteriorRadius == _exterior && definition.m_interiorRadius == _interior;

    internal static bool TryCapture(ZoneSystem zones, ZoneSystem.ZoneLocation definition, float beforeLoad,
        out LocationMetadataFallback? bounds, out string reason)
    {
        bounds = null;
        reason = "no matching serialized location bounds";
        try
        {
            if (!definition.m_prefab.IsValid || !FinitePositive(beforeLoad) || definition.m_exteriorRadius != beforeLoad ||
                !FiniteNonnegative(definition.m_interiorRadius)) return false;
            ZoneSystem.ZoneLocation? source = null;
            foreach (var candidate in SerializedLocations(zones))
            {
                if (candidate == null || !candidate.m_prefab.m_assetID.Equals(definition.m_prefab.m_assetID)) continue;
                if (source != null) { reason = "ambiguous serialized location definition"; return false; }
                source = candidate;
            }
            if (source == null) return false;
            var captured = new LocationMetadataFallback(definition, source);
            if (!captured.IsCurrent()) return false;
            if (!LoaderHasStableMetadata(definition.m_prefab.m_assetID, out reason)) return false;
            bounds = captured;
            return true;
        }
        catch (Exception error)
        {
            reason = "bounds verification unavailable: " + error.GetType().Name;
            return false;
        }
    }

    private static bool FinitePositive(float value) => value > 0 && !float.IsInfinity(value);
    private static bool FiniteNonnegative(float value) => value >= 0 && !float.IsInfinity(value);

    private static IEnumerable<ZoneSystem.ZoneLocation> SerializedLocations(ZoneSystem zones)
    {
        foreach (var prefab in zones.m_locationLists)
        {
            if (prefab == null) continue;
            var list = prefab.GetComponent<LocationList>();
            if (list == null) continue;
            foreach (var location in list.m_locations) yield return location;
        }
        // Inspect the original lists cloned by ZoneSystem.Awake, not static m_altBiomes:
        // the latter contains mutable runtime definitions and cannot prove their own bounds.
        foreach (var list in zones.m_altBiomeLists)
        {
            if (ReferenceEquals(list, null)) continue; // Read managed serialized data, not native object state.
            foreach (var biome in list.m_alts)
                foreach (var location in biome.m_addLocations) yield return location;
        }
    }

    internal static void ReadLoadState(ZoneSystem.ZoneLocation definition, out bool? isLoading, out float? progress)
    {
        isLoading = null;
        progress = null;
        try
        {
            // Reading the existing instance avoids Runtime.Loader's creating getter. Metadata is
            // cached, but the instance is read again for each sample and never retained across worlds.
            if (LoadDiagnostics.ActiveLoader?.GetValue(null) is not IAssetLoader loader) return;
            var asset = definition.m_prefab.m_assetID;
            try { isLoading = loader.IsLoading(asset); }
            catch (Exception) { }
            try
            {
                if (loader.GetType() == LoadDiagnostics.NativeLoader &&
                    LoadDiagnostics.Progress?.Invoke(loader, new object[] { asset }) is float value && value >= 0 && value <= 1)
                    progress = value;
            }
            catch (Exception) { }
        }
        catch (Exception) { } // Optional diagnostics must not change readiness, deadlines or cleanup.
    }

    private static class LoadDiagnostics
    {
        internal static readonly FieldInfo? ActiveLoader = typeof(SoftReferenceableAssets.Runtime)
            .GetField("s_assetLoader", BindingFlags.Static | BindingFlags.NonPublic);
        internal static readonly Type? NativeLoader = typeof(AssetID).Assembly.GetType("SoftReferenceableAssets.AssetBundleLoader");
        internal static readonly MethodInfo? Progress = NativeLoader?.GetMethod("GetProgress", new[] { typeof(AssetID) });
    }

    // Original internal loader types/fields stay behind reflection. This reads existing state;
    // it does not instantiate a loader, resolve an asset or invoke an external callback.
    private static bool LoaderHasStableMetadata(AssetID asset, out string reason)
    {
        reason = "unverified asset loader or pending metadata callback";
        var assembly = typeof(AssetID).Assembly;
        var loaderType = assembly.GetType("SoftReferenceableAssets.AssetBundleLoader", true)!;
        var assetType = assembly.GetType("SoftReferenceableAssets.AssetLoader", true)!;
        var loader = AccessTools.Field(typeof(SoftReferenceableAssets.Runtime), "s_assetLoader")?.GetValue(null);
        if (loader == null || loader.GetType() != loaderType ||
            AccessTools.Field(loaderType, "m_assetIDToLoaderIndex")?.GetValue(loader) is not Dictionary<AssetID, int> indices ||
            !indices.TryGetValue(asset, out var index) ||
            AccessTools.Field(loaderType, "m_assetLoaders")?.GetValue(loader) is not Array slots ||
            slots.GetType().GetElementType() != assetType || index < 0 || index >= slots.Length) return false;
        var slot = slots.GetValue(index);
        if (!asset.Equals(AccessTools.Field(assetType, "m_assetID")?.GetValue(slot)) ||
            AccessTools.Field(assetType, "m_loadedCallbacks")?.GetValue(slot) is not ICollection callbacks || callbacks.Count != 0) return false;

        // These paths can finalize a lazy definition. Startup registration and unrelated networking
        // hooks are not runtime metadata resolvers. Unknown resolving hooks keep the old fail-closed policy.
        foreach (var target in Harmony.GetAllPatchedMethods())
        {
            var type = target.DeclaringType;
            var relevant = type == assetType && target.Name != "Release" ||
                type == loaderType && (target.Name == "Get" || target.Name == "LoadAsync" || target.Name == "Load" || target.Name == "GetLoader") ||
                type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SoftReference<>) &&
                (target.Name == "get_Asset" || target.Name == "LoadAsync" || target.Name == "Load");
            if (!relevant) continue;
            var info = Harmony.GetPatchInfo(target);
            if (info == null || info.Owners.Count == 0) continue;
            if (type != assetType || target.Name != "InvokeCallbacks" || info.Postfixes.Count != 0 ||
                info.Transpilers.Count != 0 || info.Finalizers.Count != 0 ||
                info.Prefixes.Any(patch => !JotunnSkipsAsset(patch.PatchMethod, asset)))
            {
                reason = "unverified metadata hook: " + type?.FullName + "." + target.Name;
                return false;
            }
        }
        reason = "matching serialized bounds and no pending metadata resolver";
        return true;
    }

    // Prove only the dictionary-miss path of Jotunn's callback prefix. No DLL/version allowlist:
    // unrelated changes and the guarded replacement body do not affect this absent-asset path.
    internal static bool JotunnSkipsAsset(MethodInfo method, AssetID asset)
    {
        if (!TryGetJotunnResolverFields(method, out var singleton, out var map)) return false;
        var instance = singleton!.GetValue(null);
        return instance != null && map!.GetValue(instance) is IDictionary values && !values.Contains(asset);
    }

    internal static bool TryGetJotunnResolverFields(MethodInfo method, out FieldInfo? singletonField, out FieldInfo? mapField)
    {
        singletonField = mapField = null;
        if (method.DeclaringType?.FullName != "Jotunn.Managers.AssetManager+Patches" || method.Name != "SwapResolvedAsset" ||
            !method.IsStatic || method.ReturnType != typeof(void) || Patched(method) ||
            method.GetMethodBody()?.ExceptionHandlingClauses.Count != 0 ||
            method.CustomAttributes.Concat(method.DeclaringType.CustomAttributes)
                .Any(attribute => attribute.AttributeType.Name == "HarmonyArgument")) return false;
        var parameters = method.GetParameters();
        if (parameters.Length != 2 || parameters[0].Name != "__instance" || parameters[1].Name != "result" ||
            !parameters[0].ParameterType.IsByRef ||
            parameters[0].ParameterType.GetElementType() != typeof(AssetID).Assembly.GetType("SoftReferenceableAssets.AssetLoader") ||
            parameters[1].ParameterType != typeof(LoadResult) || parameters.Any(parameter => parameter.CustomAttributes.Any())) return false;
        var code = Instructions(method);
        if (code.Count < 10 || code[0].opcode != OpCodes.Ldarg_1 || !BranchToReturn(code, 1, true) ||
            code[2].opcode != OpCodes.Call || code[2].operand is not MethodInfo getter ||
            code[3].opcode != OpCodes.Ldfld || code[3].operand is not FieldInfo map ||
            code[4].opcode != OpCodes.Ldarg_0 || code[5].opcode != OpCodes.Ldfld || code[5].operand is not FieldInfo id ||
            (code[6].opcode != OpCodes.Ldloca_S && code[6].opcode != OpCodes.Ldloca) ||
            code[7].opcode != OpCodes.Callvirt || code[7].operand is not MethodInfo lookup || !BranchToReturn(code, 8, false)) return false;
        var manager = method.DeclaringType.DeclaringType!;
        if (getter.DeclaringType != manager || getter.Name != "get_Instance" || !getter.IsStatic || getter.ReturnType != manager ||
            Patched(getter) || getter.GetMethodBody()?.ExceptionHandlingClauses.Count != 0 ||
            map.DeclaringType != manager || map.Name != "assetsToResolve" || map.IsStatic || !map.FieldType.IsGenericType ||
            map.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>) || map.FieldType.GetGenericArguments()[0] != typeof(AssetID) ||
            id.DeclaringType != parameters[0].ParameterType.GetElementType() || id.Name != "m_assetID" || id.FieldType != typeof(AssetID) ||
            lookup != map.FieldType.GetMethod("TryGetValue") || Patched(lookup)) return false;
        var get = Instructions(getter);
        if (get.Count < 4 || get[0].opcode != OpCodes.Ldsfld || get[0].operand is not FieldInfo singleton ||
            singleton.DeclaringType != manager || singleton.FieldType != manager || !singleton.IsStatic ||
            get[1].opcode != OpCodes.Dup || !BranchToReturn(get, 2, true)) return false;
        singletonField = singleton;
        mapField = map;
        return true;
    }

    private static bool Patched(MethodBase method) => Harmony.GetPatchInfo(method)?.Owners.Count > 0;
    private static List<CodeInstruction> Instructions(MethodInfo method)
    {
        // Installed Harmony exposes Framework Reflection.Emit in this signature; avoid binding it
        // to the .NET Standard facade, just as the existing save compatibility verifier does.
        var reader = typeof(PatchProcessor).GetMethods().Single(candidate => candidate.Name == "GetOriginalInstructions" &&
            candidate.GetParameters().Length == 2 && !candidate.GetParameters()[1].ParameterType.IsByRef);
        var code = (List<CodeInstruction>)reader.Invoke(null, new object?[] { method, null })!;
        var labels = AccessTools.Field(typeof(CodeInstruction), "labels");
        for (var i = code.Count - 2; i >= 0; i--)
        {
            if (code[i].opcode != OpCodes.Nop) continue;
            var destination = (IList)labels.GetValue(code[i + 1])!;
            foreach (var label in (IEnumerable)labels.GetValue(code[i])!) destination.Add(label);
            code.RemoveAt(i);
        }
        return code;
    }
    private static bool BranchToReturn(List<CodeInstruction> code, int branch, bool whenTrue)
    {
        var op = code[branch].opcode;
        if (whenTrue ? op != OpCodes.Brtrue && op != OpCodes.Brtrue_S : op != OpCodes.Brfalse && op != OpCodes.Brfalse_S) return false;
        // Label is a Framework type at runtime; compare boxed values instead of casting it.
        var labels = AccessTools.Field(typeof(CodeInstruction), "labels");
        for (var i = 0; i < code.Count; i++)
            if (((IEnumerable)labels.GetValue(code[i])!).Cast<object>().Contains(code[branch].operand))
            {
                while (i < code.Count && code[i].opcode == OpCodes.Nop) i++;
                return i < code.Count && code[i].opcode == OpCodes.Ret;
            }
        return false;
    }
}
