# Third-party notices

FreshWorld includes adaptations of the world restoration algorithms from **Upgrade World**, by Jere Kuusela and contributors, introduced in version 0.2.0.

- Upstream repository: <https://github.com/JereKuusela/valheim-upgrade_world>
- Audited source revision: `f1e3b21140a9d7865d204910a1ed40a3df93297b`
- Upstream license: **Unlicense** (public-domain dedication).
- License source: <https://github.com/JereKuusela/valheim-upgrade_world/blob/f1e3b21140a9d7865d204910a1ed40a3df93297b/LICENSE>
- A verbatim copy is included at [third_party/UpgradeWorld.LICENSE.txt](third_party/UpgradeWorld.LICENSE.txt).

The adaptations are in `FreshWorld/Engine/` and the default protection marker list in `FreshWorld/Configuration/FreshWorldConfig.cs`. They cover generated-zone removal, spawned-object cleanup and destruction batching, base-marker filtering, native vegetation/location placement, terrain data restoration, and terrain-generation hooks.

FreshWorld narrows these mechanisms to exact configured IDs and its own scheduled pipeline. It adds scoped temporary-zone ownership, player-character guards, exception-safe restoration of shared generation state, bounded coroutine execution, cancellation cleanup, and terrain-layout validation. Terrain vertex and paint coordinates follow the inspected game layout. The console command layer, Upgrade World's general filters, and `world_clean` are not included.

FreshWorld does not redistribute or reference the Upgrade World or Server devcommands assemblies. BepInEx and its bundled Harmony library are external runtime requirements. Valheim, Unity, and Soft Referenceable Assets assemblies are used from a local game installation for compilation and are not included in the package.
