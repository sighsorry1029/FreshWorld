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

The selection harness calls the original `DecideChunkSize` and `AddObjectsPerChunk` methods. It supplies sector counts because the enclosing save method logs through Unity and requires a game process. The Harmony check installs the detour but does not execute a full save. Neither check publicizes or modifies the original DLLs.

These are managed-code checks, not Unity gameplay or a full disk save/reload test. Before release, use a disposable world to reset a fully populated chunk, save, restart, revisit, and repeat. Confirm that vegetation and locations do not stack, protected buildings survive, and a partial chunk containing a building persists normally. Repeat on a local host and a dedicated server. Existing duplicates are not automatically repaired by this patch.
