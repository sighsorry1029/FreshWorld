# FreshWorld structure review — 2026-09-20

## Scope and baseline

The overall structure is balanced. Two small changes remove unused tracking state and unnecessary reflection. No new layer, interface, cache, or production file is introduced. Reset scope, scheduling, permissions, configuration, and persistence policies are unchanged by this review.

The starting commit was `6f0be3a31fddd75f4b33b507dbb27bf66c7a471f`. The local branch is `master`, tracking `origin/main`; the remote default is `main`, and both tips matched at the start. Work continued in that checkout. Existing uncommitted ServerSync, automatic Piece protection, and PieceBlacklist changes were part of the reviewed working tree, but are not included in the refactoring commits. Their starting and ending SHA-256 values match. Consequently, the tested working-tree DLL also includes those earlier, still uncommitted changes.

The global instructions were read from `C:/Users/blizz/.codex/AGENTS.md`. No additional project or ancestor AGENTS.md was found. Earlier design documents were treated as historical evidence and checked against current code.

### Build and entry points

- `FreshWorld/FreshWorld.csproj` builds the `netstandard2.1` BepInEx plugin `FreshWorld.dll`. `FreshWorldPlugin.Awake`, `Update`, and `OnDestroy` own initialization, polling, and teardown. This is a normal plugin, not a preloader patcher.
- `Properties/AssemblyInfo.cs` takes identity and version from plugin constants: `sighsorry.FreshWorld`, FreshWorld 1.0.6. These were not changed.
- `FreshWorld.Core/*.cs` is compiled into the plugin. The separate Core project supports isolated tests; users do not install a separate Core DLL.
- Original installed Valheim and BepInEx assemblies are compiler references. No publicizer or access-check bypass is used.
- The existing uncommitted `ILRepack.targets` merges the compiler intermediate DLL with `Libs/ServerSync.dll`, internalizes the library, then deploys Debug or packages Release. The pinned ServerSync build is `valheim-1.0.7-r1`, SHA-256 `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`.
- The runtime has 15 cfg entries, immutable per-run options, cfg hot reload, optional Configuration Manager metadata, and optional client-side ServerSync use. State files are owned by `JsonWorldStateStore`; its schema and file naming remain unchanged.
- Root README, cfg example, manifest, packaging scripts, solution test projects, and illustration descriptions were checked for their roles. PNG artwork was not visually re-reviewed. No runtime asset bundle is introduced.

### Game reference evidence

The configured client is Windows x64 Valheim 1.0.15, Steam build 25390630. Its installed `assembly_valheim.dll` matches the preserved original snapshot, SHA-256 `59f53fb55d99d22a33e8ed094eec8d21e9f133543bce92bc3d80dce44033adb1`. Final metadata checks also use the original 1.0.15 dedicated-server snapshot, build 25390671.

The global Valheim index, the relevant storage/network/terrain section of the 1.0.12-to-1.0.14 guide, and the ServerSync integration index were used. Existing extracts were reused. No game materials were recollected, no assemblies were publicized, and no game-version or dependency support range was expanded.

For the public-field change only, the original 1.0.7 client/server snapshots were also checked. `WearNTear.m_randomInitialDamage` is `Public, Static, System.Boolean` in all four 1.0.7/1.0.15 role combinations. This establishes the changed access contract, not complete runtime compatibility across all those versions.

## Structural assessment

| Area | Judgment and reason |
| --- | --- |
| Plugin/controller | Concentrated but cohesive. It owns the host session, scheduler, accepted manual request, runner, and lease. Splitting these would add coordination between the same states. Keep it together. |
| Core scheduling and persistence | Balanced. `FreshWorldScheduler` owns transitions; `JsonWorldStateStore.Lease` owns locking and atomic writes; state models own serialized validity. Preserve publication only after successful persistence. |
| Commands and authority | Balanced. Syntax, authenticated request lifetime, and native registration/transport have separate responsibilities. Local host, dedicated console, and remote administrator policies should stay distinct. |
| Configuration and sync | Balanced. Keys and validation are colocated; presentation and ServerSync are separate compatibility boundaries. The sync adapter owns its RPC scope and event lifetime. Further splitting would increase navigation. |
| Pipeline and tracked operations | Policy sequencing and per-operation retry/load ownership are appropriately separate. Unused result collections and an unused completion callback were the concrete excess. |
| Reset engines and world access | Separate zone/resource/location policies are justified. GameWorld's registry access, deletion, and temporary load ownership share native lifetime constraints. Keep the current boundaries. |
| Native placement and terrain | Useful separation. Placement owns temporary game state; TerrainResetter adapts Unity/ZDO state; TerrainDataCodec provides independently testable serialization. One public field did not need reflection. |
| Empty chunk persistence patch | Keep separate from reset policy. It adjusts the game's save transaction without taking ownership of native mapping or retry state. |

Relevant history supports these boundaries. `8b641bc` moved candidate ownership into zone operations but retained a diagnostic copy. `1a292c1` and `04dc4d1` already simplified tracker state and timeout ownership. `2320dff` confined the RCON policy change to its context selection and tests, and grouped native API adjustments with their test boundaries. `954c784` and `5f49e46` preserve failure-safe teardown and exact shutdown overloads. `6f0be3a` isolated the empty-chunk compatibility fix and original-game regression checks.

## Implemented changes

### 1. Remove unused maintenance tracking state — `f103a26`

Files: `FreshWorld/Backend/TrackedOperations.cs`, `FreshWorld.Backend.Tests/Program.cs`.

`MaintenancePipeline.Execute` consumes selected/changed/skipped results, failure count, and completion status. It does not consume `CandidateZones`, `CompletedZones`, or `FailedZones`. Candidate selection is already owned by `ZoneOperation.ZonesToUpgrade`; outstanding loads are owned by `OperationTracker._pendingLoads`. All production constructors used the default null completion callback.

The removed types/members belong to internal helper classes, have no Harmony or Unity-message entry points, are not serialized or registered as external integration APIs, and have no discovered reflection consumers. This decision was not based solely on a missing direct reference.

The change removes the three diagnostic sets, their writes, and the unused callback argument/field/invocation. It preserves `Failed++`, timeout, yield, release, `OnEnd`, world identity, and cleanup ordering. The candidate restriction test now checks actual changed and retained zones after execution instead of asserting a redundant candidate copy.

Benefit: three fewer HashSet objects per operation, no duplicate candidate/completed-zone storage, and fewer constructor parameters to trace. Runtime improvement has not been measured. Risk: those internal diagnostic collections no longer exist; observable progress messages and failure handling are preserved.

### 2. Use the public placement state field directly — `88c9b8c`

Files: `FreshWorld/Engine/NativePlacement.cs`, `FreshWorld.Generation.Tests/EngineStubs.cs`.

`NativePlacement.Run` used cached FieldInfo to read and restore the public `WearNTear.m_randomInitialDamage` field. Original metadata confirms direct access is valid. The FieldInfo lookup, string contract, and boxed reads/writes were removed; the test boundary now matches the field's real public visibility.

RNG, ghost mode, and random damage are still captured and restored in the same order. Inner placement exceptions still propagate through the same `finally`. Genuine private-member access, including `ZNetView.m_ghostInit`, remains cached and explicit.

Benefit: a smaller native access boundary and compile-time checking of a public contract. There is no measured performance claim. Existing generation success/failure tests cover state restoration, including a previously enabled random-damage flag.

## Contracts deliberately preserved

- Harmony target overloads, priorities, returns, state handoff, and finalizers were not changed. The remote-command patch retains `Priority.First`, nested-call state, and finalizer restoration. Shutdown patches retain the bool overloads. The save patch retains private-field injection on the zero-argument save-selection method.
- Unity callbacks and event cleanup remain unchanged. Watcher callbacks flag work for the main thread; active runs defer configuration capture. Coroutine cancellation still disposes nested work and releases the maintenance lease.
- Config keys, defaults, raw value types, immutable snapshots, state schema, public plugin constants, command identifiers, and sync wire identifiers are unchanged by these commits.
- Remote command and configuration permission checks remain separate from ZDO network ownership. Admission and delayed execution both recheck identity/session/authority. Reply validation still prevents delivery to a replacement connection.
- Single-player/listen-host operation, dedicated-console operation, remote administrator operation, and optional unmodded clients retain their distinct paths.
- Protection and generation cleanup that look similar were not merged. A snapshot-time object check and a pre-deletion check guard different moments. Normal temporary-object cleanup and exception recovery have different failure aggregation rules. Resource and location scopes remain different policies.
- The mandatory pre-run save, generated-zone bookkeeping, native destruction path, player/tombstone protection, temporary load ownership, and empty-chunk persistence fix remain unchanged. No inventory stack, transfer, or item serialization code was changed.

## Per-frame and allocation review

Idle `Update` checks host/world readiness and change flags; scheduling is polled once per second. Accepted manual work revalidates authority each frame intentionally. Reflection member discovery is cached or initialization-only. The controller does not rebuild Configuration Manager UI each frame.

Maintenance still allocates world-ZDO snapshots, operation candidates, temporary-object snapshots, and loaded location-template snapshots where ownership or mutation safety requires them. Base and terrain caches retain their existing world/lifetime checks and invalidation; no new cache was added. Removing tracker diagnostics reduces allocations during maintenance, not idle frame work.

Configuration choice arrays are built during binding. Numeric drawers parse visible input but do not save merely because a row is painted. Network serialization and typed edit validation occur when settings are exchanged. More caching here was not justified without measurement.

## Verification and limits

Baseline command: `dotnet build FreshWorld.sln -c Debug -p:DeployToGame=true`.

- Baseline: zero warnings/errors, final DLL merge and deployment succeeded. All 334 automated checks passed: Core/config 64, Runtime 11, Backend 45, Terrain 10, Harmony/BepInEx 25, World 37, Generation 13, Commands 28, Plugin 36, Save 18, Sync 47.
- After tracking cleanup: Debug build plus Backend 45, Generation 13, Runtime 11, and Plugin 36 passed.
- After placement cleanup: Debug build plus Generation 13, Harmony/BepInEx 25, and final merged-DLL Sync 47 passed.
- Final original metadata verification: 51 native contracts and 168 compiled game-member references passed for each 1.0.15 role. The public field's visibility/type was checked directly in four original 1.0.7/1.0.15 DLLs.
- Installed Debug DLL matches the final build: SHA-256 `c3dba38de2c5d81ab8977476a1601a9001110eaf5f030810e1727387cddfc898`.
- Diff checks and independent read-only reviews passed. Existing dirty files retained their starting hashes. Only previously clean source/test files and this report were committed.

Most behavioral harnesses use source-linked game boundaries. Save tests exercise selected original managed game methods; Harmony tests verify selected installed-library contracts. Sync tests use the merged plugin in an isolated CLR with native/Unity calls replaced for the harness. None is an actual Unity multiplayer session. The optional installed Configuration Manager smoke was skipped because its assembly was not supplied.

No actual game, listen-host, dedicated multiplayer, Steam/PlayFab, full disk world save/reload, Linux, or native Unity mesh test was performed. Generated build output, existing ZIPs, external library internals, and the whole game's resources were excluded from manual implementation review. ServerSync was checked at its integration boundary, not audited in full.

No new confirmed functional defect was found in this review. Remaining execution checks are not reported as defects: verify reset/save/restart without duplicate vegetation or locations; preserve protected builds and player inventories through disconnect/reconnect; cancel during loading/generation; test admin settings, permission revocation, reconnect, and unmodded clients on a dedicated server. Existing documented terrain edits crossing protection boundaries are a separate policy question and were not changed.

## Safe rollout and reversal

The tracking cleanup was changed, built, tested, reviewed, and committed before placement cleanup began. Placement cleanup followed the same sequence. Each implementation commit can be reverted independently. This report is a separate documentation commit.

The final tree intentionally retains the earlier uncommitted work. No release version, changelog, ZIP, dependency, or upload setting was changed, and no push was performed. Review the earlier pending feature changes separately before preparing the next release.
