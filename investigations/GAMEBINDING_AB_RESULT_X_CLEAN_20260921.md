# GetConfigGamePad A/B result — Y → X (2026-09-21)

## 条件

- 純正Controller Key Configで `Field/Dungeon Menu: Y → X` に変更 → ゲーム完全終了 → 再起動
- ソース: `Latest.log` 845〜880行、`19:35:38.158`〜`19:35:38.163`
- deploy DLL SHA-256: `67691F3A5FA02F270834C3CBE341EB6BA425CE17374FDC58080ACD0E644EA9AC`（Y baselineと同一DLL、再ビルドなし）
- 全34件（index 0..33）とも `ERROR` なし、read成功

## X raw値（index → raw）

| index | raw | index | raw | index | raw | index | raw |
|---|---|---|---|---|---|---|---|
| 0 | 1 | 9 | 6 | 18 | 11 | 27 | 2 |
| 1 | 2 | 10 | 7 | 19 | 15 | 28 | 3 |
| 2 | 3 | 11 | 8 | 20 | 11 | 29 | 4 |
| 3 | 4 | 12 | 13 | 21 | 13 | 30 | 9 |
| 4 | 10 | 13 | 15 | 22 | 15 | 31 | 0 |
| 5 | 9 | 14 | 9 | 23 | 12 | 32 | 0 |
| 6 | 17 | 15 | 18 | 24 | 11 | 33 | 0 |
| 7 | **12** | 16 | 26 | 25 | 9 | | |
| 8 | 5 | 17 | 25 | 26 | 1 | | |

## Y baseline (`GAMEBINDING_AB_BASELINE_Y_CLEAN_20260921.md`) との diff

機械的行diffの結果、**index=7のみ**変化:

```
index=7: Y raw=11 → X raw=12
```

他33件（index 0-6, 8-33）は完全一致（diff=0）。

## 判定

SESSION_RESUME_NOTES.md §13の判定基準:「1要素のみ変化 → 非常に有力。そのindexをMenu candidateとする。」に該当。

→ **index=7 が `Field/Dungeon Menu` のMenu candidateとして非常に有力（STRONGLY SUPPORTED）。**

ただしCONFIRMEDへの格上げには、A/B/A（X→Y再変更で元に戻し、再度34件一致することを確認）が必要（§13手順4）。まだ実施していない。

index=7という番号は、`PadCfgDiagnosticProbe`側で観測済みの`SIActionName.FD_CmdMenu`のindex（`PadCfg`内、31スロット空間、こちらはX/Y不変・SSoTから除外済み）と数値上たまたま一致しているが、**別のindex空間であり、この一致を意味あるものと決め打ちしない**（enum名の扱い、§14の方針通り）。

## 次の手順

ユーザーにX→Y（元の状態）へ戻してもらい、ゲーム完全終了→再起動→再度34件取得してA/B/Aを完成させる。
