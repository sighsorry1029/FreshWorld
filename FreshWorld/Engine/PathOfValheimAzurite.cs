using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using FreshWorld.Backend;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Optional vegetation ownership of natural azurite. PoV still owns the prefab and Blight.</summary>
internal sealed class PathOfValheimAzurite : IDisposable
{
    internal const string Prefab = "PT_AzuriteVein";
    private static readonly int Vein = Prefab.GetStableHashCode();
    private static readonly MethodInfo InstructionReader = typeof(PatchProcessor).GetMethods()
        .Single(method => method.Name == "GetOriginalInstructions" && method.GetParameters().Length == 2 &&
            !method.GetParameters()[1].ParameterType.IsByRef);
    private static PathOfValheimAzurite? active;
    private readonly Harmony patches = new("sighsorry.FreshWorld.PoVAzurite");
    private readonly FieldInfo scan;
    private readonly FieldInfo? variants;
    private readonly Action<string> warn;
    private readonly List<(MethodInfo Target, MethodInfo Hook, HarmonyPatchType Kind)> installed = new();
    private readonly List<ZoneSystem.ZoneVegetation> defaults = new();
    private ZoneSystem? registry;
    private bool enabled, changing, disposed, warned, awaitingCleanup;

    private static bool Host => ZNet.instance != null && ZNet.instance.IsServer() &&
        !ZNet.instance.HaveStopped && ZNet.World != null;
    private bool Busy
    {
        get
        {
            if (MaintenanceGate.IsActive) return true;
            if (!awaitingCleanup) return false;
            if (GameWorld.HasPendingMaintenanceCleanup) return true;
            awaitingCleanup = false;
            return false;
        }
    }

    internal static PathOfValheimAzurite? TryCreate(Action<string> warn)
    {
        if (!Chainloader.PluginInfos.TryGetValue("treextr.pathofvalheiman", out var plugin)) return null;
        try { return Install(plugin.Instance.GetType().Assembly, warn); }
        catch (Exception error)
        {
            warn("PoV azurite vegetation compatibility is unavailable; native generation was not changed. " + error.Message);
            return null;
        }
    }

    // Validate installed methods, not a mod version/hash/MVID allowlist.
    internal static PathOfValheimAzurite Install(Assembly assembly, Action<string> warn)
    {
        if (active != null) throw new InvalidOperationException("PoV azurite compatibility is already installed.");
        var veins = assembly.GetType("PassiveTree.AzuriteVeins", true)!;
        var mist = assembly.GetType("PassiveTree.BlightMist", true)!;
        var poll = Method(veins, "RespawnTick", typeof(void));
        var seed = Method(veins, "ServerTick", typeof(void));
        var walk = Method(veins, "RespawnWalk", typeof(IEnumerator<object>));
        var replenish = Method(mist, "Veins", typeof(void), typeof(List<ZDO>));
        var scan = veins.GetField("respawn", BindingFlags.Static | BindingFlags.NonPublic);
        if (scan == null || scan.FieldType != typeof(IEnumerator<object>) || scan.IsInitOnly)
            throw new MissingFieldException("PoV azurite scan storage contract changed.");
        var pollBody = Instructions(poll);
        if (!pollBody.Any(code => code.Calls(walk)) ||
            !pollBody.Any(code => code.opcode == OpCodes.Stsfld && Equals(code.operand, scan)) ||
            !Instructions(seed).Any(code => code.opcode == OpCodes.Ldstr && Equals(code.operand, Prefab)))
            throw new NotSupportedException("PoV natural azurite generation contract changed.");
        var variantType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(item => item.GetType("ExpandWorldData.Vegetation.VegetationSpawning"))
            .FirstOrDefault(type => type != null);
        var variants = variantType?.GetField("Prefabs", BindingFlags.Public | BindingFlags.Static);
        if ((variantType != null && variants?.FieldType != typeof(Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>>)) ||
            (variantType == null && AppDomain.CurrentDomain.GetAssemblies().Any(item => item.GetName().Name == "ExpandWorldData")))
            throw new NotSupportedException("Cannot verify Expand World Data vegetation variants.");
        var owner = new PathOfValheimAzurite(scan, variants, warn);
        active = owner;
        try
        {
            owner.Patch(poll, nameof(BeforeRespawn), HarmonyPatchType.Prefix);
            owner.Patch(seed, nameof(BeforeSeed), HarmonyPatchType.Prefix);
            owner.Patch(replenish, nameof(CanReplenishBlight), HarmonyPatchType.Prefix);
            owner.Patch(AccessTools.Method(typeof(ZoneSystem), "SpawnZone"), nameof(BeforeZone), HarmonyPatchType.Prefix);
            var placement = AccessTools.Method(typeof(ZoneSystem), "PlaceVegetation");
            owner.Patch(placement, nameof(BeforeVegetation), HarmonyPatchType.Prefix);
            owner.Patch(placement, nameof(AfterVegetation), HarmonyPatchType.Finalizer);
            owner.RequirePatches();
            return owner;
        }
        catch
        {
            try { owner.patches.UnpatchSelf(); }
            finally { active = null; }
            throw;
        }
    }

    private PathOfValheimAzurite(FieldInfo scan, FieldInfo? variants, Action<string> warn)
    { this.scan = scan; this.variants = variants; this.warn = warn; }

    internal void Configure(bool enable)
    {
        if (disposed || enabled == enable) return;
        if (enable) RequirePatches();
        // Detach a suspended native walk before changing who owns generation.
        CancelScan();
        RemoveDefaults();
        enabled = enable;
    }

    internal void CancelScan()
    {
        if (disposed) return;
        var old = (IEnumerator<object>?)scan.GetValue(null);
        if (old == null) return;
        var wasChanging = changing;
        changing = true;
        try { scan.SetValue(null, null); old.Dispose(); }
        finally { changing = wasChanging; }
    }

    internal void BeforeMaintenance()
    {
        RequirePatches();
        awaitingCleanup = true;
        CancelScan();
        if (ZoneSystem.instance != null) EnsureVegetation(ZoneSystem.instance);
    }

    internal void EndSession()
    { awaitingCleanup = false; RemoveDefaults(); CancelScan(); }

    private void RemoveDefaults()
    {
        if (registry != null && registry.m_vegetation.Any(entry => defaults.Contains(entry)))
            registry.m_vegetation = registry.m_vegetation.Where(entry => !defaults.Contains(entry)).ToList();
        defaults.Clear();
        registry = null;
    }

    private Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>>? Variants() => variants == null ? null :
        variants.GetValue(null) as Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>> ??
        throw new InvalidOperationException("Expand World Data vegetation variants are unavailable.");

    private static bool IsAzurite(ZoneSystem.ZoneVegetation entry,
        Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>>? variants) =>
        (entry.m_prefab != null && entry.m_prefab.name == Prefab) ||
        (variants != null && variants.TryGetValue(entry, out var choices) && choices != null &&
            choices.Any(prefab => prefab != null && prefab.name == Prefab));

    // Only use the normal registry, never the temporary subset used by resource reset.
    internal static void EnsureVegetation(ZoneSystem zones)
    {
        var owner = active;
        if (owner?.enabled != true || !Host) return;
        if (!ReferenceEquals(owner.registry, zones)) { owner.RemoveDefaults(); owner.registry = zones; }
        var variants = owner.Variants();
        if (zones.m_vegetation.Any(entry => !owner.defaults.Contains(entry) && IsAzurite(entry, variants)))
        {
            // Disabled entries and mixed EWD definitions also count as an explicit user choice.
            if (zones.m_vegetation.Any(entry => owner.defaults.Contains(entry)))
                zones.m_vegetation = zones.m_vegetation.Where(entry => !owner.defaults.Contains(entry)).ToList();
            return;
        }
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Prefab) : null;
        if (prefab == null) return; // Retry when the world's prefabs are ready.
        if (owner.defaults.Count == 0 || owner.defaults[0].m_prefab != prefab)
        {
            zones.m_vegetation = zones.m_vegetation.Where(entry => !owner.defaults.Contains(entry)).ToList();
            owner.defaults.Clear();
            owner.defaults.Add(Definition(prefab, Heightmap.Biome.Mountain, 4, 6, 100));
            owner.defaults.Add(Definition(prefab, Heightmap.Biome.BlackForest | Heightmap.Biome.Swamp |
                Heightmap.Biome.Plains | Heightmap.Biome.Mistlands | Heightmap.Biome.AshLands | Heightmap.Biome.DeepNorth, 1, 1, 1.5f));
        }
        if (owner.defaults.Any(entry => !zones.m_vegetation.Contains(entry)))
            zones.m_vegetation = zones.m_vegetation.Concat(owner.defaults.Where(entry => !zones.m_vegetation.Contains(entry))).ToList();
    }

    private static ZoneSystem.ZoneVegetation Definition(GameObject prefab, Heightmap.Biome biome,
        float min, float max, float altitude) => new()
    {
        m_name = "FreshWorld Azurite " + biome, m_prefab = prefab, m_enable = true,
        m_biome = biome, m_min = min, m_max = max, m_minAltitude = altitude,
        m_blockCheck = true, m_forcePlacement = false, m_groupSizeMin = 1, m_groupSizeMax = 1,
        m_scaleMin = 1, m_scaleMax = 1, m_maxTilt = 90
    };

    private static void BeforeZone(ZoneSystem __instance) => EnsureVegetation(__instance);

    private static void BeforeVegetation(ZoneSystem __instance, Vector2s zoneID,
        out List<ZoneSystem.ZoneVegetation>? __state)
    {
        __state = null;
        var owner = active;
        if (owner?.enabled != true || !Host) return;
        var variants = owner.Variants();
        if (!__instance.m_vegetation.Any(entry => IsAzurite(entry, variants)) ||
            !GameWorld.HasRemainingPrefabNearZone(zoneID, Vein, 6)) return;
        // Keep EWD identity-keyed metadata and unrelated vegetation intact, including on exceptions.
        var filtered = __instance.m_vegetation.Where(entry => !IsAzurite(entry, variants)).ToList();
        __state = __instance.m_vegetation;
        __instance.m_vegetation = filtered;
    }

    private static void AfterVegetation(ZoneSystem __instance, List<ZoneSystem.ZoneVegetation>? __state)
    { if (__state != null) __instance.m_vegetation = __state; }

    private static bool BeforeSeed() => active?.changing != true &&
        (!Host || (active?.enabled != true && active?.Busy != true));

    private static bool BeforeRespawn()
    {
        var owner = active;
        if (owner == null) return true;
        if (owner.changing) return false;
        if (!Host || owner.enabled || owner.Busy)
        {
            try { owner.CancelScan(); }
            catch (Exception error)
            {
                if (!owner.warned)
                {
                    owner.warned = true;
                    owner.warn("PoV azurite scan cleanup failed; the detached scan will not resume. " + error.Message);
                }
            }
            return false;
        }
        return true;
    }

    // Pause periodic replenishment across maintenance; keep Blight creation, promotion and cleanup.
    private static bool CanReplenishBlight() => active?.changing != true && (!Host || active?.Busy != true);

    private void Patch(MethodInfo target, string name, HarmonyPatchType kind)
    {
        var hook = AccessTools.Method(typeof(PathOfValheimAzurite), name);
        patches.Patch(target, prefix: kind == HarmonyPatchType.Prefix ? new HarmonyMethod(hook) : null,
            finalizer: kind == HarmonyPatchType.Finalizer ? new HarmonyMethod(hook) : null);
        installed.Add((target, hook, kind));
    }

    private void RequirePatches()
    {
        foreach (var expected in installed)
        {
            var info = Harmony.GetPatchInfo(expected.Target);
            var list = expected.Kind == HarmonyPatchType.Prefix ? info?.Prefixes : info?.Finalizers;
            if (list == null || list.Count(patch => patch.owner == patches.Id && patch.PatchMethod == expected.Hook) != 1 ||
                (expected.Target.DeclaringType != typeof(ZoneSystem) && info!.Transpilers.Any(patch => patch.owner != patches.Id)))
                throw new InvalidOperationException("PoV azurite hooks changed; restart with a compatible mod combination before maintenance.");
        }
    }

    private static MethodInfo Method(Type type, string name, Type result, params Type[] parameters)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null, parameters, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static List<CodeInstruction> Instructions(MethodInfo method) =>
        (List<CodeInstruction>)InstructionReader.Invoke(null, new object?[] { method, null })!;

    public void Dispose()
    {
        if (disposed) return;
        try { RemoveDefaults(); CancelScan(); }
        finally
        {
            patches.UnpatchSelf();
            disposed = true;
            if (ReferenceEquals(active, this)) active = null;
        }
    }
}
