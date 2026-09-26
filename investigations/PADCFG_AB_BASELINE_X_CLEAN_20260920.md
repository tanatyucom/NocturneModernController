# PadCfg / CommonConfig A/B test — CLEAN baseline at X

Captured 2026-09-20 21:36:47, from `MelonLoader/Latest.log` lines 726-834.
Native Controller Key Config confirmed by user: `Field/Dungeon Menu = X`,
no setting changed during this play session (clean single-state capture).

This is byte-for-byte identical to the earlier (session-mixed, discarded)
capture, which retroactively suggests that earlier capture was not actually
contaminated — but this file is the one to trust going forward.

## PadCfg (31 entries, unchanged from prior baseline)

Same as `PADCFG_AB_BASELINE_20260920.md`. FD_CmdMenu (index=7) pad1=12, pad2=0.
FD_Automap (index=16) pad1=11, pad2=0.

## CommonConfig (14 entries)

| index | pad1(raw) | pad2(raw) | type |
|---|---|---|---|
| 0 | 89 | 0 | NONE |
| 1 | 92 | 0 | NONE |
| 2 | 91 | 0 | NONE |
| 3 | 90 | 0 | NONE |
| 4 | 45 | 0 | NONE |
| 5 | 107 | 0 | NONE |
| 6 | 1 | 0 | NONE |
| 7 | 39 | 0 | NONE |
| 8 | 41 | 0 | NONE |
| 9 | 50 | 0 | NONE |
| 10 | 52 | 0 | NONE |
| 11 | 32 | 0 | NONE |
| 12 | 35 | 0 | NONE |
| 13 | 42 | 0 | NONE |

## Next step

User changes native Controller Key Config `Field/Dungeon Menu: X -> Y`,
confirms/saves, relaunches the game, plays without further changes, exits.
Re-read the log and diff every PadCfg and CommonConfig row against this file.
