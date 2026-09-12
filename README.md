# FreshWorld 1.0.2

FreshWorld restores generated zones, selected resources and terrain, and selected locations in Valheim. It supports single-player worlds, local hosts, and dedicated servers. The cfg has **15 options in three sections** and defaults to automatic restoration every **24 game days**.

This source targets Valheim 1.0.7. Builds made for 0.221.x must be replaced on both the host and any administrator clients using FreshWorld commands.

![](https://i.ibb.co/WS2DPzB/freshzones.gif) <br>
player buildings are protected while vegetations would refresh <br>
![](https://i.ibb.co/TBdLgKyB/locationradiusaresafe.gif) <br>
locations can be forced to reset even if it have player buildings within radisu, if you turn location reset on config <br>
![](https://i.ibb.co/5hRF2ZXf/freshlocationforce.gif) <br>
another example of location being forced to reset even with the player buildings within radius <br>

![](https://i.ibb.co/5W9fRnbD/freshworld-safezones-0.png) <br>
![](https://i.ibb.co/39WtpbzL/freshworld-safezones-1.png) <br>
![](https://i.ibb.co/ZzLt1qpv/freshworld-safezones-2.png) <br>
check the image for better understanding of reset

## Installation and configuration

1. Install BepInExPack Valheim on the PC or dedicated server that opens the world.
2. Copy FreshWorld.dll from the ZIP's root into BepInEx/plugins/FreshWorld/. Create that folder if needed, replace the previous DLL, and avoid duplicate copies.
3. Launch the game or server to create BepInEx/config/sighsorry.FreshWorld.cfg. All 15 settings and their defaults are shown below.

Ordinary connecting players do not need FreshWorld. A remote administrator who uses its commands needs the same FreshWorld version on their client as on the host. The host performs all world changes and scheduling.

Saving the host cfg reloads it without restarting. An accepted manual request keeps its captured restoration settings. Changes made while work is running are applied after it finishes. Invalid settings prevent new work until corrected. A local Configuration Manager can use this same path when it saves changes to the file; client configuration is not synchronized to the server.

Configuration Manager is an optional editor. FreshWorld provides toggles for Enabled, Zones, Resources, and Locations; number inputs for GameDayInterval and ResourceTerrainRadius; and choices for Mode and each SafeZones setting. Mode and SafeZones use Configuration Manager's standard popup: choices overlay the settings, the background is dimmed and disabled, and rows stay in place. DailyTimes and prefab lists remain text fields. Enabled appears as **Automatic Runs** in the editor; its cfg key remains Enabled. Stored values and validation are unchanged, including preservation of invalid cfg text until corrected. These controls add no mod dependency. Their appearance and interaction have not yet been verified in a running game.

The BepInEx plugin ID is sighsorry.FreshWorld, which determines the cfg filename. An older fresh_world.cfg is not imported automatically. Existing cfg values are not converted or updated to new defaults; compare them with the settings below and copy your intended values into the active file. There are no legacy aliases or migration rules. For manual operation only, use Enabled=false with Mode=GameDays; ManualOnly is not a supported Mode.

## Restoration order

Each run follows this sequence:

~~~text
Save and wait for completion
-> Reset zones
-> Restore resources with surrounding terrain
-> Restore resources without terrain changes
-> Reset selected locations
-> Finish
~~~

Disabled stages are skipped. A stage must finish before the next one starts, and a failure stops the remaining stages. When both resource groups have targets and the terrain radius is positive, FreshWorld yields two frames between them so the game can refresh terrain and collision data. There is no fixed delay in seconds or minutes between stages.

The initial save is mandatory. A save failure or the 180-second save timeout prevents restoration from starting. FreshWorld does not request another save at the end: normal autosaves and a normal shutdown save persist the result. A crash before the next save can lose restoration changes even if the separate execution record says the run completed. FreshWorld does not create a separate backup or automatically restore one.

## General: automatic scheduling

~~~ini
[General]
Enabled = true
Mode = GameDays
GameDayInterval = 24
DailyTimes = 05:35,17:35
~~~

**Enabled controls automatic scheduling only. Manual commands work with either value.** Turning it off cancels automatic work that has not started, but preserves accepted manual requests. It does not interrupt running work; configuration reload remains deferred until that work finishes.

| Mode | Schedule |
|---|---|
| GameDays | Run after each GameDayInterval measured from the world's schedule baseline. |
| DailyTimes | Run at the listed HH:mm times in the host operating system's local time zone. |

A normal vanilla game day lasts **30 real minutes, or 0.5 hours**. The default 24-day interval is about 12 hours without sleep, pauses, or game-time changes. FreshWorld measures world game time, so sleep and time-changing mods affect the actual elapsed time. It does not measure time since a player visited an area.

Reopening a world with the same enabled schedule preserves its baseline. Changing the active schedule starts a new baseline. Turning automation off and back on also starts fresh: GameDays waits a full interval, while DailyTimes waits for the next scheduled time. Deadlines from the disabled period are not caught up.

DailyTimes does not catch up times missed while the world was closed. An automatic run already observed and queued in an earlier session can remain pending if automation stays enabled with the same schedule. Disabling automation cancels that pending run too.

Only active schedule fields are used. GameDays ignores DailyTimes; DailyTimes ignores GameDayInterval; Enabled=false ignores both. Editing unused fields does not reset an accepted manual request. Mode must still be GameDays or DailyTimes even when automation is disabled.

Every run includes all enabled restoration stages. There is no separate daily slot list for resources: with DailyTimes=05:35,17:35, both slots include resources when Resources=true.

## Reset: restoration targets

~~~ini
[Reset]
Zones = true
Resources = true
TerrainResourceIds = rock4_copper,silvervein
ResourceTerrainRadius = 20
ResourceIds =
Locations = false
LocationIds = Hildir_crypt,Hildir_cave,Hildir_plainsfortress,SunkenCrypt4,Crypt2,Crypt3,Crypt4,MountainCave02,Mistlands_Giant1,Mistlands_Excavation1,Mistlands_DvergrTownEntrance1,Mistlands_DvergrTownEntrance2,Mistlands_DvergrBossEntrance1,CharredFortress
~~~

Zones, Resources, and Locations switch their respective stages on or off without erasing the ID lists. The extra location stage is disabled by default; set Locations=true to enable it. Existing cfg values are preserved.

| Resource list | Behavior |
|---|---|
| ResourceIds | Restore selected resources without restoring their surrounding terrain. Empty by default. |
| TerrainResourceIds | Restore selected resources and terrain within ResourceTerrainRadius metres. Defaults to copper and silver. |

For example, ResourceIds=Beech1,Birch1 restores those trees without resetting the surrounding terrain. Keep rock4_copper,silvervein in TerrainResourceIds to restore those ores together with their nearby terrain.

Resources and ResourceSafeZones apply to both groups. Either resource list may be empty; if both are empty, the resource stage is skipped. An ID listed in both groups runs once in TerrainResourceIds. Duplicates within a single list, wildcards, and command arguments are rejected.

ResourceTerrainRadius applies **only to TerrainResourceIds**. A value of 0 restores resources from both lists without this terrain operation. It does not disable terrain changes performed by the zone or location stages.

LocationIds uses exact location IDs, with optional :variant suffixes. The default list contains 14 IDs. An empty location list is invalid; use Locations=false to disable that stage while keeping its selection. Resource or location IDs unavailable in the current game are logged and skipped. If no selected IDs are recognized, their stage or group is skipped. The default selection is not a guarantee that every ID exists in every game or mod setup.

### Which zones are affected?

The zone stage considers **all previously generated zones**, not just zones currently loaded around players. It marks reset zones as ungenerated; the game recreates them when they are loaded again.

With Zones=true, resource and location supplements use only zones protected by base markers in the initial plan that are still generated immediately before the supplemental operation. Zones already reset are not included again, and other failed or skipped zones are not added as fallback targets. A zone-stage failure stops the supplements.

With Zones=false, supplements consider all currently generated zones, then apply their own ID and protection filters. This scope is fixed and has no cfg selector.

All stages also exclude player-occupied target zones using the run's player protection described below. This exclusion does not expand the supplemental candidate set.

Resources=false and Locations=false disable only their supplemental stages. They do not exclude resources or locations from the earlier zone reset. Likewise, the resource and location ID lists do not restrict the zone stage's targets.

## Protection: players, base markers, and stage boundaries

**Player-occupied target zones are always protected, including when SafeZones=0.** After the initial save finishes, FreshWorld records the zones of active players known to the hosting server. It adds their current zones again immediately before each target attempt or retry. Once observed, a zone stays excluded from direct zone resets, resource-and-terrain resets, resource-only resets, and location resets for the remainder of that run, even if the player leaves. The next run starts with a fresh set. This works in single-player, local-host, and dedicated-server worlds and adds no cfg option or location-visit tracking.

This protection skips direct targets; it does not protect terrain across zone boundaries. A resource or location terrain radius from a neighboring target can still reach an occupied zone, and a neighboring zone reset can still repair shared terrain borders there. SafeZones marker protection has the same boundary limitation.

~~~ini
[Protection]
ZoneSafeZones = 1
ResourceSafeZones = 0
LocationSafeZones = 0
PlayerPlacedObjects = blastfurnace,bonfire,charcoal_kiln,fermenter,fire_pit,forge,guard_stone,hearth,piece_artisanstation,piece_bed02,piece_beehive,piece_brazierceiling01,piece_groundtorch,piece_groundtorch_blue,piece_groundtorch_green,piece_groundtorch_wood,piece_oven,piece_spinningwheel,piece_stonecutter,piece_walltorch,piece_workbench,portal,portal_wood,smelter,windmill,piece_chest,piece_chest_blackmetal,piece_chest_private,piece_chest_treasure,piece_chest_wood
~~~

The three SafeZones settings independently control additional base-marker protection and accept only these values. They do not disable player-occupied target protection:

| Value | Protection for that stage |
|---|---|
| 0 | No protection: ignore protection from base markers. |
| 1 | Marker zone: protect the zone containing a marker. |
| 2 | 3 x 3: protect the marker's zone and its eight neighbors. |

Other values, including 3 or higher in an existing cfg, are invalid and prevent new work until corrected. FreshWorld does not convert them automatically.

**The default LocationSafeZones=0 can remove player-built pieces inside a selected location.** It replaces the old Force behavior; there is no separate Force option. ZoneSafeZones=0 disables marker protection for the entire zone-reset stage, which has a different and broader scope.

ResourceSafeZones=0 allows the selected resource supplements in retained base zones. The resource stage does not directly select arbitrary player-built pieces for deletion, but terrain restoration can change the ground supporting them.

If you choose ZoneSafeZones=1 and LocationSafeZones=1, zero location supplements can be normal: the zone stage already covers unprotected locations, and the remaining base zones are also protected from location restoration. FreshWorld does not automatically lower protection to increase the result count.

PlayerPlacedObjects is the **complete editable list** of prefabs that act as markers when they have player-creator metadata. Its 30 default entries are shown above so you can see which workbenches, portals, chests, beds, fires, beehives, and processing stations block zone resets. Add or remove IDs directly. An empty value disables these creator-qualified markers. Removed entries are not silently restored from a built-in list.

Player_tombstone is a separate built-in marker. Tombstones store a character/profile owner ID in s_owner, while placed buildings use s_creator. FreshWorld therefore checks tombstones without the building-creator requirement. Clearing PlayerPlacedObjects leaves the separate tombstone marker in place.

**All markers, including tombstones, protect zones only when that stage's SafeZones value is greater than 0.** SafeZones=0 bypasses tombstone marker protection too; it does not grant tombstones deletion immunity. Marker protection also does not automatically preserve every structure or every zone around a player. Structures without a marker and a location's cleanup radius still matter.

## Manual commands and permissions

Use the game's F5 console. If it is unavailable, add -console to the game's launch options. Native autocomplete completes the command name and offers only status as its argument. FreshWorld does not require enabling devcommands.

| Command | Action |
|---|---|
| freshworld | Request one restoration using the current host cfg. |
| freshworld status | Show running or pending work, or the applied policy and last recorded result while idle, without starting restoration. |

Only these two command forms are accepted. Reset settings and protection rules are explained above and in the cfg descriptions.

Manual requests do not wait for the automatic schedule polling interval. They still require a ready world, completion of the initial 30-second grace period, an unpaused host, no conflicting save or maintenance, a valid cfg, and valid authority. The grace period starts once when the world becomes ready; it is not added before every run.

An existing pending or active request is not overwritten. Each requester is limited to one command per second; repeated commands within that second, including status, are silently ignored. The requester receives acceptance, waiting reasons, and completion or failure messages.

The person hosting a world inside their own game process can run commands without an administrator entry. A dedicated server's own console can also run them without a connected player. This includes authenticated RCON tools that execute the registered command inside the server process without a Valheim player connection. The RCON provider controls its own authentication; FreshWorld receives no RCON identity and continues to validate the active server, world, session, and run state.

Every requester connected as a game client must be an administrator verified by the server's adminlist.txt. This includes a separate game client connected to a dedicated server on the same PC. Client administrator flags, claimed Steam IDs, and matching IP addresses are not trusted as authorization.

Manual requests belong to the current world, connection, and verified authority. Disconnecting, losing administrator permission, or changing worlds cancels queued or active manual work. Changes already made are not rolled back or automatically retried. The command cannot override the captured IDs or protection values. A request from an earlier session must be submitted again after reconnecting or restarting.
