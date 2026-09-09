# Root-26 Pre-Reboot Evidence / Handoff (2026-09-07)

Evidence archive, not Canonical. Purpose: allow Root-26 investigation to resume
after a Windows restart without losing state. No diagnostic implementation,
no root-cause fix, no refactoring, no commit/push, no stash/reset/revert was
performed while assembling this snapshot.

## 1. Git working tree state (at snapshot time)

- Branch: `agent/right-stick-vanilla-turn`
- HEAD: `b2a2d4eac0db0dbd3b4867fef5ff02908d987081` ("feat: add broker-based
  external input launch and liveness guard"), branch up to date with
  `origin/agent/right-stick-vanilla-turn`.
- Full `git status --short` / `git diff --stat` output: see
  `source_snapshot/git_state_20260907.txt`.
- Full tracked-file diff: `source_snapshot/git_diff_tracked_20260907.txt`.
- Untracked files present (not committed, not touched by this snapshot task):
  `CLAUDE.md`, `settings/StartupShortcutManager.cs`, `src/FocusCycleProbe.cs`,
  `src/RightStickPollingProbe.cs`, several `tools/*` PoC dirs/scripts, and
  this evidence folder itself.
- Tracked-but-modified files at snapshot time: `docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md`,
  `helper/Program.cs`, `settings/Program.cs`, `src/ModMain.cs`,
  `src/ModernControllerApi.cs`. Some of these edits (notably
  `RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md`, and likely further tuning
  inside `RightStickPollingProbe.cs`/`FocusCycleProbe.cs` beyond what this
  session authored) were made by another agent ("Codex") working on the same
  tree; this snapshot preserves the state as found, without judging or
  modifying it.
- No commit, push, stash, reset, or revert was performed by this snapshot task.

## 2. Deployed binary state

- Deployed `NocturneModernController.dll`:
  `C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.dll`
  SHA-256 = `DA68D07637CA797B2726ECD1C53546175FF3FBAE96B927EAFEFC1E03B155E040`
  — **matches the SHA-256 given in the user's preservation request exactly.**
- Build artifact (`bin/Release/net6.0/NocturneModernController.dll`) hash
  matches the deployed DLL exactly (see `source_snapshot/hash_state_20260907.txt`).
- Companion binaries confirmed unchanged from their known baseline:
  - `NocturneModernController.Broker.exe` = `3ad83f518036491169f080fb1e90bef57b4c17c2287bf04197c81a0c216defe7`
  - `NocturneModernController.InputHelper.exe` = `b596705232eb24c9b64686014a3e0909156b93c97c88821cc4b7d5e1d4ce743e`
  - `NocturneModernController.Settings.dll` = `665b4335c5912673e8a0e3183c5c02820ecdbd88b1f8a9c8cc8c90fc9fd8aa2a`
- `src/RightStickPollingProbe.cs` / `src/FocusCycleProbe.cs` snapshots as of
  this deploy are saved under `source_snapshot/` (read-only copies; the
  working-tree originals were not modified).

## 3. IMPORTANT — discrepancy found vs. the preservation request (CORRECTED 2026-09-07, later same day)

The request to preserve stated as fact: **"最新rolling版はdeploy済みだが未テスト"**
(the newly deployed rolling-bucket version is deployed but not yet tested).

**This is no longer accurate.** `MelonLoader/Latest.log` (a single
self-overwriting file — see below) currently contains a session from
**2026-09-07 13:22:34–13:23:09**, in which:

- The loaded mod DLL hash is confirmed as
  `da68d07637ca797b2726ecd1c53546175ff3fbae96b927eafefc1e03b155e040`
  (the rolling-bucket build, matches the deployed hash above).
- Two short exploration sessions occurred (13:22:51–13:22:54 and
  13:23:02–13:23:04).
- In the **second** session, a real state transition was captured:
  ```
  [13:23:03.015] STATE-TRANSITION channel=X INITIAL -> DEAD(128-fixed) ...
  [13:23:03.015] STATE-TRANSITION channel=Y INITIAL -> DEAD(128-fixed) ...
  [13:23:03.765] STATE-TRANSITION channel=X DEAD(128-fixed) -> LIVE(analog-active) bucket(count=52 min=5 max=128 last=128 nonCenter=48)
  [13:23:03.765] STATE-TRANSITION channel=Y DEAD(128-fixed) -> LIVE(analog-active) bucket(count=52 min=128 max=169 last=128 nonCenter=48)
  ```
  i.e. the rolling observer **did** capture a genuine DEAD→LIVE transition,
  timestamped to the millisecond, ~750ms after the session armed.
- `SESSION-SUMMARY` for that session: X `count=362 min=1 max=204 last=128
  nonCenter=208 finalState=LIVE(analog-active) transitions=1`; Y similar.
- Both sessions in this log are very short (a few seconds each), with a
  `FOREGROUND-CHANGE` to `explorer.exe` between them (13:22:54.705).
- Broker/InputHelper/Launcher temp logs are unchanged since 2026-09-06 (see
  `temp_diagnostics/`), confirming Broker/InputHelper/External SDL were not
  involved in this 13:22–13:23 session either.

### 3.1 Correction: user-side action context for the 13:23:03.765 transition (added later, same day)

The original version of this section stated that "why the DEAD→LIVE
transition happened at 13:23:03.765 cannot be determined from this log
alone." **This has been corrected.** The user has since supplied the missing
action context from their own memory of the session (not derivable from the
log itself, since no Guide/Overlay/task-switch event is instrumented):

**CONFIRMED / USER OBSERVATION + LOG CORRELATION**

- In the 13:23 session, Right Stick was DEAD (matches
  `INITIAL -> DEAD(128-fixed)` at 13:23:03.015).
- The user long-pressed the Xbox Guide button.
- That Guide long-press did **not** trigger the Steam recording/Overlay
  behavior the user had expected; instead it brought up an Alt+Tab-equivalent
  task-switching UI.
- Immediately after that action, Right Stick became LIVE.
- The rolling observer captured the `DEAD -> LIVE` transition for both X and
  Y at 13:23:03.765 in the same session.
- Broker/InputHelper/External SDL were not involved (per the unchanged
  temp-log mtimes above).

**HYPOTHESIS / UNRESOLVED (do not elevate any of these to CONFIRMED)**

- Whether the Guide long-press itself is the cause of the Right Stick
  revival.
- Whether the Alt+Tab-equivalent task-switch processing is the cause.
- Whether a Steam Input / controller-route re-evaluation is the cause.
- Whether Steam Overlay is the cause.
- Whether the video-recording feature specifically is required at all.

**Explicitly do not treat Steam Overlay or video recording as confirmed
causes.** The user has also reported a contradicting prior pattern: in past
sessions, Guide-long-press-into-video-recording reproduced the revival every
time, but in this 13:23 session the revival happened **without recording
ever being started** — only the Guide long press + the (unexpected) Alt+Tab
task-switch UI occurred. This weakens "video recording is required" as an
explanation and is itself only an UNRESOLVED observation, not a confirmed
rule.

**Conclusion of this discrepancy check (revised):** the rolling observer is
not merely "deployed but untested" — it has recorded one real DEAD→LIVE
transition, now correlated (via user memory, not log instrumentation) with a
Guide long-press that produced an Alt+Tab-equivalent task switch, with no
recording involved. This is evidence toward "recording is not necessary for
revival," but the true causal mechanism among Guide-press itself /
task-switch processing / Steam Input re-evaluation / Overlay remains
UNRESOLVED.

## 4. Log provenance note (MelonLoader single-file rotation)

`MelonLoader/Latest.log` is a **single self-overwriting file** — it is
replaced in full on every game launch. This means:

- The 09:27:26–09:28:53 session (loaded DLL hash
  `b681540cfb978fe0144f27efa9122510ae9163c24d35b9a4146a3d11a55f07e6`, the
  **old 3-second one-shot probe**, previously fully analyzed) **no longer
  exists on disk as `Latest.log`** — it was overwritten by the 13:22–13:23
  session above.
- The only surviving record of that 09:27 session is the raw capture already
  saved into this conversation's tool-results earlier today, which has been
  copied into this evidence folder as
  `melonloader/Latest.log_20260907_0927-0928_ONESHOT_prior_capture_original_overwritten.log`.
  This is a **copy of a previously-captured dump, not a live copy of a
  currently-existing original file** (the original no longer exists).
- The current, still-live `Latest.log` (13:22–13:23 session, rolling probe,
  contains the LIVE transition above) has been copied unmodified to
  `melonloader/Latest.log_20260907_1322-1323_ROLLING_with_LIVE_transition.log`.
- **The original `MelonLoader/Latest.log` file itself was not modified or
  deleted** by this preservation task. It will very likely be overwritten
  again on the next game launch (post-reboot), which is exactly why this
  archival copy was made now.

## 5. Fixed facts to carry forward (do not re-litigate from scratch)

- `dds3PadManager.GetPadAnalog(0,1,X/Y,1)` returning 128-fixed = DEAD,
  returning real analog values = LIVE, is CONFIRMED (established pre-Root-26
  rewrite, reconfirmed by both logs above).
- Broker / InputHelper / External SDL / MMF are **not** involved in either
  the 09:27 (old probe) or 13:22 (new probe) sessions analyzed so far —
  CONFIRMED both times via Broker marker-dead log line and unchanged
  Broker/InputHelper temp-log mtimes.
- "起動後にControllerをONにすればRight Stickが生きる" is REJECTED (both
  live and dead outcomes have been observed after that same action in past
  sessions per user report).
- **Steam Overlay being the cause is still only a candidate, not confirmed.**
  It must remain classified as STRONG HYPOTHESIS / UNRESOLVED — do not
  elevate it to CONFIRMED without direct evidence (e.g. an Overlay-window
  detection observer correlated with a STATE-TRANSITION log line). The same
  applies to Xbox Guide long-press, the Alt+Tab-equivalent task-switch UI it
  triggered in the 13:23 session, reWASD profile switching, and Steam Input
  re-evaluation: **none of these have been confirmed as the cause** — see
  §3.1 for the current CONFIRMED/HYPOTHESIS split. Current instrumentation
  still only captures the native analog value and Win32
  foreground/activation messages; it has no dedicated Guide-button, Overlay,
  task-switch, or reWASD event log — the §3.1 correlation was supplied by the
  user's own memory of the session, not by log instrumentation.
- **Video recording is not established as a necessary trigger.** The 13:23
  session reached LIVE via Guide long-press + Alt+Tab-equivalent task switch
  alone, with no recording ever started, which contradicts a strict
  "recording is required" reading of earlier sessions. This is itself only
  an UNRESOLVED observation (see §3.1) — do not treat it as proof recording
  is irrelevant, only as a reason to test Guide-press-only next.

## 6. Next priority after reboot (REVISED — video recording removed from the test)

Per §3.1, the 13:23 session already reached LIVE via Guide long-press +
Alt+Tab-equivalent task switch **without any recording being started**. The
next test therefore deliberately **excludes video recording** to test
whether the Guide long-press alone (with whatever UI it happens to trigger)
is sufficient:

```
1. Normal launch.
2. Enter exploration; confirm Right Stick is DEAD.
3. Wait briefly so the rolling observer logs the DEAD state clearly.
4. Xbox Guide button long-press ONLY (no recording, no other input).
5. Return to the game.
6. Move the Right Stick.
7. Confirm whether it is now LIVE.
8. Stop the test here (do not proceed to recording).
```

Using the already-deployed rolling-bucket `RightStickPollingProbe` /
session-length `FocusCycleProbe` (hash `DA68D07...` above); no new
diagnostic code is needed for this specific next test.

**Important recording requirement for this next test:** the user must note,
as a distinct observation, **which UI/behavior the Guide long-press actually
triggered** — since it has already varied at least once (expected:
Steam Overlay/recording UI; observed instead in the 13:23 session: an
Alt+Tab-equivalent task-switch UI). Record it as one of:
- Steam Overlay / recording UI
- Alt+Tab-equivalent task switch
- Other (describe)

If Guide-long-press-alone reproduces DEAD → LIVE, that is evidence that
video recording is **not** necessary for the revival (supporting, not
proving, the §3.1 hypothesis). It would still leave open which of
Guide-press itself / task-switch processing / Steam Input re-evaluation /
Overlay is the actual mechanism — do not resolve that from this test alone.

Do not implement any new patch, Overlay-detection observer, or fix until this
next real-machine test has been read-only analyzed.
