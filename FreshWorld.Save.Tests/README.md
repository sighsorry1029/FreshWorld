# Empty chunk save regression checks

This harness compiles the production save patch against original game DLLs. It runs the game's chunk selection and mapping methods in an isolated .NET process, reproduces the omission of empty saved chunks, and checks the corrected save list. It covers all four chunk sizes, remaining objects, neighboring chunks, chunk resizing, portal exclusions, retry state, and host-only execution.

Run against the configured game installation:

```powershell
dotnet run --project FreshWorld.Save.Tests -c Debug
```

Pass another original client or dedicated-server snapshot's Managed directory to repeat the checks without changing the installation:

```powershell
dotnet run --project FreshWorld.Save.Tests -c Debug --no-build -- "<original Managed directory>"
```

After building the plugin and Harmony harness, verify that installed Harmony can apply the built patch to the original private method and inject its private fields:

```powershell
dotnet run --project FreshWorld.Harmony.Tests -c Debug --no-build -- --native-save-patch "<original Managed directory>" "FreshWorld/bin/Debug/netstandard2.1/FreshWorld.dll"
```

The selection harness calls the original `DecideChunkSize` and `AddObjectsPerChunk` methods. It supplies sector counts because the enclosing save method logs through Unity and requires a game process, and fixes the compatibility decision to standalone FreshWorld. Actual patch inspection and save admission are tested by the Mono integration harness below. The native-save Harmony check installs the detour but does not execute a full save. Neither check publicizes or modifies the original DLLs.

These are managed-code checks, not Unity gameplay or a full disk save/reload test. Before release, use a disposable world to reset a fully populated chunk, save, restart, revisit, and repeat. Confirm that vegetation and locations do not stack, protected buildings survive, and a partial chunk containing a building persists normally. Repeat on a local host and a dedicated server. Existing duplicates are not automatically repaired by this patch.

## Upgrade World compatibility checks

The opt-in integration harness loads the built FreshWorld DLL, original game DLLs, installed Harmony, and the supplied Upgrade World 1.85 DLL. Run it in the game's embedded Mono using Windows x64 Python; desktop .NET Framework lacks some of the game's collection APIs.

```powershell
python tools/run-mono-checks.py "<original Managed directory>" "<game>/MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll" "FreshWorld.Harmony.Tests/bin/Debug/net48/FreshWorld.Harmony.Tests.exe" --chunk-compatibility "<original Managed directory>" "FreshWorld/bin/Debug/netstandard2.1/FreshWorld.dll" "<UpgradeWorld.dll>"
```

It applies the actual compatibility and Upgrade World Harmony patches and checks both registration orders, late registration and removal, all six partial sets, unrelated and duplicate patches, client exclusion, and diagnostic deduplication. Blocked saves invoke the patched original `ZNet.SaveWorld(bool)` and must not raise `WorldSaveStarted`, replace preparation state, or create a worker.

For FreshWorld alone, Upgrade World alone, and both together, original `SaveChunks`, `DeleteOldChunks`, and mapping save/load methods process temporary empty-chunk fixtures at revisions 1, 2 and 7. Each fixture is saved twice, its mapping is reloaded, and an ordinary backup copy is checked. Every mapped file must exist with the expected header/count; mapping counts must match the selected strategy. The harness also reproduces the old mixed-strategy revision-one file deletion.

Only logging and cloud-availability queries are stubbed; local chunk and mapping I/O are original game methods. The embedded runtime may report unresolved Unity clock/debug internal calls while JITting methods. The blocked save never reaches the clock, and the successful chunk saves never reach the debug failure branch. This is not a full `ZNet` world startup/shutdown, automatic backup system, Steam/PlayFab/cloud, multiplayer or Unity gameplay test. Nonempty chunks, resizing, portals and retries are covered separately by the original-game selection checks above, not by these empty-chunk disk fixtures. Patch registration changes during an active save are not covered.

Verified Upgrade World 1.85 SHA-256: `00E661ECD8CA6546525D61317C9DD4CF6416F5C360BEE9451650004EB4DA6FE4`; module ID: `fec95d98-f80f-4911-8929-b0dabb46f894`. A changed implementation needs review before it can be treated as compatible.
