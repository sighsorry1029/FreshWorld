# Maintenance coordination contract v2

FreshWorld exposes an optional host-only, main-thread lease API. A cooperating consumer can exclude FreshWorld maintenance while it performs its own tracked world operations. This is mutual exclusion, not proof of a preceding reset's success. It adds no configuration, saves, scheduler history, or ServerManager/Upgrade World dependency.

## Public surface

Type: `FreshWorld.API.MaintenanceCoordination` in `FreshWorld.dll`.

```csharp
public static int ContractVersion { get; } // 2
public static string[] Capabilities { get; }
public static Dictionary<string, string> TryAcquire(string worldId, string leaseOwner);
public static bool ValidateLease(string worldId, string sessionId, string leaseOwner, string leaseToken);
public static bool ReleaseLease(string worldId, string sessionId, string leaseOwner, string leaseToken);
```

Capabilities: `atomic-lease-v1`, `main-thread-v1`, `lease-validation-v1`. Check the API contract rather than an exact mod version, MVID, or DLL hash. The v1 occurrence API is removed; there is no legacy/migration path. All calls run on Unity's main thread. There is no RPC or client authorization path through this API.

## Acquisition

Pass the current decimal world UID and a nonblank owner string of at most 256 characters. Identity comparisons are ordinal and case-sensitive. `TryAcquire` checks readiness and claims FreshWorld's own maintenance gate in the same main-thread call, so a separate `IsBusy` snapshot is not needed. Acquisition also discards FreshWorld's PoV native regeneration snapshot under the gate.

The returned dictionary is detached:

| status | Other fields | Meaning |
| --- | --- | --- |
| `Acquired` | `worldId`, `sessionId`, `leaseToken` | Owns the gate in this host session. All three identity strings are nonempty. |
| `Busy` | `errorCode=Busy` | Another FreshWorld or external maintenance operation holds the gate. |
| `Busy` | `errorCode=CleanupPending` | FreshWorld-owned deferred zone-load cleanup has not drained. No gate was acquired. |
| `Rejected` | `errorCode` | Invalid request or unavailable provider; do not silently run uncoordinated. |

Rejected codes are `WrongThread`, `ProviderNotReady`, `WrongWorld`, `InvalidRequest`, `ProviderError`, and `PreparationFailed`. Reentrant world changes or preparation errors dispose the provisional gate without publishing a lease. An installed but unsupported/unavailable provider is not equivalent to an absent FreshWorld installation.

No daily slot, date, time zone, run ID, successful result, or generation comparison is required. FreshWorld automatic scheduling may be disabled; a ready host still supports the gate for its manual maintenance. A reset that failed but finished cleanup can be followed by external work. An actual host-session fault, such as inability to persist its own scheduler result, remains unavailable.

## Ownership and cleanup

Hold the lease through actual consumer execution and cleanup. Validate exact world/session/owner/token at dispatch boundaries. Release only after native work has really stopped. A timeout or an exception alone is not evidence of completion; there is no TTL, stealing, or forced expiry. If work cannot be proven idle, the consumer may retain the lease for operator review until verified cleanup or world shutdown.

Duplicate, worker-thread, stale-session, or mismatched release returns `false` without unlocking another operation. Release remains possible after a same-session readiness/compatibility failure. Host teardown invalidates its old lease; a new world session gets a different session ID.

While externally held, FreshWorld defers automatic execution and cfg application. Pending automatic deadlines may still coalesce normally, but are not marked attempted. Manual reset and PoV repair requests are refused while held; their existing authority checks remain. Compatibility failure prevents further consumer dispatch but delays FreshWorld's operational patch/vegetation teardown until the owner releases after cleanup.

Deferred cleanup is an admission check, not a condition invalidating an already held lease. Ordinary pending destruction notifications do not block acquisition. The gate does not control gameplay, arbitrary console commands, or other mods. ServerManager uses it for its tracked Upgrade World cron jobs; Upgrade World itself does not require modification.

## Saves and verification

The API never requests a save. FreshWorld retains its own pre-maintenance save and real completion barrier. Subsequent persistence belongs to normal game saves and explicit commands; this interface does not certify disk durability or perform rollback.

Source-linked controller tests exercise idle admission, rejection without mutation, shared internal/external exclusion, ownership and thread checks, no TTL, config/pending deferral, failed settled runs, preparation failures, session replacement, persistence faults, deferred cleanup, and compatibility teardown. These are isolated tests, not live Unity/Steam/PlayFab timing proof. Verify paired use on a copied host/dedicated-server world with the intended mod set. Keep existing failed/review journal entries for operator review.
