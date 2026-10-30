using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;

// Opt-in boundary checks against original game metadata and the built, merged plugin.
// No Unity world, loader constructor, plugin Awake, or Jotunn manager initializer is executed.
internal static class LocationFallbackRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static void Run(string managed, string pluginPath, string jotunnPath, bool guardOnly = false)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            foreach (var folder in new[] { managed, Path.GetDirectoryName(jotunnPath)! })
            {
                var path = Path.Combine(folder, new AssemblyName(request.Name).Name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        var game = Assembly.LoadFrom(Path.Combine(managed, "assembly_valheim.dll"));
        var soft = Assembly.LoadFrom(Path.Combine(managed, "SoftReferenceableAssets.dll"));
        var plugin = Assembly.LoadFrom(Path.GetFullPath(pluginPath));
        var jotunn = Assembly.LoadFrom(Path.GetFullPath(jotunnPath));
        var fallback = plugin.GetType("FreshWorld.Engine.LocationMetadataFallback", true)!;
        var proof = fallback.GetMethod("TryGetJotunnResolverFields", All)!;
        var prefix = jotunn.GetType("Jotunn.Managers.AssetManager+Patches", true)!.GetMethod("SwapResolvedAsset", All)!;
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            checks++; System.Console.WriteLine("PASS " + name);
        }
        bool Proof(MethodInfo method) => (bool)proof.Invoke(null, new object?[] { method, null, null })!;
        Check(Proof(prefix), "original Jotunn callback proves an effect-free dictionary-miss path without initializing its manager");
        if (guardOnly) return;
        Check(!Proof(typeof(LocationFallbackRegression).GetMethod(nameof(Noop), All)!), "unrecognized callback is rejected");
        bool Variant(bool changeGuard)
        {
            // Only isolated test copies are changed; the supplied Jotunn and game DLLs stay intact.
            using var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managed);
            resolver.AddSearchDirectory(AppDomain.CurrentDomain.BaseDirectory);
            resolver.AddSearchDirectory(Path.GetDirectoryName(jotunnPath)!);
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(jotunnPath, new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            module.Assembly.Name.Name = "FreshWorld.JotunnGuardFixture." + Guid.NewGuid().ToString("N");
            module.Assembly.Name.Version = new Version(99, 1, 2, 3);
            foreach (var reference in module.AssemblyReferences.Where(reference => reference.Name == "Jotunn"))
            {
                reference.Name = module.Assembly.Name.Name;
                reference.Version = module.Assembly.Name.Version;
            }
            module.Mvid = Guid.NewGuid();
            var manager = module.GetType("Jotunn.Managers.AssetManager");
            var method = manager.NestedTypes.Single(t => t.Name == "Patches").Methods.Single(m => m.Name == "SwapResolvedAsset");
            if (changeGuard)
                method.Body.Instructions[1].OpCode = Mono.Cecil.Cil.OpCodes.Brfalse;
            method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop));
            var getter = manager.Methods.Single(m => m.Name == "get_Instance");
            getter.Body.GetILProcessor().InsertBefore(getter.Body.Instructions[0], Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop));
            var temporary = Path.Combine(Path.GetTempPath(), module.Assembly.Name.Name + ".dll");
            try
            {
                module.Write(temporary);
                // Harmony's member resolution caches are shared by full type names. Test each
                // altered assembly in a fresh process so original/fixture generic types cannot mix.
                var start = new System.Diagnostics.ProcessStartInfo(typeof(LocationFallbackRegression).Assembly.Location,
                    "--location-guard \"" + managed + "\" \"" + Path.GetFullPath(pluginPath) + "\" \"" + temporary + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var process = System.Diagnostics.Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!changeGuard && process.ExitCode != 0) System.Console.WriteLine(output + error);
                return process.ExitCode == 0;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        Check(Variant(false), "unrelated assembly version, MVID and no-op instructions do not reject the same guard");
        Check(!Variant(true), "a changed result guard is rejected without executing it");

        var harmony = new Harmony("FreshWorld.Tests.LocationMetadata");
        harmony.Patch(prefix, prefix: new HarmonyMethod(typeof(LocationFallbackRegression), nameof(Noop)));
        Check(!Proof(prefix), "a patched resolver implementation is rejected");
        harmony.UnpatchSelf();

        var assetType = soft.GetType("SoftReferenceableAssets.AssetID", true)!;
        var asset = Activator.CreateInstance(assetType, new object[] { 1u, 2u, 3u, 4u })!;
        var runtime = soft.GetType("SoftReferenceableAssets.Runtime", true)!;
        var loaderType = soft.GetType("SoftReferenceableAssets.AssetBundleLoader", true)!;
        var slotType = soft.GetType("SoftReferenceableAssets.AssetLoader", true)!;
        var active = runtime.GetField("s_assetLoader", All)!;
        Check(active.IsPrivate && !loaderType.IsPublic && !slotType.IsPublic, "loader inspection uses the original private/internal access boundary");
        var previous = active.GetValue(null);
        var loader = FormatterServices.GetUninitializedObject(loaderType);
        var indicesField = loaderType.GetField("m_assetIDToLoaderIndex", All)!;
        // .NET Framework lacks netstandard2.1 System.HashCode. A fixture comparer keeps
        // the original AssetID/game DLL untouched; hashing is not under test here.
        var comparer = Activator.CreateInstance(typeof(FixtureComparer<>).MakeGenericType(assetType))!;
        var indices = (IDictionary)Activator.CreateInstance(indicesField.FieldType, new[] { comparer })!;
        indices.Add(asset, 0); indicesField.SetValue(loader, indices);
        var slots = Array.CreateInstance(slotType, 1);
        var slot = Activator.CreateInstance(slotType)!;
        slotType.GetField("m_assetID", All)!.SetValue(slot, asset);
        var callbacksField = slotType.GetField("m_loadedCallbacks", All)!;
        var callbacks = (IList)Activator.CreateInstance(callbacksField.FieldType)!;
        callbacksField.SetValue(slot, callbacks); slots.SetValue(slot, 0);
        loaderType.GetField("m_assetLoaders", All)!.SetValue(loader, slots);
        active.SetValue(null, loader);
        var stable = fallback.GetMethod("LoaderHasStableMetadata", All)!;
        bool Stable() => (bool)stable.Invoke(null, new object?[] { asset, null })!;
        try
        {
            Check(Stable(), "original loader state with no callbacks permits serialized-bounds fallback");
            var callbackType = callbacksField.FieldType.GetGenericArguments()[0];
            var signature = callbackType.GetMethod("Invoke")!;
            var callback = new DynamicMethod("MetadataCallback", typeof(void), signature.GetParameters().Select(p => p.ParameterType).ToArray());
            callback.GetILGenerator().Emit(OpCodes.Ret);
            callbacks.Add(callback.CreateDelegate(callbackType));
            Check(!Stable(), "pending direct callbacks refuse fallback even without Harmony hooks");
            callbacks.Clear();
            harmony.Patch(slotType.GetMethod("InvokeCallbacks", All)!, prefix: new HarmonyMethod(typeof(LocationFallbackRegression), nameof(Noop)));
            Check(!Stable(), "an unknown metadata hook refuses fallback");
            harmony.UnpatchSelf();
            indices.Clear(); Check(!Stable(), "missing asset index refuses fallback"); indices.Add(asset, 0);

            var definitionType = game.GetType("ZoneSystem+ZoneLocation", true)!;
            var definition = Activator.CreateInstance(definitionType)!;
            var source = Activator.CreateInstance(definitionType)!;
            var prefabField = definitionType.GetField("m_prefab")!;
            var prefab = Activator.CreateInstance(prefabField.FieldType, new[] { asset })!;
            prefabField.FieldType.GetField("m_name", All)!.SetValue(prefab, "FixtureLocation");
            foreach (var target in new[] { definition, source })
            {
                prefabField.SetValue(target, prefab);
                definitionType.GetField("m_exteriorRadius")!.SetValue(target, 40f);
            }
            var snapshot = Activator.CreateInstance(fallback, All, null, new[] { definition, source }, null)!;
            var current = fallback.GetMethod("IsCurrent", All)!;
            Check((bool)current.Invoke(snapshot, null)!, "original serialized definitions support a run-local bound snapshot without asset loading");
            definitionType.GetField("m_exteriorRadius")!.SetValue(definition, 50f);
            Check(!(bool)current.Invoke(snapshot, null)!, "changing registered radius invalidates the snapshot");
            definitionType.GetField("m_exteriorRadius")!.SetValue(definition, 40f);
            definitionType.GetField("m_interiorRadius")!.SetValue(source, 80f);
            Check(!(bool)current.Invoke(snapshot, null)!, "changing serialized source also invalidates the snapshot");
            definitionType.GetField("m_interiorRadius")!.SetValue(source, 0f);

            // Uninitialized objects supply serialized fields only; no Unity constructor/Awake runs.
            var zones = FormatterServices.GetUninitializedObject(game.GetType("ZoneSystem", true)!);
            IList EmptyField(object owner, string name)
            {
                var field = owner.GetType().GetField(name, All)!;
                var list = (IList)Activator.CreateInstance(field.FieldType)!;
                field.SetValue(owner, list); return list;
            }
            EmptyField(zones, "m_locationLists");
            var altLists = EmptyField(zones, "m_altBiomeLists");
            var altList = FormatterServices.GetUninitializedObject(game.GetType("AltBiomeList", true)!);
            var biomes = EmptyField(altList, "m_alts");
            var biome = Activator.CreateInstance(game.GetType("AltBiome", true)!)!;
            var locations = EmptyField(biome, "m_addLocations");
            locations.Add(source); biomes.Add(biome); altLists.Add(altList);
            var capture = fallback.GetMethod("TryCapture", All)!;
            bool Capture() => (bool)capture.Invoke(null, new object?[] { zones, definition, 40f, null, null })!;
            Check(Capture(), "an AltBiome-only serialized source verifies the original registered bounds");
            locations.Add(definition);
            Check(!Capture(), "duplicate asset definitions remain ambiguous across source entries");
            locations.Remove(definition);
            definitionType.GetField("m_exteriorRadius")!.SetValue(source, 50f);
            Check(!Capture(), "AltBiome source bounds must match the live definition");
            definitionType.GetField("m_exteriorRadius")!.SetValue(source, 40f);
            altLists.Clear();
            var liveAltField = altList.GetType().GetField("m_altBiomes", All)!;
            var oldAlts = liveAltField.GetValue(null);
            try
            {
                var live = (IList)Activator.CreateInstance(liveAltField.FieldType)!; live.Add(biome); liveAltField.SetValue(null, live);
                Check(!Capture(), "runtime AltBiome registry alone cannot prove serialized bounds");
            }
            finally { liveAltField.SetValue(null, oldAlts); }

            var readState = fallback.GetMethod("ReadLoadState", All)!;
            object?[] Read() { var values = new object?[] { definition, null, null }; readState.Invoke(null, values); return values; }
            active.SetValue(null, null);
            var missing = Read();
            Check(missing[1] == null && missing[2] == null && active.GetValue(null) == null, "diagnostics do not initialize a missing loader");
            active.SetValue(null, loader);
            // Native pre-initialization queues can be exercised without Unity asset bindings.
            var queuedField = loaderType.GetField("m_asyncInitCompletedOperations", All)!;
            var queue = (IList)Activator.CreateInstance(queuedField.FieldType)!;
            var descriptor = queuedField.FieldType.GetGenericArguments()[0];
            var operationType = soft.GetType("SoftReferenceableAssets.AsyncAssetOperationType", true)!;
            queue.Add(Activator.CreateInstance(descriptor, new[] { asset, Enum.Parse(operationType, "LoadAsync") }));
            queuedField.SetValue(loader, queue);
            var loading = Read();
            Check(Equals(loading[1], true) && Equals(loading[2], 0f), "original loader reports a queued request and zero progress without creating or resolving an asset");
            harmony.Patch(loaderType.GetMethod("GetProgress", All)!, prefix: new HarmonyMethod(typeof(LocationFallbackRegression), nameof(FailDiagnostics)));
            var unavailable = Read();
            Check(Equals(unavailable[1], true) && unavailable[2] == null, "a progress getter failure preserves IsLoading and does not escape diagnostics");
            harmony.Patch(loaderType.GetMethod("IsLoading", All)!, prefix: new HarmonyMethod(typeof(LocationFallbackRegression), nameof(FailDiagnostics)));
            unavailable = Read();
            Check(unavailable[1] == null && unavailable[2] == null, "unavailable loader state becomes unknown without throwing");
        }
        finally { harmony.UnpatchSelf(); active.SetValue(null, previous); }
        System.Console.WriteLine($"All {checks} original-metadata fallback boundary checks passed; no Unity world executed.");
    }
    private static void Noop() { }
    private static void FailDiagnostics() => throw new InvalidOperationException("injected diagnostic failure");
    private sealed class FixtureComparer<T> : System.Collections.Generic.IEqualityComparer<T>
    {
        public bool Equals(T x, T y) => object.Equals(x, y);
        public int GetHashCode(T value) => 0;
    }
}
