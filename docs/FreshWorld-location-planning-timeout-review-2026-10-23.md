# FreshWorld location planning timeout: mod compatibility review

Date: 2026-10-23

FreshWorld baseline: 1.1.7, commit `c461d1e99f26ed3a655af8d4bf4c2904c6a9e2f9`

Incident: [FreshWorld issue #3](https://github.com/sighsorry1029/FreshWorld/issues/3#issuecomment-6342792125)

## Conclusion

The supplied log establishes that FreshWorld stopped location planning because its **cumulative 180-second limit expired**, while the current prefab had waited only **10.54 seconds**. It does not establish a broken MWL prefab or a conflict that prevents assets from loading. Another MWL prefab had already completed after a similar wait of 10.77 seconds.

The appropriate FreshWorld change is to remove the cumulative limit and retain the independent 30-second timeout for each prefab. This allows a large, slowly loading location registry to finish without ignoring incomplete metadata or weakening reset protection.

The supplied mods contain several sources of additional asset loading and server frame work. They are candidates for controlled comparison, not confirmed causes. The supplied ServersideQoL preloader patcher does not patch the asset loader and does not explain this timeout directly.

This patch removes premature failure caused by the cumulative limit. It does **not** make asset loading faster or prove that the underlying loading slowdown has been resolved.

## Evidence and scope

The investigation used:

- `RampagedReality_FreshWorld-1.1.7_Timeout_2026-10-22.log` from the user's Downloads folder.
- The supplied suspect plugins under the Gale profile `asdfasdf/BepInEx/plugins`.
- `asdfasdf/BepInEx/patchers/ArgusMagnus-ServersideQoL/ServersideQoL.Patchers.dll`.
- The supplied More World Locations AIO package under the Gale profile `adffff`.
- Current FreshWorld source, existing regression harnesses, original game assembly analysis, and decompiled copies of the supplied mod assemblies. Assemblies were not publicized for analysis.

The incident ran on a Windows dedicated server with Valheim 1.0.20, BepInExPack 5.4.2351, FreshWorld 1.1.7, and 43 logged plugins. The world loaded 729,467 ZDOs and contained 14,409 location instances. The supplied plugin versions matched the corresponding log entries; this does not prove that the remote server used identical binaries or settings.

The review focused on location metadata loading, asset callbacks, frame processing, pausing, saving, and competing location reset features. It was not an exhaustive review of every mod feature, bundled library, or possible interaction. Actual server configuration files for all candidate mods, hardware measurements, and a controlled in-game reproduction were unavailable.

## What the log proves

The issue report describes zone and resource resets disabled, location resets enabled for 17 configured IDs, and a footprint override of `MWL_AshlandsFort4=40`. The supplied log confirms manual execution and protection settings of 1/0/1; it does not independently establish every reported configuration value.

The diagnostic at failure was:

```text
stage=WaitForPrefab
prefab='MWL_MistTrainer1'
definitions=205/365
instances=14409
elapsed=180.50s
stageElapsed=10.54s
prefabElapsed=10.54s
readiness=IsLoaded
lastReady=False
collect=0.00s, between=0.92s, load=0.00s, wait=179.57s,
asset=0.00s, release=0.00s, validate=0.00s,
footprints=0.00s, groups=0.00s
slowestCompleted='MWL_PlainsTavern1' (10.77s)
No reset stage started in this run.
```

This means:

1. Planning had completed 205 of 365 location definitions and was waiting for the next one. `MWL_MistTrainer1` was the current item when the overall deadline expired, not a proven faulty asset.
2. The individual 30-second timeout had not expired for that item.
3. No zone, vegetation, or location reset stage had started. The generic warning that completed changes remain does not establish partial reset damage in this run.
4. Most elapsed planning time was recorded in the readiness-wait stage. That includes asynchronous loading, callback processing, and time between coroutine polls. It is not a measurement of disk I/O alone.
5. The pre-maintenance save had already finished. AsyncSave reported a 2,273 ms snapshot across 136 frames and a 120 ms write, approximately 2.4 seconds in total. Those frame statistics do not measure frame rate during the later planning phase.

FreshWorld resolves neighboring location definitions to establish complete footprints and overlapping protection, including locations outside the configured reset list. Seventeen configured IDs therefore do not imply only seventeen metadata loads. Skipping other definitions or using unresolved/default radii would compromise that protection.

`AutomaticResetPercent` is applied after this planning work and does not affect a manual `freshworld` run. Reducing it to 50 cannot address this particular timeout.

## Findings by mod

### More World Locations AIO 5.2.2 and Jotunn 2.30.2

The relevant path is MWL registration through Jotunn's `AssetManager.GetSoftReference` / `ResolveMocksOnLoad`, followed by the native asset-loader callback and Jotunn's resolved-asset substitution. `InstantiateAndResolveAsset` clones and resolves references; `CustomLocation.OnLocationResolve` then supplies location radius information.

That work can contribute to loading cost. It also explains why FreshWorld must wait for the real metadata before calculating protection. No fixed ten-second delay was found in the reviewed path. Jotunn's prefab cache can synchronously load a referenced prefab, so a nominally asynchronous request does not prove that all callback work is asynchronous.

The inspected MWL assets had distinct bundles with empty declared bundle dependencies. Shared-bundle unload/reload churn was not established as the cause. Invalid zero-asset-ID registration warnings elsewhere in the log are a separate issue: the two named prefabs in the timeout diagnostic had valid nonzero asset IDs.

A successful local MWL test and this server timeout can coexist. Asset residency, configuration, hardware, concurrent work, and the set of instantiated locations can differ. The available evidence does not identify which difference dominates.

### ValheimCommunityPatch 0.37.0

`LocationRoomPreloadPatch` participates in `ZoneSystem.PokeCanSpawnLocation`, `PokeLocalZone`, and `UpdatePrefabLifetimes`. It can preload rooms for nearby locations and add competing loading work. FreshWorld's direct `SoftReference.IsLoaded` path does not itself call the patched `PokeCanSpawnLocation` route.

The mod's `Location Load Wait` setting controls player zone-entry behavior; it is not the measured 10.54-second FreshWorld wait. Its model-template and location-spawn-pacing patches are client-only and were not active on the reported dedicated server. The reviewed asset-unload feature is conditional, and the log does not show an unload during this planning interval.

**Assessment:** an indirect candidate worth a controlled comparison, not a demonstrated loader conflict.

### ServersideQoL and its modules

| Component | Relevant behavior | Assessment |
| --- | --- | --- |
| Core 2.4.1 | Processes changed/repeating ZDO work in nearby sectors each frame. It skips processing below 10 server FPS, but checks its work budget after processing a sector. | A large sector can contribute frame delay. No timing measurement establishes that here. |
| AutoMapTables 2.4.1 | `MapTableProcessor.Initialize()` requests location metadata for discovered location proxies. | Can add asynchronous asset requests. |
| SuperWishbone 2.3.0 | `Process()` uses `GetAndLoadLocationByHash()` to inspect dungeon/runestone metadata and reschedules pending work. | Can add requests and repeated processing while assets are pending. |
| Core `Location` wrapper | Requests `LoadAsync()` when appropriate. `Dispose()` restores the temporary transform but does not call `Release()`. | Reference retention can increase residency and may also make later loads faster. It is not proof of a leak or this timeout. |
| Core `BossesByBiome` | First access synchronously inspects location/room prefabs, then uses a static cache. | Possible startup/first-use cost; not necessarily repeated per FreshWorld run. |
| AutoPortalHub 2.4.0, AutoProcess 2.3.0, LetItFloat 2.0.11, Player 2.2.1, PrefabConfigurator 2.3.0, WorldOptions 2.2.1 | Object processors and configuration/network behavior. | No direct asset-readiness blocker or fixed loading delay found in the reviewed paths. |

The numerous startup `missing ZNetView component` warnings are registration-time evidence. They do not establish repeated work throughout the 180-second planning phase.

Core `DiagnosticLogs=true` can write `BepInEx/ArgusMagnus.ServersideQoL.ProcessingTimes.<world>.csv`, including FPS, elapsed processing time, sector/ZDO counts, and processor timing. Use it for a short comparison because the diagnostic itself adds file writes. The log showed core `Enabled=True` and `DiagnosticLogs=False`; individual module configurations were not supplied.

### ServersideQoL.Patchers 2.4.1.0

The supplied preloader patcher adds managed property wrappers to `ZDO` and `ZNetPeer` and inserts their initialization into constructors. It does **not** patch `SoftReference`, `AssetBundle`, `ZoneSystem.Update`, or asynchronous asset completion.

Wrapper allocation may have an indirect cost, but no measurement connects that cost to this incident. Being an `assembly_valheim` preloader patcher is not sufficient evidence of an asset-loading conflict. Runtime QoL processing is a more useful investigation target than attributing the failure to this patcher.

### Other supplied candidates

| Mod | Relevant finding | Follow-up priority |
| --- | --- | --- |
| ValheimTune 0.7.10 | Frame-rate and network tuning; optional asset-unload deferral. The logged target of 60 FPS is not a measurement of achieved FPS during planning. | Secondary performance comparison. |
| AsyncSave 0.6.1 | Save hooks; the pre-maintenance save completed before planning. No asset-loader hook found in the reviewed path. | Low for this failure. |
| Pause My Server 1.5.0 | Pauses game-time/event advancement through selected hooks; no general `Time.timeScale` or asset-loader pause found. | Low; no pause evidence in the interval. |
| StarLevelSystem 1.26.4 | Optional location reset feature, disabled by default, with additional YAML conditions. Some metadata inspection uses synchronous loads. No FreshWorld-specific reset exclusion was found. | Check whether its independent location reset is enabled. Overlap is possible if configured, but not established by this log. |
| Seasonality 3.8.4 | Asset/material/texture setup and season-change work can run on a server. No per-FreshWorld-prefab loading hook found. Its ten-second status check is unrelated to this wait. | Conditional/secondary load candidate. |
| ValheimFortress 0.40.0 | Shrine registration and challenge processing. | No direct link to the reported metadata wait found. |
| ImpactfulSkills 0.25.0, ValheimArmory 1.36.1 | Player behavior and initial content registration. | Low based on reviewed paths. |
| Max Dungeon Rooms 2.0.42 | Changes actual dungeon room generation, which had not started in this run. | Low for the planning timeout. |
| PlantEverything 1.21.4 | Startup prefab setup and plant/pickable patches. | No persistent loader blocker found. |
| FoodDurationMultiplier 1.1.7, ToolTweaks 1.4.1, All the Ores 1.0.5 | Food, tool, recipe, and item behavior. | No direct planning-loading path found. |
| Conditional Config Sync 1.0.11 | Configuration/network synchronization; its reload waiting occurs on a worker path. | No asset-loader patch found. |

These priorities are investigative judgments, not measured rankings. Unreviewed plugins and combinations are not certified compatible by omission.

## FreshWorld patch

The change is confined to `MaintenancePipeline.PlanLocations` and its pause helper:

- Remove the cumulative planning stopwatch and its 180-second exception.
- Keep a fresh 30-second timer for each prefab, for both direct `IsLoaded` and named-reference readiness paths.
- Check readiness before timing out. A prefab already ready when execution resumes is accepted.
- Continue excluding observed game-pause time from the prefab and diagnostic timers.
- Preserve world identity checks, cancellation, reference cleanup, final definition validation, footprint calculation, and existing diagnostic output.

No settings, reset selection, protection rules, Harmony targets, dependency versions, or loading parallelism are changed. No permanent asset cache or extra normal-run logging is introduced.

The 30-second limit remains a **cooperative readiness timeout**. It is checked when the coroutine regains control. A synchronous external `LoadAsync`, `IsLoaded`, `Asset`, or `Release` hook can block beyond that time; paused wall time can also extend the run. There is no new maximum overall wall-clock guarantee.

## Verification

The baseline Debug build and all 103 existing backend regressions passed. With the new tests added but the production code unchanged, the 200-second progress case failed with the original 180-second exception; the other 106 tests passed. After the production change, all 107 backend tests passed.

The four added cases cover:

1. Ten successive 20-second loads complete after 200 seconds in total, release references, and only then admit restoration.
2. A continuously unready prefab times out after 30 active seconds, identifies the prefab/stage, releases references, and starts no reset.
3. Readiness on the first poll after the deadline is accepted.
4. Five paused minutes do not consume the active 30-second allowance.

The timeout, deadline-readiness, and pause cases exercise both direct and named-reference paths. Existing tests cover cancellation, world invalidation, exceptions, cleanup, protection, and overlapping location behavior. The manual clock exists only in the source-linked backend test project; the plugin continues using `System.Diagnostics.Stopwatch`.

Patch validation before the 1.1.8 version bump:

- `dotnet build FreshWorld.sln -c Debug -p:DeployToGame=true`: succeeded with zero warnings and zero errors, including ServerSync merging and deployment.
- Backend: 107/107 tests passed. The other ten harnesses also passed: Core (71), Runtime (11), Terrain (13), Harmony (46), World (83), Generation (25), Command (29), Plugin (52), Save (19), and final merged-DLL Sync checks.
- `git diff --check`: passed.
- Validated Debug plugin: `FreshWorld/bin/Debug/netstandard2.1/FreshWorld.dll`.
- Deployed plugin: `C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/plugins/FreshWorld.dll`.
- Both DLLs have SHA-256 `684F73144EFDCCC61210E13C4C966ECFE4DBD4CAC99B511836BC341B65BEE680`.

The automated harnesses combine source-linked substitutes and isolated managed execution of original game/Harmony/BepInEx components. They are not actual Unity gameplay. No real game/server reproduction or mod A/B benchmark was performed in this work. The affected dedicated server still needs a run with the corrected DLL to establish its actual loading times and maintenance outcome.

## Suggested server comparison

1. First test the timeout correction on a backup copy of the affected world. Record whether planning completes and how long individual prefab waits take.
2. Check StarLevelSystem's optional location-reset settings before allowing independent reset systems to run together.
3. If planning remains unusually slow, compare VCP, then QoL's AutoMapTables/SuperWishbone work, one change at a time. Keep MWL and its required Jotunn dependency together.
4. Keep the world copy, FreshWorld build, settings, connected-player positions, and timing after restart comparable. Warm asset caches can invalidate a casual before/after comparison.
5. Compare elapsed planning time and completed-definition counts, not only whether the old 180-second failure disappeared. Preserve per-prefab diagnostics for any actual 30-second timeout.

Do not remove protection calculations, skip unresolved locations, preload every asset at once, or disable other mods' patches based solely on the name of the prefab shown at timeout.
