# PadCfg / CommonConfig A/B test — result (X vs Y, clean single-change sessions)

Baseline: `PADCFG_AB_BASELINE_X_CLEAN_20260920.md` (Field/Dungeon Menu = X,
captured 21:36:47, no mid-session setting changes).

After: captured 2026-09-20 21:39:00, from `MelonLoader/Latest.log` lines
706-814, immediately after user changed `Field/Dungeon Menu: X -> Y`,
confirmed/saved in the native Controller Key Config screen, then relaunched
the game with no further changes during the session.

## Result: CONFIRMED — zero difference

- `PadCfg.Length`: 31 -> 31 (unchanged)
- All 31 `PadCfg` rows (index/pad1/pad2/type): byte-for-byte identical,
  including `FD_CmdMenu` (index=7, pad1=12) and `FD_Automap` (index=16, pad1=11).
- `CommonConfig.Length`: 14 -> 14 (unchanged)
- All 14 `CommonConfig` rows: byte-for-byte identical.

This is the second independent clean A/B run (X-clean baseline vs Y-clean
after) confirming the same null result seen in the earlier session-mixed run.

## Conclusion

Neither `SteamInputAssign.PadCfg` nor `SteamInputAssign.CommonConfig` reflects
the player's current native Controller Key Config binding. Both are ruled out
as the GAME Binding SSoT read path. The live value the native config screen
writes to (and that `padcheck`/`IsCheck` presumably reads at runtime) lives
somewhere else — a field/array not yet inspected, or a runtime-computed value
rather than a static array read once at load. Static managed-metadata
inspection has been exhausted for these two candidates; the write path
(`dds3ConfigGamePadSteam.ChangeKey` etc.) is native machine code and cannot be
traced further without a disassembler (Ghidra/IDA), which is not available in
this environment.
