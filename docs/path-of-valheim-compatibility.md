# Path of Valheiman compatibility and recovery

FreshWorld protects PoV objectives. The manual `freshworld pov` command restores missing Warden and Power altars and checks recorded Halls entrances. PoV handles gates that need terrain pads. Natural azurite and runic ore deposits can renew when a fully reset zone generates again.

## Protection

- `[Path of Valheiman] AltarProtection=true` protects `PT_WardenAltar` and `PT_PowerAltar` in their own zones.
- `[Path of Valheiman] BlightProtection=true` enables the active-event rules below.
- `[Path of Valheiman] DungeonProtection=true` protects Halls entrances, personal sessions, and saved dungeon runs as described below.
- `[Path of Valheiman] MerchantProtection=true` protects `PoV_Ragn`, `PoV_Sigrid`, and `PoV_Wanderer` in their current 1x1 zones. PoV still controls visits, movement, departure, and stock; FreshWorld does not recreate them.
- `[Path of Valheiman] TreasureProtection=true` protects `PoV_TreasureCache` with a nonempty `pt.tmap.cache` and cache state `pt.enc.state=0` (closed, including an absent state key) or `2` (dispensing). State `3` is empty. The world treasure record's opened flag does not mean loot dispensing has finished. Observed cache IDs and zones remain protected until the run ends, even if PoV finishes dispensing or clears the map ID. Empty caches receive no protection from this option on the next run. No inventory deserialization or new world scan is needed for merchant/cache classification.

All five protection options default to true, use ServerSync and host cfg hot reload, and apply on the next run. Other protection rules still apply when an option is disabled.
- A valid `PT_CorruptionZone` with a nonempty `pt_blight_uid` and state 0 (running), 1 (ready), or 2 (summoned) identifies an active Blight.
- Its `PT_BlightAltar` links through `pt_blight_uid`. Its `PT_RuneMonolith` links through `pt_blight_zone`. Borrowed natural objects qualify even without `pt_blight_own`.
- Warden bosses link through `pt_blight_zone` and `pt_blight_warden=true`. This includes PoV's fallback boss prefab. Ordinary Blight mobs and azurite veins are not protected objectives. PoV can replenish its event veins.

Only the objects' own zones are reserved, not the event's full circle. All reset stages, direct and recursive FreshWorld deletion, terrain restoration, and border repair use this policy. It works from saved ZDOs without loaded scenes, a Piece component, creator metadata, or an online player.

Each run indexes controllers after the initial world save. Known controllers are checked live. Missing or inactive links can refresh the index on a later frame; multiple orphan objects share the same frame's scan. The policy retains active UIDs, observed object IDs, and protected zones until the run ends. A new run starts with empty state. Closed or missing controllers therefore do not permanently protect leftovers unless another protection rule applies.

PoV creates its controller before its dependent objectives in a synchronous call. Arrivals after a coroutine yield are detected on the next target check. Arbitrary third-party callbacks that create a new controller after a negative lookup within the same frame are outside the verified lifecycle.

Only FreshWorld's own deletion path is guarded. PoV can still close events, remove its objects, and restore borrowed natural objects. No game-wide destruction patch is added. Native placement outside a protected zone may still extend geometry across its boundary.

## Halls dungeon protection

`DungeonProtection=true` protects the zone of each `PoV_HallsGate`, `PoV_HallsPad`, `PoV_HallsPadEdge`, and `PoV_HallMapSession`. A pad across a zone boundary protects that neighboring zone. A surviving pad keeps its zone protected even if the gate is gone. Sessions contain each character's map board, invested rooms and runes, pending room refunds, transaction history, and run reference.

Both `PoV_HallMapRun` and `PoV_HallsRun` are read from saved ZDOs, including unloaded sectors. Their controllers can follow players, so FreshWorld reads the immutable `hm.slot` or `h.slot` as the interior origin. Map runs use X ±320m, Z −130m to +360m; legacy Halls use X/Z ±145m. Both include Y ±60m around the slot. Linked mobs, chests, and pickables are retained independently of position. Initially observed loose rewards retain their identity for the maintenance run even if they later move out of the slot.

This is object retention, not a reservation of every ground sector below the dungeon. All reset stages check retained interiors before deleting objects. A native dungeon at Y=5000 can therefore reset while a PoV interior at Y=4400 in the same XZ sector survives. Entrance and session zones remain excluded. Existing player 3x3, base, tombstone, and other zone protection policies still apply regardless of height; the ground is eligible, not guaranteed to reset.

All saved run controllers remain under PoV ownership, including completed runs. FreshWorld does not rewrite sessions, reward claims, room investments, or pending refunds. PoV still completes runs, grants rewards, deletes its content, and reuses slots normally. FreshWorld adds no expiry timer or patch to PoV's own deletion calls.

Initial discovery shares the saved-object snapshot. During maintenance, cached accessors read PoV's small loaded-run lists and native data revisions refresh parsed state, including same-frame relaunches. Cached map and legacy layouts are checked by placement role against the known slot envelope. Missing optional layouts cached as null are allowed. Unsupported part or anchor coordinates stop maintenance before the affected deletion. Arbitrary third-party in-place edits to layout caches, runtime DLL replacement, and untagged objects outside the verified slot envelope are unsupported.

If PoV or Odin's Hollow is absent, FreshWorld looks for saved evidence during its normal world scan. A clean world skips that mod's dungeon handling. Recognized PoV objectives and runs still follow their configured protections after mod removal. Tagged Odin's Hollow content stays intact. A saved native Odin's Hollow entrance that requires the missing mod to resolve its rooms stops maintenance before any reset.

Zone cleanup records explicitly retained survivors and requires all planned deletions to finish before releasing generation state. Unknown residual objects, changed ownership, or newly retained content stop regeneration. Manually loaded zones with retained dungeon content are handed back to normal streaming rather than forcing their controllers to unload. Interrupted cleanup can leave completed changes; this is not a transaction or rollback.

Compatibility checks use actual registry/getter signatures, reach constants, layout fields, saved schema, and coordinates rather than a mod version or whole-DLL hash allowlist. Game saves, connections, and PoV's own lifecycle remain unpatched. Protection does not restore lost sessions, items, or run progress. The separate manual command can restore a missing entrance only when valid placement records remain.

The Halls contract was inspected in the original PoV 6.0.6 DLL (SHA-256 `5E2F52FE93691DE69C0BDE7CF0BADD4B62BE34111D3A382304C3A7A86372A734`). The related Odin's Hollow integration was checked against original 2.2.4 code and embedded prefab data. It resolves the fixed entrance and saved room geometry as oriented 3D boxes, adds ID-owned and linked contents, and leaves surrounding surface generation state unchanged. Its normal Ghost proxy placement and later owner-side AutoGenerate remain responsible for new interiors. The integration does not force either mod to generate overlapping dungeons or solve their original placement collisions.

Source-linked tests exercise same-sector ground/native-sky deletion with PoV preservation, saved and live runs, loose rewards, invalid custom layouts, OH multi-sector content and links, rotated geometry, protected preflight, late residue, and retained scene cleanup. Separate generation tests check that extra cleanup completes before terrain or location placement. These use simulated engine boundaries. Original-DLL metadata checks and successful Debug compilation do not establish actual host/dedicated gameplay compatibility. Still test repeated resets, unload/reload, save/restart, owner changes, dungeon entry, combat, rewards, and reconnect in game.

## Restoring missing altars and Halls entrances

Run `freshworld pov` from an authorized local host, server administrator client, or dedicated console/RCON. There is no additional `repair` argument. The operation is manual: neither automatic resets nor the plain `freshworld` command invoke it. PoV must be installed and ready on the host.

**Halls entrances with terrain pads.** On PoV 6.0.15, visit the original site and leave its zone loaded so PoV can detect a missing natural entrance and choose a suitable position and heading. Its saved coordinates omit the original heading. FreshWorld reports the missing gate and leaves the gate, terrain pads, and site record to PoV. Recovery may place the entrance nearby. The command can still restore both types of altar in the same run. Older Halls implementations without seating use the recorded-entrance recovery below.

The host reads PoV's recorded coordinates and checks saved ZDOs in active and inactive sectors. It does not generate or load zones. A world save must finish successfully before any creation; a no-op does not request a save. Each missing candidate is checked again after saving and immediately before creation. New objects are persistent, server-owned ZDOs. Results and skipped positions appear in the requesting console and the host log.

Matching rules:

- `PT_WardenAltar`, `PT_PowerAltar`, and recorded natural `PoV_HallsGate` entrances without terrain seating are eligible for direct restoration. Coordinates must come from PoV world records.
- For altars, an existing object of the same prefab within 2m horizontally counts as present, regardless of height. This tolerates PoV's rounded coordinate storage and includes inactive objects. A same-prefab object farther than 2m but within 30m is ambiguous and is skipped. Exact duplicate altar records are deduplicated.
- For Halls, any saved gate with the same `h.gsite` anywhere in the world counts as present, including a moved gate. A gate with a different or missing identity within 30m is ambiguous. Distinct nearby coordinate records for the same prefab also block creation.
- Malformed or out-of-range records are skipped. Missing PoV read contracts, unready data, or unavailable persistent prefabs stop repair. Save refusal or save failure prevents creation.
- Existing or moved objects are not deleted, repositioned, or merged. If an existing object is unusable, inspect it separately instead of expecting this command to replace it.

PoV stores Warden records as `pov_warden_altars_2_<X>_<Z>` (also reading `pov_warden_altar_1_`) and Power records as `pov_power_at_<X>_<Z>`. `pov_power_at_done_1` is a completion marker, not coordinates. FreshWorld checks the installed plugin's public `WorldKeys.Lines()` and `Synced` contracts, rather than pinning its version. Reflection metadata lives only for this repair operation. Runtime DLL replacement is unsupported.

Halls repair requires a unique, recognized row in `pov_hallssites` and its corresponding `pov_hallsgate_<site>` placement record. Unplaced sites, unknown schemas, conflicting records, unsupported biomes, and manual random-ID gates are skipped. The record's X/Z need not lie in the original site zone: PoV can place an entrance beside another dungeon in a neighboring sector. FreshWorld preserves `h.gsite`, `h.gtheme`, `h.gvar`, and the recorded `pt.enc.lvl`. It does not call `KnownSites`, `SpawnAt`, or native generation methods, which can change sites, identities, or world keys. Identity lookup scans saved ZDOs during this manual command; no permanent index or per-frame scan is added.

The restored X/Z matches PoV's rounded coordinates. Original height and rotation are not stored there: FreshWorld uses procedural height and a stable rotation. Halls' normal loading code aligns the entrance to loaded terrain; its direction and nearby return position may differ. Inspect entrance interaction and return placement, especially on edited terrain. Existing discovery and map records remain unchanged. Stale/manual pins or lost server records cannot supply missing placement records.

Warden shard/blood deposits on a deleted altar are lost; the replacement starts empty. Power progression remains in PoV's world keys. Do not clear generation-complete or progression keys to repair a missing physical altar. A backup is needed to recover lost per-object state exactly.

Repair requires the normal host/admin/RCON authority and revalidates it throughout execution. It cannot overlap a FreshWorld reset or another repair, and it rejects startup, pause, save, pending maintenance, and faulted-session admission. Config edits wait until the operation finishes. Protection switches do not disable this explicit repair command; automatic reset scheduling and history are not consumed or rescheduled by repair.

If interrupted, completed creations remain and no automatic retry occurs. A captured object whose initialization fails is queued for native deletion and the batch stops; this is not a transactional rollback. Final changes use the game's normal saves. After repair, inspect the altar's height and interaction, save normally, and verify persistence after reconnecting or restarting. Repeating the command skips replacements that already exist.

This command does not create `PoV_HallMapSession`, runs, merchants, treasure caches, Blight controllers, monoliths, azurite, or rewards. Lost session investments and reward claims cannot safely be reconstructed from an entrance. Warden and Power altars are independent world objects, not Halls entrance children; Halls lobby altar props are separate dungeon content. Restarting alone is not a reliable repair for the two world altars because PoV retains generation-complete keys separately from physical objects.

Isolated regressions cover independent merchant/treasure settings in all reset stages, closed/dispensing/empty cache states, recursive deletion and native cleanup, original entrance identity and level, moved entrances, ambiguous records, record changes during save/height lookup, initialization failure, and repeat repair. ServerSync checks cover admin edits, invalid values, and host cfg reload. Debug compilation and original Valheim 1.0.21 metadata checks pass. These are not actual gameplay tests: still verify entry, return placement, existing personal-session reconnection, treasure dispensing, merchant departure, and save/restart with PoV in a host or dedicated session.

## Runic ore deposits

The default `[Path of Valheiman] RunicVeinVegetation=true` covers PoV's ten natural runic ore deposits. Scarlet Tin comes from tin drops and has no deposit of its own.

The host can place a deposit when it first generates a zone or generates it again after a full reset. PoV's biome-weighted choice selects up to one deposit per 128 m cell. Only the zone containing its candidate can place it. Nearby saved deposits, buildings and registered locations can prevent placement. The new deposit follows PoV's normal ground settling and mining rules. Location-only and resource-only resets keep natural deposits. FreshWorld skips these deposit IDs in both resource lists.

Set `RunicVeinVegetation=false` to use PoV's one-time placement. Surviving natural deposits keep their saved mining damage even if their zones reset. Fully mined deposits stay depleted.

Changing the setting leaves existing deposits and PoV's world completion key alone. Enabling it does not refill explored zones immediately. While enabled, FreshWorld handles new zone placement and suppresses PoV's initial worldwide placement. Disabling it in a world without PoV's completion key can start that native pass. Explicit third-party spawners can independently add deposits. This option is separate from azurite and Blight.

## Azurite vegetation

`[Path of Valheiman] AzuriteVegetation` defaults to `true`. An existing cfg with an explicit `false` keeps PoV's native placement and timed regeneration. Both ore options sync from the server and support host cfg hot reload after maintenance finishes.

When enabled on the host, FreshWorld blocks only PoV's natural `AzuriteVeins.ServerTick` and `RespawnTick`. It detaches and disposes a suspended native scan before changing policy or admitting maintenance. No PoV placement-complete keys or tunable dictionaries are changed. PoV's prefab registration, `AzuriteState` promotion, `Runecarver.DivineTouch`, Blight initial creation, adoption of nearby veins, and event cleanup remain intact. Periodic `BlightMist.Veins` is paused only during FreshWorld maintenance and its deferred cleanup; ordinary deletion queues do not stop it outside that boundary.

The default native vegetation definitions use the registered `PT_AzuriteVein` without adding seeded, grade, or event tags. Mountain placement uses 4-6 attempts per zone and a minimum altitude of 100m above sea level; BlackForest, Swamp, Plains, Mistlands, AshLands, and DeepNorth use one attempt with minimum altitude 1.5m. All use individual objects at original scale, normal block checks, and no forced placement. These are placement attempts, not guaranteed counts or a reproduction of PoV's distribution. PoV's seeded-vein 12m building exclusion, exact candidate positions, mountain thinning, and special Ashlands checks are not reproduced. Promotion remains subject to PoV's original biome, player, and event conditions; a fresh vein starts plain.

Registration runs at normal zone generation and before resource selection. It never injects azurite into the temporary copper-only resource registry. Existing definitions take priority, including disabled entries and EWD variant entries. Definitions are reused by identity, keeping EWD's attached data. FreshWorld adds defaults through a new list instead of modifying EWD's retained baseline. EWD reload can replace its list and variant map; these are reread at the next generation boundary. Disabling or leaving the world removes only FreshWorld-owned defaults. Existing user definitions remain. Mixed-prefab supplemental reset retains the existing safety refusal.

Before vegetation placement, a local saved-sector query checks the zone and its boundary out to 6m, including neighboring and unloaded sectors. A remaining azurite vein suppresses all azurite-producing definitions for that pass, including mixed entries, without deleting existing veins or suppressing unrelated definitions. Queued deletions are excluded so selected resource replacement works in the same frame. The original list is restored by a Harmony finalizer even when another placement hook throws. No permanent per-zone history or global resource scan is added. Multiple overlapping user-defined EWD entries can still duplicate their own new placements; this guard prevents overlap with pre-existing veins, not arbitrary conflicting YAML.

Untouched sectors containing PoV's preplaced veins keep them and skip added vegetation. Fully reset sectors replenish on later generation. Retained sectors replenish only if selected by resource reset (`PT_AzuriteVein` in `ResourceIds` or `TerrainResourceIds`). A live vein near a boundary may conservatively suppress an otherwise empty neighbor. The option does not immediately populate all explored zones, clean old duplicates, or reproduce native timed regrowth. Opting out on a world without PoV's natural placement-complete key can start its original world-wide seed pass.

Compatibility is based on required method/field contracts and relevant instruction checks, not assembly versions or hashes. The checks are bounded evidence, not a proof that arbitrary future code is equivalent. Unknown installation contracts leave PoV's generation unchanged and warn. Removed or conflicting natural-generation hooks reject new FreshWorld maintenance before mutation. Runtime DLL replacement and arbitrary third-party replacement of the generation logic are not supported.

## Expand World Data limits

Supplemental resource reset now passes original `ZoneVegetation` objects to native placement. EWD's identity-keyed custom data, scale, and clearance remain reachable. Definitions are selected in their existing order after a zone finishes loading; native seeds depend on prefab names rather than registry indices. The original registry, random state, ghost initialization, and temporary objects retain their cleanup paths on failure.

Disabled-only prefab definitions and their fractured forms are not deleted. An EWD entry that can generate different prefab names is refused before deletion in that resource zone, because clearing only its primary prefab could leave duplicates. Split such entries into single-prefab definitions if using supplemental resource reset. Full zone regeneration continues through the normal game/EWD path.

The optional EWD check reads the `VegetationSpawning.Prefabs` field's actual type and current dictionary. It does not pin a mod version, hash, or MVID. A missing contract or unavailable map stops the resource operation before its deletion step. YAML reload can replace the dictionary; only the field accessor is retained for the operation. Runtime DLL replacement is not supported.

EWD placement does not adopt PoV's existing objects, restore its original altar coordinates, update PoV's map registry, or recreate Blight event links. EWD global-key behavior is not claimed as verified by this patch.

## Evidence and validation

Original binaries inspected:

- PoV plugin 5.1.2, SHA-256 `E72B03AD5896B484560D4DE3ADD0EEA307E1566D1F274A8847B38F22D8869F03`.
- PoV 6.0.15, SHA-256 `6F4870DBE545AE94D99518BB18FB8F969CF180C3BD113861DFF5670301EF5A6A`. Its gate placement creates persistent terrain pads. Its missing-gate check runs on loaded, generated sites. The saved run schemas, live registries, objective keys, altar records, and natural and Blight azurite paths still match the integration. All 87,021 part and anchor positions in the 209 embedded interior layouts checked fit the existing protection bounds. Entrance and kit templates were outside that coordinate check.
- Valheim 1.0.21 original Windows client and dedicated-server assemblies. Both contain the location hash index used to recognize a placed Odin's Hollow entrance after removal.
- EWD 1.79, SHA-256 `1058952168312C0D1DE76D641CA3B38A5F4E44CC84B069D4F007B63226D0DB5D`.
- Valheim 1.0.20 original managed assemblies. No publicized game reference or new runtime mod dependency was added.

Source-linked automated tests cover protection with no configured whitelist, controller states, borrowed and owned objectives, late arrivals, orphan release on subsequent runs, cross-sector recursive deletion, group deletion, terrain gates, save timing, vegetation metadata identity, disabled entries, YAML reload, and rejection of unsafe mixed-prefab entries. They use simulated engine boundaries, not a running game.

Debug verification includes the existing test harnesses and original-game metadata audit. Additional tests cover independent PoV switches, synchronized edits and cfg reload, command authority and cancellation, reset schedule independence, rounded/duplicate/malformed records, inactive-zone matching, pre-save failures, objects arriving during a save, creation cleanup, and repeat repair. The merged Debug DLL was deployed to the local game's plugins directory and its SHA-256 matched the build output. Release 1.1.7 also passed the original-game metadata audit and merged-DLL config sync checks; its archive contains the five required package files with matching contents.

The vegetation revision passed 541 checks across the 11 test harnesses, including native-scan cancellation, cleanup boundaries, existing veins in unloaded neighboring sectors, queued deletions, registry restoration after failure, and EWD list replacement. The metadata audit passed 51 native contracts and resolved 204 compiled game members. A separate probe using Valheim's embedded Mono installed the built adapter against the original PoV and game assemblies and verified its five target methods. That probe checks detour construction only: Unity native calls are unavailable outside the game, and world execution and full adapter disposal are covered by simulated boundaries rather than a running Unity session.

Before production deployment, verify on a backed-up test world: host and dedicated-server runs; loaded and unloaded objectives; active Blight completion and expiry; EWD custom data and scale; resource harvesting; normal save and reconnect; natural azurite generation across resets, Divine promotion, and Blight adoption/replenishment. PoV altar interaction, height on modified terrain, remote visibility, save/restart persistence, and the actual PoV/EWD combination still require real game/server execution; automated checks do not establish those results.
