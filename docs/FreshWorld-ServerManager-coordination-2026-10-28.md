# FreshWorld and ServerManager: simplified maintenance coordination

This update replaces the earlier exact-occurrence coordination proposal. Existing residual-object diagnostics and safe deletion checks remain. The supplied logs show a FreshWorld group-deletion stop, but do not identify a corrupt prefab or prove that Upgrade World overlap caused it.

## Current behavior

- FreshWorld exposes the optional [v2 lease contract](maintenance-coordination-contract.md). It has no ServerManager or Upgrade World assembly dependency and adds no cfg option.
- ServerManager automatically acquires the shared gate for its tracked Upgrade World cron work. No `requiresFreshWorld` date/slot field is used. Both mods keep their existing independent schedules.
- When FreshWorld is busy or still cleaning up, the consumer waits without owning the gate. Once acquired, FreshWorld cannot start a competing reset or PoV repair until the consumer's actual native work and cleanup finish.
- The gate does not require a preceding reset to succeed. A failed but settled reset can be followed by Upgrade World. It also cannot prevent manually executed Upgrade World commands or other nonparticipating mods from starting work.
- There is no automatic extra checkpoint from this contract. FreshWorld still saves before its own maintenance and waits for that save to finish. Normal game saves and explicit `save` jobs own later durability. Crash/restart before a successful later save can lose recent changes.
- FreshWorld cfg application waits for external work. Its automatic scheduler preserves/coalesces pending work; manual reset and PoV repair requests are refused while externally held. No timeout steals the lease. Compatibility failure preserves active cleanup boundaries until release; world teardown invalidates the old session.

FreshWorld no longer publishes occurrence queries, current-session completion history, result revisions, or maintenance generations for this integration. Its own existing execution journal and failure/retry policy remain unchanged.

## Residual-object diagnostics retained

Group reset captures a local object plan, including in-group Spawned children, and validates identity, prefab, position, connections, protection, and loading state before mutation. Linked children absent from the sector index are included in host deletion completion. Unexpected changes stop the run rather than being silently counted as success.

Post-deletion checks remain. Unplanned objects are not finalized merely because another hook put them into the destruction queue. Regeneration stays disabled while unexpected objects, identity changes, or pending loads remain. There is no forced deletion, automatic retry, or rollback.

Failure diagnostics include the run ID, stage, group/location, coordinates/timing, and up to eight object details. Diagnostic errors cannot replace the first failure. Checks are group-local; they do not scan the whole world on each tick. The original game DLL/private-access boundary is unchanged and no DLL was publicized.

## Validation and remaining checks

Before the release version bump, `dotnet build FreshWorld.sln -c Debug -p:DeployToGame=true` completed with zero warnings and errors, including the ServerSync merge. That Debug DLL and local game copy both have SHA-256 `F0A27A6917F0EACC0F5BBD60BC0C0489D35D0D653A22C582FA0A6E9F48DDCAD9`.

Release 1.1.9 was then built with `dotnet build FreshWorld.sln -c Release`, also with zero warnings and errors. Its DLL has SHA-256 `7811ED56E15741E5B7A606BBBF7808B32CF1A03B9BC60BB99C645F16B0CB9FAF`. Assembly and file versions are 1.1.9.0, and the package manifest is 1.1.9. The five ZIP entries match their source files: DLL, root README, changelog, manifest and icon. No duplicate Thunderstore README is generated.

Regression checks passed in Debug and were rerun successfully in Release: Core/Config 71, Runtime 11, Backend 107, Terrain 13, World 87, Generation 25, Command 29, Plugin 62, original-game Save 19, installed-Harmony/BepInEx 46, and merged-DLL Sync 89: **559 checks total**. The optional installed ConfigManager metadata smoke was skipped because no ConfigManager assembly path was supplied.

Source-linked controller tests cover mutual exclusion, pending work/reload deferral, wrong-thread and stale ownership, no timeout expiry, preparation errors and reentrant teardown, failed-but-settled runs, persistence faults, and cleanup ordering. Existing world-object diagnostic and native save tests remain part of regression verification.

These checks are not live Unity or network validation. On a copied world, run a long FreshWorld reset across a scheduled Upgrade World time, then reverse the order. Confirm no overlap, no automatic extra saves, and normal autosaves. Test failure, timeout, world shutdown, local hosting, and dedicated hosting with the actual mod set. Already applied changes survive an in-memory failure; this is not a transaction.

No operational cron/cfg/world/journal, review cursor, or dedicated-server installation was modified. The earlier local Debug plugin deployment is for testing. Release 1.1.9 packaging does not certify a live game run or publication to any mod site.
