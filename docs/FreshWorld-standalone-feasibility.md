# FreshWorld standalone engine and commands

> Historical design record. Current protection automatically detects player-built Pieces, with `PieceBlacklist = fire_pit` replacing `PlayerPlacedObjects`. Tombstones remain separate markers. ServerSync is now embedded for optional administrator configuration. See the [README](../README.md) for current installation and configuration.

Updated on 2026-09-06 for FreshWorld 0.6.4. This document explains the standalone engine introduced in 0.2.0, the commands introduced in 0.3.0, and the current operating rules. For installation and configuration, use the [README](../README.md) and [cfg example](../config/FreshWorld.example.cfg).

## Current design

BepInExPack Valheim is the only required external mod. FreshWorld contains its restoration engine and uses the game's Valheim and Unity libraries plus the Harmony library supplied with BepInEx. It does not call Upgrade World, Server devcommands, or Cron Job.

The author is sighsorry. The mod, assembly, and namespace remain FreshWorld; the plugin ID is sighsorry.FreshWorld and its cfg is BepInEx/config/sighsorry.FreshWorld.cfg. Older cfg files are not imported automatically.

The current cfg has 15 options in three sections. `Enabled` controls automatic scheduling only; authorized manual commands work with either value. `GameDays` defaults to 24 game days, and `DailyTimes` uses the host OS time zone. The two resource lists separate vegetation-only restoration from restoration with nearby terrain. The default location list contains 14 IDs; `BlackForest_DG_RtD` and `Mountain_DG_RtD` were removed in 0.6.1. Version 0.6.4 removes `Mistlands_Giant1:dark`, `FortressRuins`, and `AshlandRuins`, adds `CharredFortress`, and retains the regular `Mistlands_Giant1`.

Optional Configuration Manager controls provide toggles, numeric inputs, and native popups for Mode and the three SafeZones choices. The popups overlay the settings with a dimmed, disabled background instead of expanding rows. A lossless ConfigChoice wrapper and BepInEx converter preserve the original text of those four entries; other entries remain strings, and explicit validation still rejects invalid values. `Enabled` is displayed as `Automatic Runs`; all 15 cfg keys and defaults stay the same. The editor adds no required dependency or remote synchronization, and its UI remains unverified in a running game.

A run saves first, then processes zones, resources with terrain, resources without terrain, and locations. The game handles later saves. `world_clean` was deliberately excluded: FreshWorld does not provide Upgrade World's general cleanup of duplicate objects, container items, or old dungeon data.

## Engine coverage

| Area | Implementation |
|---|---|
| Configuration and scheduling | Validated snapshots, wall-clock or game-day schedules, manual requests, per-world records, and duplicate prevention |
| Host execution | Shared code for single-player, local hosts, and dedicated servers; ordered stages and failure reporting |
| Zones | Remove selected ZDOs while guarding player characters, update generated and placed state, and release zone roots |
| Zone borders | Repair affected height edges and corners of retained neighboring zones |
| Vegetation | Remove exact prefab IDs and registered `_frac` forms, replay selected native placement, and clean up temporary objects |
| Terrain | Use native compression with a separate TCData codec and scoped original-ground generation hooks |
| Locations | Clear and regenerate selected placed locations at their existing positions, including terrain and dungeon generation |
| Base protection | Apply configured player-placed markers, a separate tombstone marker, and each stage's SafeZones range |
| Large deletions | Limit each destruction notification to 10,000 object IDs |
| Execution control | One maintenance lease, guarded nested coroutines, error propagation, and cancellation cleanup |

Selected algorithms were adapted from or informed by the reviewed [Upgrade World 1.80 source](https://github.com/JereKuusela/valheim-upgrade_world/tree/f1e3b21140a9d7865d204910a1ed40a3df93297b). The general console parser, remote command RPC, map tools, and location relocation tools were not ported. Private game APIs are accessed through cached reflection and Harmony helpers against unmodified game assemblies.

## Manual commands and authority

`freshworld` submits a one-time restoration request. `freshworld status` reports the host's state without starting restoration. These are the only accepted command forms. Native console autocomplete completes the command name and offers only `status` as its argument. The host's validated cfg supplies reset IDs and policies; commands do not accept overrides. Configuration and protection guidance is available in the README and cfg descriptions.

Manual commands do not wait for the automatic polling interval. They still require a ready world, the initial startup grace period, an unpaused host, completed saves, and an available maintenance lease. Invalid reset settings or a faulted world session prevent new work even when the request is authorized.

All world changes run in the hosting process. A player hosting within that same game process may run the command without an administrator entry. Remote players require the server's administrator check using the identity obtained from their actual connection. A separate client connected to a dedicated server on the same PC is still remote. An IP address, a claimed identity, or a client-side administrator flag does not grant permission.

For remote commands, the host and administrator client need matching FreshWorld versions. Ordinary clients do not need the mod. Requesters receive admission, waiting, progress, and result messages. World, session, command format, duplicate, and configuration checks apply to administrators and local hosts as well. FreshWorld does not transfer character save data or introduce a character save sequence number.

An accepted manual request keeps its settings snapshot and requester session. Disconnection, revoked authority, an invalid cfg, or a world change cancels pending work. Active authority checks can also stop a running request; changes already made are not rolled back. Toggling automatic scheduling or editing a valid schedule preserves an accepted manual request. A request from an earlier server session requires a new command because its authenticated context cannot be restored from disk.

## Scheduling and reset scope

- Every run includes all enabled stages. Disabling automatic scheduling clears only unstarted automatic work. A run already in progress completes with its captured settings.
- Re-enabling automatic scheduling starts from a fresh baseline. GameDays waits a full interval; DailyTimes waits for a future configured time.
- With zone reset enabled, supplemental stages consider only initially protected zones that are still generated. With zone reset disabled, they consider all currently generated zones, then apply their own IDs and protection range.
- Stages run in order. A failed stage stops later stages. Maintenance pauses while the local host is paused and does not retry failed or interrupted runs automatically.
- Resource and location IDs do not restrict the earlier zone reset. Turning off a supplemental stage does not exclude its objects from zone regeneration.
- FreshWorld does not track location visits or provide a general-purpose `terrain_reset` command.

## Protection

`PlayerPlacedObjects` contains the complete editable marker list. An object qualifies only if its prefab is listed and its `creator` value is nonzero. Adding or removing an ID changes the actual list; the defaults are not merged back in. The default list has 30 entries, including player-built beehives.

`Player_tombstone` is a separate marker without the creator requirement. Tombstones store the character profile ID in `owner`, while player-built pieces use `creator`. Emptying `PlayerPlacedObjects` leaves this separate marker in place.

All markers, including tombstones, protect zones only when the relevant stage's SafeZones value is positive. Only 0, 1, and 2 are accepted: 0 disables that stage's marker protection, 1 protects the marker's zone, and 2 protects the marker's zone and its eight neighbors in a 3x3 area. Existing values of 3 or higher are invalid and prevent new work until manually corrected. These are zone selection rules, not blanket immunity for every object. Location reset areas and terrain support can also affect structures.

## Terrain data and differences from Upgrade World

The implementation was checked against the installed game's `TerrainComp.Save/Load` and `Heightmap.WorldToVertex/WorldToVertexMask`. TCData version 1 contains operation metadata, 65x65 height data, and either current 65x65 or older 64x64 paint data. The codec preserves the input layout and handles each paint format with its own row width.

Unknown versions, unsupported dimensions, trailing data, malformed flags, and nonfinite values are rejected. Operation metadata changes only when a cell changes. Each terrain request validates and compresses all affected compiler records before applying that request. This prevents a malformed later record from partially committing the same request; it does not make the entire maintenance run a rollback transaction.

Height vertices use `zone center + index - 32`; paint cell centers use `zone center + index - 31.5`. The reviewed Upgrade World implementation used `index - 32.5` for both. The resulting 0.5 m height and 1 m paint differences can change cell selection near the reset radius. FreshWorld follows the inspected game coordinates. Zone-border repair changes heights only and preserves paint and interior cells.

The `ZoneSystem.GetGroundData` and `GetGroundHeight(Vector3)` hooks apply only during generation that requests terrain restoration. The game's `GetTerrainDelta` also uses ground-height queries. Vegetation-only placement leaves these hooks off. When both resource groups are present and the terrain radius is positive, two frame boundaries allow terrain and collider updates before vegetation-only placement. Terrain is refreshed at stage completion and during partial-failure cleanup. General cleanup of old TerrainModifier objects and unknown future TCData formats is outside this implementation.

## Implementation history

| Version | Change |
|---|---|
| 0.1.x | Used Upgrade World and its protection configuration |
| 0.2.0 | Added the standalone engine; exposed `PlayerPlacedObjects` and a separate `Objects` list |
| 0.3.0 | Replaced `RunNow` with native commands and server-verified authority; at that time, `Enabled=false` also blocked manual requests |
| 0.4.0 | Simplified cfg and scheduling, kept only a pre-run save, and replaced the Force option with `LocationSafeZones=0` |
| 0.5.0 | Split vegetation-only and terrain-assisted resource lists |
| 0.5.1-0.5.2 | Restored the full editable player-marker list and added `piece_beehive` |
| 0.6.0 | Made Enabled automatic-only, removed ManualOnly, and set the default interval to 24 game days |
| 0.6.1 | Removed the two RtD location defaults and standardized documentation in English |
| 0.6.2 | Fixed Harmony startup discovery by renaming the terrain Prepare helper and isolating ground hooks in a nested patch class |
| 0.6.3 | Set the author and plugin ID, changed the active cfg filename, and adopted a five-file Thunderstore ZIP |

Early versions also had morning-only resource scheduling and an exposed creator-independent `Objects` option. Those are historical settings. Use the current cfg example when updating; old settings are not converted, aliased, or copied automatically. Existing explicit ID lists are not rewritten when defaults change.

## Validation and remaining checks

Release builds use unmodified local Valheim, Unity, and BepInEx assemblies. Automated harnesses cover scheduling, configuration, authority, sessions, coroutine cleanup, stage control, terrain data, and game-boundary calls. Terrain checks include synthetic height and paint payloads, radius boundaries, unchanged data, both paint row layouts, border directions, and malformed input. API stubs test control flow; they do not establish actual Unity generation or multiplayer synchronization behavior.

The startup regression invokes actual Harmony patch discovery with game API stubs. It covers the failure where Harmony treated a terrain helper named Prepare as its lifecycle callback and called it without a world ZDO. Renaming that helper and isolating the two ground hooks in a nested patch class keeps data-processing methods outside patch discovery. This regression does not establish successful startup in a running game.

In-game verification is still needed for single-player, local hosting, dedicated hosting, remote administrators, ordinary clients, reconnects, marker edits, zero-protection settings, structures over mined terrain, regeneration near connected players, pause and shutdown, and client terrain/deletion updates. Runtime loading with only BepInEx also needs an in-game check. No performance benchmark has been completed.

## Dependencies and attribution

Upgrade World's reviewed manifest declared Server devcommands as a dependency. That applied to installations using Upgrade World; FreshWorld's standalone engine does not use that installation chain. The reviewed [CommandWrapper](https://github.com/JereKuusela/valheim-upgrade_world/blob/f1e3b21140a9d7865d204910a1ed40a3df93297b/UpgradeWorld/CommandWrapper.cs) provides command-help integration and is not needed by FreshWorld's cfg-driven engine.

The source repository retains the Unlicense text, source revision, and adaptation notes in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md). The five-file release ZIP includes the attribution and upstream license link in README.md; it includes no game or BepInEx assemblies. Fewer dependencies do not by themselves prove better performance. FreshWorld remains responsible for compatibility with changes to game saves, terrain, dungeons, and networking.
