# Root-26 Handoff (2026-09-09, セッション終了時点)

保全用スナップショット。金曜日再開のためのCanonical要約。本ファイル自体はEvidenceの複製・要約であり、詳細な一次記録は
`docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md`（現在14768行）のChapter122〜127を参照。
本セッションでコード変更・build・deployは既に完了済みだが、**Git commit/pushは一切行っていない**。
このsnapshot作成自体もdestructiveな操作（stash/reset/revert/discard）を一切行っていない。

## 0. 今すぐ再開する場合、最初にやること

`il2cpp_class_get_name` / `il2cpp_class_get_namespace` を`GameAssembly.dll`から`DllImport`で呼び出すread-onlyな
最小probeを実装し、`delegateKlass=0x20CCE388BF0`相当の値（再起動後は変わる可能性が高いので、その回のログから
毎回取り直すこと）の実クラス名・namespaceを特定する。これがユーザー承認待ちで止まっている次の1手。

呼び出し対象は`GameAssembly.dll`のexport tableで直接CONFIRMED済み（pefileで確認、`total exports: 241`中に
`il2cpp_class_get_name`・`il2cpp_class_get_namespace`が実在）。offsetの推測は不要。

## 1. 現在の一本道（絶対に失いたくない要約）

```
RSTICK DEAD/LIVE問題
  → Chapter113/114: LastInputIndex復帰パス = 0x182602F30 → 0x1519140 → call qword ptr [rax] (vtable経由)
  → Chapter115: 観測probe実装、ただしProcess.Modules毎フレーム列挙で重大な速度低下 → 無効化
  → Chapter125.1/125.2: 性能修正（module一覧を初回1回だけcache）→ 実機で速度低下なしを再確認
  → Chapter125.2: P1(steamclient64.dll直結)は REJECTED。target=[GameAssembly.dll+0x1633C70]（同一process内）
  → Chapter125.2: P4(target識別子は不変、戻り値だけがDEAD/LIVEで変化) が CONFIRMED
  → Chapter125.3: そのtarget関数はDictionary風bucket/entryテーブル検索+delegate起動という汎用パターンとCONFIRMED
  → Chapter126: param_1(rcxGate)のoffsetが記録ミス（+0x80と記録されていたが実際は+0x50）とCONFIRMED、修正
  → Chapter127.1/127.2: 修正後の実機テストで、Dictionary本体(buckets/entries/全entryのhash・next・value・
    delegateField)がDEAD⇔LIVEを何度跨いでも完全不変と確認 → A(再登録)は REJECTED、B(delegateの答えが変わる)が CONFIRMED
  → Chapter127.3: delegate起動ヘルパー(func_0x0001800024e0)はIL2CPP汎用interface dispatcherで
    Guide/RSTICK固有ロジックなしとCONFIRMED。実際の呼び出し先はdelegateFieldの実クラス次第。
  → Chapter127.4: delegateFieldのklassポインタをruntimeで取得成功: delegateKlass=0x20CCE388BF0
    （ただしGameAssembly.dllのPEイメージ範囲外＝ヒープ上のIl2CppClass、既存のモジュール名解決では特定不可）
  → ★次はここ★: il2cpp_class_get_name/get_namespace で実クラス名を特定 → 未着手、ユーザー承認待ちで停止
```

## 2. 直近の重要な訂正2件

1. **`rcxGate`のoffset訂正（Chapter126）**: `rcxGate = *(*(utilPtr + 0x50) + 0x10)` が正。
   Chapter114.8/115で記録していた`+0x80`は誤記であり、これが実機テストで`rcxGate`が常に`0x0`だった直接原因。
   x64生disassembly（`0x1825F9E10`のUpdateInput()本体、call site `1825f9fa0: MOV RCX,[RSI+0x50]`）で
   machine register単位で確認済み（decompiler変数名には依存していない）。
2. **マウスカーソル観測の訂正（Chapter127.5）**: 「Chapter115の重量probe固有の副作用」という解釈はREJECTED
   （軽量化後の正常速度セッションでも再現したため）。現在のユーザー観測は
   **「RSTICK DEAD→LIVEへ切り替わる、まさにその瞬間に押したGuideでカーソルが動いている可能性が高い」**
   という限定的なものであり、「Guide一般」「Guide+RSTICK一般」での毎回の再現とはまだ確定していない
   （STRONG HYPOTHESIS、因果はUNRESOLVED）。

## 3. カメラ約3.4倍加速問題（Chapter116〜124）— 未解決のまま保留（TODO）

放棄ではなく一時保留。要約:
- 横方向(yaw)：Guide ON相当区間で約3.1〜3.7倍（平均約3.4倍）に加速することを、動画解析・実機ログの両方でCONFIRMED。
- native入力(`nativeX`)自体は不変。`fldCamera.mAxis`/`mAcceleration`がGuide区間相当でのみ跳ね上がる相関をSTRONGとして確認。
- ただし`mAxis`のwriter追跡は迷走中: 当初「calcCamNormal内」と誤認した書き込み箇所は実際は`fldCamMain`内であり、
  かつ実機診断で該当クラスが一度も初期化されない（`+0xb8`が常にNULL）ことが判明し、
  **この書き込み経路自体が実際の発火経路ではない可能性が高い**（Chapter124末尾で自己訂正済み、詳細は
  投資ドキュメントChapter124.後半参照）。
- `Chapter124CameraGainProbe`は**現在もModMain.csでactiveなまま**（無効化していない）。RSTICK側probeと同時に
  ログが出続けている（タグ`[Ch124]`）。
- 次に再開する場合の入口は、投資ドキュメントのChapter124末尾に記載の通り、mAxisの本当のwriterを
  `calcCamNormal()`本体まで含めて再捜索するところから。

## 4. Git状態（このセッションでは一切commit/push/stash/reset/revertしていない）

- ブランチ: `agent/right-stick-vanilla-turn`
- 変更済み(tracked, modified, 未commit):
  - `NocturneModernController.csproj`
  - `docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md`
  - `helper/Program.cs`
  - `settings/Program.cs`
  - `src/ModMain.cs`
  - `src/ModernControllerApi.cs`
- 未追跡(untracked, 新規ファイル、一部は本セッション以前からの既存の未commit研究ファイル):
  - `284C3827E7051C67.mp4` / `6C22F05179F1CDA8.mp4`（実機動画、Chapter121-124で使用）
  - `CLAUDE.md`
  - `docs/research/ROOT26_PRE_REBOOT_EVIDENCE_20260907/`（前回のhandoff）
  - `docs/research/ROOT26_HANDOFF_20260909/`（本ファイル、今回新規）
  - `settings/StartupShortcutManager.cs`
  - `src/Chapter122GuideYawCorrelationProbe.cs`（無効化済み、削除していない）
  - `src/Chapter123CameraOrientationProbe.cs`（無効化済み、削除していない）
  - `src/Chapter124CameraGainProbe.cs`（**active**）
  - `src/FocusCycleProbe.cs`
  - `src/ManualResetControllerPoc.cs`
  - `src/RightStickPollingProbe.cs`
  - `src/Root26Action*.cs` 他、多数の`Root26*.cs`（過去チャプターからの既存probe群、多くはactive）
  - `src/Root26LastInputIndexVtableTargetProbe.cs`（**本セッションで大幅修正、active**）
  - `tools/AxisTestBreakawayLaunch/` `tools/Root19PoC/` `tools/Root22Wrapper/` 他

**重要**: 上記いずれも本セッションでは削除・discard・stash・reset・revertしていない。次回セッションで
`git status`を再確認し、意図せず失われたものがないか照合すること。commit/pushは引き続きユーザーの明示指示待ち。

## 5. デプロイ済みDLL（現在game Modsフォルダに配置中のビルド）

```
ソースbuild: bin/Release/net6.0/NocturneModernController.dll
デプロイ先 : C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.dll
SHA-256    : f79ee54962246eff17b2ef5e3c7c38f0e25d7f216a02b1b1bee588297ad2a8f0
（ソース・デプロイ先とも一致確認済み、本セッション最終build）
```

`ModMain.cs`で現在activeなprobe（抜粋、安全上重要なもの）:
- `Root26LastInputIndexVtableTargetProbe.Sample()` — **active**（Chapter125で性能修正・再有効化、Chapter126/127でoffset修正・klass読み取り追加）
- `Chapter124CameraGainProbe.Sample()` — **active**（camera加速調査、保留中だがコードは動作中）
- `Root26Phase4NativeRecoveryPoc.Sample()` — **無効化のまま**（コメントアウト、F10ゲート付きstate-mutating PoC）
- `Root26Phase8OverlayToStoreOpenPoc.Sample()` — **無効化のまま**（コメントアウト）
- `Chapter122GuideYawCorrelationProbe` / `Chapter123CameraOrientationProbe` 系 — 無効化のまま（ファイルは保持）

## 6. 安全制約（継続中、次回セッションでも厳守）

- F9/F10は絶対に使わない
- `ResetController` / `SteamControllerReStart` / `Shutdown` / `Init` / `UpdateConnectedControllers` /
  `ActivateActionSet*` の手動呼び出し禁止
- `SendInput`禁止、Guide入力偽装禁止
- Steamバイナリへのpatch/injection/hook禁止
- `Root26Phase4NativeRecoveryPoc` / `Root26Phase8OverlayToStoreOpenPoc` は無効化のまま
- Git commit/push/stash/reset/revertは、明示指示があるまで行わない

## 7. 実機ログ保全状況

すべて解析前に保全・ハッシュ確認済み、`investigations/ROOT26_LOGS/`配下:
- `Root26_Chapter123_*.log`（複数、mAxis/mAcceleration関連）
- `Root26_Chapter124*.log`（複数、camera gain関連）
- `Root26_Chapter115b_*.log`, `Root26_Chapter125*.log`, `Root26_Chapter126_*.log`, `Root26_Chapter127_*.log`
  （RSTICK vtable/Dictionary関連、直近のもの）

## 8. 次回セッション開始時のチェックリスト

1. `git status`で本ファイル記載の状態と差分がないか確認（誰も勝手にintegrateしていないか）。
2. デプロイ済みDLLのSHA-256が`f79ee549...`のままか確認（変わっていれば別セッションが動いた可能性）。
3. 本ファイル冒頭「0. 今すぐ再開する場合」の`il2cpp_class_get_name`/`il2cpp_class_get_namespace`実装から再開。
4. `delegateKlass`の値は再起動・再デプロイのたびに変わりうる（ASLR起因のheapアドレス）ため、
   新しいテストログから毎回取り直すこと。
