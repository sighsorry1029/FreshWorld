# ServerSync integration review

Reviewed on 2026-09-06. ServerSync integration was deferred by request and is not included in FreshWorld 0.6.4. This document records a static review for a possible future feature: letting a server administrator edit server settings through Configuration Manager. It is not a report of in-game compatibility testing.

## Reviewed material

- Supplied file: `C:/Users/blizz/Downloads/ServerSync.dll`, 49,664 bytes. The assembly was inspected without running its code.
- SHA-256: `166956302A294E224474B26F4C7D58409084AD3F48BD0AF1FEB7551F229C8F60`.
- Assembly and file version: `1.0.0.0`. Target framework: `.NETFramework,Version=v4.8`. These values do not identify the exact source commit or prove current game compatibility.
- [ServerSyncModTemplate](https://github.com/AzumattDev/ServerSyncModTemplate/tree/9fd34dd7c3e61fc42e57ed0b6af4bb1192aa0b2a) and [ServerSync source](https://github.com/blaxxun-boop/ServerSync/tree/c57c2aa54e07cdcc7630d6068699ea781622323e). These are the revisions reviewed, not a verified source match for the supplied DLL.

## Packaging and editing

ServerSync could be merged into FreshWorld.dll so users would not need a separate ServerSync installation. Its documentation describes merging with ILRepack and the Internalize option. Configuration Manager would remain an optional editor on the administrator's client; the server would not need the editing UI. See the [distribution and API guide](https://github.com/blaxxun-boop/ServerSync/blob/c57c2aa54e07cdcc7630d6068699ea781622323e/README.md).

Most FreshWorld settings remain `ConfigEntry<string>`. Mode and the three SafeZones entries use `ConfigEntry<ConfigChoice>` with a lossless BepInEx converter that preserves their original cfg text, including invalid values. This lets Configuration Manager display its native choice popup while FreshWorld retains explicit validation. Other settings retain toggles, numeric inputs, or text fields. This is a local editing feature, separate from the deferred ServerSync transport. Any future use of `AddConfigEntry<T>` must verify transport serialization of the choice wrapper and preservation of invalid input. The in-game UI and synchronization integration still require testing.

## Authority and validation

In the inspected DLL, `HandleConfigSyncRPC` checks the real RPC socket identity against the server's `m_adminList` through `ListContainsId` when `isServer && IsLocked`. A read-only client UI is separate from this server-side check.

Unlocking settings bypasses that administrator check. For settings that control world resets, a future integration should keep administrator editing locked and avoid exposing an unlock option to ordinary players. ServerSync's local `IsAdmin` property should not replace FreshWorld's command authority checks.

The DLL also has a branch that skips the explicit administrator comparison when it cannot obtain the current RPC or host identity. This inspection alone does not prove a usable remote bypass. Before integration, verify that unidentified remote changes are rejected and retain FreshWorld's connection and session checks.

ServerSync handles registration and transport. FreshWorld must still validate exact IDs, finite numbers, ranges, and the active schedule. Apply a complete validated settings snapshot on the host. A running reset must keep the snapshot captured when it started.

## File changes and synchronization events

The supplied DLL sends registered `SettingChanged` events. On receipt, it sets `BoxedValue` and saves the file, using `ProcessingServerUpdate` to suppress a resend loop. A future integration should distinguish these events:

- A cfg file edit: reload the file and validate the complete settings on the main thread.
- An in-memory edit from Configuration Manager or ServerSync: request validation of the current values on the main thread. Do not reload older file values over each incoming change.

Coalesce related changes so partially received settings do not repeatedly reset the schedule. Connect these events to FreshWorld's existing validation and snapshot flow instead of copying the template's file watcher unchanged. See the [template configuration and file watcher](https://github.com/AzumattDev/ServerSyncModTemplate/blob/9fd34dd7c3e61fc42e57ed0b6af4bb1192aa0b2a/Plugin.cs).

## Optional clients and attribution

The supplied `ConfigSync.ModRequired` defaults to false, but the template also includes `VersionHandshake.cs`. Verify version checks and connections from clients without FreshWorld before adopting that code. Ordinary players should remain able to join without installing FreshWorld. Administrators who edit remote settings would need FreshWorld and an editor on their client.

The reviewed source and template use MIT-0. Any future merged distribution should record the exact included source or binary, hash, and license notice. FreshWorld currently copies or merges none of the supplied ServerSync DLL. See the [ServerSync license](https://github.com/blaxxun-boop/ServerSync/blob/c57c2aa54e07cdcc7630d6068699ea781622323e/LICENSE.txt) and [template license](https://github.com/AzumattDev/ServerSyncModTemplate/blob/9fd34dd7c3e61fc42e57ed0b6af4bb1192aa0b2a/LICENSE.txt).
