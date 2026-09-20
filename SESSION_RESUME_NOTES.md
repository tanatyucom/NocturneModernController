# Session Resume Notes（2026-09-20 17:35頃時点、コミット前の状態保全メモ）

このファイル自体はcommitしていません（未追跡）。保全目的のみで、コード変更・commit・push・merge・stash popは一切行っていません。

## 1. 現在のbranch / worktree / HEAD

| リポジトリ / worktree | パス | branch | HEAD |
|---|---|---|---|
| NocturneModernController（primary） | `C:\SMT3Modding\NocturneModernController` | `agent/right-stick-vanilla-turn` | `5bd740b` research: preserve Steam Input and Task Scheduler investigations |
| NocturneModernController（main worktree） | `C:\SMT3Modding\_v202_release` | `main` | `e8e67f0` fix: reduce field dash speed to prevent collision skipping |
| NocturneModernGameplay | `C:\SMT3Modding\NocturneModernGameplay` | `fix/skill-mutation-v2.1` | `fb85094` fix: remove closed skill mutation diagnostics causing config slowdown（**push済み**） |

## 2. stash一覧

- `NocturneModernController`（primary、`agent/right-stick-vanilla-turn`上）:
  - `stash@{0}`: "WIP on agent/right-stick-vanilla-turn before switching to main for Explorer-based right-stick"
  - 内容: `settings/Program.cs`（M）、`src/FieldDashPatch.cs`（M）、`src/ModMain.cs`（M、KeyConfigCollidersRecvProbe登録の追加分含む）、`docs/CONTROLLER_BINDING_API_V1_SPEC.md`（新規）、`investigations/`（新規untracked）、`src/KeyConfigCollidersRecvProbe.cs`（新規）、`video-contact-sheet.png`（新規untracked）
  - **まだpopしていません。**
- `NocturneModernGameplay`: stashなし
- `_v202_release`（mainのworktree）: stashなし

## 3. 未コミット差分の有無

- `NocturneModernController` primary（`agent/right-stick-vanilla-turn`）: **クリーン**（全てstash済み）
- `_v202_release`（`main`）: 未コミット差分あり（Binding API関連と思われる、おそらくCodexの作業）
  ```
  M src/BuiltInFeatureProvider.cs
  M src/ControllerSettings.cs
  M src/FieldDashPatch.cs
  M src/ModernControllerApi.cs
  ```
  **今回この差分には一切触れていません。**
- `NocturneModernGameplay`: untrackedファイルのみ（スクリーンショット10枚+動画1本+`settings.json`、いずれも今回の作業と無関係、今回commitに含めていません）。tracked差分はなし（commit済み）。

## 4. 現在ゲームへデプロイされているDLLのSHA-256

```
Mods/NocturneModernController.dll
  96c1661a1c5807307c7a57f52284f5917bf2b6e4570afd838ed99d19c66719b0
  → main worktree（_v202_release, HEAD=e8e67f0 + 未コミット差分4ファイル）からclean build
  → Explorer起動方式（explorer.exe経由でInputHelper.exeを起動、Broker/Task Scheduler不使用）

Mods/NocturneModernGameplay.dll
  083004798a746dff07cbc52018bdca342e85b892323f0a7a69bd935c18c87b9d
  → fix/skill-mutation-v2.1, commit fb85094（push済み）からclean build
  → CLOSED済み調査コード4つ（SkillCurObjSetActiveTrace等）をlegacy/へ撤去済み
```

## 5. `_VanillaTestBackup`に残っている退避物

**なし。全て復元済み。**

`C:\SMT3Modding\_VanillaTestBackup\`には`MANIFEST_Test0_vanilla.txt`（履歴記録として意図的に保持）のみが残り、`Mods/`は空、`UserData/`も既に元の場所へ復元済みで存在しません。`MelonLoader/`・`version.dll`もゲームディレクトリへ復元済みです。

現在の`Mods/`（33項目、元の全24 MOD構成＋NocturneModernController/Gameplay自身のファイル群）:
```
DemonsCanUseItems.dll, FixedEncounterRadar.dll, KeptSuspense.dll, MoonKing.dll,
Nocturne Framerate Mod.dll, Nocturne Minimap for Melon060.dll,
NocturneDetailedSkillInfo.dll, NocturneKeyboardInput.dll,
NocturneModernController/, NocturneModernController.Helper/,
NocturneModernController.dll(+jsons), NocturneModernGameplay.dll(+jsons),
PreyEyes2.dll(+json), PuzzleBoyManiax.dll, QuickPass.dll,
ReworkedMitamaFusion.dll, SafePassage.dll, SkipIntro.dll, SuspendSafe.dll,
early_compendium.dll, everyone_gets_exp_100percent_06.dll, icons/,
modern_press_turns_smtv_06.dll, no_interruptions.dll
```

`UserData/`も`MelonPreferences.cfg`・`MelonStartScreen/`・`ModsCfg/PuzzleBoyManiax.cfg`・`NocturneKeyboardInput/`（外部Helper）を含め元の構成に復元済み。

## 6. 再起動後の最初の確認事項

**Explorer版（`main`, HEAD=e8e67f0 + 未コミット差分, SHA-256=96c1661a...）で右スティックが動くか。**

## 重要な方針メモ（今回確定した事実）

- **production baseline はExplorer起動版（`main`ブランチ）**。`explorer.exe`経由で`NocturneModernController.Helper/NocturneModernController.InputHelper.exe`を直接起動し、Broker/Task Schedulerは使わない。
- **`agent/right-stick-vanilla-turn`はTask Scheduler/Broker系の実験ブランチであり、本番デプロイ禁止。** 右スティック不調は、このブランチを誤ってビルド・デプロイしていたことが原因だった。
- Controller Binding API v1の既存作業内容（仕様書`docs/CONTROLLER_BINDING_API_V1_SPEC.md`含む、stash@{0}に退避中）は失っていない。
- 今後、Binding APIはExplorer production baseline（`main`）から派生した専用branch/worktreeに統一する方向。`agent/right-stick-vanilla-turn`をBinding APIの本番基盤として使わない。

## NocturneModernGameplay スローダウン調査 サマリ

- Controller Key Config後slowdownは、`NocturneModernGameplay.dll`が必要条件であることをA/B/Aで確認済み（`NocturneModernController`単体・`MelonLoader`単体・`Framerate Mod`・`NocturneKeyboardInput`+`QuickPass`はいずれもNORMAL）。
- 最終原因: `SkillCurObjSetActiveTrace`（`UnityEngine.GameObject.SetActive`全域Harmony Prefix、CLOSED済み調査コードの残骸）。A(ON)=SLOWDOWN → B(OFF)=NORMAL → A2(ON)=SLOWDOWN で`CONFIRMED`。
- 修正: 同じCLOSED調査（HIDDEN NEW SKILL ENTRY, 2026-09-15 CLOSED）の4つ（`SkillCurObjSetActiveTrace`/`CmpMenuCursorTrace`/`StatusUiFieldOffsetProbe`/`SelectSkillIdOffsetProbe`）を`legacy/skillmutationv3/*.Legacy.cs`へ撤去、`ModMain.cs`からruntime呼び出し除去。修正ビルドでNORMAL確認済み。commit `fb85094`、`origin/fix/skill-mutation-v2.1`へpush済み。
