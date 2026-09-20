# ServerSync integration

Implemented on 2026-09-20. The earlier proposal was deferred; this document now describes the implemented integration. No mod version or release package was changed for this work.

## Distribution

FreshWorld vendors the unchanged `valheim-1.0.7-r1` DLL in `FreshWorld/Libs/ServerSync.dll`. SHA-256: `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`. It was built against original Valheim 1.0.7 assemblies from upstream commit `c57c2aa54e07cdcc7630d6068699ea781622323e`, with fixes for the Everybody constant, administrator API, and initial peer/list message ordering. Provenance and MIT-0 license: `FreshWorld/Libs/README.md`, `third_party/ServerSync.LICENSE.txt`.

ILRepack 2.0.44.1 merges and internalizes it into FreshWorld.dll, always starting from the compiler output in obj. Debug deployment and Release packaging run after merging. The final DLL has no external ServerSync reference. The existing five-file release package does not include a separate ServerSync plugin.

`ModRequired=false` preserves server-only installation. Ordinary clients and administrators using the server console/RCON need not install FreshWorld. Administrators who want in-game commands or remote Configuration Manager editing install the matching FreshWorld version on their client. Configuration Manager is optional and not required on the dedicated server.

## Settings and authority

- All existing 15 settings are registered. Sections, keys, defaults, raw cfg text, choice labels, and validation remain intact. No editable unlock option is added.
- The server supplies initial values. Clients cannot publish before initial sync or while restoring local values on disconnect.
- Administrator edits target only the server peer. The library's client broadcast-to-everyone path is bypassed so unvalidated edits are not forwarded to other clients.
- The server matches the executing ZRpc to a live, ready peer and the routed sender ID, then checks the socket identity through `ZNet.IsAdmin(string)`. Client admin flags and claimed IDs are not trusted.
- A nested RPC scope and finalizer preserve identity even across nested calls or skipped prefixes. Command permissions remain separate and unchanged.
- Remote edits must be partial config packages of at most 128 KiB. Unknown/duplicate keys, null/unexpected types, library control values, client compression/fragments, and trailing bytes are rejected before library decoding. Only registered types reach its reflection-based decoder.
- `FreshWorldConfig.Capture(edits)` validates the complete proposed policy without changing live entries. Rejected known peers receive authoritative values. Accepted edits are saved and redistributed from the server's entries.
- Client cfg files retain local fallback values, including for administrators. Disconnect restores those values.

Large edits can be made in the server cfg. Full server-to-client synchronization retains the library's compression and fragmentation. A settings edit does not itself trigger a reset.

## Reload and lifecycle

The file watcher only signals the Unity main thread. File reload suppresses autosaving and individual broadcasts until the whole file has been read and validated. Valid host reloads publish the resulting settings; invalid host cfg files still disable new maintenance rather than silently choosing defaults.

SettingChanged and source-of-truth events separately queue capture of in-memory settings without forcing a disk reload. Active resets and accepted manual requests retain their captured RunOptions. Deferred settings apply after active work finishes.

FreshWorld's Harmony discovery excludes the merged ServerSync namespace, whose library installs its own patches. Cleanup removes FreshWorld event subscriptions, sync/version registrations, and live config RPC handlers before removing FreshWorld-owned patches. This does not add general support for unloading BepInEx plugins during gameplay.

## Verification

Baseline Debug build and the existing core/config, controller, and installed-Harmony/BepInEx tests passed before integration. Integration checks cover:

- Debug compilation, internalized merge, final DLL deployment, and source/destination hash comparison.
- Existing regression harnesses, plus memory-only config application and deferral through active maintenance.
- The merged-DLL probe: all 15 setting types, malformed edits, missing/spoofed/disconnected RPC contexts, administrator versus ordinary-client edits, initial upload suppression, server-only destinations, file saving/reload, local cfg preservation, disconnect restoration, nested scopes, and cleanup.
- Original client/server metadata, reflected members, and compiled game-member resolution using `tools/verify-game-api.ps1`; original access restrictions are retained.

The merged-DLL probe runs outside Unity. In a memory-only test copy it suppresses the VersionCheck Unity bootstrap and logging, substitutes Unity object checks/coroutines and the native administrator result, skips native game detours, and bridges the .NET Standard string.Split overload for the .NET Framework runner. It uses fake sockets and captures sends. Actual BepInEx bindings, ServerSync serialization/receiving, config persistence, and the adapter's session/validation logic execute. Original game DLLs are not rewritten or publicized. These are isolated tests, not actual Steam/PlayFab or game-runtime results.

After a successful Debug build:

```powershell
dotnet run --project FreshWorld.Sync.Tests -c Debug --no-build -- FreshWorld/bin/Debug/netstandard2.1/FreshWorld.dll
```

## Remaining actual-game checks

Verify local hosting and a dedicated server: unmodded client admission; administrator and ordinary-client Configuration Manager behavior; a fresh admin cfg receiving server values without uploading defaults; direct server cfg reload; edits during maintenance; server restart persistence; disconnect/reconnect/local-world restoration; and coexistence with other embedded ServerSync copies. Steam and PlayFab crossplay need separate execution checks. Build and isolated test success do not establish these results.
