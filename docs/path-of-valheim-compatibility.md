# Path of Valheiman compatibility and altar recovery

FreshWorld protects PoV objectives and provides manual recovery of missing Warden and Power altars. Optional azurite vegetation changes natural resource placement only. FreshWorld does not change progression or discovery keys, repair map pins, or reset an event.

## Protection

- `[Path of Valheiman] AltarProtection=true` protects `PT_WardenAltar` and `PT_PowerAltar` in their own zones.
- `[Path of Valheiman] BlightProtection=true` enables the active-event rules below. Both default to true, use ServerSync and host cfg hot reload, and apply on the next run. Other protection rules still apply when either option is disabled.
- A valid `PT_CorruptionZone` with a nonempty `pt_blight_uid` and state 0 (running), 1 (ready), or 2 (summoned) identifies an active Blight.
- Its `PT_BlightAltar` links through `pt_blight_uid`. Its `PT_RuneMonolith` links through `pt_blight_zone`. Borrowed natural objects qualify even without `pt_blight_own`.
- Warden bosses link through `pt_blight_zone` and `pt_blight_warden=true`. This includes PoV's fallback boss prefab. Ordinary Blight mobs and azurite veins are not protected objectives. PoV can replenish its event veins.

Only the objects' own zones are reserved, not the event's full circle. All reset stages, direct and recursive FreshWorld deletion, terrain restoration, and border repair use this policy. It works from saved ZDOs without loaded scenes, a Piece component, creator metadata, or an online player.

Each run indexes controllers after the initial world save. Known controllers are checked live. Missing or inactive links can refresh the index on a later frame; multiple orphan objects share the same frame's scan. The policy retains active UIDs, observed object IDs, and protected zones until the run ends. A new run starts with empty state. Closed or missing controllers therefore do not permanently protect leftovers unless another protection rule applies.

PoV creates its controller before its dependent objectives in a synchronous call. Arrivals after a coroutine yield are detected on the next target check. Arbitrary third-party callbacks that create a new controller after a negative lookup within the same frame are outside the verified lifecycle.

Only FreshWorld's own deletion path is guarded. PoV can still close events, remove its objects, and restore borrowed natural objects. No game-wide destruction patch is added. Native placement outside a protected zone may still extend geometry across its boundary.

## Restoring missing altars

Run `freshworld pov` from an authorized local host, server administrator client, or dedicated console/RCON. There is no additional `repair` argument. The operation is manual: neither automatic resets nor the plain `freshworld` command invoke it. PoV must be installed and ready on the host.

The host reads PoV's recorded altar coordinates and checks saved ZDOs in active and inactive sectors. It does not generate or load zones. A world save must finish successfully before any creation; a no-op does not request a save. Each missing candidate is checked again after saving and immediately before creation. New objects are persistent, server-owned ZDOs created by the same method used by PoV itself. Results and skipped positions appear in the requesting console and the host log.

Matching rules:

- Only `PT_WardenAltar` and `PT_PowerAltar` are eligible. Coordinates must come from PoV world records; no positions are inferred from terrain or player map data.
- An existing object of the same prefab within 2m horizontally counts as present, regardless of height. This tolerates PoV's rounded coordinate storage and includes inactive objects.
- A same-prefab object farther than 2m but within 30m is ambiguous and is skipped. Distinct nearby coordinate records for the same prefab also block creation. Exact duplicate records are deduplicated.
- Malformed or out-of-range records are skipped. Missing PoV read contracts, unready data, or unavailable persistent prefabs stop repair. Save refusal or save failure prevents creation.
- Existing or moved objects are not deleted, repositioned, or merged. If an existing object is unusable, inspect it separately instead of expecting this command to replace it.

PoV stores Warden records as `pov_warden_altars_2_<X>_<Z>` (also reading `pov_warden_altar_1_`) and Power records as `pov_power_at_<X>_<Z>`. `pov_power_at_done_1` is a completion marker, not coordinates. FreshWorld checks the installed plugin's public `WorldKeys.Lines()` and `Synced` contracts, rather than pinning its version. Reflection metadata lives only for this repair operation. Runtime DLL replacement is unsupported.

The restored X/Z matches PoV's recorded pin coordinates. Original height and rotation are not stored there: FreshWorld reconstructs them with PoV's procedural height and deterministic rotation formulas. This may differ from heavily edited terrain. Existing discovery and map records remain unchanged; repair does not reveal undiscovered altars to all players. Stale/manual pins or lost coordinate records cannot supply missing server records.

Warden shard/blood deposits on a deleted altar are lost; the replacement starts empty. Power progression remains in PoV's world keys. Do not clear generation-complete or progression keys to repair a missing physical altar. A backup is needed to recover lost per-object state exactly.

Repair requires the normal host/admin/RCON authority and revalidates it throughout execution. It cannot overlap a FreshWorld reset or another repair, and it rejects startup, pause, save, pending maintenance, and faulted-session admission. Config edits wait until the operation finishes. The two protection switches do not disable this explicit repair command; automatic reset scheduling and history are not consumed or rescheduled by repair.

If interrupted, completed creations remain and no automatic retry occurs. A captured object whose initialization fails is queued for native deletion and the batch stops; this is not a transactional rollback. Final changes use the game's normal saves. After repair, inspect the altar's height and interaction, save normally, and verify persistence after reconnecting or restarting. Repeating the command skips replacements that already exist.

This command does not restore Blight controllers, natural monoliths, azurite, rewards, or other event state. Restarting alone is not a reliable repair for the two altars because PoV retains generation-complete keys and coordinate records separately from physical objects.

## Optional azurite vegetation

`[Path of Valheiman] AzuriteVegetation=false` is opt-in, server-synced, and supports host cfg hot reload after maintenance finishes. It replaces the proposed timed-regeneration settings; there is no additional respawn timer or timer-record preservation. `freshworld pov` remains an altar-only repair command.

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
- EWD 1.79, SHA-256 `1058952168312C0D1DE76D641CA3B38A5F4E44CC84B069D4F007B63226D0DB5D`.
- Valheim 1.0.20 original managed assemblies. No publicized game reference or new runtime mod dependency was added.

Source-linked automated tests cover protection with no configured whitelist, controller states, borrowed and owned objectives, late arrivals, orphan release on subsequent runs, cross-sector recursive deletion, group deletion, terrain gates, save timing, vegetation metadata identity, disabled entries, YAML reload, and rejection of unsafe mixed-prefab entries. They use simulated engine boundaries, not a running game.

Debug verification includes the existing test harnesses and original-game metadata audit. Additional tests cover independent PoV switches, synchronized edits and cfg reload, command authority and cancellation, reset schedule independence, rounded/duplicate/malformed records, inactive-zone matching, pre-save failures, objects arriving during a save, creation cleanup, and repeat repair. The merged Debug DLL was deployed to the local game's plugins directory and its SHA-256 matched the build output. Release 1.1.7 also passed the original-game metadata audit and merged-DLL config sync checks; its archive contains the five required package files with matching contents.

The vegetation revision passed 541 checks across the 11 test harnesses, including native-scan cancellation, cleanup boundaries, existing veins in unloaded neighboring sectors, queued deletions, registry restoration after failure, and EWD list replacement. The metadata audit passed 51 native contracts and resolved 204 compiled game members. A separate probe using Valheim's embedded Mono installed the built adapter against the original PoV and game assemblies and verified its five target methods. That probe checks detour construction only: Unity native calls are unavailable outside the game, and world execution and full adapter disposal are covered by simulated boundaries rather than a running Unity session.

Before production deployment, verify on a backed-up test world: host and dedicated-server runs; loaded and unloaded objectives; active Blight completion and expiry; EWD custom data and scale; resource harvesting; normal save and reconnect; natural azurite generation across resets, Divine promotion, and Blight adoption/replenishment. PoV altar interaction, height on modified terrain, remote visibility, save/restart persistence, and the actual PoV/EWD combination still require real game/server execution; automated checks do not establish those results.
