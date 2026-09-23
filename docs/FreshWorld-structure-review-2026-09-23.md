# FreshWorld structure review and implementation — September 23, 2026

## Result

The production structure is generally balanced. The plugin and native-world adapter are concentrated, but each owns a coherent lifetime. Splitting them further would distribute state and recovery responsibilities. The worthwhile runtime cleanup was to make `FreshWorldPlugin.TryDispatch` own run-option selection instead of accepting a redundant choice from three callers.

The requested bounty default is a separate feature decision: `EpicLootBountyProtection=false`. Existing explicit values survive binding and reload. Treasure protection remains enabled by default. No other reset, authority, scheduling, save, or compatibility policy was changed.

## Starting point and scope

- Checkout: local `master`, tracking `origin/main`. Both pointed to `7587cd32323ff9f470eea7fa824decb670fade7b` at the start. This is the existing main checkout; no branch or worktree was created.
- The starting tree contained 20 modified files from the immediately preceding, authorized bounty implementation. They were reviewed and committed as a separate feature checkpoint with the requested disabled default. They were not mixed into the structural refactor. No unrelated starting changes were found.
- Instructions: the supplied global rules and `C:/Users/blizz/.codex/AGENTS.md`. No additional project or parent `AGENTS.md` was found.
- Reviewed: all production areas under `FreshWorld` and `FreshWorld.Core`, solution/project/merge/package definitions, config defaults and presentation, root README/CHANGELOG, manifest and resource references, the 11 regression harnesses, and relevant Git changes. Prior structure, ServerSync, and EpicLoot reports were treated as historical evidence and compared with current code.
- Read-only reviewers independently examined runtime/config/commands and Engine/Backend. Only the primary session edited, built, deployed, and committed this checkout.
- Excluded from redesign: `bin`, `obj`, old distribution ZIPs, third-party library implementation, game binaries/resources, and historical investigation reports unrelated to these paths. The pinned library's provenance and merge input were checked; its entire implementation was not re-audited.
- Not exercised: Unity gameplay, live host/dedicated/crossplay sessions, Linux, complete modpack interactions, full prefab/resource behavior, actual Configuration Manager rendering, and large-world performance. Existing images and the package icon were not visually re-audited.

## Effective build and runtime boundaries

`FreshWorld/FreshWorld.csproj` builds a `netstandard2.1` BepInEx plugin, `FreshWorld.dll`. `FreshWorldPlugin` is the loader entry point (`sighsorry.FreshWorld`, author `sighsorry`), not a preloader patcher. The source-linked `FreshWorld.Core/*.cs` is compiled into that DLL; the separate Core project serves tests rather than adding another installed runtime DLL.

`FreshWorldPlugin.ModVersion` remains **1.0.8**. `Properties/AssemblyInfo.cs` uses that constant for assembly/file/informational versions. The final assembly version is `1.0.8.0`. The manifest already depends on `denikson-BepInExPack_Valheim-5.4.2350`; it was left unchanged.

`ILRepack.targets` merges the compiler intermediate DLL with the fixed `FreshWorld/Libs/ServerSync.dll`, internalizing the library. Merge completes before Debug deployment or Release packaging. The pinned input remains `valheim-1.0.7-r1`, SHA-256 `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`; no package or library was upgraded. Game/BepInEx/Harmony reference DLLs are not copied into the mod.

Debug deployment copies only the final merged DLL to Steam Valheim's `BepInEx/plugins/FreshWorld.dll`. The Release target calls `tools/package.ps1`, deriving package version from the DLL and using root README/CHANGELOG, manifest, icon, and DLL. That packaging path was read, not executed. There is no tracked `Thunderstore/README.md` duplicate. Root README remains a solution item.

EpicLoot is optional and is recognized through saved ZDO tags, not a compile-time dependency or a new Harmony patch. Configuration Manager is optional presentation metadata. Upgrade World and Server Devcommands are not runtime requirements. Embedded ServerSync uses `ModRequired=false`: ordinary clients need not install FreshWorld, while a modded administrator client can use its command/config integration.

## Game version and original access

The effective build reference is the installed original Windows x64 Valheim **1.0.15 client**, Steam build **25390630**. `assembly_valheim.dll` SHA-256 is `59f53fb55d99d22a33e8ed094eec8d21e9f133543bce92bc3d80dce44033adb1`, matching the preserved snapshot. Final metadata checks also use the preserved **1.0.15 dedicated-server**, build **25390671**, under:

`C:/Users/blizz/.codex/references/valheim/snapshots/dedicated-server-b25390671-windows-x64-20260918T185703Z-depot-restored/original/valheim_server_Data/Managed`

The global Valheim and ServerSync indexes and relevant existing native extracts were reused. No game collection, full extraction, publicizing, support-range expansion, or dependency migration was performed. These are the verified build/API targets for this task, not a claim that every historical game version or live runtime combination was tested. The old README heading `FreshWorld 1.0.7` was a stale **mod** version, not evidence of the target game version.

Original access restrictions remain explicit: ZoneSystem's generated/loaded/temp registries and placement/poke helpers are private; `ClearArea` is private nested. ZDOMan's object/destruction registries and save-selection methods are private. Current code uses cached AccessTools/reflection and Harmony field injection for these paths. Public `FindSectorObjects` and `WearNTear.m_randomInitialDamage` remain direct calls/access. Compilation against publicized assemblies was not used as runtime evidence.

## Structure by area

| Area and ownership | Judgment and decision |
| --- | --- |
| `FreshWorldPlugin`: host identity, scheduler, pending authenticated request, runner, backend lease | Concentrated but cohesive. `Update` and `HandleCommand` meet at dispatch. Keep ownership together; remove redundant option selection from callers. |
| `FreshWorld.Core`: schedule decisions, persisted state, file lease | Balanced. Scheduler owns transitions; `JsonWorldStateStore.Lease` owns exclusive storage and atomic replacement. Keep validation/copy-before-persist and publish-after-persist ordering. |
| `Commands`: syntax, request identity, native transport | Appropriate separation. Local host, dedicated console/RCON, and remote administrator authority have different evidence and must not be unified by appearance. |
| `Configuration`: lossless bindings, validation/snapshots, presentation, ServerSync adapter | Balanced compatibility boundaries. Keep the file-reload and in-memory-sync flags distinct. A single immutable run snapshot prevents live edits from changing an active operation. |
| `MaintenancePipeline`: pre-save, stage sequence, observed protection, active operation | Appropriate run-level concentration. Keep stage selection and the retained-zone supplement policy together. |
| `TrackedOperations`: retries, frame budget, selection identity, pending load cleanup | Appropriate execution boundary. Different subclasses retain different mutation/recovery policies; no generic reset framework was added. |
| Reset engines and `NativePlacement` | Keep separate. Zone reset removes generated state; vegetation uses exact definitions; locations clear radius/dungeon contents. Native placement restores temporary global/template state around inaccessible game APIs. |
| `GameWorld`: snapshots, destruction, owned temporary roots and deferred releases | Broad but coherent native lifetime boundary. A file split would add cross-file navigation without removing state coupling. |
| Terrain adapter/codec and empty-chunk save patch | Useful separation. Codec validation is independently testable; terrain adapter owns ZDO/Unity access. Save selection integrates with the game's transaction without taking ownership of its mappings or retry policy. |
| EpicLoot protection | Small optional integration. Per-run observed zones and immediate object deletion guards protect different moments; keep both. |

Relevant history supports these decisions. `8b641bc`, `1a292c1`, and `04dc4d1` already centralized candidate/execution/timeout ownership. `f103a26` removed unused tracking collections, and `88c9b8c` replaced unnecessary reflection with the public placement field. `954c784` and `5f49e46` preserve failure-isolated teardown and exact shutdown overloads. `2320dff` isolated native command provenance. `6f0be3a` contains the empty-chunk persistence correction. `7587cd3` introduced the current load-timeout/deferred-release behavior. Repeating those refactors would provide no new benefit.

## Implemented changes and verification rationale

### 1. Optional bounty protection, disabled by default — `41d1fcc`

This separately records the preceding feature work and the current requested default. `FreshWorldConfig.Capture -> RunOptions -> MaintenancePipeline -> OperationParameters -> reset engines -> GameWorld.RemoveZDO` carries both independent EpicLoot switches. `EpicLootProtection` records each matching controller/target/add's own zone for the run. Both switches off avoid the EpicLoot scan. No new UI layer, dependency, deserializer, or game patch was introduced.

The primary risks are confusing treasure and bounty tags, protecting the map-pin zone instead of actual targets, dropping protection on loading retries, or overwriting a saved setting with the new default. Tests cover all four option combinations, leaders/adds across a boundary, movement, late arrivals, recursive children, ownership before deletion, per-run retention, admin edits, hot reload, malformed values, and an explicit true value surviving real BepInEx Bind/Save/Reload. Seven relevant suites passed **303 checks** after the default change. Documentation describes abandoned tags and neighboring terrain limits.

This is a user-requested feature/policy addition, not a behavior-preserving refactor of the released 1.0.8 code.

### 2. One owner for dispatch option selection — `caa44da`

Before: both branches in `FreshWorldPlugin.Update` and the admission path in `HandleCommand` supplied `RunOptions` to private `TryDispatch`. Its manual branch then replaced that argument with `pendingManual.Options`. A maintainer had to inspect three callers plus the callee to understand which snapshot wins.

After: the three callers pass only `ScheduledRun`. `TryDispatch` selects current automatic settings after its existing guards and substitutes the already accepted manual snapshot in the same authenticated branch as before. Gate checks and `StartRun` are unchanged.

Benefit: one policy-selection site and no redundant private parameter, additional file, interface, state, cache, or indirect call. This reduces navigation/change propagation; no measurable performance improvement is claimed.

Risk: accidentally substituting live settings for a queued manual request, or stale manual settings for an automatic run. The existing **37 Plugin checks** passed, including `CapturedServerOptions`, enable/disable preservation, fixed schedule polling, synced edits during a run, dispatch revocation, startup/save/lease gates, and world/session replacement. No implementation-mirroring test was added. `TryDispatch` is neither a Unity message nor a Harmony/reflection/public integration entry point.

### 3. Remove duplicated README metadata and record this review

The README still advertised mod version 1.0.7 after the 1.0.8 release and listed 16 options after the bounty setting made 17. The title now uses only `FreshWorld`, and the introduction describes the three sections without another manually maintained option count. The actual config examples/defaults and required explanations remain.

This removes two demonstrated documentation-update obligations. It does not bump the DLL/manifest version or change packaging. Verification is a source diff against plugin metadata, config bindings, and the existing package input path; a documentation-only edit needs no new game build.

## Preserved contracts and rejected consolidation

- **Harmony:** `Awake` patches only explicitly annotated FreshWorld classes; merged ServerSync patches itself. Shutdown/ShutdownWithoutSave bool overloads, native `InternalCommand(ZRpc,string)` with `Priority.First` and finalizer `__state` restoration, the `GetGroundHeight(Vector3)` prefix, `GetGroundData` postfix, destruction batching, and zero-argument chunk-save postfix are unchanged. Prefix return values, field injection, nested RPC provenance, and exception cleanup remain intact.
- **Unity/session lifetime:** watcher callbacks only set flags; `Update` owns application and runner advancement. Teardown still attempts each event/watcher/command/sync/patch cleanup even when another step fails. World identity, cancellation, and child-to-parent enumerator disposal remain separate from completion reporting.
- **Config/storage/public surface:** GUID, filename, existing keys/defaults, `ConfigChoice` serialized values, strict validation, ServerSync locks/admin checks, scheduler JSON shape, hashed world paths, exclusive locks, and atomic replacement remain. The new optional bounty key and captured property are the explicit additive feature. No legacy migration was added.
- **Authority and ownership:** obtaining ZDO ownership for host mutation is not proof of user authorization. Authenticated remote origin and session are rechecked before admission, dispatch, and continued work. Duplicate requests, malformed input, revoked authority, and reconnect/world replacement retain existing rejection behavior. A persisted manual request cannot resume as an authenticated request after restart.
- **Mutation safety:** player exclusion and EpicLoot deletion checks run before ownership transfer, including spawned-child recursion and cycle guards. Planning snapshots, per-attempt protection, and final deletion checks cannot be deleted as duplicate guards. The code here does not serialize character inventories or profiles; that is not a guarantee against indirect consequences of world-object deletion or external mods.
- **Selection/load identity:** `RegenerateLocations.IsSelected` checks policy; `TrackedRegenerateLocations.IsStillSelected` verifies the originally chosen identity after loading. `_pendingLoads` tracks an operation's attempts; `GameWorld.OwnedLoads` proves actual root ownership. These are different responsibilities.
- **Temporary cleanup:** successful placement cleanup and failure cleanup have different restoration/exception-aggregation requirements. Keep their distinction and the exclusive maintenance lease.
- **ID copies:** tracker and native constructors make some defensive ID copies. Removing them saves only a few per-operation sets and can change eager validation order or introduce aliasing. Keep them until a concrete measured need justifies broader tests.

## Execution costs

Idle `Update` checks world readiness and change flags; automatic scheduling and deferred releases are polled once per second. Pending manual authority is intentionally revalidated every frame. `GetDue` copies a bounded history (up to 128 records) on schedule polling. No benchmark shows this warrants shared mutable state or another cache.

During maintenance, mutable registry/template snapshots support safe iteration and recovery. Player/peer checks run per attempted zone. Base protection performs a world ZDO scan with per-scan prefab classification and the existing ten-second cache; terrain records use world/manager identity and expiry. Run/stage/session cleanup invalidates these caches. Deferred-release snapshots are only needed when entries exist. Native reflection lookup remains cached. Destruction batching normally performs a count check and builds packets only above its threshold.

Config UI drawers operate when displayed; drawing alone does not save settings, and no new UI reconstruction loop was added. Bounty protection adds no idle scan. When enabled together with treasure protection it reuses the initial traversal and per-zone lookups, while adding tag checks. Actual cost depends on world contents; no measured speedup or zero-cost claim is made.

## Verification performed

1. Starting dirty tree: `dotnet build FreshWorld.sln -c Debug -p:DeployToGame=true --nologo` passed with zero warnings/errors. All 11 harnesses passed **370 checks**: Core 65, Runtime 11, Backend 53, Terrain 10, Harmony/BepInEx 27, World 51, Generation 14, Command 28, Plugin 37, Save 18, and merged-DLL Sync 56. This is the baseline for the current request, not a clean-HEAD-only run.
2. Bounty feature/default checkpoint: Debug solution build, final-DLL deployment, and the seven affected suites passed **303 checks**. Original client/server API inspection passed **51 native contracts and 170 compiled game-member references per role**.
3. Dispatch refactor: Debug solution build and deployment passed with zero warnings/errors; all **37 Plugin checks** passed again. The other suites were not unnecessarily repeated after this private call-site change.
4. Final merged DLL: original 1.0.15 client/server metadata checks again passed **51 contracts and 170 references per role**. Reports are in ignored `FreshWorld/bin/Debug/structure-review-20260923/`. The first report-write attempt lacked its output directory; creating that directory and rerunning resolved the tooling error. It was not a game/API failure.
5. Final Debug DLL and installed Steam plugin hashes match: `f24034c0c820471d79aa1a898fa032df478bb41ebf4fc0c0ab40f7491ee19e9c`. Final plugin: `FreshWorld/bin/Debug/netstandard2.1/FreshWorld.dll`.
6. Per-step diff and whitespace checks passed with repository line-ending settings. The optional installed Configuration Manager metadata smoke was skipped because the configured assembly path was unavailable. Source-linked UI metadata tests passed; they do not render the actual UI.

No baseline test failure was found. Builds, metadata inspection, real-library isolated tests, and source-linked stub tests are distinct from actual game execution. This task ran no live game or multiplayer tests.

## Remaining risks and separate decisions

No additional confirmed runtime defect requiring a speculative fix was established in this review. Retained behavior that may need future policy decisions:

- Neighboring terrain/border edits can cross into protected zones. Expanding terrain immunity is a behavior change, not part of this refactor.
- Abandoned EpicLoot targets can retain tags and protect zones when bounty protection is enabled. Expiry or quest-status inference would change policy and require additional evidence.
- Completed world changes remain after cancellation; execution history and world persistence are separate. No forced post-run save, rollback, character recovery, or reward repair was added.
- `tools/build.ps1` defaults to Release. This task deliberately used the explicit Debug/deployment command. Changing that script's default is a separate build-workflow decision; neither it nor packaging was invoked implicitly.

Actual testing still required on a disposable world copy: local-host pause/resume, dedicated console/RCON and remote-admin admission/revocation, unmodded client join, blank-admin initial config sync, active-run cfg edits, world switch/shutdown, late player/bounty arrival, loading timeouts and owned-root release, controller-to-target transition, normal bounty completion/rewards, and reset -> save -> restart/reconnect. Include a target/add spanning zones and an abandoned bounty; confirm that disabled bounty protection uses normal reset policy. Real Unity ownership/replication and modpack behavior cannot be certified by the isolated harnesses.

## Safe modification order and repository result

The executed order was: establish baseline; review and commit the bounty feature/default independently (`41d1fcc`); edit/build/test/review/commit the dispatch cleanup (`caa44da`); then make the documentation-only cleanup and record this review. Each is a separate checkpoint. The dispatch change can be reverted without removing bounty support; the feature commit can be reverted separately when reverting all of its config/tests/docs together. No reset, history rewrite, branch move, or forced operation was used.

Base: `7587cd32323ff9f470eea7fa824decb670fade7b`. Final runtime-code commit: `caa44daa24da91c131df2b3a3d1503e1eded5f42`. The following documentation commit contains this report and the README cleanup. The completion response records its final commit ID and working-tree status. Mod version stays 1.0.8; no Release build, ZIP generation, push, or publication was performed.
