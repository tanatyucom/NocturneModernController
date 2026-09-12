# Explorer-based Right Stick Route — Known Good

## Status

- Published production implementation
- Version: v2.0.1
- Tag: `v2.0.1`
- Commit: `a941a66762e3f145766d459b74d5120d4afd7573`
- Current `origin/main` retains the v2.0.1 Explorer-based production
  implementation. Commits after the v2.0.1 tag (such as this document)
  may contain documentation-only changes and do not imply that the
  published runtime architecture has changed.

## Why this is preserved

The Explorer route has:

- zero configuration
- zero extra user action
- Steam's own "Play" button works as-is
- no Guide button press needed
- no Settings toggle needed
- no Task Scheduler registration needed

This is the best user experience of any automatic right-stick route this
project has shipped or developed. Future work (e.g. the Task Scheduler
route) must not delete or silently replace this implementation without an
explicit, deliberate architecture decision.

## Architecture

Based directly on the source at commit `a941a66` (`src/ExternalInputBridge.cs`):

```
smt3hd.exe (inside the MOD, at OnInitializeMelon)
  -> Process.Start("explorer.exe", "\"<InputHelper.exe path>\"")
       -> NocturneModernController.InputHelper.exe
            -> SDL3 (gamepad enumeration and axis/button reads)
                 -> Memory-Mapped File ("NocturneModernController_SDL_v2")
                      -> MOD (TryRead / UpdateGameContext / Stop)
```

There is no Broker and no Launcher in this route.

- ready marker: none
- launch-request: none
- shutdown-request: none
- Mutex: none
- IPC: the shared MMF only

## Process separation

Explorer route:

```
NocturneModernController.InputHelper.exe
  <- explorer.exe
```

Task Scheduler route (developed later, see "Relationship to Task Scheduler route" below):

```
NocturneModernController.Broker.exe
  <- svchost.exe
  <- services.exe
  <- wininit.exe
```

Both routes share the same goal - keeping the input helper's execution
context outside the Steam / smt3hd.exe process tree - but they use
different mechanisms to get there.

## User flow

1. Install the MOD.
2. Start SMT3HD normally from Steam.
3. The external input helper starts automatically.
4. The right stick works.

No additional action is required.

## Known Good properties

- Works with Steam's own "Play" button
- Automatic helper launch, no user action
- No Guide button press needed
- Right stick input via SDL
- MMF-based communication with the MOD
- No initial setup of any kind

## AV / false-positive note

This is recorded neutrally, for transparency, not as a call to action.

The Explorer route launches the input helper by asking `explorer.exe` to
open it, so that the helper's process ancestry is `explorer.exe` rather
than `smt3hd.exe`. This is not a standard application launch flow.

Similar process-launch patterns (spawning a process via `explorer.exe` so
it appears to descend from an unrelated, already-trusted process) are also
used by malware for other purposes. Because of that overlap, some AV
products or automated scanning systems (including marketplace scanners)
may flag this behavior as a heuristic false positive.

The purpose of this implementation is controller input process
separation. It is not intended for, and must not be changed toward, AV
detection evasion. No obfuscation, packing, or stealth-oriented changes
should be made to this implementation for that purpose.

## Relationship to Task Scheduler route

Explorer route:

- Published
- Zero-config
- Best UX
- No Task registration
- Potential heuristic AV risk

Task Scheduler route:

- Currently an alternative, unreleased (developed on a separate branch)
- One-time Settings opt-in
- Automatic after that one-time setup
- Uses the standard Windows Task Scheduler COM API only
- Better transparency for AV / distribution purposes

The Task Scheduler route is not described as an "upgrade" or a
"successor" to the Explorer route. They are alternatives, kept for
different situations (see "Preservation rule" below).

## Restore / rebuild

To restore or inspect the Explorer Known Good implementation:

```
git checkout v2.0.1
```

or checkout the same commit directly:

```
git checkout a941a66762e3f145766d459b74d5120d4afd7573
```

Then build with the existing clean-build procedure. Required environment:

- .NET 6 SDK
- The existing SMT3HD / MelonLoader development references (same as any
  other build of this project)

This has been verified to build with 0 warnings / 0 errors from a clean,
isolated checkout of this exact commit.

## Published artifact

- GitHub release: `v2.0.1`
- Release zip: `NocturneModernController-v2.0.1.zip`
- Known GitHub release SHA-256: `D5B2B24D26C2A49722D9F0A2F3CE0AEACE549DB91598A4BEA9EE7C5D677A04DF`

Byte-for-byte equality between the originally-uploaded individual
binaries and a freshly rebuilt copy has not been verified and is
currently **UNKNOWN** (source-level rebuild reproducibility - 0
warnings/0 errors from the pinned commit - has been confirmed; bit-exact
binary/zip reproduction has not been attempted).

## Branch relationship

- `v2.0.1` tag: the published Explorer Known Good source snapshot.
- `origin/main`: the Explorer production line. Its production source
  (`src/ExternalInputBridge.cs` and everything it depends on) remains the
  v2.0.1 Explorer architecture; documentation-only commits (such as this
  file) may exist on `main` after the `v2.0.1` tag without changing that.
- `agent/right-stick-vanilla-turn`: the Task Scheduler alternative
  development branch. It has not been merged into `main`.

`origin/main` and the `v2.0.1` tag are not guaranteed to always point at
the same commit going forward (e.g. a docs-only commit on `main` would
move `origin/main` past the tag) - what matters is that `main`'s
*production source* stays the Explorer architecture until an explicit
architecture decision changes that. When merging any future branch into
`main`, do not let that merge silently remove or replace the Explorer
implementation in `src/ExternalInputBridge.cs`.

## Preservation rule

**DO NOT DELETE OR SILENTLY REPLACE THIS IMPLEMENTATION.**

This is the Known Good, published v2.0.1 Explorer-based automatic
right-stick route.

Any replacement of this implementation must be an explicit, deliberate
architecture and release decision - not an incidental side effect of
merging other work.
