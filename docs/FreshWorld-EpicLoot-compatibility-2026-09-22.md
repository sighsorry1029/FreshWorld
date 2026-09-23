# FreshWorld / EpicLoot treasure protection

This records the treasure-only implementation from September 22. The September 23 bounty extension is documented at the end; the README describes the combined current behavior.

## Scope and evidence

Implemented in the FreshWorld checkout on local `master` (tracking `origin/main`), based on `d1a75b7ee5c760dbddada2dc64f57d82e0ee1bc3`, version 1.0.7. The tree was clean before the initial compatibility work. This revision narrows that uncommitted work to treasure-only, own-sector protection at the user's request. ServerManager and EpicLoot were not modified. No version change, Release package, commit, or push was performed.

The supplied FreshWorld 1.0.7 DLL matched the checkout's existing Release output: SHA-256 `1fe42b94a139418caa9d523d53a403d8348b2e71f9c8e3dc902515374e4679bf`.
The supplied EpicLoot 0.14.10 DLL has SHA-256 `49f412bbe9d24db2c17e674147dde410991e89270020533ea6121a65ce891aa7`.
The analysis used those binaries and original Valheim 1.0.15 client (25390630) and dedicated-server (25390671) assemblies. No publicizing, support-version change, or recollection of game references was needed.

EpicLoot keeps per-world adventure progress in player custom data through `AdventureComponent.Save` and `AdventureSaveData`. Removing a world ZDO does not mark that player's treasure as found. `MinimapController` can therefore recreate a marker after a reset has removed the chest. This establishes a possible FreshWorld orphaning path, not proof of every reported missing chest.

The persisted markers verified in `AdventureSpawnController`, `TreasureMapChestInfoZNetProperty`, `TreasureMapChest`, and `BountyTarget` are:

| World object | Recognition | Policy |
| --- | --- | --- |
| Pending treasure spawn | `EL_SpawnController`, nonempty `treasure_spawn` byte array, false/missing `isBounty`, false/missing `placed` | Protect object and its own sector |
| Unfound treasure chest | Nonempty `TreasureMapChest.Biome`, false/missing `TreasureMapChest.HasBeenFound` | Protect object and its own sector |
| Bounty controller, leader, or add | `isBounty` / `BountyID` | No special protection |
| Uninitialized or already placed controller; found chest | Does not satisfy treasure predicates | No special protection |

The controller prefab is shared by treasure maps and bounties. Testing its prefab alone, or `isBounty=false` alone, would protect unrelated controllers. Constructing EpicLoot's ZNet property does not persist its default treasure payload; `ForceSet` writes the actual data. FreshWorld checks only nonempty payload presence and never deserializes its opaque contents.

These checks do not require `Piece`, creator ID, loaded components, prefab registration, or an online owner. Existing player, base, and tombstone protection remains independent and can still retain an otherwise eligible sector.

## Changes and lifecycle

- `Engine/EpicLootProtection.cs` owns observations for one maintenance run. `MaintenancePipeline.Run` captures saved ZDOs after the mandatory world save. Each target attempt/loading retry checks that exact sector again. Observed sectors remain protected until the run ends, including after completion, removal, or movement. The next run uses a new instance.
- With `EpicLootProtection=true`, `MaintenancePipeline.CanResetZone` applies treasure protection independently of stage SafeZones values. Zone, resource, and location restoration skip the treasure's own sector only (1x1). Existing administrator permissions, player protection, and supplemental candidate selection are unchanged.
- Neighboring resource/location generation, terrain-radius effects, and terrain-border repairs remain eligible even when effects cross into the treasure sector. No expanded footprint calculation or border-repair callback remains. The treasure's surrounding terrain and accessibility may change.
- With that setting enabled, `GameWorld.RemoveZDO` refuses qualifying treasure objects before ownership reassignment or destruction. This also covers a treasure reached through a recursive `Spawned` connection from an ordinary object in another sector. The ordinary parent remains deletable. Disabling the setting also disables this deletion immunity for the next run.
- Bounty targets, adds, and bounty controllers can be removed by ordinary maintenance. No bounty lifecycle or player progress is inferred from their tags.

No EpicLoot assembly dependency, Harmony patch, RPC, migration, expiry, world-wide cap, quest cancellation, reward, or player-file edit was added. The server-synchronized `EpicLootProtection` setting defaults to true. Saved keys are hashed once. There is no new per-frame idle work: one initial world snapshot and target-sector snapshots run during maintenance when enabled. This adds per-target scanning/allocation; no timing or performance improvement was measured.

## Validation

```powershell
dotnet build FreshWorld.sln -c Debug -p:DeployToGame=true
```

The original pre-compatibility baseline passed 335 checks. Before narrowing, the broader uncommitted patch built and passed its relevant World (43), Backend (49), and Generation (14) checks. The final Debug build, ServerSync merge, and deployment passed with zero warnings/errors. All 348 final checks passed:

| Harness | Checks |
| --- | ---: |
| Core/config | 64 |
| Runtime | 11 |
| Backend | 50 |
| Terrain | 10 |
| Harmony/BepInEx | 25 |
| World | 44 |
| Generation | 14 |
| Commands | 28 |
| Plugin/controller | 37 |
| Original-game save | 18 |
| Merged-DLL configuration sync | 47 |

The added cases cover treasure/controller classification and bounty exclusions, protection without creator metadata, exact-sector protection, large neighboring resource/location extents remaining eligible, loading retries, per-run retention and next-run release, moved/new targets, coordinate edges, recursive deletion, and all eight neighboring border repairs. Native generation tests confirm a denied target causes no object, terrain, or placement mutation before or after loading. Several cases contain multiple scenarios.

`tools/verify-game-api.ps1`, run with PowerShell 7 against each original 1.0.15 role, passed 51 native contracts and 170 compiled game-member references. This checks metadata resolution, not gameplay. A separate original-metadata check confirmed that the ZDO accessors used by treasure recognition, including `GetByteArray`, are public in both roles. The final DLL and the Steam Valheim `BepInEx/plugins` copy both have SHA-256 `db6ac653ed247ca8898b71874714effc82088351ec18b0856bab0084bed2463d`. Test logs and role-specific API reports are under `FreshWorld/bin/Debug/epicloot-treasure-only-verification`. `git diff --check` passed.

Most behavioral harnesses use source-linked production code with simulated game boundaries. Save and installed-library checks exercise selected original managed methods or isolated CLR contracts. No actual Unity, listen-host, dedicated multiplayer, Linux, or EpicLoot quest session was run.

## Limits and in-game acceptance checks

An unfound treasure can retain its one sector indefinitely; this patch adds no expiry or total limit. Several treasures in one sector retain only that one sector, while treasures in different sectors retain each sector independently. Completion/removal releases the special sector rule on the next maintenance run. Already lost chests and their orphan pins are not repaired.

Protection sees only ZDOs/metadata present on the host when checked. It cannot reconstruct a client-created object not yet received by the server. Other reset tools, future EpicLoot marker changes, whole-game resources, unrelated EpicLoot features, library internals, and other mods are outside this verified scope.

Test a disposable world copy with EpicLoot 0.14.10 and the patched FreshWorld on the host:

1. Purchase treasure maps; test pending controller-to-chest transitions and unfound chests with the owner online and offline. Confirm their own sectors are skipped with SafeZones=0.
2. Restore immediately adjacent sectors using zone, resource, and location operations. Confirm neighboring work and terrain-border repairs proceed, the chest still exists, and treasure loot remains accessible despite possible terrain changes.
3. Save, restart, reconnect, and check persisted protection and normal quest completion without replacement or duplicate rewards. A found/removed chest should release its sector next run unless another protection rule applies.
4. Create a treasure while restoration waits for zone loading. Confirm an own-sector treasure blocks that attempt, while a neighboring treasure does not. Check bounty objects remain subject to ordinary restoration.

Update the FreshWorld DLL on the machine hosting the world. The Debug deployment performed here updates only this PC's Steam Valheim installation; external servers and Gale profiles need their own DLL replacement while stopped.

## Bounty extension: September 23, 2026

Implemented on top of `7587cd32323ff9f470eea7fa824decb670fade7b` (FreshWorld 1.0.8), with a clean starting tree. The following structure-review request changes the bounty default to false and requests a separate commit for this feature. No version change, Release packaging, or push was requested.

The new `[Protection] EpicLootBountyProtection=false` setting is independent of the existing treasure setting. Enabling it opts into bounty protection. It uses the existing ServerSync registration, administrator validation, cfg watcher, and immutable run snapshot. The vendored ServerSync DLL and its merge path are unchanged. Both settings appear next to each other in Configuration Manager. Existing explicit values are preserved when the config is bound or reloaded.

Recognition was checked against the same original EpicLoot 0.14.10 DLL and hash listed above:

| Object | Saved markers | Scope |
| --- | --- | --- |
| Pending bounty controller | `EL_SpawnController`, `isBounty=true`, `placed=false`, nonempty **`bount_spawn`** byte array | Own zone and direct deletion protection |
| Bounty leader or add | Nonempty `BountyID` | Current zone and direct deletion protection |

The payload is never deserialized. `BountyID` alone matches EpicLoot's target restoration check; requiring `BountyData` or `MonsterID` as well could miss partially synchronized targets. The owner need not be online, and neither a loaded component nor creator metadata is required.

`EpicLootProtection` now captures the enabled treasure and bounty markers in one initial ZDO scan after the pre-maintenance save. Each target-zone attempt and loading retry checks for new or moved objects. Observed zones remain excluded until the run ends; the next run starts fresh. With both settings disabled, the EpicLoot world scan and target-zone lookup are skipped. No idle per-frame scan was added. Performance was not benchmarked.

Each controller, leader, and add protects only its own zone, independently of SafeZones. EpicLoot selects the actual spawn point within the map circle and can place adds up to 4m from the leader, so one bounty can occupy several zones. It sets a patrol point rather than restricting movement to one zone. `GameWorld.RemoveZDO` also checks the appropriate option before claiming ownership or deleting an object, including recursive spawned children reached from another zone. All three reset stages pass both options through to this guard.

Normal kills and rewards remain EpicLoot's responsibility. Its completion condition requires the leader and all adds to be slain; destroying a target object does not report a kill. Abandoning a bounty changes player progress and map pins without necessarily deleting the target or clearing `BountyID`. Such residual objects remain protected while their tags exist. FreshWorld does not infer active quest status or remove abandoned targets.

Neighboring terrain restoration, location/resource placement, and border repairs retain their existing behavior. This protection does not promise unchanged terrain around a surviving target, restore already lost targets, or protect objects not yet known to the host.

### Verification

- Baseline Debug solution build and relevant Core, World, Backend, Generation, Plugin, and merged-DLL Sync checks passed before edits.
- Final Debug solution build with `DeployToGame=true`: zero warnings and errors. No EpicLoot assembly dependency was added.
- Core/config 65, World 51, Backend 53, Generation 14, Plugin 37, merged-DLL Sync 56, and installed Harmony/BepInEx 27 checks passed. The installed Configuration Manager metadata smoke was skipped because its optional assembly path was not supplied. Configuration presentation tests checked the new toggle, ordering, and captured settings without rendering Unity UI.
- Cases cover both options' four combinations, opaque controller payloads and placed/uninitialized exclusions, leaders/adds, the 4m zone-boundary case, movement, late arrivals, recursive deletion, per-run retention/release, independent administrator edits, server file reload, malformed settings, and preservation of an admitted run's settings.
- The installed-BepInEx fixture's old 15-key schema and drawer expectations were updated to include both EpicLoot switches (17 settings total).
- Original Valheim 1.0.15 client and dedicated-server API checks passed: 51 contracts and 170 compiled member references per role. Reports: `FreshWorld/bin/Debug/bounty-verification/`.
- After changing the bounty default to false, the seven relevant suites above passed again (303 checks), including an explicit true value surviving Bind/Save/Reload. Debug DLL and Steam `BepInEx/plugins/FreshWorld.dll` SHA-256 match for this feature checkpoint: `4b987453d05b3f937a71dbcc42cc9c8139daaf80e74203ed638f82e33e3d58c7`.

These are build, metadata, and isolated regression results. Actual Unity, local-host, dedicated multiplayer, and EpicLoot quest sessions were not run. Remaining game checks are remote-owned target movement across zones, controller-to-target transitions during maintenance, and save/restart/reconnect with pending, active, completed, and abandoned bounties. Confirm normal quest completion and rewards, independent switches, and the documented neighboring-terrain limitation in a disposable world copy.
