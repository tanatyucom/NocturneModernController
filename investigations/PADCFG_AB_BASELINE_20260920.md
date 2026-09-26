# PadCfg A/B test — baseline (before change)

Captured 2026-09-20 21:13:04, from `MelonLoader/Latest.log` lines 691-784,
build SHA-256 `f432acaedf2a6a55a8db82cb1089f4434f322f299c5dcc284e5b109907d4b1d4`.

Native Controller Key Config at capture time (as told by user, unverified in-game menu screenshot):

```
メニュー = Y
マップ   = START
```

`PadCfg.Length=31`

Not yet confirmed: does `PadCfg` index correspond 1:1 to `SIActionName` numeric
value, and does the raw `pad1`/`pad2` short value share a number space with
any of `SDF_PADMAP` / `InputAssign.KeyID` / `InputAssign.AssignCode` / mod
`ControllerButton`. Do not treat the enum-cast columns as meaningful until an
A/B change confirms the number space empirically.

| index | SIActionName | pad1(raw) | pad2(raw) | type |
|---|---|---|---|---|
| 0 | CMN_UpFront | 1 | 21 | NONE |
| 1 | CMN_DownBack | 2 | 22 | NONE |
| 2 | CMN_Left | 3 | 23 | NONE |
| 3 | CMN_Right | 4 | 24 | NONE |
| 4 | CMN_EnterAction | 10 | 0 | NONE |
| 5 | CMN_Cancel | 9 | 0 | NONE |
| 6 | CMN_UIDisp | 17 | 0 | NONE |
| 7 | **FD_CmdMenu** | **12** | 0 | NONE |
| 8 | FD_Camera_Up | 5 | 0 | NONE |
| 9 | FD_Camera_Down | 6 | 0 | NONE |
| 10 | FD_Camera_Left | 7 | 13 | NONE |
| 11 | FD_Camera_Right | 8 | 0 | NONE |
| 12 | FD_Turn_Left | 13 | 9 | NONE |
| 13 | FD_Turn_Right | 15 | 9 | NONE |
| 14 | FD_Return_Front | 9 | 0 | NONE |
| 15 | FD_Subjectivity | 18 | 0 | NONE |
| 16 | **FD_Automap** | **11** | 0 | NONE |
| 17 | BTL_SkillHelp | 26 | 0 | NONE |
| 18 | BTL_AutoBattle | 11 | 0 | NONE |
| 19 | BTL_NextTurn | 12 | 0 | NONE |
| 20 | EVT_SkipMsg | 11 | 0 | NONE |
| 21 | PZL_MapRot_Left | 13 | 0 | NONE |
| 22 | PZL_MapRot_Right | 15 | 0 | NONE |
| 23 | PZL_Menu | 12 | 0 | NONE |
| 24 | PZL_ChangeScroll | 11 | 0 | NONE |
| 25 | PZL_ChangeView | 9 | 0 | NONE |
| 26 | WRP_Move_Front | 5 | 0 | NONE |
| 27 | WRP_Move_Back | 6 | 0 | NONE |
| 28 | WRP_Move_LeftT | 7 | 13 | NONE |
| 29 | WRP_Move_Right | 8 | 0 | NONE |
| 30 | WRP_Punch | 9 | 0 | NONE |

Priority rows for the A/B test: **index 7 (FD_CmdMenu)** and **index 16 (FD_Automap)**.

## Planned change (user performs manually in the native Controller Key Config GUI)

```
メニュー: Y → X
```
(single item only, everything else left untouched, change confirmed/saved in-game)

## Re-capture procedure

1. User changes the one binding in the native config screen and confirms/saves.
2. Relaunch the game (or otherwise re-trigger `PadCfgDiagnosticProbe.Sample()` —
   it fires once per process, guarded by `FieldDashPatch.IsExplorationActive`).
3. Re-read `MelonLoader/Latest.log` for the new `[PadCfgProbe]` block.
4. Diff all 31 rows against this baseline.

No write path was implemented. No Harmony patch. No commit/push.
