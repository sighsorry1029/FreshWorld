using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;

// Loaded only by the opt-in command: real game/mod types and real installed Harmony,
// in a disposable managed process. No plugin Awake or live world is executed.
internal static class EmptyChunkCompatibilityRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly game = null!, plugin = null!, upgrade = null!;
    private static Type managerType = null!, chunkType = null!, zdoType = null!, mappingType = null!, netType = null!, compatibility = null!;
    private static readonly Harmony Fresh = new("sighsorry.FreshWorld");
    private static readonly Harmony Upgrade = new("upgrade_world");
    private static readonly Harmony Other = new("FreshWorld.Tests.Unrelated");
    private static readonly string[] UpgradeTypes = { "FindEmptiedChunks", "DropEmptiedChunks", "DeleteEmptiedChunkFiles" };
    private static readonly List<string> Messages = new();
    private static int started;

    internal static void Run(string managedDirectory, string pluginPath, string upgradePath)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            foreach (var directory in new[] { managedDirectory, Path.GetDirectoryName(upgradePath)! })
            {
                var path = Path.Combine(directory, new AssemblyName(request.Name).Name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        game = Assembly.LoadFrom(Path.Combine(managedDirectory, "assembly_valheim.dll"));
        plugin = Assembly.LoadFrom(Path.GetFullPath(pluginPath));
        upgrade = Assembly.LoadFrom(Path.GetFullPath(upgradePath));
        managerType = game.GetType("ZDOMan", true)!; chunkType = game.GetType("ZoneSystem+ChunkIndex", true)!;
        zdoType = game.GetType("ZDO", true)!; mappingType = game.GetType("ChunkSaveMapping", true)!;
        netType = game.GetType("ZNet", true)!;
        compatibility = plugin.GetType("FreshWorld.Engine.EmptyChunkSaveCompatibility", true)!;
        var logger = plugin.GetType("FreshWorld.FreshWorldPlugin", true)!.GetMethod("LogSaveCompatibility", All)!;
        Other.Patch(logger, prefix: new HarmonyMethod(typeof(EmptyChunkCompatibilityRegression), nameof(CaptureLog)));
        var fileHelpers = Assembly.LoadFrom(Path.Combine(managedDirectory, "assembly_utils.dll")).GetType("FileHelpers", true)!;
        // No platform/Steam service exists in this process. Exercise only native local-file I/O.
        foreach (var property in new[] { "CloudStorageSupported", "CloudStorageSupportedAndEnabled" })
            Other.Patch(fileHelpers.GetProperty(property, All)!.GetMethod!,
                prefix: new HarmonyMethod(typeof(EmptyChunkCompatibilityRegression), nameof(NoCloud)));
        // File logging uses Unity native calls. Suppress only diagnostics, not selection, mapping or I/O.
        var zlog = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ZLog")).FirstOrDefault(t => t != null);
        if (zlog != null)
            foreach (var method in zlog.GetMethods(All).Where(m => m.Name == "Log" && m.ReturnType == typeof(void)))
                Other.Patch(method, prefix: new HarmonyMethod(typeof(EmptyChunkCompatibilityRegression), nameof(Skip)));
        var checks = 0;
        void Test(string name, Action body)
        {
            SetUpgrade(0);
            Call(compatibility, null, "Reset");
            body(); checks++; System.Console.WriteLine("PASS " + name);
        }
        try
        {
            InstallFresh();
            Test("no UpgradeWorld patches retains FreshWorld replacements and counters", () =>
            {
                var f = new Fixture(); Equal("FreshWorld", Inspect());
                var result = f.Select(); Equal(0, result.Count); f.Patch(result);
                Equal(1, result.Count); Equal(5, f.Counter());
            });
            Test("late UpgradeWorld registration delegates without adding files or counters", () =>
            {
                SetUpgrade(7); Require(Inspect() == "UpgradeWorld", "Contract not recognized: " + InspectDetail());
                var f = new Fixture(); var result = f.Select(); f.Patch(result);
                Equal(0, result.Count); Equal(4, f.Counter());
                SetUpgrade(0); Equal("FreshWorld", Inspect());
            });
            Test("opposite plugin registration order selects the same strategy", () =>
            {
                Fresh.UnpatchSelf(); SetUpgrade(7); InstallFresh(); Equal("UpgradeWorld", Inspect());
            });
            Test("new MVID, version and unrelated metadata changes retain the contract", () =>
                ContractVariant(upgradePath, module =>
                {
                    module.Mvid = Guid.NewGuid();
                    module.Assembly.Name.Version = new Version(77, 8, 9, 0);
                    module.Types.Insert(1, new Mono.Cecil.TypeDefinition("Unrelated", "NewFeature",
                        Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object));
                }, true));
            Test("equivalent branch encodings and no-ops retain the contract", () =>
                ContractVariant(upgradePath, module =>
                {
                    var body = module.Types.Single(type => type.FullName == "UpgradeWorld.ChunkSaveFix").Methods.Single(method => method.Name == "IsEmpty").Body;
                    body.Instructions.Insert(0, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop));
                    foreach (var instruction in body.Instructions)
                        if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Br_S) instruction.OpCode = Mono.Cecil.Cil.OpCodes.Br;
                }, true));
            Test("changed helper logic rejects even when identity is unchanged", () =>
                ContractVariant(upgradePath, module =>
                {
                    var body = module.Types.Single(type => type.FullName == "UpgradeWorld.ChunkSaveFix").Methods.Single(method => method.Name == "IsEmpty").Body;
                    body.Instructions.First(instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_8).OpCode = Mono.Cecil.Cil.OpCodes.Ldc_I4_7;
                }, false));
            Test("changed shared-state field rejects", () =>
                ContractVariant(upgradePath, module => module.Types.Single(type => type.FullName == "UpgradeWorld.ChunkSaveFix")
                    .Fields.Single(field => field.Name == "Dropped").IsInitOnly = false, false));
            Test("thread-local cleanup state rejects", () =>
                ContractVariant(upgradePath, module =>
                {
                    var attribute = new Mono.Cecil.CustomAttribute(module.ImportReference(typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!));
                    module.Types.Single(type => type.FullName == "UpgradeWorld.ChunkSaveFix").Fields.Single().CustomAttributes.Add(attribute);
                }, false));
            Test("changed Harmony argument binding rejects", () =>
                ContractVariant(upgradePath, module => module.Types.Single(type => type.FullName == "UpgradeWorld.FindEmptiedChunks")
                    .Methods.Single(method => method.Name == "Prefix").Parameters[0].Name = "__result", false));
            Test("class-level Harmony argument mapping rejects", () =>
                ContractVariant(upgradePath, module =>
                {
                    var constructor = typeof(HarmonyArgument).GetConstructor(new[] { typeof(string), typeof(string) })!;
                    var attribute = new Mono.Cecil.CustomAttribute(module.ImportReference(constructor));
                    attribute.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(module.TypeSystem.String, "size"));
                    attribute.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(module.TypeSystem.String, "chunkSize"));
                    module.Types.Single(type => type.FullName == "UpgradeWorld.FindEmptiedChunks").CustomAttributes.Add(attribute);
                }, false));
            Test("changed exception cleanup rejects", () =>
                ContractVariant(upgradePath, module => module.Types.Single(type => type.FullName == "UpgradeWorld.DropEmptiedChunks")
                    .Methods.Single(method => method.Name == "Postfix").Body.ExceptionHandlers.Clear(), false));
            Test("runtime detour on an otherwise recognized helper rejects", () =>
            {
                SetUpgrade(7);
                var helper = Method(upgrade.GetType("UpgradeWorld.ChunkSaveFix", true)!, "IsEmpty");
                Other.Patch(helper, prefix: new HarmonyMethod(typeof(EmptyChunkCompatibilityRegression), nameof(Noop)));
                try { Equal("Unsupported", Inspect()); }
                finally { Other.Unpatch(helper, Method(typeof(EmptyChunkCompatibilityRegression), nameof(Noop))); }
            });
            foreach (var mask in new[] { 1, 2, 3, 4, 5, 6 })
            {
                var selected = mask;
                Test("partial set " + selected + " disables FreshWorld changes without vetoing save admission", () =>
                {
                    SetUpgrade(selected); Equal("Unsupported", Inspect());
                    var f = new Fixture(); var result = f.Select(); f.Patch(result); Equal(0, result.Count); Equal(4, f.Counter());
                    Require(compatibility.GetProperty("DisabledReason", All)!.GetValue(null) != null, "Failure was not latched.");
                    ProbeAdmission(f.Net);
                });
            }
            Test("unrelated patches do not change standalone strategy", () =>
            {
                Other.Patch(Method(mappingType, "Clone"), postfix: new HarmonyMethod(typeof(EmptyChunkCompatibilityRegression), nameof(Noop)));
                Equal("FreshWorld", Inspect());
            });
            Test("matching owner without verified patch identity is rejected", () =>
            {
                Upgrade.Patch(Method(mappingType, "Clone"), postfix: new HarmonyMethod(typeof(EmptyChunkCompatibilityRegression), nameof(Noop)));
                Equal("Unsupported", Inspect());
            });
            Test("expected implementation installed by an unexpected owner rejects", () =>
            {
                var type = upgrade.GetType("UpgradeWorld.DropEmptiedChunks", true)!;
                Other.CreateClassProcessor(type).Patch();
                try { Equal("Unsupported", Inspect()); }
                finally { Other.Unpatch(Method(mappingType, "Clone"), Method(type, "Postfix")); }
            });
            Test("duplicate expected patches cannot stand in for a missing target", () =>
            {
                SetUpgrade(3);
                Upgrade.CreateClassProcessor(upgrade.GetType("UpgradeWorld.FindEmptiedChunks", true)!).Patch();
                Equal("Unsupported", Inspect());
            });
            Test("remote clients do not modify results even with unsupported patches", () =>
            {
                SetUpgrade(1); var f = new Fixture(); Set(netType, null, "m_isServer", false);
                var result = f.EmptyList(); f.Patch(result); Equal(0, result.Count); Equal(4, f.Counter());
                ProbeAdmission(f.Net);
            });
            Test("strategy diagnostics log once and again only after a change", () =>
            {
                Set(compatibility, null, "lastReport", null); Messages.Clear();
                Check(); Check(); Equal(1, Messages.Count);
                SetUpgrade(7); Check(); Check(); Equal(2, Messages.Count);
                SetUpgrade(3); Check(); Check(); Equal(3, Messages.Count);
                Require(Messages[2].Contains("NOT been blocked") && Messages[2].Contains("MVID="), "Missing actionable identities.");
                SetUpgrade(7); Check(); Equal(3, Messages.Count);
                Require(compatibility.GetProperty("DisabledReason", All)!.GetValue(null) != null, "Disabled session silently resumed.");
            });
            foreach (var revision in new uint[] { 1, 2, 7 })
            foreach (var strategy in new[] { "FreshWorld", "UpgradeWorld", "Both" })
            {
                var r = revision; var s = strategy;
                Test(s + " revision " + r + " survives two native chunk saves, mapping reloads and backup copies", () =>
                {
                    SetUpgrade(s == "FreshWorld" ? 0 : 7);
                    var f = new Fixture(r); f.RoundTrip(s != "UpgradeWorld");
                });
            }
            Test("old mixed strategy reproduces the revision-one missing-file failure", () =>
            {
                SetUpgrade(7); new Fixture().ReproduceConflict();
            });
            System.Console.WriteLine(checks + " real-DLL empty-chunk compatibility checks passed (isolated Mono/game chunk I/O, not Unity gameplay).");
        }
        catch (Exception error) { System.Console.Error.WriteLine("Compatibility check failure: " + error); throw; }
        finally
        {
            foreach (var harmony in new[] { Fresh, Upgrade, Other })
                try { harmony.UnpatchSelf(); }
                catch (Exception error) { System.Console.Error.WriteLine("Isolated cleanup: " + error.Message); }
        }
    }

    private static void InstallFresh()
    {
        var target = netType.GetMethod("SaveWorld", All, null, new[] { typeof(bool) }, null);
        Require(target != null && target.IsPrivate && target.ReturnType == typeof(void), "Native save admission contract changed.");
        Require(!compatibility.IsDefined(typeof(HarmonyPatch), false), "Compatibility must never patch SaveWorld admission.");
        Fresh.CreateClassProcessor(plugin.GetType("FreshWorld.Engine.EmptyChunkSavePatch", true)!).Patch();
    }
    private static void SetUpgrade(int mask)
    {
        Upgrade.UnpatchSelf();
        for (var i = 0; i < UpgradeTypes.Length; i++)
            if ((mask & (1 << i)) != 0) Upgrade.CreateClassProcessor(upgrade.GetType("UpgradeWorld." + UpgradeTypes[i], true)!).Patch();
    }
    private static string Inspect() => Call(compatibility, null, "Inspect", new object?[] { null })!.ToString()!;
    private static string InspectDetail()
    {
        var args = new object?[] { null };
        Call(compatibility, null, "Inspect", args);
        return (string)args[0]!;
    }
    private static void ContractVariant(string source, Action<Mono.Cecil.ModuleDefinition> mutate, bool expected)
    {
        using var definition = Mono.Cecil.AssemblyDefinition.ReadAssembly(source);
        mutate(definition.MainModule);
        using var output = new MemoryStream();
        definition.Write(output);
        var variant = Assembly.Load(output.ToArray());
        var contract = plugin.GetType("FreshWorld.Engine.UpgradeWorldSaveContract", true)!;
        var args = new object?[] { variant, null };
        bool recognized;
        try { recognized = (bool)Call(contract, null, "Recognizes", args)!; }
        catch (TargetInvocationException error) when (!expected && error.InnerException is InvalidOperationException) { return; }
        Require(recognized == expected, "Unexpected mutated contract result: " + args[1]);
    }
    private static void ProbeAdmission(object net)
    {
        var method = netType.GetMethod("SaveWorld", All, null, new[] { typeof(bool) }, null)!;
        started = 0;
        // Stop at the last prefix: no Unity world/platform services exist in this harness.
        // This checks veto behavior, not save completion; native file I/O is exercised separately.
        Other.Patch(method, prefix: new HarmonyMethod(typeof(EmptyChunkCompatibilityRegression), nameof(ProbeSave)) { priority = Priority.Last });
        try { Call(netType, net, "SaveWorld", false); Equal(1, started); }
        finally { Other.Unpatch(method, Method(typeof(EmptyChunkCompatibilityRegression), nameof(ProbeSave))); }
    }
    private static bool ProbeSave(bool __runOriginal) { if (__runOriginal) started++; return false; }
    private static void Check() => Call(compatibility, null, "Check");
    private static bool CaptureLog(string message, bool error) { Messages.Add(message); return false; }
    private static bool Skip() => false;
    private static bool NoCloud(ref bool __result) { __result = false; return false; }
    private static void Noop() { }
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, All)!;
    private static object? Call(Type type, object? target, string name, params object?[] args) => Method(type, name).Invoke(target, args);
    private static void Set(Type type, object? target, string name, object? value) => type.GetField(name, All)!.SetValue(target, value);
    private static object? Get(object target, string name) => target.GetType().GetField(name, All)!.GetValue(target);
    private static object New(Type type) => FormatterServices.GetUninitializedObject(type);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal(object expected, object? actual) => Require(expected.Equals(actual), $"Expected {expected}; got {actual}.");

    private sealed class Fixture
    {
        internal readonly object Manager = New(managerType), Net = New(netType);
        internal readonly object Mapping = Activator.CreateInstance(mappingType)!;
        internal readonly object SaveData = Activator.CreateInstance(managerType.GetNestedType("SaveData", All)!, true)!;
        private readonly object chunk = Activator.CreateInstance(chunkType, (ushort)0x1010, (byte)0)!;
        private readonly Type objectsType = typeof(List<>).MakeGenericType(zdoType);
        private readonly Array dirty;
        private readonly Array sectors;
        internal Fixture(uint revision = 1)
        {
            Set(netType, null, "m_instance", Net); Set(netType, null, "m_isServer", true);
            Set(netType, Net, "m_zdoMan", Manager);
            var hashSetType = typeof(HashSet<>).MakeGenericType(chunkType);
            dirty = Array.CreateInstance(hashSetType, 2);
            dirty.SetValue(Activator.CreateInstance(hashSetType), 0); dirty.SetValue(Activator.CreateInstance(hashSetType), 1);
            Call(hashSetType, dirty.GetValue(0), "Add", chunk);
            sectors = Array.CreateInstance(objectsType, 512 * 512);
            Set(managerType, Manager, "m_chunkSaveMapping", Mapping); Set(managerType, Manager, "m_saveData", SaveData);
            Set(managerType, Manager, "m_dirtyChunks", dirty); Set(managerType, Manager, "m_objectsBySector", sectors);
            Set(SaveData.GetType(), SaveData, "m_numFiles", 4); Set(SaveData.GetType(), SaveData, "m_dirtyFiles", 4);
            Call(mappingType, Mapping, "CreateOrUpdate", chunk);
            var info = Call(mappingType, Mapping, "Get", chunk)!;
            Set(info.GetType(), info, "m_numZDOs", 123); Set(info.GetType(), info, "m_version", revision);
        }
        internal IList EmptyList() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(Tuple<,>).MakeGenericType(chunkType, objectsType)))!;
        internal IList Select()
        {
            var result = EmptyList();
            for (byte size = 0; size <= 3; size++)
                Call(managerType, Manager, "AddObjectsPerChunk", 64 >> size, size, new int[(64 >> size) * (64 >> size)], result);
            return result;
        }
        internal void Patch(IList result) => Call(plugin.GetType("FreshWorld.Engine.EmptyChunkSavePatch", true)!, null,
            "Postfix", result, Get(Manager, "m_chunkSaveMapping"), dirty, sectors, SaveData);
        internal int Counter() => (int)Get(SaveData, "m_dirtyFiles")!;
        private object Local => Enum.Parse(game.GetType("SaveFileHelper", true)!.Assembly.GetType("FileHelpers+FileSource") ??
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("FileHelpers+FileSource")).First(t => t != null)!, "Local");
        private void Save(string directory, IList selected)
        {
            Set(SaveData.GetType(), SaveData, "m_objectsByChunk", selected);
            Equal(true, Call(managerType, Manager, "SaveChunks", directory, Local));
            Call(managerType, Manager, "DeleteOldChunks", directory, Local);
        }
        internal void RoundTrip(bool applyFresh)
        {
            var directory = Path.Combine(Path.GetTempPath(), "FreshWorld-chunk-test-" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllBytes(directory + Call(mappingType, Mapping, "GetChunkFilename", chunk), new byte[] { 42, 0, 123, 0, 0, 0 });
                for (var pass = 0; pass < 2; pass++)
                {
                    var selected = Select(); if (applyFresh) Patch(selected); Save(directory, selected);
                    VerifyReload(directory, applyFresh && Inspect() == "FreshWorld" ? 1 : 0);
                }
                var backup = Path.Combine(directory, "backup"); Directory.CreateDirectory(backup);
                foreach (var file in Directory.GetFiles(directory)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)));
                VerifyReload(backup, applyFresh && Inspect() == "FreshWorld" ? 1 : 0);
            }
            finally { DeleteFixture(directory); }
        }
        internal void ReproduceConflict()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FreshWorld-conflict-test-" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(directory);
            try
            {
                var selected = Select();
                // This is the old FreshWorld addition, deliberately bypassing the new arbitration.
                selected.Add(Activator.CreateInstance(typeof(Tuple<,>).MakeGenericType(chunkType, objectsType), chunk, Activator.CreateInstance(objectsType)));
                Save(directory, selected);
                var current = Get(Manager, "m_chunkSaveMapping")!;
                Equal(1, Call(mappingType, current, "GetNumChunks"));
                Require(!File.Exists(directory + Call(mappingType, current, "GetChunkFilename", chunk)), "Conflict was not reproduced.");
            }
            finally { DeleteFixture(directory); }
        }
        private static void DeleteFixture(string directory)
        {
            var fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Require(fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullPath).StartsWith("FreshWorld-", StringComparison.Ordinal), "Unexpected fixture cleanup path.");
            Directory.Delete(fullPath, true);
        }
        private void VerifyReload(string directory, int expectedChunks)
        {
            var loaded = Activator.CreateInstance(mappingType)!;
            Call(mappingType, loaded, "Load", directory, Local);
            var chunks = (IDictionary)mappingType.GetProperty("Chunks")!.GetValue(loaded)!;
            Equal(expectedChunks, chunks.Count);
            foreach (DictionaryEntry entry in chunks)
            {
                var path = Path.Combine(directory, (string)Call(mappingType, loaded, "GetChunkFilename", entry.Key)!);
                Require(File.Exists(path), "Saved mapping references a missing file: " + path);
                using var reader = new BinaryReader(File.OpenRead(path));
                Equal((short)42, reader.ReadInt16()); Equal(Get(entry.Value!, "m_numZDOs")!, reader.ReadInt32());
                Equal(6L, reader.BaseStream.Length);
            }
        }
    }
}
