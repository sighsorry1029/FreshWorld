# Embedded ServerSync

FreshWorld vendors the fixed `valheim-1.0.7-r1` ServerSync library and merges it into the final plugin with ILRepack 2.0.44.1 and internalization. This DLL is not a standalone game plugin.

- Upstream: https://github.com/blaxxun-boop/ServerSync/tree/c57c2aa54e07cdcc7630d6068699ea781622323e
- Common build: `valheim-1.0.7-r1`, compiled against original Valheim 1.0.7 assemblies.
- DLL SHA-256: `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`
- Assembly version: `1.0.0.0`; file version: `1.0.0.1`.
- License: MIT-0; see `../../third_party/ServerSync.LICENSE.txt`.
- The fixed DLL is unchanged. FreshWorld's scoped integration is in `Configuration/ConfigSynchronization.cs`.

The common build repairs the old Everybody field access, uses the public administrator API, and preserves initial peer/list message ordering. Its original verification did not include actual multiplayer sessions. FreshWorld integration verification is recorded in `../../docs/FreshWorld-serversync-review.md`.

`ILRepack.targets` always reads the compiler's intermediate DLL and this pinned input. It finishes before Debug installation or Release packaging. No build downloads or replaces ServerSync from a global latest path.
