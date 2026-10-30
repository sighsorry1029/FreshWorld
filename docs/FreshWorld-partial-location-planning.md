# Partial location planning

The 30-second per-prefab planning timeout remains unchanged. A timeout can now exclude a verified area and let other targets proceed. Other planning errors, unknown bounds, execution failures and cleanup failures retain the existing stop behavior.

## Exclusion policy

- Exclude every registered instance of a timed-out prefab, including ungenerated instances and instances outside the selected reset list.
- Start with the anchor's 3x3 zones, then include the full known footprint and any configured minimum radius.
- Expand through every location footprint sharing an excluded sector. This avoids regenerating half of a connected location.
- Apply the resulting mask to zone groups, resource and location candidates, terrain tiles, border cleanup and recursive object deletion. SafeZones=0 does not bypass it.
- Remove excluded targets before automatic percentage selection.
- Keep exclusions in the current pipeline only. A later manual or scheduled run tries loading the prefab again.

A completed run with exclusions is reported as partially completed. Its scheduled occurrence is consumed normally; there is no immediate retry, new journal status, saved exclusion list or new setting. Previously applied world changes are not rolled back if a later stage fails.

## Bounds trust boundary

`LocationMetadataFallback` requires a valid native soft reference, a positive finite registered exterior radius unchanged by the loading request, and exactly one matching definition in the original serialized LocationLists or `ZoneSystem.m_altBiomeLists[*].m_alts[*].m_addLocations`. Both definitions must agree on asset identity, name, exterior radius and interior radius. The cloned runtime `AltBiomeList.m_altBiomes` registry cannot prove its own bounds. Duplicate asset entries remain ambiguous and refuse fallback, including duplicates across the two sources.

It reads existing native loader state through reflection using the original private/internal declarations. It does not create a loader or synchronously request the timed-out asset. Pending load callbacks or unrecognized asset-resolution patches refuse the fallback. A configured radius override alone is a minimum, not proof that a custom prefab cannot extend further.

Jotunn's asset replacement prefix is accepted only when its relevant method and singleton getter prove that an already-initialized manager returns without modifying the asset on a dictionary miss, and that asset is absent from its resolver map. This is a behavior check, not a version, whole-DLL hash or MVID allowlist. Unknown implementations stop maintenance rather than bypassing another mod's resolver. Bounds refer to the engine's advertised extent; this does not inspect every child mesh of an unloaded or incorrectly authored prefab.

The full loader/patch check runs when accepting a timeout and at stage boundaries. During a partial run, world-readiness checks also compare the planned location snapshot. This catches new anchors, same-count registry replacements and synchronous callbacks that change bounds within one frame. Completed zone resets may refresh their own ground height; other position changes remain invalid. The game's unique-location placement may remove unused instances; removed entries leave the old exclusion mask intact rather than aborting valid work. It adds comparisons proportional to the location registry only while a partial run is active; no benchmark or measured performance gain is claimed. Normal runs and idle frames do not perform these additional checks. No permanent asset or world-state cache is introduced.

## Loading diagnostics

Waiting for a native soft reference samples the existing loader's `IsLoading` and `GetProgress` at most once per second, plus a final sample at the 30-second timeout before releasing the reference. Reflection metadata is cached; the active loader instance is read afresh. Diagnostics never create a loader, request an asset, wait synchronously, or decide whether the prefab is ready. Accessor failures and nonfinite or out-of-range progress become `unknown`.

Only failure/cancellation and timeout-exclusion warnings format the extra fields: `isLoading`, `loadProgress`, `loadSamples`, `loadSampleAge`, and `progressUnchangedFor`. The latter measures unchanged observations through the last sample, not beyond it; the separate sample age identifies stale information. The native 0–1 progress value weights bundles and the asset request rather than reporting bytes transferred. It does not identify a responsible mod or prove a deadlock. Per-prefab state resets between definitions, observed game pauses do not count toward waiting time, and normal logs remain unchanged.

## Validation

Debug solution build and ServerSync merge passed with zero warnings and errors. The final DLL was deployed through `DeployToGame=true`.

Relevant automated checks passed: Backend 119, Generation 26, Plugin 63, World 87, Terrain 13, installed Harmony/BepInEx 48, and original-metadata fallback boundary checks 21. The API verifier also passed 57 native contracts and 214 compiled game-member references against the preserved Valheim 1.0.21 Windows dedicated-server assemblies. The optional Configuration Manager assembly smoke check was not enabled.

Coverage includes multiple instances, diagonal neighbors, ungenerated anchors, large and transitively connected footprints, SafeZones=0, percentage selection, later retry, same-frame metadata changes, same-count registry replacements, native ground-height updates and unique-instance pruning, partial completion reporting and deletion predicate forwarding. Diagnostic checks cover throttling, the final timeout sample, getter failures, named references, and state reset between prefabs.

The optional original-metadata harness uses the built merged plugin, installed Harmony, original Valheim assemblies and the supplied Jotunn 2.30.2 DLL. It checks private/internal field access, callback rejection, original AltBiome serialized sources, ambiguous and runtime-only sources, and diagnostic reads of an existing loader queue in an isolated managed process. Unity objects supply serialized fields without running constructors or Awake; Unity asset loading and native progress after loader initialization are not executed. Test-only copies with changed version/MVID and no-op instructions are accepted; a changed resolver guard is rejected. The supplied DLLs remain untouched. Run it with:

```powershell
FreshWorld.Harmony.Tests/bin/Debug/net48/FreshWorld.Harmony.Tests.exe --location-fallback <original-managed-directory> <built-FreshWorld.dll> <Jotunn.dll>
```

These are static and isolated managed checks, not Unity gameplay. No live client, host, dedicated server, Linux server or affected modpack reproduction was executed. The affected server still needs to confirm that the timed-out prefab has verifiable bounds, excluded areas remain intact, unaffected targets complete, and the next run retries the prefab. The patch does not diagnose or repair the underlying asset-loading delay.
