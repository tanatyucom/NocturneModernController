# Session Resume Notes（2026-09-20 21:4x頃時点、作業終了時の保全メモ）

このファイル自体はcommitしていません（未追跡）。保全目的のみで、コード変更・commit・push・merge・stash pop・branch切替・deploy・削除は一切行っていません。

## 1. Canonical構成（現状）

```text
C:\SMT3Modding\
├─ NocturneModernController\          primary repo, branch=main, 安定main
├─ NocturneModernController-Binding\  Binding API開発正本（旧 _binding_api_atomic）
├─ _v202_release\                     production baseline保険（今は触らない）
├─ archive\
│   ├─ _binding_api_step6_runtime\
│   ├─ _v202_candidate\
│   └─ NocturneModernController-v2.0.1-deploy\
└─ backups\
    └─ _VanillaTestBackup\
```

全てNocturneModernController（primary）の同一git repoのlinked worktree。
`agent/right-stick-vanilla-turn`は実験branchで、production build/deployに使わない。

## 2. 保全確認（2026-09-20 21:4x時点の実測）

| worktree | HEAD | branch/detached | git status |
|---|---|---|---|
| `NocturneModernController` | `05080c1`系列から`main`へ切替済み（現HEAD=`e8e67f0`と同じ、origin/mainと同期） | `main` | clean（untracked: `*.mp4`×3, `tools/Root19PoC/` — 今回の作業と無関係、既存） |
| `NocturneModernController-Binding` | `a8de17a193737707a71acec7dda91130d4475105` "feat: add deterministic controller binding persistence and conflict handling" | **detached** | 未commit7ファイル + untracked3件（下記） |
| `_v202_release` | `e8e67f0d6d5f1682ace341e4dc3357e6ae6aa96a` "fix: reduce field dash speed to prevent collision skipping" | **detached** | 未commit4ファイル（下記、Step7とは無関係の既存差分） |

**stash**: `stash@{0}`保持中（`agent/right-stick-vanilla-turn`上、"WIP on agent/right-stick-vanilla-turn before switching to main for Explorer-based right-stick"）。**popしていません。**

**現在デプロイ中のDLL**:
```
Mods/NocturneModernController.dll
SHA-256 = 00fffff524e35f54f4fca07ed02b616a43b2c54ad872bb5978c103cceb21113c
→ NocturneModernController-Binding（a8de17a + 未commit差分、PadCfgDiagnosticProbe入り）からのclean build
→ ローカルbin/Release/net6.0と完全一致、確認済み
```

**`NocturneModernController-Binding`の未commit差分**:
```
modified:
  settings/NocturneModernController.Settings.csproj
  settings/Program.cs
  shared/BindingStatusFormatter.cs
  src/ModMain.cs
  src/ModernControllerApi.cs
  tests/DefaultBindingResolverTests/DefaultBindingResolverTests.csproj
  tests/DefaultBindingResolverTests/Program.cs
untracked:
  investigations/   （PadCfg/CommonConfig A/B調査記録、下記参照）
  shared/GameBindingSnapshot.cs
  src/PadCfgDiagnosticProbe.cs
```

**`_v202_release`の未commit差分**（Step7・今回のPadCfg調査とは無関係の既存差分、Codexの作業と推測）:
```
modified:
  src/BuiltInFeatureProvider.cs
  src/ControllerSettings.cs
  src/FieldDashPatch.cs
  src/ModernControllerApi.cs
```

## 3. Explorer右スティック

productionの正しい方式はExplorer起動:
```
smt3hd.exe → explorer.exe → NocturneModernController.InputHelper.exe → SDL3 → MMF → ModernController
```
Task Scheduler/Broker版は実験系（`agent/right-stick-vanilla-turn`）。過去に誤ってdeployし右スティックが死んだ事故があり、Explorer版へ戻して解決済み。

## 4. NocturneModernGameplay slowdown — 解決済み

CONFIRMED root cause: `SkillCurObjSetActiveTrace`（`UnityEngine.GameObject.SetActive`全域Harmony Prefix、CLOSED済みHIDDEN NEW SKILL ENTRY調査コードの残骸）。実機A/B/A（ON→SLOWDOWN、OFF→NORMAL、ON→SLOWDOWN）で確認。同じCLOSED調査の4コード（`SkillCurObjSetActiveTrace`/`CmpMenuCursorTrace`/`StatusUiFieldOffsetProbe`/`SelectSkillIdOffsetProbe`）をproduction runtimeから撤去し`legacy/skillmutationv3/*.Legacy.cs`へ移動済み。`NocturneModernGameplay`: `origin/fix/skill-mutation-v2.1`, commit `fb85094`, push済み。

## 5. Controller Binding API v1

**Step1-6 checkpoint**: commit `a8de17a` "feat: add deterministic controller binding persistence and conflict handling"。実装済み: atomic JSON write、bindings v0→v1 migration、deterministic default resolver、Unassigned override、GUI edit/reset、Binding Status、conflict handling。

**Step7 GAME read-only**: 未commitだが実在。主要ファイル: `shared/GameBindingSnapshot.cs`, `src/ModernControllerApi.cs`, `settings/Program.cs`, `shared/BindingStatusFormatter.cs`。

`InputAssign.GetAssignCode(PAD1, KeyID)`で16個の低レベルbindingを正常に取得できている（実機確認済み: `Y[GAME] PAD1 -> Pad_Button3`等）。ただし重要: これはユーザーが純正Controller Key Configで設定する「Game Action → Physical Button」ではなく、より低レベルのKeyID/AssignCode層。**GAME Binding完成とはまだ扱わない。**

## 6. 純正GAME Binding調査 — PadCfg/CommonConfigはSSoTではないとCONFIRMED

ユーザーの純正Controller Key Config: `Field/Dungeon Menu`を`X`⇔`Y`で切り替えてA/Bテストを実施（クリーンな単一セッションで2回、独立に再現）。

**候補1: `SteamInputAssign.PadCfg`**（`Length=31`）
仮説: `PadCfg[(int)SIActionName]` = current GAME Binding
結果: **FALSIFIED**。Menu=X時とMenu=Y時で31要素全て完全一致（diff=0）。`FD_CmdMenu`（index=7）は`pad1=12, pad2=0, type=NONE`で不変。

**候補2: `SteamInputAssign.CommonConfig`**（`Length=14`、当初仮説の34とは不一致）
結果: **FALSIFIED**。同様にX/Yで14要素全て完全一致（diff=0）。

**結論**: `PadCfg`・`CommonConfig`ともに、純正Controller Key Configのcurrent bindingのSSoTではない。

## 7. 現在の診断コード

`NocturneModernController-Binding`に`src/PadCfgDiagnosticProbe.cs`が未commitで存在。`FieldDashPatch.IsExplorationActive`でガードし、`SteamInputUtil.Instance.GetSteamInputAssign()`経由で`PadCfg`・`CommonConfig`をread-onlyで1回だけログ出力する（write処理なし、Harmony追加なし）。`ModMain.OnUpdate()`から1行呼び出し。

`investigations/`に未追跡の調査記録あり:
- `PADCFG_AB_BASELINE_20260920.md`（最初のbaseline、セッション途中変更の疑いで実質破棄）
- `PADCFG_AB_BASELINE_X_CLEAN_20260920.md`（クリーンなX状態baseline）
- `PADCFG_AB_RESULT_Y_CLEAN_20260920.md`（X→Y diff結果、diff=0を記録）

これらは調査記録であり、現時点ではcommit対象外の扱い。

## 8. 次回の最優先調査

Ghidra/IDAへ行く前に、**`InputAssign`側の未確認current/saved state**をA/Bする。

優先対象:
- `InputAssign._assignData`（`Il2CppReferenceArray<AssignData>`）
- `InputAssign.SavedAssignData`（単体`AssignData`）
- `InputAssign._assignData_sav`
- その他`InputAssign`内のcurrent/saved/config系field/property

手順（未実施）:
1. まずreflection/metadataでfield/property一覧を確認（推測で意味を決め打ちしない）
2. **現在ユーザー側の純正設定はY**で終了している → Y状態でbaseline取得
3. ユーザーにY→X変更を依頼 → 再起動 → 再取得 → diff
4. 必要ならX→YでA/B/Aまで確認

条件: write禁止、Harmony不要、`PadCfg`/`CommonConfig`は再調査しない。

**これでも全候補不変なら**、managed wrapper側探索はいったん終了し、Ghidra/IDA等で以下のnative write pathの解析へ移行:
```
dds3ConfigGamePadSteam.ChangeKey
dds3ConfigGamePadSteam.ChangeKeyDuplicate
dds3ConfigGamePadSteam.GamePadDuplicateWrite
dds3ConfigGamePadSteam.ChgConfigGamePadAll
dds3ConfigGamePadSteam.GetGamePadPadMap
dds3ConfigMainSteam.GetConfigPadMap
```
これらは全てIl2CppInterop生成のネイティブ関数スタブであり、C#の静的リフレクション（`System.Reflection.Metadata`/`MetadataLoadContext`）ではシグネチャは読めても中身のロジックは読めない。この環境には現在Ghidra/IDA等のディスアセンブラは無い。

## 9. Evidence summary

**CONFIRMED**:
- `PadCfg.Length=31`, `CommonConfig.Length=14`
- `PadCfg` X/Y clean A/B diff=0（2回独立に再現）
- `CommonConfig` X/Y clean A/B diff=0
- Step7 `GetAssignCode`低レベルsnapshotは正常動作
- pure GAME action binding（Action→現在の物理ボタン）はまだ未特定

**STRONGLY SUPPORTED**:
- GAME current bindingのSSoTは`PadCfg`/`CommonConfig`以外
- `InputAssign`内部のsaved/current state、またはnative config側にある可能性が高い

**UNRESOLVED**:
- `ChangeKey`が実際にどこへ書くか
- `CFG_TYPE_GAMEPAD`（34項目とされる、docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.mdにユーザー提供画像由来の記録あり、未検証）の実体
- Menu actionの`CFG_TYPE_GAMEPAD`インデックス
- save/load経路

## 10. 再開時の最初の1手(2026-09-20時点、§15により更新済み)

~~`InputAssign`のfield/property一覧を...`PadCfgDiagnosticProbe.cs`に`InputAssign`側の読み取りを追加する。~~
**この節はGhidra解析(§11〜§13)により優先度が変わったため、再開時は§15を参照すること。** `InputAssign._assignData`側のreflection一覧化自体はまだ未着手のまま残っているが、次の最優先ではない。

## 11. Ghidra native解析 — CONFIRMED write path(2026-09-21)

Ghidra 12.1.3、Project `SMT3HD_GameAssembly`(`C:\SMT3Modding\GhidraProjects\`)でのDecompiler解析により、純正Controller Key Configの書き込み経路をnative側でほぼ特定した。

**Canonical記録**(このセッションの詳細はすべてここに集約済み、重複ファイルなし):
- `C:\SMT3Modding\GhidraProjects\SMT3HD_GameAssembly_METADATA.md`(fingerprint: SHA-256 `59ADBB5B18AAEDC7DF6C1672790CA48BE99B5E8559C81DB27132C436FB0A9FC4`、Ghidra 12.1.3、Image Base `0x180000000`、既知RVA/VA)
- `C:\SMT3Modding\GhidraProjects\SMT3HD_GameAssembly_ANALYSIS_NOTES.md`(Evidence分類付きの全解析詳細)

**確認済みdata flow**:
```
ChangeKey (FUN_18228e320, VA 0x18228E320)
  ↓
ChangeKeyDuplicate (FUN_18228dea0, VA 0x18228DEA0)  ※34要素(0x22)の重複チェックゲート
  ↓ 重複なしの場合のみ
FUN_182296ef0 (VA 0x182296EF0)  ※実際のbinding write
  ↓
対象オブジェクトT の T+0x20+action_id*4 へint32を書き込み(action_id=0..0x21、34スロット)
```
重複が見つかった場合はbinding本体を書かず、衝突情報だけを記録して早期return(CONFIRMED、`ChangeKeyDuplicate`のdecompile本文で確認)。

`ChangeKey`は全パス共通で末尾に`FUN_1822ee830(1)`を呼ぶ(重複ブロック時・defaultで何もしない時も含め無条件)。`FUN_1822ee830`は`FUN_1822ee540(param_1,0,0)`への薄いwrapperで、実体(`FUN_1822ee540`)は**未解析・UNRESOLVED**。

decompile exportは全て保全済み:
- `investigations/ghidra/ChangeKey_18228E320_decompile.txt`
- `investigations/ghidra/ChangeKeyDuplicate_18228DEA0_decompile.txt`
- `investigations/ghidra/FUN_182296EF0_decompile.txt`
- `investigations/ghidra/FUN_1822EE830_decompile.txt`

`PadCfg`/`CommonConfig`はcurrent GAME binding SSoT候補から除外済みのまま(§6)。再調査しない。

## 12. managed metadata側の発見(2026-09-21、read-only reflection、ゲーム非起動・Ghidra非使用)

`System.Reflection.MetadataLoadContext`でAssembly-CSharp.dll(Il2CppInterop dummy)を読み取り専用ロードし、`Il2Cpp.dds3ConfigGamePadSteam`が実在するmanaged型であることをCONFIRMED。以下のシグネチャがGhidra解析のparamと1:1一致:

```csharp
public static bool ChangeKey(uint, ref bool);
private static void ChangeKeyDuplicate(uint, int, int, int, ref bool);
public static void GamePadDuplicateWrite();
public static void ChgConfigGamePad(uint, int);
public static void ChgConfigGamePadAll();
public static int GetConfigGamePad(int);          // ★★最重要候補、下記§13参照
public static short GetConfigGamePadGuideKID(short);
public static byte GetGamePadPadMap(byte[], SDF_PADMAP, int, int);
public static int GetKIDtoSDF_PADMAP(int);
public static bool GamePadDoneChk();
```

`dds3ConfigGBWK_t`(Config画面UI作業領域)はbinding配列を持たずUI状態のみと判明、候補から除外。

`SAVEDATA.dds3GlobalWork_tag`に`config_data_audio/game/graphics/display/gamepad/keyboard`という6分類Int32配列field名が実在(CONFIRMED)。ライブ版`dds3GlobalWork_t.config_data`は単一の入れ子配列(`Il2CppReferenceArray<Il2CppStructArray<Int32>>`)。`config_data[4]`がgamepad configかもしれない、というのはHYPOTHESIS(宣言順の踏襲を仮定しているだけで未検証)。

## 13. 最重要新発見・次回最優先PoC

`dds3ConfigGamePadSteam.GetConfigGamePad(int index)`という**公式のread-onlyゲッター**がmanaged APIとして存在する(CONFIRMED、型シグネチャのみ。動作内容は未検証)。生ポインタ演算やoffset決め打ちをせずに安全に呼び出せるため、`FUN_1822ee540`の追加Ghidra解析より**こちらを先に実機検証する**。

**次回再開時の最優先PoC**:
```
for i = 0..0x21 (34件):
    dds3ConfigGamePadSteam.GetConfigGamePad(i)
```
1. 現在のユーザー純正設定(`Field/Dungeon Menu = Y`)でbaseline取得
2. ユーザーに純正GUIで`Menu Y→X`変更を依頼 → 再起動
3. 同じ34件を再取得 → before/after diff
4. 必要なら`X→Y`でA/B/A
5. 判定:
   - 1要素(またはFD_CmdMenuと説明可能な関連要素)のみ変化 → `GetConfigGamePad`がcurrent GAME binding read pathとして有力、CONFIRMEDへ格上げ
   - 全34件不変 → 別用途/別cacheのgetterと判断
   - 複数要素変化 → duplicate/group/sync構造の追加解析が必要

条件: read-onlyのみ、write禁止、Harmony追加不要、`PadCfg`/`CommonConfig`再調査禁止、A/B結果が出るまで本実装しない。`config_data[4]`は安全に読める場合のみ補助比較として使ってよい。

**`GetConfigGamePad`が外れた場合**の次のGhidra対象は`FUN_1822ee540`(save/apply/sync/notify/refreshのいずれか、未確認)。

## 14. enum名の扱い(継続)

`CFG_TYPE_GAMEPAD`/`SIActionName`/`SDF_PADMAP`/`InputAssign.KeyID`/`InputAssign.AssignCode`/`ControllerButton(mod)`は引き続き別の数値体系として扱う。`GetConfigGamePad`のindex空間が`ChangeKey`のparam_1(0〜0x21)と一致する可能性は高いが、実測で確認するまで決め打ちしない。

## 15. 再開時の最初の1手(最新、§10を上書き)

`NocturneModernController-Binding`の`PadCfgDiagnosticProbe.cs`と同様の構造で、`dds3ConfigGamePadSteam.GetConfigGamePad(i)`(i=0..0x21)をread-onlyでログ出力するプローブを追加し、§13の手順でY baseline取得から開始する。まだこのプローブ自体は未実装(コード変更していない)。

## 16. 本日終了時点の保全確認(2026-09-21)

| worktree | HEAD | branch/detached | git status |
|---|---|---|---|
| `NocturneModernController` | `e8e67f0`(§2時点と同一、変化なし) | `main` | untracked: `*.mp4`×3, `tools/Root19PoC/`(既存、今回と無関係) |
| `NocturneModernController-Binding` | `a8de17a`(§2時点と同一) | **detached**(変化なし) | 未commit7ファイル + untracked4件(`SESSION_RESUME_NOTES.md`自体・`investigations/`・`shared/GameBindingSnapshot.cs`・`src/PadCfgDiagnosticProbe.cs`、§2から変化なし) |
| `_v202_release` | `e8e67f0`(§2時点と同一) | **detached** | 未commit4ファイル(§2から変化なし、Step7と無関係の既存差分) |

**stash**: `stash@{0}`保持中、**popしていない**(§2から変化なし)。

**現在デプロイ中のDLL**:
```
Mods/NocturneModernController.dll
SHA-256 = 00FFFFF524E35F54F4FCA07ED02B616A43B2C54AD872BB5978C103CCEB21113C
LastWriteTime = 2026-09-20 21:30:10
→ §2時点と完全一致。今回のセッションはbuild/deployを一切行っていない。
```

**Ghidra canonical資産の存在確認**: `SMT3HD_GameAssembly_METADATA.md`(3497 bytes)・`SMT3HD_GameAssembly_ANALYSIS_NOTES.md`(9355 bytes)ともに存在。重複metadataファイルは無し(`SMT3HD_GameAssembly\_METADATA.md`は削除済み、確認済み)。

## 17. GetConfigGamePad A/B/A — CONFIRMED(2026-09-21、実機テスト完了)

`src/GameBindingProbe.cs`を`PadCfgDiagnosticProbe.cs`と同構造で新規実装(read-only、write API不使用、Harmony不要、起動後1回のみログ、index 0..0x21個別try-catch)。`ModMain.OnUpdate()`に1行追加。clean build(0エラー/0警告)→deploy。

**deploy DLL SHA-256(§13以降の全A/B/A測定で共通)**: `67691F3A5FA02F270834C3CBE341EB6BA425CE17374FDC58080ACD0E644EA9AC`(旧`00FFFFF5...`から更新)

**A/B/A実機結果**(`investigations/GAMEBINDING_AB_BASELINE_Y_CLEAN_20260921.md`、`GAMEBINDING_AB_RESULT_X_CLEAN_20260921.md`、`GAMEBINDING_AB_RESULT_Y_ABA_20260921.md`に詳細):

| 測定 | Field/Dungeon Menu | index=7 raw | 他33件(index 0-6,8-33) |
|---|---|---|---|
| 1回目 | Y | 11 | baseline |
| 2回目 | X | 12 | baseline と完全一致(diff=0) |
| 3回目 | Y | 11 | baseline と完全一致(diff=0) |

**CONFIRMED**:
- `dds3ConfigGamePadSteam.GetConfigGamePad(int index)`はcurrent GAME bindingのread-only read pathである(§13の仮説がCONFIRMEDへ格上げ)。
- `index=7`は`Field/Dungeon Menu` actionに対応する(Y/X切り替えに1:1追従、クロストークなし、A/B/Aで再現)。
- `PadCfg`/`CommonConfig`(§6でSSoTから除外済み)とは異なるindex空間として、`GetConfigGamePad`がSSoTの有力候補。

**UNRESOLVED(継続)**:
- Menu以外のactionでの`GetConfigGamePad` index対応は未検証(今回はMenu 1件のみ)。
- `index=7`と`CFG_TYPE_GAMEPAD`/`SIActionName`/`FD_CmdMenu`等の既存enumとの対応は数値上の一致のみで、意味的対応はまだ未証明(§14方針、決め打ち禁止を継続)。
- Step7本実装(current GAME binding読み取りAPI化)は未着手。着手前にユーザーへ確認を挟む。

commit/push/build設定変更は行っていない。write API・native GAME binding変更は一切行っていない。

## 18. Step7A実装 — readiness gate誤りの発見と修正(2026-09-21)

`shared/GameBindingSnapshot.cs`に`GameActionBindingRawEntry`/`GameActionBindingSnapshotResult`/`GameActionBindingSnapshotReader`を新規追加。`ModernControllerApi.SaveSnapshots()`から`GetConfigGamePad(0..0x21)`をcaptureし、`actions.json`の`ActionRegistrySnapshot`へ`GameActionBindingsAvailable`/`GameActionBindingsRaw`として保存する基盤を実装。index=7のみ`ConfirmedActionName="Field/Dungeon Menu"`、raw 11=Y/12=Xの意味付け。既存`GameBindings`(`GetAssignCode`ベース)とは別系統として共存。

**第一次実機テストで発覚した問題**: `OnInitializeMelon`時点の`SaveSnapshots()`では`GetConfigGamePad(0..30)`が全て`NullReferenceException`(native GAME state未構築)。31..33のみ`raw=0`で成功。

**readiness retry実装(第一版、REJECTED)**: `ModMain.OnUpdate()`から毎フレーム`GetConfigGamePad`34件成功を条件に1回だけ`actions.json`を更新する仕組みを実装したが、実機で反証された。`ready(34/34)`ログが`dds3TitleInit`より前(タイトル初期化前)に出現し、その時点のindex=7の値はユーザーの実際の設定(Y)ではなくstale/default値(raw=12=X)だった。「34/34 exception-free」は「ユーザー設定ロード済み」の証拠にならないとCONFIRMED(REJECTED)。

**readiness retry実装(第二版、CONFIRMED)**: readiness条件を`FieldDashPatch.IsExplorationActive == true`(既存`GameBindingProbe`のA/B/Aで正しく動作実証済みの条件)へ変更。exploration active前は`GetConfigGamePad`自体を呼ばない。exploration active後、45フレーム間隔で34/34成功を確認できた時点でのみ`actions.json`のregistry部分(`GameBindings`/`GameActionBindingsRaw`)を1回だけ上書きし、以後retry停止。bindings.json/features.jsonには触れない(binding resolver/user binding stateへ副作用なし)。

**第二次実機テストで確認**(deploy DLL SHA-256 `4C0783CC92953B1986E52A2F6147B728A50C65FC54579A3DC628FAE4BFEA3107`):
```
20:15:07.844  SkipIntro hooked
20:15:12.510  dds3TitleInit called (タイトル初期化)
20:15:19.356  GameBindingProbe BEGIN (exploration active後)
20:15:19.358  GameBindingProbe index=7 raw=11
20:15:19.687  [GameActionBindingSnapshot] ready after exploration became active (34/34); snapshot refreshed (1回のみ)
```
`actions.json`: `GameActionBindingsRaw`34件、index=7 = `RawValue=11, ConfirmedActionName="Field/Dungeon Menu", ConfirmedPhysicalButton="Y"`(実際のMenu=Yと一致)。他33件はsemantic mappingなし。既存`GameBindings`16件正常。registry連続更新なし。

**CONFIRMED(更新)**:
- `GetConfigGamePad(int)` = current GAME binding read path — ただし`FieldDashPatch.IsExplorationActive == true`後の取得のみが信頼できる値。
- 「例外なく34/34読める」だけではreadiness条件として不十分(REJECTED、実機で反証済み)。

**UNRESOLVED**: タイトル画面等、exploration active前でも使える「config load完了」専用のnative/managed signalが存在するかは未調査(今回は追わない、と明示的に判断)。

commit/push未実施。他33 index意味付け・Settings UI表示は未着手。

## 19. GetConfigGamePad index対応表 拡充(2026-09-21、A/B調査、詳細は`investigations/GAMEBINDING_INDEX_MAP_20260921.md`)

`index=16` = 「オートマップ表示」（FIELD/DUNGEON）としてA/B/A CONFIRMED。純正Controller Key Configでオートマップ表示をSTART→RT→STARTと変更し、`GameBindingProbe`(exploration active後)で34件中index=16のみ変化(raw 26↔16)、他33件は3回とも不変を確認。

**画面表示名の確認**(`investigations/GAMEPAD_UI_ACTION_LIST_20260921.md`、実機スクリーンショット6枚): これまで便宜的に「Field/Dungeon Menu」と呼んでいたactionの正式UI表示名は**「コマンドメニュー」**(FIELD/DUNGEONカテゴリ)。「Map」の正式表示名は**「オートマップ表示」**(同カテゴリ)。「視点変更（上/下/左/右）」の4項目は標準でグレーアウトし変更不可、A/B対象外。

`index=18` = 「オートバトル」（BATTLE）としてもA/B/A CONFIRMED（Y→X→Y、raw 11↔12、他33件は3回とも不変）。

`index=20` = 「テキストの早送り」（EVENT）としてもA/B/A CONFIRMED。消去法（baselineでraw=11のindexは{7,18,20,24}、CONFIRMED済み7・18を除くと候補は20と24の2件）で絞り込み、1回のA/Bで20が該当と判明、A/B/Aで確認。`index=24`はraw=11のまま不変で、別のY割当actionの未特定候補として残る。

**CONFIRMED対応表（2026-09-21時点）**:
```
index=7:  コマンドメニュー(旧称 Field/Dungeon Menu)   raw 11=Y, 12=X
index=16: オートマップ表示(旧称 Map)                   raw 26=START, 16=RT
index=18: オートバトル                                 raw 11=Y, 12=X
index=20: テキストの早送り                             raw 11=Y, 12=X
```

**未確定候補（STRONGLY SUPPORTED、消去法）**: `index=24` = PUZZLE「スクロール切替」の可能性が高い（raw=11=Y割当のうちスクショ上唯一の未検証action）。CONFIRMEDにはPUZZLE到達後のA/B/Aが必要（現在ゲーム進行上未到達のため保留）。

## 20. 効率化: 小規模バッチA/B/A方式(2026-09-21、詳細は`investigations/GAMEBINDING_INDEX_MAP_20260921.md`)

1 action = 2リブートから、最大3 actionを異なるボタンへ同時変更し2リブートでバッチ全体を確認する方式へ切替。バッチ1（決定・アクション A→X、キャンセル B→RT、次に回す RB→START）を実施したところ、期待した3変化に加え**想定外の4件目**（`index=23`, raw 12→10）が発生。原因はユーザーが実機確認: 「決定・アクション」変更の重複解消(duplicate handling)により、元々Xだった「PUZZLE メニュー」の割当が外れて空欄（未割当）になり、ゲームが再割当を要求。ユーザーが手動でAを選択した結果raw=10になった（自動玉突きではない）。複数action同時変更バッチでは、変更対象以外が意図せず空欄になり得るため、変更後に未割当項目がないか確認する手順を追加。

バッチ1の4件はA/B/A完了、CONFIRMED（4index全てbaseline値へ正確に復元、他30件も完全一致）。複数action同時変更は重複解消の副作用リスクがあるため、diff数と変更数が一致しない場合は原因を必ず確認すること。

**CONFIRMED対応表（2026-09-21最終）**:
```
index=4:  決定・アクション(COMMON)         raw 10=A, 12=X
index=5:  キャンセル(COMMON)               raw 9=B, 16=RT
index=7:  コマンドメニュー(FIELD/DUNGEON)  raw 11=Y, 12=X
index=16: オートマップ表示(FIELD/DUNGEON)  raw 26=START, 16=RT
index=18: オートバトル(BATTLE)             raw 11=Y, 12=X
index=19: 次に回す(BATTLE)                 raw 15=RB, 26=START
index=20: テキストの早送り(EVENT)          raw 11=Y, 12=X
index=23: メニュー(PUZZLE)                 raw 12=X, 10=A
```

**STRONGLY SUPPORTED（宿題）**: `index=24` = スクロール切替(PUZZLE)、raw=11(Y)、未A/B（PUZZLE到達待ち）。

**判明したraw値**（index毎の観察、他indexへの一般化はしない）: `A=10, B=9, Y=11, X=12, RB=15, RT=16, START=26`。8件CONFIRMED達成。

## 21. バッチ2（スキルヘルプON/OFF / 視点を正面に戻す / UI表示ON/OFF）— CONFIRMED、詳細は`investigations/GAMEBINDING_INDEX_MAP_20260921.md`

3件の意図的変更（スキルヘルプON/OFF SELECT→RT、視点を正面に戻す B→RB、UI表示ON/OFF L3→START）が、重複解消の連鎖により**6件の変化**を引き起こした（意図3件+玉突き3件: オートマップ表示START→LB、視点変更(左回転)LB→LT、視点変更(右回転)RB→RT）。全6indexがbaseline/変更後raw値と整合的に説明でき、A/B/A（全復元、diff=0）でCONFIRMED。

**CONFIRMED対応表（2026-09-21最終、計13件）**:
```
index=4:  決定・アクション(COMMON)          raw 10=A, 12=X
index=5:  キャンセル(COMMON)                raw 9=B, 16=RT
index=6:  UI表示ON/OFF(COMMON)              raw 17=L3, 26=START
index=7:  コマンドメニュー(FIELD/DUNGEON)   raw 11=Y, 12=X
index=12: 視点変更（左回転）(FIELD/DUNGEON) raw 13=LB, 14=LT
index=13: 視点変更（右回転）(FIELD/DUNGEON) raw 15=RB, 16=RT
index=14: 視点を正面に戻す(FIELD/DUNGEON)   raw 9=B, 15=RB
index=16: オートマップ表示(FIELD/DUNGEON)   raw 26=START, 13=LB
index=17: スキルヘルプON/OFF(BATTLE)        raw 25=SELECT, 16=RT
index=18: オートバトル(BATTLE)              raw 11=Y, 12=X
index=19: 次に回す(BATTLE)                  raw 15=RB, 26=START
index=20: テキストの早送り(EVENT)           raw 11=Y, 12=X
index=23: メニュー(PUZZLE)                  raw 12=X, 10=A
```

**STRONGLY SUPPORTED（宿題）**: `index=24` = スクロール切替(PUZZLE)、PUZZLE到達待ち。

**判明したraw値一覧**: `A=10, B=9, Y=11, X=12, LB=13, LT=14, RB=15, RT=16, L3=17, SELECT=25, START=26`（indexごとの観察、一般化はしない）。

## 22. commit前整理（2026-09-21）

`shared/GameBindingSnapshot.cs`の`ConfirmedActionNames`/`ConfirmedPhysicalButtonsByIndex`を、CONFIRMED済み13件全部へ拡張（旧: index=7のみ）。各エントリのコメントに、A/B/A実測のみを根拠とする旨・エビデンス参照先(`investigations/GAMEBINDING_INDEX_MAP_20260921.md`)を明記。`ModernControllerApi.CaptureGameActionBindingsRaw()`の古いコメント（"only index=7 has confirmed mapping"）も13件へ更新。

core/Settings/Tests、3プロジェクトともclean build 0エラー・0警告を確認。`src/ModMain.cs`/`shared/BindingStatusFormatter.cs`の既存差分（Step7実装分）も再レビューし問題なし。

`src/GameBindingProbe.cs`（A/B/A用一時診断）は方針通り削除せず残置（診断能力維持、production pathとは責務分離済み）。Settings UIへのread-only表示追加は今回スコープ外（次のタスク候補として残す）。

deploy/commitはまだ実施していない。

index=7,18,20の3件でraw値(11=Y/12=X)が一致しているが、他indexへの一般化・raw value enumの確定は行わない。PUZZLE区分5項目・「視点変更（上/下/左/右）」4項目はA/B対象外（前者はゲーム進行未到達、後者はグレーアウト）。他28 indexは引き続き意味未確定（推測禁止を継続）。Settings UI表示は未着手。
