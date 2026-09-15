# Changelog

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
