# FreshWorld configuration design - 0.6.4

FreshWorld is designed primarily for ordinary single-player and local-host players, while retaining dedicated-server support. The current configuration has **15 options in three sections**. It exposes choices about when restoration runs, what it restores, and which base zones it protects. Scheduling checks, execution budgets, and save handling are internal policies.

The [README](../README.md) describes current use and shows the full default lists. The [configuration example](../config/FreshWorld.example.cfg) is the copyable reference. This document explains the decisions and their practical limits.

The author is sighsorry and the BepInEx plugin ID is sighsorry.FreshWorld. The active file is BepInEx/config/sighsorry.FreshWorld.cfg. The previous fresh_world.cfg is not automatically imported; existing cfg values are not updated to new defaults.

## Current configuration

| Key | Default | Purpose |
|---|---|---|
| General.Enabled | true | Enable automatic scheduling; manual commands work with either value. |
| General.Mode | GameDays | Select GameDays or DailyTimes. |
| General.GameDayInterval | 24 | Game days between automatic runs. |
| General.DailyTimes | 05:35,17:35 | Daily times in the host OS time zone. |
| Reset.Zones | true | Reset generated zones first. |
| Reset.Resources | true | Enable both resource supplemental groups. |
| Reset.TerrainResourceIds | rock4_copper,silvervein | Resources to restore together with surrounding terrain. |
| Reset.ResourceTerrainRadius | 20 | Terrain radius in metres for TerrainResourceIds; 0 is allowed. |
| Reset.ResourceIds | Empty | Resources to restore without surrounding terrain restoration. |
| Reset.Locations | false | Enable selected location supplements. |
| Reset.LocationIds | 14 selected IDs | Locations to restore, starting with Hildir_crypt. |
| Protection.ZoneSafeZones | 1 | Marker protection for the zone stage. |
| Protection.ResourceSafeZones | 0 | Marker protection shared by both resource groups. |
| Protection.LocationSafeZones | 0 | Marker protection for the location stage. |
| Protection.PlayerPlacedObjects | Full 30-ID list | Complete editable set of creator-qualified base markers. |

General has four options, Reset has seven, and Protection has four. Separate stage switches let a player disable restoration without deleting a carefully edited ID list. The marker list is visible in full so players can understand which objects are preventing a zone from resetting.

The table follows the Configuration Manager display order. This optional editor shows boolean toggles, numeric inputs for the interval and terrain radius, and native popups for GameDays/DailyTimes and the three SafeZones choices. These are Configuration Manager's standard choice popups, also used for enum settings: choices overlay the rows while the background is dimmed and disabled. Rows do not expand. DailyTimes and ID lists remain text fields. General.Enabled is displayed as Automatic Runs without renaming the key. No Configuration Manager dependency or remote configuration synchronization is required or added, and the UI has not been verified in a running game.

Mode and the three SafeZones entries use a lossless ConfigChoice wrapper rather than ConfigEntry<string>. A BepInEx converter preserves their original text, including invalid values, while the popup displays readable choices. The other entries remain strings. Serialized cfg keys, values, and full snapshot validation are unchanged; invalid values are not silently replaced or migrated.

The default location list includes CharredFortress and the regular Mistlands_Giant1, and excludes BlackForest_DG_RtD, Mountain_DG_RtD, Mistlands_Giant1:dark, FortressRuins, and AshlandRuins. It is a configurable selection, not a guarantee that every ID exists in every installed game or content-mod combination. IDs not found in the active game are logged and skipped. Existing cfg selections are not automatically edited when defaults change.

## Automatic scheduling and manual execution

Enabled controls only automatic scheduling. Both true and false allow the authorized freshworld command, status queries, execution history, and the same validation. The cfg Mode accepts only GameDays and DailyTimes. Manual operation alone is expressed with Enabled=false; ManualOnly is not a supported cfg value or a legacy alias.

The default 24-day interval uses world game time. The inspected vanilla environment sets a game day to 1,800 seconds: 30 minutes, or 0.5 hours. Without sleeping, pausing, or changing time, 24 game days correspond to about 12 real hours. Time-changing mods and sleep can change that relationship. This is neither a visit timer nor a measurement of active player time.

Only fields used by the current automatic schedule are validated and included in its identity. GameDays ignores DailyTimes, DailyTimes ignores GameDayInterval, and Enabled=false ignores both. Mode must still contain a supported name. Editing unused values does not discard an accepted manual request or reset an unchanged automatic schedule.

Disabling automation drops automatic work that has not started. It preserves an accepted manual request and its captured restoration options. Configuration changes made during active work are deferred until completion, so switching automation off is not an emergency stop for the current run.

Re-enabling automation establishes a fresh baseline. GameDays waits a full interval from that point, and DailyTimes uses the next future slot. Deadlines from the disabled period are not caught up. Reopening a world with the same enabled schedule retains its world-specific baseline and history. An automatic deadline already observed and queued can survive a restart with the same schedule, but disabling automation cancels it.

DailyTimes follows the operating system time zone of the host, not a connecting client's time zone. Each slot runs all enabled stages. There is no separate morning-only resource schedule. Deadlines missed while the world was closed are not caught up on reopening it.

## Resource groups and terrain order

ResourceIds restores selected vegetation without restoring nearby terrain. TerrainResourceIds restores vegetation and terrain within ResourceTerrainRadius. The default first list is empty; the second contains copper and silver ore IDs. This preserves the default ore-and-terrain behavior while allowing trees or other vegetation to use a different policy.

For example:

~~~ini
TerrainResourceIds = rock4_copper,silvervein
ResourceTerrainRadius = 20
ResourceIds = Beech1,Birch1
~~~

The selected trees are restored without resetting the surrounding ground. The selected ores are restored together with terrain within 20 metres. Resources and ResourceSafeZones apply to both groups.

Either resource list may be empty. If both are empty, the resource stage is skipped. An ID appearing in both lists belongs to TerrainResourceIds and runs once. Duplicate IDs within a single list remain an error; accepting cross-list overlap does not weaken that validation.

The terrain group runs before the vegetation-only group. When both groups have targets and the radius is positive, execution yields two frames between them for the game's terrain and collider refresh. The following vegetation generation then uses the refreshed terrain. There is no fixed seconds-long or minutes-long delay.

A radius of 0 restores the resources in both lists without this terrain operation. It does not disable terrain work performed by zone or location restoration. Similarly, ResourceIds never acquires terrain restoration merely because a positive radius is configured.

## Zone-first supplementation

The zone stage considers zones recorded as generated, including zones that are not currently loaded. Resetting a zone marks it ungenerated so the game regenerates it on a later load. This broad first pass covers unprotected resources and locations without restoring each one individually.

When Zones=true, the initial plan captures zones retained because of base-marker protection. Each supplemental operation intersects that original set with zones still generated immediately before the operation. It does not add already-reset zones or unrelated failed or skipped zones as fallback targets. If the zone stage fails, the remaining supplements stop.

When Zones=false, resource and location supplements use the currently generated zones and apply their own ID and protection filters. There is no exposed scope selector.

The stage switches and ID lists must not be mistaken for exclusions from zone restoration. Turning Resources or Locations off disables that supplement only. A resource or location inside a zone selected by the broad first stage can still be reset there.

## Independent protection values

All three SafeZones settings accept only 0 (no protection), 1 (the marker's own zone), and 2 (the marker's zone and its eight neighbours in a 3 x 3 area). These are zone-grid choices, not metre radii. Other values, including 3 or higher in an existing cfg, are rejected and prevent new work until manually corrected. There is no automatic conversion or legacy alias.

The values remain separate because the defaults deliberately serve different purposes. ZoneSafeZones=1 leaves base-marker zones untouched by the broad reset. ResourceSafeZones=0 then allows selected resources and terrain to be restored inside those retained zones. Applying the same positive protection to both stages could leave little or no resource supplementation.

The default LocationSafeZones=0 expresses the old Force behavior without a second overlapping switch. It can remove player-built pieces within a selected location's cleanup area. ZoneSafeZones=0 bypasses protection at the larger zone-stage scope. Resource restoration does not directly select arbitrary player-built pieces for deletion, but terrain changes can alter their support.

If you choose ZoneSafeZones=1 and LocationSafeZones=1, zero location supplements are possible and expected: unprotected locations are already covered by the zone stage, and the retained base zones are also excluded from location restoration. The engine does not silently relax protection to increase restoration counts.

## Visible base-marker list and tombstones

PlayerPlacedObjects is a replacement list, not an additions-only list. Its 30 default IDs are displayed in the cfg, including piece_beehive. Players can add or remove prefab IDs directly, and an empty value disables creator-qualified markers. Removed entries are not merged back from the defaults. Existing edited lists are not automatically extended when the shipped defaults change.

A matching prefab becomes a player-placed marker only when it has the creator metadata used by placed buildings. Ordinary pieces record this through Piece.SetCreator in s_creator. A tombstone records its character/profile owner in s_owner instead; that is not a Steam account ID and does not satisfy the building-creator test.

Player_tombstone is therefore maintained separately and tested without the building-creator condition. Clearing PlayerPlacedObjects leaves that separate marker in place. However, **both kinds of marker protect zones only when the applicable stage's SafeZones value is positive**. At 0, tombstone marker protection is bypassed too. This is not a promise that tombstone objects themselves cannot be deleted.

Marker-based protection also does not guarantee preservation of structures lacking a marker, every player-occupied zone, or all pieces near the boundary of a location's cleanup area. Broader structural protection would require a separate engine change rather than a cfg rename.

## Saving, execution, and failure handling

Execution is sequential: save and wait, zones, resources with terrain, resources without terrain, locations, then completion. A save error or timeout stops the run before restoration. An error in a later stage stops subsequent stages but does not undo completed changes.

The save before restoration is mandatory. There is no additional save request afterward; normal game autosaves and a normal shutdown save persist the result. If the process crashes before the next save, restoration changes can be lost even when the separate execution history records completion. Execution history is not a world-file backup, and no automatic rollback is provided.

Manual requests use a snapshot of host restoration settings captured when accepted. Current connection, world identity, and administrator authority remain checked while a request is queued or active. Revocation or session loss stops the request; already-applied changes remain. Old-session manual requests are not resumed after a restart or reconnect.

The same-process local host can issue a command without an administrator entry. Every remote request requires the server's verified administrator identity, including a separate client connected to a dedicated server on the same PC. Client admin flags, client-provided identity claims, and a shared IP address do not establish authority. The command does not accept per-run ID or protection overrides.

File changes are reloaded on the host's main update path. Reload temporarily suppresses BepInEx's automatic config saving so partially read settings cannot overwrite the rest of the file. A running operation retains its snapshot and defers the reload until completion. No ServerSync or remote Configuration Manager write path is included.

Execution state is separated by world UID and retains the latest 128 runs. A durable run ID is recorded before mutations. Failed and interrupted runs are not automatically retried. Corrupt state or failed state writes stop new maintenance for that world session.

## Internal controls and performance limits

| Internal policy | Value and purpose |
|---|---|
| Schedule check | Once per second; manual requests do not wait for this poll. |
| Startup grace | 30 seconds after world readiness, once per session. |
| Zone attempts | At most 64 attempts before yielding. |
| Frame budget | Check for 8 ms elapsed between zone operations. |
| Save wait | Abort after 180 seconds without observed completion. |
| Base-marker cache | Reuse for up to 10 seconds. |

The startup grace is an extra delay, not a replacement for world readiness, save, pause, and concurrency checks. It is not applied afresh before every run.

The frame budget cannot interrupt a single synchronous zone or dungeon operation. Individual operations and some preparation or cleanup can exceed 8 ms, so it is not a hard limit on total frame time. Removing the tuning options does not establish a measured performance improvement. No in-game benchmark has been completed.

There is no per-location visit tracking and no per-frame distance scan over all locations. Game API and terrain-format compatibility still need review after Valheim updates.

## Historical option correspondence

This table explains how the earlier larger configuration was simplified. It is design history, **not an automatic migration map**. Current cfg values are not read from old aliases.

| Earlier option | Current policy |
|---|---|
| General.Enabled | Automatic scheduling only; manual commands remain available. |
| General.StartupDelaySeconds | Internal 30-second startup grace. |
| Schedule.Mode | General.Mode with GameDays or DailyTimes. |
| Schedule.DailyTimes | General.DailyTimes. |
| Schedule.TimeZone | Host OS local time zone. |
| Schedule.VegetationDailyTimes | All enabled stages run at every slot. |
| Schedule.GameDayInterval | General.GameDayInterval, default 24. |
| Schedule.RunMissedOnWorldStart | No catch-up for unobserved offline deadlines. |
| Pipeline.SaveBefore | Mandatory save before restoration. |
| Pipeline.SaveAfter | No extra save after restoration. |
| Zones.Enabled | Reset.Zones. |
| Zones.SafeZones | Protection.ZoneSafeZones. |
| Protection.PlayerPlacedObjects | Visible, complete editable marker list. |
| Protection.Objects | Separate built-in Player_tombstone marker. |
| Vegetation.Enabled | Reset.Resources. |
| Vegetation.Ids | ResourceIds or TerrainResourceIds, selected explicitly. |
| Vegetation.TerrainRadius | Reset.ResourceTerrainRadius. |
| Vegetation.SafeZones | Protection.ResourceSafeZones. |
| Locations.Enabled | Reset.Locations. |
| Locations.Ids | Reset.LocationIds. |
| Locations.Force | Use LocationSafeZones=0 for the same protection bypass. |
| Locations.SafeZones | Protection.LocationSafeZones. |
| Performance.MaxZonesPerFrame | Internal 64 attempts. |
| Performance.FrameBudgetMilliseconds | Internal 8 ms check. |
| Performance.SaveTimeoutSeconds | Internal 180-second wait limit. |

Older ManualOnly and AdditionalPlayerPlacedObjects settings have no compatibility aliases. Use Enabled=false for manual operation only and edit the complete PlayerPlacedObjects list directly. If an older resource selection was intended to restore terrain, place those IDs in TerrainResourceIds explicitly.

## Validation boundary

Configuration preserves serialized text, using strings or the lossless choice wrapper, and validates it explicitly so malformed booleans, numbers, and IDs are not silently replaced with a more permissive default. Resource lists may be empty; location lists may not. Within-list duplicates, wildcards, and command arguments are rejected. Unknown but syntactically valid game IDs are logged and skipped at the relevant stage.

Automated verification covers configuration, scheduling, automatic enable/disable transitions, manual requests, permissions, hot reload, stage protection, initial saves, supplemental scope, resource groups, terrain, and game boundaries. These checks do not replace actual world testing. Single-player, local-host, and dedicated-server restoration have not yet been validated in a running game, and no performance benchmark is claimed.
