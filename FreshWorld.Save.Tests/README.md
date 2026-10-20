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

The opt-in integration harness loads the built FreshWorld DLL, original game DLLs, installed Harmony, and a supplied Upgrade World DLL. Run it separately with 1.85 and 1.86 in the game's embedded Mono using Windows x64 Python; desktop .NET Framework lacks some of the game's collection APIs.

```powershell
python tools/run-mono-checks.py "<original Managed directory>" "<game>/MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll" "FreshWorld.Harmony.Tests/bin/Debug/net48/FreshWorld.Harmony.Tests.exe" --chunk-compatibility "<original Managed directory>" "FreshWorld/bin/Debug/netstandard2.1/FreshWorld.dll" "<UpgradeWorld.dll>"
```

It applies the actual save and Upgrade World Harmony patches and checks both registration orders, late registration and removal, all six partial sets, unrelated and duplicate patches, client exclusion, and diagnostic deduplication. Unsupported sets latch FreshWorld's disabled state and leave its save list/counters untouched. A final test prefix probes `ZNet.SaveWorld(bool)` admission without running Unity services; FreshWorld must not veto it. This probe does not verify a save worker or save completion.

In-memory Cecil variants verify that a new MVID, assembly version, unrelated metadata, branch encoding or no-op does not invalidate the contract. Changed helper logic, shared-state fields, thread-local state, Harmony argument mappings and exception cleanup must be rejected. Detours on the recognized helper are also rejected. Only the three patch bodies, their empty-chunk helper, its static initializer and shared fields are fingerprinted. Tokens, offsets, build identity and version are excluded. Cached original IL does not authorize a later runtime detour.

`FreshWorld.Plugin.Tests` separately checks delayed operational initialization, disabled manual/automatic admission, authenticated diagnostics, durable cancellation and failure records, coroutine cleanup and draining before patch removal. The merged-DLL sync harness checks login-buffer detection through nested socket wrappers and verifies that config edits cannot restart synchronization after disposal and operational patch removal.

For FreshWorld alone, Upgrade World alone, and both together, original `SaveChunks`, `DeleteOldChunks`, and mapping save/load methods process temporary empty-chunk fixtures at revisions 1, 2 and 7. Each fixture is saved twice, its mapping is reloaded, and an ordinary backup copy is checked. Every mapped file must exist with the expected header/count; mapping counts must match the selected strategy. The harness also reproduces the old mixed-strategy revision-one file deletion.

Only logging and cloud-availability queries are stubbed; local chunk and mapping I/O are original game methods. The embedded runtime may report unresolved Unity clock/debug internal calls while JITting methods. The admission probe never reaches the clock, and the successful chunk saves never reach the debug failure branch. This is not a full `ZNet` world startup/shutdown, automatic backup system, Steam/PlayFab/cloud, multiplayer or Unity gameplay test. Nonempty chunks, resizing, portals and retries are covered separately by the original-game selection checks above, not by these empty-chunk disk fixtures. Patch registration changes during an active save are not covered.

Verified on 2026-10-21: all 34 integration checks passed for each of the four combinations of original Valheim 1.0.20 Windows x64 client/server DLLs and Upgrade World 1.85/1.86. Plugin/controller checks use controlled host doubles and do not establish live ServerManager checkpoint or multiplayer behavior.

| Upgrade World | DLL SHA-256 | Module ID |
| --- | --- | --- |
| 1.85 | `00E661ECD8CA6546525D61317C9DD4CF6416F5C360BEE9451650004EB4DA6FE4` | `fec95d98-f80f-4911-8929-b0dabb46f894` |
| 1.86 | `2605EED18DB4857F05D4DF1F3530C24CCCF1EEB90121EEBFBD5B06A58EB7CD34` | `fb94e0de-09d6-4075-9338-c053bbb639dc` |

These identities record the inspected inputs, not an allowlist. Both produce the same normalized empty-chunk contract: `B1953042E43C44374CC58A6DCF63F56E421EE35E0B1448F76975CB2F528DEB3F`. Original bodies are cached for the loaded assembly's lifetime; active patch registration is rechecked at maintenance admission and chunk selection. Runtime DLL replacement is unsupported. A changed relevant implementation requires review; normalization does not prove equivalence for arbitrary code or changes elsewhere in the game's save pipeline.
