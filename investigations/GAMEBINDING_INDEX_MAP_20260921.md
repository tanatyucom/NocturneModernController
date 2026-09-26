# GetConfigGamePad index → GAME Action 対応表 A/B調査ログ (2026-09-21)

CONFIRMEDした対応のみ`SESSION_RESUME_NOTES.md`へ後で追記する。本ファイルは調査過程の記録。

## CONFIRMED済み

| index | GAME Action(画面表示名) | raw値の意味 | Evidence |
|---|---|---|---|
| 7 | コマンドメニュー(旧称: Field/Dungeon Menu, FD_CmdMenu) | Y=11, X=12 | A/B/A（§17, §18） |
| 16 | オートマップ表示(FIELD/DUNGEON, 旧称: Map) | START=26, RT=16 | A/B/A（本ファイル#2） |

画面表示名は`GAMEPAD_UI_ACTION_LIST_20260921.md`（実機スクリーンショット6枚、2026-09-21）で確認済み。

## 調査中: #2 Map / オートマップ系

### baseline（変更前、Menu=Yの既存キャプチャを流用）

Field/Dungeon Menu=Y、Map=START（純正設定、未変更）の状態。`Mods/NocturneModernController.actions.json`の`GameActionBindingsRaw`（exploration active後に確定、readyログ20:15:19.687、Menu A/B/Aと同一セッション）から抽出。以前保存済みの`GAMEBINDING_AB_BASELINE_Y_CLEAN_20260921.md`のY baselineと完全一致（diff=0、確認済み）。

| index | raw | index | raw | index | raw | index | raw |
|---|---|---|---|---|---|---|---|
| 0 | 1 | 9 | 6 | 18 | 11 | 27 | 2 |
| 1 | 2 | 10 | 7 | 19 | 15 | 28 | 3 |
| 2 | 3 | 11 | 8 | 20 | 11 | 29 | 4 |
| 3 | 4 | 12 | 13 | 21 | 13 | 30 | 9 |
| 4 | 10 | 13 | 15 | 22 | 15 | 31 | 0 |
| 5 | 9 | 14 | 9 | 23 | 12 | 32 | 0 |
| 6 | 17 | 15 | 18 | 24 | 11 | 33 | 0 |
| 7 | 11 | 16 | 26 | 25 | 9 | | |
| 8 | 5 | 17 | 25 | 26 | 1 | | |

### 次の手順

1. ユーザーが純正Controller Key Config画面で「Map」に該当する項目（現在STARTに割当）を確認し、空いている別ボタンへ変更。変更先はユーザーが画面を見て選択（Claude側では仮定しない）。
2. ゲーム完全終了 → 再起動 → exploration activeまで進める。
3. `GetConfigGamePad(0..33)`再取得（`GameBindingProbe`のログ、または`actions.json`の`GameActionBindingsRaw`）。
4. baselineとの差分indexのみ抽出。
5. 元のSTARTへ戻し、A/B/Aまで確認。

### A/B結果（#1変更: Map STARTC→RT）

ソース: `Latest.log` 800〜836行、`20:24:39.119`〜`20:24:39.465`（`GameBindingProbe`出力、exploration active後）。ゲーム完全終了→再起動→exploration active到達後の測定。

機械的diffの結果、**index=16のみ**変化:

```
index=16: baseline(Map=START) raw=26 → after(Map=RT) raw=16
```

他33件（index 0-15, 17-33）は完全一致（diff=0）。

**判定**: 1要素のみ変化 → 「Map」= index=16としてSTRONGLY SUPPORTED。CONFIRMEDへ格上げにはA/B/A（STARTへ戻して再確認）が必要（未実施）。

raw値(26→16)の物理ボタン意味は未確定。既存`GameBindingSnapshotEntry`(`GetAssignCode`ベース、別index/value空間)ではraw=26相当の値が"R2"に対応していたが、**これは別のenum空間であり、数値一致だけで同一視しない**(方針継続)。

### A/B/A結果（#1変更: Map START→RT→START）— CONFIRMED

ソース: `Latest.log` 805〜841行、`20:27:44.376`〜`20:27:44.702`（`GameBindingProbe`出力、exploration active後）。Mapを元のSTARTへ戻し、ゲーム完全終了→再起動→exploration active到達後の測定。

| index | baseline(START, 1回目) | RT | START(A/B/A, 2回目) |
|---|---|---|---|
| 0-15, 17-33 | 不変 | 不変 | 不変 |
| **16** | **26** | **16** | **26** |

index=16以外の33件は3回の測定を通じて完全に不変。

**判定: CONFIRMED**

- `index=16` は `Map`（オートマップ系）actionに対応する（純正GUIのSTART/RT切り替えに1:1で追従、他indexとのクロストークなし、A/B/Aで再現）。
- raw値: `26 = START`, `16 = RT`（Menu同様、この対応もindex=16限定であり、他indexへ一般化しない）。

## 対応表サマリ（2026-09-21時点、CONFIRMEDのみ）

| index | GAME Action(画面表示名) | raw→物理ボタン |
|---|---|---|
| 7 | コマンドメニュー | 11=Y, 12=X |
| 16 | オートマップ表示 | 26=START, 16=RT |

**現在ステータス**: #1(オートマップ表示)完了、CONFIRMED。次のaction候補は未着手。「視点変更（上/下/左/右）」はグレーアウトでA/B対象外（`GAMEPAD_UI_ACTION_LIST_20260921.md`参照）。PUZZLE区分5項目はゲーム進行上未到達のためA/B対象外（ユーザー確認）。

## 調査中: #3 BATTLE「オートバトル」

baseline: 上記「#2 Map/オートマップ表示」で使用したbaselineテーブル（index 0-33、コマンドメニュー=Y・オートマップ表示=START、他未変更）をそのまま流用。BATTLE区分の設定もグローバルなconfig配列上にあり、フィールド探索中に読み取り可能なため、戦闘に入らなくても純正設定変更→フィールド探索での再測定で足りる。

現在の割当: オートバトル = Y（`GAMEPAD_UI_ACTION_LIST_20260921.md`のBATTLE表参照）。

### 次の手順

1. ユーザーが純正Controller Key Config「BATTLE」タブで「オートバトル」（現在Y）を、空いている別ボタンへ変更。
2. ゲーム完全終了 → 再起動 → exploration activeまで進める。
3. `GetConfigGamePad(0..33)`再取得（`GameBindingProbe`ログ、または`actions.json`の`GameActionBindingsRaw`）。
4. baselineとの差分indexのみ抽出。
5. 元のYへ戻し、A/B/Aまで確認。

**現在ステータス**: baseline確認済み（既存流用）、ユーザーの設定変更待ち。

### A/B結果（#3変更: オートバトル Y→X）

ソース: `Latest.log` 830〜864行、`20:57:18.891`〜`20:57:18.995`（`GameBindingProbe`出力、exploration active後）。ユーザー確認: オートバトルをYからXボタンへ変更。

機械的diffの結果、**index=18のみ**変化:

```
index=18: baseline(オートバトル=Y) raw=11 → after(オートバトル=X) raw=12
```

他33件（index 0-17, 19-33）は完全一致（diff=0）。

**判定**: 1要素のみ変化 → 「オートバトル」= index=18としてSTRONGLY SUPPORTED。CONFIRMEDへ格上げにはA/B/A（Yへ戻して再確認）が必要（未実施）。

**raw値の補足観察（未確定・慎重に扱う）**: `index=7`（コマンドメニュー、Y=11/X=12）と`index=18`（オートバトル、Y=11/X=12）で、Y/Xそれぞれのraw値が偶然一致している。複数indexにわたって"11=Y, 12=X"という体系が成立している可能性を示唆するが、**まだ2件のみの観察であり、他indexへの一般化・raw value enumの確定は行わない**（方針継続、「enum数値一致だけでcastしない」）。

### A/B/A結果（#3変更: オートバトル Y→X→Y）— CONFIRMED

ソース: `Latest.log` 815〜849行、`21:00:40.783`〜`21:00:40.912`（`GameBindingProbe`出力、exploration active後）。オートバトルを元のYへ戻し、ゲーム完全終了→再起動→exploration active到達後の測定。

| index | baseline(Y, 1回目) | X | Y(A/B/A, 2回目) |
|---|---|---|---|
| 0-17, 19-33 | 不変 | 不変 | 不変 |
| **18** | **11** | **12** | **11** |

index=18以外の33件は3回の測定を通じて完全に不変。

**判定: CONFIRMED**

`index=18` は「オートバトル」（BATTLE）actionに対応する（純正GUIのY/X切り替えに1:1で追従、他indexとのクロストークなし、A/B/Aで再現）。raw値: `11=Y, 12=X`（index=7と同じraw値だが、これはindex=7・18の2件で確認された対応であり、他indexへは一般化しない）。

## 対応表サマリ（2026-09-21時点、CONFIRMEDのみ・更新）

| index | GAME Action(画面表示名) | raw→物理ボタン |
|---|---|---|
| 7 | コマンドメニュー | 11=Y, 12=X |
| 16 | オートマップ表示 | 26=START, 16=RT |
| 18 | オートバトル | 11=Y, 12=X |

**現在ステータス**: #3(オートバトル)完了、CONFIRMED。

## 調査中: #4 EVENT「テキストの早送り」（消去法アプローチ）

baselineで`raw=11`のindexは`{7, 18, 20, 24}`の4件。CONFIRMED済みの7(コマンドメニュー)・18(オートバトル)を除くと、残り候補は`index=20`と`index=24`の2件（当初想定の1件には絞り込めなかった）。現在アクセス可能なY割当の未確定actionは「テキストの早送り」(EVENT)のみのため、1回のA/Bで候補を確定させる。

### A/B結果（#4変更: テキストの早送り Y→X）

ソース: `Latest.log` 825〜859行、`21:12:27.893`〜`21:12:28.217`（`GameBindingProbe`出力、exploration active後）。ユーザー確認: テキストの早送りをYからXへ変更。

機械的diffの結果、**index=20のみ**変化:

```
index=20: baseline(テキストの早送り=Y) raw=11 → after(テキストの早送り=X) raw=12
```

`index=24`は`raw=11`のまま不変。他32件も完全一致（diff=0）。

**判定**: 1要素のみ変化 → 「テキストの早送り」= index=20としてSTRONGLY SUPPORTED。CONFIRMEDへ格上げにはA/B/A（Yへ戻して再確認）が必要（未実施）。`index=24`は依然としてraw=11の未確定候補として残る（別のY割当action、`GAMEPAD_UI_ACTION_LIST_20260921.md`で未特定のもの、と推定されるが未確認）。

### A/B/A結果（#4変更: テキストの早送り Y→X→Y）— CONFIRMED

ソース: `Latest.log` 830〜864行、`21:15:46.610`〜`21:15:46.964`（`GameBindingProbe`出力、exploration active後）。テキストの早送りを元のYへ戻し、ゲーム完全終了→再起動→exploration active到達後の測定。

| index | baseline(Y, 1回目) | X | Y(A/B/A, 2回目) |
|---|---|---|---|
| 0-19, 21-33 | 不変 | 不変 | 不変 |
| **20** | **11** | **12** | **11** |
| 24(参考) | 11 | 11(不変) | 11 |

index=20以外の33件（index=24含む）は3回の測定を通じて完全に不変。

**判定: CONFIRMED**

`index=20` は「テキストの早送り」（EVENT）actionに対応する（純正GUIのY/X切り替えに1:1で追従、他indexとのクロストークなし、A/B/Aで再現）。raw値: `11=Y, 12=X`（index=7,18と同じraw値パターン、3件目の一致）。

`index=24`（raw=11のまま不変）は依然として**別の、まだ特定されていないY割当action**の候補として残る。`GAMEPAD_UI_ACTION_LIST_20260921.md`で確認した項目のうち、アイコンが不明瞭で断定できなかったもの（例: BATTLE「スキルヘルプON/OFF」、COMMON「UI表示ON/OFF」等）が該当する可能性があるが、未確認・推測しない。

## 対応表サマリ（2026-09-21時点、CONFIRMEDのみ・更新）

| index | GAME Action(画面表示名) | raw→物理ボタン |
|---|---|---|
| 7 | コマンドメニュー | 11=Y, 12=X |
| 16 | オートマップ表示 | 26=START, 16=RT |
| 18 | オートバトル | 11=Y, 12=X |
| 20 | テキストの早送り | 11=Y, 12=X |

**未確定候補**: `index=24`（raw=11、Y割当の別actionの可能性、未特定）

## index=24 = PUZZLE「スクロール切替」（STRONGLY SUPPORTED、消去法）

**判定: STRONGLY SUPPORTED**（CONFIRMEDではない）

**根拠**:
- baselineでraw=11(Y)のindexは`{7, 18, 20, 24}`の4件。
- 7/18/20はそれぞれ「コマンドメニュー」/「オートバトル」/「テキストの早送り」とA/B/AでCONFIRMED済み。
- `GAMEPAD_UI_ACTION_LIST_20260921.md`のスクリーンショット一覧上、現在Y割当と確認できるactionは「コマンドメニュー」「オートバトル」「テキストの早送り」「スクロール切替」(PUZZLE)の4件のみ。
- 残るY割当actionが「スクロール切替」のみであるため、`index=24 = スクロール切替`が消去法により有力。

**CONFIRMEDにしない理由**: PUZZLEはゲーム進行上未到達のため、実機A/B/Aが未実施。他のスクショ未確認項目（アイコン不明瞭だったBATTLE「スキルヘルプON/OFF」、COMMON「UI表示ON/OFF」等）が実はY割当である可能性も完全には排除できない。

**格上げ条件**: 将来PUZZLEへ到達した際、「スクロール切替」をY→X→Yと変更し、`index=24`のraw値が`11↔12`で追従することをA/B/Aで確認できればCONFIRMEDへ格上げする。

**現在ステータス**: #4(テキストの早送り)完了、CONFIRMED。`index=24`はSTRONGLY SUPPORTED（宿題として保留）。

## 効率化: 小規模バッチA/B/A方式（2026-09-21以降）

1 action = 2リブート(A/B, A/B/A)の方式から、**最大3 actionを同時に異なるボタンへ変更し、2リブートでバッチ全体をCONFIRMEDにする**方式へ切替。

**ルール**:
- 1バッチ最大3 action
- 各actionの変更先ボタンは必ず別（重複禁止）
- 変更先は既にraw対応を実測済みのボタンを優先（X=12, RT=16, START=26等）
- 純正GUI上で変更後の割当が実際に反映されたことを確認
- 変更action以外は触らない
- 変更action数とdiff index数が一致しない、同じraw変化で対応が一意に決まらない、他actionまで変化した場合はそのバッチをCONFIRMED扱いにせず保留

## 調査中: #5 バッチ1（決定・アクション / キャンセル / 次に回す）

baseline: #4のA/B/A完了時点（`Latest.log` 830〜864行、`21:15:46.610`）の34件をそのまま流用（テキストの早送り=Y復元後、他は未変更）。

| Action(画面表示名) | context | 現在の割当 | 変更先 |
|---|---|---|---|
| 決定・アクション | COMMON | A | X |
| キャンセル | COMMON | B | RT |
| 次に回す | BATTLE | RB | START |

期待値: 変化するindexが3つ現れ、新raw値がそれぞれ`12(X) / 16(RT) / 26(START)`と一致すれば、3件を一意に対応付けてSTRONGLY SUPPORTEDとする。

### バッチ1 結果（想定外: 4変化）

ソース: `Latest.log` 815〜849行、`21:59:59.427`〜`21:59:59.532`（`GameBindingProbe`出力、exploration active後）。

```
index=4:  baseline=10 → after=12  (期待通りX=12)
index=5:  baseline=9  → after=16  (期待通りRT=16)
index=19: baseline=15 → after=26  (期待通りSTART=26)
index=23: baseline=12 → after=10  (想定外)
```

他30件は不変。変更action数(3)とdiff index数(4)が不一致 → ルール上バッチ全体を保留扱い。

index=4/5/19は期待した新raw値(X=12,RT=16,START=26)と一致し、決定・アクション/キャンセル/次に回すへの対応が有力（ただし正式にはCONFIRMED/STRONGLY SUPPORTEDとしない、ルール通り保留）。index=23(12→10)は「キャンセル」変更に伴う重複解消(duplicate handling)の副作用の可能性が高い（`docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md`に記載の「Cancel/Return/Viewpoint switching関連の重複例外」と符合）。index=23の変更前raw=12はX相当であり、何らかの別actionが元々Xに割当られていて、決定・アクションがAからXへ移った際に玉突きで動かされた可能性がある(未確認、推測に留める)。

### 想定外4件目の原因判明（ユーザー確認、2026-09-21、訂正あり）

ユーザーが純正Controller Key Config画面を確認したところ、**PUZZLE「メニュー」がAボタンになっていた**（デフォルトはX、`GAMEPAD_UI_ACTION_LIST_20260921.md`参照）。

**正確な経緯（ユーザー訂正）**: 「決定・アクション」をA→Xに変更したことによる重複解消(duplicate handling)で、元々Xに割当られていた「PUZZLE メニュー」の割当が**外れて空欄（未割当）になった**。ゲーム側が「未割当のactionがあります、何か割り当ててください」という趣旨の表示を出し、**ユーザーが手動でAを選択して割り当てた**。「自動的にAへ移動した」のではなく、「空欄→ユーザーが能動的にAを選んだ」が正しい。

これにより`index=23`（baseline raw=12 → after raw=10）の意味が説明できる:
- `index=23` = PUZZLE「メニュー」
- raw値: `12=X`(元)。重複解消で一時的に未割当（空欄）状態を経由し、ユーザーが`A`を選択した結果`10=A`になった。

**運用上の注意点（今後のバッチ変更に反映）**: 複数actionを同時に変更すると、重複解消により意図しないactionが「空欄」になり、ゲーム側から再割当を要求される場合がある。バッチ変更後は、変更対象以外の項目が空欄になっていないか純正GUI上で確認することを手順に追加する。

**評価**: この4件目は意図した直接変更ではなく重複解消の副作用によるものだが、ユーザーが画面上で実際の結果（メニュー=A）を確認しているため、単なる推測ではなく実測に基づく対応付けである。ただし通常のA/B/A（意図的な1項目変更→確認→復元）とは性質が異なるため、**STRONGLY SUPPORTED**（CONFIRMEDではない）として扱う。将来、PUZZLEへ実際に到達した際に、メニューを独立してY→X→Y等でA/B/Aできれば格上げ可能。

新たなraw値対応の手がかり: `A=10`（今回の観察のみ、他indexへの一般化はしない）。

### バッチ1 最終判定（4件すべて説明済み、STRONGLY SUPPORTED）

| index | GAME Action(推定) | raw変化 | 判定 |
|---|---|---|---|
| 4 | 決定・アクション(COMMON) | 10(A)→12(X) | STRONGLY SUPPORTED（意図的変更、期待raw一致） |
| 5 | キャンセル(COMMON) | 9(B?)→16(RT) | STRONGLY SUPPORTED（意図的変更、期待raw一致） |
| 19 | 次に回す(BATTLE) | 15(RB?)→26(START) | STRONGLY SUPPORTED（意図的変更、期待raw一致） |
| 23 | メニュー(PUZZLE) | 12(X)→10(A) | STRONGLY SUPPORTED（重複解消で空欄化→ユーザーが手動でA選択） |

CONFIRMEDへ格上げするには、それぞれを個別にA/B/Aで戻し確認する必要がある（次のA/B/Aステップで4件まとめて元に戻し確認予定）。

## 対応表サマリ（2026-09-21時点、更新）

**CONFIRMED**:
```
index=7:  コマンドメニュー   raw 11=Y, 12=X
index=16: オートマップ表示   raw 26=START, 16=RT
index=18: オートバトル       raw 11=Y, 12=X
index=20: テキストの早送り   raw 11=Y, 12=X
```

**STRONGLY SUPPORTED**:
```
index=4:  決定・アクション(COMMON)  raw 10=A, 12=X
index=5:  キャンセル(COMMON)        raw 9=B(推定), 16=RT
index=19: 次に回す(BATTLE)          raw 15=RB(推定), 26=START
index=23: メニュー(PUZZLE)          raw 12=X, 10=A
index=24: スクロール切替(PUZZLE)    raw=11(Y)、未A/B
```

### バッチ1 A/B/A結果 — CONFIRMED

ソース: `Latest.log` 800〜834行、`22:07:33.865`〜`22:07:33.969`（`GameBindingProbe`出力、exploration active後）。決定・アクション/キャンセル/次に回すを元に戻し（メニューも連動して元に戻ることを期待）、ゲーム完全終了→再起動→exploration active到達後の測定。

| index | baseline(1回目) | バッチ1変更後 | 復元後(A/B/A, 2回目) |
|---|---|---|---|
| 4 | 10 | 12 | **10** |
| 5 | 9 | 16 | **9** |
| 19 | 15 | 26 | **15** |
| 23 | 12 | 10 | **12** |
| その他30件 | 不変 | 不変 | 不変 |

4index全てが正確にbaseline値へ復元。他30件も完全一致。

**判定: CONFIRMED（4件とも）**

## 対応表サマリ（2026-09-21時点、最終・CONFIRMEDのみ）

| index | GAME Action(画面表示名) | raw→物理ボタン |
|---|---|---|
| 4 | 決定・アクション(COMMON) | 10=A, 12=X |
| 5 | キャンセル(COMMON) | 9=B, 16=RT |
| 7 | コマンドメニュー(FIELD/DUNGEON) | 11=Y, 12=X |
| 16 | オートマップ表示(FIELD/DUNGEON) | 26=START, 16=RT |
| 18 | オートバトル(BATTLE) | 11=Y, 12=X |
| 19 | 次に回す(BATTLE) | 15=RB, 26=START |
| 20 | テキストの早送り(EVENT) | 11=Y, 12=X |
| 23 | メニュー(PUZZLE) | 12=X, 10=A |

**STRONGLY SUPPORTED（未A/B/A）**:
```
index=24: スクロール切替(PUZZLE)  raw=11(Y)、未A/B（PUZZLE到達待ち）
```

**判明したraw値**（index毎の観察、他indexへの一般化はしない）: `A=10, B=9, Y=11, X=12, RB=15(推定), RT=16, START=26`

**現在ステータス**: 8件CONFIRMED、1件STRONGLY SUPPORTED（宿題）。

## 調査中: #6 バッチ2（スキルヘルプON/OFF / 視点を正面に戻す / UI表示ON/OFF）

baseline: バッチ1 A/B/A完了時点（`Latest.log` 800〜834行、`22:07:33.865`）の34件をそのまま流用。

| Action(画面表示名) | context | 現在の割当 | 変更先 | 備考 |
|---|---|---|---|---|
| スキルヘルプON/OFF | BATTLE | SELECT | RT(16) | RTは現在CONFIRMED表のどのactionにも未使用、比較的安全 |
| 視点を正面に戻す | FIELD/DUNGEON | B | RB(15) | RBは「次に回す」(BATTLE)で使用中だが別context、Yの前例からモード排他的共存を期待 |
| UI表示ON/OFF | COMMON | L3 | START(26) | STARTは「オートマップ表示」(FIELD/DUNGEON)で使用中。COMMON項目は全モード共通のため、バッチ1「決定・アクション」同様に衝突・重複解消リスクが高い（意図的にリスクを取るケース） |

**注意点（バッチ1の教訓を反映）**: 変更後、純正Controller Key Config画面全体を確認し、対象外の項目が空欄（未割当）になっていないか必ず確認する。空欄が見つかった場合は、その項目名と元の割当を記録し、どう再割当したかも記録する。

### バッチ2 結果（想定通り6変化、連鎖玉突き込み）

ソース: `Latest.log` 815〜849行、`22:20:48.027`〜`22:20:48.130`（`GameBindingProbe`出力、exploration active後）。

ユーザー確認: 意図した3件（スキルヘルプON/OFF→RT、視点を正面に戻す→RB、UI表示ON/OFF→START）は画面上で正しく反映。加えて、重複解消の連鎖により以下3件が空欄→ユーザーが手動再割当:
- UI表示ON/OFF(COMMON)がSTART奪取 → 元STARTだった「オートマップ表示」が空欄 → ユーザーがLBを選択
- 「オートマップ表示」がLB奪取 → 元LBだった「視点変更（左回転）」が空欄 → ユーザーがLTを選択
- 「視点を正面に戻す」がRB奪取 → 元RBだった「視点変更（右回転）」が空欄 → ユーザーがRTを選択

機械的diffの結果、**6indexが変化**（意図した3件+連鎖玉突き3件、想定通り）:

```
index=6:  17 → 26   (UI表示ON/OFF: L3→START)
index=12: 13 → 14   (視点変更(左回転): LB→LT)
index=13: 15 → 16   (視点変更(右回転): RB→RT)
index=14: 9  → 15   (視点を正面に戻す: B→RB)
index=16: 26 → 13   (オートマップ表示、既存CONFIRMED済み index: START→LB)
index=17: 25 → 16   (スキルヘルプON/OFF: SELECT→RT)
```

他28件は完全一致。各indexのbaseline値・変更後値が既知/推定raw値と全て整合的に説明できる:

| index | GAME Action(推定) | context | raw変化 | 整合性 |
|---|---|---|---|---|
| 6 | UI表示ON/OFF | COMMON | 17(L3,新規)→26(START,既知) | ✓ |
| 12 | 視点変更（左回転） | FIELD/DUNGEON | 13(LB,新規)→14(LT,新規) | ✓ |
| 13 | 視点変更（右回転） | FIELD/DUNGEON | 15(RB,既知)→16(RT,既知) | ✓ |
| 14 | 視点を正面に戻す | FIELD/DUNGEON | 9(B,既知)→15(RB,既知) | ✓ |
| 16 | オートマップ表示 | FIELD/DUNGEON | 26(START,既知,CONFIRMED済み)→13(LB,新規) | ✓ index=16は既にCONFIRMED済みのため、これは独立した追加傍証 |
| 17 | スキルヘルプON/OFF | BATTLE | 25(SELECT,新規)→16(RT,既知) | ✓ |

**新規raw値**（今回の観察から）: `L3=17, LB=13, LT=14, SELECT=25`（indexごとの観察、他indexへの一般化はしない）。

`index=16`が「オートマップ表示」であることは既にA/B/A CONFIRMED済み。その変更後値(13)が今回「LB」への手動再割当と整合することは、`13=LB`という新しいraw値対応の独立した傍証にもなる。

**判定**: 全6件がSTRONGLY SUPPORTED（意図的変更3件+重複解消の間接確認3件）。CONFIRMEDへ格上げにはA/B/A（全て元に戻す）が必要。

### バッチ2 A/B/A結果 — CONFIRMED

ソース: `Latest.log` 875〜909行、`22:25:49.404`〜`22:25:49.532`（`GameBindingProbe`出力、exploration active後）。6項目全てを元に戻し（連鎖玉突き分も含め全て復元）、ゲーム完全終了→再起動→exploration active到達後の測定。

機械的diffの結果、**baselineとの差分は0件**。6index全てが正確に復元、他28件も完全一致。

**判定: CONFIRMED（6件とも）**

## 対応表サマリ（2026-09-21時点、最終・CONFIRMEDのみ）

| index | GAME Action(画面表示名) | context | raw→物理ボタン |
|---|---|---|---|
| 4 | 決定・アクション | COMMON | 10=A, 12=X |
| 5 | キャンセル | COMMON | 9=B, 16=RT |
| 6 | UI表示ON/OFF | COMMON | 17=L3, 26=START |
| 7 | コマンドメニュー | FIELD/DUNGEON | 11=Y, 12=X |
| 12 | 視点変更（左回転） | FIELD/DUNGEON | 13=LB, 14=LT |
| 13 | 視点変更（右回転） | FIELD/DUNGEON | 15=RB, 16=RT |
| 14 | 視点を正面に戻す | FIELD/DUNGEON | 9=B, 15=RB |
| 16 | オートマップ表示 | FIELD/DUNGEON | 26=START, 13=LB |
| 17 | スキルヘルプON/OFF | BATTLE | 25=SELECT, 16=RT |
| 18 | オートバトル | BATTLE | 11=Y, 12=X |
| 19 | 次に回す | BATTLE | 15=RB, 26=START |
| 20 | テキストの早送り | EVENT | 11=Y, 12=X |
| 23 | メニュー | PUZZLE | 12=X, 10=A |

**STRONGLY SUPPORTED（未A/B/A）**: `index=24` = スクロール切替(PUZZLE)、raw=11(Y)、PUZZLE到達待ち。

**判明したraw値一覧**（indexごとの観察、他indexへの一般化はしない）:
```
A=10, B=9, Y=11, X=12, LB=13, LT=14, RB=15, RT=16, L3=17, SELECT=25, START=26
```

**現在ステータス**: 13件CONFIRMED、1件STRONGLY SUPPORTED（宿題）。

## 構造仮説: GetConfigGamePad index = 純正UI「GAMEPAD」タブの表示順（2026-09-21）

**仮説**: `GetConfigGamePad`のindex 0..30は、純正Controller Key Config画面「GAMEPAD」タブに表示される項目の並び順と1:1で一致する（`COMMON`→`FIELD/DUNGEON`→`BATTLE`→`EVENT`→`PUZZLE`→`WARP FIELD`の表示順、`GAMEPAD_UI_ACTION_LIST_20260921.md`参照）。index 31-33は画面外（未使用/予約の可能性、UNRESOLVED）。

**検証**: CONFIRMED済み13件全てのindexを、この表示順仮説から予測される位置と機械的に突き合わせた結果、**13件全て完全一致**（不一致0件）。

| index | 推定GAME Action | context | 状態 |
|---:|---|---|---|
| 0 | キャラ/カーソル移動（前） | COMMON | STRONGLY SUPPORTED（構造仮説） |
| 1 | キャラ/カーソル移動（後） | COMMON | STRONGLY SUPPORTED（構造仮説） |
| 2 | キャラ/カーソル移動（左） | COMMON | STRONGLY SUPPORTED（構造仮説） |
| 3 | キャラ/カーソル移動（右） | COMMON | STRONGLY SUPPORTED（構造仮説） |
| 4 | 決定・アクション | COMMON | **CONFIRMED** |
| 5 | キャンセル | COMMON | **CONFIRMED** |
| 6 | UI表示ON/OFF | COMMON | **CONFIRMED** |
| 7 | コマンドメニュー | FIELD/DUNGEON | **CONFIRMED** |
| 8 | 視点変更（上） | FIELD/DUNGEON | STRONGLY SUPPORTED（構造仮説、グレーアウト項目） |
| 9 | 視点変更（下） | FIELD/DUNGEON | STRONGLY SUPPORTED（構造仮説、グレーアウト項目） |
| 10 | 視点変更（左） | FIELD/DUNGEON | STRONGLY SUPPORTED（構造仮説、グレーアウト項目） |
| 11 | 視点変更（右） | FIELD/DUNGEON | STRONGLY SUPPORTED（構造仮説、グレーアウト項目） |
| 12 | 視点変更（左回転） | FIELD/DUNGEON | **CONFIRMED** |
| 13 | 視点変更（右回転） | FIELD/DUNGEON | **CONFIRMED** |
| 14 | 視点を正面に戻す | FIELD/DUNGEON | **CONFIRMED** |
| 15 | 客観/主観切替 | FIELD/DUNGEON | STRONGLY SUPPORTED（構造仮説、未A/B） |
| 16 | オートマップ表示 | FIELD/DUNGEON | **CONFIRMED** |
| 17 | スキルヘルプON/OFF | BATTLE | **CONFIRMED** |
| 18 | オートバトル | BATTLE | **CONFIRMED** |
| 19 | 次に回す | BATTLE | **CONFIRMED** |
| 20 | テキストの早送り | EVENT | **CONFIRMED** |
| 21 | マップ回転(左) | PUZZLE | STRONGLY SUPPORTED（構造仮説、未到達） |
| 22 | マップ回転(右) | PUZZLE | STRONGLY SUPPORTED（構造仮説、未到達） |
| 23 | メニュー | PUZZLE | **CONFIRMED** |
| 24 | スクロール切替 | PUZZLE | STRONGLY SUPPORTED（構造仮説+消去法の二重根拠、未到達） |
| 25 | 視点切替 | PUZZLE | STRONGLY SUPPORTED（構造仮説、未到達） |
| 26 | 移動（前） | WARP FIELD | STRONGLY SUPPORTED（構造仮説、未A/B） |
| 27 | 移動（後） | WARP FIELD | STRONGLY SUPPORTED（構造仮説、未A/B） |
| 28 | 移動（左） | WARP FIELD | STRONGLY SUPPORTED（構造仮説、未A/B） |
| 29 | 移動（右） | WARP FIELD | STRONGLY SUPPORTED（構造仮説、未A/B） |
| 30 | パンチ | WARP FIELD | STRONGLY SUPPORTED（構造仮説、未A/B） |
| 31-33 | 画面外/未使用の可能性 | - | UNRESOLVED（raw値は常に0、既存observation） |

**評価**: 13/13の完全一致は、単なる偶然（ランダムな34スロット配置で13件が構造仮説の予測位置と全て一致する確率は極めて低い）ではなく、強い構造的裏付けと判断する。ただし、これらは「UI表示順から推定」であり、個別のA/B/A実測ではないため、**構造仮説由来の項目はあくまでSTRONGLY SUPPORTEDに留め、CONFIRMEDへは格上げしない**。

## 調査中: #7 スポットチェック index=15「客観/主観切替」

baseline: バッチ2 A/B/A完了時点（`Latest.log` 875〜909行、`22:25:49.404`）の34件をそのまま流用。

| Action | context | 現在 | 変更先 |
|---|---|---|---|
| 客観/主観切替 | FIELD/DUNGEON | R3 | X（FIELD/DUNGEON内で未使用、START等の同context衝突回避） |

### A/B結果

ソース: `Latest.log` 825〜859行、`22:41:02.599`〜`22:41:02.703`（`GameBindingProbe`出力、exploration active後）。

機械的diffの結果、**index=15のみ**変化:

```
index=15: baseline(客観/主観切替=R3) raw=18 → after(客観/主観切替=X) raw=12
```

他33件は完全一致（diff=0）。構造仮説の予測（index=15=客観/主観切替）と一致。

**新規raw値**: `R3=18`（今回の観察のみ）。

**判定**: STRONGLY SUPPORTED。CONFIRMEDへ格上げにはA/B/A（R3へ戻して再確認）が必要（未実施）。

### A/B/A結果 — CONFIRMED

ソース: `Latest.log` 895〜929行、`22:42:52.163`〜`22:42:52.266`（`GameBindingProbe`出力、exploration active後）。客観/主観切替を元のR3へ戻し、ゲーム完全終了→再起動→exploration active到達後の測定。

| index | baseline(R3, 1回目) | X | R3(A/B/A, 2回目) |
|---|---|---|---|
| 0-14, 16-33 | 不変 | 不変 | 不変 |
| **15** | **18** | **12** | **18** |

index=15以外の33件は3回の測定を通じて完全に不変。

**判定: CONFIRMED**

`index=15` は「客観/主観切替」（FIELD/DUNGEON）actionに対応する。raw値: `18=R3, 12=X`。

**構造仮説アンカー更新**: COMMON(4-6)→FIELD/DUNGEON(7)→視点系(12-14)→**客観/主観切替(15)**→オートマップ表示(16)→BATTLE(17-19)→EVENT(20)→PUZZLE(23)まで、中盤の連続区間で仮説と完全一致するアンカーが得られた。次は末尾のWARP FIELD「パンチ」(index=30)でスポットチェック予定（到達可能性はユーザー確認待ち）。

## 対応表サマリ（2026-09-21時点、最終・CONFIRMEDのみ・14件）

| index | GAME Action(画面表示名) | context | raw→物理ボタン |
|---|---|---|---|
| 4 | 決定・アクション | COMMON | 10=A, 12=X |
| 5 | キャンセル | COMMON | 9=B, 16=RT |
| 6 | UI表示ON/OFF | COMMON | 17=L3, 26=START |
| 7 | コマンドメニュー | FIELD/DUNGEON | 11=Y, 12=X |
| 12 | 視点変更（左回転） | FIELD/DUNGEON | 13=LB, 14=LT |
| 13 | 視点変更（右回転） | FIELD/DUNGEON | 15=RB, 16=RT |
| 14 | 視点を正面に戻す | FIELD/DUNGEON | 9=B, 15=RB |
| 15 | 客観/主観切替 | FIELD/DUNGEON | 18=R3, 12=X |
| 16 | オートマップ表示 | FIELD/DUNGEON | 26=START, 13=LB |
| 17 | スキルヘルプON/OFF | BATTLE | 25=SELECT, 16=RT |
| 18 | オートバトル | BATTLE | 11=Y, 12=X |
| 19 | 次に回す | BATTLE | 15=RB, 26=START |
| 20 | テキストの早送り | EVENT | 11=Y, 12=X |
| 23 | メニュー | PUZZLE | 12=X, 10=A |

**判明したraw値一覧**（indexごとの観察、他indexへの一般化はしない）:
```
A=10, B=9, Y=11, X=12, LB=13, LT=14, RB=15, RT=16, L3=17, R3=18, SELECT=25, START=26
```

**現在ステータス**: 14件CONFIRMED。

## 調査中: #8 スポットチェック index=30「パンチ」(WARP FIELD、末尾アンカー)

baseline: index=15 A/B/A完了時点の34件をそのまま流用。「コンフィグ画面上の変更のみ、実際のWARP FIELDプレイには入らず、通常のフィールド探索状態でread」という条件で実施（GetConfigGamePadはグローバルなconfig配列を読むため、実際のWARP FIELDプレイは不要）。

| Action | context | 現在 | 変更先 |
|---|---|---|---|
| パンチ | WARP FIELD | B | X |

### A/B結果

ソース: `Latest.log` 800〜834行、`22:45:24.251`〜`22:45:24.356`（`GameBindingProbe`出力、exploration active後）。

機械的diffの結果、**index=30のみ**変化:

```
index=30: baseline(パンチ=B) raw=9 → after(パンチ=X) raw=12
```

他33件は完全一致（diff=0）。構造仮説の予測（index=30=パンチ）と一致。これで末尾のアンカーも的中。

**判定**: STRONGLY SUPPORTED。CONFIRMEDへ格上げにはA/B/A（Bへ戻して再確認）が必要（未実施）。

### A/B/A結果 — CONFIRMED

ソース: `Latest.log` 815〜849行、`22:47:07.607`〜`22:47:07.710`（`GameBindingProbe`出力、exploration active後）。パンチを元のBへ戻し、ゲーム完全終了→再起動→exploration active到達後の測定。

| index | baseline(B, 1回目) | X | B(A/B/A, 2回目) |
|---|---|---|---|
| 0-29, 31-33 | 不変 | 不変 | 不変 |
| **30** | **9** | **12** | **9** |

index=30以外の33件は3回の測定を通じて完全に不変。

**判定: CONFIRMED**

`index=30` は「パンチ」（WARP FIELD）actionに対応する。raw値: `9=B, 12=X`。

**構造仮説の検証完了**: 先頭(index 4-7)、中盤(12-20, 23)、末尾(30)に渡って計15件のアンカーがCONFIRMEDされ、全て仮説の予測位置と一致。「GetConfigGamePad index = 純正UI表示順」構造仮説は広範囲にわたる実測アンカーで強く裏付けられた。

## 対応表サマリ（2026-09-21時点、最終・CONFIRMEDのみ・15件）

| index | GAME Action(画面表示名) | context | raw→物理ボタン |
|---|---|---|---|
| 4 | 決定・アクション | COMMON | 10=A, 12=X |
| 5 | キャンセル | COMMON | 9=B, 16=RT |
| 6 | UI表示ON/OFF | COMMON | 17=L3, 26=START |
| 7 | コマンドメニュー | FIELD/DUNGEON | 11=Y, 12=X |
| 12 | 視点変更（左回転） | FIELD/DUNGEON | 13=LB, 14=LT |
| 13 | 視点変更（右回転） | FIELD/DUNGEON | 15=RB, 16=RT |
| 14 | 視点を正面に戻す | FIELD/DUNGEON | 9=B, 15=RB |
| 15 | 客観/主観切替 | FIELD/DUNGEON | 18=R3, 12=X |
| 16 | オートマップ表示 | FIELD/DUNGEON | 26=START, 13=LB |
| 17 | スキルヘルプON/OFF | BATTLE | 25=SELECT, 16=RT |
| 18 | オートバトル | BATTLE | 11=Y, 12=X |
| 19 | 次に回す | BATTLE | 15=RB, 26=START |
| 20 | テキストの早送り | EVENT | 11=Y, 12=X |
| 23 | メニュー | PUZZLE | 12=X, 10=A |
| 30 | パンチ | WARP FIELD | 9=B, 12=X |

**判明したraw値一覧**（indexごとの観察、他indexへの一般化はしない）:
```
A=10, B=9, Y=11, X=12, LB=13, LT=14, RB=15, RT=16, L3=17, R3=18, SELECT=25, START=26
```

**STRONGLY SUPPORTED（構造仮説のみ、未A/B/A）**: index 0-3, 8-11, 21-22, 24-29（18件）。index 31-33はUNRESOLVED（raw値常に0）。

**現在ステータス**: 15件CONFIRMED。構造仮説は先頭〜末尾まで広範囲に検証済み。
