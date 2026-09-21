# GetConfigGamePad A/B/A result — Y → X → Y (2026-09-21) — CONFIRMED

## 条件

- A/B/Aの最終ステップ: 純正Controller Key Configで `Field/Dungeon Menu: X → Y`（元に戻す）→ ゲーム完全終了 → 再起動
- ソース: `Latest.log` 810〜845行、`19:38:59.665`〜`19:38:59.672`
- deploy DLL SHA-256: `67691F3A5FA02F270834C3CBE341EB6BA425CE17374FDC58080ACD0E644EA9AC`（3回とも同一DLL、再ビルドなし）
- 全34件（index 0..33）とも `ERROR` なし、read成功

## A/B/A全結果比較

| index | Y (1回目, baseline) | X | Y (2回目, ABA) |
|---|---|---|---|
| 0-6 | 不変 | 不変 | 不変 |
| **7** | **11** | **12** | **11** |
| 8-33 | 不変 | 不変 | 不変 |

機械的diff結果:
- Y(1回目) vs X: index=7のみ diff（11→12）
- Y(1回目) vs Y(2回目): **完全一致（diff=0）**

index=7以外の33件は3回の測定を通じて完全に不変。

## 判定: CONFIRMED

SESSION_RESUME_NOTES.md §13の格上げ条件「純正GUI変更に追従することをA/B/Aで確認したら、`GetConfigGamePad(int)` = current GAME binding read path としてCONFIRMED」を満たした。

**CONFIRMED**:
- `dds3ConfigGamePadSteam.GetConfigGamePad(int index)` は current GAME binding のread-only read pathである。
- `index=7` は `Field/Dungeon Menu` action に対応する（純正GUIのY/X切り替えに1:1で追従、他indexとのクロストークなし）。
- `PadCfg`（`SIActionName`空間、31スロット）と`GetConfigGamePad`（34スロット、`0x22`）は別のindex空間だが、`FD_CmdMenu`と`GetConfigGamePad index=7`が数値上一致したのは偶然ではなく、両方とも同じaction識別子の異なる層での表現である可能性が高い（ただしこの対応自体はまだ未証明、他indexでの追加検証なしに一般化しない）。

## 次のステップ（未着手）

Step7本実装（GAME current bindingの読み取りAPI化）へ進める土台ができた。ただし:
- 他action（Menu以外）でも同様にindex対応を検証すべきか、Menu 1件のCONFIRMEDで十分としてStep7へ進むかは要判断。
- `index=7`の意味を`CFG_TYPE_GAMEPAD`/`SIActionName`等の既存enumと安易に同一視しない（§14方針、継続）。
- write API（`ChangeKey`等）には一切触れていない。read-only検証のみで完結。
