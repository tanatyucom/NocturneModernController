# SMT3HD 右スティック視点左右・LB/RBダッシュ調査

## 2026-08-18 実機検証の最終結果

Xbox Elite Series 2 + Steam Input環境では、ゲーム内操作が正常でも、Unity Input、ゲーム内部`GetPadAnalog`、Steam Input action、複数世代のXInput、HID、Windows Gaming Inputのいずれからも右スティック実値を安定取得できなかった。Windows Gaming Inputは1台を列挙したものの、左右スティック、トリガー、全ボタンが常にゼロだった。Steam Inputを無効にするとゲーム内のコントローラー操作自体が使用不能になった。

`DDS3_PADCHECK_PRESS`へのネイティブフックは複数回の起動時クラッシュを起こしたため廃止した。最終的に、reWASDで右スティック左／右を未使用文字キー`[`／`]`へ変換し、SMT3HD標準の`KEYBOARD+MOUSE`設定でFIELD/DUNGEONとPUZZLEの回転操作へ割り当てる方法で実機動作を確認した。BATTLEへ同じキーを登録しないことで戦闘操作を変更せずに実現できた。F13/F14はゲーム側の設定画面で割り当て対象外だった。

したがって、右スティック回転についてはMOD方式を採用せず、reWASDとゲーム標準設定の組み合わせを推奨結果とする。ダッシュはゲーム内部のL2論理マップ（Xbox LT）または文字キー`P`を受け取り、`fldPlayerCalc()`実行中だけ通常移動のネイティブ基準速度`29`／`20`とワールドマップ専用定数`16`を1.6倍にする方式で実装した。LT＋RT同時押しでダッシュ固定を切り替えられる。Steam版1.0.4の病院内とワールドマップで実機確認済み。

調査日: 2026-08-18  
対象: Steam版 SMT3HD 1.0.4 / Unity IL2CPP x64  
調査範囲: 静的解析およびユーザー提供のゲームパッド設定画面確認（MOD実装、設定変更、セーブ変更なし）

## 0. 今回の対象操作

ユーザー提供画像により、対象はゲームパッド設定の `FIELD/DUNGEON` セクションにある次の2項目と確定した。

```text
視点変更（左回転） = LB
視点変更（右回転） = RB
```

これを次へ変更したい、という調査である。

```text
Right Stick Left  → 視点変更（左回転）
Right Stick Right → 視点変更（右回転）
```

同じ画面にある次の項目は別操作であり、今回の目的そのものではない。

```text
視点変更（左） = Right Stick Left
視点変更（右） = Right Stick Right
```

内部列挙でも前者は `FD_Turn_Left/Right`、後者は `FD_Camera_Left/Right` と明確に分かれている。ただしユーザーの実機観察では、後者の「視点変更（上/下/左/右）」は通常のダンジョンで機能していない可能性が高い。このため、コード上は潜在競合として扱うが、最初から抑制パッチを入れてはならない。

## 1. エグゼクティブサマリー

| 項目 | 結論 | 確度 |
|---|---|---|
| Right Stick X → 視点変更（左右回転） | **実現性 HIGH** | 右スティックXと左右回転は別々の既存論理アクションとして確認済み |
| 既存の視点ロジック再利用 | **PARTIAL** | 論理アクションとカメラ更新処理は確認。比例値を受け取る公開メソッドは未確認 |
| アナログ比例回転 | **PARTIAL** | 軸値の取得は可能だが、既存の左右回転アクションはデジタル判定 |
| VanillaでLB/RBを視点左右から解放 | **UNKNOWN** | 設定データは再割当可能な構造だが、UIから完全解除できることは未実機確認 |
| Dash | **実現性 MEDIUM** | 通常移動の中心処理と走行状態は特定。安全な速度スカラーは未特定 |

最小で安全な次の一手は、ダッシュやLB/RB抑制を含めず、通常のFIELD/DUNGEON探索中だけ右スティックXを既存の `FD_Turn_Left` / `FD_Turn_Right` 相当へ変換するログ付きPoCである。初回PoCはデジタル方式を推奨する。現在右スティックへ表示上割り当てられている `FD_Camera_Left/Right` は触らず、実際に競合が観測された場合だけ対処する。比例回転は、既存カメラ内部値への直接書込み位置を追加検証してから別段階にすべきである。

## 2. 調査資料と制約

使用した主要ファイル:

- `MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll`
- `MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out/Assembly-CSharp.dll`
- `GameAssembly.dll`
- `smt3hd_Data/il2cpp_data/Metadata/global-metadata.dat`

Cpp2IL 2022.1.0-pre-release.10でISILを生成し、生成ラッパーの型・シグネチャ・ネイティブRVAと照合した。`fldPlayer.fldPlayerCalc_Nml()` はCpp2ILの制御フロー復元が失敗したため、移動速度については名前だけから断定していない。

確度表記:

- **CONFIRMED**: 型情報に加え、ネイティブ命令または明確な呼出関係で確認
- **LIKELY**: 複数の構造的証拠が一致するが、実機または完全な逆コンパイルが未完
- **UNKNOWN**: 現在の静的証拠では確定不能

## 3. 既存の視点左右入力経路

### 3.1 論理アクション層 — CONFIRMED

`Il2Cpp.SIActionName` に次のフィールド専用アクションが存在する。

```text
FD_Camera_Up      = 8
FD_Camera_Down    = 9
FD_Camera_Left    = 10
FD_Camera_Right   = 11
FD_Turn_Left      = 12
FD_Turn_Right     = 13
FD_Return_Front   = 14
FD_Subjectivity   = 15
FD_Automap        = 16
```

よって、ゲームは「視点変更（上下左右）」と「視点変更（左右回転）」を別の論理操作として扱っている。設定画面との並びも一致するため、今回の対象は `FD_Turn_Left` / `FD_Turn_Right` で **CONFIRMED** と判断する。

論理入力の公開経路:

```csharp
bool SteamInputUtil.PRESS(SIActionName action);       // native RVA 0x25F9170
bool SteamInputUtil.TRIG(SIActionName action);        // native RVA 0x25F98B0
bool SteamInputUtil.REP(SIActionName action);         // native RVA 0x25F91A0
bool SteamInputAssign.IsCheck(int pad, SIActionName action, SIPressType type);
bool SteamInputAssign.padcheck(int pad, SIActionName action, SIPressType type);
```

`SIPressType.DOWN = 0`, `TRIG = 1`, `REP = 2` である。押し続け回転には `PRESS` / `DOWN` 系が対応する。

### 3.2 物理パッドマップ — CONFIRMED

`Il2Cpplibsdf_H.SDF_PADMAP`:

```text
L1 = 8
R1 = 10
L2 = 9
R2 = 11
L3 = 14
R3 = 15
```

`Il2Cpp.dds3PadManager` は次の論理マップ判定を提供する。

```csharp
bool DDS3_PADCHECK_PRESS(SDF_PADMAP map, int padNo); // native RVA 0x222BD70
bool DDS3_PADCHECK_TRIG(SDF_PADMAP map, int padNo);  // native RVA 0x222C0E0
bool DDS3_PADCHECK_REP(SDF_PADMAP map, int padNo);   // native RVA 0x222BF20
```

`fldCamera.calcCamNormal()`（native RVA `0x2020E80`）内で、L1（8）とR1（10）に対する `DDS3_PADCHECK_PRESS` 呼出しを確認した。同じ関数は方向入力、アナログ中立、カメラ補間値も処理する。

### 3.3 実際のカメラ更新 — LIKELY

フィールドカメラの中心型は `Il2Cpp.fldCamera` である。

```csharp
private static void calcCamNormal();  // RVA 0x2020E80, length 0x18C0
public static object fldCamMain();    // RVA 0x2027200, length 0x181C
```

関連する静的プロパティ:

```text
Vector2 mAxis
Vector2 mAcceleration
int     mDirection
int     mMoveLR
int     mMoveUD
float   CameraMoveLR
float   CameraMoveUD
float   CameraMoveDir
float   CameraBackDir
float   fldCamDirLockRotY
```

ネイティブコード上、`fldCamMain()` が入力を読み、`calcCamNormal()` がカメラ位置・方向と補間を更新する構造は確認できる。ただしCpp2ILのISILだけでは、L1/R1判定から最終的に書き換わる角度フィールドまでを一意にラベル付けできなかった。

したがって現在の完全な経路は次の確度である。

```text
LB/RB physical
  ↓ CONFIRMED
SDF_PADMAP_L1 / SDF_PADMAP_R1
  ↓ LIKELY（設定割当を介する場合は FD_Turn_Left / FD_Turn_Right）
fldCamera.fldCamMain()
  ↓ CONFIRMED
fldCamera.calcCamNormal()
  ↓ LIKELY
CameraMoveDir / fldCamDirLockRotY とカメラ姿勢更新
```

LB/RBが常にハードコード直結なのか、通常設定では `SIActionName` を経由してL1/R1へ解決されるのかは、設定配列の実値または実機ログで最終確認が必要である。

## 4. 右スティック入力経路

### 4.1 軸の取得 — CONFIRMED

`dds3PadManager.GetPadAnalog`:

```csharp
byte GetPadAnalog(int padno, int stick_lr, int xy, int cip_no = 0);
// native RVA 0x222C1C0
```

`fldCamera.fldCamMain()` 内の実呼出し:

```text
GetPadAnalog(0, 1, 0, 1)  // Right Stick X
GetPadAnalog(0, 1, 1, 1)  // Right Stick Y
```

返値は符号付き `-1..+1` ではなく、中心 `128` の `byte` である。ネイティブ処理は `128 ± GetAnalogAdjust()` と比較して左右・上下を判定する。`dds3ConfigGamePadSteam.GetAnalogAdjust()`（RVA `0x228EE40`）が設定デッドゾーンを返す。

右スティック方向を表す割当コードも存在する。

```text
InputAssign.AssignCode.Pad_RStickUp    = 30
Pad_RStickDown  = 31
Pad_RStickLeft  = 32
Pad_RStickRight = 33
```

Steam Input側にも `SteamPad.EAnalogActionsInGameControls.IG_RSTICK = 1` がある。

### 4.2 既存利用コードと実際の有効性 — PARTIAL

右スティックは設定画面上では `FD_Camera_Up/Down/Left/Right`（表示名「視点変更（上/下/左/右）」）へ割り当てられている。`fldCamMain()` がX/Yを取得し、`fldCamera.mMoveLR` / `mMoveUD` と方向状態を作るコードも存在する。ここまでは **CONFIRMED**。

一方、ユーザーの実機観察ではこれらの「視点変更（上/下/左/右）」は通常のダンジョンで機能していない。この観察と静的コードを合わせると、設定と入力取得は残っているが、通常カメラへの最終反映が無効・未完成・特定モード限定である可能性が高い（**LIKELY**）。

従って、新規にUnityの軸名を推測したり、物理LB/RBを偽装したりする必要はない。またPoC 1では `FD_Camera_Left/Right` を抑制せず、そのまま `FD_Turn_Left/Right` を追加する。実機で二重動作が観測された場合に限り、通常探索中だけ既存横アクションを抑制する。

## 5. 推奨する右スティック視点実装

### 推奨: Right Stick Xから既存の回転アクションを追加発火

```text
dds3PadManager.GetPadAnalog(0, 1, 0, 1)
  ↓
中心128と vanilla の GetAnalogAdjust() でデッドゾーン判定
  ↓
左: FD_Turn_Left / 右: FD_Turn_Right
  ↓
fldCamera の既存通常更新
```

最初のPoCでは比例量を直接角度へ掛けず、左右のデジタル状態へ変換するのが安全である。これによりLB/RB操作と同じ回転速度、補間、イベントカメラ制御を保持できる。既存の `FD_Camera_Left/Right` は変更せず、二重動作が実際に出た場合だけ抑制範囲を決める。

物理LB/RBの偽装は不要であり、推奨しない。論理アクション層または `fldCamera` の既存分岐を対象とする。

### 比例アナログ — PARTIAL

入力の大きさ自体は次式で正規化可能である。

```text
raw = byteValue - 128
deadzone = GetAnalogAdjust()
magnitude = clamp((abs(raw) - deadzone) / (127 - deadzone), 0, 1)
signed = sign(raw) * magnitude
```

しかし、`FD_Turn_Left/Right` は `PRESS/TRIG/REP` のデジタルアクションであり、float引数を取らない。比例回転には `CameraMoveDir`、`fldCamDirLockRotY`、または `calcCamNormal()` 内部の回転加算値へ安全に量を渡す追加解析が必要になる。現時点で公開された「回転速度(float)」メソッドは確認できていない。

また、ネイティブ処理中に `Time.deltaTime` 相当を使用していることをシンボル付きで確認できていないため、フレームレート依存性は **UNKNOWN** とする。

## 6. コンテキスト安全性

`fldCamMain()` 自体がフィールドカメラのモード、イベントカメラ、UI表示、特殊カメラを多数分岐している。最も安全な方法はグローバルな `Update()` で角度を直接変更することではなく、通常フィールド処理の既存分岐内に限定することである。

PoCの許可条件候補:

- `fldCamera.fldCamMain()` が通常カメラ経路を実行中
- `fldPlayer.fldPlayerCalc_Nml()` が選択される通常移動状態
- `dds3KernelMain.UIDispCheck(...)` が入力を阻害するUI状態ではない
- イベントカメラが存在しない（`fldCamNowEveCamera(...)` / `SetEventCamera(...)`）
- プレイヤー入力禁止カウンタが0（`fldGlobalWork_t.NoInpPlCnt`）

無効化すべき状態:

- バトル
- キャンプ/各種メニュー、名前入力
- イベント/カットシーン、スクリプトカメラ
- 主観視点、カメラ方向ロック
- ワープフィールド処理 (`fldPlayerCalc_Wm*`)
- はしご、穴、ダメージ、強制移動、`fldPlayerCalc_WarpRun()`
- パズル固有の `PZL_MapRot_*` 操作

正確な単一状態ビットは未特定である。PoCでは「通常メソッド内でのみ有効」という構造的ガードを第一選択にし、広いグローバル状態の推測は避ける。

## 7. LB/RB再割当

### 設定構造と現在値 — CONFIRMED

設定項目には次が存在する。

```text
CFG_TYPE_GAMEPAD.LEFT_ROTATION  = 15
CFG_TYPE_GAMEPAD.RIGHT_ROTATION = 16
```

`SteamInputAssign.ConfigSet` は各アクションに `pad1`, `pad2`, `key1`, `key2`, `mouse`, `type` を持つ。`dds3ConfigGamePadSteam` には `ChangeKey`、`ChangeKeyDuplicate`、`ChgConfigGamePad`、`GetConfigGamePad` がある。従ってデータ構造上、左右回転は再割当対象である。

提供画像では、現在値が次の通りであることも確認した。

```text
LEFT_ROTATION  (FD_Turn_Left)  = LB
RIGHT_ROTATION (FD_Turn_Right) = RB
CAMERA_MOVEMENT_LEFT  (FD_Camera_Left)  = Right Stick Left
CAMERA_MOVEMENT_RIGHT (FD_Camera_Right) = Right Stick Right
```

### Vanilla UIから完全に解放できるか — UNKNOWN

静的解析だけでは「未割当」をUIから選べるか、LB/RBを別アクションへ移した際に元の回転割当が自動消去されるかを確定できなかった。`ChangeKeyDuplicate()` は重複解消を行い、34個の設定項目を走査するため、別操作へLB/RBを割り当てると元設定を更新する可能性は高いが、実機設定画面で確認が必要である。

確認できた重複例外には、Cancel/Return/Viewpoint switching（設定番号8/17/28）とワープフィールド系（29..33）などがある。LB/RBに関する特別なハードコード例外はこの処理では確認できなかった。

方針:

1. Vanillaで解放できるならMODはLB/RBへ触れない。
2. 解放できない場合のみ、`FD_Turn_Left/Right` の解決直後で旧肩ボタン由来を抑制する。
3. `DDS3_PADCHECK_PRESS` 全体のL1/R1を潰すパッチは、UIやイベント操作まで壊すため避ける。

## 8. 既存移動アーキテクチャ

中心型は `Il2Cpp.fldPlayer` である。

```csharp
private static int  fldPlayerCalc_Nml();        // RVA 0x205EA20
public  static int  fldPlayerCalc();            // RVA 0x206CCD0
public  static void fldPlayerCalcForUnity();
private static void fldPlayerMotion(int, int, float); // RVA 0x206FAC0
private static void fldPlayerCalc_WarpRun();
private static void fldPlayerCalc_FastStart();
private static int  fldPlayerCalc_Nml_syukan(); // RVA 0x2064C60
```

主な状態:

```text
bool playerRun
int  fldPlayerAct
int  gKeyInputDir
int  gKeyInputCnt
int  gfldPlayerHasiCnt
int  gfldPlayerAnaCnt
float fldCamKakudoGoal
```

`fldPlayerCalc()` は状態に応じて `fldPlayerCalc_Nml()` または `fldPlayerCalc_Nml_syukan()` などを呼ぶディスパッチャである。`fldPlayerCalc_Nml()` が通常フィールド移動の中心であることは呼出関係から **CONFIRMED**。

一方、次は未確定である。

- 通常速度を保持する単一の公開 `speed` フィールド
- 左スティック量から移動距離への正確な式
- `fldPlayerMotion(..., float)` のfloat引数が移動速度かアニメーション速度か
- 位置更新がフレーム時間で正規化されるか

`fldWmFastMove`、`fldPlayerFastSet()`、`fldPlayerCalc_FastStart()` はワールドマップ/スクリプト高速移動用とみられ、通常ダッシュへの流用は危険である。

## 9. 推奨するダッシュ実装

### 推奨: 通常移動計算内の移動量スカラーを一時倍率化

```text
通常フィールド + 手動移動 + Dash held
  ↓
fldPlayerCalc_Nml() 内で算出済みの移動量だけを倍率化
  ↓
既存の当たり判定、床判定、イベント判定、姿勢更新へ渡す
```

Transform/座標の事後加算は、壁抜け、イベント領域の飛び越し、床/坂判定の不整合を招くため避ける。

現段階で `1.5x / 1.75x / 2.0x` のいずれかを安全と断定できる証拠はない。最初の速度PoCは1.25x程度から始め、フレーム単位の衝突・イベント判定をログ確認して段階的に上げるべきである。これは最終値の推奨ではない。

### ダッシュの安全ガード

最低限、次の場合は無効化する。

- `fldPlayerCalc_Nml()` 以外の移動状態
- `playerRun == false` かつ走行アニメーションに入れない状態
- `NoInpPlCnt != 0`
- はしご (`gfldPlayerHasiCnt`)、穴 (`gfldPlayerAnaCnt`)、ダメージ
- ワープ、強制/自動移動、イベント
- 主観視点、特殊パズル、ワープフィールド

アニメーション速度を別に補正すべきかは **UNKNOWN**。移動量のみを増やすと足滑りが起きる可能性がある。倍率を衝突計算前に入れれば既存判定を再利用できるが、高倍率では薄いトリガーを1フレームで越える可能性が残る。

## 10. LBとRBのダッシュ適性

通常のパッドマップではL1/R1はいずれも独立した入力であり、設定重複処理に片方だけの特別扱いは確認できなかった。従ってフィールド通常状態に限れば両者は同程度に利用可能と **LIKELY** 判断する。

ただし `calcCamNormal()` がL1/R1の物理マップを直接参照する箇所があるため、Vanilla再割当後もカメラ側で肩ボタンに反応が残らないかは実機確認が必要である。片方だけを先にダッシュへ割り当て、視点・UI・イベントで副作用を確認するのが安全である。

## 11. 最小の安全なPoC

### PoC 1（別承認後）

```text
対象: 通常のFIELD/DUNGEON探索のみ
入力: GetPadAnalog(0, 1, 0, 1)
デッドゾーン: vanilla GetAnalogAdjust()
出力: FD_Turn_Left / FD_Turn_Right 相当のデジタル状態
既存横入力: 変更しない（実機で二重動作が出た場合のみ後続PoCで対処）
LB/RB変更: なし
Dash: なし
永続設定変更: なし
ログ: 状態遷移時のみ
```

検証項目:

- 左右方向とゲーム内表示の一致
- LB/RBと同じ回転速度・補間・復帰挙動
- メニュー、バトル、イベント、主観、ワープで無反応
- 右スティックの既存用途との競合
- デッドゾーン内でドリフトしない

### PoC 2（PoC 1合格後、さらに別承認）

```text
対象: fldPlayerCalc_Nml() の通常手動移動のみ
入力: Vanillaで解放できた肩ボタンのhold
処理: 衝突判定前の移動量へ小倍率
座標直接操作: なし
```

## 12. 未解決事項と追加確認

1. `FD_Turn_Left/Right` とL1/R1のデフォルト設定配列実値。
2. `calcCamNormal()` 内の最終角度フィールドと1フレーム加算量。
3. Vanilla設定画面で左右回転を未割当または別入力へ移せるか。
4. `fldPlayerCalc_Nml()` の移動量算出式（Cpp2IL復元失敗箇所）。
5. 通常移動とアニメーション速度の結合点。
6. 実機でのイベントカメラ、パズル、ワープフィールド時の入力抑制。

これらは最終MOD実装前に解決すべきだが、PoC 1の「右スティックを既存デジタル回転へ接続する」実現性を否定するものではない。

## 13. 2026-08-22 最終実機結果

調査後半で、Steam録画を開始した直後だけ上下カメラの速度と可動範囲が大きくなる現象を確認した。カメラ座標、マウスドラッグ経路、ゲーム論理アクション、アナログ入力を同時記録して比較した結果、変化点は `dds3PadManager.GetPadAnalog` の右スティック縦軸だった。

```text
対象チャネル: GetPadAnalog(0, 1, 1, 1)
録画開始前:   128（外部パッドを上下しても中央固定）
録画開始後・上: 253～255
録画開始後・下: 0～42
```

録画前に使っていた `FD_Camera_Up/Down` のデジタル注入では、上限がおよそ `+26°`、下限がおよそ `-42°` だった。録画後のネイティブアナログ経路では、およそ `+58°` から `-62°` まで球面移動し、速度も上昇した。`CameraCamLeng` と `CameraFieldView` は変化せず、マウスドラッグ関数も発火していなかった。

### Root26 Evidence修正（2026-09-07）

CONFIRMEDではない事項:

- 「ゲーム起動後、セーブ／ロード画面でControllerをONにすればRight Stickが生きる」という手順は再現が安定せず、生きる場合と死ぬ場合があった。
- Steam Overlay自体が原因である、という因果関係は現時点ではCONFIRMEDではない。

ユーザー実機観測:

- 上記の不安定さを受けてreWASDを調査した結果、reWASD profile切替とRight Stickの生死に関係がある挙動を確認した。
- reWASD profile切替時、SMT3HDが「デバイスを切断しました」と表示するケースがあった。
- Guide長押しからSteamの動画録画操作を行った後はRight Stickが毎回生存し、その後はreWASD profileに関係なくRight Stickが有効だった。

HYPOTHESIS:

Steam Overlay／録画操作に伴うforeground、window message、または入力デバイス再初期化のいずれかが、通常128固定のnative Right Stick channelをanalog-activeへ遷移させる有力な契機である。ただし、どの要素が必要十分条件かはUNRESOLVEDである。

したがって、録画開始時のフォーカス更新または入力再初期化を契機に、通常は遮断されていたゲーム内蔵の右スティック縦アナログ経路が開通した可能性が高い。ただし録画機能やSteam Overlayそのものを原因として確定はしない。

正式実装ではSDL3対応ゲームパッドから右スティックを取得し、探索中だけ次の値をゲーム標準チャネルへ渡す。

```text
Right Stick Up   -> GetPadAnalog(0,1,1,1) = 255
Right Stick Down -> GetPadAnalog(0,1,1,1) = 0
Neutral          -> ゲーム本来の戻り値を維持
```

これによりSteam録画を開始せず、ゲーム標準の球面カメラ、補間、上下限を再現できた。Xbox Elite Series 2の有線・無線接続で実機確認済み。Helperの特定VID/PID制限は撤廃し、SDL3が標準ゲームパッドとして認識するXbox、PlayStation、Switchおよび一般USB/Bluetoothパッドを対象とした。

同時に次の回帰確認を通過した。

- ダンジョンの右スティック左右：標準旋回
- ダンジョンの右スティック上下：標準アナログ上下カメラ
- ダンジョンのLB／RB：旋回しない
- 戦闘のRB：Passが通常どおり動作
- ダッシュ：ダンジョンおよびワールドマップで動作

パズル操作は進行状況の都合により最終実機確認のみ未完了である。

## 14. Root-26 追加実機Evidence（2026-09-07、Windows再起動後）

Rolling bucket observer（`RightStickPollingProbe` / `FocusCycleProbe`、DLL SHA-256
`da68d07637ca797b2726ecd1c53546175ff3fbae96b927eafefc1e03b155e040`）による、
Windows再起動を挟んだ2回の独立した実機再現。生ログは
`docs/research/ROOT26_POST_REBOOT_EVIDENCE_20260907/melonloader/` に保存済み。

### 14.1 セッションA（13:46、Guide長押し→Alt+Tab相当タスク切替）

`CONFIRMED`（ログから直接）:

```
13:46:12.140  X/Y: INITIAL -> DEAD(128-fixed)
13:46:17.123  WM_NCACTIVATE / WM_ACTIVATE(WA_INACTIVE) / WM_ACTIVATEAPP(DEACTIVATING) / WM_KILLFOCUS
13:46:17.133  FOREGROUND-CHANGE -> process="explorer"
13:46:17.188  セッション終了 finalState=DEAD, transitions=0
（約5.5秒の空白）
13:46:22.772  探索再開、同一hWndへ復帰確認
13:46:23.269  X/Y: INITIAL -> DEAD(128-fixed)
13:46:24.020  X/Y: DEAD(128-fixed) -> LIVE(analog-active)（DEAD確認から約750ms後）
```

Broker marker-deadを再確認、Broker/InputHelper/SDL不関与（`temp_diagnostics`のmtime不変）。

`USER OBSERVATION`: Guide長押し→Alt+Tab相当のタスク切替UI→ゲーム復帰、動画録画は未実施。

### 14.2 セッションB（14:09、Guideボタン通常1回押下 + Steam Overlay表示）

`CONFIRMED`（ログから直接）:

```
14:09:15.412  探索セッション1開始、WNDPROC-OBSERVER install（hWnd=0xA0CCE, smt3hd）
14:09:15.860  X/Y: INITIAL -> DEAD(128-fixed)
14:09:18.125  MONITOR-END（FieldDashPatch.IsExplorationActiveが非アクティブ判定）
14:09:18.126  WNDPROC-OBSERVER restore、SESSION-SUMMARY finalState=DEAD, transitions=0
（約3.79秒の空白）
14:09:21.923  探索セッション2開始、同一hWndへ復帰確認
14:09:22.407  X/Y: INITIAL -> DEAD(128-fixed)
14:09:22.907  X/Y: DEAD(128-fixed) -> LIVE(analog-active)（DEAD確認から約500ms後、X/Yはほぼ同時）
14:09:26.553  X/Y: LIVE -> DEAD（探索終了時、スティック中立に伴う自然な遷移とみられる）
```

セッション1のWNDPROC-OBSERVER生存区間（15.413〜18.126）中、`WM_ACTIVATE`系メッセージも
`FOREGROUND-CHANGE`も1件も記録されなかった（セッションAとの明確な相違）。Broker marker-dead
を再確認、`NocturneModernController.Broker.log`/`.ready.json`はゲーム起動より前のmtimeで不変、
`InputHelper.log`/`Launcher.log`も無変化。ログ本文に`SDL`/`ExternalInput`/`InputHelper`の文字列なし
（Broker/InputHelper/SDL不関与）。

`USER OBSERVATION`（Guide押下・Overlay表示はログ計装なし、ユーザー申告として扱う）:
探索開始時DEAD確認 → Xbox Guideボタンを通常1回押下（長押しではない）→ Steam Overlayが開いた →
ゲームへ復帰 → Right Stick操作でLIVE確認。動画録画は未実施。

`HYPOTHESIS`:

- WM_ACTIVATE系メッセージが皆無だったことから、Steam Overlay表示は今回、OSレベルの
  ウィンドウ非activate化を伴わずに発生した可能性がある（Steam OverlayがDirectX/OpenGL hook
  経由でゲームプロセス内に直接描画され、フォアグラウンドウィンドウ自体は変わらない、という
  一般的な既知動作と整合的）。
- `FieldDashPatch.IsExplorationActive`が非アクティブに転じたのはOverlay表示の影響と考えられるが、
  それがOS focus変化を伴わない内部一時停止的処理によるものかはUNRESOLVED。

`UNRESOLVED`:

- DEAD確認からLIVEまでの時間が、セッションA/前回13:23（いずれも約750ms）に対し、
  今回は約500msだった。rolling observerのbucket=250ms・debounce=2bucketという
  実装上、500msはdebounce境界の理論上の最小検出時間（250ms×2）と一致するため、
  現象自体の時間差かdebounce位相のずれかは3サンプルでは判定できない。
- Overlay表示時点で実際にOS側フォアグラウンドウィンドウが変化したかどうかは、
  `RunForegroundMonitor()`の実装（`FieldDashPatch.IsExplorationActive`がfalseになった
  時点でフォアグラウンドチェック自体を打ち切る）により確認不能。

### 14.3 セッションC（18:38、`SteamControllerReacquisitionProbe`計装後の実機テスト）

DLL SHA-256 `815c4eec86b2b12bdae04bbf0d742dc0c0854390c35b7538cbc7fb8179716aeb`
（`Root26SteamState`計装込みビルド、`docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md`
15.8/15.9節の候補6/7/8〜12のうち、controller再取得・再初期化系の最小構成を実装したもの）。
生ログは`melonloader/Latest.log_20260907_SteamStateProbe_raw.log`。テスト条件は前回と同一
（探索開始→DEAD確認→Guide通常1回押下→Steam Overlay→復帰→Right Stick操作、録画なし）。

`CONFIRMED`（ログから直接、タイムスタンプは`Root26NativePoll`と同一の`DateTimeOffset.Now:O`基盤）:

```
18:38:16.306  探索セッション1: X/Y INITIAL -> DEAD(128-fixed)
18:38:22.618  セッション1終了 SESSION-SUMMARY finalState=DEAD, transitions=0
（約1.6秒の空白）
18:38:24.232  CALL SteamInputUtil.ResetController()          count=1
18:38:24.232  CALL SteamInputUtil.ResetController()          count=2
18:38:24.232  CALL SteamPad.SteamControllerReStart()         count=3
18:38:24.232  CALL SteamPad.SteamControllerReStart()         count=4
18:38:24.244  探索セッション2開始（SESSION-START、約12ms後）
18:38:24.746  X: INITIAL -> LIVE(analog-active)（DEADを一切経由せず、最初の判定から既にLIVE）
18:38:24.996  Y: INITIAL -> LIVE(analog-active)（同上）
18:38:29.854  X/Y: LIVE -> DEAD、セッション2終了（探索終了に伴う自然な遷移とみられる）
```

`PadConnectDiff` / `PadConnectMax` / `PadConnectDiffCount` / `LastInputIndex` /
`LastInputIndex_OLD` / `CurrentInputID`の6値は、ゲーム起動直後のSteam Input初期化
（18:38:10.498〜18:38:11.675の間に1回だけ変化、`SteamInputUtil.ResetController()`の
**初回**呼出しより前で、こちらは通常の起動処理と判断される）以降、18:38:24.232の
再呼出しを含め、DEAD→LIVE境界の前後で**一切変化していない**（値の変化としては
STATE-CHANGEが1件も出ていない＝不変）。

`STRONGLY SUPPORTED`（因果関係はまだCONFIRMEDにしない）:

- 探索セッション1終了後、次の探索セッション2が始まる直前（約12ms前）というピンポイントの
  タイミングで`SteamInputUtil.ResetController()`と`SteamPad.SteamControllerReStart()`が
  呼ばれ、その直後のセッション2ではRight StickがDEADを一切経由せず最初からLIVEだった、
  という強い時間的相関が得られた。これは今回の12候補のうちinstrumentationした5つの
  「値」ではなく、「2つのメソッド呼出しの発生」そのものが、これまでで最も直接的な
  revival候補シグナルであることを示す。
- `PadConnectDiff`等の接続差分カウンタが不変だったことから、「接続コントローラ数の
  変化」自体が引き金ではなく、`ResetController()`/`SteamControllerReStart()`という
  再初期化処理そのもの（またはそれを呼び出す上位の判断ロジック）が引き金である
  可能性が高まった。

`HYPOTHESIS` / `UNRESOLVED`（CONFIRMEDへ昇格させない）:

- `ResetController()`/`SteamControllerReStart()`が18:38:24.232に呼ばれた**直接の原因**
  （Guide押下そのものか、Steam Overlay表示か、あるいはOverlayを閉じてゲームへ戻る動作か）
  はUNRESOLVED。Guide/Overlayイベント自体は依然として未計装（意図的、focus/WM_INPUT系を
  主線へ戻さない方針を維持）。
- 起動時（18:38:10.498）にも同じ`SteamControllerReStart()`が2回呼ばれているが、これは
  探索セッションが始まる前の通常起動処理であり、DEAD→LIVE境界とは無関係と判断する
  （時系列上、探索セッション自体がまだ一度も始まっていない）。
- 各メソッドが常に2回連続（同一ミリ秒）で呼ばれる理由（LSTICK/RSTICK分、あるいは
  登録デバイス数分のループなど）はUNRESOLVED。
- `SteamPad.UpdateConnectedControllers()`は毎フレーム（約35ms間隔で2回ずつ）継続的に
  呼ばれ続けており、接続監視のための単発イベントではなく常時ポーリング処理である
  ことが判明した。呼出し回数はDEAD→LIVE境界の識別に有効なシグナルにならない
  （既存Evidenceの訂正）。
- サンプル数は今回のセッションCの1回のみ。次回以降のテストで同じ
  「セッション終了→ResetController/SteamControllerReStart呼出し→次セッションは
  最初からLIVE」というパターンが再現するかどうかが、このHYPOTHESISを
  `STRONGLY SUPPORTED`からさらに先へ進めるための鍵となる。

### 14.3.1 再現性確認（セッションD、18:45、call logging最小化後）

セッションCの結果を受け、`SteamPad.UpdateConnectedControllers()`のcall probeを削除した
最小構成（DLL SHA-256 `859e5fdfc38cd799fe93c2493e343927f637016a1a56aec181138ae64f3207c8`）
で同一条件（探索開始→DEAD確認→Guide通常1回押下→Steam Overlay→復帰→Right Stick操作、
録画なし）を再度実施。生ログは`melonloader/Latest.log_20260907_SteamStateProbe_rep2_raw.log`。
`UpdateConnectedControllers`関連ログは0件（意図通りの除外を確認）。ログファイルサイズは
セッションC比で約245KB→約55KBに縮小。

`CONFIRMED`（ログから直接）:

```
18:45:41.745  探索セッション1: X/Y INITIAL -> DEAD(128-fixed)
18:45:45.541  セッション1終了 SESSION-SUMMARY finalState=DEAD, transitions=0
（約0.93秒の空白）
18:45:46.470  CALL SteamInputUtil.ResetController()          count=1
18:45:46.470  CALL SteamInputUtil.ResetController()          count=2
18:45:46.471  CALL SteamPad.SteamControllerReStart()         count=3
18:45:46.471  CALL SteamPad.SteamControllerReStart()         count=4
18:45:46.498  探索セッション2開始（約28ms後）
18:45:46.998  X/Y: INITIAL -> DEAD(128-fixed)（セッション2冒頭はまだDEAD）
18:45:47.498  X/Y: DEAD(128-fixed) -> LIVE(analog-active)（DEAD確認から約500ms後）
18:45:52.200  X/Y: LIVE -> DEAD、セッション2終了（探索終了に伴う自然な遷移とみられる）
```

`PadConnectDiff`/`PadConnectMax`/`PadConnectDiffCount`/`LastInputIndex`/`LastInputIndex_OLD`/
`CurrentInputID`は、ゲーム起動直後の初期化（18:45:35.467〜18:45:36.551）以降、
18:45:46.470の`ResetController`/`SteamControllerReStart`呼出しを含め、
DEAD→LIVE境界の前後で一切変化なし（セッションCと同じパターンを再現）。

`STRONGLY SUPPORTED`（引き続きCONFIRMEDへは昇格させない）:

- セッションC（18:38）とセッションD（18:45）の独立した2回のテストで、いずれも
  「探索セッション終了→約1〜1.6秒後に`ResetController()`/`SteamControllerReStart()`が
  呼ばれる→その数十ms後に次の探索セッションが始まる→まもなくRight StickがLIVEになる」
  という同一パターンが再現した。今回はセッション2冒頭で一度DEADを経由してから500ms後に
  LIVEへ遷移しており（セッションCでは経由せず最初からLIVEだった）、遷移そのものの
  タイミングにはブレがあるが、**その直前に同じ2メソッドが呼ばれるという事実は2回とも一致**。
- 6つの状態値（PadConnectDiff等）は2回とも変化なしで一致しており、
  「接続差分カウンタの変化」ではなく「再初期化メソッドの呼出しそのもの」が
  再現性のある相関シグナルであることの裏付けが強まった。

`UNRESOLVED`:

- 呼出しの直接原因（Guide/Overlay/復帰動作のどれか）は今回も不明。
- DEAD経由の有無（セッションCは経由せず、セッションDは経由）の違いが何に起因するかは
  未確認（呼出しからセッション開始までの時間差: セッションC=12ms、セッションD=28ms、
  という程度の差はあるが、この差でDEAD経由の有無が決まるかはUNRESOLVED）。
- サンプル数は依然2回のみ。

### 14.4 累積される固定事実（既存事実に追加）

- Guide長押し＋Alt+Tab相当（13:23、13:46）、Guide通常1回押下＋Overlay（14:09）の
  いずれもDEAD→LIVEを再現。3回とも動画録画なし。→「録画が必須」という説はさらに弱まる
  （引き続きUNRESOLVEDの域を出ないが、録画なしでの再現数が増えた）。
- 3回ともBroker/InputHelper/SDL不関与をCONFIRMED。
- Guide/Overlay/Alt+Tabのいずれも、native側の直接計装（Guideボタン押下イベント、
  Overlay表示イベント）は存在せず、すべてユーザー申告とログタイムスタンプの相関に留まる。

## 15. 次の調査計画: `GetPadAnalog`上流のcontroller/input state（zero-base、2026-09-07）

目的を「Alt+Tab再現のN数を増やす」から、「復帰後にDEAD→LIVEへ切り替わる瞬間、
SMT3HD内部のcontroller/input stateで何が変化しているか」の特定へ変更する。
以下は、デプロイ済みDLLと同一ゲームバージョンの
`MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll`（IL2CPP interop向け生成アセンブリ）を
Mono.Cecilで直接メタデータ解析した結果である。**まだコード変更・build・deploy・
commit/push/stash/reset/revertは行っていない。** ネイティブ関数本体（IL2CPP AOTコード）は
未逆アセンブルであり、以下はすべてフィールド／メソッドの「シグネチャの存在」のみを
CONFIRMEDとし、値の因果関係はHYPOTHESIS/UNRESOLVEDとして扱う。

### 15.1 `dds3PadManager.GetPadAnalog`の直接の上流 — CONFIRMED（構造）/ HYPOTHESIS（因果）

`Il2Cpp.dds3PadManager`は次のstatic配列・メソッドを持つ（`CONFIRMED`、メタデータ実在確認済み）。

```csharp
byte[][][] dds3PadAnalog;         // 恐らく [cip_no][padno][channel]
byte[][][] dds3PrivatePadAnalog;  // 同形状、"Private"側（raw/未加工値の疑い）
byte[][]   dds3PadAnalogMap;      // [cip_no][channel] 相当
byte[][][] dds3PadMap;            // digital側、同形状
byte[][][] dds3PrivatePadMap;
uint[][][] dds3PrivatePadMapRepeat;
uint[][]   dds3PrivatePadAnalogRepeat;
byte[]     dds3PadActPow, dds3PadActPow_goal;  // 振動（rumble）関連、cip_no系配列と同じ次元感
uint[]     dds3PadActFrame;

void dds3PadInitialize();
void dds3PadUpdate();        // 恐らく毎フレーム呼び出され、Private→公開バッファへ反映
void dds3PadAct2Sub();
void dds3PadAct(uint mode, byte pow, uint frame);
void dds3PadActUpdate();
uint dds3PadGetPadMap();     // 恐らくpad接続状態を表すビットマスク
byte GetPadAnalog(int padno, int stick_lr, int xy, int cip_no);
```

`HYPOTHESIS`: `GetPadAnalog`は`dds3PadAnalog[cip_no][padno][channel]`を読むだけの
アクセサであり、実際の値は`dds3PadUpdate()`が毎フレーム`dds3PrivatePadAnalog`から
`dds3PadAnalog`へ反映することで更新される。DEAD状態とは、この反映が特定の
`cip_no`（Right Stickに対応するインデックス）についてのみ止まっている状態、という仮説。
native本体を読んでいないためCONFIRMEDにはしない。

さらに上流として、Steamworks統合クラス`Il2Cpp.SteamPad`が存在する（`CONFIRMED`、実在確認済み）。

```csharp
Dictionary<ulong, SteamPad.InputInfo> Controller;
bool     bInitialized;
InputHandle_t[] InputHandles;
InputHandle_t[] InputHandles_new;   // 列挙→差分比較用の二重バッファの疑い
ulong[]  InputHandles_id;

bool   Initialize();
bool   UpdateControl();                 // 毎フレームSteam Inputアクション値を取得？
void   UpdateConnectedControllers();    // 名称からして接続コントローラ再列挙処理
EControlType GetCurrentControlDevice();
EControlType GetControlDevice(int index);
ESteamInputType GetControllerType(int index);
void   SteamControllerReStart();        // 名称からしてSteam Input再起動処理
void   StopVibration(); void Vibration();

// nested:
struct InputInfo { InputHandle_t Handle; ESteamInputType ControllerType; int Current; List<ActionInfo> ActionSets; }
struct ActionInfo { int index; InputActionSetHandle_t Handle; List<AnalogAction> Analog; List<DigitalAction> Digital; }
struct AnalogAction { string Name; InputAnalogActionHandle_t Handle; }
enum EAnalogActionsInGameControls { IG_LSTICK = 0, IG_RSTICK = 1 }
enum EControlType { Unknown, Keyboard, Steam, XInput, XBoxOne, DInput, Switch, Ps4, Ps5 }
```

`HYPOTHESIS`: `SteamPad.UpdateControl()`がSteam Inputのアナログアクション
（`get_analogaction(handle, ref ActionInfo, "IG_RSTICK")`のような非公開メソッドが存在する、
`CONFIRMED`実在確認済み）を毎フレーム問い合わせ、その結果を`dds3PadManager`側の
どこか（恐らく`dds3PrivatePadAnalog`）へ書き込む、という経路が存在する。これが真であれば、
`GetPadAnalog`の全体経路は次のようになる。

```
SteamInput (OS/Steamworksレベル)
  ↓ HYPOTHESIS
SteamPad.UpdateControl() → get_analogaction(handle, "IG_RSTICK")
  ↓ HYPOTHESIS（未確認の橋渡し）
dds3PadManager.dds3PrivatePadAnalog[cip_no][...]
  ↓ HYPOTHESIS
dds3PadManager.dds3PadUpdate() が公開バッファへコピー
  ↓ CONFIRMED（構造のみ）
dds3PadManager.dds3PadAnalog[cip_no][padno][channel]
  ↓ CONFIRMED（既存Evidence）
GetPadAnalog(0, 1, X/Y, 1)
```

中間の「橋渡し」部分（SteamPadからdds3PadManagerへどう値が渡るか）はnative本体解析なしには
UNRESOLVEDである。

### 15.2 `cip_no`の意味 — HYPOTHESIS

既存Evidence（4.1節）で`GetPadAnalog(0,1,X/Y,1)`のように、Right Stickの呼出しは常に
`cip_no=1`固定である。設定クラス名が`dds3ConfigGamePadSteam`（Steam固有の設定層）である
ことと、`GetGamePadPadMap(padmap, id, cip_no, type)` / `GetGamePadSDFMAP(id, cip_no)`
など、デジタル入力（`SDF_PADMAP`）側でも`cip_no`が使われていることを合わせると、

`HYPOTHESIS`: `cip_no`は「どの入力経路（Controller Input Path）から読むか」を選ぶ
汎用インデックスであり、`cip_no=0`が既定/非Steam経路、`cip_no=1`がSteam Input経由の
経路を指す可能性がある。

`SteamPad.EAnalogActionsInGameControls.IG_RSTICK = 1`という値が`cip_no=1`と一致するのは
状況証拠だが、native側で本当にこの enum 値がそのまま`cip_no`として渡っているかは
未確認であり、**この対応関係はCONFIRMEDへ昇格させない**。

`cip`という略語自体の正式な展開（例: Controller Input Path/Point等）はゲーム側に
コメントやシンボル名が残っておらず、UNRESOLVEDのままとする。

### 15.3 controller device/slot/type/capability state — CONFIRMED（存在）/ UNRESOLVED（相関）

zero-base検索で実在を確認した候補（すべて`Il2Cpp.SteamPad`所属、`CONFIRMED`）:

- `Controller`（`Dictionary<ulong, InputInfo>`）: 接続中コントローラのハンドル→情報テーブル。
- `InputHandles` / `InputHandles_new` / `InputHandles_id`: Steamworks `InputHandle_t`配列。
  2本の配列（`InputHandles`と`InputHandles_new`）が存在することから、
  「新規列挙→旧リストと比較」という典型的な接続変化検出パターンをHYPOTHESISとして提示。
- `bInitialized`: Steam Inputサブシステム全体の初期化フラグ。
- `GetCurrentControlDevice()` / `GetControlDevice(int)`: `EControlType`
  （Unknown/Keyboard/Steam/XInput/XBoxOne/DInput/Switch/Ps4/Ps5）を返す。
- `GetControllerType(int)`: Steamworks `ESteamInputType`（デバイス種別capability相当）。
- `InputInfo.Current`（int）: そのコントローラで現在アクティブなActionSetのインデックスと
  思われる。`ActionSets[Current].Analog`にIG_LSTICK/IG_RSTICKのハンドルリストが入る構造。

これらの値がDEAD⇔LIVE遷移の前後でどう変化するかはすべてUNRESOLVED（今回は
メタデータ読み取りのみで、実行時の値は未取得）。

### 15.4 device disconnect/reconnect/reacquisition処理 — HYPOTHESIS

`SteamPad.UpdateConnectedControllers()`と`SteamPad.SteamControllerReStart()`が
名称から見て最有力候補（`CONFIRMED`: メソッドの存在。`HYPOTHESIS`: 実際にDEAD→LIVE境界で
呼ばれているか、呼ばれる頻度はどの程度か、は完全に未確認）。前者は「接続コントローラの
再列挙」、後者は「Steam Input自体の再起動」を意味する名前であり、reWASD profile切替時の
「デバイスを切断しました」表示や、Overlay/Alt+Tab復帰後の再構築処理の実体である可能性がある。

### 15.5 「デバイスを切断しました」に至る処理 — UNRESOLVED

今回のIL2CPPメタデータ検索（型名に`Device`/`Connect`/`Lost`/`MesWin`/`SysMes`/`Warning`/
`Caution`等を含むもの）では、このメッセージを表示する専用クラス・メソッドを特定できなかった。
候補として`dds3KernelMain`（既存Evidenceで`UIDispCheck`を確認済みのUI制御クラス）配下に
汎用メッセージウィンドウ機構が存在する可能性はあるが未確認。文字列自体はローカライズ
アセットに格納されている可能性が高く、型名からの逆算ではなく、ゲームのテキスト/文字列
テーブル資産を直接検索する方が特定の近道と考えられる（次の一手の候補、まだ未実施）。

### 15.6 Right stickだけを128固定し得るgate/state — HYPOTHESIS

Left Stick（`IG_LSTICK`）とRight Stick（`IG_RSTICK`）は別々の`AnalogAction`エントリとして
`ActionSets[Current].Analog`に格納される構造がある。ユーザー既存観測では、Right Stickが
DEADの間もLeft Stick相当の移動操作は生きていた（過去のEvidenceより）。この非対称性は、
「IG_RSTICKというアクション固有の解決（binding resolution）だけが失敗し、IG_LSTICKは
正常に解決され続ける」という仮説と矛盾しない。すなわち、gate候補は特定のnative条件分岐
というより、**Steam Inputのaction-set解決状態そのもの**（`InputInfo.Current`が指す
ActionSet内でIG_RSTICKのハンドルが有効な値を返すかどうか）である可能性がある。

### 15.7 Overlay復帰後に更新され得るcontroller state — HYPOTHESIS（候補列挙のみ）

- `SteamPad.Controller`辞書の再構築（キーの入れ替わり、エントリ数の変化）。
- `InputHandles` / `InputHandles_new`の内容・長さの変化。
- `GetCurrentControlDevice()`の戻り値変化（例: `Keyboard`→`Steam`、または`Unknown`→`XInput`等）。
- `dds3PadManager.dds3PadGetPadMap()`が返すビットマスクの変化。
- `InputInfo.Current`（アクティブActionSetインデックス）の変化。

### 15.8 最小限instrumentation計画（提案のみ、未実装）

既存の`RightStickPollingProbe`（`dds3PadManager.GetPadAnalog`へのHarmony postfix、
read-only）と同じ設計方針を踏襲し、次のHarmony postfixフックを追加候補とする。
いずれも戻り値の観測のみで、`SendInput`/`AttachThreadInput`等の副作用操作は行わない。
focus/Method D/WM_INPUT系のフックは主線に戻さず、あくまで補助情報として扱う。

```text
候補1: SteamPad.GetCurrentControlDevice() の戻り値
候補2: SteamPad.GetControllerType(0) の戻り値
候補3: SteamPad.Controller.Count と、各エントリのControllerType/Current
候補4: SteamPad.InputHandles.Length と InputHandles_new.Length
候補5: dds3PadManager.dds3PadGetPadMap() の戻り値（ビットマスク）
候補6: SteamPad.UpdateConnectedControllers() の呼出し回数（prefixでカウントのみ）
候補7: SteamPad.SteamControllerReStart() の呼出し回数（prefixでカウントのみ）
```

これらを既存の`Root26NativePoll`（GetPadAnalog監視）と同一のログストリーム・
タイムスタンプ基盤に統合し、値が変化した時だけログする方式（既存probeと同じ
"状態遷移時のみログ"方針）にすることで、次のような相関表を実機ログから
機械的に作れるようにする。

```text
timestamp | GetPadAnalog(RightStickX) state | GetCurrentControlDevice() | Controller.Count | UpdateConnectedControllers呼出し回数 | SteamControllerReStart呼出し回数
```

このテーブル上で、DEAD→LIVEの遷移時刻の直前・直後にどの候補値が変化しているかを見れば、
「Overlay/Guide/Alt+Tab復帰後に何が変化してRight Stickが生き返るか」を、focus/WM_INPUT系の
イベントに頼らずに直接特定できる。

実装に進む前の確認事項:

1. 上記メソッド（特に`SteamPad`側）へのHarmonyパッチが、既存の`DDS3_PADCHECK_PRESS`への
   ネイティブフックで過去に発生した起動時クラッシュ（2.節参照）と同種のリスクを持たないか、
   Harmony postfix方式（ネイティブdetourではない）であることの再確認。
2. `SteamPad`のメンバーはすべてインスタンスメソッド/プロパティである（`CONFIRMED`、
   `dds3PadManager`側のstaticメソッド一覧との対比で確認済み）。15.9節で判明した
   `SteamInputUtil.instance`シングルトン経由でインスタンスへ到達できる。

### 15.9 追記: `SteamInputUtil`シングルトンの発見

`Il2Cpp.SteamInputAssign`と`Il2Cpp.SteamInputUtil`をさらに調査した結果、`SteamPad`
インスタンスの実際の保持元が判明した（`CONFIRMED`、メタデータ実在確認済み）。

```csharp
class SteamInputUtil : /* MonoBehaviour相当、Awake()/Update()/OnDestroy()を持つ */
{
    static SteamInputUtil instance;   // シングルトン
    static SteamInputUtil Instance;   // 別名アクセサ（重複生成の疑い、Il2CppInterop側の命名ゆれ）

    SteamPad steam_pad;               // ← 15.1のSteamPadインスタンス本体
    SteamMouse steam_mouse;
    SteamInputAssign input_assign;

    int  LastInputIndex;
    int  LastInputIndex_OLD;          // ← "OLD"付き、前回値との比較用と思われる
    ulong CurrentInputID;
    int  PadConnectDiff;              // ← 名称からして接続台数の差分
    int  PadConnectMax;
    int  PadConnectDiffCount;
    int  bRequestButtonUpdate;

    SteamPad GetSteamPad();
    void UpdateInput();                // 毎フレーム呼出しの疑い
    void UpdateData();                 // 同上
    void ResetController();            // ← 名称からしてcontroller再初期化そのもの
    void RequestButtonUpdate(int type, bool sw);
    int  GetControllersNum();
    EControlType GetControllerType(bool gamepad);
    void Awake(); void Update(); void OnDestroy();
}
```

`HYPOTHESIS`: `SteamInputUtil.instance`（Unityコンポーネントのシングルトン）が毎フレーム
`Update()`→`UpdateInput()`/`UpdateData()`を通じて`steam_pad`（`SteamPad`インスタンス）を
更新し、その結果が最終的に`dds3PadManager.dds3PadAnalog`へ反映される、という一続きの経路が
存在する。`PadConnectDiff`/`PadConnectMax`/`PadConnectDiffCount`という名前は、
「接続コントローラ数の差分検出」を強く示唆しており、`ResetController()`は
「device disconnect/reconnect/reacquisition処理」の直接的な候補として
`SteamPad.SteamControllerReStart()`と並ぶ最有力候補である。ただしいずれも呼出し頻度・
呼出しタイミング・DEAD→LIVE境界との相関はすべて未検証でHYPOTHESISに留まる。

これにより、15.8節のinstrumentation候補は次のように具体化できる（`SteamInputUtil.instance`
経由でアクセス、いずれもHarmony postfix、read-only）。

```text
候補8: SteamInputUtil.instance.PadConnectDiff / PadConnectMax / PadConnectDiffCount
候補9: SteamInputUtil.instance.LastInputIndex / LastInputIndex_OLD / CurrentInputID
候補10: SteamInputUtil.instance.GetControllersNum() の戻り値
候補11: SteamInputUtil.instance.ResetController() の呼出し回数（prefixでカウントのみ）
候補12: SteamInputUtil.instance.bRequestButtonUpdate の値
```

候補8〜12は候補6/7（`SteamPad`側のUpdateConnectedControllers/SteamControllerReStart）と
対になる「もう一段上のシングルトン層」であり、両方を同時に仕込むことで、
「`SteamInputUtil`が接続差分を検出→`SteamPad`を再構築」という因果順序そのものを
ログから直接確認できる可能性がある。

この計画についてどう進めるか（全候補を一度に実装するか、候補6/7/8〜12のような
「device再列挙・再起動」系だけを先に検証するか等）は、次の指示を待つ。

## 16. Native caller解析: `ResetController()` / `SteamControllerReStart()`（2026-09-07）

セッションC/Dで得られた相関を受け、`SteamInputUtil.ResetController()`と
`SteamPad.SteamControllerReStart()`の呼び出し元・内部処理をnativeレベルで解析した。
使用ツールは、MelonLoaderに同梱済みの`Cpp2IL.exe`（version 2022.1.0-pre-release.10、
`MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/Cpp2IL.exe`）を
`--use-processor callanalyzer --output-as isil`で自分で再実行し、デプロイ済みゲーム本体
（`smt3hd.exe` / `GameAssembly.dll` / `global-metadata.dat`）に対してISIL（Instruction
Semantic Intermediate Language）ダンプを生成したもの。読み取り専用の静的解析であり、
ゲーム本体・save data・controller stateへの書き込みは一切行っていない。出力は
リポジトリ外（ユーザープロファイル配下）に保存し、git管理下には置いていない。
**コード変更・build・deploy・commit/push/stash/reset/revertは行っていない。**

### 16.1 `ResetController()`の全caller — CONFIRMED（否定的結果）/ HYPOTHESIS（真の呼び出し元）

`SteamInputUtil.ResetController()`のネイティブエントリRVAは`0x25F9340`
（`Cpp2ILInjected.AddressAttribute`より、Length=0x96）。このRVAへの`Call`命令を、
Cpp2ILが解析した**全42アセンブリ**（mscorlib、System、Unity各モジュール、
`Assembly-CSharp`、`Assembly-CSharp-firstpass`）のISIL全体から検索した。

`CONFIRMED`: 上記のいずれの管理コード（IL2CPPコンパイル済みC#由来のネイティブコード）
からも、`ResetController()`への直接呼び出しは1件も見つからなかった（`SteamInputUtil`
自身の`Awake()`/`Update()`/`UpdateInput()`/`UpdateData()`を含む）。

`HYPOTHESIS`: `ResetController()`はSteamworks SDK側（`steam_api64.dll`相当、
Cpp2ILの解析対象外のネイティブコード）からのコールバック経由で呼ばれている可能性が高い。
`SteamInputUtil`には既に`m_GamepadTextInputDismissed`という
`Callback<GamepadTextInputDismissed_t>`（Steamworksのコールバック機構）が存在することが
確認済みであり、同種の仕組み（例えばコントローラー接続変化やOverlay状態変化に対応する
Steamworksコールバック）が`ResetController()`を駆動している可能性がある。ただし、
どのSteamworksコールバックが対応するかはUNRESOLVED（Steamworks SDK側のシンボルは
Cpp2ILの解析範囲外であり、これ以上は追跡できていない）。

`UNRESOLVED`: focus/WM_INPUT系のWin32メッセージ経由で呼ばれている可能性も論理的には
排除できないが、そのような呼び出し経路が存在するならUnityの
`OnApplicationFocus`/`OnApplicationPause`等のC#側ハンドラを経由するはずで、それも
`Assembly-CSharp`内のISIL全体検索で見つかっていない。したがって「focus/WM_INPUT系の
C#ハンドラ経由」という経路は今回の検索範囲内では否定的（見つからなかった）が、
方針として主線には戻さない。

### 16.2 `SteamControllerReStart()`の全caller — CONFIRMED

`SteamPad.SteamControllerReStart()`のネイティブエントリRVAは`0x2602EB0`
（Length=0x73）。全42アセンブリのISIL全体を検索した結果、呼び出し元は
**ちょうど2箇所のみ**、いずれも`Assembly-CSharp`内:

1. `SteamInputUtil.ResetController()`（ISIL行028、`rcx`にSteamPadインスタンスと
   思われるオブジェクト、`rdx=0`を渡して呼び出し）。
2. `SteamPad.UpdateConnectedControllers()`自身の内部（ISIL行396、同メソッド末尾付近、
   400行超あるISIL本体のうち条件分岐を経た後にのみ到達する分岐）。

`CONFIRMED`: `SteamControllerReStart()`への到達経路はこの2つのみで、他に存在しない。

`HYPOTHESIS`: 経路2（`UpdateConnectedControllers()`自身が自分の判断で
`SteamControllerReStart()`を呼ぶ）は、セッションC/Dで観測された「毎フレーム呼ばれる
`UpdateConnectedControllers()`が特定の条件でのみ`SteamControllerReStart()`を誘発する」
という可能性を示す。ただし、その条件分岐（400行超のISIL、`NotImplemented`命令を含み
Cpp2ILが完全に意味解析できていない箇所を含む）を、フィールド名の対応が取れない状態で
正確に特定することはできなかった。`PadConnectDiff`/`PadConnectMax`との対応も
UNRESOLVED（ISILはフィールドをオフセット番号でしか示さず、シンボル名との対応には
別途デコンパイラかフィールドオフセット手計算が必要）。

`UNRESOLVED`: セッションC/Dで実際に観測された呼び出しが経路1（ResetController経由）
なのか経路2（UpdateConnectedControllers自己誘発）なのかは、ログ上の呼び出し順序
（`ResetController` count=1,2 → `SteamControllerReStart` count=3,4、同一ミリ秒）と
経路1の構造（ResetControllerがSteamControllerReStartを直接呼ぶ）が整合するため
経路1の可能性が高いと考えられるが、経路2も`ResetController()`実行中に
`UpdateConnectedControllers()`が別途フレーム内で呼ばれて条件を満たした可能性を
完全には排除できない。

### 16.3 `ResetController()`内部で何が行われているか — CONFIRMED（構造）/ HYPOTHESIS（意味）

ISILトレース（150バイトの本体）を確認した。`CONFIRMED`（命令列として）:

1. 一度きりの初期化ラッチ（静的bool的フラグを1に設定、既に1なら該当ブロックをスキップ）
   から始まる、典型的な「初回だけ追加初期化」パターン。
2. 静的フィールド経由でオブジェクト参照を取得し、そのオブジェクトに対してメソッドを
   1つ呼び出す（戻り値を後続で使用）。
3. 別の静的オブジェクトに対する軽いnullチェック/状態チェックガード（`test byte ptr
   [rcx+12Fh],2`というパターンが、この関数に限らず`SteamControllerReStart`や
   `RequestButtonUpdate`など複数の無関係なメソッドでも共通して出現しており、
   Unity/IL2CPPランタイム側の汎用ヘルパー的チェック（メインスレッド確認等）である
   可能性が高い。個別の業務ロジックではないとみられる）。
4. 多数の引数（スタック経由で18スロット分）を伴う大きな呼び出し（RVA `0x10D6760`、
   ベースアドレスの傾向からSteamworks SDK側の可能性が高いが未特定）。
5. `SteamInputUtil`インスタンスの`+0x50`オフセットにあるフィールド（`SteamPad`
   インスタンスへの参照と推測されるが未CONFIRMED）がnullでなければ、
   **`SteamPad.SteamControllerReStart()`を呼び出し**、続けて
   **`SteamPad.UpdateConnectedControllers()`を呼び出す**（いずれもCONFIRMED、
   RVA一致による直接確認）。

`HYPOTHESIS`: `ResetController()`は名前が示す通り、Steam Input側の状態を
「立て直す」処理であり、その具体的な中身は「(おそらくSteam Input自体を指す)
何らかのオブジェクトへの1回の呼び出し」→「SteamPadインスタンスに対する
SteamControllerReStart→UpdateConnectedControllersの順次呼び出し」という
2段構成である。RVA `0x28A1430`/`0x28A1370`（`SteamControllerReStart()`自身の
末尾で`rcx=0`を渡して呼ばれる2つの関数、管理コード側に対応するラッパーが存在しない
＝native Steamworks SDK関数である可能性が高い）が、それぞれ
`SteamAPI_ISteamInput_Shutdown`相当・`SteamAPI_ISteamInput_Init`相当である
可能性がある（呼び出し順序・「rcx=0を渡す」という単純な引数パターンから）。
**これはシンボル名が取得できていないため推測であり、CONFIRMEDへ昇格させない。**

### 16.4 `bRequestButtonUpdate` / `RequestButtonUpdate(type, sw)` — CONFIRMED

`RequestButtonUpdate(int type = -1, bool sw = false)`のISILを確認した。

`CONFIRMED`:
- `type == -1`（既定値）の場合、`this`インスタンスの`+0x68`オフセットのフィールドへ
  `7`（`0b111`）を直接代入する。
- `type != -1`の場合、既存の`+0x68`の値に対し`bts`（bit-test-and-set、
  `(1 << (type & 31))`相当）でビットを立てる。

`HYPOTHESIS`: `+0x68`は`bRequestButtonUpdate`（メタデータ上`System.Int32`の
public instanceプロパティとして確認済み）に対応するフィールドであり、
`bRequestButtonUpdate`は「ボタンアイコン更新が必要な種別」を表すビットマスクである
可能性が高い。`type=-1`は「全種別を再要求」を意味すると解釈できる。ただし
`ResetController()`/`SteamControllerReStart()`本体のISILには
`RequestButtonUpdate()`やオフセット`+0x68`への言及は見つからず、この2つの経路が
`bRequestButtonUpdate`を直接操作している証拠はない（UNRESOLVED、関連なしの可能性が高い）。

### 16.5 `LastInputIndex` / `LastInputIndex_OLD` / `CurrentInputID` / controller type判定 — UNRESOLVED

これらのフィールドが`SteamInputUtil`インスタンスのどのオフセットに対応するかを
ISILから直接対応付けることができなかった（ISILはフィールドを`[rcx+オフセット]`の
数値でしか表現せず、フィールド名との対応にはIL2CPPのフィールドレイアウト計算
（フィールド宣言順・型サイズからのオフセット手計算、または専用デコンパイラ）が
別途必要）。`UpdateConnectedControllers()`の本体（400行超のISIL、複数の
`NotImplemented`命令を含む複雑な制御フロー）のどこでこれらのフィールドが
更新されるかも、同じ理由でUNRESOLVEDのままである。

`GetControllerType(int index)`（`SteamPad`側）のISILは確認した。`CONFIRMED`:
インデックスの範囲チェック（`[rax+24]`と比較、範囲外なら例外/デフォルト値扱いの
分岐）を行い、範囲内なら配列的な構造（`[rax+32+index*8]`）から要素を取得して
別のヘルパー関数（RVA `0x28A12B0`、未特定）へ渡す、という一般的な「配列から
インデックスで取得して変換する」パターンであることが分かった。これ以上の
意味解析（`ESteamInputType`のどの値がどう決まるか）はUNRESOLVED。

### 16.6 まとめ

`STRONGLY SUPPORTED`（14.3/14.4節から継続、native解析により経路の一部がCONFIRMEDに格上げ）:

- `SteamControllerReStart()`が`ResetController()`から直接呼ばれる、という構造は
  **CONFIRMED**（RVA一致による静的確認）。セッションC/Dのログで両者が同一ミリ秒に
  連続して呼ばれていた観測と、この静的構造は矛盾しない。
- 依然として「`ResetController()`が呼ばれること」自体がRight Stick復活の
  **直接の原因である、とはCONFIRMEDにしない**。呼び出しの真のトリガー（16.1節、
  Steamworksコールバック側）と、`ResetController()`内部の2つの大きな未特定呼び出し
  （16.3節、RVA `0x10D6760`と`0x28A1430`/`0x28A1370`）の意味が未解決である限り、
  この評価はSTRONGLY SUPPORTEDのまま据え置く。

`UNRESOLVED`（次に解決すべき項目）:

1. `ResetController()`の真の呼び出し元（Steamworksコールバックの種類）。
2. RVA `0x10D6760`（Steam Input全体を再評価していると思われる大きな呼び出し）の正体。
3. RVA `0x28A1430`/`0x28A1370`（Steam Input Shutdown/Init相当と推測される2呼び出し）の正体。
4. `LastInputIndex`/`LastInputIndex_OLD`/`CurrentInputID`/`PadConnectDiff`系フィールドの
   実際のオフセットと、`UpdateConnectedControllers()`内でそれらが更新される具体的な条件分岐。
5. `UpdateConnectedControllers()`が自己判断で`SteamControllerReStart()`を呼ぶ
   （16.2節、経路2）条件そのもの。

これらはいずれも、より高機能なIL2CPPデコンパイラ（フィールドオフセット解決・
Steamworks SDKシンボル対応が可能なツール）か、Steamworksコールバック一覧との
突き合わせが必要であり、今回のCpp2IL ISIL解析だけでは追い切れなかった。

## 17. 因果確認PoC: `SteamInputUtil.instance.ResetController()`単独呼び出し（2026-09-07）

診断専用PoC（`src/ManualResetControllerPoc.cs`）を実装し、F9キー1回押下で
`SteamInputUtil.instance.ResetController()`のみを明示的に1回呼び出し、
`SteamControllerReStart()`/`UpdateConnectedControllers()`はMOD側から呼ばず、
ゲーム自身の内部呼び出しとして`Root26SteamState`のCall probeで観測する構成とした。
Guide押下・Steam Overlay・Broker/InputHelper/SDL・focus/WM_INPUT系は一切使用していない。

### 17.1 初回テスト（19:11） — 実装バグにより無効

`_lastTriggerTick`の初期値`int.MinValue`と`Environment.TickCount`の`unchecked`減算が
符号オーバーフローし、F9押下が常に「クールダウン中」と誤判定されて`ResetController()`が
一度も呼ばれなかった。ログに`MANUAL-RESET-REQUEST`は皆無。`_hasTriggeredBefore`フラグを
追加して修正（DLL SHA-256 `e659f0d4e5a52f26a8b96bdaae41713f094348871196f6c64ea76206058e3562`）。

### 17.2 修正後テスト（19:18） — CONFIRMED

生ログ: `melonloader/Latest.log_20260907_ManualResetPoc_fixed_raw.log`。

```
19:17:53.665  探索セッション: X/Y INITIAL -> DEAD(128-fixed)
19:18:01.575  MANUAL-RESET-REQUEST (F9)
19:18:01.576  CALL SteamInputUtil.ResetController()    count=1, 2
19:18:01.576  CALL SteamPad.SteamControllerReStart()   count=3, 4
19:18:01.577  MANUAL-RESET-REQUEST invoked ResetController()（正常返却）
（この後、探索セッションはさらに約9.9秒継続）
19:18:11.495  セッション終了 SESSION-SUMMARY finalState=DEAD(128-fixed), transitions=0
```

`CONFIRMED`:
- `ResetController()`は正しく1回呼ばれ、16.2節の静的解析どおり内部から
  `SteamControllerReStart()`を直接呼び出した（同一ミリ秒、count=1,2→3,4という
  セッションC/Dと全く同じ呼び出しパターンを、Guide/Overlayなしで単独発火により再現）。
- しかし、その後**約9.9秒間、Right StickはDEADのまま**であり、
  `transitions=0`（LIVEへの遷移なし）。

`REJECTED`（新規）:
- 「`ResetController()`単独呼び出しが、Right Stick復活の十分条件である」は
  この対照実験により**REJECTED**とする。呼び出し自体はセッションC/Dと構造的に
  同一（`ResetController`→`SteamControllerReStart`の直接連鎖、CONFIRMED済み）が
  発生しているにもかかわらず、復活が起きなかった。

`HYPOTHESIS`（据え置き、REJECTEDにより再整理）:
- セッションC/Dで観測された相関は、`ResetController()`/`SteamControllerReStart()`の
  呼び出しそのものではなく、それらが**Guide押下やSteam Overlay表示に伴う何らかの
  付随的な処理**（16.1節で未解決のSteamworksコールバック起因の呼び出しと、
  それ以外の要素の組み合わせ）の中で発生した場合にのみ意味を持つ可能性がある。
- あるいは、`ResetController()`が復活に寄与するとしても、探索が非アクティブな
  タイミング（フォーカス喪失中、フィールド更新tickが止まっている間）に呼ばれる
  必要があり、今回のように探索アクティブ中（DEADのまま探索は続いている状態）に
  呼んでも効果がない、という可能性がある。今回のPoCは意図的に「探索中にF9を押す」
  形だったため、この条件の違いは検証できていない。

`UNRESOLVED`:
- 「探索を一時中断させてからResetController()を呼ぶ」（例:
  `FieldDashPatch.IsExplorationActive`がfalseになるタイミングを待って発火する）
  という条件変更で再現するかどうかは未検証。
- Guide押下/Steam Overlay自体が引き起こす、`ResetController()`呼び出し**以外**の
  副作用（16.1節UNRESOLVED、Steamworksコールバックの正体）が、真の必要条件である
  可能性が高まった。

### 17.3 未解決の別件: システム終了時の確認ダイアログ

ユーザーより、テスト後の「システム終了」時に本来出るはずの「はい/いいえ」確認
ダイアログが出なかった、との報告があった。今回のMelonLoaderログ末尾は
`PreyEyes2: Hooks detached. Knowledge saved. Goodbye.` / `Preferences Saved!`で
正常終了しており、ゲームプロセス自体のクラッシュ・ハングを示すログは確認できない。
「システム終了」が何を指すか（ゲーム終了/Windows シャットダウン/Steam終了等）を
含め、UNRESOLVEDとして次のユーザー確認を待つ。

## 18. Deep state比較PoC: 成功セッション（Guide/Overlay）の観測結果（2026-09-07）

`Root26SteamPadDeepStateProbe`（`SteamPad.Controller`/`InputHandles`系/
`GetCurrentControlDevice()`/`GetControlDevice(0)`/`GetControllerType(0)`/
IG_RSTICKハンドルの変化時ログ、DLL SHA-256
`86c30a49fcc87ea40c280408fb950862e98ce4fb9978041abb59dc85528eaac5`）による
Guide/Overlay成功セッションの観測結果。生ログは
`melonloader/Latest.log_20260907_DeepState_GuideSuccess_raw.log`。

### 18.1 タイムライン — CONFIRMED

```
19:40:19.09x  起動直後: Controller.Count=0, InputHandles=全0,
              GetCurrentControlDevice()=Keyboard, GetControlDevice(0)=DInput,
              GetControllerType(0)=k_ESteamInputType_Unknown
19:40:20.827  CALL SteamPad.SteamControllerReStart() count=1,2（起動時の初期化、探索開始前）
19:40:20.834  Controller.Count 0 -> 2（2つのcontroller handleが出現）
19:40:20.838  Controller[19680159496504676] INITIAL -> (Current=0 ControllerType=Unknown IG_RSTICK_Handle=2)
19:40:20.838  Controller[91728467815138660] INITIAL -> (Current=0 ControllerType=Unknown IG_RSTICK_Handle=2)
19:40:20.839  InputHandles/InputHandles_new/InputHandles_id が上記2ハンドルで確定
19:40:20.840  GetControlDevice(0) DInput -> XBoxOne
19:40:20.840  GetControllerType(0) Unknown -> k_ESteamInputType_XBoxOneController
19:40:22.017  GetCurrentControlDevice() Keyboard -> XInput
--- （以降、これら9項目はセッション終了までCONFIRMEDで一切変化なし） ---
19:40:26.586  探索セッション1開始
19:40:27.085  X/Y: INITIAL -> DEAD(128-fixed)
19:40:30.990  セッション1終了 finalState=DEAD, transitions=0
（約160msの空白 ― この間にGuide押下・Overlay表示・復帰と推定される）
19:40:31.150  CALL SteamInputUtil.ResetController()     count=1,2
19:40:31.151  CALL SteamPad.SteamControllerReStart()    count=3,4
              （★この呼出しの前後でRoot26SteamPadStateのSTATE-CHANGEは1件もなし★）
19:40:31.177  探索セッション2開始
19:40:31.677  X/Y: INITIAL -> DEAD(128-fixed)
19:40:32.427  X/Y: DEAD(128-fixed) -> LIVE(analog-active)（DEAD確認から750ms後）
19:40:33.177  X/Y: LIVE(analog-active) -> DEAD(128-fixed)（LIVEから750ms後、再度DEADへ）
19:40:33.254  セッション2終了 finalState=DEAD, transitions=2
（約1.46秒の空白、この間もSTATE-CHANGEなし、追加のResetController/SteamControllerReStart呼出しもなし）
19:40:34.716  探索セッション3開始
19:40:35.454  X/Y: INITIAL -> LIVE(analog-active)（最初からLIVE）
19:40:37.490  セッション3終了 finalState=LIVE(analog-active), transitions=0（LIVEのまま終了）
```

### 18.2 CONFIRMED

- 起動直後の1回限りの初期化（20.827〜22.017）を最後に、以下9項目は
  **探索セッション1のDEAD、Guide+Overlayを挟んだResetController/SteamControllerReStart
  呼出し、セッション2のDEAD→LIVE→DEAD、セッション3の安定LIVE化に至るまで、
  一切変化しなかった**:
  - `Controller.Count`（2のまま）
  - `Controller[各handle].Current`（0のまま）
  - `Controller[各handle].ControllerType`（`k_ESteamInputType_Unknown`のまま。
    実際にはXbox系パッドで復活しているにもかかわらず、この値自体は
    `GetControllerType(0)`とは別の内部値のままだった点に注意）
  - `Controller[各handle].IG_RSTICK_Handle`（`2`のまま）
  - `InputHandles` / `InputHandles_new` / `InputHandles_id`（3配列とも完全に不変）
  - `GetCurrentControlDevice()`（`XInput`のまま）
  - `GetControlDevice(0)`（`XBoxOne`のまま）
  - `GetControllerType(0)`（`k_ESteamInputType_XBoxOneController`のまま）
- したがって、今回計装した9項目のうち**8項目はDEAD⇔LIVE境界と一切相関しない**
  ことがCONFIRMEDされた（起動時の初期化イベントとは相関するが、それとDEAD→LIVEは
  別事象）。

### 18.3 HYPOTHESIS / UNRESOLVED

- 真の因果メカニズムは、今回計装した9項目のいずれにも現れない、より深い場所
  （16章で未解決のSteamworksコールバック内部状態、または`ActionInfo`/
  `AnalogAction`の「解決済みかどうか」を示す別のフィールド・関数）にある可能性が高い。
  `IG_RSTICK_Handle`という値自体は「アクションのハンドル（ID）」であり、
  「そのアクションが現在有効な入力を受け取っているか」を示す値ではないため、
  ハンドルが不変でも実際の入力解決状態が変化した可能性は排除できない
  （UNRESOLVED、今回は未計装）。
- セッション2で一度LIVEになった直後（750ms後）に自然にDEADへ戻り、追加の
  ResetController/SteamControllerReStart呼出しなしにセッション3で安定LIVE化した
  理由はUNRESOLVED。ユーザーがこの間に2回目のGuide操作を行ったかどうかの確認が必要。
- F9手動リセットの失敗セッションは、本probe追加前の実行分のみ取得済みであり、
  同一計装での直接比較はまだ行っていない。同一ビルドでのF9失敗セッション再取得が
  次のステップとして必要。

## 19. 安全性の懸念: `ResetController()`呼び出しの副作用（2026-09-07）

ユーザー報告により、F9（`ManualResetControllerPoc`経由の`SteamInputUtil.instance.ResetController()`
呼び出し）を押した回のプレイで、後に「システム終了」を選択した際の「はい/いいえ」確認
ダイアログが**視覚的に表示されなくなる**（内部ロジック・入力受付は生きており、
上キー→決定キーで正常に終了はできた）という副作用が発生した。

`CONFIRMED`（ユーザー報告による対照確認）:
- F9を押していないプレイでは、「システム終了」時に「はい/いいえ」が正常に表示される。
- F9を押した回のプレイでは、同じ操作で選択肢が視覚的に表示されない
  （ただし入力受付・決定処理そのものは機能していた）。

`CONFIRMED`（静的解析）: `Nocturne Framerate Mod.dll`
（`Nocturne_Graphics_Configurator`、`.\Mods\`配下の別MOD、本プロジェクトのコードではない）
の全メソッドを確認したが、キーボード直接読み取り（`GetAsyncKeyState`/`Input.GetKey`等）は
一切存在しない。したがってF9キーそのものが他MODのホットキーと衝突しているわけではない。
同MODは`updateui()`（1918命令）を持ち、ゲーム自身のダイアログ/選択肢UIクラスである
`Il2Cpp.talkUI`/`Il2Cpp.talkChoice`へHarmonyパッチを当てている。

`HYPOTHESIS`: `SteamInputUtil.instance.ResetController()`の呼び出し自体
（16章の静的解析で確認済みの内部処理、または16.3節で未特定のRVA
`0x10D6760`/`0x28A1430`/`0x28A1370`の副作用）が、`Nocturne_Graphics_Configurator`側の
UIスケーリング/表示ロジック（`talkUI`/`talkChoice`パッチ）に予期しない状態変化を与え、
選択肢ダイアログの描画だけが無効化される、という経路が最も可能性が高い。

`UNRESOLVED`:
- 具体的にどの内部処理が競合しているかは、`Nocturne_Graphics_Configurator`側の
  ロジックまで踏み込む必要があり、本プロジェクトのスコープ外のため未調査。
- この視覚的な非表示が「システム終了」ダイアログに限定されるのか、他の選択肢UI
  （戦闘コマンド、会話選択肢等）にも及ぶのかは未確認。
- この状態が同一セッション中ずっと持続するのか、一定時間や特定操作で復帰するのかも未確認。

**安全性の判断**: `ResetController()`単独呼び出しは、Right Stick復活には無効
（17章でREJECTED）でありながら、ゲーム側に実害のある副作用（UI描画異常）を
及ぼすことが確認された。この事実を踏まえ、`ManualResetControllerPoc`（F9）の
追加使用は、原因究明が進むかユーザーが明示的に許可するまで、一旦保留を推奨する。
ユーザーの判断により、以後F9は使用せず、既存Guide/Overlay成功セッションデータ
（18章）のみで分析を継続する方針となった。managed-side deep-state観測はここで
一旦区切りとし、以降はnative解析に限定する（20章）。

## 20. Native解析（第2段）: `SteamPad.UpdateControl()`内の128.0書き込みループ（2026-09-07）

zero-baseで、`SteamInputUtil.ResetController()`のnative呼び出し元と、
`IG_RSTICK`アナログ値の解決経路を再調査した。使用ツールは引き続き
`Cpp2IL.exe --use-processor callanalyzer --output-as isil`（16章と同一手法）。
**まだ実装・patch・手動呼び出しは一切行っていない。静的解析のみ。**

### 20.1 `ResetController()`のnative呼び出し元 — CONFIRMED（否定的結果、16.1節を補強）/ UNRESOLVED

16.1節の否定的結果（IL2CPPコンパイル済み管理コード全体からの直接呼び出しが
1件もない）を再確認した上で、さらに以下を検証した。

`CONFIRMED`: `ResetController()`のRVA `0x25F9340`を、16進文字列として
（`Call`命令のオペランドとしてだけでなく、レジスタへのロード等の**データ参照としても**）
全42アセンブリのISIL全文から検索したが、**1件も出現しなかった**。

`HYPOTHESIS`（新規、より具体的な技術的根拠）: これは「呼び出し元が存在しない」の
ではなく、「呼び出しが静的な即値アドレスとして埋め込まれない方式（間接呼び出し）で
行われている」ことを強く示唆する。Steamworksの`Callback<T>`機構は、通常
コールバック登録時にデリゲート/関数ポインタをSteamworks SDK側（native、
Cpp2ILの解析対象外）へ渡し、`SteamAPI_RunCallbacks()`実行時にSDK側から
間接呼び出し（関数ポインタ経由）で呼び戻す。この経路はCpp2ILの静的アドレス
一致検索では原理的に発見できない。

`UNRESOLVED`: `SteamInputUtil`の`Awake()`のISIL全体を確認したが、
`Callback<T>`型フィールドへの新規登録処理（`m_GamepadTextInputDismissed`
以外）は見当たらなかった。ただし`Awake()`内の複数の未解決呼び出し
（RVA `0x18143AD70`、`0x1811D1570`ほか、C#側ラッパーが存在せず`findrva`で
未解決）が、実際にはSteamworksコールバック登録関数である可能性は排除できない。

### 20.2 `GameOverlayActivated_t`コールバック — CONFIRMED（未使用）

`Il2CppSteamworks.GameOverlayActivated_t`（Steamworks標準のOverlay表示/非表示
コールバック構造体、`m_bActive`フィールドを持つ）の型定義自体は
`Assembly-CSharp-firstpass.dll`に存在することを確認した。

`CONFIRMED`: `Assembly-CSharp`全体（`SteamInputUtil`/`SteamPad`を含む）を検索したが、
この型を参照するフィールド・メソッドシグネチャは1件もなかった。ゲーム側は
`GameOverlayActivated_t`コールバックを**購読していない**（少なくとも、型として
明示的に使う形では）。

`HYPOTHESIS`: Steam Overlay自体の開閉は、この標準コールバックではなく、
`ISteamUtils::IsOverlayEnabled`/`BOverlayNeedsPresent`のようなポーリング系API、
またはWindows側のフォーカスイベント経由で間接的に検知されている可能性がある。
いずれもCpp2ILの解析範囲内では確認できていない。

### 20.3 `SteamPad.UpdateControl()`内の分岐構造 — CONFIRMED（構造）/ HYPOTHESIS（意味）

`UpdateControl()`（RVA基準は16.2節参照、`SteamPad`のinstanceメソッド、
既存Evidence: `SteamInputUtil.UpdateData()`から毎フレーム`UpdateConnectedControllers()`
と並んで到達し得る経路上にある）のISIL全体を確認した。**この関数の中に、
DEAD/LIVEの正体そのものと直結する可能性が高い分岐構造を発見した。**

`CONFIRMED`（命令列として）:

```
if (A && B && C) {
    // 早期return分岐
    X = <singleton取得: 0x1825FC4B0(0)の戻り値>
    Call 0x1825F9E10(X, 0)
    return true
} else {
    // メインループ分岐: rbx = 0 .. 3 (4回反復)
    for (slot = 0; slot < 4; slot++) {
        obj1 = <static配列>[+112][+32+slot*8]
        if (obj1 != null && obj1.Length > slot) {
            nested = obj1[+32]  // さらにネストした配列
            if (nested != null && nested.Length > 1) {
                inner = nested[+32]
                if (inner.Length > slot) {
                    inner[+36] = 0x43000000   // = 128.0f (IEEE754)
                    inner[+32] = 0x43000000   // = 128.0f
                }
                inner2 = nested[+40]
                if (inner2.Length > slot) {
                    inner2[+36] = 0x43000000  // = 128.0f
                    inner2[+32] = 0x43000000  // = 128.0f
                }
            }
        }
    }
    return true
}
```

条件:
- `A` = `0x1825FF2C0(0) != null`。この関数は`SteamDlcFileUtil`（Steam DLC確認処理、
  Steam Inputと無関係のクラス）からも呼ばれており（`CONFIRMED`、2箇所のみで参照）、
  「Steam APIが利用可能か」といった汎用チェックの可能性が高い（`HYPOTHESIS`）。
- `B` = `[rbx+24] != 0`（`rbx` = `SteamPad`インスタンス自身のフィールド、
  offset +24 / 0x18）。用途はUNRESOLVEDだが、`SteamPad`固有のフィールドである
  ことは`CONFIRMED`。
- `C` = `0x1825FC4B0(0) != null`。この関数は`buttonguideUI`・`ChangeTex`・
  `cmpInit`など**互いに無関係な多数のクラス**から広く呼ばれており（`CONFIRMED`、
  10箇所以上）、Steam Input固有ではない汎用ヘルパーである可能性が高い
  （`HYPOTHESIS`）。

`0x1825F9E10`は、**全解析対象コードベース中でこの1箇所からしか呼ばれていない**
（`CONFIRMED`）。C#側のラッパーメソッドが存在せず、`findrva`でも解決できない
（native関数、または生成専用の内部メソッドの可能性）。

### 20.4 HYPOTHESIS（因果候補、CONFIRMEDへ昇格させない）

`UpdateControl()`は`Root26SteamState`計装より前段、つまり`GetPadAnalog`の
さらに上流にある。この分岐構造から導ける最有力仮説:

- **「else」分岐（メインループ）が実行されるフレームでは、最大4スロット分の
  アナログ格納領域へ`128.0`（float）が直接書き込まれる。** これは既存Evidence
  である「DEAD = native GetPadAnalogが常に128固定を返す」と、値・意味の両面で
  一致する（128は既存Evidenceの中心値そのもの）。
- **「if」分岐（早期return）が実行されるフレームでは、この128.0書き込みは
  発生せず、代わりに単一の未特定関数`0x1825F9E10`が呼ばれる。** これが
  実際のSteam Inputアナログ値取得・反映処理（`GetAnalogActionData`相当）
  である可能性が高い。
- したがって、**DEAD⇔LIVEの切り替えは、`UpdateControl()`内の`if(A && B && C)`
  分岐が毎フレームどちらを通るかによって決まっている**、というのが現時点で
  最も具体的で有力な仮説である。特に条件`B`（`SteamPad`インスタンス自身の
  offset +24フィールド）が、AおよびCが汎用的で常時真である可能性が高いことを
  踏まえると、実質的な決定要因である可能性が高い。

### 20.5 UNRESOLVED（次に解決すべき項目）

1. 条件`B`（`[SteamPad+24]`）の正体。`bInitialized`プロパティ（メタデータ上
   確認済みのbool）である可能性はあるが未確認。
2. `0x1825F9E10`の内部処理（真にGetAnalogActionData相当の処理を行っているか）。
3. Guide/Steam Overlayの操作が、条件`B`（またはA/C）の値をどう変化させるか
   （またはそもそも変化させないのか）。
4. `InputAnalogActionData_t`（`eMode`/`x`/`y`/`bActive`、20.2節近辺で型定義を
   確認済み）が実際にどのメソッド内でローカル変数として使われているか
   （シグネチャに現れないため、型定義の存在確認までに留まる）。

これらはいずれも、`0x1825F9E10`・条件B自体の生成元（`SteamPad`のフィールド
初期化コード）をさらに追う必要があり、今回のCpp2IL ISIL解析の範囲では
未解決である。既存方針どおり、focus/WM_INPUT/Method Dへは戻らず、
ResetController単独説へも戻らない。実装・patch・手動呼び出しはまだ行っていない。

## 21. Native解析（第3段）: `SteamPad+0x18`の正体（2026-09-07）

優先順位どおり `SteamPad+0x18`（条件B） → `0x1825F9E10` → `InputAnalogActionData_t`
の順で調査した。**静的解析のみ、patch/手動呼び出しは行っていない。**

### 21.1 `SteamPad+0x18`の初期値と全書き込み・読み取り箇所 — CONFIRMED

`SteamPad`の主要メソッド（`.ctor()`、`Initialize()`、`Awake()`のsteam_pad構築部、
`UpdateControl()`、`UpdateConnectedControllers()`、`SteamControllerReStart()`）の
ISIL全体を確認し、`this`（オブジェクト自身）を指すレジスタに対する`+24`
（`0x18`）へのアクセスをすべて洗い出した。配列/コレクションの`.Length`が
同じ`+24`オフセットに位置する（IL2CPPの一般的なレイアウト慣習）ため、
`this`ポインタを保持するレジスタと一致する箇所のみを対象とした。

`CONFIRMED`（書き込み箇所、全2箇所のみ）:

```
.ctor()      IL_end:  Move [rdi+24], 0        ← 生成時に明示的に0クリア
Initialize() IL_mid:  Move [rbx+24], rax       ← 0x1828A1370(0)の戻り値を格納
                       （0x1828A1370は16.3節で"Shutdown/Init相当"と推測した
                         2関数のうち、SteamControllerReStart()も呼ぶ"Init側"と
                         同一RVA）
```

`CONFIRMED`（読み取り箇所、確認した範囲内で全2箇所）:

```
UpdateControl()             : Compare [rbx+24], 0   ← 条件B（20章）
UpdateConnectedControllers(): Compare [rdi+24], 0   ← 同一構造の条件
                               (0x1825FF2C0(0)!=null && this+24!=0) の場合のみ
                               本体処理を実行し、そうでなければ即return
```

`SteamControllerReStart()`は`0x1828A1370(0)`を呼ぶが、戻り値を`+24`へは
**格納しない**（`System.Void`メソッドで戻り値を破棄している、`CONFIRMED`）。

### 21.2 重大な矛盾 — UNRESOLVED（CONFIRMEDへ昇格させない）

`SteamPad.Initialize()`（RVA `0x2602D00`）の呼び出し元を、独立した2つの手法で
検索した。

1. 今回のCpp2IL再解析（`callanalyzer`、全42アセンブリのISIL）でRVA
   `0x2602D00`への`Call`を検索 → **0件**。
2. MelonLoaderが生成した`Il2CppAssemblies/Assembly-CSharp.dll`自体に付与された
   `Il2CppInterop.Common.Attributes.CallerCountAttribute(0)` → **こちらも0件**
   （MelonLoader側の独自xref解析ツールによる、別経路からの確認）。

`CONFIRMED`: 2つの独立した手法で、`Initialize()`の呼び出し元が管理コード内に
1件も見つからなかった。

`UNRESOLVED`（矛盾の指摘）: `SteamPad+0x18`への書き込みが`.ctor()`（0クリア）と
`Initialize()`（呼び出し元不明）の2箇所しかなく、他のどのSteamPadメソッド
（`SteamControllerReStart()`含む）も書き込んでいないとすれば、**`Initialize()`が
一度も呼ばれない限り`SteamPad+0x18`は生成時の0のまま変化しないはずであり、
`UpdateControl()`/`UpdateConnectedControllers()`の条件Bは常にfalseとなり、
Right Stickは常にDEADのままになるはずである。** しかし実機Evidenceでは
LIVEへの遷移が複数回確認されている（14章・18章）。この矛盾は次のいずれかで
説明できる（いずれもHYPOTHESIS、検証できていない）:

- `Initialize()`はSteamworksコールバック経由（20.1節と同様の間接呼び出し）で
  呼ばれており、静的解析では検出できない。
- `Initialize()`はMelonLoader/Harmonyのreflectionベースの呼び出し
  （`MethodInfo.Invoke`等）経由で呼ばれており、IL本体に定数アドレスとして
  現れない。
- `SteamPad+0x18`はそもそも`bInitialized`ではなく、別の書き込み経路
  （今回チェックしていない残りのSteamPadメソッド、または`SteamInputUtil`側から
  直接メモリを書き換えるコード）が存在する。
- 今回の`.ctor()`として解析したコードが、実際に実行時に使われている
  コンストラクタ経路と異なる（複数コンストラクタ・Unity側の別インスタンス化
  経路の可能性）。

**結論**: `SteamPad+0x18`が条件Bの実体であることはCONFIRMEDだが、
それがDEAD⇔LIVEを実際に切り替えているかどうかは、この矛盾が解決するまで
HYPOTHESISに留める。20.4節の「条件Bが実質的な決定要因」という記述は、
本節の矛盾により信頼度を下げ、UNRESOLVEDへ差し戻す。

### 21.3 `0x1825F9E10`のcallee内部 — UNRESOLVED（到達不能）

`0x1825F9E10`は、Cpp2ILの型モデル上どの管理メソッド（`Assembly-CSharp`、
`Assembly-CSharp-firstpass`、`Il2Cppmscorlib`、`UnityEngine.CoreModule`を含め）
にも対応するC#メソッド定義が存在しない（`findrva`で不一致、ISILダンプにも
該当する`Method:`見出しが存在しない）。したがって、この関数のISIL命令列を
今回のツールセットでは取得できなかった。

`CONFIRMED`: 全解析範囲内で、この関数はSteamPad.UpdateControl()の1箇所からしか
呼ばれていない（20.3節で確認済み、再確認）。

`UNRESOLVED`: 内部処理は完全に不明。C#側ラッパーを持たない純粋native関数
（Steamworks SDKの薄いラッパー、またはIL2CPPが生成した名前なしヘルパー）で
ある可能性が高いが未確認。これ以上の追跡には、`GameAssembly.dll`を直接の
逆アセンブラ（Ghidra等）で解析する必要があり、今回の環境・ツールセットでは
実施できなかった。

### 21.4 `InputAnalogActionData_t`の使用箇所 — CONFIRMED（型定義のみ）/ UNRESOLVED（使用箇所）

`Il2CppSteamworks.InputAnalogActionData_t`（`eMode`/`x`/`y`/`bActive`）の
型定義は`Assembly-CSharp-firstpass.dll`に存在することを再確認した（`CONFIRMED`、
20.2節の再確認）。

`CONFIRMED`（否定的結果）: `Assembly-CSharp`内の全型・全メソッドシグネチャ
（パラメータ型・戻り値型）を検索したが、この型を直接使用するメソッドは
1件もない。

`HYPOTHESIS`: この型はローカル変数としてのみ使用されている可能性が高く、
最有力候補は21.3節で追跡不能だった`0x1825F9E10`の内部（Steamworksの
`GetAnalogActionData`相当のnative呼び出しを行い、結果をこの構造体で
受け取っている可能性）。

`UNRESOLVED`: 実際の使用箇所は特定できなかった。`GetAnalogActionData`相当の
Steamworks API呼び出しの痕跡（`ISteamInput`インターフェースへの仮想関数呼び出し
等）も、今回のISILベースの解析では発見できなかった。

### 21.5 まとめと次の候補

`SteamPad+0x18`は`UpdateControl()`/`UpdateConnectedControllers()`双方の
ゲート条件であることはCONFIRMEDだが、それを書き換える`Initialize()`の
呼び出し元が発見できないという矛盾（21.2節）により、これがDEAD⇔LIVEの
実際の切り替え機構であるという説はHYPOTHESISからUNRESOLVEDへ格下げする。
`0x1825F9E10`と`InputAnalogActionData_t`の実使用箇所は、現在のツールセット
（Cpp2IL ISIL解析）では到達限界に達した。これ以上の追跡には、
`GameAssembly.dll`の直接逆アセンブル（Ghidra等の専用ツール）が必要と考えられる。

## 22. Runtime probeによる`SteamPad+0x18`の実測（2026-09-07）

21.2節の矛盾を受け、静的解析を一旦区切り、read-only runtime probe
（`src/SteamPadOffset18Probe.cs`、`Root26SteamPadOffset18Probe`、DLL SHA-256
`de2524066127f29d2c210a82ae627c50b8f7e8a8142fd3b4abfea032b0bb5825`）を実装。
`SteamInputUtil.instance.steam_pad`の`Il2CppObjectBase.Pointer`を用いて、
native offset `+0x18`を`Marshal.ReadByte`/`Marshal.ReadInt64`で**読み取るのみ**
（書き込みなし）、値が変化した時だけログする構成。F9/ResetControllerは
今回不使用、Guide/Overlay成功セッションのみ取得。生ログは
`melonloader/Latest.log_20260907_Offset18Probe_raw.log`。

`CONFIRMED`:

```
20:18:57.981  byte@+0x18 / int64@+0x18: INITIAL -> 1（起動直後、探索開始前）
（以降、このセッション全体を通じて[Root26SteamPadOffset18]のSTATE-CHANGEは
  これ以外に1件も発生しなかった）
20:19:07.509  探索セッション1: X/Y INITIAL -> DEAD
20:19:09.771  セッション1終了 finalState=DEAD
20:19:09.889  CALL SteamInputUtil.ResetController()     count=1,2
20:19:09.890  CALL SteamPad.SteamControllerReStart()    count=3,4
20:19:10.411  探索セッション2: X/Y INITIAL -> DEAD
20:19:11.161  X/Y: DEAD -> LIVE
20:19:16.162  セッション2終了 finalState=LIVE(analog-active), transitions=1
```

`SteamPad+0x18`は、起動直後に`1`（true相当）へ一度だけ変化した後、
DEAD期間、Guide/Overlay復帰、`ResetController()`/`SteamControllerReStart()`
呼出し、LIVE期間のすべてを通じて**一切変化しなかった**。

`CONFIRMED`（21.2節の矛盾の部分的解消）: 起動直後に`+0x18`が実際に`0`→`1`へ
変化する事実そのものはruntime観測でCONFIRMEDされた。すなわち`Initialize()`
（またはこの値を1にする何らかの処理）は実際に実行されている。ただし
**その呼び出し経路自体は依然UNRESOLVED**のままである（静的解析で発見できず、
今回のprobeは値の変化を検知するのみで呼び出し元を特定する機能を持たない）。

**判定（ユーザー指定の判定基準に基づく）**: `SteamPad+0x18`はDEAD/LIVE境界でも
Guide/Overlay復帰の前後でも不変であった。したがって
**「条件B（`SteamPad+0x18`）主因説」はここでREJECTEDとする**
（20.4節で一時的に立てた仮説、21.2節でUNRESOLVEDに格下げ済みのものを、
本節のruntime実測により正式に否定する）。

`HYPOTHESIS`（次段階への示唆）: `UpdateControl()`の`if(A && B && C)`分岐の
うち、B（`+0x18`）は起動直後に恒久的にtrueとなる「一度きりの初期化完了ラッチ」
であり、DEAD/LIVEの実際の切り替えには関与しないことが強く示唆される。
したがって、20.3節で発見した「else分岐（128.0書き込みループ）とif分岐
（`0x1825F9E10`呼出し）」という分岐構造自体は正しい候補のままだが、
その分岐を実際に切り替えているのはA・Cのいずれか、またはこの分岐構造とは
別の場所（`0x1825F9E10`内部、またはUpdateControl()呼び出し元での
ガード）である可能性が高まった。

次段階は、方針どおり`0x1825F9E10`の直接native逆アセンブル
（`GameAssembly.dll`をGhidra等で解析）に進む。

## 23. Native逆アセンブル（Ghidra）による直接解析（2026-09-07）

Ghidra 11.2.1（headlessモード）をこの環境に新規インストールし、
`GameAssembly.dll`（383MB）を対象に**全体auto-analysisは行わず**、
`0x1825F9E10`・`0x1825FF2C0`・`0x1825FC4B0`の3アドレスのみを対象に、
その場でdisassemble + decompileするスクリプトを実行した。**静的解析のみ、
patch/手動呼出しは一切行っていない。** 実行ログは
`C:\Users\tanat\ghidra_run.log`（リポジトリ外、git管理下に置いていない）。

### 23.1 `0x1825F9E10`（最優先） — CONFIRMED

`CONFIRMED`（逆アセンブル・逆コンパイル結果から直接）:

- 関数サイズ1717バイト、`param_1`（第1引数、UpdateControl()の早期return分岐で
  渡していた「0x1825FC4B0(0)の戻り値」）を主対象に操作する。
- `param_1+0x24`を「負なら3、それ以外はデクリメント」というrepeat/cooldown
  カウンター的パターンで更新する。
- `param_1+0x50`にあるハンドル配列を`func_0x000182602c40(...)`で件数取得し、
  0件超ならループ処理。ループ内で`func_0x000181519140(...)`（bool返却）を
  呼び、trueなら`func_0x000182602f30(...)`の結果を別配列へ格納する。
- 複数箇所で `newBits = (prev XOR cur) & cur` という**典型的な「エッジ検出
  （press-this-frame）」のビット演算**が現れる（64bit整数のAND/XOR操作）。
- **浮動小数点命令（movss/movd/addss等）が1件も存在しない。**

`CONFIRMED`（結論）: `0x1825F9E10`は**デジタルボタンのpress/repeat状態処理**
（複数コントローラのボタン状態をXOR/AND方式でエッジ検出し、リピートカウンターを
回す処理）であり、アナログスティックのX/Y値を扱っていない。

**20.4節/20.5節の仮説（`0x1825F9E10`がSteam Input analog取得＝
`GetAnalogActionData`相当）はREJECTEDとする。**

### 23.2 `0x1825FF2C0`（条件A） / `0x1825FC4B0`（条件C） — CONFIRMED

両関数とも、共通の定型パターン（`test byte[X+0x12f],2` → `cmp [X+0xe0],0` →
条件付きで`0x180079560`呼出し、という「シングルトンのメインスレッド確認/遅延初期化」
定型句、全コードベースで数百箇所使われている汎用パターン）に加え、
`0x1814c69b0`/`0x1814c6af0`（型チェック系と思われるbool返却ヘルパー、
Unityの`is`/`GetComponent`型チェックに酷似）、`0x1800e6930`・`0x182843080`・
`0x181558200`・`0x182843250`・`0x1815583e0`（いずれもUnityの
`GetComponent<T>()`/`FindObjectOfType<T>()`類似の「オブジェクト解決」
パターンに酷似した2〜3引数の呼び出し連鎖）を用いて、**特定のUnity
コンポーネント/シングルトンを解決し、その`+0x18`バイトフィールドを
読んで返す**、という構造であることを確認した。

`CONFIRMED`: 両関数とも、確認した範囲の逆アセンブル・逆コンパイル結果には
浮動小数点命令が現れなかった。

`HYPOTHESIS`: 条件A・Cはいずれも「特定のマネージャ/シングルトン
コンポーネントが解決可能かつ準備完了か」を汎用的にチェックする
アクセサであり、Steam Input固有のロジックではなく、Unity的な
「このシステムはまだ利用可能か」の判定である可能性が高い。厳密な対象
コンポーネント名は、グローバルデータ（`_DAT_182e496a8`、`_DAT_182e4e4b8`
等）が指す型情報まで追わないと特定できずUNRESOLVED。

### 23.3 総括（2026-09-07、27章の結果を受けて再修正）

`CONFIRMED`: `0x1825F9E10`は、そのbit edge検出・repeatカウンター構造
（23.1節）から判断して、**Steam InputのGetAnalogActionData相当の
処理ではない**（デジタルボタンのpress/repeat処理である）。この点のみ
REJECTEDとして確定する。

`CONFIRMED`（27.3節で確定、本節の記述を最終的に更新）: **`UpdateControl()`
のif/else分岐構造は、DEAD⇔LIVEと無関係ではない。** else分岐は
`InputUtil.AnalogStickLRval`へ`128.0f`を直接書き込んでおり
（27.3節で命令列レベルで確認済み）、これは`dds3PadUpdate()`経由で
`dds3PrivatePadAnalog`へ伝播することも確認済みである。したがって
**「if/else分岐構造全体がDEAD⇔LIVEと無関係」という判定は誤りであり、
撤回する。** 少なくとも**else分岐は「analog値をneutralへ潰す経路」
として確実に関与している**（`CONFIRMED`）。

`UNRESOLVED`（現在の焦点）: if分岐（`0x1825F9E10`呼出し側）は
`AnalogStickLRval`に一切書き込まない（デジタルボタン処理のみ）ため、
**「実際のアナログ値がどこでAnalogStickLRvalへ書き込まれるか」は
依然未発見**である。真のLIVE値の書き込み元（Steamworksの
`GetAnalogActionData`相当の結果を`AnalogStickLRval`へ格納する箇所）は
27章以降で最優先に探索する。

### 23.4 UNRESOLVED（次の候補）

1. アナログX/Y（`InputAnalogActionData_t`相当）を実際に扱っている
   コード箇所はまだ未発見。`SteamPad.get_analogaction()`
   （private instanceメソッド、既存Evidence15.1節で存在確認済みだが
   本体は未逆アセンブル）が次の優先候補。
2. `dds3PadManager.dds3PadUpdate()`自体（`GetPadAnalog`が読む
   `dds3PadAnalog`配列を実際に書き換えるメソッド、native RVA未特定）も
   未調査であり、`SteamPad`側からの経路とは独立に調べる価値がある。
3. `0x1825FF2C0`/`0x1825FC4B0`が解決している具体的なコンポーネント型の
   特定（グローバルデータの型情報まで追う必要がある）。

Ghidraは`C:\Users\tanat\ghidra`、対象コピーは`C:\Users\tanat\ga_analysis`
（いずれもリポジトリ外）に保持している。継続調査時は再利用可能。

## 24. `SteamPad.get_analogaction()` / `dds3PadManager.dds3PadUpdate()`の直接解析（2026-09-07）

Ghidra既存プロジェクトを再利用し（再import不要）、
`SteamPad.get_analogaction()`（RVA `0x2604640`）、
`dds3PadManager.dds3PadUpdate()`（RVA `0x222D960`、4554バイトの巨大関数）、
`dds3PadManager.GetPadAnalog()`（RVA `0x222C1C0`）を対象に逆アセンブル・
逆コンパイルを実施した。**静的解析のみ、patch/手動呼出しは一切行っていない。**

### 24.1 `SteamPad.get_analogaction()` — CONFIRMED（役割の特定）

`CONFIRMED`（逆アセンブル・逆コンパイル結果から直接）:

- シグネチャは`(InputHandle_t handle, ref ActionInfo ainfo, string name)`だが、
  **`handle`引数（第1実引数、native RDX）は関数内で一度も参照されない**。
- `name`引数（native R9）を使って`func_0x0001828a0bf0(name, 0)`を呼び、
  その結果を`func_0x000180079cd0(_DAT_182e4f0a0, &local)`へ渡してハンドルを
  取得している。この「文字列 → ハンドル」という経路は、Steamworksの
  `GetAnalogActionHandle(const char* pszActionName)`（コントローラー非依存、
  アクション名からハンドルを引くAPI）の呼び出しパターンと一致する。
- 取得したハンドルと`name`文字列を新規オブジェクト（`AnalogAction`と推測）へ
  格納し、`func_0x000181494830(obj, 0)`で`ainfo`側のリスト
  （`ActionInfo.Analog`と推測）へ追加している（`List<T>.Add`相当の呼び出し
  パターン）。
- 浮動小数点命令は存在しない。

**結論（CONFIRMED）**: `get_analogaction()`は**「名前→ハンドルの一度きりの
登録処理」であり、現在のアナログ値（x/y/bActive）を取得する処理ではない**。
`handle`（どのコントローラーか）を必要としないのは、Steamworksの
`GetAnalogActionHandle`自体がコントローラー非依存のAPIであることと整合する。

`HYPOTHESIS`: `IG_LSTICK`と`IG_RSTICK`は、この同一関数に異なる`name`文字列
（"IG_LSTICK"/"IG_RSTICK"）を渡して呼び出されているだけであり、
**この関数自体にはLSTICK/RSTICKの処理差を生む要素がない**。差が生じ得る
とすれば「呼ばれる／呼ばれない」「いつ呼ばれるか」という**呼び出し側**の
条件差のみであり、これはまだ未調査（`get_analogaction()`自身のcallerは
今回追っていない）。

### 24.2 `dds3PadManager.dds3PadUpdate()` — CONFIRMED（重要な新手がかり）/ UNRESOLVED（L/R判別）

4554バイトの全体は今回精査しきれていないが、その中に
**「byteをfloatへ変換し、閾値と比較する」という、既存Evidenceの
デッドゾーン処理（`128 ± GetAnalogAdjust()`）と直接一致するブロック**を
発見した。

`CONFIRMED`（命令列として）:

```
RAX = <ある配列>[コントローラースロット]  ; R15をベースにした配列参照
EAX = byte[RAX + 0x20]        ; 1バイト読み取り（Xチャンネルと推測）
EAX = EAX - 0x80              ; **-128 の演算（centerが128であることと一致）**
XMM2 = (float)EAX             ; 整数→float変換 (CVTDQ2PS)
buffer[0] = XMM2               ; ローカルbufferへ格納 (func_0x182884830)

（同じパターンをbyte[RAX+0x21]（Yチャンネルと推測）に対しても実行、
  buffer[1]へ格納）

func_0x1826576e0(&buffer)      ; buffer(X,Y)を渡して呼び出し、float戻り値
COMISS XMM0, XMM13              ; 戻り値をあらかじめロードしておいた
                                 ; 定数閾値（GetAnalogAdjust系と推測）と比較
JNC ...                         ; 比較結果で分岐
  → 一方の分岐: 別配列へ 0（未押下/中立）を書き込む
  → 他方の分岐: 別配列へ 1（押下/非中立）を書き込む
```

`CONFIRMED`: `dds3PadUpdate()`関数全体を通じて、この「`-0x80`によるbyte中心化」
パターンは**この1箇所（X/Yの2軸分）にしか出現しない**。

`HYPOTHESIS`（強く示唆されるが未確定）: このブロックが読む生byte配列
（`[R15+スロット]+0x20`/`+0x21`）は`dds3PrivatePadAnalog`（または類する
「Steam Input経由の生アナログ値」バッファ）である可能性が高く、
`func_0x1826576e0`はデッドゾーン判定（magnitude計算またはaxis別しきい値
比較）に相当する可能性が高い。この処理の後段で0/1の「非中立フラグ」を
別配列へ書き込んでいることから、**「アナログ値そのもの」ではなく
「非中立かどうかのデジタル化されたフラグ」を生成している**可能性がある
（Right Stick方向割当コード`Pad_RStickUp/Down/Left/Right`（既存Evidence
4.1節）に関連する処理の可能性）。

`UNRESOLVED`（最重要な未解決点）:

1. このブロックが処理しているのが**Left StickかRight Stickか**（あるいは
   両方を別ループで処理しており、たまたま一方の反復のみを読んだのか）は
   未確定。関数全体でこの`-0x80`パターンが1箇所のみだったことから、
   「LSTICKとRSTICKで処理経路自体が異なり、このブロックは片方専用」
   という可能性と、「共通ループの中の1回の展開がこれで、実際は両方が
   通る」という可能性の両方が残る。
2. `func_0x1826576e0`・`func_0x182884830`の内部処理は未逆アセンブル。
3. このブロックの出力（0/1フラグ）と、`dds3PadManager.GetPadAnalog`が
   最終的に返す**生byte値（128固定 or 実値）自体**が同じ経路で
   決まっているのか、別経路なのかは未確認。`GetPadAnalog`自体
   （RVA `0x222C1C0`）は今回取得済みだが未解析（次の作業候補）。
4. `get_analogaction()`の呼び出し元（"IG_LSTICK"/"IG_RSTICK"呼び分けの
   実際の発生箇所）は未調査。

### 24.3 次の候補

優先度の高い順:

1. `func_0x1826576e0`の内部を逆アセンブルし、デッドゾーン処理の実体を確認。
2. 24.2節のブロックの直前・直後（ループ構造全体、特にR15/R14配列の由来と、
   どのインデックスがLSTICK/RSTICKに対応するか）を追い、L/R判別を確定する。
3. `dds3PadManager.GetPadAnalog()`（既取得、未解析）を確認し、この
   ブロックの出力（またはその上流の生byte配列）と実際に接続されているかを
   確認する。
4. `get_analogaction()`の呼び出し元を検索し、"IG_LSTICK"/"IG_RSTICK"の
   呼び分け条件を確認する。

いずれも静的解析のみで継続可能。Ghidraプロジェクトは
`C:\Users\tanat\ghidra_project`に保持済み。

## 25. `dds3PadManager.GetPadAnalog()`内のRight Stick専用分岐（2026-09-07）— 最重要発見

`GetPadAnalog()`自体（299バイト、既取得のRVA `0x222C1C0`）を精査した結果、
既存Evidenceの中心テーマに**直接回答するCONFIRMED事実**を発見した。

### 25.1 `GetPadAnalog()`の実際の構造 — CONFIRMED

逆コンパイル結果（要約、`padno`/`stick_lr`/`xy`/`cip_no`は既存Evidenceの
引数名に対応）:

```c
byte GetPadAnalog(int padno, int stick_lr, int xy, int cip_no)
{
    lVar1 = dds3PadAnalog[padno];              // 3段ネスト配列
    lVar1 = lVar1[stick_lr];
    bVar6 = *(byte*)(lVar1 + xy);               // dds3PadAnalog[padno][stick_lr][xy] を読む

    bVar3 = func_0x1822a7900(padno, stick_lr, xy, cip_no, 0);  // 常に呼ぶ

    if (bVar6 == 0x80) {          // 格納値が厳密に128(中心値)の場合のみ
        bVar6 = bVar3;             // func_0x1822a7900の戻り値で上書き
    }
    return bVar6;
}
```

`CONFIRMED`: `dds3PadAnalog[padno][stick_lr][xy]`に格納されている値が
**厳密に128以外**なら、その値がそのまま返る（`func_0x1822a7900`の結果は
無視される）。格納値が**厳密に128**の場合のみ、`func_0x1822a7900`の
戻り値で上書きされる。

### 25.2 `func_0x1822a7900`（VA `0x1822a7900`） — CONFIRMED（Right Stick専用分岐の実在）

逆コンパイル結果（全文、361バイト）:

```c
byte func_0x1822a7900(int padno, int stick_lr, int xy, int cip_no)
{
    if (stick_lr == 1) {                                    // ← Right Stickのみ
        if (xy == 0) {                                       // X軸
            if (DDS3_PADCHECK的関数(map=4, cip_no)) return 0;
            if (DDS3_PADCHECK的関数(map=5, cip_no)) return 0xff;
        }
        else if (xy == 1) {                                  // Y軸
            if (DDS3_PADCHECK的関数(map=6, cip_no)) return 0xff;
            if (DDS3_PADCHECK的関数(map=7, cip_no)) return 0;
        }
    }
    return 0x80;   // ← stick_lr==0（Left Stick）は常にこの行へ直行
}
```

（`DDS3_PADCHECK的関数` = `func_0x0001822a53c0(0, mapID, cip_no, 0, 0)`、
既存Evidenceの`DDS3_PADCHECK_PRESS`類似の、単一マップIDに対するbool判定
関数と推測される。）

`CONFIRMED`（これは今回の調査全体で最も明確な非対称性の証拠）:

- **`stick_lr == 1`（Right Stick）の場合のみ**、4つの「デジタルパッドマップ」
  的な条件（map ID 4/5/6/7）をチェックし、いずれかが真なら`0`または`0xff`
  （全振り値）を返す。
- **`stick_lr == 0`（Left Stick）の場合、この関数はどのような条件でも
  常に`0x80`（128）を返す**（Left Stick用の分岐は存在しない、
  無条件でデフォルト値行へ落ちる）。
- したがって、`GetPadAnalog`のフォールバック経路（`dds3PadAnalog`格納値が
  128のとき）において、**Right Stickだけが「代替手段（デジタル入力の
  全振り値への変換）」を持ち、Left Stickは常に128固定のまま**という、
  明確なコード上の非対称性が存在する。

`HYPOTHESIS`: この関数が返す値は`0`/`0x80`/`0xff`の3値のみであり、
実機で観測されたLIVE時の滑らかな中間値（例: 111、219、143等）を
説明できない。したがって**この関数自体は、既存Evidenceで観測された
「滑らかなLIVEアナログ値」の発生源ではない**。この関数は「デジタル入力
（キーボード等）による疑似アナログ全振り」のためのフォールバックである
可能性が高く、真のLIVE値は`dds3PadAnalog[padno][1][xy]`へ**別の経路で
直接書き込まれている**と考えられる（その書き込み経路はまだ未発見）。

`UNRESOLVED`:
1. `dds3PadAnalog[padno][stick_lr][xy]`へ実際に値を書き込んでいる箇所
   （128書き込みも、真のLIVE値書き込みも）はまだ特定できていない。
   `dds3PadUpdate()`内で確認した`-0x80`ブロック（24.2節）は、生成している
   値が0/1のフラグであり、`dds3PadAnalog`そのものへの書き込みとは
   直接確認できていない。
2. map ID 4/5/6/7が具体的にどの物理入力（キーボードのカーソルキー等）に
   対応するかは未確認。
3. `func_0x0001822a53c0`（`DDS3_PADCHECK`類似判定）自体は未逆アセンブル。

### 25.3 `func_0x1826576e0`・`func_0x182884830`（24章のフォローアップ） — CONFIRMED

`func_0x182884830(buffer, index, value)`: `index`（0〜3）に応じて
`buffer`の対応floatフィールドへ`value`を書き込むだけの、**Vector4的な
汎用セッター**であることを確認した（Steam Input固有ではない）。

`func_0x1826576e0`: グローバル設定オブジェクトから`Vector2`
（`+0x10`/`+0x18`、デッドゾーン関連の設定値と推測）を取得し、
`func_0x000182884710`（Vector演算、詳細未確認）・
`func_0x0001828834d0`（同上）を経由して1つのfloatを返す、**汎用的な
デッドゾーン/距離計算ヘルパー**であることを確認した。L/R固有の分岐は
このヘルパー自体には存在せず、24.2節の`-0x80`ブロックが**L/R共通のコードを
両スティックに対して呼んでいる可能性が高い**（25.2節の非対称性とは
別レイヤー）。

### 25.4 総括

`CONFIRMED`: Right Stick固有の分岐は**確かに実在する**（25.2節、
`func_0x1822a7900`内の`if (stick_lr==1)`）。ただし、この分岐が生成する
値は離散的（0/128/255）であり、実機で観測された滑らかなLIVE値の
直接の発生源ではない。

`HYPOTHESIS`: 真の滑らかなLIVE値は、`dds3PadAnalog[padno][1][xy]`への
**別の書き込み経路**（未発見）によって生成されている。DEAD状態とは
「その書き込みが行われず、格納値が128のままである」状態であり、
その場合にのみ25.2節のRight Stick専用フォールバックが働く、という
2層構造である可能性が高い。

`UNRESOLVED`: `dds3PadAnalog`への直接の書き込み元（`dds3PadUpdate()`内の
どこか、または`dds3PadManager`の別メソッド）は次の最優先調査対象。

## 26. `dds3PrivatePadAnalog`への実際の書き込み元を発見（2026-09-07）

### 26.1 調査方法と否定的結果 — CONFIRMED

`GetPadAnalog`から`dds3PadAnalog`の実体は
`*(longlong*)(_DAT_182e44688 + 0xb8) + 0x30`であることが確定していた
（25章）。この`+0x30`アクセスパターンを手がかりに:

- `dds3PadUpdate()`（4554バイト全体）を精査したが、**`_DAT_182e44688`
  への参照はあるものの、オフセット`+0x30`（`dds3PadAnalog`）への
  アクセスは1件も存在しなかった**（`CONFIRMED`、否定的結果）。
- Cpp2IL ISILダンプ全体（全42アセンブリ）を`182E44688`（大文字小文字
  無視）で横断検索した結果、この値を参照するのは
  `dds3PadManager`自身に加え、`dds3ConfigKeyBoardSteam`・
  `dds3KernelCore`・`dds3KernelMain`・`fclEncyc`・`scrCommonCommand`の
  **合計6クラスのみ**であることを確認した（`CONFIRMED`）。
  **`SteamPad`/`SteamInputUtil`はこのグローバルを一切参照していない**
  （`CONFIRMED`、重要な否定的結果 — SteamPad側からdds3PadManagerの
  静的フィールドテーブルへの直接アクセスは存在しない）。
- `dds3ConfigKeyBoardSteam`は設定UI（キー割当変更画面）のコールバック群
  であり、実行時の値書き込みとは無関係と判断した（`CONFIRMED`、
  メソッド名一覧確認による）。
- 残り4クラスいずれにも、`182E44688`参照の近傍で`+0x30`アクセスは
  見つからなかった（`CONFIRMED`）。

`CONFIRMED`（プロパティのgetter/setterについて）: `dds3PadAnalog`等の
プロパティは、native側に独立した`get_dds3PadAnalog`/`set_dds3PadAnalog`
関数を持たない（メタデータ上のメソッド一覧に存在しない）。したがって
これらのフィールドはIL2CPPインターロップ層が直接メモリオフセットで
読み書きする「実質public field」であり、native側でも常に
「グローバル→+0xb8→+オフセット」という直接アクセスパターンで
読み書きされる（呼び出し経由のsetterは存在しない）。

### 26.2 `dds3PrivatePadAnalog`（+0x18）への実際の書き込みを発見 — CONFIRMED

`dds3PadUpdate()`を全文精査した結果、`+0x30`ではなく**`+0x18`
（`dds3PrivatePadAnalog`、宣言順から算出、26.1節と同じ手法で確認）**
への実際の書き込みを発見した。

```c
// (1) ソースのVector2相当を読み、byteへ変換（デッドゾーン/クランプ処理込み）
lVar13 = _DAT_182e4c718 -> +0xb8 -> +0x70;   // 2段配列
lVar13 = lVar13[uVar20][uVar19];              // leafオブジェクト
fVar27 = *(float*)(lVar13 + 0x20) * fVar7 + fVar7;   // X: raw*係数+係数
fVar25 = *(float*)(lVar13 + 0x24) * fVar7 + fVar7;   // Y: raw*係数+係数
// (クランプ/デッドゾーン相当の分岐で fVar26/fVar27 を決定)
*(char*)(staging + 0x20) = (char)(int)fVar26;   // X をbyteへ格納
*(char*)(staging + 0x21) = (char)(int)fVar27;   // Y をbyteへ格納

// (2) stagingからdds3PrivatePadAnalogへコピー
dst = dds3PrivatePadAnalog[uVar20][uVar18];     // _DAT_182e44688+0xb8+0x18 経由
*(byte*)(dst + 0x20) = *(byte*)(staging + 0x20);  // X
*(byte*)(dst + 0x21) = *(byte*)(staging + 0x21);  // Y
```

`CONFIRMED`: `fVar27 = raw_x * fVar7 + fVar7`という式が存在すること自体は
命令列として確認済みだが、`fVar7`の実際の値は未確認である。

`UNRESOLVED`（27章の発見を受けた訂正）: 27.3節で、`AnalogStickLRval`
自体に対して**リセット時に`128.0f`という値がそのまま直接書き込まれる**
ことが判明した。仮に`AnalogStickLRval`がSteamworksの生float
`[-1,+1]`レンジを保持するバッファなら、その「中立値」は`0.0f`である
はずで、`128.0f`をリセット値として書き込む設計とは整合しない。
**したがって`AnalogStickLRval`はSteamworksの`InputAnalogActionData_t.x/.y`
そのもの（`[-1,+1]`レンジ）ではなく、既にこのゲーム独自の
「中心128」を前提とした中間表現（内部正規化後の値）である可能性が高い。**
`raw*127+127`を「Steamworks float→byte変換の実体」と断定した前回の
記述は撤回し、「意味不明な線形変換が存在する」という事実のみを
CONFIRMEDとして残す。真にSteamworksの生float値をこのゲーム内部表現へ
変換している箇所は、依然として別途特定が必要である（27章参照）。

`UNRESOLVED`（重要、次の最優先）:

1. `_DAT_182e4c718 -> +0xb8 -> +0x70`が指す配列の**由来・型**は未確認。
   この配列こそが、Steamworksの`GetAnalogActionData`相当の結果を
   保持する「本当のソース」である可能性が高いが、それを実際に
   埋めている書き込み元（native側 or 完全にnativeのみで完結する
   処理）はまだ特定できていない。
2. このwriteループ自体（`uVar20`/`uVar19`/`uVar18`のインデックス）が
   **LSTICK/RSTICKを対称に処理しているように見える**（コード上、
   `idx==1`のような分岐は見当たらない）。これは25章で発見した
   `func_0x1822a7900`内の明確な`if(stick_lr==1)`非対称性とは対照的で
   ある。したがって、**この書き込みパス自体は対称であり、非対称性は
   このパスの「上流」（`_DAT_182e4c718+0xb8+0x70`を実際に埋める処理）に
   ある可能性が高い**。
3. `dds3PrivatePadAnalog`への書き込みが確認できたのみで、
   `dds3PadAnalog`（GetPadAnalogが主に読む方）自体への書き込みは
   依然未発見。両者の関係（`dds3PrivatePadAnalog`から`dds3PadAnalog`への
   コピーがどこかで行われているのか、あるいは別々に更新されるのか）は
   UNRESOLVED。
4. `uVar20`/`uVar19`/`uVar18`が具体的に`padno`/`stick_lr`のどちらに
   対応するかは、ループ境界（いずれも0..1の2回）が一致するため未確定
   （両方とも2値を取り得るため、コード上の変数名だけでは判別できない）。

### 26.3 次の最優先候補（本節時点、27章で更新）

1. `_DAT_182e4c718 -> +0xb8 -> +0x70`の実体（型・書き込み元）を追跡する。
   このアドレスチェーンが指す配列が、SteamPad/SteamInputUtil由来なのか、
   別の独立したネイティブ入力バッファなのかが、残る最大の未解決点である。
2. `dds3PrivatePadAnalog`から`dds3PadAnalog`（+0x30）へのコピー処理を
   （`dds3PadUpdate()`以外のメソッドを含めて）探す。
3. 26.2節の書き込みループが本当に対称かどうか、より広い前後関脈
   （このループを囲む条件分岐全体）を確認する。

いずれも静的解析のみで継続可能。

## 27. `_DAT_182e4c718`の正体は`InputUtil`、`SteamPad.UpdateControl()`との接続を確認（2026-09-07）

### 27.1 `_DAT_182e4c718`の正体 — CONFIRMED

Cpp2IL ISILダンプ全体を横断検索した結果、`0x182E4C718`を参照するのは
`dds3ConfigKeyBoardSteam`・`dds3KernelMisc`・`dds3PadManager`・
**`InputUtil`**・`sdf_PadManager`・`Sega/PrismStatus`・
`SteamInputAssign`・**`SteamInputUtil`**・`SteamMouse`・**`SteamPad`**の
10クラスであることを確認した。`InputUtil`自身の`Init()`
（RVA `0x2141540`、Ghidraで確認）が、この`+0xb8`チェーンの先で
`EDX=2`（要素数2）の配列を2段階で確保する処理を実行しており、
これは`InputUtil`の静的フィールド初期化そのものと一致する。

`CONFIRMED`: `_DAT_182e4c718`は`InputUtil`クラス自身の
「静的コンストラクタ実行済みフラグ/型情報」シングルトンである
（`+0xb8`が静的フィールドテーブル本体を指す、既存の全クラス共通パターン）。

### 27.2 `InputUtil.AnalogStickLRval`の実体確認 — CONFIRMED

`InputUtil`のメタデータ確認により、フィールド宣言順から
`+0x70`（=10進112）に対応するプロパティは**`AnalogStickLRval`**
（型: `float[][][]`、3段配列）であることを確認した。native側に
`get_`/`set_`の独立関数は存在せず（メタデータ上に該当メソッドなし）、
全アクセスは`_DAT_182e4c718+0xb8+0x70`への直接メモリオフセットで
行われる。

`CONFIRMED`（`InputUtil.Init()`, RVA `0x2141540`）: 起動時に
`AnalogStickLRval = new float[2][...]`（外側サイズ2、内側もサイズ2で
確保）という初期化処理を確認した。

`CONFIRMED`（否定的結果、`InputUtil.Update()`, RVA `0x2141880`,
1348バイト全文精査）: **`Update()`はキーボードのキー状態管理
（`currentKey`/`oldKey`/`oneShotKey`/`repeatKey`/リピートタイマー、
`uVar11=0..3`×`iVar14=0..31`のビット走査）のみを行っており、
`AnalogStickLRval`（+0x70）には一切アクセスしていない。**

### 27.3 `SteamPad.UpdateControl()`と`InputUtil.AnalogStickLRval`の接続を確認 — CONFIRMED（本調査全体の核心）

`SteamPad.UpdateControl()`のISIL（Cpp2IL callanalyzer出力）を再確認した
結果、20.3節・24章で解析した「else分岐（128.0書き込みループ）」が、
**まさに`InputUtil.AnalogStickLRval`を対象に書き込んでいる**ことを
直接確認した。

```
rax = [0x182E4C718]          ; InputUtilシングルトン
rax = [rax + 184]            ; = +0xb8、静的フィールドテーブル
r9  = [rax + 24]  (=+0x18)   ; InputUtilの別フィールド(oneShotKey等)
r8  = [rax + 16]  (=+0x10)
rcx = [rax + 8]   (=+0x08)
（r9/r8/rcxの各要素[rdx]へ 0 を書き込み — digital key状態のクリア）
...
rax = [0x182E4C718]
rax = [rax + 184]
rcx = [rax + 112] (=+0x70)   ; ★ InputUtil.AnalogStickLRval ★
rax = rcx[rdx]                ; AnalogStickLRval[slot]
rax[+32] = 0x43000000  (128.0f)
rax[+36] = 0x43000000  (128.0f)
（同様の書き込みブロックがもう1組、[+40]経由でも存在）
```

`CONFIRMED`: **`SteamPad.UpdateControl()`の「else」分岐（条件A&&B&&Cが
不成立の場合）は、`InputUtil.AnalogStickLRval[slot]`へ`128.0f`を
直接書き込む。** これが20.3節・26.2節で追跡した「128.0書き込み」の
真の起点である。26.2節で確認した`dds3PadUpdate()`内の変換処理
（`AnalogStickLRval`を読んでbyte化し`dds3PrivatePadAnalog`へコピー）と
合わせ、次の完全な経路がCONFIRMEDされた:

```
SteamPad.UpdateControl() else分岐
  → InputUtil.AnalogStickLRval[slot] = 128.0f (X/Y)
    ↓
dds3PadManager.dds3PadUpdate()
  → InputUtil.AnalogStickLRval[slot] を読み、byte変換
    ↓ (raw*127+127、デッドゾーン処理)
dds3PadManager.dds3PrivatePadAnalog[?][?] へbyte書き込み
```

`HYPOTHESIS`（重要な新知見）: **`UpdateControl()`の「if」分岐
（条件A&&B&&C成立時）は`AnalogStickLRval`に一切書き込まない**
（23.1節で確認済み、`0x1825F9E10`はデジタルボタン処理のみ）。
したがって、`AnalogStickLRval`への書き込みは
「else分岐実行時に128.0へリセットされる」ケースしか、
少なくとも`UpdateControl()`内には存在しない。**`AnalogStickLRval`に
"実際の"滑らかなアナログ値を書き込む処理は、`UpdateControl()`にも
`InputUtil.Update()`にも存在しない**、という新たな否定的結果が
得られた。

`UNRESOLVED`（最重要、更新）:
1. `AnalogStickLRval`へ実際の（128以外の）滑らかな値を書き込む箇所は
   依然未発見。`UpdateControl()`の「if」分岐、`InputUtil.Update()`の
   いずれでもないことがCONFIRMEDされたため、他のSteamPad/SteamInputUtil
   メソッド、または完全にnativeのみで完結する処理（Steamworks
   コールバック等、IL2CPPコンパイル済みコードとして現れない経路）に
   ある可能性が高まった。
2. 「else分岐でrdxが0..3を回る」ように見える書き込みループの正確な
   反復回数・インデックス対応は未確定（`AnalogStickLRval`の実際の
   確保サイズが`Init()`で見た「2」と矛盾しないかも含め未検証）。
3. L/R非対称性の真因は、25章で確認した`GetPadAnalog`側の
   `func_0x1822a7900`（フォールバック限定でRight Stick専用）に加えて、
   もう一箇所——「`AnalogStickLRval`へ実際の値が書き込まれる/
   書き込まれない」を左右する未発見の処理——に存在する可能性が高い。

### 27.4 次の最優先候補（28章で更新・大幅前進）

いずれも静的解析のみで継続可能。28章参照。

## 28. `SteamInputUtil.SetAnalog()`とその唯一の呼び出し元を発見（2026-09-07）— 本調査全体の最大の前進

### 28.1 訂正: `AnalogStickLRval`は生Steamworks値ではない — CONFIRMED

ユーザー指摘を受け、26.2節の`raw*127+127`という解釈を撤回した
（該当箇所を修正済み）。27.3節で`AnalogStickLRval`に対し中立値として
`128.0f`が直接書き込まれることを確認しているため、**`AnalogStickLRval`は
Steamworksの`[-1,+1]`レンジではなく、ゲーム独自の内部表現
（中心値128付近）である**、という位置づけに訂正済み。

### 28.2 `SteamInputUtil.SetAnalog()` — CONFIRMED（`AnalogStickLRval`への真の書き込み元）

`182E4C718→+184(0xb8)→+112(0x70)`という完全なチェーン（27章で確立した
検証手法）を、`SteamPad.txt`・`SteamInputUtil.txt`全体に対して機械的に
適用した。誤検出（無関係クラスの`+112`）を排除した結果、
`SteamInputUtil.SetAnalog(ref System.UInt64 ret, System.Int32 index,
System.Int32 i, System.Single dx, System.Single dy)`
（RVA `0x25F93E0`）が、このチェーンを通じて**`AnalogStickLRval[index][i]`へ
実際に書き込みを行っている**ことを確認した。

`CONFIRMED`（命令列から）:
- 既存の`AnalogStickLRval[index][i].x`（オフセット`+0x20`）を読み出す。
- 引数`dx`と、既存値・定数（`[1828C7494h]`・`[1828C7BD4h]`など、
  用途未確認の閾値/クランプ定数）を用いた浮動小数点演算
  （`comiss`/`minss`/`maxss`によるクランプ処理）を行う。
- 計算結果を`AnalogStickLRval[index][i].x`へ`movss`で書き戻す
  （`y`成分についても同様の処理が後続する）。
- `ret`（`ref ulong`）には、何らかのビットマスクが格納される
  （用途未確認、恐らく方向/press相当のデジタルビット）。

### 28.3 `SetAnalog()`の唯一の呼び出し元: `SteamPad.SteamPadSet(int index)` — CONFIRMED（本調査の核心）

`SetAnalog`のRVA（`0x25F93E0`）を全ISILダンプ（42アセンブリ全体）で
横断検索した結果、**呼び出し元は`SteamPad.SteamPadSet(int index)`の
1箇所のみ**であることを確認した。

`CONFIRMED`（`SteamPadSet(int index)`の構造、instanceメソッド、
`index`=接続コントローラーのインデックス）:

```
this.Controller[index] を取得（InputInfoオブジェクト）
  ↓
ControllerTypeを取得し、値が10かどうかで
  this.フィールドA（+64）または this.フィールドB（+56）
  のどちらの「ActionSet的構造」を使うか選択
  （HYPOTHESIS: デバイス種別によるaction-set切替）
  ↓
その構造から18要素のループを実行（用途未確認）
  ↓
for (i = 0; i < 2; i++) {                       ← ★2回ループ★
    ActionSets[Current].Analog[i] のHandleを取得  ← IG_LSTICK/IG_RSTICKと推測
    func_0x1828A0AF0(&buffer, controllerHandle, analogActionHandle)
        ↓ CONFIRMED: Steamworks GetAnalogActionData相当の呼び出し
    buffer から mode(8B) と x/y(movsd、8B一括読み) を取得
    dx = buffer.x, dy = buffer.y
    SetAnalog(ref bitmask, index, i, dx, dy)      ← AnalogStickLRval[index][i]へ書込み
}
```

`CONFIRMED`: このループは`i=0,1`の2回、**コード上は完全に対称**に
実行される（`if (i==1) {...}`のような分岐は見当たらない）。両方とも
同一の`func_0x1828A0AF0`（Steamworks `GetAnalogActionData`相当と
推測、未逆アセンブル）を経由して実データを取得し、`SetAnalog`で
書き込む。

`HYPOTHESIS`（新規、最有力）: `i`は`SteamPad.EAnalogActionsInGameControls`
（`IG_LSTICK=0`, `IG_RSTICK=1`）に対応し、`AnalogStickLRval[index][i]`の
第2次元がLSTICK/RSTICKを表す、という23章までの推測を裏付ける。

`func_0x1828A0AF0`の**戻り値そのもの**（`buffer.mode`/`buffer.x`/
`buffer.y`）が、IG_RSTICKの場合にのみ無効・ゼロ・інactiveになる
という可能性が、現時点で最もシンプルにRight Stick限定の障害を
説明できる。ただし`func_0x1828A0AF0`自体は未解析であり、
CONFIRMEDではない。

### 28.4 UNRESOLVED（最重要、更新）

1. `func_0x1828A0AF0`（Steamworks `GetAnalogActionData`相当と推測される
   呼び出し）の内部は未解析。この関数自体が返す`mode`/`x`/`y`/`bActive`
   相当の値が、IG_LSTICKとIG_RSTICKで異なる結果を返す条件があるかは
   最優先の次の調査対象。
2. `ActionSets[Current].Analog[0]`/`[1]`の`Handle`が、実際に
   `IG_LSTICK`/`IG_RSTICK`のどちらに対応するかの厳密な確認
   （24.1節の`get_analogaction()`呼び出し順に依存、未確認）。
3. `SteamPadSet(index)`内の「ControllerType==10による2種類の
   ActionSet切替」（+64 vs +56フィールド）が、L/R非対称性と
   関係するかは未確認。
4. `SteamPadSet(index)`自体の呼び出し元・呼び出し頻度・Guide/Overlay
   前後での挙動変化は未確認（次の優先候補）。
5. `SetAnalog()`内のクランプ定数（`1828C7494h`/`1828C7BD4h`）の
   実際の値は未確認。

### 28.5 `func_0x1828A0AF0`の正体確認 — CONFIRMED（Steamworks `GetAnalogActionData`相当）/ HYPOTHESIS（真因、新規・最有力）

Ghidraで直接逆アセンブル・逆コンパイルした。全文248バイト。

`CONFIRMED`（構造として）:

```c
undefined8* FUN_1828a0af0(outBuffer, handle_arg2, actionHandle_arg3)
{
    // 一度きりの初期化: native関数シグネチャ記述子を組み立て、
    // 0x1800E69D0（IL2CPPの「このシグネチャに一致するnative関数ポインタを
    // 動的解決する」定型ヘルパー、既存パターンと一致）で
    // 関数ポインタを解決しキャッシュする
    if (_DAT_182e2f2c0 == null) {
        _DAT_182e2f2c0 = 0x1800E69D0(signature descriptor);
    }
    // 解決済みnative関数ポインタを直接呼び出す
    result = (*_DAT_182e2f2c0)(localBuffer, someHandle, handle_arg2, actionHandle_arg3);

    // 戻り値構造体をコピー: {x,y(float×2, 8byte一括movsd)} + {mode(int32)} + {bActive(byte)}
    outBuffer->xy   = result->xy;      // 8 bytes
    outBuffer->mode = result->mode;    // 4 bytes
    outBuffer->bActive = result->bActive; // 1 byte
    return outBuffer;
}
```

`CONFIRMED`: 戻り値構造体のレイアウト（float×2 + int32 + byte）は、
`Il2CppSteamworks.InputAnalogActionData_t { EInputSourceMode eMode;
float x; float y; byte bActive; }`（20.2節・21.4節で型定義のみ確認済み
だった構造体）の**フィールド構成と完全に一致する**。

**結論（CONFIRMED）**: `func_0x1828A0AF0`は、**Steamworksの
`ISteamInput::GetAnalogActionData()`そのもの（動的に解決したnative
関数ポインタ経由の直接呼び出し）である。**これは本調査で最初に
`InputAnalogActionData_t`の型定義を確認して以来、探し続けていた
「Steamworks GetAnalogActionData相当のAPI呼び出し」の実体である。

`CONFIRMED`（対称性）: この関数自体は`handle`と`actionHandle`を
そのまま渡すだけの汎用ラッパーであり、**LSTICK/RSTICKを区別する
コードは一切存在しない**。28.3節のループから`i=0`（LSTICK）でも
`i=1`（RSTICK）でも同一のこの関数が同一の経路で呼ばれる。

### 28.6 真因についての候補整理 — HYPOTHESIS（未確認、複数残存）

`func_0x1828A0AF0`自体にLSTICK/RSTICK差がないことがCONFIRMEDされた
ことで、非対称性の原因は**この関数の呼び出し結果そのものより手前
（渡される引数・呼び出しに至るまでの経路）**、または
**Steamworks（Steamクライアント側）が保持するコントローラー設定・
バインディングそのもの**にある可能性が高まった。ただし、
「原因はハンドル解決失敗かSteam設定の2つに絞られた」という表現は
時期尚早であり撤回する。**ゲーム内部（IL2CPP側）だけでもまだ複数の
未検証箇所が残っている**。

`UNRESOLVED`（候補、優先度順ではなくすべて並列に保持）:

1. `ActionSets[Current].Analog[0]`/`[1]`が実際に`IG_LSTICK`/
   `IG_RSTICK`のどちらに対応するか（登録順の確認が必要、未確認）。
2. `get_analogaction("IG_LSTICK")`/`get_analogaction("IG_RSTICK")`
   （24.1節）でSteamworksから取得した`Handle`が、それぞれ
   有効な値として保存されているか（未確認）。
3. `SteamPadSet(index)`内で`func_0x1828A0AF0`へ実際に渡される
   `analogActionHandle`の生成元・値そのもの（未確認）。
4. `SteamPadSet(index)`内で確認した「`ControllerType==10`による
   `+56`/`+64`フィールドの構造選択」（28.3節）が、`Analog[0]`/`[1]`の
   内容・構築経路にどう影響するか（未確認）。
5. `SteamPadSet(index)`自体の呼び出し元・呼び出し条件・タイミング
   （通常フレーム更新か、`ResetController()`/`SteamControllerReStart()`/
   `UpdateConnectedControllers()`に付随するものか）は未確認。
6. Guide/Overlay操作後に、ActionSet・Handleそのものが
   再構築・再取得され得る経路が存在するかは未確認。
7. `GetAnalogActionData`の戻り値に含まれる`bActive`を、
   `SteamPadSet`側が実際に判定・使用しているか、無視しているかは
   未確認（28.2節の`SetAnalog`引数に`bActive`が渡っているかどうかを
   含む）。

これらゲーム内部（IL2CPP側）だけで検証可能な項目をすべて確認し
尽くしてから、初めてSteamクライアント側のコントローラー設定確認
（次節）に進む、という順序をEvidence上の方針とする。

### 28.7 次の最優先候補（28.5/28.6の結果を受けて更新）

`func_0x1828A0AF0`がSteamworks `GetAnalogActionData`相当であることが
CONFIRMEDされ、かつそれ自体にL/R差がないことも判明したため、
残る調査は「渡される引数（ハンドル）」と「Steam側の設定」の
2方向に絞られる。

1. **（静的解析で継続可能）** `ActionSets[Current].Analog`リストの
   構築順（`get_analogaction()`の呼び出し元）を確認し、
   `Analog[0]`/`[1]`がLSTICK/RSTICKのどちらに対応するか、また
   その`Handle`が有効値かを確定する（28.6節HYPOTHESIS 1の検証）。
2. **（静的解析で継続可能）** `SteamPadSet(int index)`自体の
   呼び出し元を検索し、Guide/Overlay復帰・`ResetController()`との
   時間的関係を確認する。
3. **（ユーザー確認が必要、静的解析の範囲外）** Steamクライアントの
   コントローラー設定画面（Steam → 設定 → コントローラー →
   このゲーム専用の設定 / コントローラーレイアウトのプレビュー）を
   直接確認し、Right Stick（右アナログスティック）に対応する
   アクションが実際にバインドされているかを見る（28.6節
   HYPOTHESIS 2の検証）。特に、Guide長押し/Overlay操作の前後で
   この設定表示が変化するかも確認材料になり得る。
4. 並行して低コストで、`dds3PrivatePadAnalog → dds3PadAnalog`の
   コピー経路も引き続き探索する（優先度は上記より低い）。

## 29. Analog action handleの登録経路と登録タイミングを追跡（2026-09-07）

28.6節のUNRESOLVED項目のうち、1（`Analog[0]`/`[1]`とLSTICK/RSTICKの
対応）と5（`SteamPadSet`/登録処理の呼び出しタイミング）を中心に
静的解析を継続した。**静的解析のみ、patch/手動呼出しは行っていない。**

### 29.1 `Analog[0]`/`[1]`とLSTICK/RSTICKの対応 — HYPOTHESIS（構造的に強く支持、文字列内容は未確認）

`SteamPad.get_action(int index)`（RVA `0x2603DE0`）のISILを確認した。
`CONFIRMED`（命令列として）:

- 固定の文字列配列（コンパイル時定数、グローバル`0x182E6BC98`起点）を
  IL2CPPの配列初期化ヘルパー経由で取得し、**その配列の要素数分だけ
  ループして`get_analogaction()`を1要素につき1回呼ぶ**
  （`get_analogaction`のRVA `0x2604640`への呼び出しは、全コードベース中
  この1箇所のみ、`CONFIRMED`）。
- 同様に別の固定文字列配列（グローバル`0x182E6BC50`起点）に対して
  `get_digitalaction()`を1要素につき1回呼ぶループも存在する。

`HYPOTHESIS`（文字列リテラルの実内容はISILから直接読み取れないため
CONFIRMEDにはできない）: このAnalog名配列は、20.1節で確認済みの
`SteamPad.EAnalogActionsInGameControls { IG_LSTICK = 0, IG_RSTICK = 1 }`
と対応する`{"IG_LSTICK", "IG_RSTICK"}`という2要素配列であり、
ループはindex 0から順に実行されるため、**`ActionSets[Current].Analog[0]`
がIG_LSTICK、`Analog[1]`がIG_RSTICKに対応する可能性が高い**。
`get_analogaction()`自体は登録順を問わず同一処理を行う
（28.2節参照の前段、24.1節で確認済み）ため、登録順が非対称性の
直接原因になっている証拠はない。

### 29.2 `get_action(index)`の呼び出し元: `SteamPad.UpdateConnectedControllers()` — CONFIRMED（重要）

`get_action`のRVA（`0x2603DE0`）を全ISILダンプで横断検索した結果、
**呼び出し元は`SteamPad.UpdateConnectedControllers()`の1箇所のみ**
であることを確認した。

`CONFIRMED`（呼び出し文脈）: `get_action(this, index=rbx)`の`rbx`は、
`UpdateConnectedControllers()`内部のループ変数であり、複数の配列に対して
`.Length`との境界チェックを伴いながらインデックスとして使われている
（コントローラー接続スロットに対応するループと推測される、
`HYPOTHESIS`）。呼び出し直前には、新規/既存ハンドルの照合と思われる
一連の処理（`0x181678020`・`0x18163A2E0`呼び出し、ハンドル比較）が
存在する。呼び出し結果（`rax`）はその場でチェックされ、成功/失敗で
分岐する。

`HYPOTHESIS`（重要、既存Evidenceとの接続）: `UpdateConnectedControllers()`
は既存Evidence（21章）により**`SteamPadSet`ではなく`UpdateData()`から
毎フレーム呼ばれ続けることがCONFIRMED済み**である。したがって
`get_action()`（Analog/Digitalハンドル登録）も、`UpdateConnectedControllers()`
自身のゲート条件（`0x1825FF2C0(0)!=null && this+0x18!=0`）が満たされる限り、
**単発の初期登録ではなく、繰り返し（場合によっては毎フレームに近い
頻度で）再実行されている可能性がある**。この場合、「IG_RSTICKの
ハンドル登録が特定の瞬間にのみ失敗し、それが後続フレームでも
訂正されないまま固定化する」というシナリオと矛盾しない。

`UNRESOLVED`:
1. `get_action()`呼び出しが実際に「毎フレーム」なのか「新規接続検出時
   のみ」なのかは、直前の一連の照合ロジック（0x181678020等）の意味が
   未解析のため確定できない。
2. `get_action()`の戻り値（成功/失敗）が呼び出し元でどう扱われるか
   （失敗時にリトライするか、そのまま放置するか）は未確認。
3. `SteamPadSet(index)`自体の直接の呼び出し元はまだ検索していない
   （28.3節で発見した経路の起点、次の優先候補）。

### 29.3 次の優先候補（30章で更新・重大訂正あり）

## 30. 重大訂正: `0x1825F9E10` = `SteamInputUtil.UpdateInput()`本体。全経路が確定（2026-09-07）

### 30.1 訂正の経緯 — CONFIRMED

`SteamPadSet(int index)`（RVA `0x2602F30`）の全呼び出し元を全ISILダンプで
横断検索した結果、`SteamInputUtil.UpdateInput()`（`SteamInputUtil.txt`
171行目）から1回呼ばれていることを確認した。この`UpdateInput()`の
冒頭が、23.1節で解析した「`0x1825F9E10`の冒頭」（`param_1+0x24`の
repeatカウンターデクリメントパターン）と**完全に一致**していたため、
`SteamInputUtil::UpdateInput`のRVAを再確認したところ、**`0x25F9E10`
（＝VA `0x1825F9E10`）と完全一致することを確認した**。

**訂正（CONFIRMED）**: `0x1825F9E10`は正体不明のnative関数ではなく、
**`SteamInputUtil.UpdateInput()`そのものである。** 20章〜23章で
「native関数、C#メソッド定義なし」としていた記述は誤りであり、
以前の`findrva`検索（Assembly-CSharp / Assembly-CSharp-firstpassのみを
対象としたもの）が何らかの理由でこの一致を検出できなかったことに
起因する（原因未特定、ツール側の見落としと考えられる）。23章の
「デジタルボタンのpress/repeat処理」という結論自体（bit edge検出・
repeatカウンター構造）はISIL/Ghidra両方の実際の命令列と一致しており
命令列レベルでは正しいが、それは`UpdateInput()`という**具体的な
メソッド本体**の一部であったことが今回判明した。

### 30.2 完全な経路の確定 — CONFIRMED

`UpdateControl()`のif分岐から`AnalogStickLRval`書き込みまで、
以下の完全な経路がすべて命令列レベルでCONFIRMEDされた。

```
SteamPad.UpdateControl()
  if (条件A: 0x1825FF2C0(0)!=null
      && 条件B: this(SteamPad)+0x18 != 0
      && 条件C: 0x1825FC4B0(0)!=null) {

      steamInputUtilInstance = 条件Cの戻り値   ← SteamInputUtilシングルトン
      steamInputUtilInstance.UpdateInput()      ← (旧称"0x1825F9E10")

        repeatカウンター処理（デジタルボタン、既存23.1節の記述どおり）
        ↓
        controllerCount = 0x182602C40(controllerArray, 0)
        for (rdi = 0; rdi < controllerCount; rdi++) {
            handle = controllerArray[rdi]
            if (func_0x181519140(dict, handle, ...)) {  // Dictionary存在確認等
                SteamPadSet(rdi)     ← ★ここでAnalog更新が走る★
                    for (i = 0; i < 2; i++) {   // IG_LSTICK, IG_RSTICK
                        analogActionHandle = ActionSets[Current].Analog[i].Handle
                        {mode,x,y,bActive} = func_0x1828A0AF0(handle, analogActionHandle)
                            ↓ CONFIRMED: Steamworks GetAnalogActionData相当
                        SetAnalog(ref bitmask, rdi, i, x, y)
                            ↓
                        AnalogStickLRval[rdi][i] = クランプ済み値
                }
            }
        }
      return true;
  } else {
      // 全スロットのAnalogStickLRvalを128.0へリセット（27.3節）
  }
```

`CONFIRMED`: `SteamPadSet(index)`は`UpdateInput()`内のループから
「接続されている各コントローラーについて1回」呼ばれる。呼び出し自体は
LSTICK/RSTICKを区別しない（28章確認済み、`SteamPadSet`内部で
`i=0,1`両方を対称に処理）。

### 30.3 真のゲート条件の絞り込み — CONFIRMED（条件B）/ HYPOTHESIS（条件A・Cへの絞り込み）

`CONFIRMED`（22章のruntime probe結果より）: 条件B（`SteamPad+0x18`）は
起動直後に`1`へ変化した後、DEAD⇔LIVE境界を含め**一切変化しない**
（常時true）。したがって**条件Bは、UpdateControl()のif/else分岐を
フレームごとに切り替える実質的な要因ではあり得ない**（起動後は
恒常的にif分岐側の条件を満たしている）。

`UNRESOLVED`（訂正: 条件Cはruntime未確認）: 条件C
（`0x1825FC4B0(0)`、SteamInputUtilシングルトンの解決）は、
`SteamInputUtil`が永続的なMonoBehaviourである以上、ゲーム実行中は
常に成功すると考えるのが自然だが、これは推測に過ぎず、
22章のような直接のruntime観測はまだ行っていない。条件Bのように
「恒常的にtrue」と確定した扱いにはできない。

**判定の訂正**: 前回の報告で「実質的なゲートは条件Aに絞り込まれた」
としたのは時期尚早だった。正しくは
**「`UpdateControl()`のif/else分岐を切り替える候補として、
条件A（`0x1825FF2C0`）の優先度が相対的に上がった」**に留める。
理由は以下の2点:

1. 条件Cがruntime未確認であるため、条件A単独に絞り込む根拠が
   まだ不足している。
2. **たとえA/B/Cがすべてtrueで`UpdateInput()`に到達しても、その先に
   さらに複数の未解決なゲート候補が存在する**（30.2節の経路図
   参照）: コントローラー列挙、`func_0x181519140`による
   Dictionary/存在確認（`SteamPadSet`呼出し自体をスキップさせ得る）、
   `SteamPadSet`内の`ActionSet`/`analogActionHandle`選択、そして
   `GetAnalogActionData()`自体の戻り値（`bActive`/`x`/`y`）。
   したがって「DEAD状態＝A/B/Cのどれかがfalseでelse分岐に落ちている」
   と断定することもできず、「if分岐には入っているが、その先の
   どこかでRSTICKだけ実質的に無効化されている」という可能性も
   同等にUNRESOLVEDとして残る。

### 30.4 UNRESOLVED（最重要、更新）

1. 条件A（`0x1825FF2C0`）が具体的に何を判定しているか
   （最優先の逆アセンブル対象）。
2. 条件A・条件Cの戻り値が、DEAD時とLIVE時で実際に異なるか
   （静的解析では確認不能、runtime probeでの直接観測が必要）。
3. `UpdateInput()`内の`func_0x181519140`（Dictionary存在確認等と
   推測）が、特定のコントローラー/条件でfalseを返し、その
   コントローラーに対する`SteamPadSet`呼び出し自体をスキップさせる
   ケースがあるか。
4. `SteamPadSet`内の`ControllerType==10`による`+56`/`+64`選択
   （28.3節）が、`i=0,1`ループの`Analog`リスト自体の内容に
   影響するか。
5. **DEAD状態が「if/else分岐でelseに落ちている」ケースなのか、
   「if分岐（UpdateInput経由）まで毎フレーム到達しているが、
   RSTICKの`GetAnalogActionData()`だけが`bActive=false`/`x=y=0`を
   返している」ケースなのかが未分離。** これは静的解析だけでは
   区別できず、次段のruntime probeで直接観測するのが最も効率的。

### 30.5 条件A = `SteamManager.get_Initialized()` — CONFIRMED

`0x1825FF2C0`（条件A）と、`SteamManager`（`Il2Cpp.SteamManager`、
Steamworks.NET/Facepunch.Steamworks系の標準的なUnity統合クラス、
`base=UnityEngine.MonoBehaviour`）の`get_Initialized()`メソッドの
RVAを比較した結果、**`0x25FF2C0`（VA `0x1825FF2C0`）で完全一致**した。

**結論（CONFIRMED）**: `UpdateControl()`の条件A（`0x1825FF2C0(0)`）は
**`SteamManager.Initialized`（`SteamManager.get_Initialized()`）
そのものである。**

`CONFIRMED`（`get_Initialized()`の構造、`SteamManager.txt`84行目〜）:
`SteamManager`の静的フィールド（`_DAT_182e496a8+0xb8+0x08`、
宣言順から`s_instance`と推測）を読み、`func_0x1814C69B0`
（型チェック系ヘルパー、23.2節で条件Cにも使われていたのと同種の
パターン）でその妥当性を確認し、有効なインスタンスが得られれば
そのインスタンスの特定フィールドを読んで返す、という構造。
これは20.1節で確認済みの条件A自体の構造（`_DAT_182e496a8`起点、
型チェック→GetComponent的解決→`byte[+0x18]`を返す）と完全に
一致する（同一関数であることの独立した再確認）。

`HYPOTHESIS`（重要な留保）: 一般的なSteamworks.NET/Facepunch系
`SteamManager`実装では、`Initialized`は`Awake()`時に`SteamAPI.Init()`が
成功したかどうかを一度だけ記録する**セッション永続的なフラグ**である
ことが多く、条件Bと同様に**起動後は恒常的にtrueのまま変化しない
可能性がある**（未検証）。この場合、条件Aも実質的なDEAD/LIVE切替の
決定要因ではなく、30.3節で整理した「if分岐に入った後のさらに先の
ゲート」（`SteamPadSet`到達可否、`GetAnalogActionData`の`bActive`等）に
真因がある可能性が残る。**この判定はruntime観測なしには確定できない。**

### 30.6 次の最優先候補: read-only runtime probeの設計

静的解析だけでは条件A/Cの「DEAD⇔LIVE境界での実際の値」を確認できず、
かつif分岐内部（30.2節の経路）にも複数の未検証ゲートが残っているため、
次段はread-only runtime probeによる直接観測が最も効率的である。
**今回はまず静的解析の報告のみとし、実装（patch/manual call含む）は
行っていない。**

観測候補（22章の`Root26SteamPadOffset18Probe`と同じ手法、
`Marshal.Read*`によるnative offset直接読み取りを想定）:

1. 条件A（`SteamManager.Initialized`、パブリックプロパティなので
   本来は`Marshal`を介さず直接呼び出し可能）の値。
2. 条件C（`0x1825FC4B0`の戻り値=SteamInputUtilシングルトン解決成否）。
3. `SteamInputUtil.UpdateInput()`（旧`0x1825F9E10`）へ実際に
   到達しているか（call-count probe、既存`Root26SteamStateProbe`と
   同じ設計で追加可能）。
4. `SteamPad.SteamPadSet(int index)`へ実際に到達しているか
   （同上、call-count probe）。
5. `i=0`（LSTICK）・`i=1`（RSTICK）それぞれの
   `analogActionHandle`（`ActionSets[Current].Analog[i].Handle`）の値
   （`SteamPad.Controller`辞書から`InputInfo.ActionSets[Current].Analog`
   を辿ることで、既存のmanaged property経由で読み取り可能と見込まれる）。
6. **最重要**: `func_0x1828A0AF0`（`GetAnalogActionData`相当）の
   戻り値そのもの（`bActive`/`x`/`y`）を、`i=0`と`i=1`それぞれについて
   観測する。

`HYPOTHESIS`（ユーザー提示の判定基準）: もしDEAD状態で
「`UpdateInput()`/`SteamPadSet()`まで毎フレーム到達しているが、
RSTICK（`i=1`）の`GetAnalogActionData`だけが`bActive=false`/`x=y=0`を
返している」ことが観測できれば、原因はゲームコードの外側
（Steam Input側のアクション状態）にほぼ確定できる。逆に
「`UpdateInput()`自体に到達していない」ことが観測できれば、
条件A/Cのどちらかが真因であると絞り込める。この判定は
静的解析では不可能であり、read-only runtime probeでの直接観測が
必須である。

## 31. Phase 1 read-only runtime probe実装（2026-09-07）

### 31.1 条件Cの正体（追加の静的確認、CONFIRMED）

Phase 1実装に着手する前に、条件C（`0x1825FC4B0(0)`）の正体を
`MetaDump body`コマンドでRVA完全一致により確認した。

```
==== BODY SteamInputUtil::get_Instance() ====
  [ATTR] Cpp2ILInjected.AddressAttribute() RVA=0x25FC4B0, Length=0x297
```

**結論（CONFIRMED）**: 条件C = `SteamInputUtil.get_Instance()`
（`SteamInputUtil.instance`静的プロパティのgetter）そのものである。
つまり条件A（`SteamManager.Initialized`）・条件C（`SteamInputUtil.instance`
の解決成否）は両方とも既存probe（`Root26SteamStateProbe`等）が
既に使っている標準的なmanaged property/staticシングルトンアクセスと
完全に一致する。これによりPhase 1の観測項目1・2は
`SteamManager.Initialized`と`SteamInputUtil.instance != null`の
直接読み取りだけで実装でき、native detourは不要と確定した。

### 31.2 実装内容（`src/Root26Phase1Probe.cs`、`src/ModMain.cs`に登録）

ユーザー指定のPhase 1最小構成（6項目）をmanaged側read-only観測のみで
実装した。native `GetAnalogActionData` wrapper（`func_0x1828A0AF0`）への
detourは今回は行っていない。

- **`Root26Phase1UpdateInputCallProbe`**（Harmony Prefix、
  `SteamInputUtil.UpdateInput()`）: 到達回数カウンタのみ（毎回ログはしない）。
- **`Root26Phase1SteamPadSetCallProbe`**（Harmony Prefix、
  `SteamPad.SteamPadSet(int index)`）: 到達回数カウンタ（index別内訳含む）に加え、
  呼び出しごとに`pad.Controller`辞書を走査し、各controller handleについて
  `Current`（ActionSet）・`ControllerType`・`Analog[0].Handle`・
  `Analog[1].Handle`をスナップショットし、**前回スナップショットと差分がある
  ときのみ**ログする（change-only）。
- **`Root26Phase1SetAnalogProbe`**（Harmony Prefix、
  `SteamInputUtil.SetAnalog(ref ulong ret, int index, int i, float dx, float dy)`）:
  `ret`/引数への書き込みは一切行わない。`i=0`（`IG_LSTICK`）・`i=1`
  （`IG_RSTICK`）ごとに`NO-CALL`/`CALLED-ZERO`（`dx==0 && dy==0`）/
  `CALLED-ACTIVE`（それ以外）の3状態を保持し、状態遷移時のみ
  `index`/`dx`/`dy`とともにログする。`NO-CALL`への遷移は
  Harmony Prefixだけでは検出できない（「呼ばれなくなったこと」は
  ポーリングでしか分からない）ため、毎フレーム`Sample()`から
  500msタイムアウトで検出する。
- **`Root26Phase1ConditionProbe`**（`ModMain.OnUpdate()`から毎フレーム
  `Sample()`、`FieldDashPatch.IsExplorationActive`に非依存）: 条件A・条件Cを
  毎フレーム読み、変化時のみログ。加えて2秒間隔で
  `conditionA`/`conditionC`/`UpdateInputCalls`/`SteamPadSetCalls`
  （index別内訳）/`SetAnalogCalls(i0,i1)`の累積値をHEARTBEATとしてログし、
  何も状態遷移が起きていない期間でも「到達回数」の推移を確認できるようにした。

ログはすべて`[NocturneModernController][Root26Phase1]`タグ、
`DateTimeOffset.Now:O`形式のタイムスタンプで出力され、既存の
`[Root26NativePoll]`（`RightStickPollingProbe`のnative
`GetPadAnalog`観測）と直接付き合わせられる。

行動変更・手動API呼び出し・F9・入力値/handle/bufferへのwriteは一切なし。
Harmonyパッチはすべて観測用Prefixのみで、`__result`・`ref`引数・その他の
引数を変更していない。

### 31.3 判定フレームワーク（ユーザー指定）

DEAD状態のログが以下のどれに該当するかを実機テストで確認する。

- **A**: `conditionA=false`または`conditionC=false` → `UpdateInput`未到達
- **B**: `UpdateInput`到達 → `SteamPadSet`未到達
- **C**: `SteamPadSet`到達 → `i=0`は`CALLED-ACTIVE`、`i=1`は`CALLED-ZERO`
- **D**: `i=0`/`i=1`とも`CALLED-ACTIVE` → 後段のゲーム側変換/コピーが問題

Cが確認された場合のみ、Phase 2として`func_0x1828A0AF0`
（`GetAnalogActionData`相当）の戻り値`bActive`/`eMode`/`x`/`y`を
直接観測するnative detourの追加を検討する。

### 31.4 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   ac853595ae026d9c0b685d78394904120e6cdf14835c596babffc4b190831d10
SHA-256 (deployed): ac853595ae026d9c0b685d78394904120e6cdf14835c596babffc4b190831d10
  → 一致 (bin/Release/net6.0/NocturneModernController.dll ->
     Steam/steamapps/common/smt3hd/Mods/NocturneModernController.dll)
```

### 31.5 実機テスト手順（次のステップ、ユーザー実施待ち）

1. 通常起動 → Right Stickが`DEAD`であることを確認。
2. `MelonLoader/Latest.log`をアーカイブ（次回起動で上書きされるため）。
3. Guideボタンを1回押す → Steam Overlayを開く → ゲームに戻る。
4. Right Stickが`LIVE`になることを確認。
5. `Latest.log`を回収し、`[Root26Phase1]`タグのログ
   （特にDEAD区間とLIVE区間それぞれのHEARTBEAT・STATE-CHANGE）を
   `[Root26NativePoll]`のSTATE-TRANSITIONと突き合わせて、
   A/B/C/Dのどのシナリオに該当するかを判定する。

## 32. Phase 1 実機テスト結果（2026-09-07 22:00台、1回目）

ログ: `docs/research/ROOT26_POST_REBOOT_EVIDENCE_20260907/melonloader/Latest.log_phase1_20260907.log`

### 32.1 タイムライン（すべて同一ログから、タイムスタンプ相互照合済み）

| 時刻 | 出来事 |
|---|---|
| 22:00:47 | ゲーム起動（MelonLoader init） |
| 22:01:02.710 | `SteamControllerReStart()` count=1,2（初回コントローラ接続時） |
| 22:01:02.717〜 | `SteamPadSet(index=0)`到達開始。`Analog0=IG_LSTICK=1 Analog1=IG_RSTICK=2`（handle非ゼロで安定） |
| 22:01:08.116〜17.093 | exploration session 1。`Root26NativePoll`が**RightStick X/Y = DEAD(128-fixed)固定、count=1064、transitions=0**で終了（`SESSION-SUMMARY`） |
| 22:01:14.286〜15.154 | この間、`SetAnalog i=0(IG_LSTICK)`は複数回`CalledActive`（dx/dy実値）⇔`CalledZero`を遷移＝**LSTICKは正常に生値を受け取っている** |
| （session 1全体） | `SetAnalog i=1(IG_RSTICK)`は22:01:02.72の初回`CalledZero`から**一度もSTATE-CHANGEログが出ない**＝dx=dy=0のまま**約14秒間変化なし** |
| 22:01:17.093 | `Root26FocusProbe` `MONITOR-END`・`Root26NativePoll` `SESSION-SUMMARY`（exploration終了、finalState=DEAD） |
| **22:01:17.226** | **`SteamInputUtil.ResetController()` count=1,2 と `SteamPad.SteamControllerReStart()` count=3,4 が呼ばれる**（本Modからの呼び出しではない - F9/`ManualResetControllerPoc`は`ModMain.OnUpdate()`から既に除去済みで一度も発火していない） |
| 22:01:17.234〜 | exploration session 2 開始。`Root26NativePoll`再度`DEAD`から観測開始 |
| **22:01:18.072** | **`SetAnalog i=1(IG_RSTICK)`が session内で初めて`CalledActive`（dx=-0.247, dy=-0.040）** |
| 22:01:18.489 | `Root26NativePoll` `STATE-TRANSITION channel=X/Y DEAD -> LIVE` （native `GetPadAnalog`側で確認） |
| 22:01:19〜26 | `SetAnalog i=1(IG_RSTICK)`が継続的に`CalledActive`⇔`CalledZero`を遷移（実際のスティック操作を反映） |
| 22:01:26.737 | `SESSION-SUMMARY`：`finalState=LIVE(analog-active) transitions=1` |

全期間を通じて`conditionA=True`・`conditionC=True`・`UpdateInputCalls`/`SteamPadSetCalls`/`SetAnalogCalls(i0,i1)`は完全に同期して単調増加（DEAD区間でもLIVE区間でも`UpdateInput`・`SteamPadSet`・`SetAnalog(i=0,1とも)`は毎フレーム欠かさず到達）。

### 32.2 判定: Scenario C — CONFIRMED

30章で立てたA/B/C/D判定基準に対する結果:

- A（`conditionA`/`conditionC`未到達）: REJECTED。両方とも起動直後からDEAD区間・LIVE区間を通じて常に`True`。
- B（`UpdateInput`到達→`SteamPadSet`未到達）: REJECTED。`SteamPadSetCalls`は`UpdateInputCalls`と常に同数で、毎フレーム到達している。
- **C（`SteamPadSet`到達→i=0はactive、i=1はdx=0/dy=0）: CONFIRMED。** DEAD区間で`SetAnalog`はi=0/i=1とも毎フレーム呼ばれているが、i=0（LSTICK）は実際のスティック入力に応じて`CalledActive`になるのに対し、i=1（RSTICK）はDEAD区間中一度も`CalledActive`にならず、`GetAnalogActionData`相当の呼び出し結果が常にゼロを返し続けていたことがmanaged側から直接確認できた。
- D（両方ともactive）: REJECTED（i=1がDEAD区間中終始ゼロだったため）。

これにより、**Right Stick DEAD問題の実質的な発生箇所は
`SteamPadSet`内部の`i=1`（RSTICK）に対応する
`func_0x1828A0AF0`（`GetAnalogActionData`相当）の呼び出し結果側にある**
ことが、静的解析の帰結（26〜30章）と矛盾なく、runtime観測で
直接裏付けられた（CONFIRMED、native detourなしでもここまで絞り込めた）。

### 32.3 新知見（重要、CONFIRMED）: `ResetController()`/`SteamControllerReStart()`呼び出しとDEAD→LIVE境界の直接一致

今回のテストで**予期していなかった追加の観測事実**が得られた。
`ResetController()`/`SteamControllerReStart()`は本Mod側からは
一度も呼んでいない（F9=`ManualResetControllerPoc`は
`ModMain.OnUpdate()`から既に除去済み。ログ全体を検索しても
`ManualResetControllerPoc`関連の文字列は一切出現しない）。
にもかかわらず、DEAD→LIVE境界の**わずか846ms前**
（native側のSTATE-TRANSITION確認からは1263ms前）に、
この2メソッドがゲーム自身の内部コードによって呼び出されている。

`CONFIRMED`: `ResetController()`/`SteamControllerReStart()`は
本Mod以外の呼び出し元（＝ゲームのnativeコード、Harmonyパッチの
Prefixで観測しているため呼び出し元を問わず捕捉できる）から
実際に呼ばれた。

`HYPOTHESIS`（時間的一致に基づく、未確定）: この呼び出しは
`Root26FocusProbe`が同時刻（22:01:17.093）に記録した
`MONITOR-END (exploration ended)`、すなわち
`FieldDashPatch.IsExplorationActive`が`false`に落ちたタイミングと
一致しており、これがユーザーのGuideボタン押下→Steam Overlay操作
（フォーカス喪失/復帰、またはUnityの`OnApplicationFocus`/一時停止相当の
挙動を誘発）によって引き起こされた可能性が高い。ただし
`Root26FocusProbe`は`WNDPROC-OBSERVER`のinstall/restoreログしか
出しておらず、個々のWM_メッセージ（フォーカス喪失/復帰そのもの）は
今回のログには記録されていないため、「exploration終了の直接の原因が
Guide/Overlayである」ことも「`ResetController`/`SteamControllerReStart`が
exploration終了イベントから直接呼ばれている」ことも、
**まだ未確認（UNRESOLVED）**。単発の時間的相関のみである
（session 1開始からsession 1終了までの約9秒間、この2メソッドは
一度も呼ばれていない＝ルーチン処理ではなく、明らかに単発の
特殊イベントに紐づいている、という点は確認できる）。

`HYPOTHESIS`（因果関係の方向性、未確定）: この時間的近接性
（呼び出し→846ms後にRSTICKが生値を返し始める）は、
`ResetController()`/`SteamControllerReStart()`の実行が
RSTICKのSteam Inputアクションハンドル解決状態を
再初期化し、それが功を奏してDEAD→LIVEに切り替わった、
という因果関係を強く示唆する。ただし今回は1回のサンプルのみであり、
単なる時間的相関を因果と断定することはできない
（例えば、両方とも「exploration再開」という共通の上流イベントに
それぞれ独立に反応しているだけで、`ResetController`自体は
RSTICK復活に無関係という可能性も排除できていない）。

### 32.4 次の検討候補（UNRESOLVED、ユーザー判断待ち）

- `ResetController()`/`SteamControllerReStart()`の**呼び出し元
  （コールスタック）**を特定する（今のHarmony Prefixは呼び出しの
  発生時刻・回数のみを記録し、呼び出し元until未特定）。
  スタックトレース取得はread-only観測の範囲内で可能
  （`Environment.StackTrace`等、副作用なし）。
- `Root26FocusProbe`の観測範囲を広げ、`WM_ACTIVATE`/`WM_KILLFOCUS`/
  `WM_SETFOCUS`等の個別メッセージも記録し、Guide/Overlayに起因する
  フォーカスイベントと`ResetController`呼び出しの前後関係を
  より直接的に確認する。
- 上記の因果仮説が正しければ、`ResetController()`/
  `SteamControllerReStart()`が「なぜ効くのか」（RSTICKの
  action handle再解決か、controller handle再列挙か等）を
  30.2節の未解決ゲート一覧と突き合わせて特定する。
- Phase 2（native `func_0x1828A0AF0`の`bActive`/`eMode`/`x`/`y`直接観測）は
  32.2節でScenario Cが確認されたため候補として有効だが、
  32.3節の新知見（`ResetController`呼び出し元特定）の方が
  優先度が高い可能性がある - ユーザー判断を仰ぐ。

## 33. `ResetController()`/`SteamControllerReStart()`呼び出し元の特定（2026-09-07、静的解析）

ユーザー指示により、32.3節の呼び出し元特定を最優先で実施した。

### 33.1 静的解析4種、すべて0件（CONFIRMED、重要な否定的結果）

以下4つの独立した手法で、`ResetController()`（VA `0x1825F9340`）・
`SteamControllerReStart()`（VA `0x182602EB0`）への静的な参照を
網羅的に検索したが、**いずれも0件**だった。

1. **IL命令レベル検索**（Mono.Cecil、`call`/`callvirt`/`ldftn`/`ldvirtftn`、
   全42アセンブリ横断）: 0件。ただしこれ単体では弱い根拠 -
   IL2CPP AOTメソッド本体は`ldloca/initobj/ldloc/ret`という
   自明なILスタブしか持たず（本物のロジックはnative側にのみ存在）、
   この点は本調査全体を通じて繰り返し確認済みの事実である。
2. **既知managedメソッド全件（32343件中、実body長のある31692件、
   全42アセンブリ）のnative E8（near CALL rel32）バイトスキャン**:
   0件。Cecilの`AddressAttribute`からRVA+Lengthを取得した
   全メソッドの実際のnativeバイト列を対象に、ターゲットVAへの
   直接call命令を検索。
3. **GameAssembly.dll実行可能領域全体（374MB、
   `.impdata`/`.text`/`.sbss`/`.idata`/`.trace`/`.didata`/`.arch`）の
   E8バイトスキャン**: 0件。
4. **GameAssembly.dllの初期化済み全メモリ領域（387MB、
   `.rdata`/`.data1`/`.debug`/`.edata`等の非実行セクション含む）の
   生8バイト絶対ポインタ値スキャン**: 0件。固定の関数ポインタ
   テーブルエントリ（デリゲート/イベント用に静的に埋め込まれた値）
   としてのパターンも見つからなかった。

**結論（CONFIRMED、範囲を限定）**: **今回実施した上記4種類の
探索方法では、両メソッドへの静的参照を検出できなかった。**
これは「バイナリ上のどこにも存在しない」ことの証明ではない
点に注意が必要である。x64ネイティブコードには`E8 rel32`以外にも
レジスタ間接call（`CALL reg`/`CALL [mem]`）、アドレスを実行時に
演算して生成する経路、IL2CPP runtime metadata/invoker経由の
呼び出しなど、今回の「固定バイトパターン検索」では原理的に
検出できない経路が存在する。したがって「GameAssembly.dll全体に
参照が存在しない」とまでは主張できず、あくまで今回の4手法が
無力だったという否定的結果にとどまる。

**解釈（HYPOTHESIS）**: IL2CPPの実行時メソッドポインタ解決機構
（デリゲート・`UnityEvent`・Steamworksコールバックディスパッチ等）を
経由して呼ばれている可能性がある。これらは典型的に、モジュール
初期化時に`LEA reg,[rip+disp32]`でアドレスを計算してテーブルに
格納する（固定定数としてではなく実行時に構築される）ため、
本調査で用いた「固定バイトパターン検索」では原理的に検出しづらい。

### 33.2 有力候補: `dds3DefaultMain.OnApplicationFocus(System.Boolean)`

`findmethodname`検索（全型のメソッド名を`OnApplicationFocus`/
`OnApplicationPause`で照合）により、Assembly-CSharp.dll内に
以下2件が見つかった:

- `dds3DefaultMain::OnApplicationFocus(Boolean)`（RVA `0x22D2920`,
  Length `0x376`=886バイト）
- `Sega.PrismStatus::OnApplicationFocus(Boolean)`（RVA `0x26C59B0`,
  Length `0x76`=118バイト）

`OnApplicationPause`は該当メソッドが1件も見つからなかった
（`CONFIRMED`、この名前のメソッドは存在しない）。

`dds3DefaultMain`は本調査を通じて確認してきたゲーム内部命名規則
（`dds3PadManager`/`dds3PadUpdate`/`dds3PrivatePadAnalog`等）と
一致するプレフィックスを持ち、ゲームのメイン/アプリケーション制御
クラスである可能性が高い。33.1節の全件スキャンには当然この
メソッドも含まれていた（31692件に含まれる）ため、
`dds3DefaultMain.OnApplicationFocus`自体が`ResetController`/
`SteamControllerReStart`を**直接**呼んでいないことは`CONFIRMED`
（0件スキャン結果に既に含まれる）。

Ghidraで`dds3DefaultMain.OnApplicationFocus(bool)`
（VA `0x1822D2920`）を直接逆コンパイルした結果、以下の構造が
確認できた（`CONFIRMED`、instruction-level）:

- 複数の`func_0x1800e6XXX`系ヘルパー呼び出し
  （IL2CPP実行時ヘルパー: 型チェック・GetComponent的解決・
  例外送出パス`swi(3)`など、本調査で既知の定型パターン）。
- 複数の静的フィールド（`_DAT_182e33a28`/`_DAT_182e4df30`/
  `_DAT_182e7b708`等）から値を読み、`plVar2[N]`という配列へ
  順に格納していく処理 - **引数配列（`object[]`likeなboxed引数列）を
  組み立てているパターン**に一致する。
- 最後に`func_0x000181494460(plVar2, 0)`で何かを解決し、
  `func_0x000180024980(_DAT_182e79aa8)`で別の値を取得した後、
  `func_0x0001810d6760(uVar5, uVar6, 0)`という呼び出しで
  実際の「invoke」を行っている。

`HYPOTHESIS`: この一連の構造（引数配列の組み立て→invoke呼び出し）は、
`UnityEngine.Events.UnityEvent`（またはそれに類する汎用delegate/
イベント）の**persistent listener呼び出しパターン**と構造的に
一致する。もしそうであれば、実際の呼び出し先（`ResetController`/
`SteamControllerReStart`を含むか否か）はUnityエディタ上で
Inspectorに配線されたリスナー（シーン/プレハブのシリアライズデータ内）
によって決まり、**GameAssembly.dll単体の静的解析（IL・native
いずれも）では原理的に確認できない**（33.1節の0件という結果と
矛盾なく整合する）。これは`OnApplicationFocus`がGuide/Overlayによる
フォーカス変化を契機に何らかの汎用通知を発行している可能性を
示すに留まり、**その通知の受信者が`ResetController`/
`SteamControllerReStart`であるという確証はまだない（UNRESOLVED）**。

`OnApplicationPause`が存在しないことから、もしフォーカス関連の
契機が真因であるなら`OnApplicationFocus`側である可能性が高い
（`HYPOTHESIS`）。

### 33.3 実装: runtime call-stack観測プローブ（read-only）

静的解析でこれ以上呼び出し元を追い切れないため、ユーザー指定の
項目4（`Environment.StackTrace`等によるread-only呼び出しスタック記録）
を実装した。`src/Root26CallerDiagnosticsProbe.cs`を新規作成し、
`src/SteamControllerReacquisitionProbe.cs`の既存2プローブ
（`Root26ResetControllerCallProbe`/`Root26SteamControllerReStartCallProbe`）
のPrefixから呼び出す。

- `System.Diagnostics.StackTrace(true)`（標準.NET API、read-only、
  副作用なし）でmanaged呼び出しスタックを取得し、改行を` | `に
  置換して1行のログにフラット化する。
- 同時にユーザー指定のスナップショット（`SteamManager.Initialized`
  =条件A、`SteamInputUtil.instance`有無=条件C、`Controller.Count`、
  各controllerの`handle`/`Current`/`ControllerType`/`Analog[0].Handle`/
  `Analog[1].Handle`）を`Root26Phase1SteamPadSetCallProbe`の
  既存ヘルパー（`ResolveAnalogHandle`、可視性を`internal`に変更して
  共有）を再利用して構築する。
- ログタグは`[Root26CallerDiag]`、フォーマットは
  `CALLER-CONTEXT <method> count=<N> snapshot=(...) stack=[...] at <timestamp>`。

もしIL2CPPの実行時invoker経由（33.2節のUnityEvent仮説）であっても、
.NETの`StackTrace`はその瞬間の実際のmanaged呼び出しスタックを
歩くため、静的解析で追えなかった呼び出し元がここで直接判明する
可能性が高い（呼び出し元が本当に純粋なnativeコード/Steamworks SDK
内部から直接来ている場合は、このProbe自身のフレームのみが
表示されると見込まれ、それ自体が有益な否定的所見となる）。

### 33.4 32.1節データの再確認（既存Phase 1ログからの追加所見、CONFIRMED）

新しいテストを待たずとも、32.1節で既に取得済みのログから
重要な事実が確認できる: DEAD区間開始（22:01:02.717の初回
`SteamPadSet`スナップショット、`Analog1=IG_RSTICK=2`）から
ログ終了（22:01:28.769、LIVE確認後も継続）まで、
`STATE-CHANGE SteamPadSet(index=0) snapshot`ログは
**一度も変化していない**（全ログ中1回のみ出現）。

**結論（CONFIRMED）**: 今回のテストにおいて、`Controller`の
`handle`・`Current`（ActionSet）・`ControllerType`・
`Analog[0].Handle`・`Analog[1].Handle`は、DEAD区間・
`ResetController`/`SteamControllerReStart`呼び出し・LIVE区間の
全体を通じて**一切変化しなかった**（`Analog1=IG_RSTICK=2`のまま）。

これはユーザーが提示した2つの仮説のうち、
「同じaction handleに対してSteam Input側の状態が有効化された」
（handle再取得ではない）という仮説を支持する方向の結果である
（`HYPOTHESIS`、1回のサンプルのみのため確定ではない）。
次回テストでも同じ結果が再現するかを確認する必要がある。

### 33.5 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   524cb4e642f762f71dfa536352c4858dcc05df62a9a6fb80e6149e8b1658619e
SHA-256 (deployed): 524cb4e642f762f71dfa536352c4858dcc05df62a9a6fb80e6149e8b1658619e
  → 一致
```

行動変更・手動API呼び出し・F9・入力値/handle/bufferへのwriteは
一切なし。`ResetController`/`SteamControllerReStart`は本Modから
一度も呼んでいない（既存2プローブのPrefixは観測のみで、
`__result`や引数を変更していない）。

### 33.6 次の実機テスト（次のステップ、ユーザー実施待ち）

前回（32章）と同じ手順で実施:

1. 通常起動 → Right Stick DEAD確認。
2. `MelonLoader/Latest.log`をアーカイブ。
3. Guideボタン1回押し → Steam Overlay → ゲームに戻る。
4. Right Stick LIVE確認。
5. `Latest.log`を回収し、`[Root26CallerDiag]`タグの
   `CALLER-CONTEXT`ログ（`ResetController`/`SteamControllerReStart`
   呼び出し時のmanaged stack trace + snapshot）を確認する。
   特に`dds3DefaultMain`や`OnApplicationFocus`、あるいは
   `UnityEvent`関連のフレームがスタックトレースに含まれるかを
   最優先で確認する。

## 34. Caller-diagnostics 実機テスト結果（2026-09-07 22:39台）

ログ: `docs/research/ROOT26_POST_REBOOT_EVIDENCE_20260907/melonloader/Latest.log_callerdiag_20260907.log`
（今回はログをアーカイブせず、DEAD→Guide/Overlay→LIVEを同一ログに連続記録）

### 34.1 タイムライン

| 時刻 | 出来事 |
|---|---|
| 22:39:00.216 | `SteamControllerReStart()` count=1,2（初回コントローラ接続時） - stack trace **完全に追跡可能**（後述34.2） |
| 22:39:05.938〜09.937 | exploration session 1、`Root26NativePoll`が`DEAD(128-fixed)`固定で終了 |
| 22:39:09.935 | `Root26FocusProbe` `MONITOR-END`（exploration終了） |
| **22:39:10.804〜.806** | **`ResetController()` count=1,2 → `SteamControllerReStart()` count=3,4** が連鎖して発生 |
| 22:39:10.832〜 | exploration session 2 開始 |
| 22:39:11.567 | `SetAnalog i=1(IG_RSTICK)`が初めて`CalledActive`（Reset呼び出しから約763ms後） |
| 22:39:11.831 / 12.074 | `Root26NativePoll` `STATE-TRANSITION DEAD -> LIVE`（X/Y、native側で確認、Resetから約1.0〜1.3秒後） |
| 22:39:16.436 | `SESSION-SUMMARY`：`finalState=LIVE(analog-active) transitions=1` |

32章のテスト（別セッション）と同一パターンが**再現**した
（`CONFIRMED`、n=2）: exploration終了直後に`ResetController`/
`SteamControllerReStart`が発生し、その約0.75〜1.3秒後にRSTICKが
生値を返し始める。

### 34.2 CALLER-CONTEXTログの解析（最重要）

#### (a) 初回`SteamControllerReStart()`（count=1,2、22:39:00.216、コントローラ接続時）: stackが完全に追跡できた

```
Root26CallerDiagnostics.LogCallerContext
Root26SteamControllerReStartCallProbe.Prefix
DMD<Il2Cpp.SteamPad::SteamControllerReStart>
(il2cpp -> managed) SteamControllerReStart
il2cpp_runtime_invoke ×2
DMD<Il2Cpp.dds3KernelMain::m_dds3KernelMainLoop>
(il2cpp -> managed) m_dds3KernelMainLoop
il2cpp_runtime_invoke ×2
Il2Cpp.dds3KernelMain.m_dds3KernelMainLoop()
Nocturne_Graphics_Configurator.NocturneGraphicsConfigurator.OnFixedUpdate()   ← サードパーティ「Nocturne Framerate Mod」
MelonLoader.MelonEventBase`1.Invoke(...)
MelonLoader.SupportModule_From.FixedUpdate()
MelonLoader.Support.SM_Component.FixedUpdate()
Trampoline_VoidThis / Invoker_VoidThis（IL2CPPネイティブ↔managed境界）
```

`CONFIRMED`: **`dds3KernelMain.m_dds3KernelMainLoop()`**
（ゲーム本体のnativeメインループ、`Il2Cpp.dds3KernelMain`クラス）が
`SteamPad.SteamControllerReStart()`を呼んでいる経路が実際に存在する。
また、このイベントは**サードパーティMod「Nocturne Framerate Mod」
（`Nocturne_Graphics_Configurator.NocturneGraphicsConfigurator.
OnFixedUpdate()`）が独自に`m_dds3KernelMainLoop()`を追加実行**して
いることで発生している（本調査の18章付近で既出の、このModが
`talkUI`/`talkChoice`にパッチを当てていた件と同様、フレームレート
制御のために本来のUnity FixedUpdateとは別にゲームのカーネル
ループを手動で追加実行している構造だと考えられる、`HYPOTHESIS`）。

ただし、これは**初回コントローラ接続時のイベント**であり、
Guide/Overlayによる復活イベントとは別物である（22:39:00、
exploration開始前）。

#### (b) Guide/Overlay復活イベント（22:39:10.804〜.806）: `ResetController()`のstackは境界で途切れる

```
ResetController() count=1 のstack:
  Root26CallerDiagnostics.LogCallerContext
  Root26ResetControllerCallProbe.Prefix
  DMD<Il2Cpp.SteamInputUtil::ResetController>
  (il2cpp -> managed) ResetController(IntPtr, MethodInfo*)
  ← ここで途切れる。これ以上上流のフレームが一切ない。
```

`CONFIRMED`（重要な否定的所見）: `ResetController()`の直接の
呼び出し元は、.NETの`StackTrace`からは一切見えない。
これは(a)の`SteamControllerReStart`（初回接続時）のケースで
`il2cpp_runtime_invoke`を介した完全なmanagedフレーム連鎖が
はっきり見えたのとは対照的である。つまりこのGuide/Overlay
復活イベントでの`ResetController()`呼び出しは、MelonLoaderの
`FixedUpdate`プロキシ経由でも、`il2cpp_runtime_invoke`ヘルパー
（`Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke`）経由でも
ない、**別の経路**から来ている。

`HYPOTHESIS`: Unityエンジン自体のネイティブ内部コード
（組み込みの`FixedUpdate`/`Update`ディスパッチや、Steam Input/
フォーカス変化に関連する内部処理）が直接IL2CPPのnative→managed
トランポリンを呼んでいる、あるいは`dds3KernelMain.
m_dds3KernelMainLoop()`が**サードパーティModを介さない通常の
Unityエンジン駆動で実行された際**に内部でこの呼び出しを行って
いる可能性がある（後者であれば(a)と同じ`m_dds3KernelMainLoop`
起点の可能性があるが、今回はMelonLoaderの`FixedUpdate`プロキシを
経由していないため、そのフレームが記録に残らない）。
`UNRESOLVED`のまま。

#### (c) `SteamControllerReStart()`（count=3,4、22:39:10.806）: `ResetController()`内部から呼ばれていることが判明

```
Root26CallerDiagnostics.LogCallerContext
Root26SteamControllerReStartCallProbe.Prefix
DMD<Il2Cpp.SteamPad::SteamControllerReStart>
(il2cpp -> managed) SteamControllerReStart
il2cpp_runtime_invoke ×2
DMD<Il2Cpp.SteamInputUtil::ResetController>
(il2cpp -> managed) ResetController(IntPtr, MethodInfo*)
```

`CONFIRMED`（範囲を限定）: **今回観測したGuide/Overlay起点の
`ResetController()`呼び出しでは、内部から`SteamPad.
SteamControllerReStart()`が`il2cpp_runtime_invoke`（IL2CPPの
汎用ランタイム呼び出し機構）経由で連鎖して呼ばれた。**
これは33.1節の静的スキャン（`ResetController`→
`SteamControllerReStart`間の直接E8呼び出しを探索して0件だった）
が偽陰性ではなく正しい結果だったことと矛盾しない - 呼び出しは
実在するが、直接callではなく実行時解決経由だったため検出
できなかった、という解釈は妥当である。

ただし「`ResetController()`が呼ばれれば**必ず**
`SteamControllerReStart()`が呼ばれる」（無条件call）とまでは
今回のruntimeサンプル（n=2、いずれもGuide/Overlay起点）だけでは
言えない - `ResetController()`のnative body内でこの呼び出しが
条件分岐の外にあるか（無条件）、何らかの条件下でのみ実行される
分岐の中にあるかは、native decompileで確認するまで`UNRESOLVED`
のままとする。34.4節の次ステップで検証する。

### 34.3 Controller状態の安定性（再現確認、CONFIRMED）

今回のテストでも、全4回の`CALLER-CONTEXT`ログ
（22:39:00.216〜22:39:10.806）を通じて
`handle=19680159496504676 Current=0
ControllerType=k_ESteamInputType_Unknown Analog0=IG_LSTICK=1
Analog1=IG_RSTICK=2`は**一切変化していない**。33.4節で確認した
「handleは変化しない」という所見が2回目のサンプルでも再現した
（`CONFIRMED`、n=2）。ユーザーが整理した以下のモデルが、
現時点で最も裏付けの強い仮説である:

```text
同じ Controller handle / 同じ Current ActionSet /
同じ ControllerType / 同じ IG_RSTICK handle=2
        ↓
DEAD: GetAnalogActionData(RSTICK) → x=0, y=0
        ↓ Guide / Overlay
Steam Input内部で何らかの状態変化（ResetController/ReStartも発生）
        ↓ (約0.75〜1.3秒)
LIVE: GetAnalogActionData(RSTICK) → 実値
```

「RSTICKのhandleが間違っている/再取得される」という仮説は
2サンプルとも否定的な結果（handle不変）であり、後退した。

`重要な留保`: 過去の実機テストでは、**手動で`ResetController()`
単独を実行してもRight StickはDEADのままだった**（ユーザー提供の
既知結果）。したがって、今回n=2で確認された「Guide/Overlay →
ResetController → SteamControllerReStart → 約0.75〜1.3秒後LIVE」
という時間的相関だけから、`ResetController()`自体を復活の
十分条件と判定することはできない。**「Guide/Overlayが Steam Input
内部状態を変化させ、その過程で`ResetController`/`ReStart`も
（副産物として、あるいは並行して）発生している」という可能性を、
「`ResetController`の実行自体が復活の原因である」という可能性と
同等以上に残す**（`HYPOTHESIS`、いずれもまだ確定ではない）。

### 34.4 未解決点・次の候補

- `ResetController()`本体（Guide/Overlay起点）の直接の呼び出し元は
  まだ未特定（`UNRESOLVED`）。候補: Unityエンジンのnative内部
  ディスパッチ、または`dds3KernelMain.m_dds3KernelMainLoop()`が
  サードパーティMod非経由の通常実行時に内部で呼んでいる経路。
  `m_dds3KernelMainLoop`をGhidraで直接decompileすれば、
  `ResetController`への`il2cpp_runtime_invoke`的な間接呼び出し
  （token/MethodInfo経由）が内部に存在するかを確認できる可能性が
  ある。
- `ResetController()`が内部で`SteamControllerReStart()`以外に
  何を行っているか（Steam Input側の状態を実際にどう変更するのか）
  は、`ResetController()`自体のnative decompileでまだ未確認。
  34.3節の仮説（Steam Input側の内部状態変化）を検証する上で
  次の有力候補。
- Phase 2（native `GetAnalogActionData`の`bActive`/`eMode`/`x`/`y`
  直接観測）も引き続き有効な候補。

## 35. `ResetController()`本体のnative decompile（2026-09-07、静的解析）

ユーザー優先順位（② `ResetController`本体 → ③ Phase 2 →
① `m_dds3KernelMainLoop`のcaller探索）に従い、
`SteamInputUtil.ResetController()`（RVA `0x25F9340`,
Cecil報告Length `0x96`=150バイト）をGhidraで直接decompileした。

### 35.1 手法上の注意（重要、今後も適用すべき教訓）

`ResetController()`を素朴に`CreateFunctionCmd(addr)`
（flow-following自動境界検出、`-noanalysis`環境）で解析すると、
関数サイズが**2170バイト**と誤検出され、実際には無関係な
後続コード（`SteamPad.get_action`等の領域）まで1つの関数として
decompileされてしまうことが判明した。Cecilの`AddressAttribute`が
報告する正確なLength（150バイト）と全く一致しなかったため、
この誤検出に気づくことができた。

対策として、Ghidraの`CreateFunctionCmd(name, entry, AddressSet, sourceType)`
を使い、Cecilが報告する`[RVA, RVA+Length)`を明示的な
`AddressSet`として関数本体に指定する手法に切り替えた。これにより
関数オブジェクト自体は正しく150バイトに収まったが、**Ghidraの
decompilerはFunction Objectの境界を無視し、実際の命令列の
制御フロー（JMP等）に従って後続コードの逆コンパイルを継続する**
ことも判明した（tail-jmp = 別関数への末尾ジャンプの場合、
decompilerの出力にはその先の関数のロジックまで表示される -
これは誤りではなく、tail-jmp先が実際に実行される以上、
正しい継続的な逆コンパイルである）。したがって「生の逆
アセンブリをCecilのLengthちょうどの範囲だけ確認する」ことと
「decompile結果はtail-jmp先まで含めて解釈する」ことを
組み合わせる必要がある。

### 35.2 `ResetController()`の正確な本体（150バイト、CONFIRMED）

```
1825f9340: (lazy static初期化の定型処理、業務ロジックと無関係)
1825f9368: MOV RCX,[0x182e79aa8]
1825f936f: CALL 0x180024980            ; 値を解決 -> RDI
1825f9374〜9390: (プロファイラ/ログ的マーカー呼び出しの定型処理
                   - "if ((flags&2)!=0 && counter==0) func_0x180079560()"
                   というイディオムは本調査で複数の無関係な関数
                   （本節、`dds3DefaultMain.OnApplicationFocus`、
                   `SteamControllerReStart`自身)に共通して出現して
                   おり、業務ロジックではなく汎用的な計測/ログの
                   定型コードだと考えられる、`HYPOTHESIS`)
1825f9395: MOV RCX,[0x182e5c940]
1825f93a2: CALL 0x1810d6760(RCX=static値, RDX=RDI, R8=0)
                                         ; 汎用invoke的呼び出し
                                         ; (OnApplicationFocusの末尾
                                         ; 呼び出しと同一パターン)
1825f93a7: MOV RCX,[RBX+0x50]           ; RBX=this(SteamInputUtil)
                                         ; this->field_0x50 = steam_pad
                                         ; フィールドの可能性が高い
1825f93ae: (nullなら NullReferenceException throw)
1825f93b2: CALL 0x182602eb0(RCX=this->field_0x50, RDX=0)
                                         ; = SteamPad.SteamControllerReStart()
1825f93b7: MOV RCX,[RBX+0x50]           ; 同じフィールドを再読込
1825f93be: (nullなら NullReferenceException throw)
1825f93cc: JMP 0x182603240(RCX=this->field_0x50, RDX=0)
                                         ; tail-jump
                                         ; = SteamPad.UpdateConnectedControllers()
```

`CONFIRMED`: `ResetController()`本体は、以下の2つの呼び出し以外
**一切の業務ロジックを含まない**（Controller dictionary/ActionSet/
Action handleへの直接の書き込みは、この150バイトの中には存在しない）。

1. `this.steam_pad.SteamControllerReStart()` を**無条件**に呼ぶ
   （data-dependentな分岐は存在しない。存在するのはnull安全性
   チェックのみで、`steam_pad`が非nullである限り必ず実行される）。
2. `this.steam_pad.UpdateConnectedControllers()` を**無条件**に
   tail-callする（同上）。

これにより、33.2節で仮の表現にとどめていた
「`ResetController()`が呼ばれれば`SteamControllerReStart()`も
呼ばれる」は、**native body確認により無条件callであることが
確定した**ため`CONFIRMED`に昇格できる。加えて、
`UpdateConnectedControllers()`も同時に無条件で呼ばれるという
新事実が判明した（`CONFIRMED`、これまで未確認だった）。

### 35.3 `SteamPad.SteamControllerReStart()`の正確な本体（CONFIRMED、重要な発見）

`SteamControllerReStart()`（RVA `0x2602EB0`,
Cecil報告Length `0x73`=115バイト）とそのtail-jump先を同様の
手法でdecompileした結果:

```
(lazy static初期化 + プロファイラ/ログ的マーカー呼び出しの定型処理
 - 35.2節と同一パターン)
CALL 0x1810d6760(...)                   ; 同じ汎用invoke的呼び出し
XOR ECX,ECX
CALL 0x1828a1430(0)                     ; = Steamworks.SteamInput.Shutdown()
XOR ECX,ECX
ADD RSP,0x20 / POP RBX
JMP 0x1828a1370(0)                      ; tail-jump
                                         ; = Steamworks.SteamInput.Init()
```

RVAテーブル照合により、`func_0x1828a1430` =
**`Steamworks.SteamInput.Shutdown()`**（RVA `0x28A1430`）、
tail-jump先の`0x1828a1370` = **`Steamworks.SteamInput.Init()`**
（RVA `0x28A1370`）と**exact RVA一致でCONFIRMED**できた。

**結論（CONFIRMED、本調査で最も具体的な発見の1つ）**:
`SteamPad.SteamControllerReStart()`は、その名前が示す通り、
**Steamworks ISteamInputサブシステム全体を`Shutdown()`してから
`Init()`で再初期化する**、文字通りの「Steam Input完全再起動」で
ある。個別のcontroller handleやAnalog action handleを再取得する
処理ではなく、Steamworks側のISteamInputインターフェース自体を
一度破棄して再構築する、より大掛かりな操作である。

`CONFIRMED`なのはあくまで「実機n=2で`Analog1`（IG_RSTICK）の
handle値`=2`がDEAD→LIVE境界を通じて実際に変化しなかった」という
観測事実のみである。「Steam Input action handleがアクション名
文字列から決定論的に導出され、`Shutdown()`→`Init()`後も同じ値に
なる」という一般則は、Steamworks側の仕様や実装を別途確認して
いないため`HYPOTHESIS`にとどめる（説明力は高いが未検証）。
この仮説が正しければ、`Shutdown()`→`Init()`でhandle値は不変の
ままSteam Input内部のaction-data配信状態だけがリセット・再確立
される、という説明が可能になる - という位置づけである。

### 35.4 `SteamPad.UpdateConnectedControllers()`について（部分確認、UNRESOLVED多数）

冒頭部分のみ確認した（RVA `0x2603240`, Length `0x7E2`=2018バイトと
大きく、全体はまだ未解析）。冒頭のゲート条件が判明した:

```
CALL 0x1825ff2c0   ; = SteamManager.get_Initialized() (条件A)
TEST AL,AL / JZ <end>
CMP byte [this+0x18],0   ; (条件B - SteamPad+0x18)
JZ <end>
```

`CONFIRMED`: `UpdateConnectedControllers()`は条件A
（`SteamManager.Initialized`）と条件B（`SteamPad+0x18`）の両方が
真の場合のみ本体処理へ進む。これは`SteamPad.UpdateControl()`の
if/else分岐で使われていたのと**同一の2条件**であり
（30章で既出）、両メソッドが同じゲート条件を共有していることが
構造的に確認できた。ゲートを通過した後は、条件C
（`SteamInputUtil.get_Instance()`、`0x1825fc4b0`）を繰り返し呼び
ながら、controller enumeration・フレームカウンタ的な差分追跡
処理（`PadConnectDiff`/`PadConnectMax`と推測される値の計算に
対応する可能性が高い、`HYPOTHESIS`）を行っている。関数全体の
詳細な逐次解析はまだ行っていない（`UNRESOLVED`、残り約1900バイト）。

### 35.5 更新された全体像

```text
Guide / Steam Overlay
        ↓ (native、呼び出し元は33章時点でUNRESOLVED)
SteamInputUtil.ResetController()
        ↓ (無条件、CONFIRMED)
  ├─ SteamPad.SteamControllerReStart()
  │     ↓ (無条件、CONFIRMED)
  │   Steamworks.SteamInput.Shutdown() → Steamworks.SteamInput.Init()
  │   （ISteamInputサブシステム全体の再起動）
  └─ SteamPad.UpdateConnectedControllers()
        ↓ (条件A・条件Bゲート後、controller再列挙処理 - 詳細UNRESOLVED)
        ↓ (約0.75〜1.3秒後)
LIVE: 同一のIG_RSTICK handle=2に対するGetAnalogActionDataが実値を返す
```

`重要な留保`（34.3節から継続）: 過去の実機テストで手動
`ResetController()`単独ではRight Stickは復活しなかったという
既知結果があるため、上記の`Shutdown()`/`Init()`実行そのものが
復活の十分条件であるとはまだ判定しない。35.3節はあくまで
「`ResetController()`実行時に何が起きるか」の解明であり、
「それが実際にRSTICKを直すメカニズムである」ことの証明では
ない（`HYPOTHESIS`のまま）。

## 36. Phase 2: `GetAnalogActionData`直接観測プローブ（2026-09-07、実装）

### 36.1 設計方針: native detourではなく直接managed API呼び出し

`func_0x1828A0AF0`（`GetAnalogActionData`相当のnativeラッパー）を
Cecil/RVAテーブルで照合したところ、これは実は**匿名のnative関数
ではなく、`Il2CppSteamworks.SteamInput.GetAnalogActionData
(InputHandle_t, InputAnalogActionHandle_t)`という、パブリックかつ
static、既にAssembly-CSharp-firstpass経由でこのプロジェクトから
参照可能なmanaged APIそのものであった**（RVA `0x28A0AF0`で
exact一致、`CONFIRMED`）。返り値の`InputAnalogActionData_t`構造体
も`{EInputSourceMode eMode; float x; float y; byte bActive;}`と
判明した。

これにより、Phase 2はnative detour/hookingを一切行わず、この
既存のpublic APIを**独立したread-only pollとして直接呼び出す**
方式で実装した。native関数へのbyteパッチや、実行アドレスの
書き換え、トランポリン設置は一切行っていない。

`重要な留保（表現の限定）`: これは「APIの入口（呼び出す関数）が
ゲーム本体の内部呼び出しと同一である」ことを意味するに留まる。
`func_0x1828A0AF0`ラッパー自身が持つ独自の前処理
（lazy-init・プロファイラ的マーカー呼び出し等、26章で確認済みの
構造）と全く同一の条件・タイミングで我々の呼び出しが行われる
という意味では**ない**。あくまで「同一のcontroller handle /
action handleに対して、同じSteam Input公開APIを独立に
read-onlyでポーリングしている」という位置づけであり、
「ゲーム内部呼び出し経路の完全な再現」とは表現しない。

### 36.2 実装（`src/Root26Phase2AnalogActionDataProbe.cs`）

- 毎フレーム`Sample()`（`ModMain.OnUpdate()`から呼び出し、
  `FieldDashPatch.IsExplorationActive`に非依存）。
- `SteamInputUtil.instance.steam_pad.Controller`辞書から
  既存のcontroller handle・`ActionSets[Current].Analog[i].Handle`
  （`i=0`=IG_LSTICK, `i=1`=IG_RSTICK）を取得（**独自にhandleを
  再取得・再登録することはしない** - 既存のgame内部状態を
  そのまま読むだけ）。
- `Il2CppSteamworks.SteamInput.GetAnalogActionData(controllerHandle,
  analogActionHandle)`を呼び出し、`bActive`/`eMode`/`x`/`y`を取得。
- `INACTIVE`（`bActive==false`）/ `ACTIVE-ZERO`（`bActive==true`か
  つ`x==0 && y==0`）/ `ACTIVE-NONZERO`（`bActive==true`かつ
  `x!=0 || y!=0`）の3状態に分類し、**状態遷移時のみ**ログ
  （`STATE-CHANGE`、controller handle/`i`/analogActionHandle/
  state/`bActive`/`eMode`/`x`/`y`/timestampを記録）。
  `eMode`単独の変化（stateが変わらなくても）も変化検出対象に
  含めた（ユーザー診断フレームワークのCase 3に対応）。
- 3秒間隔の`HEARTBEAT`ログも追加し、長時間状態変化がない期間でも
  現在値を確認できるようにした（`Root26Phase1ConditionProbe`と
  同じ設計方針）。
- タイムスタンプは既存の`[Root26Phase1]`/`[Root26NativePoll]`/
  `[Root26CallerDiag]`と同じ`DateTimeOffset.Now:O`形式で、
  `[Root26Phase2]`タグを使用。

行動変更・手動`ResetController()`呼び出し・F9・runtime状態への
writeは一切なし。`GetAnalogActionData`はSteamworks側の
問い合わせAPIであり、呼び出し自体がAction登録/有効化/状態変更を
引き起こすものではない（読み取り専用のクエリAPI）。

### 36.3 判定フレームワーク（ユーザー指定）

DEAD→Guide/Overlay→Reset/ReStart→LIVEの連続区間で、以下の
どのケースに該当するかを確認する。

- **Case 1**: `DEAD: bActive=false, x=y=0` → `LIVE: bActive=true,
  x/y実値` — RSTICK actionが非active→activeになった可能性が強い。
- **Case 2**: `DEAD: bActive=true, x=y=0` → `LIVE: bActive=true,
  x/y実値` — actionは有効だが値配信のみが変化した可能性が強い。
- **Case 3**: `eMode`がDEAD/LIVEで変化 — action mode/binding側の
  再構成を疑う。
- **Case 4**: native側（`GetAnalogActionData`）ではDEAD中から
  non-zeroなのに`SetAnalog`ではzero — Scenario C解釈の再検討が
  必要。

### 36.4 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   c5f5b009ddd4d478d15f49fd72da9f14aef21243ae7508be6ee10b8234d6a5dd
SHA-256 (deployed): c5f5b009ddd4d478d15f49fd72da9f14aef21243ae7508be6ee10b8234d6a5dd
  → 一致
```

### 36.5 次の実機テスト（次のステップ、ユーザー実施待ち）

同一手順（起動→DEAD確認→Guide/Overlay→LIVE確認、ログ
アーカイブ不要）。今回は`[Root26Phase2]`タグの
`STATE-CHANGE GetAnalogActionData`ログを最優先で確認し、
36.3節のCase 1〜4のどれに該当するかを判定する。

## 37. Phase 2 実機テスト結果（2026-09-07 23:53台）

ログ: `docs/research/ROOT26_POST_REBOOT_EVIDENCE_20260907/melonloader/Latest.log_phase2_20260907.log`

### 37.1 タイムライン

| 時刻 | 出来事 |
|---|---|
| 23:53:17.833 | ゲーム起動 |
| 23:53:30.508 | `SteamControllerReStart()` count=1,2（初回接続） |
| 23:53:30.536 | Phase2初回ログ: `i=0`/`i=1`とも**既に`ACTIVE-ZERO`**（`bActive=1`,`eMode=JoystickMove`,`x=y=0`）。この時点ではexploration未開始（`Root26NativePoll`未起動）。 |
| 23:53:36.240〜38.839 | exploration session 1。`Root26NativePoll`が**`DEAD(128-fixed)`固定、count=300、nonCenter=0、transitions=0**で終了。この間、Phase2は`i=1(IG_RSTICK)`について**継続して`ACTIVE-ZERO`**（`bActive=1`,`eMode=JoystickMove`,`x=y=0`）を報告し続けた（`STATE-CHANGE`ログが出ない=変化なし）。 |
| **23:53:38.403** | `i=0`・`i=1`**両方**が同時に`INACTIVE`（`bActive=0`,`eMode=k_EInputSourceMode_None`）に変化。 |
| 23:53:38.839 | `Root26NativePoll` `SESSION-SUMMARY`（exploration終了、finalState=DEAD） |
| **23:53:38.949〜.950** | `i=0`・`i=1`**両方**が`ACTIVE-ZERO`（`bActive=1`,`eMode=JoystickMove`）に復帰。 |
| **23:53:38.957〜.959** | `ResetController()` count=1,2 → `SteamControllerReStart()` count=3,4 |
| 23:53:39.717 | `Root26NativePoll` session 2開始、`DEAD`から観測再開 |
| **23:53:39.772** | `i=1(IG_RSTICK)`が**初めて`ACTIVE-NONZERO`**（`x=-0.0106,y=0.0092`、Reset呼び出しから約815ms後） |
| 23:53:40.210 | `Root26NativePoll` `STATE-TRANSITION DEAD -> LIVE`（X/Y、native側確認、Resetから約1.25秒後） |
| 23:53:40.9〜44.3 | `i=1`が`ACTIVE-ZERO`⇔`ACTIVE-NONZERO`を継続的に遷移（実際のスティック操作を反映、`bActive`は常に`1`のまま） |
| 23:53:44.960〜45.460 | `Root26NativePoll`が短時間`LIVE→DEAD→LIVE`と遷移（後述37.3で解釈） |
| 23:53:47.132 | session 2終了、`finalState=DEAD transitions=4` |

### 37.2 判定: 今回のサンプルは **Case 2**（`bActive`は不変、`x`/`y`のみゼロ→実値）

`CONFIRMED`: 観測されたDEAD区間（23:53:36.7〜38.4、native側で
`128-fixed`固定を確認済みの区間）を通じて、`i=1(IG_RSTICK)`の
`GetAnalogActionData`は**`bActive=1`（true）、
`eMode=k_EInputSourceMode_JoystickMove`のまま変化せず**、
`x=0, y=0`だけが継続していた。Reset/ReStart（23:53:38.957）の
約815ms後、`x`/`y`が初めて実値（非ゼロ）を返し始めた
（`bActive`/`eMode`はこの前後でも変化していない）。

これは36.3節の判定フレームワークで**Case 2**
（`DEAD: bActive=true, x=y=0` → `LIVE: bActive=true, x/y実値`）に
該当する。Case 1（`bActive=false→true`）ではなかった。

`CONFIRMED`（ユーザー確認済み、2026-09-07）: 今回のDEAD確認時、
ユーザーは実際に右スティックを動かし、カメラが反応しないことを
確認している。したがって37.2節冒頭のDEAD区間中の`x=0, y=0`は
「スティックが単に中央にあっただけ」ではなく、**実際に右
スティックを操作していたにも関わらず`GetAnalogActionData`が
`x=0, y=0`を返し続けていた**ことが確定した。

`CONFIRMED`（範囲を限定）: 同一のController handle / ActionSet /
`IG_RSTICK handle=2`に対し、`bActive=true`・
`eMode=k_EInputSourceMode_JoystickMove`と報告されているにも
関わらず、ユーザーが物理的に右スティックを動かしても
`GetAnalogActionData()`が`x=0, y=0`を返し続けていたことが直接
観測できた。これは「ゲーム側の`GetPadAnalog`→`SteamPadSet`→
`GetAnalogActionData`呼び出しより下流（ゲームコード側）に原因が
ある」という仮説を強く後退させる証拠である。

`UNRESOLVED`（過度に強めない）: 「actionの存在・有効化・binding」
まで完全に正常であったとは、まだ確定していない。`bActive=true`/
`eMode=JoystickMove`という2つのフィールドが正常な値を示している
ことは確認できたが、Steam Input内部でこのactionが「boundとして
activeだが、物理デバイスとの結び付け（binding）自体は死んでいる」
という状態である可能性は排除できていない。したがって現時点では
「Steam Input内部のanalog action data配信境界（`GetAnalogActionData`
の入出力境界）で問題が観測された」という範囲にとどめ、
「bindingは正常であり、純粋に値配信層だけが原因」とまでは
判定しない。

### 37.3 副次的発見1: Overlay開閉と思われる両スティック同時`INACTIVE`ウィンドウ

`i=0`・`i=1`が**同時に**`INACTIVE`（`bActive=0`,`eMode=None`）に
なった23:53:38.403〜38.949の約546ms間は、`ResetController`呼び出し
（38.957）の直前かつexploration session 1終了（38.839）の前後に
位置する。`HYPOTHESIS`: これはSteam Overlayが開いている間、
Steam Input側が**全アクションを一時的にinactive扱いにする**
（`bActive=false`かつ`eMode=None`）という、Steamworksの一般的な
挙動を反映している可能性が高い。これはRSTICK固有の問題とは別の、
Steam Overlay自体の既知の副作用と考えられる（未検証、Steamworks
仕様の直接確認はしていない）。この「両スティック同時
`INACTIVE`」パターンは、今後Overlay開閉タイミングを検出する
独立した手がかりとして使える可能性がある。

### 37.4 副次的発見2: LIVE確立後の短時間`DEAD`再検出は「スティック中央」の可能性が高い

23:53:44.960〜45.460、23:53:47.132で`Root26NativePoll`が短時間
`LIVE→DEAD`（または`DEAD`のまま終了）と判定している一方、
同時刻帯のPhase2ログでは`i=1`の`bActive`は**一度も`0`に戻って
いない**（`ACTIVE-ZERO`のまま）。`HYPOTHESIS`: native側の
`Root26NativePoll`バケット判定（250msバケット内が全て128固定なら
`DEAD`）は、「壊れている」状態と「単にスティックが中央
（未操作）にある」状態を区別できない。したがって、LIVE確立後に
短時間だけ観測される`DEAD`遷移は、多くの場合RSTICKが再度
壊れたのではなく、**単にプレイヤーがスティックを操作していない
瞬間を指している可能性が高い**。この解釈は、`GetAnalogActionData`
側で`bActive`が継続して`true`のままだったという直接証拠に
支えられている。本調査全体でこれまで「短時間のDEAD再遷移」を
個別に深く調べていなかったが、今後は`Root26NativePoll`単独では
なくPhase2の`bActive`と併せて判定するのが安全である。

### 37.5 未解決点・次の候補

- 37.2節のCase 2判定はユーザー確認により`CONFIRMED`級に確定した
  （前節参照）。
- `x`/`y`が実際にゼロ配信からリアルタイム値配信へ切り替わる
  正確なメカニズム（Steam Input内部で何が変わるのか）は、
  35章の`Shutdown()`/`Init()`が引き金という以上のことはまだ
  分かっていない（`UNRESOLVED`）。
- 34.3節の留保（過去の手動`ResetController()`単独テストで
  復活しなかった件）は今回も維持する - 今回のn=3
  （32章・34章・37章）はすべてGuide/Overlay起点であり、
  `ResetController()`単独実行の効果はまだ独立に検証されていない。

## 38. `SteamPad.UpdateConnectedControllers()`完全decompile（2026-09-08、静的解析）

ユーザー指示により、`SteamPad.UpdateConnectedControllers()`
（RVA `0x2603240`, Length `0x7E2`=2018バイト）を35章と同じ
exact-bounds手法で完全にdecompileした。

### 38.1 ゲート条件（CONFIRMED、既出条件と一致）

```c
if (!SteamManager.Initialized) return;   // 条件A
if (this->field_0x18 == '\0') return;    // 条件B（SteamPad+0x18）
```

`UpdateControl()`のif/else分岐で使われていたのと同一の2条件
（30章）であることを確認済み（37章時点で冒頭のみ確認、
今回全体を通じてもこの2条件のみがゲートであることを確認）。

### 38.2 `Steamworks.SteamInput.GetConnectedControllers()`の呼び出し（CONFIRMED、新事実）

```c
lVar7 = SteamInputUtil.instance();    // 条件C
if (lVar7 != 0) {
    int oldCount = lVar7->field_0x3c;               // 旧カウンタ保存
    lVar7 = SteamInputUtil.instance();
    uVar6 = func_0x1828a0df0(this->field_0x28, 0);  // = GetConnectedControllers(handlesOut)
    if (lVar7 != 0) {
        lVar7->field_0x3c = uVar6;                   // 新カウント保存
        ...
        lVar7->field_0x38 = (新カウント - oldCount);  // = PadConnectDiff!
```

`func_0x1828a0df0`はRVA完全一致で
**`Steamworks.SteamInput.GetConnectedControllers(InputHandle_t[]
handlesOut)`**（RVA `0x28A0DF0`）と`CONFIRMED`できた。これは
本物のSteamworks controller列挙APIであり、`this->field_0x28`
（配列）へ結果を書き込む。

さらに`SteamInputUtil`インスタンスの
`+0x3c`フィールド＝直近の`GetConnectedControllers()`が返した
controller数、`+0x38`フィールド＝その差分（新−旧）であることが
判明した。この`+0x38`は、既存probe（`Root26SteamStateProbe`）が
`instance.PadConnectDiff`として読んでいる managed property と
**正確に一致する**と考えられる（`HYPOTHESIS`、フィールドの
意味とアクセスパターンから極めて説明力が高いが、managed
property→native offsetの直接的な1:1対応はCecilレベルでは
未検証）。`PadConnectDiff < 0`（コントローラ数が減少）の場合、
`+0x40`フィールド（初回のみ）に`1`をセットする処理がある
（このフィールドの正体は`UNRESOLVED`、`PadConnectDiffCount`等の
候補はあるが確認できていない）。

### 38.3 メインループ: controller配列の新旧比較と`get_action()`の再実行条件（CONFIRMED、重要）

```c
for (i = 0; i < count; i++) {
    newElem = this->field_0x28[i];   // GetConnectedControllersで
                                       // 取得した「新」配列
    oldElem = this->field_0x20[i];   // 前回の「旧」配列
    if (!Equals(newElem, oldElem, 4)) {   // 新旧が一致しない
                                            // (=このスロットは
                                            // 変化した)
        bVar4 = true;   // 「何か変化があった」フラグ
        // 新しいInputInfo的オブジェクトを確保・フィールド設定
        ...
        func_0x1810d6760(...);   // 汎用invoke的イベント発火
                                   // (35章と同一パターン、
                                   //「controller接続」通知の可能性)
        // Controller辞書への存在チェック・挿入
        cVar5 = func_0x181678020(newElem, dict+i, 0);  // 既存判定
        if (cVar5 == '\0') {   // 辞書にまだ存在しない
            func_0x18163a2e0(this->field_0x10, ..., ...);  // 挿入
        }
        // **SteamPad.get_action(index) を呼ぶ**
        cVar5 = func_0x182603de0(this, i, 0);
        if (cVar5 == '\0') {
            // get_actionが失敗 -> GetInputTypeForHandle呼び出し +
            // 別のinvokeイベント発火 + 配列スロットをクリア
        }
    }
    else {
        // 変化なし: 旧データをそのまま新スロットへコピーするだけ
        newElem = oldElem;   // (get_actionは呼ばれない)
    }
}
```

`func_0x182603de0`はRVA完全一致で**`SteamPad.get_action(int
index)`**（RVA `0x2603DE0`）と`CONFIRMED`できた。これは本調査の
24〜25章で既に「`IG_LSTICK`/`IG_RSTICK`のaction handle
（再）登録を行う唯一の経路」として特定済みの関数である。

**結論（CONFIRMED）**: `get_action(index)`（＝action handle
再登録処理）が実際に呼ばれるのは、`UpdateConnectedControllers()`
のメインループで「そのcontrollerスロットの新旧比較が
不一致だった場合」**のみ**である。新旧が一致する（＝
接続中のcontrollerに変化がない）場合は、`get_action()`は
呼ばれず、既存データがそのままコピーされるだけである。

これは24〜25章で既に確認済みの「`get_action`の唯一のcaller
=`UpdateConnectedControllers`」という事実と矛盾なく、今回
**呼び出し条件（controller配列の新旧比較不一致）**まで
具体的に特定できたことになる。

### 38.4 第2ループ: `SteamInputUtil.instance()->field_0x30`との一致判定と`SteamControllerReStart()`の再帰的呼び出し（CONFIRMED、重要な新事実）

メインループ終了後（`bVar4`が立っている場合）、`SteamInputUtil.
instance()->field_0x28 == -1`という特殊条件で即座に
`LAB_1826038d8`へジャンプするパスと、そうでなければ第2の
ループ（`LAB_182603780`起点）に入るパスがある。第2ループは
以下を行う:

```c
for (i = 0; i < this->field_0x28count; i++) {
    elem = this->field_0x20[i];
    if (elem != 0) {
        instance2 = SteamInputUtil.instance();
        if (elem == instance2->field_0x30) {   // 「現在アクティブな
                                                  // controller」的な
                                                  // ポインタと一致
            // invokeイベント発火（35章と同一パターン）
            instance3 = SteamInputUtil.instance();
            instance4 = SteamInputUtil.instance();
            if (instance4 != 0) {
                instance4->field_0x2c = i;
                if (instance3 != 0) {
                    instance3->field_0x28 = i;
                    goto LAB_1826038d8;
                }
            }
        }
    }
}

LAB_1826038d8:
    SteamPad.SteamControllerReStart(this, 0);   // 再度呼び出し！
    return;
```

`CONFIRMED`: `UpdateConnectedControllers()`自身が、上記の条件
（`field_0x28==-1`、または第2ループで`field_0x30`と一致する
要素が見つかった場合）を満たすと、`SteamPad.
SteamControllerReStart()`を**再度**呼び出すコードパスが実在する。
すなわち`ResetController()`→`SteamControllerReStart()`
（1回目）→（tail-call）`UpdateConnectedControllers()`→
（条件次第で）`SteamControllerReStart()`（2回目）という、
最大2段の再帰的な構造になり得る。

`UNRESOLVED`: 34章の実機ログでは`ResetController()`が2回
（count=1,2）、`SteamControllerReStart()`も2回（count=3,4）
観測されており、これは「`ResetController()`が外部から2回
呼ばれ、その都度`SteamControllerReStart()`が1回ずつ
（`ResetController`自身の直接呼び出し分のみ）発生した」という
単純な説明で数が一致する。したがって、今回確認した
`UpdateConnectedControllers()`内部の再帰的`SteamControllerReStart()`
呼び出しパスが実機テストで**実際に踏まれたかどうかは
未確認**である（`bVar4`や`field_0x28==-1`条件の実際の値を
runtimeで観測していないため）。

### 38.5 明示的に不在を確認した項目（CONFIRMED、否定的所見）

- `ActivateActionSet`/`ActivateActionSetLayer`
  （RVA `0x28A0820`他）への呼び出しは、この関数の逆アセンブリ
  全体を通じて**一度も出現しない**。ActionSetの切り替えは
  `UpdateConnectedControllers()`の役割ではない。
- 明示的な「Steam Input RunFrame」に相当する呼び出しも
  見当たらない（`GetConnectedControllers()`のみが列挙相当の
  呼び出し）。

### 38.6 815ms遅延に関する重要な考察（HYPOTHESIS、説明力が高い）

`ResetController()`・`SteamControllerReStart()`・
`UpdateConnectedControllers()`の3関数はいずれも、内部に
ループ待機・リトライ・非同期処理を一切含まない、単一の
同期的な関数呼び出し連鎖である（`CONFIRMED`、instruction-level
で確認済み）。したがって、34章で観測された「Reset呼び出しから
約815ms後に初めてRSTICKの`x`/`y`が実値化する」という遅延は、
**今回解析した`ResetController`/`SteamControllerReStart`/
`UpdateConnectedControllers`自体の同期実行時間では説明できない**
（`CONFIRMED`、範囲を限定）。

`UNRESOLVED`: これらの3関数以外に、後続フレームでGameAssembly側の
別の処理（例えば毎フレーム走る`UpdateInput()`/`SteamPadSet()`が、
815msの間に追加の再初期化・再列挙・action再登録を行っている
可能性）までは排除できていない。

`HYPOTHESIS`: この遅延は、`Steamworks.SteamInput.Shutdown()`→
`Init()`（35.3節）の実行後、Valve側のSteam Inputサブシステム
（`steam_api64.dll`/Steamクライアント側、GameAssembly.dllの
範囲外でありこれ以上の逆アセンブリ対象外）が非同期的に
再初期化を完了するまでの時間である可能性がある。この部分は
GameAssembly.dllの静的解析ではこれ以上追跡できない
（Valve製のクローズドソースコンポーネントであるため）。

### 38.7 ユーザー確認項目への回答まとめ

| 確認項目 | 結果 |
|---|---|
| Controller dictionaryの追加・削除・再列挙 | `CONFIRMED`: 新旧比較不一致時のみ追加。削除処理はメインループ内では未確認（`UNRESOLVED`）。列挙自体は`GetConnectedControllers()`（`CONFIRMED`）。 |
| Input handleの取得・更新 | `CONFIRMED`: `GetConnectedControllers()`が`this->field_0x28`へ書き込む。 |
| `InputInfo.Current`/`ControllerType` | 新規オブジェクト構築時にフィールド設定される構造を確認したが、正確なフィールド名対応は`HYPOTHESIS`。 |
| `get_action()`の呼び出し条件 | `CONFIRMED`: 新旧比較が不一致の場合のみ。 |
| Analog/Digital action handle再登録条件 | `CONFIRMED`: 上記`get_action()`条件と同一（24〜25章の経路） |
| `ActivateActionSet`系呼び出し | `CONFIRMED`（否定的）: 呼ばれていない。 |
| `RunFrame`/enumeration相当 | `GetConnectedControllers()`が該当（`CONFIRMED`）。専用の`RunFrame`呼び出しは無し。 |
| `PadConnectDiff`/`PadConnectMax` | `PadConnectDiff`=`+0x38`フィールドと特定（`HYPOTHESIS`、説明力高）。`PadConnectMax`はこの関数内では未確認（`UNRESOLVED`）。 |
| 即時完了か後続frame持ち越しか | `CONFIRMED`: 本関数自体は同期的・単発で完結。815ms遅延はSteam側非同期処理に起因すると推測（`HYPOTHESIS`、38.6節）。

## 39. Phase 3（最小構成）: Reset後815ms区間のGameAssembly側呼び出し観測（2026-09-08、実装）

### 39.1 目的

38.6節の`UNRESOLVED`を検証するため、`ResetController()`/
`SteamControllerReStart()`発生から約2.5秒間（観測された遅延
815ms〜1.3秒に十分なマージンを持たせた区間）、GameAssembly側で
追加の再初期化・再列挙・action再登録が発生していないかを
最小限の追加コードで確認する。目的は「Valve側に飛んだ」ことの
証明ではなく、**GameAssembly側で新たに何か起きていないか
潰すこと**である。

### 39.2 実装（`src/Root26Phase3PostResetWatchProbe.cs`）

新規に以下の呼び出し回数カウンタ（Harmony Prefix、count-onlyで
毎回はログしない）を追加した:

- `SteamPad.UpdateConnectedControllers()`
- `SteamPad.get_action(int index)`（indexも記録）
- `SteamPad.UpdateControl()`
- `Il2CppSteamworks.SteamInput.ActivateActionSet(...)`
- `Il2CppSteamworks.SteamInput.GetConnectedControllers(...)`

既存の`SteamPadSet`/`UpdateInput`カウンタ（`Root26Phase1*`）は
再利用し、重複実装しない。

**監視ウィンドウ方式**: `ResetController()`/
`SteamControllerReStart()`のPrefix（既存の
`Root26ResetControllerCallProbe`/
`Root26SteamControllerReStartCallProbe`）から
`Root26Phase3PostResetWatchProbe.NotifyResetEvent()`を呼び、
2500ms間の監視ウィンドウを開始（既に開いていれば延長）する。
ウィンドウが開いている間のみ、`Sample()`
（`ModMain.OnUpdate()`から毎フレーム呼び出し）が約100ms間隔で
上記カウンタの累積値スナップショットをログする。これにより、
ログを100ms刻みのタイムラインとして読むことで、「Reset直後に
1回だけ発生したもの」（スナップショット間で+1のみ）と
「815ms経過後も毎フレーム発生し続けるもの」（スナップショット
間で継続的に増加）を区別できる。

行動変更・手動`ResetController()`/`SteamControllerReStart()`呼び出し・
F9・native patch/detourは一切なし。すべてHarmony Prefixによる
観測のみ。

### 39.3 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   d74be3b49bce0632c96ea2824bf60e2f793090bd67654e93a3940432f37a34f4
SHA-256 (deployed): d74be3b49bce0632c96ea2824bf60e2f793090bd67654e93a3940432f37a34f4
  → 一致
```

### 39.4 次の実機テスト（次のステップ、ユーザー実施待ち）

同一手順（起動→DEAD確認→Guide/Overlay→LIVE確認、ログ
アーカイブ不要）。`[Root26Phase3]`タグの`SNAPSHOT`/
`WINDOW-END`ログを`[Root26Phase2]`の`STATE-CHANGE`
（RSTICK実値化タイミング）と突き合わせ、815ms区間で
`UpdateConnectedControllers`/`get_action`/`UpdateControl`/
`ActivateActionSet`/`GetConnectedControllers`の増分があるかを
判定する。

## 40. Phase 3 実機テスト結果（2026-09-08 00:09台）

ログ: `docs/research/ROOT26_POST_REBOOT_EVIDENCE_20260907/melonloader/Latest.log_phase3_20260908.log`

### 40.1 タイムライン（Guide/Overlay起点の監視ウィンドウ）

| 時刻 | 出来事 |
|---|---|
| 00:09:24.377 | `ResetController()`（監視ウィンドウ開始トリガー） |
| 00:09:24.378〜.379 | `SteamControllerReStart()`×2（ウィンドウ延長） |
| 00:09:24.387〜26.882 | `[Root26Phase3]` `SNAPSHOT`が約100〜110ms間隔で24回記録 |
| **00:09:25.300** | `[Root26Phase2]` `i=1(IG_RSTICK)`が初めて`ACTIVE-NONZERO`（`x=-0.270,y=0.019`、Resetから約923ms後） |
| 00:09:25.646 | `[Root26NativePoll]` `STATE-TRANSITION DEAD -> LIVE`（native側確認、Resetから約1269ms後） |
| 00:09:26.882 | `WINDOW-END`（監視終了） |

### 40.2 判定: GameAssembly側に追加の再初期化イベントなし（CONFIRMED）

監視ウィンドウ全体（24.387〜26.882、約2.5秒）を通じて、各スナップ
ショット間の増分（1スナップショットあたり約100〜110ms）を計算した:

| 関数 | 増分パターン |
|---|---|
| `UpdateConnectedControllers` | 毎スナップショット+6〜+8（**RSTICK実値化前後で変化なし**、常に毎フレーム相当のペースで継続） |
| `UpdateControl` | 同上、毎スナップショット+6〜+8（変化なし） |
| `GetConnectedControllers` | 同上、毎スナップショット+6〜+8（変化なし） |
| `UpdateInput`/`SteamPadSet`（既存カウンタ流用） | 同上、毎スナップショット+6〜+8（変化なし） |
| `get_action` | ウィンドウ開始時点から**一度も増加せず**、`idx0=2`のまま固定 |
| `ActivateActionSet` | ウィンドウ開始時点から**一度も増加せず**、`2`のまま固定 |

RSTICK実値化の瞬間（00:09:25.300）を挟む前後のスナップショット
（25.257時点: `UpdateConnectedControllers=666`、25.368時点:
`UpdateConnectedControllers=674`、差分+8で他の区間と同一）を
直接比較しても、**この瞬間に対応する特別なイベント・スパイク・
呼び出しパターンの変化は一切観測されなかった**。

`CONFIRMED`: `ResetController()`/`SteamControllerReStart()`発生後
815ms〜1.3秒のRSTICK実値化区間において、`UpdateConnectedControllers`
/`UpdateControl`/`GetConnectedControllers`/`UpdateInput`/
`SteamPadSet`はいずれも、Reset前から継続していた**通常の毎フレーム
ポーリングをそのまま継続しているだけ**であり、追加の
再初期化・再列挙は検出されなかった。`get_action()`（action
handle再登録）・`ActivateActionSet()`（ActionSet切り替え）は
この区間中**一度も呼ばれなかった**。

### 40.3 結論（38.6節の`UNRESOLVED`への回答）

今回観測した5つの関数（`UpdateConnectedControllers`/`get_action`/
`UpdateControl`/`ActivateActionSet`/`GetConnectedControllers`、
および既存カウンタの`UpdateInput`/`SteamPadSet`）の範囲では、
Reset後のRSTICK実値化に対応する追加のGameAssembly側処理は
`CONFIRMED`級に否定された。ただし、これはGameAssembly.dll内の
**観測対象とした関数群に限った**否定であり、これら以外の
未観測の経路が存在する可能性は論理的には排除できない
（`UNRESOLVED`、ただし主要な候補はほぼ潰せたと言える）。

これにより、38.6節の`HYPOTHESIS`
（「815ms〜1.3秒の遅延はValve側Steam Inputサブシステムの
非同期再初期化に起因する」）は、それを否定する対抗仮説
（「GameAssembly側の何らかの後続処理が原因」）が今回の観測範囲では
支持されなかったという意味で、相対的に強化された
（`HYPOTHESIS`のまま、確定はしていない）。

## 41. Phase 4: 最小シーケンス切り分けPoC（`SteamInput.Shutdown()`/`Init()`単独、2026-09-08）

### 41.1 目的と位置づけ

39〜40章までで、Guide/Overlay成功経路
（`ResetController()`→`SteamControllerReStart()`→
`Shutdown()`/`Init()`→`UpdateConnectedControllers()`）のうち、
`UpdateConnectedControllers()`以降の815ms〜1.3秒区間には
GameAssembly側の追加処理がないことが確認できた。次段として、
根本原因をさらにSteam内部へ掘るより、**この既知の回復経路を
安全に自動化するための最小十分シーケンスを切り分ける**方向へ
進める。

`重要な既知の制約（ユーザー提供、既知Evidence）`: 過去の有効な
手動`ResetController()`単独テストではRight Stickは復活しなかった。
したがって`ResetController()`単独は復活の十分条件ではないことが
既に分かっている。Phase 4はこれとは異なる、より狭い候補
（`SteamInput.Shutdown()`+`Init()`のみ、`ResetController()`/
`SteamControllerReStart()`/`UpdateConnectedControllers()`は
一切呼ばない）を独立に検証する。

### 41.2 実装（`src/Root26Phase4NativeRecoveryPoc.cs`）

これは本調査で初めて「read-onlyではない」意図的な単発PoCである
（明示的にユーザーが要求した、1変数ずつ切り分ける実験の
第1弾）。

- トリガーキー: **F10**（VK `0x79`）。**F9は使用しない**
  （F9は退役済みの`ManualResetControllerPoc`に割り当てられており、
  過去にサードパーティMod関連の不可視System→Quit確認ダイアログの
  副作用が疑われたため、本調査全体で恒久的に使用禁止としている）。
  F10はこのMod・SMT3HD本体のいずれの既存操作にも割り当てられて
  いない。
- 1セッションにつき**最大1回のみ**発火する。旧F9 PoCで発生した
  整数オーバーフローのcooldownバグ（クラス全体を回避するため、
  時間ベースのcooldownではなく、単純な真偽値ラッチ
  （`_hasFiredThisSession`）を使用し、他の処理より先に即座に
  セットすることで、以降の処理が例外を投げても再発火しない
  設計にした。
- 立ち上がりエッジのみで発火（押しっぱなしで連続発火しない）。
- 呼び出すのは**`Il2CppSteamworks.SteamInput.Shutdown()`→
  `Il2CppSteamworks.SteamInput.Init()`のみ**。
  `SteamInputUtil.ResetController()`・`SteamPad.
  SteamControllerReStart()`・`SteamPad.
  UpdateConnectedControllers()`は一切直接呼ばない
  （ゲーム自身がこれらを事後的に呼ぶかどうかは、既存のRoot26
  プローブでGuide/Overlay経路と全く同じ形で観測されるのみ）。
- 発火後、既存の`Root26Phase3PostResetWatchProbe.
  NotifyResetEvent()`を呼び、Guide/Overlay経路と同一の観測
  インフラ（2.5秒監視ウィンドウ、100ms間隔スナップショット）で
  事後の挙動を捕捉できるようにした。
- Broker/InputHelper/SDLには一切関与しない。他のSteam Input APIは
  呼ばない。handle/buffer/action-data値へのwriteは一切ない。

### 41.3 判定基準（ユーザー指定）

- **成功条件**: Guide/Overlayなしで、F10押下後0.5〜2秒程度で
  `[Root26Phase2]`の`i=1(IG_RSTICK)`が`ACTIVE-NONZERO`となり、
  `[Root26NativePoll]`でnative側もLIVEに遷移する。
- **失敗条件**: 2〜3秒経過してもRSTICKが`ACTIVE-ZERO`
  （または`INACTIVE`）のまま。
- 失敗した場合のみ、次段階として
  `Shutdown()`→`Init()`→`UpdateConnectedControllers()`を
  追加した別PoCを検討する（今回は実装しない）。

### 41.4 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   7f43ba02503b1bc2c64e781c87490a61c652185db3509cf5f5314c63bdb3e1b1
SHA-256 (deployed): 7f43ba02503b1bc2c64e781c87490a61c652185db3509cf5f5314c63bdb3e1b1
  → 一致
```

### 41.5 実機テスト手順（次のステップ、ユーザー実施待ち）

1. 通常起動 → 探索で右スティックDEAD確認（実際にスティックを
   動かして確認）。
2. **F10を1回押す**（Guide/Overlay操作は行わない）。
3. 2〜3秒待ち、右スティックが反応するか確認する。
4. ログを回収（`[Root26Phase4]`の`PHASE4-TRIGGER`/`PHASE4-CALL`/
   `PHASE4-DONE`、`[Root26Phase2]`の`i=1`状態変化、
   `[Root26NativePoll]`の`STATE-TRANSITION`、`[Root26Phase3]`の
   `SNAPSHOT`を確認）。
5. 成功/失敗いずれの場合もゲームを終了してログを提供する。

F10は1セッションにつき1回しか効かないため、テストを複数回
行いたい場合はゲームを再起動すること。

## 42. Phase 4 実機テスト結果: FAIL + 重大な副作用（2026-09-08 00:16台）

ログ: `docs/research/ROOT26_POST_REBOOT_EVIDENCE_20260907/melonloader/Latest.log_phase4_20260908_FAIL.log`

### 42.1 判定: FAIL（CONFIRMED）

F10押下（00:16:40.432）で`SteamInput.Shutdown()`（`True`を返却）
→`SteamInput.Init()`（`True`を返却）を実行したが、41.3節の成功
条件は満たされなかった。ログ終了（00:16:53、正常なゲーム終了）
までの間、`[Root26Phase2]`の`i=1(IG_RSTICK)`は一度も
`ACTIVE-NONZERO`にならず、`[Root26NativePoll]`の
`SESSION-SUMMARY`（00:16:49.639）も
`finalState=DEAD(128-fixed) transitions=0`のままだった。

**結論（CONFIRMED、範囲を限定）**: `SteamInput.Shutdown()`→
`Init()`単独ではRight Stickの復活に**不十分**である。これ以上は
まだ確定していない - `UpdateConnectedControllers()`が呼ばれる
ことが必要なのか、Guide/Overlayに伴うGameAssembly.dllの外側
（Steam側）で起きる別の状態変化が必要なのかは未分離である。
現時点では「`ResetController()`経路に含まれる他の処理、または
Guide/Overlayに伴うGameAssembly外の状態変化が必要」という
範囲にとどめる（`HYPOTHESIS`）。

### 42.2 重大な副作用: ゲーム全体の更新頻度が約2倍に（CONFIRMED、精密に定量化）

ユーザー報告（「動きが超高速化した」）を、独立した2つのプローブ
（`[Root26Phase3]`の100ms間隔`SNAPSHOT`、`[Root26Phase1]`の
2秒間隔`HEARTBEAT`）で相互検証できた。

**Phase1 HEARTBEAT（`UpdateInputCalls`、2秒間隔、F10前後）**:

| 時刻 | 累積値 | 直前比差分 | 2秒あたりレート |
|---|---|---|---|
| 00:16:28.814 | 122 | +116 | 約58.3回/秒 |
| 00:16:30.810 | 240 | +118 | 約59.3回/秒 |
| 00:16:32.810 | 360 | +120 | 約60.2回/秒 |
| 00:16:34.811 | 470 | +110 | 約55.0回/秒 |
| 00:16:36.813 | 590 | +120 | 約59.9回/秒 |
| 00:16:38.813 | 710 | +120 | 約60.0回/秒 |
| **00:16:40.813**（F10=40.432、区間内） | 852 | +142 | 約71.0回/秒（遷移区間） |
| **00:16:42.813**（F10後、最初の完全区間） | 1092 | **+240** | **約120.0回/秒** |
| 00:16:44.813 | 1332 | +240 | 約120.0回/秒 |
| 00:16:46.813 | 1572 | +240 | 約120.0回/秒 |
| 00:16:48.813 | 1812 | +240 | 約120.0回/秒 |
| 00:16:50.808 | 2026 | +214 | 約107.4回/秒 |
| 00:16:52.808 | 2238 | +212 | 約106.0回/秒 |

F10直前は安定して約58〜60回/秒（60fps相当）だったのが、F10後は
約106〜120回/秒（**ほぼ正確に2倍**）に跳ね上がり、ログ終了
（ゲーム正常終了、00:16:53）まで**元に戻らなかった**。

**Phase3 SNAPSHOT（100ms間隔、独立クロスチェック）**でも同一の
傾向を確認: F10前の監視ウィンドウ（00:16:26.7〜29.2、Guide/Overlay
起点）では`UpdateConnectedControllers`/`UpdateControl`/
`GetConnectedControllers`/`UpdateInput`/`SteamPadSet`すべてが
1スナップショット（約108ms）あたり+6〜+8（約63回/秒相当）で
一致していたのに対し、F10後の監視ウィンドウ（00:16:40.4〜42.9）
では同じ5つの関数すべてが1スナップショットあたり+12〜+14
（約119回/秒相当）と、**約1.9〜2.0倍**に一致して増加していた。
`get_action`と`ActivateActionSet`はこの区間でも一度も増加しな
かった（`2`のまま固定）。

`CONFIRMED`: F10（`SteamInput.Shutdown()`→`Init()`単独実行）の
直後から、`UpdateInput`/`SteamPadSet`/`UpdateControl`/
`UpdateConnectedControllers`/`GetConnectedControllers`の
呼び出し頻度が**5つとも均一に約2倍**になり、この状態がゲーム
セッション終了まで継続した（自然に元へ戻ることはなかった）。
`get_action`/`ActivateActionSet`の頻度には変化がなかった。

### 42.3 原因候補（ユーザー提示の4仮説、CONFIRMED/HYPOTHESIS/UNRESOLVED分離）

1. `SteamInput.Init()`再実行によるSteam Input更新/コールバック
   二重登録: `UNRESOLVED`。Steam Input側の内部コールバック機構は
   GameAssembly.dll静的解析の範囲外。
2. GameAssembly側のSteamPad更新ループ自体が多重化: `UNRESOLVED`
   （部分的に否定的材料あり）。もしSteamPad更新ループ「だけ」が
   多重化したなら、Steam Inputと無関係な`UpdateControl`
   （SteamPad自身のメソッドではあるが）はともかく、本来Steam
   Inputと直接関係しないはずの処理まで同率で倍増している必要は
   ないはずだが、今回計測した5つの関数はいずれも広義の
   「SteamPad/Steam Input経路」に属するため、この仮説だけでは
   まだ棄却できない。
3. 入力値自体が異常なスケール・頻度になった: `CONFIRMED`（否定的）
   — `[Root26Phase2]`の`x`/`y`の値自体は他のセッションと同程度の
   範囲（例: LSTICK`x=0.24`等）であり、値のスケールが異常に
   なった形跡はない。**頻度**が変化したのであって、**値**が
   異常になったのではない。
4. Steam Inputとは別に、ゲーム全体のsimulation/update速度が変化: `HYPOTHESIS`
   （最も説明力が高い）。ユーザーの直接観察（「動きが超高速化」＝
   キャラクター/カメラ等の実際のゲーム内動作が視覚的に倍速化）は、
   我々のプローブ（Steam Input関連の呼び出しのみ計測）の範囲外の
   独立した証拠であり、これは「Steam Input周りだけでなく、
   ゲームの基本更新ループ自体（`dds3KernelMain.
   m_dds3KernelMainLoop()`等）が2倍の頻度で実行されるように
   なった」ことを示唆する。34.2(a)節で確認済みの、サードパーティ
   Mod「Nocturne Framerate Mod」
   （`Nocturne_Graphics_Configurator.NocturneGraphicsConfigurator.
   OnFixedUpdate()`）が独自に`m_dds3KernelMainLoop()`を追加実行
   している構造と関連している可能性がある（例:
   このModが内部で参照しているフレームレート/タイミング関連の
   Steam APIの状態が、`SteamInput.Shutdown()`/`Init()`によって
   意図せず変化し、Mod側が「もう1回実行すべき」と誤判定する
   ようになった、等）。ただし`m_dds3KernelMainLoop()`自体の
   呼び出し回数やこのサードパーティModの内部状態は今回
   直接計測しておらず、`UNRESOLVED`のまま。

### 42.4 今後の方針（ユーザー指示により凍結中）

- **F10・F9とも今後使用禁止**（ユーザー指示）。`SteamInput.
  Shutdown()`/`Init()`単独呼び出しは安全な自動復旧策として
  **採用しない**。
- Phase 4B（`Shutdown`→`Init`→`UpdateConnectedControllers`の
  追加PoC）は、この副作用の原因が十分理解されるまで**保留**。
- 今回の発見（Guide/Overlay経由の`Reset→ReStart→Shutdown→Init`
  では発生しない副作用が、MODから直接`Shutdown→Init`のみを
  呼んだ場合には発生した）は、**実行コンテキストまたは
  タイミングが両者で同等ではない**ことを強く示唆する
  （`HYPOTHESIS`、43節以降で扱う）。

## 43. 倍速化の原因切り分け: サードパーティ「Nocturne Framerate Mod」の静的解析（2026-09-08）

Phase 4Bには進まず、ユーザー指示により42.2節の倍速化現象の
切り分けを最優先で実施した。F10は使用していない（静的解析のみ）。

### 43.1 重要な前提: このMODは通常の.NETアセンブリ（IL2CPPではない）

`Nocturne Framerate Mod.dll`はMelonLoader mod本体であり、IL2CPPの
AOTコンパイル対象ではない**通常の.NETアセンブリ**である。したがって
本調査で繰り返し問題になってきた「ILスタブのみで実体はnative」
という制約が**存在しない** - Cecilで読んだIL命令列がそのまま
実際のロジックである。これにより本節の解析はすべて高い確信度で
`CONFIRMED`である。

### 43.2 `OnFixedUpdate()`の構造（CONFIRMED、決定的）

`Nocturne_Graphics_Configurator.NocturneGraphicsConfigurator.
OnFixedUpdate()`の実IL（181命令）を読んだ結果、以下の構造が
判明した（擬似コード化）:

```csharp
void OnFixedUpdate() {
    if (フォーカス喪失等の条件) return;
    if (kernel != null && kernel.isActiveAndEnabled && kernel.get_initflag()) {
        gamelogicrun = true;
        ...
        InterpolationStage1(true);
        ForceExtraLoopCall = false;      // <-- 毎回ここでリセット
        ForceInterpolationOff = false;
        ...
        m_dds3KernelMainLoop();          // <-- 1回目、常に無条件

        (カメラのイベント遷移に応じたCatchup()呼び出し、
         KernelMainLoop回数とは無関係)

        if (ForceExtraLoopCall) {        // <-- このOnFixedUpdate内で
                                          //     真になっていれば
            m_dds3KernelMainLoop();      // <-- 2回目！
            Thread.Sleep(33);
        }

        InterpolationStage1(false);
        ...
        gamelogicrun = false;
    }
}
```

`CONFIRMED`: `m_dds3KernelMainLoop()`は`OnFixedUpdate()`1回につき
**1回（無条件）または2回（`ForceExtraLoopCall`が真の場合）**呼ばれる
構造になっている。`ForceExtraLoopCall`は**毎回のOnFixedUpdate冒頭で
必ず`false`にリセットされる**ため、2回目の呼び出しが発生するには、
**同一のOnFixedUpdate呼び出し内で**（1回目の`m_dds3KernelMainLoop()`
呼び出しを含むどこかで）このフラグが`true`にセットされる必要がある。

### 43.3 `ForceExtraLoopCall`をセットする経路（CONFIRMED）

```
NocturneGraphicsConfigurator.CallAhead() { ForceExtraLoopCall = true; }
  ← 唯一の呼び出し元:
NocturneGraphicsConfigurator.OutOfPlaceFix.Postfix() {
    CallAhead();
    Catchup();   // ForceInterpolationOff = true
}
```

`OutOfPlaceFix`はHarmonyの`TargetMethods()`パターン（reflectionで
動的にターゲットを収集するPostfixパッチ）であり、以下のメソッド群
すべてにこの`Postfix()`（＝`CallAhead()`+`Catchup()`）をフックして
いることが、IL内の文字列リテラルから直接確認できた（`CONFIRMED`）:

- 固定名: `fldAutoMapSeqEnd`, `fldAutoMapSeqStart`,
  `fldTitleMiniStart2`
- あるType（型Aとする）内で、メソッド名が`"Cut"`を含み**かつ**
  `"Init"`を含ま**ず**、`"End"`を含む全メソッド
- 別のType（型B）内で、メソッド名に`"_Set"`を含む全メソッド
- 別のType（型C）内で、メソッド名に`"_Init"`を含む全メソッド
- 別のType（型D）内で、メソッド名が`"PopPosition"`または
  `"PopRotate"`である全メソッド
- 別のType（型E、型Cと同一の可能性あり）内で、メソッド名に
  `"_Init"`を含む全メソッド

（各Type A〜Eの具体的な型名はreflectionで`Type.
GetTypeFromHandle`経由で解決されており、IL上の文字列としては
現れないため今回は特定していない - `UNRESOLVED`）

`CONFIRMED`: これらの命名パターン（`PopPosition`/`PopRotate`/
`_Set`/`_Init`/`Cut...End`）は、本調査で繰り返し見てきたゲーム内部
オブジェクト（`dds3Basic_t`等）のtransformスタック操作・シーン
遷移イベントに典型的な命名規則であり、Steam Input関連のメソッドで
**はない**（`GetAnalogActionData`/`SteamPadSet`等の名前はこの
パターンに一致しない）。

### 43.4 倍速化メカニズムの結論（HYPOTHESIS、説明力が非常に高い）

以上を総合すると、以下のシナリオが構造的に成立する:

**もし`m_dds3KernelMainLoop()`自身の通常の毎フレーム処理が、
上記でHarmonyフックされているいずれかのメソッド
（`PopPosition`/`PopRotate`/`_Set`/`_Init`等）を呼び出す構造に
なっているなら**、`OnFixedUpdate()`の1回目の`m_dds3KernelMainLoop()`
呼び出しの中で`ForceExtraLoopCall`が`true`にセットされ、**同じ
`OnFixedUpdate()`呼び出し内**で即座に2回目の
`m_dds3KernelMainLoop()`が発生する。もしこの条件が**毎フレーム
成立し続ける**なら（＝`m_dds3KernelMainLoop()`の通常処理が
恒常的にこれらのメソッドのどれかを呼ぶ状態になっている場合）、
42.2節で観測された「一度発生したら元に戻らない持続的な倍速化」を
**完全に説明できる**。

`UNRESOLVED`（重要）: なぜF10（`SteamInput.Shutdown()`/`Init()`）
実行後にこの条件が「毎フレーム成立する」状態に切り替わったのかは
未解明。Guide/Overlay経由の`ResetController`テストではこの副作用が
一度も観測されていない（32章・34章・37章・40章、いずれも倍速化の
報告なし）ことから、**MODから直接`Shutdown()`/`Init()`を呼ぶ実行
コンテキスト・タイミングが、Guide/Overlay経由の場合と何らかの形で
異なる**ことが示唆される（`HYPOTHESIS`、ユーザーが42.4節で既に
指摘済み）。具体的には、F10のキー入力処理自体が
`OnFixedUpdate()`の外側・別スレッド・別タイミングで発生し、
「フォーカス喪失」や「カメライベント遷移」に類する状態を誘発した
可能性等が考えられるが、いずれも未検証。

### 43.5 実装: 将来の検証用read-onlyカウンタ（今回はテストしない）

ユーザー指示により、将来的な検証（F10を使わない自然発生的な
トリガーでの再現時など）に備えて、`m_dds3KernelMainLoop()`と
`NocturneGraphicsConfigurator.OnFixedUpdate()`のcall-countを
観測するHarmony Prefixカウンタ（`src/
Root26KernelLoopFrequencyProbe.cs`）を実装した。2秒間隔で両者の
累積呼び出し数を`[Root26KernelLoop]`タグでHEARTBEATログする。
サードパーティMod（`Nocturne Framerate Mod.dll`）が不在/変更されて
いる場合でも、このMod自体の初期化が失敗しないよう、該当パッチの
登録は`try/catch`で保護している。

**今回はF10を使用しないため、このカウンタでの実機検証は行って
いない。** 次にこの倍速化現象を確認する自然な機会（別の要因で
再現した場合等）があれば、このカウンタで`m_dds3KernelMainLoop`と
`OnFixedUpdate`それぞれの呼び出し頻度を直接比較できる。

行動変更・手動API呼び出し・F9/F10・write操作は一切なし。すべて
Harmony Prefixによる観測のみ。

### 43.6 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   0a28a6fe9e7561ddc12bab00eaff5aa2b38bcf60dfa5bb38171d6081054efb47
SHA-256 (deployed): 0a28a6fe9e7561ddc12bab00eaff5aa2b38bcf60dfa5bb38171d6081054efb47
  → 一致
```

## 44. Phase 5: Steam Overlay activation callbackの発見（2026-09-08、静的解析で決定的な結果）

Phase 4Bには進まず、ユーザー指示により「Guide/Overlay経路にのみ
存在する状態遷移」の特定を最優先で実施した。

### 44.1 `dds3DefaultMain.OnGameOverlayActivated(GameOverlayActivated_t)`の発見（CONFIRMED）

全42アセンブリを対象に、メソッドの**引数の型**が
`GameOverlayActivated_t`/`GamepadTextInputDismissed_t`に一致する
メソッドを検索した結果、Assembly-CSharp.dll内に以下3件が
見つかった:

- **`dds3DefaultMain::OnGameOverlayActivated(GameOverlayActivated_t)`**
- `SteamIME::OnGamepadTextInputDissmissed(GamepadTextInputDismissed_t)`
- `SteamInputUtil::OnGamepadTextInputDismissed(GamepadTextInputDismissed_t)`
  （33章で既出）

デプロイ済みinteropアセンブリで確認したところ、
`OnGameOverlayActivated`はSteamworks.NETの標準的な
**Steam Callback登録パターン**（`Callback<GameOverlayActivated_t>`
型の静的プロパティ`OnGameOverlayActivatedCallback`を持つ）に
従っており、`Il2CppSteamworks.GameOverlayActivated_t`構造体は
`public byte m_bActive`フィールドを持つ（Overlayが有効=1/
無効=0）。

### 44.2 native body完全decompile: `ResetController()`を直接呼んでいた（CONFIRMED、決定的）

RVA `0x22D2DE0`（Length `0x1AF`=431バイト）を35章と同じ
exact-bounds手法でdecompileした結果:

```c
void OnGameOverlayActivated(GameOverlayActivated_t pCallback /* param_2 = m_bActive */) {
    (lazy init boilerplate)
    (汎用invoke的イベント通知、35/38章と同一パターン)

    isOverlayActive = (m_bActive == 1);   // 静的フィールドへ保存
                                            // (_DAT_182e48b50->0xb8+8)

    (再度、汎用invoke的イベント通知)

    if (isOverlayActive == false) {        // <-- Overlayが閉じられた
        instance = SteamInputUtil.instance();  // 条件C
        if (instance == null) throw NRE;

        SteamInputUtil.ResetController(instance, 0);   // <<< 直接呼び出し！
    }

    if (isOverlayActive == true) {         // Overlayが開かれた
        dds3DefaultMain.PauseResume(this, false, 0);
    }

    if (isOverlayActive == false && (何らかのfloat条件 == 0.0)) {
        dds3DefaultMain.PauseResume(this, true, 0);
    }
}
```

`func_0x1822d2f90`はRVA完全一致で**`dds3DefaultMain.
PauseResume(System.Boolean)`**（RVA `0x22D2F90`）と`CONFIRMED`
できた。これは33.2節で確認済みの`dds3DefaultMain.
OnApplicationFocus(bool)`が自身の末尾で呼んでいたのと**全く同じ
関数**である（`OnApplicationFocus`は`PauseResume(hasFocus)`を、
`OnGameOverlayActivated`は`PauseResume(!isOverlayActive相当)`を
呼ぶ、という対応関係）。

**結論（CONFIRMED、本調査の中核的発見）**: `SteamInputUtil.
ResetController()`のGuide/Overlay起点での真の呼び出し元は
**`dds3DefaultMain.OnGameOverlayActivated(GameOverlayActivated_t)`
であり、Steam Overlayが閉じられた（`m_bActive: 1 -> 0`）瞬間に
無条件で呼ばれる**。これは34.2(b)節で「.NET StackTraceが
il2cpp→managed境界で途切れ、呼び出し元が特定できなかった」
という観測と完全に整合する - Steamworksの`Callback<T>`機構は
Valve側のC++ネイティブコールバックディスパッチから直接IL2CPPの
native→managedトランポリンを呼ぶため、Unity/MelonLoaderの
通常のイベントディスパッチ経路（`il2cpp_runtime_invoke`ヘルパー
経由）を通らず、.NET `StackTrace`から見て「呼び出し元不明の
native起点」に見える。

### 44.3 34.3節/38.6節への回答（更新）

- **34.3節「`ResetController`が呼ばれれば必ず`SteamControllerReStart`
  も呼ばれる」の親カウント**: `OnGameOverlayActivated`が
  `ResetController()`を呼ぶ経路が`CONFIRMED`されたことで、
  Guide/Overlay起点の`ResetController()`呼び出しの発生源が
  完全に説明された。
- **38.6節「815ms〜1.3秒の遅延はValve側の非同期処理」仮説**:
  今回発見された経路自体はここに新情報を加えないが、
  `ResetController()`の呼び出しタイミングが「Steam Overlayが
  実際に閉じられ、Steamクライアント側でOverlay終了処理が完了した
  後」であることが明確になった - つまりGuide/Overlay経路の
  `ResetController()`は、Steamクライアント側のOverlay終了処理
  **後**に発火するという時系列上の制約があり、手動`Shutdown()`/
  `Init()`（Phase 4、F10）のような「任意のタイミングでの直接
  呼び出し」とは実行コンテキストが本質的に異なることが
  `CONFIRMED`された。

### 44.4 Phase 4の倍速化副作用との関連（HYPOTHESIS、説明力が高い）

`OnGameOverlayActivated`は`ResetController()`呼び出しに加えて
`PauseResume(bool)`も呼ぶが、Phase 4（F10による直接
`Shutdown()`/`Init()`呼び出し）は`PauseResume()`を一切経由
しない。43章で判明したFramerate Modの`OnFixedUpdate()`は
`Application.isFocused`や`kernel.get_initflag()`等の状態を
チェックしており、`PauseResume()`が内部で操作している状態
（未解析、`UNRESOLVED`）と、Framerate Modが参照する状態との
間に依存関係がある場合、**F10経路が`PauseResume()`を経由しな
かったことが、42章の持続的倍速化の一因になっている可能性が
ある**（`HYPOTHESIS`、`PauseResume()`自体の内部処理は未解析）。

### 44.5 実装: `OnGameOverlayActivated`呼び出しのread-only観測（`src/Root26Phase5OverlayActivatedProbe.cs`）

Harmony Prefixで`dds3DefaultMain.OnGameOverlayActivated`を観測し、
`pCallback.m_bActive`（Overlay有効/無効）と呼び出し回数を
毎回ログする（この呼び出しはOverlay開閉時のみの低頻度イベントで
あり、他のper-frameプローブと異なり全件ログしても問題ない）。
引数・戻り値への書き込みは一切ない。手動でのSteam API呼び出し・
F9/F10・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`の手動実行は一切なし。

### 44.6 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   825c3b2223451c4bf36a6ccc30de8e652ff12a34c2b75a92e977ad9b40a53e61
SHA-256 (deployed): 825c3b2223451c4bf36a6ccc30de8e652ff12a34c2b75a92e977ad9b40a53e61
  → 一致
```

### 44.7 次の実機テスト（次のステップ、ユーザー実施待ち）

通常手順（起動→探索でDEAD確認→Guide 1回→Steam Overlay→戻る→
LIVE確認→ゲーム終了、ログアーカイブ不要）。今回は`[Root26Phase5]`
の`OVERLAY-ACTIVATED-CALLBACK`ログ（`m_bActive`の1→0遷移）を、
`[Root26SteamState]`の`ResetController`/`SteamControllerReStart`
呼び出しログ、`[Root26Phase2]`のRSTICK状態変化と同一タイムライン
上で突き合わせ、「Overlay閉鎖→ResetController→...→RSTICK復活」の
順序を直接確認する。F9/F10・手動API呼び出しは行わない。

## 45. Phase 5 実機テスト結果: 因果連鎖の直接確認（2026-09-08 00:43台）

ログ: `docs/research/ROOT26_POST_REBOOT_EVIDENCE_20260907/melonloader/Latest.log_phase5_20260908.log`

### 45.1 タイムライン（すべて同一ログ、`[Root26Phase5]`と
既存プローブを直接突き合わせ）

| 時刻 | 出来事 |
|---|---|
| 00:43:30.343 | `[Root26Phase5]` `OVERLAY-ACTIVATED-CALLBACK` `m_bActive=1`（**Overlay開いた**） |
| 00:43:30.430 | `Root26NativePoll` `SESSION-SUMMARY`（exploration終了、finalState=DEAD） |
| 00:43:31.325 | RSTICK（handle...676）が`INACTIVE`（`bActive=0`）に - Overlay表示中 |
| 00:43:31.603 | RSTICK（handle...676）が`ACTIVE-ZERO`（`bActive=1`）に復帰 |
| **00:43:31.734〜.735** | `[Root26Phase5]` `OVERLAY-ACTIVATED-CALLBACK` `m_bActive=0`（**Overlay閉じた**、count=3,4） |
| **00:43:31.735〜.737** | `ResetController()` count=1,2 → `SteamControllerReStart()` count=3,4（**Overlay閉鎖コールバックの直後、1〜2ms以内**） |
| 00:43:31.749 | `Root26NativePoll` `SESSION-START`（session 2開始） |
| 00:43:32.354 | RSTICK（handle...660、2台目のcontroller handle）が`ACTIVE-NONZERO`（Resetから約619ms後） |
| 00:43:32.744 | `Root26NativePoll` `STATE-TRANSITION -> LIVE`（native側確認、Resetから約1009ms後） |

### 45.2 判定: 因果連鎖の直接確認（CONFIRMED）

**Overlay閉鎖コールバック（`m_bActive: 1->0`）の発生から、
`ResetController()`の発火まで、わずか1〜2msしかない。** これは
44.2節で静的に確認した「`OnGameOverlayActivated`が
`isOverlayActive==false`の場合に`ResetController()`を無条件で
直接呼ぶ」という構造と完全に一致し、**同一のnative関数呼び出し
連鎖内で起きている**ことを示す（呼び出しが非同期にディスパッチ
されているのではなく、コールバックハンドラの実行そのものの中で
同期的に発生している）。

**結論（CONFIRMED）**: 本調査全体を通じて追ってきた因果連鎖が、
静的解析（44章）と実機ログの直接相関（本章）の両方で確定した:

```text
Steam Overlay閉鎖
        ↓ (~0-1ms)
dds3DefaultMain.OnGameOverlayActivated(m_bActive=0)
        ↓ (同一関数呼び出し内、無条件)
SteamInputUtil.ResetController()
        ↓ (無条件、35.2節でCONFIRMED)
  ├─ SteamPad.SteamControllerReStart()
  │     ↓ (無条件、35.3節でCONFIRMED)
  │   Steamworks.SteamInput.Shutdown() → Init()
  └─ SteamPad.UpdateConnectedControllers()
        ↓ (約0.6〜1.3秒、GameAssembly側に追加処理なし、40章でCONFIRMED)
RSTICK: GetAnalogActionData が bActive=true 維持のまま x/y を配信開始
```

Guide/Overlayという操作そのものが本質だったのではなく、
**「Steam Overlayが閉じられる」というイベントに対してゲーム
自身が登録しているコールバックが、無条件に`ResetController()`を
呼ぶ**という、ゲーム内部の明示的な設計であったことが判明した。

### 45.3 副次的観測: 2つのcontroller handleが存在した（UNRESOLVED、本筋と無関係の可能性）

本セッションでは`handle=19680159496504676`と
`handle=91728467815138660`の2つのcontroller handleが観測された
（前回までのテストはすべて単一handleのみ）。RSTICK復活は2台目の
handle（`...660`）側で先に観測された。これがcontroller再接続
（何らかの理由で新しいhandleが割り当てられた）によるものか、
2つの物理デバイスが同時に認識されていたのかは未確認
（`UNRESOLVED`）。今回の主要な結論（Overlay閉鎖→
`ResetController`の直接呼び出し）には影響しない。

### 45.4 本調査の到達点（まとめ）

Root-26調査は、当初の疑問
「なぜRight Stickが起動直後はDEADで、Guide/Overlay後にLIVEに
なるのか」に対し、以下をすべて`CONFIRMED`レベルで解明した:

1. DEAD状態の実体は、`GetAnalogActionData`（Steam Input公開API）が
   `bActive=true`・正しい`eMode`を返しながら`x=y=0`を返し続ける
   という、Steam Input内部のanalog action data配信境界の問題で
   あった（37章）。
2. この配信状態は、Steam Overlayが閉じられた際に発生する
   `OnGameOverlayActivated`コールバック処理単位（`PauseResume()`
   呼び出し＋`SteamInputUtil.ResetController()`呼び出しを含む、
   44〜45章）と時間的に強く相関しており解消される。ただし
   `ResetController()`単独では復活しないことがF9での過去テストで
   既に確認されているため（42章）、**`OnGameOverlayActivated`
   コールバック処理単位全体（`PauseResume()`等、`ResetController`
   以外の処理を含む）が復旧に寄与している可能性が高い**という
   段階にとどめる（`HYPOTHESIS`） - 「`ResetController`経路単独が
   十分条件」とはまだ確定していない。
3. Controller handle・Analog action handle自体は一貫して不変
   （33〜34章）であり、「handleの誤り」ではなかった。
4. `ResetController()`/`SteamControllerReStart()`/
   `UpdateConnectedControllers()`はいずれも同期処理であり、
   復活までの0.6〜1.3秒の遅延はGameAssembly.dll側の追加処理では
   説明できない（40章） - Steamクライアント側（範囲外）の
   非同期処理によるものと考えられる（`HYPOTHESIS`のまま）。
5. `SteamInput.Shutdown()`/`Init()`を`ResetController()`経由（＝
   Overlay閉鎖コールバック経由）ではなく直接単独で呼ぶと、
   RSTICKは復活せず、さらに別の重大な副作用（ゲーム更新頻度の
   持続的な倍速化、42〜43章）を引き起こすことが判明した - これは
   実行コンテキスト・タイミング（`PauseResume()`等の付随処理の
   有無、44.4節）が本質的に異なるためと考えられる。

残る`UNRESOLVED`事項（十分に安全な自動復旧策の設計に必要な
情報は概ね揃った）:

- なぜ起動直後は`bActive=true`のまま`x=y=0`になるのか、という
  Steam Input内部の根本原因自体（Valve側、範囲外）。
- 0.6〜1.3秒の遅延の正確な機序（Steam側非同期処理という
  `HYPOTHESIS`のまま）。
- `PauseResume()`の内部処理の詳細。

## 46. Phase 6: `PauseResume(bool)`完全解析（2026-09-08、静的解析）

### 46.1 重要な訂正: 実際の呼び出し順序は`ResetController`→`PauseResume`（ユーザー想定と逆）

44.2節の`OnGameOverlayActivated`のdecompile結果を精査すると、
実際のコード順序は以下だった（`CONFIRMED`）:

```c
isOverlayActive = (m_bActive == 1);
(invoke通知)
if (isOverlayActive == false) {          // Overlay閉鎖
    instance = SteamInputUtil.instance();
    ResetController(instance, 0);         // <-- 1. 先に呼ばれる
}
if (isOverlayActive == true) {            // Overlay開放
    PauseResume(this, false, 0);
}
if (isOverlayActive == false && 条件) {   // Overlay閉鎖・条件付き
    PauseResume(this, true, 0);           // <-- 2. Resetの後に呼ばれる
}
```

ユーザーが想定していた`OnGameOverlayActivated → PauseResume →
ResetController`という順序ではなく、**`ResetController()`が先に
無条件で呼ばれ、`PauseResume(true)`はその後に、しかも条件付きで
呼ばれる**（`CONFIRMED`）。45.1節の実機ログ（Overlay閉鎖
コールバックの1〜2ms後に`ResetController`が発火）とも整合する。

### 46.2 `PauseResume(bool)`の完全な内容（CONFIRMED、569バイト全体を解析）

`param_2==false`（Overlay**開放**時に呼ばれる、"PAUSE"側）:

```c
(invoke通知、静的値=_DAT_182e33a50)
UnityEngine.Time.set_timeScale(0.0f);        // ゲーム内時間を停止
UnityEngine.AudioListener.set_pause(true);   // オーディオを一時停止
dds3KernelMain.dds3PauseOn();                // ゲームカーネルを一時停止
mviManager.MoviePause(true);                 // ムービー再生を一時停止
if (SteamInputUtil.instance() != null) return;
```

`param_2==true`（Overlay**閉鎖**時、条件付きで呼ばれる、"RESUME"側）:

```c
(invoke通知、静的値=_DAT_182e5d120、PAUSE側とは異なる値)
instance = SteamInputUtil.instance();
if (instance != null) {
    if (instance->PadConnectDiff(+0x38) >= 0) {   // 38.2節で
                                                    // 確認済みの
                                                    // フィールド
        cVar2 = func_0x1822b5b30(0);   // dds3ConfigKeyBoardSteam
                                         // 領域内の未特定関数
                                         // (関数境界がずれており
                                         // 正確な識別は未確定、
                                         // UNRESOLVED)
        if (cVar2 == false) {
            UnityEngine.Time.set_timeScale(1.0f相当の定数);  // 時間再開
            UnityEngine.AudioListener.set_pause(false);       // 音声再開
            dds3KernelMain.dds3PauseOff();                     // カーネル再開
            mviManager.MoviePause(false);                      // ムービー再開

            eType1 = SteamInputUtil.GetControllerType(true);
            bVar6 = (eType1==5 && instance->field_0x44==0);

            if (instance->field_0x50 != null) {
                SteamPad.CheckProConButtonLayout(instance->field_0x50, 0);
            }

            eType2 = SteamInputUtil.GetControllerType(true);  // 再度呼ぶ
            bVar7 = (eType2==5 && instance->field_0x44==0);

            if (bVar6 != bVar7) {   // CheckProConButtonLayoutの結果
                                     // フラグが変化した場合のみ
                instance->field_0x68 = 7;
                (別クラスの静的フィールドへ1を書き込み - UI関連の
                 再描画フラグと推測、UNRESOLVED)
            }
        }
        // cVar2==trueの場合は再開処理を一切行わずreturn
    }
    // PadConnectDiff < 0（コントローラ数が減少）の場合も
    // 再開処理を一切行わずreturn
}
```

### 46.3 判定（ユーザー確認項目への回答）

| 確認項目 | 結果 |
|---|---|
| Unity `Time.timeScale` | `CONFIRMED`: PAUSE側で`0.0`、RESUME側で`1.0`相当の定数へ設定。 |
| `Application.isFocused` | このメソッド自体では参照していない（`OnApplicationFocus`側の呼び出し元で既に使われている、33章）。 |
| kernel pause/init flag | `CONFIRMED`: `dds3KernelMain.dds3PauseOn()`/`dds3PauseOff()`を直接呼ぶ。 |
| input enable/disable state | `SteamInputUtil.GetControllerType()`と`SteamPad.CheckProConButtonLayout()`は呼ぶが、**Analog action data配信自体には触れていない**（`CONFIRMED`、後述46.4）。 |
| camera / field / menu state | 直接の参照は見当たらない。 |
| Steam Input関連state | `CONFIRMED`: `SteamInputUtil.instance()`と`PadConnectDiff`（+0x38）を参照するが、これは38.2節で確認済みの「コントローラ接続数差分」チェックのみ。 |
| Framerate Modが参照する状態との接点 | `Time.timeScale`はUnityの標準APIであり、Framerate Modも独自のフレーム制御ロジックを持つため、理論上は干渉し得るが直接の呼び出し関係は確認できていない（`UNRESOLVED`）。 |
| `OnApplicationFocus`から呼ばれる場合との違い | 同じ`PauseResume(bool)`を共有しているため処理内容は同一（`CONFIRMED`）。 |
| 呼び出し順序 | `CONFIRMED`（46.1節）: `ResetController()`が先、`PauseResume(true)`は後、かつ条件付き。 |

**1. PauseResumeはResetControllerより前か後か** → **後**（`CONFIRMED`、
ユーザー想定と逆）。

**2. Overlay ONとOFFで渡されるbool値** → ON時`false`（PAUSE）、
OFF時`true`（RESUME、ただし条件付き呼び出し）。

**3. ResetControllerの安全性/Right Stick復旧に必要そうな状態変更が
あるか** → **`CONFIRMED`（否定的）: 見当たらない。** `PauseResume()`
はゲーム全体のtimeScale/オーディオ/ムービー/カーネル一時停止の
解除と、Pro Controllerボタンレイアウトのアイコン再描画フラグ
更新を行うのみで、Steam InputのAnalog action data配信状態に
直接影響する処理は含まれていない。また`ResetController()`の方が
**先に**呼ばれるため、そもそも`PauseResume()`が`ResetController()`
の前提条件になっている構造でもない。

**4. Phase4の倍速化を説明し得る状態変更があるか** → `HYPOTHESIS`
（弱まった）: `Time.timeScale`の`0.0`→`1.0`復元処理は直接Framerate
Modのロジックとは無関係に見えるが、Phase 4（F10）はこの
`timeScale`復元処理自体を経由しなかった可能性がある
（F10は`Shutdown()`/`Init()`のみを直接呼び、`PauseResume(true)`は
一切経由していない）。ただし43章で確認したFramerate Modの
`ForceExtraLoopCall`機構は`timeScale`を直接参照していない
（`OutOfPlaceFix`がフックする対象は`PopPosition`/`_Set`/`_Init`
系のゲーム内部メソッドであり、`Time.timeScale`ではない）ため、
この接点は薄い（`UNRESOLVED`のまま）。

### 46.4 結論: `PauseResume()`は「安全な復旧の欠けているピース」ではなさそう（HYPOTHESIS、重要）

`PauseResume()`の全内容を確認した結果、**RSTICKのanalog action
data配信を直接左右する処理は一切含まれていなかった**。さらに、
実際の呼び出し順序は`ResetController()`が先であるため、
「`PauseResume`が`ResetController`を安全にする前処理」という
仮説の構造自体も成立しない。

これにより、Guide/Overlay経由の`ResetController()`呼び出しが
F9単独呼び出しと異なり実際に機能する理由は、`OnGameOverlayActivated`
コールバック内のGameAssembly.dll側の処理（`ResetController`
自体と`PauseResume`）には見当たらず、**Steamクライアント側
（GameAssembly.dllの範囲外）の、実際のOverlay open/closeライフ
サイクルに紐づいた状態遷移**に起因する可能性が、今回の解析で
むしろ強まった（`HYPOTHESIS`）。

この結果は、ユーザーが提示した3段階の方針のうち、**選択肢1
（ゲーム本来のOverlay-close handler相当を内部的に安全に呼べる）
の実現可能性を下げ、選択肢3（Broker fallbackの維持）の相対的な
優先度を上げる**方向の材料である。ただし、これは
`PauseResume()`単体の解析結果に基づく判断であり、
`OnGameOverlayActivated`内のまだ未解析の要素（46.2節の
`func_0x1822b5b30`の正体、`invoke通知`の実際の配信先）が
関与している可能性は完全には排除できない（`UNRESOLVED`）。

### 46.5 PoC候補（ユーザー指定の3段階、この解析結果を踏まえて再評価）

1. **最良（ゲーム本来のOverlay-close handler相当を安全に呼ぶ）**:
   `OnGameOverlayActivated(false)`相当を直接呼び出すことは技術的に
   可能（Harmonyでの直接呼び出し、または`GameOverlayActivated_t`
   構造体を構築して渡す）だが、これは**本物のOverlay
   close`Callback<T>`ディスパッチを偽装する**ことになり、
   Steamworks内部状態との整合性が保証されない。今回の解析で
   `PauseResume`に「真に必要な前処理」が見つからなかったため、
   この方式の優位性は当初期待したほど高くない
   （`HYPOTHESIS`、後退）。
2. **次善（`ResetController`相当の処理のみ再現）**: 既にPhase 4で
   `Shutdown`/`Init`単独が失敗し、深刻な副作用も確認済み。
   `UpdateConnectedControllers()`も含めた完全な`ResetController()`
   自体を直接呼ぶ場合、45.4節の留保（F9でのResetController単独
   テストは`SteamControllerReStart`/`Shutdown`/`Init`は経由するが
   `UpdateConnectedControllers`の効果を含めた完全な形では
   なかった可能性がある点に注意）を踏まえ、まだ試されていない
   組み合わせとして検討の余地はある。
3. **Broker/SDL fallbackを正式maintainする**: 今回の解析結果は、
   この選択肢の相対的な妥当性を高める材料となった。

**推奨（ユーザー判断待ち）**: 次に試す価値が最も高いのは、
実は45.4節で触れた「Phase 4Bとして保留していた`Shutdown→Init→
UpdateConnectedControllers`の組み合わせ」である可能性が、
今回`PauseResume`が「決め手ではない」と分かったことで相対的に
上がった。ただしこれは新たな手動API呼び出し実験であり、
ユーザーの明示的な承認が必要。

## 47. Phase 7: Steam Overlay lifecycle中のSteam/Steam Input側状態変化の探索（2026-09-08、静的解析）

Phase 4Bは実施せず、ユーザー指示によりOverlay lifecycle中の
Steam側状態遷移の特定を最優先で実施した。F9/F10・手動API呼び出しは
一切行っていない（静的解析のみ）。

### 47.1 Steam callbackポンプの発見: `SteamManager.Update()` → `SteamAPI.RunCallbacks()`（CONFIRMED、決定的）

`SteamManager.Update()`（RVA `0x25FF1E0`、わずか14バイト）を
exact-bounds手法でdecompileした結果:

```
CMP byte [this+0x18], 0      ; this.m_bInitialized相当のフラグ確認
JZ <return>                   ; 未初期化なら何もしない
XOR ECX,ECX
JMP 0x18289d250                ; tail-jump
RET
```

tail-jump先`0x18289d250`はRVA完全一致で
**`Steamworks.SteamAPI.RunCallbacks()`**（RVA `0x289D250`）と
`CONFIRMED`できた。

**結論（CONFIRMED、本調査の因果連鎖の最後の空白を埋める発見）**:
`SteamManager`はUnityの標準`MonoBehaviour.Update()`
（Unity自身のエンジン駆動、MelonLoaderのイベントプロキシを
経由しない）として毎フレーム`SteamAPI.RunCallbacks()`を呼んで
おり、これがSteamworksのコールバックポンプそのものである。
34.2(b)節で「`ResetController()`の呼び出し元が.NET
`StackTrace`では境界で途切れ特定できなかった」という観測は、
これで完全に説明できる:

```text
SteamManager.Update()  ← Unity engineが毎フレーム直接呼ぶ
                          (MelonLoaderのFixedUpdateプロキシを
                           経由しないため、.NET StackTraceには
                           現れない)
        ↓ tail-call
Steamworks.SteamAPI.RunCallbacks()
        ↓ (Steam側C++ネイティブのCallbackDispatcher機構、
           範囲外)
dds3DefaultMain.OnGameOverlayActivated(GameOverlayActivated_t)
        ↓ (44章、無条件)
SteamInputUtil.ResetController()
```

### 47.2 `ActivateActionSet()`は本ゲーム内のどこからも呼ばれていない（CONFIRMED、否定的）

`Steamworks.SteamInput.ActivateActionSet(InputHandle_t,
InputActionSetHandle_t)`（VA `0x1828A0820`）への直接E8呼び出しを、
既知の全managedメソッド本体（31692件、全42アセンブリ）に対して
バイトスキャンした結果、**呼び出し元は0件**だった。

**結論（CONFIRMED）**: このゲームは`ActivateActionSet()`を
一度も呼んでいない。本調査を通じて`Current`（ActionSet index）が
常に`0`のまま変化しなかった（33〜34章）のは、ゲームが明示的に
ActionSetを切り替えていないためであり、Overlay lifecycle中に
ActionSetが切り替わっている可能性は排除できる。

### 47.3 `GetCurrentActionSet`相当のAPIは、このSteamworks.NETバインディングに存在しない（CONFIRMED、否定的）

全42アセンブリを検索したが、`GetCurrentActionSet`という名前の
メソッドは一件も見つからなかった。本ゲームが使用している
Steamworks.NETラッパー版には、この機能自体が含まれていない
（`CONFIRMED`、SDKバージョンの制約）。

### 47.4 `OnGameOverlayActivated`本体に、Steam Input抑止/再取得の追加処理は存在しない（CONFIRMED、44.2節の再確認）

44.2節で既に431バイト全体をdecompile済みの
`OnGameOverlayActivated(GameOverlayActivated_t)`を改めて精査した
結果、その全処理は「invoke通知×2」「`isOverlayActive`フラグの
更新」「`ResetController()`呼び出し（Overlay閉鎖時、無条件）」
「`PauseResume()`呼び出し（開放時/条件付き閉鎖時）」のみであり、
`GetConnectedControllers`・`GetInputTypeForHandle`・
`ActivateActionSet`・その他Steam Input APIへの追加の呼び出しは
**一切存在しない**（`CONFIRMED`、否定的）。

**結論（CONFIRMED、重要）**: Phase 5（45.1節）で観測された
「Overlay開放中にRSTICKが一時的に`INACTIVE`になる」現象は、
**GameAssembly.dll側のどのコードにも起因しない**。これは
Steamクライアント自身が、Overlay描画中に`GetAnalogActionData`の
結果を自律的に抑制している（ゲーム側からは一切関知・制御
できない、native/closed-sourceな）挙動であると考えられる
（`HYPOTHESIS`、ただし「ゲーム側にそのような処理が存在しない」
こと自体は`CONFIRMED`）。

### 47.5 runtime probe: 新規実装は不要（既存Phase 2+5の組み合わせで既に取得済み）

ユーザーが要求した「Overlay ON/OFF前後のcontroller handle /
action handle / bActive / eMode / x/y」の観測は、既存の
`Root26Phase2AnalogActionDataProbe`（bActive/eMode/x/y）と
`Root26Phase5OverlayActivatedProbe`（Overlay ON/OFFタイムスタンプ）
の組み合わせで**既に取得済み**である（45.1節のタイムライン）。
新規のruntime probe実装は不要と判断した。

### 47.6 Phase 7の結論: GameAssembly.dll側にこれ以上の候補は見当たらない（CONFIRMED negative）

47.1〜47.4節の否定的所見をまとめると:

- Steam callbackポンプの場所は特定できたが（47.1）、それ自体は
  単なる中継であり、追加の状態変更ロジックを含まない。
- `ActivateActionSet`/`GetCurrentActionSet`はこのゲームでは
  未使用/未提供（47.2〜47.3）。
- `OnGameOverlayActivated`本体にResetController/PauseResume以外の
  Steam Input操作はない（47.4）。
- `PauseResume()`にもRSTICK配信を直接左右する処理はない（46章）。

**GameAssembly.dll側を静的解析する限り、Guide/Overlay経路に
存在し手動`Shutdown()`/`Init()`経路には存在しない「決定的な
追加処理」は見つからなかった。** 残る差分は、実際のSteam
Overlayライフサイクル（Steamクライアント自身のC++実装、
closed-source、範囲外）に起因すると考えるのが、現時点で
最も筋が通る（`HYPOTHESIS`、ただし対抗する具体的な
GameAssembly側候補は本節までで実質的に尽きた）。

ユーザーが事前に示した方針に従い、これ以上GameAssembly.dllを
掘り進めても新たな手がかりが得られる見込みは薄いと判断する。
次の判断（Broker/SDL fallbackの正式化とREADMEへのOverlay開閉
トラブルシューティング記載、あるいは45.4節の
`Shutdown→Init→UpdateConnectedControllers`の未試行の組み合わせを
最後に試すか）は、ユーザーに委ねる。

## 48. Phase 7B: MODから本物のSteam Overlay lifecycleを発生させられるか（2026-09-08、静的解析）

「内部Guide偽装」ではなく「Steamに本物のOverlayを開閉させる」
方向性の実現可能性を、このゲームに実際にバンドルされている
Steamworks.NETラッパーの範囲内で調査した。

### 48.1 Overlayを開くAPI: `SteamFriends.ActivateGameOverlayToStore`のみ存在（CONFIRMED）

全42アセンブリのメタデータを"overlay"というキーワードで
網羅的に検索した結果、Steam Overlayを開く目的で使えるAPIは
**`Steamworks.SteamFriends.ActivateGameOverlayToStore(AppId_t,
EOverlayToStoreFlag)`（RVA `0x28A0750`）ただ1つ**だった
（`CONFIRMED`）。

これは標準的なSteamworks SDKにある`ActivateGameOverlay(string
pchDialog)`（`"friends"`/`"community"`/`"players"`等の汎用
ページを開く）や`ActivateGameOverlayToUser`/
`ActivateGameOverlayToWebPage`/
`ActivateGameOverlayInviteDialog`とは**異なる、Storeページ専用の
限定APIである**（`CONFIRMED`）。このゲームのSteamworks.NET
バインディングには、汎用`ActivateGameOverlay`は**含まれていない**
（`CONFIRMED`、否定的）。`SteamFriends`クラス自体、バインドされて
いるメソッドはこの1つのみだった。

**結論（CONFIRMED、ただし実用上の含意はHYPOTHESIS）**:
`ActivateGameOverlayToStore(GetAppID(), EOverlayToStoreFlag.None)`
を呼べば、**このゲーム自身のStoreページ**を表示するSteam
Overlayが実際に開くと考えられる（Steamworks SDKの標準的な仕様に
基づく推測、本ゲームでの実地未検証）。これは本物のSteam
Overlay起動であり、`GameOverlayActivated_t(m_bActive=true)`
コールバックが本物のSteamクライアントから発行されると期待できる
（`HYPOTHESIS`、実機未検証）。ただし、プレイヤーには
**自ゲームのストアページ**というやや不自然なOverlay画面が
表示される（UX上の副作用、後述48.4）。

### 48.2 Overlayを閉じる正規APIは存在しない（CONFIRMED、否定的）

全メタデータを検索したが、Steam OverlayをプログラムAPIから
明示的に閉じる（dismiss/close）ためのメソッドは**一件も
見つからなかった**。これは本ゲームのバインディングの制約という
より、**Steamworks SDK自体の一般的な仕様**である
（Steam Overlayはユーザー操作によってのみ閉じられる設計であり、
`ISteamFriends`等にプログラム的なclose APIは公式に提供されて
いない、業界一般に知られている制約）。

`CONFIRMED`（本ゲームのバインディング範囲内）: Overlayを閉じる
Steamworks APIは存在しない。

### 48.3 Guide入力をSteamクライアントへ送る公開APIも存在しない（CONFIRMED、否定的）

"Guide"を名前に含むSteamworks APIも、本ゲームのバインディングには
一件も存在しなかった。Steam Input側でのGuideボタン処理は
Steamクライアント内部で完結しており、外部から「Guideが押された」
という通知を注入する公式な経路はない（`CONFIRMED`、否定的）。

### 48.4 実現可能性の評価: Overlayを開くところまでは有望、閉じる部分に課題

ユーザーが提示した理想形（MOD→本物のOverlay ON→lifecycle→
Overlay OFF→本物の`OnGameOverlayActivated(false)`→
`ResetController()`）のうち:

1. **Overlayを開く**: `ActivateGameOverlayToStore`により
   技術的に可能と考えられる（`HYPOTHESIS`、実機未検証）。
2. **本物の`OnGameOverlayActivated(true)`が来る**: Overlayが
   実際に開けば、Steamworksの標準的なコールバック機構
   （44章で確認済み）により自動的に発行されるはずである
   （`HYPOTHESIS`）。
3. **Overlayを閉じる**: **プログラムAPIでは不可能**
   （`CONFIRMED`）。唯一現実的な代替手段は、Steam Overlayが
   グローバルに監視している**Escキー**を合成入力
   （`SendInput`等のOS APIレベル）で送ることである。これは
   Steamworks SDKの公式APIではなく、Steam Overlay UI自体が
   Escキー押下を監視して自身を閉じるという、広く知られた
   Steamクライアントの一般的な挙動に依存する（`HYPOTHESIS`、
   Steam公式の保証された仕様ではない）。
4. **OFF後に本物の`OnGameOverlayActivated(false)`が来る**:
   Overlayが実際に（Escキー経由であっても）閉じれば、これも
   Steamworksの標準コールバック機構により自動発行されるはずである
   （`HYPOTHESIS`）。

**重要な区別（ユーザー指摘通り）**: このEscキー送信は、
「SMT3にGuideボタンが押されたと偽装する」入力偽装とは**性質が
異なる**。SMT3自身のコントローラ入力処理には一切触れず、
Steam Overlay自身のUIが監視しているグローバルなキー入力
（OS/Steamクライアントレベル）を模倣するものである。ただし
「合成キー入力をOSレベルで送る」という手法自体は、本調査で
これまで一貫して避けてきた「read-onlyでない、副作用のある
介入」の新しいカテゴリであり、F9/F10のGetAsyncKeyState型
トリガーとは異なるが、実行には`SendInput`等のWin32 API呼び出しが
必要になる。

### 48.5 まとめ（CONFIRMED / HYPOTHESIS分離）

| 項目 | 判定 |
|---|---|
| Overlayを開く専用API (`ActivateGameOverlayToStore`) が存在する | `CONFIRMED` |
| 汎用`ActivateGameOverlay`はこのバインディングに存在しない | `CONFIRMED`（否定的） |
| Overlayを閉じるSteamworks APIは存在しない | `CONFIRMED`（否定的） |
| Guide入力を注入する公開APIは存在しない | `CONFIRMED`（否定的） |
| `ActivateGameOverlayToStore`実行で本物のOverlayが開き`OnGameOverlayActivated(true)`が発行される | `HYPOTHESIS`（実機未検証） |
| Escキー合成入力でOverlayが閉じ`OnGameOverlayActivated(false)`が発行される | `HYPOTHESIS`（実機未検証、Steam公式仕様ではない一般的挙動に依存） |

**次のステップの選択肢（ユーザー判断待ち、まだ何も実装していない）**:

1. `ActivateGameOverlayToStore`単独をまず実機で試し、本物の
   `OnGameOverlayActivated(true)`が実際に発行されるかを
   `[Root26Phase5]`プローブで確認する（Overlayは開いたまま、
   閉じるのはユーザーが手動で行う）。これはEscキー合成入力を
   一切必要とせず、48.1節の`HYPOTHESIS`部分のみを検証できる
   最小の一歩である。
2. 上記が成功した場合のみ、Escキー合成入力によるOverlay自動
   クローズを追加で検討する（新しいカテゴリの介入のため、
   別途明示的な承認を要する）。
3. Storeページが表示されるUX上の副作用が許容できない場合は、
   この方向自体を断念し、Broker/SDL fallbackへ戻る。

F9/F10・`ResetController`/`ReStart`/`Shutdown`/`Init`の手動呼び出し・
Guide入力偽装・SendInput等はまだ一切実行していない（静的解析のみ）。

## 49. Phase 8: `ActivateGameOverlayToStore`最小PoC実装（2026-09-08）

ユーザー指示（選択肢①）に従い、Overlayを開くAPI単独の実機検証
PoCを実装した。Escキー自動送信は含まない。

### 49.1 実装（`src/Root26Phase8OverlayToStoreOpenPoc.cs`）

- **トリガー**: 新規キーボードホットキーは追加せず、既存の
  `FieldDashPatch.IsExplorationActive`が最初に`true`になってから
  5000ms（5秒）後に自動発火する。
- **1セッション1回のみ**: 真偽値ラッチ（他の処理より先に即座に
  セット、Phase4/Phase4のPoCと同じ設計方針）。
- **AppId**: `Il2Cpp.SteamManager.APPID`（ゲーム自身が既に
  初期化済みのSteamworks AppId）から取得。ハードコードなし。
- **呼び出す内容**: `Il2CppSteamworks.SteamFriends.
  ActivateGameOverlayToStore(appId,
  EOverlayToStoreFlag.k_EOverlayToStoreFlag_None)`のみ。
  `ResetController`/`SteamControllerReStart`/`Shutdown`/`Init`/
  `UpdateConnectedControllers`は一切直接呼ばない。
- **Overlayを閉じる操作**: このPhaseでは実装しない
  （ユーザーが手動で行う）。Escキー等の合成入力は一切なし。
- **観測**: 新規の観測コードは追加せず、既存の
  `Root26Phase5OverlayActivatedProbe`（Overlay ON/OFF
  コールバック）・`Root26Phase2AnalogActionDataProbe`（RSTICKの
  bActive/eMode/x/y）・`RightStickPollingProbe`（native
  GetPadAnalog、`[Root26NativePoll]`）・`Root26SteamStateProbe`
  等の既存`[Root26SteamState]` `ResetController`/
  `SteamControllerReStart`呼び出しログをそのまま再利用する。

### 49.2 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   6d68e2838266461568859900d2fd620f82c1a875e91b537e396e5e89378493d3
SHA-256 (deployed): 6d68e2838266461568859900d2fd620f82c1a875e91b537e396e5e89378493d3
  → 一致
```

### 49.3 実機テスト手順（次のステップ、ユーザー実施待ち）

1. 通常起動。
2. 探索状態へ入る（フィールド上を移動できる状態）。
3. 約5秒後、`[Root26Phase8]`が自動的に
   `SteamFriends.ActivateGameOverlayToStore`を1回だけ呼ぶ - この
   ゲーム自身のSteamストアページがOverlayとして表示されるはず
   （`HYPOTHESIS`、実機で初めて確認）。
4. そのOverlayを**ユーザーが手動で閉じる**（Escキー等、通常の
   Overlay閉じ方で構わない）。
5. 右スティックが反応するか確認する。
6. ゲームを終了し、ログを提供する。

### 49.4 判定基準（ユーザー指定）

- **成功A**: `PHASE8-CALL`完了 → `[Root26Phase5]`
  `OVERLAY-ACTIVATED-CALLBACK m_bActive=1` → （手動close）→
  `m_bActive=0` → `[Root26SteamState]` `ResetController`呼び出し →
  `[Root26Phase2]` RSTICK `ACTIVE-NONZERO` → `[Root26NativePoll]`
  LIVE。→ 物理Guideボタンは不要と確定。次にEsc自動送信
  （Phase 9）を検討する。
- **成功B**: Overlayは開き、コールバックも来て手動closeもできるが、
  RSTICKが復活しない。→ 物理Guide経由とSteamworks API経由で
  Steam内部状態が異なる可能性 - 重要な否定的結果として記録する。
- **失敗C**: Overlay自体が開かない、または`m_bActive=1`
  コールバックが来ない。→ このルートは終了、Broker/SDL
  fallbackへ戻る。

F9/F10・`ResetController`/`SteamControllerReStart`/`Shutdown`/`Init`/
`UpdateConnectedControllers`の手動呼び出し・SendInput/Esc自動送信は
一切行っていない。

## 50. CHECKPOINT: PC再起動前の状態保全（2026-09-08）

実機側でコントローラーが正常認識されなかったため、PC再起動を
優先し、Phase 8実機テストは**まだ実施していない**。

**重要**: 今回のコントローラー未認識は、Phase 8のAPI
（`ActivateGameOverlayToStore`）を発火させる前の、テスト開始
段階で発生したものである。**したがってこれはPhase 8の
PASS/FAIL判定材料には使用しない。** Phase 8の判定は、次回
実機テストを実施した際に49.4節の基準（成功A/成功B/失敗C）で
改めて行う。

### 50.1 実装状態（すべて完了・変更なし）

- `Root26Phase8OverlayToStoreOpenPoc.cs`実装済み（49.1節）。
- `src/ModMain.cs`への登録済み（`Sample()`呼び出し追加）。
- clean build成功: **0警告 / 0エラー**。
- ビルド成果物とデプロイ済みDLLのSHA-256が一致していることを
  本チェックポイント作成直前に再確認済み:
  ```
  6d68e2838266461568859900d2fd620f82c1a875e91b537e396e5e89378493d3
  ```
- **Phase 8実機テストは未実施。**

### 50.2 再起動後の再開地点（NEXT STEP）

```text
PC再起動
        ↓
コントローラー正常認識確認
        ↓
コード変更・再ビルド・再デプロイなし
        ↓
現在deploy済みのPhase 8 DLL（SHA-256上記）をそのまま使用
        ↓
SMT3HD通常起動
        ↓
探索開始
        ↓
約5秒後 ActivateGameOverlayToStore 自動発火
        ↓
Steam Overlayが開くか確認
        ↓
ユーザーが手動でOverlayを閉じる
        ↓
Right Stick確認
        ↓
ゲーム終了
        ↓
Latest.log解析
```

### 50.3 Phase 8判定基準（49.4節から再掲、変更なし）

- **成功A**: `PHASE8-CALL`完了 → Overlay callback ON → 手動close →
  callback OFF → `ResetController` → RSTICK `ACTIVE-NONZERO` →
  Native LIVE。→ 物理Guideボタン不要と確定、次にPhase 9
  （Esc自動送信）を検討。
- **成功B**: Overlay lifecycleは成立するがRSTICKは復活しない。→
  物理Guide経由とSteamworks API経由でSteam内部状態が異なる可能性、
  重要な否定的結果。
- **失敗C**: Overlayが開かない、またはcallback ONが来ない。→
  このルートは終了、Broker/SDL fallbackへ戻る。

### 50.4 禁止事項（再確認、変更なし）

F9禁止・F10禁止・`ResetController`手動呼び出し禁止・
`SteamControllerReStart`手動呼び出し禁止・`SteamInput`
`Shutdown`/`Init`手動呼び出し禁止・`UpdateConnectedControllers`
手動呼び出し禁止・SendInput禁止・Guide入力偽装禁止・Esc自動送信
禁止。Phase 8では`ActivateGameOverlayToStore`以外のSteam Input
状態変更を行わない。

### 50.5 Git / working tree状態（保全のみ、変更操作は一切行っていない）

- **branch**: `agent/right-stick-vanilla-turn`
- **HEAD**: `b2a2d4eac0db0dbd3b4867fef5ff02908d987081`
  （`feat: add broker-based external input launch and liveness guard`）
- `reset`/`checkout`による破棄/`restore`/`clean`/`stash`/`revert`/
  `rebase`は一切実行していない。commit/pushも今回は行っていない。

`git status --short`:
```
 M NocturneModernController.csproj
 M docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md
 M helper/Program.cs
 M settings/Program.cs
 M src/ModMain.cs
 M src/ModernControllerApi.cs
?? CLAUDE.md
?? docs/research/ROOT26_PRE_REBOOT_EVIDENCE_20260907/
?? settings/StartupShortcutManager.cs
?? src/FocusCycleProbe.cs
?? src/ManualResetControllerPoc.cs
?? src/RightStickPollingProbe.cs
?? src/Root26CallerDiagnosticsProbe.cs
?? src/Root26KernelLoopFrequencyProbe.cs
?? src/Root26Phase1Probe.cs
?? src/Root26Phase2AnalogActionDataProbe.cs
?? src/Root26Phase3PostResetWatchProbe.cs
?? src/Root26Phase4NativeRecoveryPoc.cs
?? src/Root26Phase5OverlayActivatedProbe.cs
?? src/Root26Phase8OverlayToStoreOpenPoc.cs
?? src/SteamControllerReacquisitionProbe.cs
?? src/SteamPadDeepStateProbe.cs
?? src/SteamPadOffset18Probe.cs
?? tools/AxisTestBreakawayLaunch/
?? tools/Root19PoC/
?? tools/Root22Wrapper/
?? tools/Run-AxisTestComparison.ps1
?? tools/Run-InGameAxisTestComparison.ps1
?? tools/Run-Root2Comparison.ps1
?? tools/Run-Root2SingleVariableExperiment.ps1
```

`git diff --stat`（追跡済みファイルの変更）:
```
 NocturneModernController.csproj                    |   12 +
 .../RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md     | 4476 +++++++++++++++++++-
 helper/Program.cs                                  |  258 +-
 settings/Program.cs                                |  176 +-
 src/ModMain.cs                                     |   37 +
 src/ModernControllerApi.cs                         |   16 +-
 6 files changed, 4930 insertions(+), 45 deletions(-)
```

これらの未追跡/未commit変更はすべて本調査（Root-26含む）の
成果物であり、削除・破棄・commit・pushのいずれも行っていない。

## 51. Phase 8 実機テスト結果: 成功B確定、同一セッション内Guide経路との比較（2026-09-08）

再起動後の実機テストで、`Latest.log`（01:19:31開始セッション）に
`ActivateGameOverlayToStore`経路と、その直後の物理Guide経路の
両方が同一セッション内に記録された。両経路を時系列比較した。

### 51.1 タイムライン — CONFIRMED

**`ActivateGameOverlayToStore`経路:**

```text
01:19:58.234-236  [Root26Phase8] PHASE8-TRIGGER →
                   ActivateGameOverlayToStore(appId=1413480) 正常return
01:19:58.247       [Root26Phase5] OVERLAY-ACTIVATED-CALLBACK m_bActive=1
                   （ユーザーが手動でOverlayを閉じる、約6秒間）
01:20:04.320       [Root26Phase5] OVERLAY-ACTIVATED-CALLBACK m_bActive=0
01:20:04.321-323   [Root26SteamState] ResetController() → SteamControllerReStart()
01:20:04.333       [Root26Phase1] SetAnalog i=0,i=1 → CalledZero
01:20:05.077       [Root26NativePoll] STATE-TRANSITION X/Y INITIAL → DEAD(128-fixed)
01:20:10.157       [Root26NativePoll] SESSION-SUMMARY finalState=DEAD transitions=0
01:20:17.707       次のexploration sessionでもfinalState=DEAD transitions=0（復旧なし）
```

**物理Guide経路（同一セッション、直後）:**

```text
01:20:17.616       [Root26Phase5] OVERLAY-ACTIVATED-CALLBACK m_bActive=1
01:20:18.575       [Root26Phase5] OVERLAY-ACTIVATED-CALLBACK m_bActive=0
01:20:18.575-576   [Root26SteamState] ResetController() → SteamControllerReStart()
01:20:18.595       [Root26Phase1] SetAnalog i=0,i=1 → CalledZero
01:20:19.095-096   [Root26NativePoll] STATE-TRANSITION X/Y INITIAL → DEAD(128-fixed)
                   （↑ここまでStore経路と完全に同一形状）
01:20:19.303       [Root26Phase2] RSTICK state=ACTIVE-NONZERO x=-0.321 y=0.155
                   （ResetController/ReStartから約0.73秒後）
01:20:19.331       [Root26Phase1] SetAnalog i=1(RSTICK) → CalledActive
01:20:19.595-596   [Root26NativePoll] STATE-TRANSITION X/Y DEAD → LIVE(analog-active)
                   （ResetController/ReStartから約1.02秒後）
01:20:20.332       [Root26NativePoll] SESSION-SUMMARY finalState=LIVE(analog-active) transitions=1
```

### 51.2 CONFIRMED

1. Overlay OFF以降の再取得シーケンス（`ResetController()`→
   `SteamControllerReStart()`→`SetAnalog` CalledZero→
   `[Root26NativePoll]` SESSION-START→STATE-TRANSITION
   INITIAL→DEAD）は、呼び出し回数・順序・引数の形状まで
   Store経路とGuide経路で完全に一致している。
2. controller handle（`19680159496504676`）とRSTICK analog action
   handle（`2`）はセッション全体を通じて不変。`[Root26SteamPadState]`
   のController.Count／InputHandles／`GetControlDevice`／
   `GetControllerType`も、初回接続確立（01:19:47）以降は両経路の
   reset前後で一切STATE-CHANGEしていない。→ Steam Input側の
   enumeration/handle/type状態は両経路で識別可能な差がない。
3. 最初に観測可能な分岐点は、上記の完全一致区間の**後**、
   `SteamInput.GetAnalogActionData`がRSTICKの実x/yを返し始めるか
   否かの一点に絞られる。Store経路は最終的に`finalState=DEAD
   transitions=0`のまま2回のexploration sessionをまたいで復旧せず、
   Guide経路はINITIAL→DEAD遷移から約0.7秒後にACTIVE-NONZERO、
   約1.0秒後にNative LIVEへ遷移した。
4. **ユーザー実機観測（本人確認）**: Store Overlayを閉じた直後、
   実際に右スティックを操作して反応しないことを確認済み。→
   「Store経路の区間でRSTICK入力を試みていない」という代替説明を
   排除する。前回記録した「UNRESOLVED: ユーザーが実際に右スティック
   を動かしたか」は、本人確認によりCONFIRMEDへ格上げする。

### 51.3 HYPOTHESIS

- Store OverlayとGuide経由の通常Overlayでは、`[Root26SteamPadState]`
  が捕捉する範囲（enumeration/handle/type）の外側にある、Steam
  client内部の非公開state transitionが異なる可能性がある。
- Guideボタン自体の入力が必要なのか、それとも「Steamの通常Overlay」
  を開く経路であれば入力方法を問わないのかは、まだ未確定。

### 51.4 Phase 8判定（49.4節／50.3節の基準に対して）

**成功B**が確定した: `PHASE8-CALL`完了→Overlay ON/OFFコールバック→
`ResetController`/`SteamControllerReStart`まではすべて正常に発生した
が、RSTICKは復活しなかった。→ 「Overlayを表示しさえすれば経路は
問わない」という仮説は否定された。**Phase 8はOverlayなら何でも
良いわけではないことを示した。**

### 51.5 次の実験計画: 物理`Shift+Tab`によるSteam通常Overlay起動（コード変更ゼロ）

51.3節のHYPOTHESISを次の3通りに切り分けるため、次の実機テストでは
**コード変更・再ビルド・再デプロイを一切行わず**、clean launchで
RSTICKがDEADの状態から物理キーボードの`Shift+Tab`でSteamの通常
Overlayを開閉し、右スティックが復旧するか確認する。

```text
A. Guideボタンそのものが特別
B. 「通常のSteam Overlay」を開く経路自体が特別（入力手段は問わない）
C. Store Overlayだけが特殊

Shift+Tabで復旧する
  → Bが正しい。Guideボタン自体は不要。
  → 将来的に通常Overlay起動経路（MOD内から）を探す価値が高い。

Shift+Tabでは復旧せず、Guide経由のみ復旧する
  → AまたはCが正しい。Guide入力経路そのものがSteam Inputの
    再取得に関与している可能性が一気に高くなる。
  → ここで初めて「内部Guide」経路の研究が本命になる。
```

この結果が出るまで新規probe実装・build・deployは行わない。
F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止を継続する。

## 52. Shift+Tab / Alt+Tab実機テスト結果: 新規confound（controller handle重複）を検出、判定保留（2026-09-08）

51.5節の計画に従い、コード変更・再ビルド・再デプロイなしで
`Shift+Tab`による通常Steam Overlay起動テストを実施した。ユーザーは
実際には**Shift+Tab（先、失敗）→ Alt+Tab（後、復活を確認）**の順で
テストしたと報告している。同一`Latest.log`セッション（01:44:24
開始）に、この2つとStore API自動発火を合わせた3サイクルが記録
されている。

### 52.1 タイムライン — CONFIRMED（サイクルの正体はユーザー報告に基づく）

```text
01:44:35.103-35.939  サイクル1（Shift+Tab、ユーザー実施、先）
                      Overlay ON→OFF（約0.8秒）→ ResetController/ReStart
                      直後にサイクル2（Store API自動発火）と重なり、
                      単独の観測窓が約1秒未満しかない
                      → ログ単体では復旧有無を判定できないが、
                        ユーザー本人が「復活しなかった」と直接確認済み

01:44:36.906-39.227  サイクル2（Root26Phase8 Store API、自動発火）
                      Overlay ON→OFF（約2.3秒）→ ResetController/ReStart
                      → 51章と同様、finalState=DEAD transitions=0

01:44:43.940-46.085  サイクル3（Alt+Tab、ユーザー実施、後）
                      Overlay ON→OFF（約2.1秒）→ ResetController/ReStart(46.086-088)
                      → 直後の[Root26NativePoll]観測窓（46.095〜54.530、約8.4秒）では
                        RSTICKは一度もACTIVE-NONZEROにならず、
                        SESSION-SUMMARY finalState=DEAD(128-fixed) transitions=0
                      → その後、他のOverlay/Resetイベントを一切挟まずに
                        約14秒後（01:44:59.868〜01:45:00.188）、
                        RSTICKがACTIVE-NONZERO化しNative LIVEへ遷移
                        （52.3節で詳述）
```

### 52.2 CONFIRMED

1. Shift+Tab（サイクル1）はユーザー本人が実機で復旧しないことを
   直接確認している。ログ上は観測窓が短く単独では判定できないが、
   ユーザー観測と矛盾しない。
2. Alt+Tab（サイクル3）でも`[Root26Phase5]`
   `OVERLAY-ACTIVATED-CALLBACK`（m_bActive=1→0）と
   `ResetController`/`SteamControllerReStart`が発生することを確認
   した（Steamがフルスクリーン中のAlt+Tabをoverlay経由で処理して
   いる可能性と整合）。
3. Alt+Tab（サイクル3）の**直後**（46.095〜54.530、約8.4秒の観測窓）
   ではRSTICKは復旧せず、`finalState=DEAD transitions=0`だった。
   ユーザーが「アルト側だと復活した」と体感したのは、この直後の
   窓ではなく、その後さらに時間が経ってから（52.3節のタイミング）
   である可能性が高い。

### 52.3 新規confound: controller handle重複 — CONFIRMED（発生）/ HYPOTHESIS（原因）

このセッションは、Overlayテストより前の01:44:25.873時点で
`[Root26SteamPadState]` `Controller.Count 0 → 2`を記録しており、
2つの異なるcontroller handleが存在した:

- `19680159496504676`（45章・51章と同一、元のcontroller）
- `91728467815138660`（**このセッションで初めて出現**）

45章・51章のセッション（Guide成功セッション）は`Controller.Count
0 → 1`のみで、handleは常に1つだった。

**ユーザー確認（本人観測）**: コントローラーの抜き差し、2台目の
パッド/仮想パッド接続、DS4Windows等のソフト実行、いずれも心当たり
なし。→ この重複は既知の外部要因では説明できない
（**CONFIRMED: 外部要因なし**）。

このセッション全体を通じて、
- `19680159496504676`のRSTICKは一度もACTIVE-NONZERONにならず
  （ACTIVE-ZERO/INACTIVEのみ）、
- `91728467815138660`のRSTICKは、Alt+Tab（サイクル3）のresetから
  **約14秒後**（間に他のOverlay/Reset/Guideイベントなし、直前の
  NativePoll sessionが一度exploration終了で区切られただけ）に
  単発でACTIVE-NONZERO（x=0.0606 y=0.0175、01:44:59.868）となり、
  `[Root26NativePoll]`も同時にDEAD→LIVEへ遷移した
  （01:45:00.188）。

**HYPOTHESIS**: `91728467815138660`は本物の2台目物理デバイスでは
なく、同一物理controllerのSteam Input側での重複/ghost
enumerationである可能性が高い（ユーザーが外部要因を否定した
ことと整合）。

### 52.4 51.5節の判定への影響 — 更新されたが依然UNRESOLVED

Shift+Tab（失敗）とAlt+Tab（最終的に復旧と相関）という結果は、
一見「Guideボタン自体は不要で、通常Overlay経路一般で十分」という
HYPOTHESIS Bを支持するように見える。しかし、以下の理由で
**まだCONFIRMEDへ格上げできない**:

- Alt+Tab（サイクル3）の**直後**の観測窓（8.4秒）ではRSTICKは
  復旧しておらず、実際の復旧はcontroller handle重複という新規
  confoundを伴う、約14秒遅れた・別handle上の事象である。
- 前回（Guide成功、45章・51章）との比較は「単一controller・単一
  handle・reset直後1〜1.3秒での復旧」というクリーンな相関の上に
  成立していたが、今回のAlt+Tabの事象はこのパターンと形状が異なる
  （遅延が10倍以上、handleも別）。
- Shift+Tabはそもそも単独の観測窓がログ上ほぼ存在しないため、
  「Shift+Tabという経路固有の失敗」なのか「たまたま短時間で次の
  イベントに上書きされただけ」なのかも厳密には切り分けられていない。

したがって、A/B/Cの切り分けは**UNRESOLVED（保留）**のまま据え置く。
Guideボタン自体が必要なのか、通常Overlay経路で十分なのかを
再度クリーンに検証するには、まずこの重複handle現象が再現するか、
そして本物のSteam側から見て実際に何台のcontrollerとして認識
されているかを確認する必要がある。

### 52.5 次の確認事項（コード変更・build・deploy不要）

1. Steamクライアント自身の「設定 > コントローラー > 全般
   コントローラー設定」画面、またはSteamのBig Pictureモードの
   コントローラー一覧を、ゲーム起動中に開いて確認することを推奨
   する。これによりSteam自身が今回のセッションでcontrollerを1台と
   認識しているか2台と認識しているかを、mod側のログとは独立に
   確認できる。
2. 次回のクリーンな比較のためには、Shift+TabとAlt+Tabそれぞれに
   ついて、直後最低5秒程度は他の操作を挟まずに右スティックの反応を
   確認し、「いつ」「どちらのhandleで」復旧したかをできる限り明確に
   区別できるようにする。

この結果が出るまで、新規probe実装・build・deployは行わない。
F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止を継続する。

## 53. Steam自身の内部ログ（`Steam\logs\controller.txt`）による相関解析（2026-09-08）

「Steam Overlay lifecycle一般」ではなく「物理Guide入力をSteamが
処理する経路」を標的にするため、Steamバイナリのreverse
engineeringに入る前に、**Steamクライアント自身が既に出力している
内部ログ**（MODのMelonLoaderログとは完全に独立、同一マシンの
同一時計を使うため秒単位でそのまま突き合わせ可能）を確認した。
`C:\Program Files (x86)\Steam\logs\controller.txt`がそれで、
Steam Input側のcontroller接続・config cache・action set活性化を
記録している。コード変更・build・deploy・Steamバイナリへの
patch/injection/hookは一切行っていない（読み取りのみ）。

### 53.1 4つのテスト区間との突き合わせ — CONFIRMED

各区間について、`controller.txt`に該当時刻のエントリがあるかを
機械的に確認した。

```text
[Store API経路、01:19:58.234〜01:20:04.333、失敗]
  controller.txt: 01:19:56〜01:20:05の範囲に該当行 0件（完全な無音）

[物理Guide経路、01:20:17.616〜01:20:18.595、成功]
  controller.txt:
    01:20:17  Controller 0 uses xinput : true
              Add to Config Cache Request 0 443510 - adopting binding 26
              Queueing activation for controller: 0 app: 443510
              Controller 0 uses xinput : true
              Add to Config Cache Request 0 1413480 - adopting binding 27
              Queueing activation for controller: 0 app: 1413480
    01:20:18  （同型のバーストがapp 443510→1413480で再度発生）
  → 直後 01:20:19.303 RSTICK ACTIVE-NONZERO（51章）

[Shift+Tab経路、01:44:35.103〜01:44:35.943、失敗]
  controller.txt: 01:44:34〜01:44:36の範囲に該当行 0件（完全な無音）

[Alt+Tab経路、01:44:43.940〜01:44:46.088、直後は失敗]
  controller.txt: 01:44:43〜01:44:49の範囲に該当行 0件（直後は無音）
  → ただし約8秒後、01:44:54に以下のバーストが発生
    （Overlay ON/OFFコールバックは伴わない、Steam Input内部単独の
    イベント）:
      Controller 0 uses xinput : true
      Add to Config Cache Request 0 413080 - adopting binding 84
      Queueing activation for controller: 0 app: 413080
      Controller 1 uses xinput : true
      Add to Config Cache Request 1 413080 - adopting binding 85
      Queueing activation for controller: 1 app: 413080
      Controller PollState Changed from 2 to 1
      Controller 0 uses xinput : true
      Add to Config Cache Request 0 1413480 - adopting binding 86
      Queueing activation for controller: 0 app: 1413480
      Controller 1 uses xinput : true
      Add to Config Cache Request 1 1413480 - adopting binding 87
      Queueing activation for controller: 1 app: 1413480
      Controller PollState Changed from 1 to 2
  → さらに約5〜6秒後、01:44:59.868〜01:45:00.188に
    controllerHandle=91728467815138660（Controller 1）のRSTICKが
    ACTIVE-NONZERO化・Native LIVE化（52章）。
    controllerHandle=19680159496504676（Controller 0、同じバーストを
    受けている）は最後までRSTICK復旧せず。
```

### 53.2 CONFIRMED（53.2.1節の訂正を必ず併読すること）

1. RSTICKが復旧しなかった2区間（Store API・Shift+Tab）は、いずれも
   `controller.txt`側の記録が完全に無音（該当行0件）という共通点を
   持つ。
2. RSTICKが復旧した2つの事象（Guide、Alt+Tab session後半）は、
   いずれも直前に`controller.txt`側で「`Controller N uses xinput :
   true`」「`Add to Config Cache Request ... adopting binding`」
   「`Queueing activation for controller: N app: X`」
   「`Controller PollState Changed from X to Y`」という同一形状の
   バーストを伴っている。**ただし53.2.1節の訂正により、この
   バースト自体はGuide/Overlayと無関係な日常的housekeepingである
   ことが判明したため、この一致は現時点では相関の強い証拠とは
   扱わない。**
3. これらのログ文字列は`steamclient64.dll`内に実在することを
   確認した（読み取り専用のバイナリ内文字列検索、patch/inject
   なし）:
   ```
   "uses xinput"                          : 2箇所
   "PollState Changed from"               : 1箇所
   "adopting binding"                     : 1箇所
   "Queueing activation for controller"   : 1箇所
   "Add to Config Cache Request"          : 3箇所
   ```
   `GameOverlayRenderer64.dll`には上記文字列は1つも存在しない
   （0箇所）。→ **CONFIRMEDなのは、これらのログを実際に出力している
   実装が`steamclient64.dll`側に存在するということまで**である。
   `GameOverlayRenderer64.dll`がこのバーストの上流イベント（トリガー
   発生元）に一切関与していない、とはこの文字列検索だけでは断定
   できない（`GameOverlayRenderer64.dll`はoverlay描画そのものを
   担うプロセス内DLLであり、ログ出力自体を持たなくても、何らかの
   経路で`steamclient64.dll`側の処理を起動している可能性は残る）。

### 53.2.1 重大な訂正: burstは日常的periodic housekeepingであり、Guide/Overlay固有ではない — REJECTED（marker仮説）

52章時点のフォローアップ指示（過去ログとの相関確認、B項）に従い、
`controller.txt`全体（2026-09-06 10:23〜2026-09-08 01:45、約2日分、
ゲーム内外の複数回起動を含む）で`"Queueing activation for
controller"`の全出現タイムスタンプを機械的に集計した。

**CONFIRMED（否定的結果）**: このバースト（`app 413080/769`等 ⇔
`app 1413480`を往復する構造）は、Guide/Overlay/RSTICKの状態と無関係
に、通常プレイ中を通じてほぼ常時、約10〜90秒間隔で断続的に発生し
続けている。例（2026-09-06、通常プレイ中、テストとは無関係）:

```text
10:26:39 → 10:26:48 → 10:27:05 → 10:27:25 → 10:27:45 → 10:27:50 → ...
（以降も同様の頻度で数時間継続）
```

さらに、01:44セッション自体でも、Shift+Tab（35.1秒）・Store API
（36.9〜39.2秒）・Alt+Tab（43.9〜46.1秒）という**3つのOverlay
イベントそのものの瞬間には、いずれもburstが発生していない**
（burstが観測されたのは01:44:13・01:44:54・01:45:08・01:45:21・
01:45:22の5回のみで、いずれもOverlayイベントの発生時刻そのものとは
一致しない）。

**訂正**: 53.2節・53.3節（旧稿）で述べた「burstがRSTICK復旧に強く
相関するSteam Input内部イベントである」という評価は**REJECTED**
とする。正しい評価は次の通り: burst自体はStore/Shift+Tab/Guide/
Alt+Tabのいずれとも無関係に約10〜90秒周期で常時発生している
routine housekeepingであり、Guide成功事例・Alt+Tab事例で観測時刻
近傍にburstがあったこと、Store・Shift+Tab事例でその瞬間にburstが
無かったことは、**この周期性から見て偶然の一致である可能性を
排除できない**。burstの有無を根拠にA/B/Cを切り分けることは
できない。

この結果、burst自体を「Steam Input側のGuide専用処理」として
追いかける53.5節・53.6節の当初方針は前提が崩れている。次の
焦点は53.7節（後述）で扱う、**controller.txtが明示的に区別している
`Controller 0`/`Controller 1`という2つのSDL/HIDデバイス自体の
素性**に移す方が有望である。

### 53.3 HYPOTHESIS（burstに関する記述は53.2.1節により撤回。以下は
参考として残すが、burstを根拠にした因果推論は行わない）

- 訂正: Alt+Tabセッションのburst（01:44:54）は、`Controller 0`
  （handle`19680159496504676`）と`Controller 1`
  （handle`91728467815138660`）の**両方**をactivation対象にしたが、
  実際にRSTICKが復旧したのは`Controller 1`のみで、`Controller 0`は
  最後までDEADのままだった（52章）。**したがってこのburst単独は
  RSTICK復旧の十分条件ではない（53.2.1節によりburstは日常的事象と
  判明したため、そもそも相関自体が偶然の可能性が高い）。**
  ```text
  Store FAIL      : burstなし → DEAD
  Shift+Tab FAIL  : burstなし → DEAD
  Guide PASS      : burstあり → LIVE
  Alt+Tab後       : burstあり → Controller 0 DEAD / Controller 1 LIVE
  ```
- 現時点で安全に言えるのは: 「`steamclient64.dll`内のconfig-cache
  再adoption/`Queueing activation`/`PollState`遷移burstは、RSTICK
  復旧に強く相関するSteam Input内部イベントであり、**必要条件候補、
  またはSteam Input内部reconfiguration過程のmarker**である」まで。
  Store OverlayやShift+Tabは（少なくとも今回の観測では）このburstを
  引き起こしていないため、burstが起きないことはDEAD継続と相関するが、
  burstが起きることは復旧を保証しない（Controller 0の反例）。
- Alt+Tabセッションでburstが「Alt+Tabのoverlay ON/OFF」自体とは
  非同期（約8秒遅れ、overlayコールバックを伴わない）に発生している
  ことから、このバーストはSteam Overlayのlifecycleコールバックとは
  別の、Steam Input側の何らかの独立したトリガー（例:
  フォーカス変化の別経路、定期的な内部再検証、あるいはWindows側の
  何らかのイベント）によって引き起こされている可能性がある。

### 53.4 UNRESOLVED

- ログに現れる`app 443510` / `413080` / `769` / `2371090`の正体。
  いずれもこのPC上に現在インストールされていない
  （`steamapps\appmanifest_<id>.acf`が存在しない）。これらが実際に
  何のappか、そしてこのバースト機構において本質的な役割を持つのか
  単なる「たまたま切り替え先に選ばれた別app」に過ぎないのかは未確認。
- このバーストを引き起こす直接のトリガーが「物理Guideボタン入力」
  なのか、「入力フォーカス変化全般」なのか、「Steamの内部タイマー」
  なのかは、まだ切り分けられていない。Alt+Tabセッションでの
  約8秒の遅延・overlayコールバック非同期は、単純な
  「Guide/Overlay即時トリガー」説とは整合しない。

### 53.5 静的解析の位置づけ（53.2.1節の訂正後）

53.2節3項の文字列存在確認（`steamclient64.dll`内に5文字列とも実在、
`GameOverlayRenderer64.dll`には存在しない）自体はそのまま有効な
事実として残る。ただし53.2.1節により、これらの文字列が指す
「burstイベント」自体はGuide/Overlay固有の処理ではなく日常的な
housekeepingだと判明したため、**この5文字列へのxrefを追っても
「Guide固有の処理」には辿り着けない可能性が高い**。静的解析を
行う場合の優先度は53.7節の発見（`Controller 0`/`Controller 1`の
素性、および`hid_read failure`によるデバイス脱落）を起点にする方が
筋が良い。

### 53.6 Part C（steamclient64.dllのxref静的解析）— 実施不可（環境制約、正直な報告）

ユーザー指示のPart Cを試みたが、**この環境にはx64バイナリの
xref解析ができるツールが存在しない**ことを確認した。

```text
確認結果（すべて "not found"）:
  strings
  dumpbin
  objdump
  nm
  radare2 / r2
  ghidra / ghidraRun
"C:\Program Files" 以下に ghidra インストールも dumpbin.exe も
見つからなかった。
```

実施できたのはPython製の読み取り専用バイト列検索（文字列の
存在確認のみ、53.2節）に限られる。関数境界の特定・xref追跡・
呼び出し元の特定といった実際の逆アセンブル作業には、Ghidra/IDA/
radare2等の追加ツールのインストールが必要であり、**本セッションでは
未実施**（推測でxrefの中身を報告することはしない）。

### 53.7 Part A: `Controller 0` / `Controller 1`の正体 — 重要な新発見（CONFIRMED＋HYPOTHESIS）

`controller.txt`の接続シーケンス（`Local Device Found` /
`Product:` / `Interface:` / `device opened for index` /
`Controller using HIDAPI driver`）を、Guide成功セッション
（01:19:36〜）と今回のセッション（01:44:12〜、実際の接続は
それ以前から継続）の双方、および過去2日分の全接続イベントで
確認した。

**CONFIRMED**: この物理controller（Xbox Elite Series 2、
`vid=0x045e`）は、**接続の度に必ず2つの別デバイスとしてSteamに
検出される**:

```text
index 0: Product "Xbox One Elite 2 Controller"
         HIDAPI driver, vid=0x045e pid=0x0b00
         シリアル "45e-b00-33e98564"
         → controller_baseの `configset_45e-b00-...vdf`
           （Elite固有の生HID経路）

index 1: Product "Xbox 360 Controller"（総称名）
         HIDAPI driver, vid=0x045e pid=0x028e
         シリアル "45e-28e-33e98564"
         → controller_baseの `configset_45e-28e-...vdf`
           （Windows XInput互換の汎用経路。XInputクラスデバイスは
           実機種名を返さないため、SDL/Steamは常に "Xbox 360
           Controller" という総称名で見える）
```

このindex0/index1のペアは、2026-09-06 10:23の最初の記録から
2026-09-08 01:44まで、**全ての接続イベントで一貫して同一の
vid/pid/シリアルの組み合わせ**として現れる。つまりこれは今回の
セッション固有の異常ではなく、**このElite Series 2
controllerの恒常的な二重インターフェース挙動**である。

**CONFIRMED（Guide成功セッションとの決定的な違い）**: Guide成功
セッション（01:19:36開始）では、index0・index1の両方が開かれた
直後、`01:19:38`に

```text
[2026-09-08 01:19:38] Controller device closed after hid_read failure
```

が発生し、以降そのセッションでは1台のcontrollerしか生き残らな
かった（MOD側`Root26SteamPadState`が`Controller.Count 0→1`と
一致）。一方、今回（Shift+Tab/Alt+Tabセッション、01:44:12〜）は、
この時間帯に`hid_read failure`による切断が発生しておらず、
index0・index1の**両方が生き残ったまま**app 1413480の
binding adoptionを受けた（`Controller.Count 0→2`と一致）。

**HYPOTHESIS**: `91728467815138660`は、
- 別の物理デバイスではない
- reWASD等由来のvirtual instanceでもない（そのような形跡は
  controller.txt上に一切ない）
- **同一物理Elite Series 2 controllerの、XInput互換の汎用
  インターフェース（index1、"Xbox 360 Controller"名義、
  pid=0x028e）が、たまたま`hid_read failure`で脱落せずに生き
  残った結果、Steam Inputが別のcontroller instanceとして
  同時に公開してしまったもの**である可能性が最も高い。

この`hid_read failure`が起きるかどうか自体は、今回のUSB/
Bluetooth接続状態に依存するランダム性の高い事象である可能性が
あり、Guide/Overlay/Shift+Tab/Alt+Tabのどの操作とも無関係に、
セッション開始直後の数秒以内に（起きる場合は）発生している。
**したがって「controllerが1台か2台か」はGuide/Overlayとは独立に
決まっている**可能性が高い。

**UNRESOLVED**: `19680159496504676`と`91728467815138660`のどちらが
`controller.txt`上の`Controller 0`/`Controller 1`（SDLデバイス
index）に対応するかは、厳密な1対1の対応付けができていない
（同時期・同appidという状況証拠のみ）。また、なぜindex1側
（`Controller 1`）だけがACTIVE-NONZERO化し、index0側
（`Controller 0`）が最後まで復旧しなかったのかも未解明。
`Root26SteamPadState`側で`CurrentInputID 0 →
91728467815138660`が接続直後（01:44:27.108）に記録されている
ことから、Steam Input側が2台のうちどちらを「現在の入力ソース」と
みなすかという内部選択（`CurrentInputID`）が、実際にどちらの
handleの分析データが有効化されるかを左右している可能性がある
（HYPOTHESIS、未検証）。

### 53.8 Part D: app ID — 未確定のまま据え置き

`443510` / `413080` / `769` / `2371090`について、ローカルに
`appmanifest_<id>.acf`が存在しないことは確認済みだが、これは
「現在インストールされていない」ことしか示さず、Steam Input内部
での役割（config-cache/application-context/binding-context等）を
判定する材料にはならない。53.2.1節の訂正により、これらのappIDは
そもそも「Guideと関係する特別な切替先」ではなく、**日常的な
housekeeping cycleが定期的に巡回する対象のひとつ**である可能性が
高まった。断定はせずUNRESOLVEDのまま据え置く。

### 53.9 総括と次の一手（未実施、提案のみ）

今回のPart A〜D実施により、調査の焦点は
「Steam Overlay lifecycle」「Guide専用burst」から、
**「なぜ同一物理controllerの2つのHIDインターフェースのうち、
片方だけがdds3PadManagerに届く実データを持つのか」**
へ明確に移った。次に価値が高いのは新規実機テストではなく、
以下のread-only作業だと考えられる:

1. `controller.txt`から、`hid_read failure`が発生した/しなかった
   セッションを可能な限り洗い出し、「1台のみ生存」セッションで
   RSTICKが最終的にどう振る舞ったかを（過去のRoot26 Evidence章と
   突き合わせて）確認する。
2. Ghidra等のツールが用意できた場合、53.6節ではなく、
   `dds3PadManager.GetPadAnalog()`（25章で既にVA判明済み）が
   `controllerHandle`をどう選択しているか（`GetConnectedControllers()`
   が返す配列のうち何番目を使うか、あるいは`CurrentInputID`
   相当の値を参照しているか）を優先して追う。

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 54. Chapter 53 follow-up: controller selection pathへの焦点転換（2026-09-08）

ユーザー指摘（burst仮説を自分で過去ログ全体から潰したのは正しい、
という承認、および「本当に重要なのはこっち」という指摘）を受け、
53章の訂正とPart A〜Dのフォローアップを実施した。新規実装・
build・deploy・Steamバイナリへのpatch/injection/hookは行って
いない。

### 54.1 訂正: `hid_read failure`はindexを特定できない — UNRESOLVEDへ差し戻し

53.7節の「`01:19:38`の`hid_read failure`でindex1側が脱落した」という
記述を確認した。実際のログ行:

```text
[2026-09-08 01:19:38] Controller device closed after hid_read failure
```

この行自体にはcontroller index・handle・vid/pidのいずれも含まれて
おらず、**どちらのインターフェースが閉じたのかはログから直接
判定できない**。53.7節の該当記述は`UNRESOLVED`に差し戻す。

ただし、副次的に重要な発見として、同種の`hid_read failure`は
セッション中に何度も無関係なタイミングで発生していることを確認した
（例: `01:20:29`の直後、`01:35:25`にも発生し、直後に
`Controller PollState Changed from 1 to 0`＝全controller切断まで
進んでいる）。**この失敗はGuide/Overlay操作の有無と無関係に、
Elite Series 2のUSB/Bluetooth接続状態に起因する環境要因である
可能性が高い**、という評価はHYPOTHESISとして維持する。

### 54.2 過去Evidence（45章）との重要な再接続 — 見落としの訂正

52〜53章執筆時点で見落としていたが、**45.3節に、今回と全く同じ
現象が既に記録されていた**:

```text
（2026-09-08 00:43台、Phase 5実機テスト、Guide/Overlay経由）
handle=19680159496504676 と handle=91728467815138660 の
2つのcontroller handleが観測された（前回までのテストはすべて
単一handleのみ）。RSTICK復活は2台目のhandle（...660）側で
先に観測された。
```

このセッションではResetControllerから約619ms後にhandle...660の
RSTICKがACTIVE-NONZERO化しており（52章のAlt+Tabセッションの
約14秒遅延とは対照的に、Guide成功セッション（01:19-01:20）と
同程度の高速revival）、45.3節時点では「本筋と無関係の可能性」として
UNRESOLVEDのまま保留されていた。

**CONFIRMED（新規）**: handle`91728467815138660`という**同一の
64bit数値**が、時間的に離れた複数回のゲーム起動（00:43台と
01:44台、間に挟まる01:19-01:20セッションでは出現せず）で
繰り返し出現している。Steam Inputのcontroller handleは通常
デバイスの永続的な識別情報（device path/serial等）から導出される
ため、この値の再現性は、53.7節のHYPOTHESIS
「`91728467815138660`はランダムなghostではなく、同一物理Elite
Series 2 controllerの second HID interfaceに対応する安定した
Steam Input registrationである」を後押しする（HYPOTHESISのまま
だが、支持材料が増えた）。

### 54.3 訂正: 次の解析対象は`GetPadAnalog()`ではなく、既存Evidence（26章・30章）の再接続で足りる

ユーザー指摘の通り、`dds3PadManager.GetPadAnalog()`は`AnalogStickLRval`
から見て十分に下流であり、controller選択の場所ではない。**新規の
Ghidra解析を行うまでもなく、既存章の再接続だけで有力な答えが
得られた。**

30.2節（CONFIRMED）が既に示す経路:

```text
SteamInputUtil.UpdateInput()
    controllerArray = GetConnectedControllers() が返す配列
    for (rdi = 0; rdi < controllerCount; rdi++) {
        handle = controllerArray[rdi]
        SteamPadSet(rdi)
            for (i = 0; i < 2; i++) {   // LSTICK, RSTICK
                {mode,x,y,bActive} = GetAnalogActionData(handle, ...)
                SetAnalog(ref bitmask, rdi, i, x, y)
                AnalogStickLRval[rdi][i] = クランプ済み値
            }
    }
```

**CONFIRMED（30.2節より再確認）**: `SteamPadSet`は接続中の**各
controllerについて1回ずつ、完全に対称・独立に**呼ばれる。2台の
controllerが存在する場合、両方が**それぞれ別のインデックス
`rdi`へ**書き込む。ここに「どちらを採用するか」という選択・
統合ロジックは存在しない。

26.2節（CONFIRMED）が示す次の段階:

```text
dds3PadUpdate():
    dst = dds3PrivatePadAnalog[uVar20][uVar18]
        ← AnalogStickLRval[uVar20][uVar19] から変換コピー
        （uVar20は入力側・出力側で同一インデックスのまま、
          並び替え・選択は行われない）
```

そして4.1節（CONFIRMED、既存の最重要ファクト）:

```csharp
// fldCamera.fldCamMain() 実際の呼出し（リテラル定数）
GetPadAnalog(0, 1, 0, 1)  // Right Stick X — padno=0固定
GetPadAnalog(0, 1, 1, 1)  // Right Stick Y — padno=0固定
```

**CONFIRMED（既存4.1節、再確認）**: 実際にカメラを動かす呼び出しは
`padno=0`を**リテラル定数**として渡しており、動的な変数ではない。

### 54.3.1 実データによる検証 — 仮説はREJECTED（単純な「先頭勝ち」ではない）

54.3節の仮説作成の直後、`Root26SteamPadDeepStateProbe`が実際に
記録した`InputHandles`配列（52章のセッション、01:44:25.876に確定、
以降セッション終了まで不変）を確認した:

```text
InputHandles = [19680159496504676 (DEAD), 91728467815138660 (LIVE), 0, 0, ...]
```

**DEADなhandleがindex0、LIVEなhandleがindex1**である。もし
54.3節の仮説（「`GetConnectedControllers()[0]`＝配列先頭が
ゲームに反映される」）が正しければ、この配列が一度も変化して
いない以上、カメラはDEADなindex0のデータを読み続け、RSTICKは
最後までDEADのままになるはずである。しかし実際には
01:45:00.188に`[Root26NativePoll]`がLIVEへ遷移しており、
**observed factと54.3節の仮説は直接矛盾する。**

**REJECTED**: 「`SteamInput.GetConnectedControllers()`の返却配列の
先頭（index0）だけがそのままゲームに反映される」という単純な
仮説は、この実データにより否定する。

**HYPOTHESIS（再整理、未検証）**: 矛盾を説明しうる候補は複数
並存している。優先順位はまだ付けられない。

1. `SteamPad.InputHandles`（`Root26SteamPadDeepStateProbe`が読む
   managedフィールド）と、`SteamInputUtil.UpdateInput()`が
   `0x182602C40`経由で毎フレーム直接取得する`controllerArray`
   （30.2節）が、**そもそも同じ配列/同じ並び順とは限らない**。
   前者は「新規列挙→旧リストと比較」用の保存済みスナップショット
   （15.3節既存HYPOTHESIS）である可能性があり、`UpdateInput()`内で
   使われる`rdi`のインデックス基準とは無関係かもしれない。
2. `dds3PrivatePadAnalog[uVar20]`の`uVar20`が「controller
   enumeration index」ではなく、26.2節UNRESOLVED項目4で既に
   指摘されている通り**実は`stick_lr`（LSTICK/RSTICK区別）の方
   かもしれず**、controller選択は別の未発見の経路で行われている
   可能性がある。
3. `SteamPadSet(rdi)`の`rdi`自体が、`GetConnectedControllers()`の
   配列順とは別の基準（例: 内部の`Controller`辞書のキー順、
   `CurrentInputID`との一致判定等）で決まっている可能性がある。

**UNRESOLVED（最優先、更新）**: `SteamPadSet(int index)`本体
（30.2節でRVA `0x2602F30`と特定済み）の`index`引数の実際の意味と、
それが`GetConnectedControllers()`の配列順・`SteamPad.InputHandles`
のどちらとも一致するとは限らないという点を、既存
`Root26Project`（GameAssembly.dll）のGhidra再解析で直接確認する
必要がある。この時点でnative呼び出し経路の再構成を静的解析のみで
完了させることはできず、次段はGhidraでの`UpdateInput()`/
`SteamPadSet`呼び出し元ループの命令列レベル再確認が必須。

### 54.4 Ghidra環境の再確認 — ユーザー指摘の通り実在した（訂正）

53.6節で「この環境にはx64バイナリのxref解析ツールが存在しない」と
報告したのは**不正確だった**。ユーザー指摘の通り、過去のRoot26
静的解析で使用したGhidra環境がこのPC上に残っていることを確認した:

```text
C:\Users\tanat\ghidra\ghidra_11.2.1_PUBLIC\ghidraRun(.bat)
C:\Users\tanat\ghidra\ghidra_11.2.1_PUBLIC\support\analyzeHeadless(.bat)
C:\Users\tanat\ghidra_project\Root26Project.gpr （既存プロジェクト、.rep一式あり）
C:\Users\tanat\ghidra_scripts\ 以下に既存の解析用Pythonスクリプト
  （find_callers.py / find_callers_by_scan.py / find_ptr_refs.py 等）
C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot （Java 21、動作確認済み）
```

53.6節の「実施不可」という結論は誤りだったため訂正する。既存の
`Root26Project`は（過去章の内容から）GameAssembly.dll
（IL2CPP側、`SteamPad`/`SteamInputUtil`等のC#実装を含む）を
対象としたものであり、`steamclient64.dll`（Valve純正のnative
Steamクライアント本体）とは別バイナリである点に注意。54.3節の
結論により、次に静的解析すべきはむしろ`steamclient64.dll`側
ではなく、既存`Root26Project`（GameAssembly.dll側）の延長で
`SteamPad.UpdateConnectedControllers()`本体
（`InputHandles`/`InputHandles_new`の差分マージ処理、複数handle
存在時の並び順生成箇所）である。

### 54.5 次の一手（54.3.1節の再検証を反映、未実施・提案）

54.3.1節で既に実施した通り、`InputHandles`配列（DEAD=index0,
LIVE=index1、セッション中不変）とRSTICK復旧タイミングの突き合わせは
**完了しており、単純な「配列先頭が勝つ」仮説を否定する結果になった**。
次に必要なのは、この配列とGameAssembly内部の実際のnative呼び出し
経路との関係を、Ghidraで直接確認することである。

1. 既存`Root26Project`（GameAssembly.dll、新規インストール不要）で、
   `SteamInputUtil.UpdateInput()`本体（RVA `0x25F9E10`、30.1節で
   特定済み）を再逆アセンブルし、`0x182602C40`
   （`controllerArray`取得呼び出し、30.2節）が実際に読む配列が
   `SteamPad.InputHandles`フィールドそのものか、それとも
   毎フレーム独立に取得される別の一時配列かを確認する。
2. `SteamPadSet(int index)`（RVA `0x2602F30`）に渡される`index`
   引数の実際の生成元命令列を確認し、それが`InputHandles`配列の
   位置と一致するインデックスなのか、`Controller`辞書の別の
   走査順（例: 挿入順・handle値順）なのかを特定する。
3. 26.2節で未解決のまま残っている`dds3PrivatePadAnalog[uVar20]`の
   `uVar20`が「controller index」なのか「stick_lr」なのかを、
   周辺の命令列（ループ境界・他の次元との対応）から再確認する。
   これが「stick_lr」だった場合、controller選択はこの経路とは
   全く別の場所で行われていることになり、54.3節の経路図自体を
   見直す必要がある。

いずれもGameAssembly.dll側の既存Ghidraプロジェクトに対する
read-only静的解析であり、Steamバイナリへのpatch/injection/hookは
不要。実機テストの追加も不要（既存ログの再解析＋静的解析のみで
進められる）。

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 55. 既存`Root26Project`によるGhidra read-only再解析（2026-09-08）

54.5節の方針に従い、既存の`Root26Project`（GameAssembly.dll、
新規インストールなし、`analyzeHeadless`＋既存`ghidra_scripts`と
同一の手法）で、`SteamInputUtil.UpdateInput()`・
`SteamPad.SteamPadSet(int)`・`func_0x182602c40`
（controllerCount取得）・`dds3PadManager.dds3PadUpdate()`を
zero-baseで再逆アセンブル・再デコンパイルした。Steamバイナリへの
patch/injection/hookは行っていない。新規実機テスト・実装・build/
deployも行っていない。

### 55.1 `func_0x182602c40`（controllerCount取得） — CONFIRMED

```text
int func_0x182602c40(SteamPad this, int unused=0)
{
    if (!this.field_0x18 /* bool、bInitialized相当 */) return 0;
    if (!SteamManager.get_Initialized())              return 0;

    array = this.field_0x30;   // ★配列フィールド、要素はulong相当
    if (array == null) return 0;

    count = 0;
    for (pos = 0; pos < array.Length; pos++) {
        if (array[pos] != 0) count++;   // 0でない要素の個数を数えるだけ
    }
    return count;
}
```

**CONFIRMED**: この関数は`this.field_0x30`という配列の**非ゼロ要素数を
数えるだけ**であり、要素の並び替え・圧縮（compaction）は一切行わない。
戻り値`count`は、`UpdateInput()`のループ回数（`iVar7`）として使われる。

### 55.2 `SteamInputUtil.UpdateInput()`の実際のループ構造 — CONFIRMED（更新、54.3節を精緻化）

既存bounds（`getFunctionContaining`）で正常にデコンパイルできた
（1717バイト、既に適切な関数境界が登録済みだった）。

```text
void SteamInputUtil.UpdateInput(SteamInputUtil this)
{
    // (digital button repeatカウンター処理、既存23.1節通り、省略)

    steamPad = this.field_0x50;              // SteamPadインスタンス参照
    if (steamPad == null) return;

    count = func_0x182602c40(steamPad, 0);   // 55.1節、非ゼロ要素数

    // digital側の初期化ブロック（省略、AnalogStickLRvalとは無関係）

    for (pos = 0; pos < count; pos++) {
        handleArrayObj = steamPad.field_0x30;      // ★UpdateInput側は+0x30を参照
        handle = handleArrayObj[pos];               // 生の配列読み出し、位置=pos
        if (handle != 0) {
            found = ContainsKey(steamPad.field_0x10 /* Controller dict */, handle);
            if (found) {
                bitmask = SteamPadSet(steamPad, pos, null);   // ★引数はposそのもの（handleではない）
                // ...digital button側の後処理（AnalogStickLRvalとは別系統、省略）
            }
        }
    }
}
```

**CONFIRMED**: `SteamPadSet`の`index`引数は、`steamPad.field_0x30`
という配列の**生の配列位置（pos＝0,1,2,...）**そのものであり、
handle値そのものではない。

### 55.3 `SteamPad.SteamPadSet(int index)`の実際の内部動作 — CONFIRMED（本節が最大の前進、28.3節を精緻化・訂正）

既存bounds未登録だったため生の命令列を直接確認した。28.3節の
「`this.Controller[index]`を取得」という要約は**不正確**だったと
判明したため訂正する。

```text
undefined8 SteamPad.SteamPadSet(SteamPad this, int index, void* unused)
{
    handleArray = this.field_0x20;             // ★SteamPadSet側は+0x20を参照
    if (handleArray == null) return default;
    if ((uint)index >= handleArray.Length) return default;   // 範囲外アクセス防止

    handle = handleArray[index];                // ★handle値はここで初めて取得される

    dict = this.field_0x10;                     // Controller辞書（Dictionary<ulong,InputInfo>と推測）
    if (dict == null) return default;
    found = ContainsKey(dict, handle);
    if (!found) return default;

    inputInfo = TryGetValueOrIndexer(dict, handle);   // handleをキーに実際のInputInfoを取得
    if (inputInfo == null) return default;

    // 28.3節で確認済みのControllerType==10分岐、以降は変更なし
    handleArray2 = this.field_0x20;             // 同じ配列を再度参照
    handle2 = handleArray2[index];              // 同じ手順でhandleを再取得
    controllerType = func_0x1828a12b0(handle2, 0);
    isType10 = (controllerType == 10);
    // ...（以降、i=0,1のループでGetAnalogActionData→SetAnalogへ、既存28章通り）
}
```

**CONFIRMED（訂正、重要）**:
1. `SteamPadSet(index)`は`Controller`辞書を**`index`という小さい整数で
   直接引かない**。正しくは、`index`は**別の配列
   （`this.field_0x20`）の位置**であり、その位置から得られた
   **64bitのhandle値**を使って初めてControllerディクショナリを
   引く、という2段階の間接参照になっている。
2. `AnalogStickLRval[index]`（28章）へ書き込まれる`index`は、
   **配列上の生の位置**であり、handle値そのものでも、
   `Controller`辞書のキー順でもない。

**UNRESOLVED（新規、重要）**: `UpdateInput()`が参照する
`steamPad.field_0x30`（55.2節）と、`SteamPadSet`が参照する
`steamPad.field_0x20`（本節）は、**オフセットが異なる別々の
フィールド**である。15.3節で存在確認済みの`InputHandles` /
`InputHandles_new` / `InputHandles_id`という3本の配列のうち、
どれが`+0x20`でどれが`+0x30`かは今回まだ特定していない。
`Root26SteamPadDeepStateProbe`（52章）のログでは、この3配列は
セッションを通じて常に同一内容・同一順序
（`[19680159496504676, 91728467815138660, 0,...]`）だったため、
今回のケースでは`+0x20`と`+0x30`が食い違っている証拠はまだ
ないが、**別フィールドである以上、原理的に食い違い得る**という
可能性自体は残しておく。

### 55.4 `dds3PadManager.dds3PadUpdate()`の外側ループ変数 — CONFIRMED（26.2節UNRESOLVED項目4に決着）

exact-bounds（RVA `0x222D960`、長さ`0x11CA`=4554バイト、24.1節の
既存値）で再構築し、全文をデコンパイルした上で`uVar20`の全出現箇所を
機械的に抽出した。

**CONFIRMED**: 外側ループ変数`uVar20`は**controller/pad側の
インデックス**であり、`stick_lr`ではない。根拠:

```text
uVar20 = 0;
while (true) {
    padHandle = func_0x182141080(uVar20, 0);   // ★新規発見、未解析★
    if (padHandle == 0) break;                  // 無効なら即座にループ全体を終了

    src = AnalogStickLRval[uVar20];   // _DAT_182e4c718+0xb8+0x70 経由、55.3節のindexと同一系列
    (uVar19 = 0..1 の内側ループで src[uVar19] を読み、byte変換)

    dst = dds3PrivatePadAnalog[uVar20];   // _DAT_182e44688+0xb8+0x18 経由
    (uVar18 = 0..1 の内側ループで dst[uVar18] へ書込み)

    uVar20 = uVar20 + 1;
    if (uVar20 > 1) { func_0x18222c3b0(0); ...; break-or-continue-to-caller-logic; }
    // (uVar20<=1ならwhile(true)の先頭へ戻る)
}
```

`AnalogStickLRval[uVar20]`・`dds3PrivatePadAnalog[uVar20]`ともに
**同一の`uVar20`でindex保存のままコピー**されており、選択・
並び替えは行われない（26.2節の暫定結論を再確認）。

### 55.5 新規最重要候補: `func_0x182141080(uVar20, 0)` — UNRESOLVED（未解析、次の最優先）

**CONFIRMED（構造のみ）**: `dds3PadUpdate()`の外側ループは、
`uVar20`番目のスロットを処理する**前に必ず`func_0x182141080(uVar20,
0)`を呼び、その戻り値が0なら（`uVar20`が0の時点でも）ループ全体を
即座に終了する**。

これは今回の調査で初めて発見された関数であり、内部は未解析。
**この関数こそが「どのスロットを実際に有効な物理コントローラーと
みなすか」を判定している最有力候補**である。仮にこの関数が
`AnalogStickLRval`や`this.field_0x20/0x30`とは独立に、Steam側の
別の状態（例:`CurrentInputID`相当、またはSteamworks
`GetConnectedControllers`とは別の「アクティブなコントローラー」
判定API）を参照しているなら、54.3.1節で見つかった
「`InputHandles`はDEAD側が先頭なのに、実際にはLIVE側の
データがカメラへ届く」という矛盾を解消できる可能性が高い。

### 55.6 総括

```text
Steam Input controllerHandle
        ↓
steamPad.field_0x20[index] → handle          ← ★index=配列上の生位置（55.3節、CONFIRMED）
        ↓ (handleでControllerディクショナリを引く)
GetAnalogActionData(handle, actionHandle)
        ↓
SetAnalog(index, i, x, y)
        ↓
AnalogStickLRval[index][i]                    ← indexは上と同じ生位置（CONFIRMED）
        ↓ (index保存のまま、選択なし)
dds3PadUpdate(): for (uVar20=0..1) { if (func_0x182141080(uVar20,0)==0) break; ... }
        ↓
dds3PrivatePadAnalog[uVar20][stick]           ← uVar20はcontroller/pad側（55.4節、CONFIRMED）
        ↓ (未解決の別リンク、26.2節UNRESOLVED項目3のまま)
dds3PadAnalog[?]
        ↓
GetPadAnalog(padno=0, ...)                    ← padno=0はリテラル固定（4.1節、CONFIRMED）
```

54.3.1節の矛盾（`InputHandles`配列順ではDEADが先頭なのに、実際は
LIVE側がカメラへ反映される）は、**まだ解消されていない**。次の
最優先候補は55.5節の`func_0x182141080`の解析、および
`steamPad.field_0x20`と`field_0x30`が本当に同一配列
（`InputHandles`）を指しているかどうかの確認である。後者は
静的解析だけでは限界があり（フィールド宣言順の裏付けとなる
Il2Cppメタデータ/dump.csに相当するものが本セッションでは
見つかっていない）、確実な決着にはread-only runtime probe
（例: `Marshal`経由で`field_0x20`と`field_0x30`を直接読み、
managedの`InputHandles`/`InputHandles_new`/`InputHandles_id`
プロパティ値と突き合わせる）が必要になる可能性が高い。**ユーザー
承認により、57章で実装・ビルド・デプロイまで完了した。**

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 56. `func_0x182141080`の完全解析、`dds3PrivatePadAnalog→dds3PadAnalog`の追跡（2026-09-08）

既存`Root26Project`（GameAssembly.dll、新規インストールなし）で
追加のread-only解析を行った。新規実機テスト・実装・build/deploy・
Git writesは行っていない。Steamバイナリへのpatch/injection/hookも
行っていない。

### 56.1 `func_0x182141080(padno, unused)` — CONFIRMED（完全解析）

命令列を直接確認した結果:

```text
undefined8 func_0x182141080(int padno, int unused)
{
    steamInputUtilInstance = *(longlong*)0x182e4c718;   // 静的singleton参照（AnalogStickLRvalと同一の起点）
    // (遅延初期化チェック、省略)

    obj = steamInputUtilInstance.field_0xb8;             // ★AnalogStickLRvalチェーンと同じ+0xb8
    array = obj.field_0x8;                                // ★新規: +0x8という別配列
    if (array == null) throw;
    if ((uint)padno >= array.Length /* +0x18 */) throw;

    return array[padno];   // +0x20 + padno*8 から読む生の8byte値（handle相当）
}
```

**CONFIRMED**: この関数は`dds3PadUpdate()`のループが各`uVar20`
（controller/padスロット）を処理する**前に**、`steamInputUtilInstance
.field_0xb8.field_0x8[uVar20]`という**第3の配列**を参照し、その値が
0なら**ループ全体を即座に打ち切る**。この`+0x8`は、55.3節で確認した
`SteamPadSet`側の`+0x20`とも、`UpdateInput`側の`+0x30`とも異なる
**別のオフセット**である。同じ`_DAT_182e4c718+0xb8`という起点
（`AnalogStickLRval`と共通）を使っているため、到達する「object」は
`AnalogStickLRval`と同じ管理オブジェクトである可能性が高いが、
55.3節の`+0x20`/`+0x30`は**別の到達経路**（`SteamInputUtil.this.
field_0x50`経由）で得られたSteamPadインスタンスに対するオフセット
であり、両者が本当に同一オブジェクトを指しているかは未確認のまま
残る。

**HYPOTHESIS（重要）**: `+0x8`・`+0x20`・`+0x30`という3つの配列は、
15.3節で存在確認済みの`InputHandles`/`InputHandles_new`/
`InputHandles_id`という3本の管理フィールドにそれぞれ対応している
可能性が高い（3対3で数が一致）。ただしどの配列がどの管理フィールド
名に対応するかは、本節時点でもまだ確定していない。

**再評価（ユーザー指摘の通り）**: この関数は、あくまで
「そのスロットを処理するかどうか」の**ゲート**であり、
`AnalogStickLRval[uVar20]`自体の**内容を選ぶ・置き換える処理では
ない**。ゲートを通過した後、`dds3PadUpdate()`は依然として
`AnalogStickLRval[uVar20]`（`SteamPadSet`が`+0x20`から得たhandleで
埋めたもの）をそのまま読む。したがって、もし`+0x8`の配列が
`+0x20`/`+0x30`と同じ並び順を持つなら、この関数は真の分岐点には
ならない。**真の分岐点になり得るのは、`+0x8`が`+0x20`/`+0x30`と
異なる並び順・内容を持つ場合に限られる**（未確認）。

### 56.2 `dds3PadManager.GetPadAnalog()` — CONFIRMED（完全解析、26.2節UNRESOLVED項目3に前進）

```text
byte GetPadAnalog(int padno, int stick_lr, int xy, int cip_no)
{
    obj = _DAT_182e44688.field_0xb8;      // dds3PadManagerインスタンス
    arr1 = obj.field_0x30;                 // ★読むのは+0x30（"dds3PadAnalog"、公開側と推測）
    ... bounds check padno ...
    arr2 = arr1[padno];
    ... bounds check stick_lr ...
    arr3 = arr2[stick_lr];
    ... bounds check xy ...
    value = arr3[xy];         // +0x20から読む1byte、中心値0x80(128)扱い
    (value==0x80の場合のみ、FUN_1822a7900による追加のRight Stick専用処理・25章参照)
    return value;
}
```

**CONFIRMED**: `GetPadAnalog()`が実際に読むのは
`dds3PadManagerインスタンス.field_0x30`である。26.2節で確認済みの
`dds3PadUpdate()`の書き込み先（`field_0x18`、いわゆる
`dds3PrivatePadAnalog`）とは**異なるオフセット**であり、
「`dds3PrivatePadAnalog`（+0x18）」と「`GetPadAnalog`が読む先
（+0x30）」が同一のオブジェクト内の別々のフィールドであることが
命令列レベルで確定した。

### 56.3 `dds3PrivatePadAnalog → dds3PadAnalog`のbridge関数 — CONFIRMED（否定的結果）/ HYPOTHESIS（別解）

Ghidra自身のreference manager（手動バイトスキャンではなく、
`getReferencesTo()`による正規のxref解決）で、
`_DAT_182e44688`（dds3PadManagerの静的singleton参照）を直接参照する
**全関数**を列挙した。

**CONFIRMED（否定的結果）**: `_DAT_182e44688`を直接参照する関数は
GameAssembly.dll全体で**2つだけ**である:

```text
0x18222C1C0  GetPadAnalog()           - field_0x30を読む（56.2節）
0x18222D960  dds3PadUpdate()          - field_0x18へ書く（26.2節）
```

さらに、`dds3PadUpdate()`の全文デコンパイル結果（4554バイト全体）を
文字列検索した結果、**`0x30`という数値は一度も出現しない**ことを
確認した。つまり`dds3PadUpdate()`自身が`+0x30`（`GetPadAnalog`が
読む側）へ書き込むことも一切ない。

**結論（否定的結果、CONFIRMED）**: GameAssembly.dll内には、
`dds3PrivatePadAnalog`（+0x18）から`dds3PadAnalog`（+0x30）へ
明示的にコピーする「bridge関数」は**存在しない**。

**HYPOTHESIS（有力、次善の説明）**: 明示的なコピー処理が存在しない
にもかかわらず、`dds3PadUpdate()`が書いたデータが`GetPadAnalog()`
経由で実際にゲームへ反映されている（RSTICK復活が観測されている）
という既存事実と両立させるには、**`field_0x18`
（`dds3PrivatePadAnalog`）と`field_0x30`（`GetPadAnalog`が読む方）
が、初期化時に同一の配列オブジェクトへの参照として設定されている
（別名・エイリアス）**、つまり「privateフィールドとpublic
フィールドが実体として同じ配列を指しており、明示的なコピー自体が
そもそも不要」という可能性が最も高い。これはCONFIRMEDではないが、
「bridge関数が存在しないのにデータが伝播している」という観測結果
と矛盾なく説明できる、現時点で最も単純な仮説である。

**評価**: この仮説が正しい場合、26.2節UNRESOLVED項目3
（`dds3PrivatePadAnalog→dds3PadAnalog`のリンク）は「未発見の
bridge関数がある」という前提自体が誤りだったことになり、
実質的にクローズしてよい。ここに「どちらのcontrollerを選ぶか」の
分岐が隠れている可能性は低いと判断する。

### 56.4 総括: 静的解析で追える範囲の結論

```text
Steam Input controllerHandle
        ↓
steamPad.field_0x20[index] → handle          （55.3節、CONFIRMED）
        ↓
GetAnalogActionData(handle, actionHandle) → SetAnalog(index,i,x,y)
        ↓
AnalogStickLRval[index][i]                    （CONFIRMED）
        ↓ (index保存のまま、選択なし、55.4節)
dds3PadUpdate():
    for (uVar20=0..1) {
        if (steamInputUtilInstance.field_0xb8.field_0x8[uVar20] == 0) break;  ← ゲートのみ（56.1節）
        dds3PrivatePadAnalog[uVar20] = transform(AnalogStickLRval[uVar20])     ← +0x18へ書込み（26.2節）
    }
        ↓ (56.3節HYPOTHESIS: +0x18と+0x30は同一配列のエイリアス、bridge関数は存在しない)
GetPadAnalog(padno=0, ...) が dds3PadManager.field_0x30 を読む       ← padno=0はリテラル固定（4.1節、CONFIRMED）
        ↓
Camera
```

本調査全体を通じて、静的解析だけで到達できる範囲では、
**「どちらのcontroller handleがカメラに反映されるか」を実際に
分岐させ得る箇所は、`steamPad.field_0x20`（`SteamPadSet`が
handle取得に使う）・`field_0x30`（`UpdateInput`がループに使う）・
`field_0x8`（`func_0x182141080`のゲートに使う）という3本の配列
フィールドが、実際には同じ内容・同じ並び順を常に維持しているのか
どうか、という一点に収束した**。この3本がそれぞれ`InputHandles`/
`InputHandles_new`/`InputHandles_id`のどれに対応するか、また
それらが本当に常に同期しているかは、この環境で入手可能な
Il2Cppメタデータ相当の裏付けが見つからず、**静的解析のみでは
これ以上決着できない**。

次に必要なのは、この3本の配列を実機で直接比較するread-only
runtime probe（`Marshal`経由でnative offset `+0x8`/`+0x20`/`+0x30`を
直接読み、managed `InputHandles`/`InputHandles_new`/
`InputHandles_id`プロパティの値と突き合わせる）である。**ユーザー
承認により、57章で実装・ビルド・デプロイまで完了した。**

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 57. `Root26FieldOffsetMapProbe`実装・ビルド・デプロイ（2026-09-08）

56章までの静的解析結果を受け、ユーザー承認のもとread-only runtime
probeを実装した。目的は、native offset`+0x8`/`+0x20`/`+0x30`が
それぞれ`InputHandles`/`InputHandles_new`/`InputHandles_id`の
どれに対応するか、および3配列がDEAD/LIVE遷移前後で同じ内容・
同じ並び順を維持するかを実測で確認すること。

### 57.1 実装内容（`src/Root26FieldOffsetMapProbe.cs`）

- **完全read-only**: `Marshal.ReadIntPtr`/`ReadInt32`/`ReadInt64`に
  よるnative offset直接読み取りのみ。書き込みは一切行わない。
- **`ResetController`/`SteamControllerReStart`/`Shutdown`/`Init`/
  `UpdateConnectedControllers`等の手動呼び出しなし**。F9/F10・
  SendInput・Guide偽装・Steamバイナリへのpatch/injection/hookも
  一切なし。
- **BASE-CHECK**（1回のみ）: `SteamInputUtil.instance.Pointer+0xb8`
  と`SteamInputUtil.instance.steam_pad.Pointer`が同一かどうかを
  直接比較してログ出力する（56.1節の未確認だった前提を検証）。
- **3本のnative配列**を`+0x8`（`util.Pointer+0xb8`起点）・`+0x20`・
  `+0x30`（いずれもSteamPadインスタンス起点）から、55章・56章で
  確認済みのIL2CPP SZ配列レイアウト（長さ`+0x18`、要素`+0x20`から
  8byte刻み）でそのまま読み取る。
- 同時に**既存のmanagedプロパティ**（`pad.InputHandles`/
  `InputHandles_new`/`InputHandles_id`）も読み、native側と
  並べてログ出力する。
- 変化時のみSTATE-CHANGEログ、加えて5秒間隔のHEARTBEATログ
  （ログ肥大化防止）。
- `ModMain.OnUpdate()`から無条件（`FieldDashPatch.IsExplorationActive`
  に依存しない）で呼び出し、Guide/Overlayウィンドウ中も観測を
  継続する（既存probeと同じ設計方針）。

### 57.2 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```text
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   ba23b1938c8aa8ce439d8b5fefbbd2bdb6c9fd8cf6f74c3e003ef6faa8e62b66
SHA-256 (deployed): ba23b1938c8aa8ce439d8b5fefbbd2bdb6c9fd8cf6f74c3e003ef6faa8e62b66
  → 一致

配置先: C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.dll
```

### 57.2.1 実機テスト前の観測汚染防止（ユーザー指摘、CONFIRMED）

ユーザー指摘を受け、実機テスト前に`ModMain.OnUpdate()`を点検し、
今回の「通常起動→DEAD確認→物理Guide→LIVE確認」という観測を
汚染し得る**Steam Input状態を変更するPoCの呼び出し**を無効化した。

```text
無効化（コメントアウト、コードは削除せず呼び出しのみ停止）:
  Root26Phase8OverlayToStoreOpenPoc.Sample();
    → 探索開始から約5秒後にActivateGameOverlayToStore()を自動発火する
      PoC。今回のテスト中に自動的にOverlayが開いてしまい、物理Guide
      による観測と混在する恐れがあったため停止。
  Root26Phase4NativeRecoveryPoc.Sample();
    → F10キー押下でSteamInput.Shutdown()/Init()を呼ぶPoC。F10は
      本来押す予定がないが、誤操作防止のため今回のテストbuildでは
      呼び出し自体を停止。

継続（read-only、観測に必要）:
  Root26KernelLoopFrequencyProbe.Sample();
  Root26FieldOffsetMapProbe.Sample();
  その他の既存Root26系probe（Root26SteamStateProbe/
  Root26SteamPadDeepStateProbe/Root26SteamPadOffset18Probe/
  Root26Phase1ConditionProbe/Root26Phase2AnalogActionDataProbe/
  Root26Phase3PostResetWatchProbe等）はすべてread-only（Marshal
  読み取り・Harmony Prefixでの観測のみ）であることをソース確認済み。
```

`src`ディレクトリ全体を`ResetController()`/`SteamControllerReStart()`/
`SteamInput.Shutdown()`/`SteamInput.Init()`/
`ActivateGameOverlayToStore`/`UpdateConnectedControllers()`の
呼び出し箇所で横断検索し、実際にこれらを**呼び出している**ファイルは
`Root26Phase4NativeRecoveryPoc.cs`（F10ゲート、無効化済み）と
`Root26Phase8OverlayToStoreOpenPoc.cs`（自動発火、無効化済み）の
2つのみであることを確認した（`ManualResetControllerPoc.cs`・
`SteamControllerReacquisitionProbe.cs`はそもそも`ModMain.cs`から
呼ばれていない退役済みコード）。他はすべて名前の通りログ出力・
Harmony Prefixでの観測のみで、Steam Input状態を変更する呼び出しは
含まれていない。

再ビルド・再デプロイ・ハッシュ再確認:

```text
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   2f4737bff670945b03dbf7f2e2703f69bea45716dfaede11404588e72e67e243
SHA-256 (deployed): 2f4737bff670945b03dbf7f2e2703f69bea45716dfaede11404588e72e67e243
  → 一致
```

また、`Root26FieldOffsetMapProbe`のログ表記も、BASE-CHECKで
`util.Pointer+0xb8 == pad.Pointer`を検証する前から「SteamPad+0x08」
と呼ぶのはEvidence上不正確という指摘を受け、以下に修正した:

```text
旧: native SteamPad+0x08 / SteamPad+0x20 / SteamPad+0x30
新: util+b8:+0x08（util.Pointer→+0xb8→+0x08経由）
    steamPad:+0x20（pad.Pointer→+0x20、到達経路が異なる別起点）
    steamPad:+0x30（pad.Pointer→+0x30）
```

### 57.3 次の実機テスト手順（ユーザー実施待ち）

1. 通常起動。
2. 探索状態へ入り、右スティックがDEAD（`ACTIVE-ZERO`、動かしても
   反応なし）であることを確認する。
3. 既存の安全な操作（物理Guideボタン1回、またはSteam Overlayを
   開閉する操作）だけでLIVE化を試す。**F9/F10・手動API呼び出し・
   SendInput・Guide偽装は使用しない。**
4. LIVE化を確認できたら（できなくても）、ゲームを終了する。
5. `Latest.log`を提供する。

**確認したい内容**（判定基準）:

- `[Root26FieldOffsetMap] BASE-CHECK`の`SAME-AS-PAD`が`True`か
  `False`か。
- `util+b8:+0x08`・`steamPad:+0x20`・`steamPad:+0x30`の3つが、
  セッションを通じて常に同じ内容・同じ並び順を維持しているか、
  それともどこかで食い違うか。
- `19680159496504676`と`91728467815138660`が、3配列それぞれの
  何番目に位置しているか。
- 上記が、`managed pad.InputHandles`/`InputHandles_new`/
  `InputHandles_id`のどれと一致するか。

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 58. `Root26FieldOffsetMapProbe`実機テスト結果（2026-09-08 08:29台）

57章で実装したprobeによる実機テストを実施した。今回は57.2.1節の
汚染防止措置により、Store API/Shift+Tab/Alt+Tabの介入なく、
「通常起動→DEAD確認→物理Guide 1回のみ→LIVE確認」という
クリーンな単一操作のテストになった。

### 58.1 タイムライン — CONFIRMED

```text
08:29:43.967  BASE-CHECK util.Pointer+0xb8=0x0 pad.Pointer=0x233F4C00660
              SAME-AS-PAD=False（controller接続前、両方とも初期状態）
08:29:43.968  util+b8:+0x08 INITIAL -> OBJ-NULL
              steamPad:+0x20 / +0x30 / managed 3配列: すべて0埋め配列

08:29:45.621-627  SteamControllerReStart() count=1,2（初回接続処理）
08:29:45.650-651  steamPad:+0x20 / steamPad:+0x30 / managed.InputHandles /
                  managed.InputHandles_new / managed.InputHandles_id が
                  **同時に、完全に同一の内容**へ変化:
                  [19680159496504676, 91728467815138660, 0,0,...]
                  （以降セッション終了まで、この5つに一切の
                  STATE-CHANGEは記録されなかった＝内容・並び順が
                  完全に固定）

08:30:04.754-05.519  Overlay ON(m_bActive=1)→OFF(m_bActive=0)（物理Guide）
08:30:05.520-522     ResetController() count=1,2 → SteamControllerReStart() count=3,4
08:30:06.428         [Root26Phase2] controllerHandle=91728467815138660
                     i=1(IG_RSTICK) state=ACTIVE-NONZERO x=-0.0418 y=0.0190
08:30:06.789         [Root26NativePoll] STATE-TRANSITION X/Y DEAD -> LIVE
                     （Reset から約1.27秒後）
```

### 58.2 CONFIRMED（重要な新事実）

1. `steamPad:+0x20`（`SteamPadSet`の handle取得元）・`steamPad:+0x30`
   （`UpdateInput`のループ/事前チェック元）・managedの
   `InputHandles`/`InputHandles_new`/`InputHandles_id`の**5つすべて**
   が、接続直後の一瞬（08:29:45.650-651）に**完全に同時・完全に
   同一の内容**へ変化し、以降セッション終了（Guide成功を含む）まで
   **一度も食い違わなかった**。native側を直接読んだ実測により、
   **`+0x20`/`+0x30`/managed 3フィールドが食い違っているという
   HYPOTHESISはREJECTED**する。**ただし`util+b8:+0x08`は58.4節の
   通り有効観測できなかったため、`+0x08`を含めた3配列全体の
   一致・不一致はUNRESOLVEDのまま**とする（`+0x20`/`+0x30`/managed
   側だけでの結論であり、`+0x08`側の食い違いの可能性はまだ
   排除できていない）。
2. `uVar24`（`dds3PadUpdate()`の外側ループ変数`uVar20`の初期値）が
   単純なリテラル`0`であることを、既存デコンパイル結果
   （`uVar24 = 0;`という直接代入）で確認した。動的な開始位置
   （`CurrentInputID`等に連動する可能性）は**REJECTED**する。

### 58.3 REJECTED（範囲限定）: `+0x20`/`+0x30`/managed 3フィールドの食い違い仮説

54.3.1節以来の中心的な疑問（`InputHandles`配列ではDEAD側handle
`19680159496504676`が先頭＝index0、LIVE側handle
`91728467815138660`がindex1にもかかわらず、実際にカメラへ反映
されるのはindex1側のデータである、という矛盾）は、
「`SteamPadSet`（`+0x20`参照）と`UpdateInput`（`+0x30`参照）が
実は異なる配列を参照しており食い違っている」という説明では
**説明できないことが確定した**。この2つ、およびmanaged側3
プロパティは常に同一である。**`func_0x182141080`が参照する
`+0x08`側については58.4節の通り観測できておらず、この矛盾の
説明としてまだ完全には排除されていない**点に注意する。

### 58.4 UNRESOLVED: `util+b8:+0x08`は終始`OBJ-NULL`（probe設計上の限界、CONFIRMED否定的結果）

`util+b8:+0x08`（`func_0x182141080`が参照する第3の配列、
`util.Pointer→+0xb8→+0x8`経由）は、セッション開始から終了まで
**一度も`OBJ-NULL`から変化しなかった**。

`func_0x182141080`自身の命令列（56.1節）には、`_DAT_182e4c718`を
読む前に、その対象オブジェクトの`+0x12f`ビットと`+0xe0`フィールドを
チェックし、条件を満たせば`func_0x180079560`という「遅延初期化
確保」らしきヘルパーを呼んでから**再読込する**という分岐が存在する
（`UpdateInput()`内の複数箇所にも同型のパターンが繰り返し現れる）。
本probeはこの遅延初期化ヘルパーを**一切呼ばず、`Marshal`による
直接読み取りのみ**を行っている。ゲームは明らかに正常に動作して
いた（LSTICK・デジタル入力等は機能していたと推測される）ため、
`func_0x182141080`が実際のゲーム実行時に常にNULLを見ているとは
考えにくい。

**結論（CONFIRMED、否定的結果）**: 本probeの`util+b8:+0x08`読み取り
手法は、この遅延初期化ヘルパーを再現していないため、**この経路に
関しては有効なデータを得られなかった**。`+0x8`配列の内容について、
今回のテストからは何も結論できない（UNRESOLVEDのまま）。

### 58.5 総括: 核心の矛盾は依然UNRESOLVED、むしろ深まった

58.2〜58.4の結果により、55章・56章で有力候補として残っていた
「3配列（`+0x8`/`+0x20`/`+0x30`）が食い違っている」という仮説は
実測でREJECTEDされた。`steamPad:+0x20`・`steamPad:+0x30`は常に
同一であり、`AnalogStickLRval[index]`を書く`SteamPadSet(index)`も
`UpdateInput()`のループも、同じ配列・同じ並び順を参照している。

にもかかわらず、55.6節で整理した経路
（`steamPad:+0x20[0]`→`SteamPadSet(0)`→`AnalogStickLRval[0]`→
`dds3PrivatePadAnalog[0]`→`GetPadAnalog(padno=0)`）を素直に辿れば、
`index=0`側（DEADなhandle`19680159496504676`）のデータが
カメラへ反映されるはずである。しかし実際に観測されたのは
`index=1`側（LIVEなhandle`91728467815138660`）のデータだった。

**この矛盾は、55章・56章で構築したデータフロー理解のどこかに、
まだ発見できていない誤り・見落としが残っていることを強く示唆する。**
候補として、`SteamInputUtil.SetAnalog()`が実際に書き込む配列
添字が、`SteamPadSet`から渡される`index`パラメータそのもので
あるかどうかを28章時点の解析のまま信頼してよいか（今回のセッション
では再検証していない）、および`dds3PadUpdate()`の外側ループが
`func_0x182141080`の戻り値を単なる真偽判定以上の用途（例:
実効的なpad番号の算出）に使っている可能性がないか、をUNRESOLVEDの
まま次点候補としたい。

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 59. `SteamInputUtil.SetAnalog()`のindex変換ロジックを発見（2026-09-08）

58章の実測結果（`+0x20`/`+0x30`/managedが常に同一なのに、なぜ
index1側のデータがカメラへ届くのか）を受け、ユーザー指示に従い
`SteamInputUtil.SetAnalog()`（RVA`0x25F93E0`）をzero-baseで
命令列から再解析した。既存Root26Project（GameAssembly.dll）への
read-only解析のみ。新規probe・build・deploy・実機テストは行って
いない。

### 59.1 呼び出し規約の確定 — CONFIRMED（28章の記述を修正）

`SteamPadSet(int index)`から`SetAnalog`への実際の呼び出し箇所
（VA`0x1826031a0`）の直前の引数セットアップを確認した:

```text
182603168: CALL 0x1825fc4b0        ; SteamInputUtilシングルトン解決
18260316d: TEST RAX,RAX / JZ ...   ; null bail
182603178: LEA RDX,[RSP+0xc0]      ; RDX = &ret（bitmask格納先）
182603186: MOV R9D,EDI             ; R9D = i（0=LSTICK/1=RSTICK側ループ変数）
18260318e: MOV R8D,R13D            ; R8D = index（SteamPadSetのindex引数そのもの）
182603197: MOV RCX,RAX             ; RCX = SteamInputUtilインスタンス
1826031a0: CALL 0x1825f93e0        ; SetAnalog(SteamInputUtil this, ref ulong ret, int index, int i, float dx, float dy)
```

**訂正（CONFIRMED）**: 28.2節で「`SetAnalog(ref System.UInt64 ret,
System.Int32 index, ...)`」とした引数説明は不正確だった。実際の
第1引数（RCX）は**`ret`ではなく、`SteamInputUtil`インスタンス
そのもの**であり、`ret`は第2引数（RDX、ポインタ）である。`index`は
第3引数（R8D）で、`SteamPadSet`が保持する`index`パラメータ
（`R13D`）がそのまま渡っている——**この時点まではindexは一切
変換されていない**。

### 59.2 `SetAnalog()`内部: `EDI`という「実効index」への置換ロジックを発見 — CONFIRMED（命令列）/ HYPOTHESIS（意味）

`SetAnalog()`冒頭の引数受け取り部分:

```text
1825f93fd: MOV EBX,R8D      ; EBX = index（呼び出し元からの値、そのまま）
1825f9406: MOV R15,RDX      ; R15 = &ret
1825f9409: MOVSXD RSI,R9D   ; RSI = i
1825f940c: MOV R14,RCX      ; R14 = SteamInputUtilインスタンス（this）
```

続く分岐（X成分計算の直前）:

```text
1825f9423: MOVZX EAX,byte ptr [R14 + 0x20]   ; ★EAX = SteamInputUtilインスタンス.field_0x20（1byte）
1825f9458: XOR EDI,EDI                        ; EDI = 0
1825f945e: TEST AL,AL                         ; ZF = (AL==0)
1825f9460: CMOVZ EDI,EBX                      ; ZFなら EDI = EBX(index)　★条件付き代入★
1825f9463: JZ 0x1825f94f2                     ; ZFなら（=AL==0なら）Xはゼロ埋め経路へ
1825f9469: TEST EBX,EBX
1825f946b: JZ 0x1825f94f2                     ; index==0でもゼロ埋め経路へ
1825f9471: (以降、AnalogStickLRval[EDI][i]を読む経路。ここに来る時点でEDIは
            常に0のまま——直前のCMOVZはAL==0の分岐でのみ発生し、その分岐は
            既にjmpで94f2へ抜けているため)
```

その後、X・Y両方の**実際の書き込み**（`AnalogStickLRval[EDI][i].x`/
`.y`への`MOVSS`）は、一貫して**この同一の`EDI`**を第1次元添字として
使っている（X書き込み: VA`0x1825f967f`、Y書き込み:
VA`0x1825f9722`、どちらも`RCX + RSI*8 + 0x20/0x24`という形で
`AnalogStickLRval[EDI][ESI]`のleafオブジェクトへ書く。`RCX`は
`AnalogStickLRval[EDI]`から`RSI`(=i)でさらに一段掘り下げたもの）。

**CONFIRMED（命令列上の事実）**:
1. `SetAnalog()`は、呼び出し元が渡した`index`（EBX）を**無条件には
   使わない**。`SteamInputUtilインスタンス+0x20`の1byteフラグの値に
   応じて、実際に`AnalogStickLRval`へ読み書きする際のindex
   （`EDI`）が、**`index`そのもの**になる場合と、**強制的に`0`**に
   なる場合に分岐する、という構造が実在する。
2. 具体的な分岐条件（本節時点の命令列読解）:
   - フラグ（`+0x20`）が`0`の場合: X成分はゼロ埋め経路へ進む
     （実際の`AnalogStickLRval`読み出しをスキップ）。
   - フラグが非`0`かつ`index==0`の場合: 同様にゼロ埋め経路。
   - フラグが非`0`かつ`index!=0`の場合のみ、`EDI=0`（`CMOVZ`が
     発火しなかったため初期値の0のまま）で`AnalogStickLRval[0]`を
     実際に読み書きする。
   - つまり、**このX成分の読み出しに関する限り、`AnalogStickLRval`の
     実際の読み書き対象は`index`の値に関わらず常に`EDI=0`
     （＝スロット0）になる**（実際に読み書きが行われる唯一の
     分岐がこの経路であるため）。

**HYPOTHESIS（意味、次点最有力）**: もしこの`+0x20`フラグが
「複数コントローラーの入力をplayer1用の単一スロット（index0）へ
まとめる」ような趣旨のフラグであり、かつ`UpdateInput()`のループが
`rdi=0`（DEAD側handle）→`rdi=1`（LIVE側handle）の順で
`SteamPadSet`を呼ぶ（30.2節でCONFIRMED済みの順序）とすれば、

```text
毎フレーム:
  SteamPadSet(0) → SetAnalog(..., index=0, ...) → AnalogStickLRval[0]へ書込み（DEAD側の値、x=y=0近辺）
  SteamPadSet(1) → SetAnalog(..., index=1, ...) → EDIが0に丸められ、同じくAnalogStickLRval[0]へ上書き（LIVE側の値）
```

という**「最後に処理されたcontrollerが常にslot 0を上書きする」**
という構造になり、54章以来の中心的な矛盾
（`InputHandles`ではDEADが先頭・LIVEが2番目なのに、カメラは
LIVE側を反映する）を**きれいに説明できる**。この場合、
「index0/index1のどちらが選ばれるか」という問いの立て方自体が
誤りで、実質的には**「configuration上どのcontrollerが最後に
`UpdateInput()`のループで処理されるか」**が真の分岐点だった、
という結論になる。

**UNRESOLVED（重要、次の最優先）**:
1. `SteamInputUtilインスタンス+0x20`という1byteフィールドの実際の
   意味・名称は未確定（HYPOTHESISの「player1への集約フラグ」は
   命令列の形からの推測であり、CONFIRMEDではない）。
2. このフラグが実機で実際にどの値を取っているか（DEAD時・LIVE時で
   変化するのか、常に一定なのか）は、本節時点では静的解析のみで
   実機観測していない。
3. `i`（LSTICK/RSTICK区別、ESI）に対して、このindex置換ロジックが
   両方の軸で完全に同一に働くか（本節ではX/Y両方が同じ`EDI`を
   使うことは確認済みだが、`i`自体の値がこの分岐に影響しないかは
   再確認が必要）。
4. ゼロ埋め経路（フラグ`0`、またはフラグ非`0`かつ`index==0`）へ
   進んだ場合、その後の`AnalogStickLRval`への**書き込み自体は
   行われるのか、それともスキップされるのか**（59.2節の解析は
   X成分の「読み出し・クランプ用ベースライン計算」部分に限定して
   おり、最終的な書き込み分岐の全体像は未確認）。

### 59.3 総括

`func_0x182141080`の戻り値は、55章・56章で懸念した「controller
選択」には一切使われておらず（58章時点で自己訂正、単なる
digital buttonビットマスクのAND演算にのみ使用、`58.4`節参照）、
**真の分岐点は`SetAnalog()`内部の`SteamInputUtilインスタンス+0x20`
フラグによる、`index`→`0`への強制置換ロジックであった可能性が
最も高い**。ただしこのフラグの意味・実機での挙動はまだ
UNRESOLVEDであり、次段はこのフラグをread-only runtime probeで
直接観測することが最有力候補になる（ただし新規probe実装は
ユーザー承認後に行う）。

### 59.4 `SetAnalog()`の最終store先を完全解析 — CONFIRMED（命令列、59.2節の懸念を解消）

ユーザー指示に従い、新規runtime probeへ進む前に`SetAnalog()`の
残り全体（既存の生ディスアセンブル結果に既に含まれていた分、
新規Ghidra実行なしで精査完了）を命令列レベルで最後まで追跡した。

```text
1825f9640: CMP EDI,[RCX+0x18]            ; 境界チェック（RCX=AnalogStickLRval配列本体）
1825f9649: MOVSXD RBP,EDI                ; ★RBP = EDI（59.2節のflag調整済みindex、そのまま再利用）
1825f964c: MOV RAX,[RCX+RBP*8+0x20]      ; RAX = AnalogStickLRval[EDI]
1825f9663: MOV RCX,[RAX+RSI*8+0x20]      ; RCX = AnalogStickLRval[EDI][i]（RSI=i、LSTICK/RSTICK）
1825f967f: MOVSS [RCX+0x20],XMM6         ; ★X書き込み: AnalogStickLRval[EDI][i].x = XMM6
...
1825f96f3: MOV RAX,[RAX+RBP*0x8+0x20]    ; 同じRBP(=EDI)で再度 AnalogStickLRval[EDI] を取得
1825f970a: MOV RCX,[RAX+RSI*8+0x20]      ; 同じRSI(=i)で AnalogStickLRval[EDI][i]
1825f9722: MOVSS [RCX+0x24],XMM12        ; ★Y書き込み: AnalogStickLRval[EDI][i].y = XMM12
```

**CONFIRMED（ユーザー確認事項への回答）**:

1. **X/Yの最終書き込みは、59.2節でflag（`SteamInputUtil+0x20`）に
   応じて確定した同一の`EDI`を、一貫して使っている。**
   途中で再計算・別の値への差し替えは一切ない（`RBP`は`EDI`の
   単なるsign-extend後の別名であり、X用・Y用それぞれで
   `AnalogStickLRval[EDI]`を独立に再フェッチしているが、
   フェッチに使うindexは常に同一の`EDI`である）。
2. **最終store先はX=`+0x20`、Y=`+0x24`**（同一leafオブジェクト内の
   隣接フィールド）。
3. **ゼロ埋め経路（59.2節の`94f2`ジャンプ）は書き込み自体を
   スキップしない。** 関数全体を通じて`AnalogStickLRval`への
   実際のstore命令（X用`0x1825f967f`、Y用`0x1825f9722`）は
   **それぞれ1箇所ずつしか存在せず**、すべての分岐（flag=0/
   flag≠0&&index=0/flag≠0&&index≠0）は最終的にこの同じstore
   命令へ合流する。異なるのは「クランプ計算に使うベースライン値
   （前回値0扱いか、実際に読んだ値か）」であり、「書き込むか
   どうか」ではない。
4. **`i`（LSTICK/RSTICK、`RSI`/`ESI`）は`EDI`の決定ロジックに
   一切関与しない。** `i`は`AnalogStickLRval[EDI][i]`の第2次元
   添字としてのみ使われ、59.2節のflag分岐（`CMOVZ EDI,EBX`から
   `94f2`分岐まで）には一度も登場しない。LSTICK・RSTICK両方が
   完全に同一のindex決定ロジックを共有する。

**59.2節の条件表を、本節の確認を踏まえて確定させる**:

```text
flag(SteamInputUtil+0x20) == 0
    → EDI = index（そのまま）→ AnalogStickLRval[index]へ書込み（従来通りの想定モデル）

flag != 0 かつ index == 0
    → EDI = 0（index==0なので実質的に差はない）→ AnalogStickLRval[0]へ書込み

flag != 0 かつ index != 0
    → EDI = 0（★indexが強制的に0へ丸められる★）→ AnalogStickLRval[0]へ書込み
      （本来のAnalogStickLRval[index]ではなく、slot 0を上書きする）
```

**結論（HYPOTHESIS、ただし命令列的根拠はCONFIRMED）**: もし実機で
`SteamInputUtil+0x20`が非`0`の状態にあるなら（未観測、次の
runtime probeの主目的）、ユーザーが提示したモデル

```text
SteamPadSet(0) → slot0へ書込み（常に）
SteamPadSet(1) → 同じフレーム内でslot0を上書き（flag≠0の場合）
```

は命令列レベルで**完全に成立する**。`UpdateInput()`が`rdi=0`→
`rdi=1`の順で`SteamPadSet`を呼ぶこと（30.2節でCONFIRMED済み）と
組み合わせれば、**「毎フレーム、最後に処理されたcontrollerの
analogデータだけがslot 0（＝`GetPadAnalog(padno=0)`が読む唯一の
場所）に残る」**という一貫した説明になる。DEAD/LIVEの実体は
「どちらのhandleがslot 0を勝ち取るか」ではなく、「その時点で
Steamworks自身が返す実データ（`GetAnalogActionData`の戻り値）が
dead/liveのどちらか」に完全に還元される——という、より単純な
（そして41章以前の当初の疑問に近い）モデルに戻ることになる。

**UNRESOLVED（変更なし、次の最優先）**: `SteamInputUtil+0x20`の
実機での値そのものはまだ観測していない。この値がDEAD/LIVE
セッションを通じて実際に非`0`のまま一定なのか、その名称・意味
（「player1集約フラグ」等）が何なのかは、引き続きHYPOTHESISの
まま据え置く。次段はこの1byteフィールドをread-only runtime probe
で観測することが最有力候補になる（ユーザー承認後に実装）。

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 60. `Root26SteamInputUtilFlagProbe`実装・ビルド・デプロイ（2026-09-08）

59.4節で命令列レベルCONFIRMEDとなった`SteamInputUtil+0x20`の
1byteフラグを実測するため、ユーザー承認のもと最小のread-only
runtime probeを実装した。

### 60.1 実装内容（`src/Root26SteamInputUtilFlagProbe.cs`）

- **完全read-only**: `Marshal.ReadByte(SteamInputUtil.instance.Pointer,
  0x20)`のみ。書き込みは一切行わない。
- 値の意味・名称は一切推測せず、生byte値のみをログ出力する。
- 初期値・変化時にSTATE-CHANGEログ、加えて5秒間隔のHEARTBEATログ。
- 既存の`Root26Phase2AnalogActionDataProbe`/`Root26NativePoll`と
  同一の`DateTimeOffset.Now:O`形式でタイムスタンプを出すため、
  DEAD/LIVE遷移と同一タイムライン上で直接突き合わせ可能。
- `ModMain.OnUpdate()`から無条件（探索状態に依存しない）で呼び出す。
- `ResetController`/`SteamControllerReStart`/`Shutdown`/`Init`/
  `UpdateConnectedControllers`等の手動呼び出しなし。F9/F10・
  SendInput・Guide偽装・Steamバイナリへのpatch/injection/hookも
  一切なし。
- 57.2.1節の措置（`Root26Phase8OverlayToStoreOpenPoc`・
  `Root26Phase4NativeRecoveryPoc`のコメントアウト）は今回も
  維持したまま（再確認済み、状態変更PoCはすべて無効のまま）。

### 60.2 ビルド・デプロイ・ハッシュ確認（CONFIRMED）

```text
dotnet build NocturneModernController.csproj -c Release -v q
  → ビルド成功 (0 警告 / 0 エラー)

SHA-256 (source):   cab47739a4dfa0679b69ada8207c35350af1ba72947f7831d0bab8a48f6560b7
SHA-256 (deployed): cab47739a4dfa0679b69ada8207c35350af1ba72947f7831d0bab8a48f6560b7
  → 一致

配置先: C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.dll
```

### 60.3 次の実機テスト手順（ユーザー実施待ち）

1. 通常起動。
2. 探索状態へ入り、右スティックがDEADであることを確認する
   （この時点の`[Root26SteamInputUtilFlag]`初期値を記録）。
3. 既存の安全な操作（物理Guideボタン1回）だけでLIVE化を試す。
   **F9/F10・手動API呼び出し・SendInput・Guide偽装は使用しない。**
4. LIVE化を確認できたら（できなくても）、ゲームを終了する。
5. `Latest.log`を提供する。

**判定基準**（ユーザー提示のまま）:

- `byte@+0x20 != 0`がDEAD/LIVEを通じて維持される
  → 「複数controllerのanalogがslot0へ集約され、後から処理される
  handleが上書きする」という59.4節のモデルをruntime evidenceで
  強く支持。
- `byte@+0x20 == 0`
  → 59章のモデルではindex1→slot0の上書きを説明できないため、
  再解析が必要。
- Guide前後で値が変化する
  → このフラグ自体が復活処理に関与する新しい可能性として扱う。

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。

## 61. `Root26SteamInputUtilFlagProbe`実機テスト結果: 59章モデルの確定（2026-09-08 09:19台）

60章で実装したprobeによる実機テストを実施した。今回も57.2.1節の
汚染防止措置が有効なままで、Store API/Shift+Tab/Alt+Tabの介入なく
「通常起動→DEAD確認→物理Guide 1回のみ→LIVE確認」のクリーンな
単一操作テストになった。

### 61.1 タイムライン — CONFIRMED

```text
09:19:28.288  [Root26SteamInputUtilFlag] STATE-CHANGE byte@+0x20 INITIAL -> 1
              （controller接続前、SteamInputUtilインスタンス確立直後）
09:19:28.289〜53.283  HEARTBEAT byte@+0x20=1（5秒間隔、セッション終了まで
              一貫して同じ値。STATE-CHANGEは最初の1回のみ、以降一度も
              変化なし）

09:19:32.101-260  SteamControllerReStart() count=1-4（初回接続処理）

09:19:42.903  Overlay ON (m_bActive=1)（物理Guide）
09:19:44.270  Overlay OFF (m_bActive=0)
09:19:44.271-273  ResetController() count=1,2 → SteamControllerReStart() count=5,6
09:19:44.909  [Root26Phase2] controllerHandle=91728467815138660
              i=1(IG_RSTICK) state=ACTIVE-NONZERO x=-0.296 y=0.080
              （Resetから約0.64秒後）
09:19:45.284  [Root26NativePoll] STATE-TRANSITION X/Y DEAD -> LIVE
              （Resetから約1.01秒後）
09:19:46-49   handle=91728467815138660のRSTICK ACTIVE-NONZERO継続
09:19:50.393  [Root26NativePoll] STATE-TRANSITION X/Y LIVE -> DEAD
              （ユーザーがスティックを離した／探索終了）
```

### 61.2 CONFIRMED（本調査の核心的な決着）

**`byte@+0x20`はセッション開始（controller接続前）からセッション
終了まで、一貫して`1`（非`0`）のまま一度も変化しなかった。** DEAD
状態・物理Guide開閉中・Guide直後・RSTICK復活後のいずれの時点でも
値は同一である。

**判定（59.4節の判定基準に従う）**: `byte@+0x20 != 0`が
DEAD/LIVEを通じて維持されるパターンに該当する。したがって
**「複数controllerのanalogがslot0へ集約され、後から処理される
handleが上書きする」という59.4節のモデルは、runtime evidenceに
よって強く支持される（CONFIRMED、命令列上の構造と実測値の両方が
一致）。**

`flag(+0x20)`が常時`1`である以上、59.4節の条件表から:

```text
SteamPadSet(0, handle=19680159496504676) → EDI=0（index==0、常にslot0）
SteamPadSet(1, handle=91728467815138660) → EDI=0（★flag≠0なので強制★、slot0を上書き）
```

が**毎フレーム、常に**成立していたことになる。`UpdateInput()`が
`rdi=0`→`rdi=1`の順で呼ぶ（30.2節）ため、**`AnalogStickLRval[0]`
（＝`GetPadAnalog(padno=0)`が最終的に読む唯一の場所）は、常に
`handle=91728467815138660`（`SteamPadSet(1)`）が最後に書いた値で
上書きされ続けていた**、というのが本セッション全体を通じての
実態である。

### 61.3 帰結: 「index選択問題」から「Steamworks側のhandle状態問題」への還元 — CONFIRMED

61.2節の結果により、54章以来追いかけてきた
「`InputHandles`ではDEAD側が先頭・LIVE側が2番目なのに、なぜ
カメラにはLIVE側が反映されるのか」という**index選択の謎は解消
された**。答えは「index0/index1のどちらを選ぶか」という選択問題
ではなく、**「flagが常時真であるため、常に2番目に処理される
controller（今回はhandle`91728467815138660`固定）の値だけが
slot0へ反映され続ける」という、GameAssembly側では常に一定の
構造**だった。

したがって、Root-26調査の残る本丸は、GameAssembly側の
pad/camera/indexの追跡ではなく、**当初の疑問そのもの**へ
還元される:

> なぜSteam Inputの同一handle`91728467815138660`（
> `GetAnalogActionData`が返す値）が、起動直後は物理RSTICKの
> 動きを`x=y=0`として返し続け、物理Guideボタンの押下→Steam
> Overlay ON/OFF→`ResetController()`の後は実値を返すように
> なるのか。

これはGameAssembly.dll側のindex/配列/pad番号の問題ではなく、
**Steam Input（Valve側）がこの特定のcontroller handleに対する
action data配信状態をどう管理しているか**という、53章以前の
問いに近い、より単純な形へ戻ったことになる。

### 61.4 UNRESOLVED（変更なし）

- `byte@+0x20`（`SteamInputUtil+0x20`）の名称・本来の意味は
  依然として未確定（HYPOTHESISのまま）。今回の観測は「常に非0」
  という事実のみを確定させた。
- `19680159496504676`（handle0、常にDEAD）が、そもそもSteamworks
  内部で本当に「別の物理インターフェース」として扱われ続けている
  のか、それとも単に「誰も動かしていないので当然x=y=0」なだけ
  なのかは、本調査ではまだ実機で意図的に検証していない（52章の
  Store/Shift+Tabテストで、この2つ目のhandleが常にDEADだった
  ことは確認済みだが、これが「本質的に使えない」のか「単に
  操作されていない」のかは未分離）。

F9/F10禁止・`ResetController`/`SteamControllerReStart`/`Shutdown`/
`Init`/`UpdateConnectedControllers`の手動呼び出し禁止・SendInput
禁止・Guide入力偽装禁止・Steamバイナリへのpatch/injection/hook
禁止を継続する。commit/push/stash/reset/revertは行っていない。
