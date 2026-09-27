# Changelog

## 1.1.1

- Narrowed Jotun invasion protection to the 1x1 zones containing `BlackIce_Core` or `BlackIce_Core_outer`, including their terrain tiles and border repairs. Both objects remain protected from direct and recursive deletion.
- Made objective protection independent of event records. Remaining ice protects its own zone; observed zones stay protected until the current run ends. Missing or invalid event metadata no longer stops maintenance.
- Added `freshworld invasions` to inspect active events and objective objects without changing the world. Results are shown to the requester and written to the host log.
- Added `TheHole01` and `MorkBorg` to the default location list. Existing cfg selections are preserved, and location restoration remains disabled by default.

## 1.1.0

- Restricted `ZoneSafeZones` to 1 or 2. Existing values of 0 must be changed before maintenance can run; resource and location protection still allow 0.
- Added server-synced `AlwaysProtectedPrefabs`, defaulting to `Player_tombstone`. Listed objects and their own zones are protected regardless of SafeZones or PieceBlacklist, without requiring a Piece component or creator.
- Protected listed objects from direct and recursive deletion, and their terrain tiles from restoration and border repairs, including requests from neighboring zones. Native placement from neighboring zones can still cross the boundary.
- Kept observed zones protected until the run ends and applied config changes to the next run. Existing tombstone marker protection and EpicLoot settings remain unchanged.

## 1.0.10

- Protected active Jotun invasion areas from direct resets, terrain restoration, and border repairs, regardless of SafeZones settings.
- Prevented FreshWorld from deleting invasion cores and their outer ice. Newly started invasions are detected during maintenance; observed areas stay protected until the run ends.
- Stopped maintenance when persistent event data is unavailable or invalid. This update does not restore previously lost invasion objectives.
- Updated the BepInExPack dependency to 5.4.2351.

## 1.0.9

- Added independent, server-synced `EpicLootBountyProtection`, disabled by default. When enabled, pending bounty controllers, targets, and their adds protect their own zones and cannot be deleted by FreshWorld.
- Simplified run option selection while preserving accepted manual settings and current automatic settings.

## 1.0.8

- Protected unfound EpicLoot treasure chests and pending treasure controllers in their own zones, with a server-synced `EpicLootProtection` switch enabled by default.
- Skipped zones that remain unavailable after a bounded load wait, reported their status, and cleaned up deferred zone releases without aborting the rest of maintenance.

## 1.0.7

- Fixed a startup error caused by Harmony treating ordinary cleanup methods as patch hooks.
- Embedded ServerSync so administrators can apply Configuration Manager edits to the server while keeping ordinary clients optional.
- Protected player-built Pieces automatically and replaced `PlayerPlacedObjects` with `PieceBlacklist`, defaulting to `fire_pit`. Existing whitelist entries are not converted.
- Kept tombstones as built-in protection markers, independent of the Piece blacklist.
- Simplified internal reset tracking without changing restoration order or stage protection settings.

## 1.0.6

- Ensured completely cleared chunks replace their old chunk files during normal saves, preventing deleted world objects from returning and stacking after a reload.

## 1.0.5

- Added `Cart` to the default `PlayerPlacedObjects` list.

## 1.0.4

- Protected each player's zone and its eight neighbors from direct resets for the rest of a run.
- Added `Raft`, `Karve`, `VikingShip`, and `VikingShip_Ashlands` to the default `PlayerPlacedObjects` list.

## 1.0.3

- Simplified reset tracking and removed redundant candidate and state handling.
- Reduced allocations during large zone and world-object resets.
- Hardened plugin shutdown cleanup and pinned Harmony shutdown overloads.

## 1.0.2

- Added `freshworld` command support for dedicated server consoles and authenticated RCON tools.
- Kept connected-player administrator checks and blocked remote commands from inheriting server-console authority.

## 1.0.1

- Updated for Valheim 1.0.7.
- Updated zone coordinates, world object lookup, console command registration, and terrain refresh handling for the new game APIs.
- Preserved portal cleanup and prevented zone-coordinate wrapping at world boundaries.

## 1.0.0

- Initial release.
