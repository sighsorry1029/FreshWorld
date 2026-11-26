using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Optional host-side placement on a successful transition to a generated zone.</summary>
internal sealed class RunicVeinGeneration : IDisposable
{
    private const string Owner = "sighsorry.FreshWorld.PoVRunicVeins";
    private static readonly float MaxRiseSquared = (float)Math.Pow(4 * Math.Tan(35 * Math.PI / 180), 2);
    private static readonly HashSet<int> OtherDeposits = new(new[] { "MineRock_Tin", "rock4_copper", "rock4_copper_frac",
        "mudpile", "mudpile2", "silvervein", "silvervein_frac", "MineRock_Obsidian", "PT_AzuriteVein" }
        .Select(id => id.GetStableHashCode()));
    private static RunicVeinGeneration? active;
    private readonly Harmony harmony = new(Owner);
    private readonly MethodInfo seed, spawn;
    private readonly FieldInfo definitions, id;
    private readonly Func<Heightmap.Biome, float, object?> pick;
    private readonly Action<string> warn;
    private bool enabled;
    private World? checkedWorld;

    private static bool Host => ZNet.instance != null && ZNet.instance.IsServer() && !ZNet.instance.HaveStopped &&
        ZNet.World != null && !ZNet.World.m_menu;

    // A cfg value alone must never authorize deletion without a working replacement.
    internal static bool Renewing => active?.enabled == true && active.checkedWorld != null &&
        ReferenceEquals(active.checkedWorld, ZNet.World);

    internal static RunicVeinGeneration? TryCreate(Action<string> warn)
    {
        if (!Chainloader.PluginInfos.TryGetValue("treextr.pathofvalheiman", out var plugin)) return null;
        var assembly = plugin.Instance.GetType().Assembly;
        if (assembly.GetType("PassiveTree.RunicCraft.Veins") == null) return null;
        try { return Install(assembly, warn); }
        catch (Exception error)
        {
            warn("PoV runic vein renewal is unavailable. FreshWorld will keep natural deposits. " + error.Message);
            return null;
        }
    }

    internal static RunicVeinGeneration Install(Assembly assembly, Action<string> warn)
    {
        if (active != null) throw new InvalidOperationException("PoV runic vein hooks are already installed.");
        var owner = new RunicVeinGeneration(assembly, warn);
        try
        {
            owner.harmony.Patch(owner.spawn, prefix: Hook(nameof(BeforeZone)), postfix: Hook(nameof(AfterZone)));
            owner.harmony.Patch(owner.seed, prefix: Hook(nameof(AllowNativeSeed)));
            owner.CheckHooks();
            active = owner;
            return owner;
        }
        catch { owner.harmony.UnpatchSelf(); throw; }
    }

    private static HarmonyMethod Hook(string name) => new(typeof(RunicVeinGeneration), name);

    private RunicVeinGeneration(Assembly assembly, Action<string> warn)
    {
        this.warn = warn;
        var type = assembly.GetType("PassiveTree.RunicCraft.Veins", true)!;
        var def = type.GetNestedType("VeinDef") ?? throw new MissingMemberException("PoV VeinDef");
        id = def.GetField("Id") ?? throw new MissingFieldException("PoV vein Id");
        definitions = type.GetField("All") ?? throw new MissingFieldException("PoV vein definitions");
        var choose = AccessTools.DeclaredMethod(type, "Pick", new[] { typeof(Heightmap.Biome), typeof(float) });
        var lay = AccessTools.DeclaredMethod(type, "Lay", new[] { typeof(WorldGenerator), typeof(ZoneSystem), typeof(bool) });
        seed = AccessTools.DeclaredMethod(type, "ServerTick", Type.EmptyTypes);
        spawn = AccessTools.DeclaredMethod(typeof(ZoneSystem), "SpawnZone",
            new[] { typeof(Vector2s), typeof(ZoneSystem.SpawnMode), typeof(GameObject).MakeByRefType() });
        if (choose == null || !choose.IsStatic || choose.ReturnType != def || id.FieldType != typeof(string) ||
            !definitions.IsStatic || definitions.FieldType != typeof(List<>).MakeGenericType(def) ||
            lay == null || !lay.IsStatic || seed == null || !seed.IsStatic || seed.ReturnType != typeof(void) ||
            spawn == null || spawn.ReturnType != typeof(bool))
            throw new NotSupportedException("Runic placement signatures changed.");
        var seedBody = PathOfValheimAzurite.Instructions(seed);
        var layBody = PathOfValheimAzurite.Instructions(lay);
        if (!seedBody.Any(i => i.Calls(lay)) || !seedBody.Any(i => Equals(i.operand, "pov_runic_veins_v1")) ||
            !layBody.Any(i => i.Calls(choose)) || !layBody.Any(i => i.operand is MethodInfo m &&
                m.DeclaringType == typeof(ZDOMan) && m.Name == nameof(ZDOMan.CreateNewZDO)) ||
            !layBody.Any(i => i.operand is FieldInfo f && f.DeclaringType == type && f.Name == "SeededKey"))
            throw new NotSupportedException("PoV no longer uses the expected one-time runic vein placement.");
        pick = (Func<Heightmap.Biome, float, object?>)choose.CreateDelegate(typeof(Func<Heightmap.Biome, float, object>));
    }

    private void CheckHooks()
    {
        var seedPatches = Harmony.GetPatchInfo(seed);
        var spawnPatches = Harmony.GetPatchInfo(spawn);
        bool Has(IEnumerable<Patch>? patches, string name) =>
            patches?.Any(p => p.owner == Owner && p.PatchMethod == AccessTools.Method(typeof(RunicVeinGeneration), name)) == true;
        if (!Has(seedPatches?.Prefixes, nameof(AllowNativeSeed)) || !Has(spawnPatches?.Prefixes, nameof(BeforeZone)) ||
            !Has(spawnPatches?.Postfixes, nameof(AfterZone)) || seedPatches!.Transpilers.Any())
            throw new InvalidOperationException("Runic placement hooks changed.");
    }

    internal void Configure(bool value)
    {
        if (enabled == value) return;
        checkedWorld = null;
        enabled = value;
        if (!value) return;
        try { CheckHooks(); }
        catch (Exception error) { Disable(error); }
    }

    internal void CheckBeforeMaintenance()
    {
        if (!enabled) return;
        try
        {
            CheckHooks();
            if (!Ready(recheck: true)) throw new InvalidOperationException("PoV deposit prefabs are unavailable.");
        }
        catch (Exception error) { Disable(error); }
    }

    internal void EndSession() => checkedWorld = null;

    private bool Ready(bool recheck = false)
    {
        if (!Host || ZNetScene.instance == null || WorldGenerator.instance == null || ZDOMan.instance == null) return false;
        if (!recheck && ReferenceEquals(checkedWorld, ZNet.World)) return true;
        var ids = ((IEnumerable)definitions.GetValue(null)!).Cast<object>().Select(d => (string)id.GetValue(d)!);
        if (!new HashSet<string>(ids, StringComparer.Ordinal).SetEquals(PathOfValheimProtection.RunicPrefabs))
            throw new NotSupportedException("PoV's runic deposit definitions changed.");
        if (PathOfValheimProtection.RunicPrefabs.Any(p => ZNetScene.instance.GetPrefab(p) == null)) return false;
        checkedWorld = ZNet.World;
        return true;
    }

    private static bool AllowNativeSeed() => active?.enabled != true || !Host;

    private static void BeforeZone(Vector2s zoneID, ZoneSystem.SpawnMode mode, out bool __state)
    {
        __state = active?.enabled == true && Host && (mode == ZoneSystem.SpawnMode.Full || mode == ZoneSystem.SpawnMode.Ghost)
            && !GameWorld.IsGenerated(zoneID);
    }

    private static void AfterZone(ZoneSystem __instance, Vector2s zoneID, bool __state, bool __result, bool __runOriginal)
    {
        var owner = active;
        if (!__state || !__result || !__runOriginal || owner?.enabled != true) return;
        try
        {
            if (owner.Ready() && GameWorld.IsGenerated(zoneID)) owner.Place(__instance, zoneID);
        }
        catch (Exception error) { owner.Disable(error); }
    }

    private void Disable(Exception error)
    {
        enabled = false;
        checkedWorld = null;
        warn("Disabled runic vein renewal. Existing natural deposits will be retained. " + error.Message);
    }

    private void Place(ZoneSystem zones, Vector2s zone)
    {
        RunicVeinSites.Visit(ZNet.World.m_seed, zone, (x, z) => WorldGenerator.instance.GetBiome(x, z),
            pick, GroundHeight, (definition, point, yaw, x, z) =>
            {
                if (!Blocked(zones, point, x, z)) Create((string)id.GetValue(definition)!, point, yaw);
            });
    }

    private static float? GroundHeight(float x, float z, Heightmap.Biome biome)
    {
        var gen = WorldGenerator.instance;
        var y = gen.GetHeight(x, z, out var mask);
        if (y < (biome == Heightmap.Biome.Swamp ? 29f : 31.5f) ||
            (biome == Heightmap.Biome.AshLands && mask.a > .4f)) return null;
        var dx = gen.GetHeight(x + 2, z) - gen.GetHeight(x - 2, z);
        var dz = gen.GetHeight(x, z + 2) - gen.GetHeight(x, z - 2);
        // PoV samples the slope across a four-metre baseline and allows up to 35 degrees.
        return dx * dx + dz * dz <= MaxRiseSquared ? y : (float?)null;
    }

    private static bool Blocked(ZoneSystem zones, Vector3 point, int x, int z)
    {
        foreach (var location in zones.GetLocationList())
        {
            var radius = Mathf.Max(8f, location.m_location?.m_exteriorRadius ?? 16f) + 6;
            if (DistanceSquared(location.m_position, point) < radius * radius) return true;
        }

        // Look up saved sectors, including unloaded neighbors. A PoV vein may have settled
        // outside its original 128 m cell. Buildings and ordinary ore have separate clearances.
        var min = ZoneSystem.GetZone(new Vector3(Math.Min(x - RunicVeinSites.SettleDistance, point.x - 40), 0, Math.Min(z - RunicVeinSites.SettleDistance, point.z - 40)));
        var max = ZoneSystem.GetZone(new Vector3(Math.Max(x + RunicVeinSites.Size + RunicVeinSites.SettleDistance, point.x + 40), 0, Math.Max(z + RunicVeinSites.Size + RunicVeinSites.SettleDistance, point.z + 40)));
        for (int sx = min.x; sx <= max.x; ++sx)
        for (int sz = min.y; sz <= max.y; ++sz)
        foreach (var obj in GameWorld.GetZDOs(new Vector2s(sx, sz)))
        {
            var pos = obj.GetPosition();
            var hash = obj.GetPrefab();
            var inCell = RunicVeinSites.CouldBelongToCell(pos, x, z);
            if (((inCell && PathOfValheimProtection.IsRunicPrefab(hash)) ||
                (OtherDeposits.Contains(hash) && DistanceSquared(pos, point) < 400) ||
                (DistanceSquared(pos, point) < 1600 && obj.GetLong(ZDOVars.s_creator) != 0)) &&
                !GameWorld.IsQueuedForDeletion(obj)) return true;
        }
        return false;
    }

    private static float DistanceSquared(Vector3 a, Vector3 b)
    { var x = a.x - b.x; var z = a.z - b.z; return x * x + z * z; }

    private static void Create(string prefab, Vector3 point, float yaw)
    {
        var manager = ZDOMan.instance;
        if (ZNetScene.instance.GetPrefab(prefab) == null)
            throw new InvalidOperationException("Missing registered runic deposit " + prefab);
        int hash = prefab.GetStableHashCode();
        var vein = manager.CreateNewZDO(point, hash) ?? throw new InvalidOperationException("Could not create " + prefab);
        try
        {
            vein.Persistent = true;
            vein.Type = ZDO.ObjectType.Default;
            vein.Distant = false;
            vein.SetPrefab(hash);
            vein.SetRotation(Quaternion.Euler(0, yaw, 0));
            vein.Set(PathOfValheimProtection.RunicSeeded, true);
            manager.SetDirtySector(vein);
        }
        catch { manager.DestroyZDO(vein); throw; }
    }

    public void Dispose()
    {
        enabled = false;
        checkedWorld = null;
        if (ReferenceEquals(active, this)) active = null;
        harmony.UnpatchSelf();
    }
}
