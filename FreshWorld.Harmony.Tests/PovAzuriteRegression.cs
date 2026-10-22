using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using FreshWorld.Backend;
using FreshWorld.Engine;
using HarmonyLib;
using UnityEngine;

internal static class PovAzuriteRegression
{
    internal static int Run()
    {
        var tests = new Action[] { Policy, Definitions, UserDefinitions, Placement, FailureRestoration, ScanCancellation,
            CleanupBoundary, FailedDisposal, MissingPatch, RejectedForeignRewrite };
        foreach (var test in tests)
        {
            ZNet.instance = new ZNet(); ZNet.World = new World(); ZoneSystem.instance = new ZoneSystem();
            ZNetScene.instance = new ZNetScene();
            ZNetScene.instance.Prefabs[PathOfValheimAzurite.Prefab] = new GameObject(PathOfValheimAzurite.Prefab);
            GameWorld.HasPendingMaintenanceCleanup = false; GameWorld.RemainingVein = false;
            PassiveTree.AzuriteVeins.Reset(); PassiveTree.BlightMist.Count = 0;
            EwdFixture.Prefabs = new();
            test();
            Require(MaintenanceGate.IsAvailable, "test leaked lease");
            System.Console.WriteLine("PASS installed Harmony PoV " + test.Method.Name);
        }
        return tests.Length;
    }

    private static PathOfValheimAzurite Install(Action<string>? warn = null) =>
        PathOfValheimAzurite.Install(typeof(PassiveTree.AzuriteVeins).Assembly, warn ?? (_ => { }));

    private static void Policy()
    {
        using var adapter = Install(); PassiveTree.AzuriteVeins.Poll();
        Require(PassiveTree.AzuriteVeins.Seeded == 1 && PassiveTree.AzuriteVeins.HasScan, "off changed PoV policy");
        adapter.Configure(true); PassiveTree.AzuriteVeins.Poll(); PassiveTree.BlightMist.Poll();
        Require(PassiveTree.AzuriteVeins.Seeded == 1 && PassiveTree.AzuriteVeins.Disposed == 1 &&
            !PassiveTree.AzuriteVeins.HasScan && PassiveTree.BlightMist.Count == 1, "natural/event ownership changed incorrectly");
        adapter.Configure(false); PassiveTree.AzuriteVeins.Poll();
        Require(PassiveTree.AzuriteVeins.Seeded == 2 && PassiveTree.AzuriteVeins.HasScan, "off did not restore native paths");
        adapter.Configure(true); ZNet.instance!.Server = false; ZoneSystem.instance!.Spawn(new(0, 0));
        Require(ZoneSystem.instance.m_vegetation.Count == 0, "client registry changed");
    }

    private static void Definitions()
    {
        using var adapter = Install(); adapter.Configure(true);
        var zones = ZoneSystem.instance!; var externalDefaults = zones.m_vegetation;
        zones.Spawn(new(0, 0)); zones.Spawn(new(1, 0));
        Require(externalDefaults.Count == 0, "external registry baseline polluted");
        Require(zones.m_vegetation.Count == 2, "defaults duplicated");
        var mountain = zones.m_vegetation[0]; var other = zones.m_vegetation[1];
        Require(mountain.m_min == 4 && mountain.m_max == 6 && mountain.m_minAltitude == 100 &&
            other.m_min == 1 && other.m_max == 1 && other.m_minAltitude == 1.5f, "default distribution");
        Require(zones.m_vegetation.All(e => ReferenceEquals(e.m_prefab, ZNetScene.instance.Prefabs[PathOfValheimAzurite.Prefab])), "prefab replaced");
        adapter.EndSession(); Require(zones.m_vegetation.Count == 0, "world lifetime leaked definitions");
        zones.Spawn(new(0, 0)); Require(zones.m_vegetation.Count == 2, "next world failed to register");
        adapter.Configure(false); Require(zones.m_vegetation.Count == 0, "off retained owned definitions");
        zones.m_vegetation = externalDefaults; zones.Spawn(new(0, 0));
        Require(zones.m_vegetation.Count == 0, "external baseline resurrected defaults while off");
    }

    private static ZoneSystem.ZoneVegetation Entry(string name, bool enabled = true) => new() { m_prefab = new GameObject(name), m_enable = enabled };
    private static void UserDefinitions()
    {
        using var adapter = Install(); adapter.Configure(true);
        var zones = ZoneSystem.instance!; zones.Spawn(new(0, 0));
        var explicitEntry = Entry(PathOfValheimAzurite.Prefab, false);
        zones.m_vegetation = new() { explicitEntry }; // EWD reload replaced the list.
        zones.Spawn(new(0, 0));
        Require(zones.m_vegetation.Count == 1 && ReferenceEquals(zones.m_vegetation[0], explicitEntry), "disabled user definition overridden");
        var mixed = Entry("stone"); zones.m_vegetation = new() { mixed };
        EwdFixture.Prefabs = new() { [mixed] = new() { mixed.m_prefab, new(PathOfValheimAzurite.Prefab) } };
        zones.Spawn(new(0, 0)); Require(zones.m_vegetation.Count == 1 && ReferenceEquals(zones.m_vegetation[0], mixed), "variant ignored");
        GameWorld.RemainingVein = true; zones.Place(new(0, 0));
        Require(zones.Seen.Count == 0 && ReferenceEquals(zones.m_vegetation[0], mixed), "mixed entry duplicate guard/restoration");
        adapter.Configure(false); Require(zones.m_vegetation.Count == 1, "off removed external definition");
    }

    private static void Placement()
    {
        using var adapter = Install(); adapter.Configure(true);
        var zones = ZoneSystem.instance!; var copper = Entry("rock4_copper"); zones.m_vegetation.Add(copper);
        zones.Spawn(new(0, 0)); var registry = zones.m_vegetation;
        GameWorld.RemainingVein = true; zones.Place(new(0, 0));
        Require(zones.Seen.Count == 1 && ReferenceEquals(zones.Seen[0], copper) && ReferenceEquals(zones.m_vegetation, registry), "existing vein duplicated or unrelated vegetation changed");
        GameWorld.RemainingVein = false; zones.Place(new(0, 0)); Require(zones.Seen.Count == 3, "empty/deleted zone did not regenerate");
        zones.m_vegetation = new() { copper }; zones.Place(new(0, 0));
        Require(zones.Seen.Count == 1, "copper-only supplemental reset injected azurite");
        zones.m_vegetation = registry;
    }

    private static void FailureRestoration()
    {
        using var adapter = Install(); adapter.Configure(true); var zones = ZoneSystem.instance!;
        zones.Spawn(new(0, 0)); var registry = zones.m_vegetation; GameWorld.RemainingVein = true;
        zones.Failure = true;
        try { zones.Place(new(0, 0)); throw new Exception("failure was swallowed"); } catch (InvalidOperationException) { }
        Require(ReferenceEquals(zones.m_vegetation, registry), "exception leaked scoped registry");
        zones.Failure = false; GameWorld.RemainingVein = false; zones.Place(new(1, 0));
        Require(zones.Seen.Count == 2, "next zone remained suppressed");
    }

    private static void ScanCancellation()
    {
        using var adapter = Install(); PassiveTree.AzuriteVeins.Poll();
        using (MaintenanceGate.Acquire())
        {
            PassiveTree.AzuriteVeins.OnDispose = PassiveTree.AzuriteVeins.Poll;
            adapter.BeforeMaintenance();
            Require(!PassiveTree.AzuriteVeins.HasScan && PassiveTree.AzuriteVeins.Disposed == 1, "scan not detached before disposal");
            PassiveTree.AzuriteVeins.Poll(); Require(PassiveTree.AzuriteVeins.Grown == 0, "stale scan resumed");
        }
        PassiveTree.AzuriteVeins.OnDispose = null;
        PassiveTree.AzuriteVeins.Poll(); PassiveTree.AzuriteVeins.Poll();
        Require(PassiveTree.AzuriteVeins.Started == 2 && PassiveTree.AzuriteVeins.Grown == 1, "fresh native scan did not resume when mode off");
    }

    private static void CleanupBoundary()
    {
        using var adapter = Install(); PassiveTree.AzuriteVeins.Poll();
        GameWorld.HasPendingMaintenanceCleanup = true; PassiveTree.AzuriteVeins.Poll();
        Require(PassiveTree.AzuriteVeins.Grown == 1, "ordinary deletion starved native scan");
        using (MaintenanceGate.Acquire()) adapter.BeforeMaintenance();
        var seed = PassiveTree.AzuriteVeins.Seeded;
        PassiveTree.AzuriteVeins.Poll(); PassiveTree.BlightMist.Poll();
        Require(!PassiveTree.AzuriteVeins.HasScan && PassiveTree.AzuriteVeins.Seeded == seed && PassiveTree.BlightMist.Count == 0, "deferred cleanup overlapped writes");
        GameWorld.HasPendingMaintenanceCleanup = false; PassiveTree.BlightMist.Poll();
        Require(PassiveTree.BlightMist.Count == 1, "Blight did not resume");
    }

    private static void FailedDisposal()
    {
        var warnings = 0; using var adapter = Install(_ => warnings++); PassiveTree.AzuriteVeins.Poll();
        PassiveTree.AzuriteVeins.ThrowOnDispose = true;
        using (MaintenanceGate.Acquire()) { PassiveTree.AzuriteVeins.Poll(); PassiveTree.AzuriteVeins.Poll(); }
        Require(warnings == 1 && !PassiveTree.AzuriteVeins.HasScan, "cleanup failure escaped/repeated");
        PassiveTree.AzuriteVeins.ThrowOnDispose = false;
    }

    private static void MissingPatch()
    {
        using var adapter = Install();
        new Harmony("pov-test-removal").Unpatch(AccessTools.Method(typeof(PassiveTree.BlightMist), "Veins"), HarmonyPatchType.Prefix, "sighsorry.FreshWorld.PoVAzurite");
        using (MaintenanceGate.Acquire())
        {
            try { adapter.BeforeMaintenance(); throw new Exception("partial guard admitted maintenance"); }
            catch (InvalidOperationException) { }
        }
    }

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> codes) => codes;
    private static void RejectedForeignRewrite()
    {
        var foreign = new Harmony("pov-test-rewrite");
        var target = AccessTools.Method(typeof(PassiveTree.AzuriteVeins), "RespawnTick");
        foreign.Patch(target, transpiler: new HarmonyMethod(AccessTools.Method(typeof(PovAzuriteRegression), nameof(Rewrite))));
        try
        {
            try { using var adapter = Install(); throw new Exception("foreign rewrite accepted"); }
            catch (InvalidOperationException) { }
            Require(Harmony.GetPatchInfo(target).Prefixes.All(p => p.owner != "sighsorry.FreshWorld.PoVAzurite"), "partial install leaked");
        }
        finally { foreign.UnpatchSelf(); }
        using var restored = Install();
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}

namespace PassiveTree
{
    public static class AzuriteVeins
    {
        private static IEnumerator<object>? respawn;
        public static int Started, Grown, Disposed, Seeded;
        public static bool ThrowOnDispose;
        public static Action? OnDispose;
        public static bool HasScan => respawn != null;
        public static void Poll() { ServerTick(); RespawnTick(); }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void ServerTick() { GC.KeepAlive("PT_AzuriteVein"); Seeded++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RespawnTick()
        {
            if (respawn == null) respawn = RespawnWalk();
            if (!respawn.MoveNext()) { respawn.Dispose(); respawn = null; }
        }
        private static IEnumerator<object> RespawnWalk()
        {
            try { Started++; yield return null!; Grown++; yield return null!; }
            finally { Disposed++; OnDispose?.Invoke(); if (ThrowOnDispose) throw new InvalidOperationException("injected native dispose failure"); }
        }
        public static void Reset()
        {
            OnDispose = null; ThrowOnDispose = false;
            respawn?.Dispose(); respawn = null; Started = Grown = Disposed = Seeded = 0;
        }
    }
    public static class BlightMist
    {
        public static int Count;
        public static void Poll() => Veins(new List<ZDO>());
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Veins(List<ZDO> zones) { Count++; }
    }
}

public sealed class ZNetScene
{
    public static ZNetScene instance = new();
    public readonly Dictionary<string, GameObject> Prefabs = new();
    public GameObject? GetPrefab(string name) => Prefabs.TryGetValue(name, out var prefab) ? prefab : null;
}
namespace UnityEngine { public sealed class GameObject(string name) { public string name = name; } }
// Define the optional mod only for fixture runs. Original-DLL probes must not discover fake game types.
internal static class EwdFixture
{
    private static FieldInfo? mapField;
    internal static Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>> Prefabs
    {
        set
        {
            if (mapField == null)
            {
                var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("EwdFixture"), AssemblyBuilderAccess.Run);
                var type = assembly.DefineDynamicModule("Fixture").DefineType("ExpandWorldData.Vegetation.VegetationSpawning", TypeAttributes.Public);
                type.DefineField("Prefabs", typeof(Dictionary<ZoneSystem.ZoneVegetation, List<GameObject>>), FieldAttributes.Public | FieldAttributes.Static);
                mapField = type.CreateType().GetField("Prefabs")!;
            }
            mapField.SetValue(null, value);
        }
    }
}
