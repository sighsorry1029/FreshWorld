# FreshWorld

FreshWorld restores explored areas of your Valheim world on a schedule or with one command. By default, it resets zones away from player builds and restores mined copper and silver with the surrounding ground.

![Vegetation growing back around player-built structures during a FreshWorld reset](https://i.ibb.co/WS2DPzB/freshzones.gif)

*Resources can return near a protected base while its buildings stay in place.*

![Location reset near player-built structures](https://i.ibb.co/TBdLgKyB/locationradiusaresafe.gif)

*With location resets enabled, `LocationSafeZones = 0` allows a site to regenerate inside a base.*

![Player-built pieces inside a location's reset radius](https://i.ibb.co/5hRF2ZXf/freshlocationforce.gif)

*Buildings inside that radius can be removed when the location regenerates.*

![Reset targets around a base marker with SafeZones set to 0](https://i.ibb.co/5W9fRnbD/freshworld-safezones-0.png)

*`0` ignores base markers for resource and location resets. Whole-zone resets require `1` or `2`.*

![A single protected grid cell with the base marker in its center](https://i.ibb.co/39WtpbzL/freshworld-safezones-1.png)

*`1` protects the zone containing the marker.*

![A three-by-three block of protected zones around a base marker](https://i.ibb.co/ZzLt1qpv/freshworld-safezones-2.png)

*`2` protects the marker's zone and its eight neighbors.*

## Getting started

Install FreshWorld and BepInExPack Valheim on your single-player game, local host, or dedicated server. Players can join without the mod. Start the world and FreshWorld runs automatically every **24 game days** (about 12 real hours without sleeping or pausing).

Edit `BepInEx/config/sighsorry.FreshWorld.cfg` on the host. Changes apply to the next run. Server admins with FreshWorld and Configuration Manager on their client can also edit settings in-game.

Use `freshworld status` to check progress or the last result. Use `freshworld` to start a reset now. Both work in the host's F5 console, the dedicated server console, or authenticated RCON. To use F5 as a remote admin, install FreshWorld on your client. If F5 is unavailable, add `-console` to the game's launch options.

## What gets restored?

FreshWorld saves the world before making changes. It runs the enabled stages in this order.

| Stage | Default | What happens |
| --- | --- | --- |
| Zones | On | Previously generated zones can reset even when nobody is nearby. Their terrain, vegetation, and locations regenerate the next time the game loads them. |
| Resources | On | In eligible zones left standing, the default `TerrainResourceIds` restores copper and silver plus the ground within 20 m of each deposit. Add trees or other vegetation to `ResourceIds` for regrowth alone. |
| Locations | Off | Enable `Locations` and list prefab names in `LocationIds` to restore specific sites that the zone stage skipped. A dungeon in a fully reset zone can regenerate with this switch off. |

For resource or location restoration alone, set `Zones = false`. The enabled stages then check generated zones using their own target lists and protection settings.

## Protecting players and bases

Players protect their zone and its eight neighbors from direct resets. Once recorded, this protection lasts until the run ends. A player-built piece such as a workbench also marks its zone while the base is empty. A lone campfire (`fire_pit`) is excluded by default.

| Setting | Default | Protection around a base marker |
| --- | --- | --- |
| `ZoneSafeZones` | `1` | `1` protects the marker's zone. `2` protects the surrounding 3×3 area. |
| `ResourceSafeZones` | `0` | `0` allows resource and terrain restoration in base zones. Use `1` or `2` to protect them. |
| `LocationSafeZones` | `0` | `0` allows a selected location to reset inside a base. Use `1` or `2` to protect builds in its footprint. |

**If you enable location resets, check `LocationSafeZones` first.** At `0`, a location reset can remove player buildings inside its radius even when the zone reset spared the base. Resource restoration can also change the ground supporting a building, including across a zone border. Player tombstones and their zones have separate protection through `AlwaysProtectedPrefabs`.

## Scheduling

- For manual runs only, set `Enabled = false`.
- For fixed times, set `Mode = DailyTimes` and enter host-local times such as `DailyTimes = 05:35,17:35`.
- For smaller automatic runs, lower `AutomaticResetPercent` from its default of `100`. At `20`, each run draws a fresh sample from eligible areas. Some may still be untouched after five or ten runs. Manual `freshworld` runs use the full eligible set and follow the same protection rules.

## Compatibility

- **Epic Loot** treasure hunts are protected by default. Enable `EpicLootBountyProtection` to protect bounties too.
- **Path of Valheiman** altars, active Blight objectives, Halls dungeons, merchants, and active treasure caches are protected by default. Use `freshworld pov` to restore missing altars and check Halls entrances. For entrances with terrain pads, visit the original site so PoV can restore the gate. [Recovery guide](https://github.com/sighsorry1029/FreshWorld/blob/main/docs/path-of-valheim-compatibility.md)
- **Odin's Hollow** sky dungeons reset with their entrance. Clearing a sky dungeon preserves the ground below neighboring zones.
- **Expand World Data** vegetation keeps its custom placement settings. Use one prefab per entry for resources FreshWorld should restore individually.
- **Upgrade World** is optional. If its save patches conflict with FreshWorld, `freshworld status` reports why maintenance is disabled.

For multi-zone locations, Jotun invasion objectives, exclusions, and all command options, see the [restoration guide](https://github.com/sighsorry1029/FreshWorld/blob/main/docs/restoration-guide.md).

FreshWorld adapts world restoration code from Jere Kuusela's Upgrade World. [Third-party notices and license](https://github.com/sighsorry1029/FreshWorld/blob/main/THIRD_PARTY_NOTICES.md)
