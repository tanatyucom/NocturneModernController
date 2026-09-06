# Broker Liveness — Root-23〜Root-25 Evidence Archive(investigation history、Canonicalではない)

**性質**: 本文書はEvidence / investigation historyのアーカイブである。Root-23(Startup Broker実機PASS)、
Root-24(既存named Mutexによる生存判定PoC)、Root-25(MOD実行コンテキストからのMutex検証)、および
production実装のbuild/deploy Evidence・実機Case 1/2結果について、生ログ・詳細な時系列記録・試験時点ごとの
Evidence classをそのまま保持する。**本文書自体はCanonicalではない。**

**Canonicalはこちら**: 確定した設計・production実装の要約は
[`docs/broker-liveness-architecture.md`](../broker-liveness-architecture.md) を参照すること。
本文書はその根拠となった生Evidence・investigation historyであり、将来の再調査
(例: `gameoverlayrenderer64.dll`注入の直接確認、PID/identity補助diagnosticの追加検討)で
参照する目的でリポジトリに保持する。

**作成日時**: 2026-09-06(Root-23本試験、Windows sign-out前の状態保全から書き起こし)
**旧ファイル名**: `docs/research/ROOT-23_HANDOFF_TEMP.md`(Canonical反映・commit候補整理に伴い、
investigation historyであることが分かる名前へ改名)

---

## 1. Git状態(保全時点のスナップショット)

```
branch: agent/right-stick-vanilla-turn
HEAD:   0726015c5afea9690b3f49215cd37b873e78dd51 "Fix controller diagram title overlap"
```

`git status`(保全時点):

```
Changes not staged for commit:
	modified:   helper/Program.cs
	modified:   settings/Program.cs
	modified:   src/ExternalInputBridge.cs
	modified:   src/ModernControllerApi.cs

Untracked files:
	CLAUDE.md
	tools/AxisTestBreakawayLaunch/
	tools/Broker/
	tools/Launcher/
	tools/Root19PoC/
	tools/Root22Wrapper/
	tools/Run-AxisTestComparison.ps1
	tools/Run-InGameAxisTestComparison.ps1
	tools/Run-Root2Comparison.ps1
	tools/Run-Root2SingleVariableExperiment.ps1
```

Git commit / push: 一切行っていない。今回も行わない。

## 2. production Launcher/Broker/Helper/MODは未変更であること(CONFIRMED)

保全時点でのソースファイル最終更新時刻(すべてRoot-22/Root-23開始より前):

```
helper/Program.cs           2026-09-06 12:11:42 (Phase 2B完了時点、以降未変更)
src/ExternalInputBridge.cs  2026-09-06 11:40:50 (Production Phase 1完了時点、以降未変更)
tools/Broker/Program.cs     2026-09-06 11:41:09 (Production Phase 1完了時点、以降未変更)
tools/Launcher/Program.cs   2026-09-06 11:41:34 (Production Phase 1完了時点、以降未変更)
```

配置済みproductionバイナリのSHA-256(保全時点):

```
NocturneModernController.Broker.exe:
  3ad83f518036491169f080fb1e90bef57b4c17c2287bf04197c81a0c216defe7

NocturneModernController.InputHelper.exe:
  b596705232eb24c9b64686014a3e0909156b93c97c88821cc4b7d5e1d4ce743e

NocturneModernController.dll (MOD本体):
  6a834e87ef0d09833bef93b77c732598765bd66d74edad5784967677adab18ba
```

配置パス: `C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\...`

再開時は、これらのハッシュが変化していないことを再確認してからRoot-23の結果解釈に進むこと。

## 3. Root-23用Startup shortcut(一時検証専用)

```
配置場所: C:\Users\tanat\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\NocturneModernController.Broker.Root23Test.lnk
TargetPath:       C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.Helper\NocturneModernController.Broker.exe
WorkingDirectory: C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.Helper
WindowStyle: 7 (最小化)
Description: "ROOT-23 TEMPORARY TEST ONLY - NocturneModernController Broker startup validation - safe to delete"
admin権限: 不使用
```

これはproduction Broker.exeを無改変のまま指す、Root-23検証専用の一時ショートカットである。
Root-23完了後は**ユーザーへ削除の確認を取ってから**削除すること(勝手に削除しない)。

## 4. Steam側の前提条件

- Steam Launch Options(AppID `1413480`, SMT3HD)は空であることをread-onlyで確認済み。
  確認方法: `C:\Program Files (x86)\Steam\userdata\870942052\config\localconfig.vdf` の
  `"apps" > "1413480"` セクションに `LaunchOptions` キーが存在しないことを確認(Steamは空でない場合のみこのキーを書く)。
- Steam Inputは意図的にONのまま試験する(Root-23の目的そのものがSteam Input ON環境での検証のため)。

## 5. Root-23の目的とPASS/FAIL基準

**目的**: `Windows Sign-in → Startup Folder → Independent Broker` が、Steam Input ONのまま
`Steam → smt3hd.exe`(通常の「プレイ」ボタン)と並行して、正常なphysical controller inputを
維持できるか(Root-17/19Rと同等のprocess independenceを保てるか)を実機検証する。

**必須手順(ユーザー操作)**:

1. Steam Launch Optionsが空であることを確認
2. Steam InputはONのまま
3. **Windowsから一度サインアウト**
4. Windowsへ再度サインイン(Startup FolderからBrokerが自動起動する)
5. Startup FolderからBrokerが自動起動したことを確認
6. 専用Launcherは使用しない
7. SteamライブラリからSMT3HDの通常の「プレイ」を押す
8. ゲーム内で右スティック・左スティック・代表的なボタン(A/B/X/Y等)を操作
9. 右スティックカメラが実際に正常動作するか確認
10. 通常通りゲーム終了

**重要**: ショートカットを単にダブルクリックするだけではRoot-23にならない。必ず実際のsign-out→sign-inを挟むこと。

**必須観測項目**:

- Broker / Helperのactual process ancestry(Steam/smt3hd.exeとの関係)
- BrokerがSteam/gameより前に起動していたこと
- Helperへの`gameoverlayrenderer64.dll` injection有無
- SDL device enumeration / selected device(vid/pid、`deviceCount`)
- right-stick / left-stick / buttons実入力(`NocturneModernController.InputHelper.log`)
- InputHelper crash有無、Windows Event Logの新規`0xc0000374`有無
- game終了後のHelper lifecycle(正常終了すること)
- Broker常駐継続(**ゲーム終了時にBrokerが終了しないこと自体はFAILとしない**)

**PASS基準**(すべて満たすこと):

- Windows StartupからBroker自動起動成功
- Broker/HelperがSteam game-launch process treeから独立
- HelperにSteam Overlayが注入されない
- physical-lookingなSDL controller取得
- right stick / left stick / buttons すべてPASS
- 新規Helper crashなし、新規`0xc0000374`なし
- Helper lifecycle正常

PASS時: Option BをInstallerの"Optional Steam Integration"候補へ昇格。ただしproduction化はまだ行わない
(下記7節のhardening項目が残っているため、Root-23 PASSだけで一般配布可能とは判定しない)。

**FAIL基準**(いずれか一つでも該当):

- Startup Brokerが期待通り起動しない
- HelperにSteam Overlayが注入される
- controllerがvirtual/zero状態になる
- right stickが動作しない
- Helperがheap corruption等でcrash

FAIL時: 原因をEvidenceとして記録し、**他のStartup方式(HKCU Run/Scheduled Task/Explorer/Steam wrapper/parent spoofing等)へその場で派生せず停止する。**

## 6. Root-22までの重要Evidence(前提として引き継ぐ)

`CONFIRMED`:
- Root-19R: 専用Launcher → Independent Broker → Helper は実機PASS(右スティック含む全入力正常)。
- Root-17: Steam/game起動前に手動起動したIndependent Brokerも実機PASS。
- Root-22: Steam `%command%` wrapper配下(`Steam → Wrapper → Broker/Helper`、Broker/Helperがsmt3hd.exeの兄弟process)はFAIL。
  Helperへの`gameoverlayrenderer64.dll`注入を確認、SDL `deviceCount`が2に増加し誤ったdeviceを選択、右スティックFAIL。
  Phase 2B production Helper(RawXInputProbe除去済み)でも`0xc0000374 / offset 0x117eb5`のheap corruptionが再発。

`STRONG HYPOTHESIS`:
- Steam側のprocess classification/lineageからBroker/Helperを完全に独立させることが、正常なphysical controller viewを得る主要条件。
- Root-22の`0xc0000374`再発は、`RawXInputProbe`固有の問題というより、`gameoverlayrenderer64.dll`がHelperのSDL3/XInput呼び出し経路に注入されている状況そのものが引き金である可能性が高い(RawXInputProbeが無関係とは断定しない)。

`UNRESOLVED`(Root-23で検証する対象そのもの):
- Windows Startup Folderから自動起動したBrokerが、Root-17のmanual pre-existing Brokerと同等に機能するか。
- **Option B(Startup Broker方式)全体は、Root-23完了までUNRESOLVEDのまま扱うこと。CONFIRMEDに格上げしない。**

## 7. Root-23 PASS後に検討する項目(Root-23の範囲外、production化前に必要)

Root-23がPASSしても、これらが未対応のままでは一般配布可能と判定しないこと:

- **stale `ready.json` hardening**: 現行`Broker.exe`はshutdown時に`NocturneModernController.Broker.ready.json`
  マーカーファイルを削除しない。Brokerが異常終了した場合、古いマーカーが残り続け、MOD(`ExternalInputBridge.cs`)が
  「Broker稼働中」と誤認して誰も処理しないLaunchRequestを書くだけで終わる可能性がある。
  Option Cでは実害がない(Launcherが毎回起動時にstaleマーカーを削除してから新しいBrokerを起動する自己修復設計のため)が、
  長時間常駐するOption Bでは実害の窓が大きく開く。マーカーへtimestampを含め鮮度チェックを行う、
  またはBrokerが定期的にマーカーを更新する設計への変更を推奨。
  **→ Root-24/25を経てMutexベースのhardeningとして解決済み。詳細は16節、Canonicalは`docs/broker-liveness-architecture.md`。**
- Broker shutdown/cleanup(Uninstaller連携含む)
- Startup shortcut install/uninstall(Installer実装)
- Settings(`NocturneModernController.Settings.exe`)からのON/OFF切り替え導線
- idle時のevent-driven化(現行300msポーリングから`FileSystemWatcher`等への変更)
- Installer UI(opt-inチェックボックス文言含む、Root-23とは別セッションで既に日本語案を提示済み)

**2026-09-06追記**: 上記のうち「Installer自動Startup登録」「設定UIからの常駐管理」は、ユーザー決定により
production方針として**採用しない**。production導線は「標準: 専用Launcher」「任意: README記載の手動Startup Folder
配置」の二本立てとする(`docs/broker-liveness-architecture.md` 6節参照)。

## 8. 制約(継続して遵守すること)

- production code変更: NONE(Root-23試験自体では変更しない)
- 既存Canonical文書を人間の承認なしに変更しない
- Git commit / push: しない
- Root-23がFAILした場合、他のStartup方式や過去に棄却した方式へ勝手に派生しない

---

## 9. 再確認ログ(PC再起動前の追加検証)

**検証日時**: 2026-09-06 14:5x頃(PC再起動直前、別Claude Codeセッション(git-c6)による再確認)

以下すべて、本文中の記載どおり変化がないことをread-onlyで再確認済み(変更は一切行っていない):

- `git branch --show-current` = `agent/right-stick-vanilla-turn`、`git log -1` = `0726015 Fix controller diagram title overlap`(本文記載と一致)。
- `git status --short` = 本文「1. Git状態」に記載の内容と完全一致(modified 4件、untracked一覧も一致)。
- ソースファイル最終更新時刻、本文記載と一致:
  - `helper/Program.cs` = 2026-09-06 12:11:42
  - `src/ExternalInputBridge.cs` = 2026-09-06 11:40:50
  - `tools/Broker/Program.cs` = 2026-09-06 11:41:09
  - `tools/Launcher/Program.cs` = 2026-09-06 11:41:34
- 配置済みproductionバイナリのSHA-256、本文記載と一致(3件とも完全一致):
  `NocturneModernController.Broker.exe` / `NocturneModernController.InputHelper.exe` / `NocturneModernController.dll`。
- Root-23用Startup shortcut(`NocturneModernController.Broker.Root23Test.lnk`)の実在を確認済み。

**結論**: 本文書(1〜8節)は現時点で内容の陳腐化なし。新規Claude Codeセッションはこの文書のみを読めば、PC再起動(sign-out相当)後のRoot-23実機試験結果確認へそのまま進める状態。

**このセッション(git-c6)での対応事項**:
- ユーザーより「モダンコントローラー側で誤って書いてしまった指示書」への対応を依頼されたが、誤送信先の別セッション(gameplay側、複数オフライン)は特定不能のため、そちらへの保全作業は行わないことをユーザーと確認済み(メッセージ送信は行っていない)。
- 本ファイルの正確性をread-onlyで再検証し、本節(9節)を追記した。これ以外のファイル変更・Git操作は一切行っていない(Git commit/push: 引き続きNONE)。

## 10. Root-23 実機試験結果(本試験、PASS)

**試験日時**: 2026-09-06 14:5x(Windows再起動・再sign-in後)〜15:05頃(別Claude Codeセッション、本セッションによるread-only確認)

**結論**: `PASS`。5節記載のPASS基準をすべて満たし、FAIL基準への該当なし。

`CONFIRMED`:

- Windows再起動後、Startup FolderからBrokerが自動起動した。
  `NocturneModernController.Broker.exe`(PID 37444)が14:53:35に起動していることを確認。
- BrokerはSteam/smt3hd.exeのprocess treeから独立。
  Process ancestry: `Broker.exe(37444)` ← 親`explorer.exe(7200, 14:51:51起動)` ← 遡及不能(正常終了済み祖先)。
  Steam(PID 9376, 14:52:48起動)/smt3hd.exeをいずれも経由していない。
- Broker経由Helperでright stick / left stick / buttonsが実機PASS。
  `NocturneModernController.InputHelper.log`にて、`AXIS X/Y ENGAGED/NEUTRAL`(右スティック、`CAMERA CONTEXT ACTIVE/INACTIVE`と連動)、
  `LEFTSTICK X/Y ENGAGED/NEUTRAL`、`BUTTON A/B/X/Y/LB/RB/DpadUp/DpadLeft/DpadDown PRESSED/RELEASED`をすべて確認。
- SDL deviceCount=1、Xbox 360 Controller 045E:028E selected。
  `DEVICE SELECTED instanceId=2 name="Xbox 360 Controller" path="XInput#1" vid=0x045E pid=0x028E ... deviceCount=1`。
  Root-22で観測された`deviceCount=2`への誤増加は再現せず。
- Helper正常終了。
  `InputHelper.log`末尾は`STOP`(15:05:33)。例外/クラッシュスタックの記録なし。
- Root-23試験時間帯の新規`0xc0000374`なし。
  Windows Event Log(Application)を`2026-09-06 15:00:00`以降でfilterし、`InputHelper`/`Broker`/`0xc0000374`該当イベント0件。
  既存の4件の`0xc0000374`(fault offset `0x117eb5`共通)はいずれも14:51:51のWindowsサインインより前のタイムスタンプ(13:55/11:44/11:09/10:29)であり、
  Root-22時点の既知crash(6節記載)と一致、Root-23試験ウィンドウ外。
- Brokerはゲーム終了後も常駐継続している。
  確認時点でPID 37444が稼働継続中。5節PASS基準上、これはFAILとしない。

`UNRESOLVED`:

- Root-23実行中Helperへの`gameoverlayrenderer64.dll`注入有無は直接未確認。
  Helper processが確認時点で既に正常終了しており、module listを直接検証できなかった。
  `deviceCount`が2に増加しなかったこと、Root-22型のheap corruptionが再発しなかったことは間接的に「注入なし」を支持するが、
  module list等による直接Evidenceではない。次回はHelper稼働中に直接確認することを推奨。

`STRONG HYPOTHESIS`:

- Steam/game process treeから独立したBroker/Helper lineageが、正常なphysical controller viewを得る主要条件である。
  Root-17/19R/23がこの条件下で一貫してPASSし、Root-22はSteam `%command%` wrapper配下で一貫してFAILしたことから支持される。

`REJECTED`:

- Steam `%command%` wrapperをproduction経路として使う案。
  Root-22実機試験でHelperへの`gameoverlayrenderer64.dll`注入、SDL `deviceCount`の誤増加(2)、右スティックFAIL、
  `0xc0000374` heap corruptionの再発を確認済み(6節)。

**Root-23完了に伴う扱い**: Option B(Startup Broker方式)は本試験でPASSしたが、7節記載のhardening項目
(特にstale `ready.json`対策)が未対応のため、**production化・一般配布可能とはまだ判定しない**(7節の方針を維持)。

**このセッションでの対応**: 上記結果をread-onlyで確認し、本節(10節)を追記した。これ以外のファイル変更・Git操作は行っていない
(Git commit/push: 引き続きNONE)。Root-23用Startup shortcutは削除していない(ユーザー指示により保持継続)。

---

## 11. ready.json stale marker hardening — 設計整理(read-only、未実装)

**性質**: 本節はproduction code変更を一切伴わない設計整理である。実装・build・deploy・Git writesはまだ行わない(ユーザー指示)。

### 11.1 stale markerがどのケースで残るか(Evidence)

`CONFIRMED`(コード読解による、`tools/Broker/Program.cs`):

- mainループ(80〜95行)には、shutdown request受理時(`SHUTDOWN REQUESTED`)を含むいかなる終了経路でも
  `ReadyMarkerPath`を削除する処理がない。Brokerが自ら正常終了する唯一の経路(ShutdownRequest経由)でもmarkerは残る。
- Brokerがunhandled exceptionでcrashした場合、OSはBrokerの終了処理コードを一切実行しないため、markerは当然残る。
- Task Manager「タスクの終了」やtaskkill /F等の強制終了でも同様に残る。
- Windows再起動/sign-outでBrokerが終了しても、marker file自体は`%TEMP%`(per-user)に物理的に残存する
  (per-userのTempディレクトリは既定でreboot時に自動clearされない)。

`STRONG HYPOTHESIS`(未実機検証、コード構造からの推論):

- Option B(Startup Folder常駐)運用下でこの問題が顕在化する具体的shape: あるWindows sign-inでBrokerが何らかの理由
  (crash/強制終了/Startup項目の一時的失敗)で稼働していない状態のまま、ユーザーがSteamの通常「プレイ」でゲームを起動すると、
  **そのゲームセッション全体で誰にも気づかれないまま右スティック等が機能しない**状態になり得る。
  Option C(Launcher経由)は起動の都度Launcher自身がmarkerを削除してから新しいBrokerを起動するため
  (`tools/Launcher/Program.cs` 66〜69行)、この窓はほぼ生じない。

### 11.2 MODがBroker生存を誤認する経路

`CONFIRMED`(コード読解による、`src/ExternalInputBridge.cs`):

- `Start()`(59〜65行)は`File.Exists(BrokerReadyMarkerPath)`のみでBroker生存を判定している。
  pidの生存確認・timestampの鮮度確認のいずれも行っていない。
- 判定がtrueの場合、`RequestBrokerLaunch()`が`LaunchRequest.json`を書き込み、ログに
  「Requested broker to launch the SDL input helper」と**成功したかのように**記録して終わる(74〜86行)。
  実際にBrokerがそれを読むかどうかを待機・検証する処理は意図的に存在しない(非blocking設計、コメントに明記)。
- 結果として、markerがstaleな場合、MODは「Brokerに依頼した」ところまでしか分からず、それ以上の失敗検出手段を持たない。
  ユーザーから見ると右スティック等がただ動かないだけで、原因を示すログがMOD側には残らない。

### 11.3 最小変更で確実にlive Brokerを判定する方法(設計案)

**方式A: heartbeat方式(推奨)**

- Brokerの既存300msポーリングループ内で、一定間隔(例: 2〜5秒)ごとに`ReadyMarkerPath`の`readyAtUtc`を
  現在時刻で上書きする(`pid`は不変)。
- checker側(`ExternalInputBridge.Start()`、および`Launcher.WaitForFile`)は、単純な`File.Exists`ではなく
  「fileが存在し、かつ`readyAtUtc`が閾値(例: 10〜15秒)以内」であることを生存条件とする。
- 利点: Broker側の変更が最小(既存ループへのタイマー付き書き込み追加のみ)。crash/強制終了/reboot/hibernate resume
  いずれのケースでも、heartbeatが単純に止まることで自動的にstale判定される。「shutdown時にmarkerを消す」実装を
  別途作る必要がない(消せなくても鮮度切れで自然に無効化される)。
- 欠点: 検出までに最大で閾値分の遅延がある(ただし判定は「ゲーム起動の一瞬」にしか行われないため実害は小さい)。

**方式B: PID existence + identity check(補助)**

- markerの`pid`を使い、checker側で`Process.GetProcessById(pid)`が例外を投げないこと、かつそのprocessの
  `ProcessName`/`MainModule.FileName`が期待するBroker.exeのパスと一致することを確認する。
- 利点: 実装コストが低く、Broker側の変更が不要(pidは既にmarkerに含まれている)。
- 欠点: 単独ではWindowsのPID再利用(reuse)により、無関係なprocessが偶然同じpidを持つ場合に誤って
  「生存」と判定する理論上のリスクがある(identity checkを併用すればほぼ無視できる水準まで低減可能)。
  単独運用は非推奨、方式Aの補助として使うのが妥当。

**方式C: 既存named Mutexの活用(検討候補、要実機検証)**

- Brokerは既に`Local\NocturneModernController.Broker.SingleInstance`という名前付きMutexを自身の生存期間中
  保持し続けている(`tools/Broker/Program.cs` 55行)。このMutexはOSの仕組み上、Brokerがどのように終了しても
  (正常終了・crash・強制終了いずれでも)process終了と同時にハンドルが閉じられる。
- checker側で`Mutex.TryOpenExisting("Local\\NocturneModernController.Broker.SingleInstance", out _)`を呼び、
  成立すればBroker生存中と判定できる。理論上は即時性が最も高く、Broker.exe側の変更が一切不要という利点がある。
- ただし、named kernel objectへのcross-process open時のアクセス権/ACL挙動をこの環境で実機検証していないため、
  `STRONG HYPOTHESIS`止まりであり、単独採用はまだ推奨しない。方式Aの実装後、余力があれば追加の即時検出手段として
  実機検証する価値がある。

**推奨**: 方式A(heartbeat)を主、方式B(PID+identity)を補助として組み合わせる。方式Cは将来の追加検証候補として残す。

### 11.4 timestamp / PID existence / process identity / heartbeat の候補比較

| 候補 | Broker側変更 | 検出遅延 | crash/強制終了への耐性 | reboot後の耐性 | 実装コスト |
|---|---|---|---|---|---|
| 現行(File.Exists) | なし | なし(常にtrueなら誤検出) | なし(誤検出の原因そのもの) | なし | -(現状) |
| timestamp(起動時1回のみ) | なし(既存フィールド) | Option Bの長時間常駐では機能しない(起動直後以外は常に「古い」と誤判定される) | 不可 | 不可 | 低 |
| heartbeat(周期更新) | 小(周期書き込み追加) | 閾値分(数秒〜十数秒) | 良好(自動的にstale化) | 良好 | 低〜中 |
| PID existence | なし | 即時 | 良好(ただしPID再利用の理論的懸念) | 良好 | 低 |
| PID + identity | なし | 即時 | 良好 | 良好 | 低 |
| 既存named Mutex | なし | 即時 | 良好(OS保証) | 良好 | 低(ただしACL挙動の実機検証が必要) |

### 11.5 Option Cとの互換性

- Option C(`Launcher.exe`)は起動の都度、`BrokerReadyMarkerPath`を削除してから新しいBrokerを起動し、
  `WaitForFile`でfile出現を待つ(66〜69行、90行)。
- heartbeat方式・PID方式いずれを採用しても、Launcherが起動直後に見るmarkerは「たった今作られたfresh markerを
  起動直後に確認する」構図であるため、閾値判定・PID判定いずれも即座にPASSする。Option Cの挙動・タイミングに
  実質的な影響はない。
- `Launcher.WaitForFile`は現状「file存在」だけを条件にしているため、方式A採用時はこちらも「存在 かつ 鮮度内」に
  揃えるのが望ましい(揃えなくても、起動直後の1回目だけ旧基準のままになる程度で実害はほぼない)。

### 11.6 crash / reboot / forced kill時の挙動(設計後の想定)

- **Broker crash**: heartbeat停止 → 次にMODが確認する時点で閾値超過 → 生存していないと正しく判定 →
  MODは「Broker未検出」の警告ログを出し、無駄な`LaunchRequest.json`を書かない(現状の「書いて終わる」動作を回避)。
- **Windows reboot/sign-out**: Broker processごと終了しheartbeatも止まる。次回sign-inでStartup Brokerが
  正常起動すればmarkerは即座に新しいpid/timestampで上書きされる(現行Broker起動時の削除→再作成ロジックは維持)。
  Startup Brokerの起動自体が何らかの理由で失敗した場合でも、古いmarkerは閾値超過によりstaleと判定されるため、
  方式Aにより安全側に倒れる(現行のFile.Existsのみの実装より確実に改善する)。
- **forced kill(taskkill/Task Manager)**: crashと同一の扱いになる(heartbeat停止→閾値超過→stale判定)。
  追加のcleanupロジックを別途実装する必要がない。

---

## 12. Root-24 — 既存named Mutexによる生存判定PoC(read-only / minimal PoC)

**試験日時**: 2026-09-06 15:1x〜15:18頃(本セッション、Root-23 Startup Broker常駐中に実施)

**性質**: production code変更なし。PowerShellから`Mutex.TryOpenExisting`を用いた最小PoCと、
既存の一時停止プロトコル(`ShutdownRequest.json`、Launcher.exeが通常使うものと同一の一時ファイル)を用いた
Broker再起動テスト。対象Mutex名: `Local\NocturneModernController.Broker.SingleInstance`。

`CONFIRMED`:

- Broker稼働中(PID 37444)、`[System.Threading.Mutex]::TryOpenExisting(...)`は`True`を返した。
- 取得したhandleを`ReleaseMutex()`を呼ばず`Dispose()`のみで解放した直後も、Brokerの`PID`/`CreationDate`は不変、
  `Broker.log`に新規エントリなし、稼働状態(`LaunchRequest.json`待ちのidle)にも変化なし。
  → **checker側がhandleをopen/disposeするだけでは、Broker自身の所有権やsingle-instance判定に一切影響しない**ことを確認。
- 既存の`ShutdownRequest.json`プロトコル(Launcher.exeが通常呼ぶものと同一)でBrokerを正常停止させたところ、
  `Broker.log`に`SHUTDOWN REQUESTED - exiting cleanly.`が記録され、process(PID 37444)は消滅した。
- **Broker停止の直後**、`NocturneModernController.Broker.ready.json`は**削除されず物理的に残存**しており
  (`{"pid":37444,...}`のまま)、11.1/11.2節で指摘した「`File.Exists`のみでは生存中と誤判定する」stale marker問題を
  その場で再現できた。
- 同時点で`TryOpenExisting`は`False`を返した。**stale markerが残っていてもMutex判定は正しく「非生存」と判定できる**ことを確認。
- 同時点でPID existence check(`Get-Process -Id 37444`)も正しく失敗した(例外)。方式B(PID existence)単体でも
  今回のケースでは正しくstale判定できることを確認(ただしPID再利用の理論的懸念は11.3節の指摘通り残る)。
- Brokerを手動再起動(`Broker.exe`を直接起動、Launcher.exeが行うのと同じ起動)したところ、新PID(26836)で
  `ready.json`が自動的に新しい`pid`/`readyAtUtc`へ上書きされ(既存の起動時削除→再作成ロジックが動作)、
  `TryOpenExisting`は再び`True`を返した。
- PID/process identity(Root-23常駐中のBroker、PID 37444を対象に実施):
  `Get-Process -Id`成功、`ProcessName`="NocturneModernController.Broker"、`MainModule.FileName`は
  期待するexeパスと完全一致(same-userセッション内でaccess denied等の問題は発生しなかった)、
  `Process.StartTime`(UTC)と`ready.json`の`readyAtUtc`の差分は約-130ms(StartTimeの方がわずかに早い、
  「processが起動→その後marker書き込み」という実装順序と整合)。

`STRONG HYPOTHESIS`:

- 今回の4項目(稼働中True/Dispose無害/停止後False/再起動後True)がすべて成立したことから、
  `Mutex.TryOpenExisting`は**この環境(Windows 11、同一userセッション、Local\名前空間)においては**
  Broker生存判定の即時・Broker側無変更の手段として機能する。
- ただし今回の呼び出し元はいずれもPowerShell(素の.NET runtime)であり、実際の消費者である
  `ExternalInputBridge.cs`はMelonLoaderにホストされ`smt3hd.exe`内で動作する(Il2Cpp/Mono相当の実行環境の可能性がある)。
  **その実行コンテキストから同じ`TryOpenExisting`呼び出しが同様に成功するかは未検証**であるため、
  production採用の可否は`STRONG HYPOTHESIS`に留め、`CONFIRMED`へは格上げしない。

`UNRESOLVED`:

- 実際の消費者(`ExternalInputBridge.cs`、`smt3hd.exe`内のMelonLoaderホスト環境)から
  `Mutex.TryOpenExisting`を呼び出した場合の成否は未検証。
- 長期運用(Option Bの数時間〜数日単位の常駐)における繰り返し呼び出しでのhandle leak等の懸念は、
  今回の短時間PoC(数回の呼び出し)では検証範囲外。

`REJECTED`: 該当なし(本PoCで新たに棄却した案はない。11節の`%command%` wrapper棄却は既存のまま維持)。

**現在の状態(本PoC実施結果としての副作用)**:

- Root-23試験時のBroker(PID 37444)は本PoCの中で意図的に停止させ、その後手動で再起動した(新PID 26836)。
- 新しいBroker(PID 26836)の親processは本セッションのPowerShell(PID 24852)であり、**Root-23本試験時点の
  `explorer.exe`起源のancestryとは異なる**。Root-23のPASS判定(10節)自体はこの操作より前に確定済みのため
  影響しないが、現在稼働中のBrokerを「genuineなStartup Folder起動のサンプル」として次の調査へ流用しないこと。
  真正なStartup Folder ancestryを再確認したい場合は、改めてWindows sign-out→sign-inが必要。
- Root-23用Startup shortcutは削除していない。次回sign-inでは通常通りexplorer.exe経由でBrokerが起動する。
- production code変更、build、deploy、Git write: いずれも行っていない。

### 12.1 Heartbeat方式とMutex方式の比較(更新版)

| 候補 | Broker側変更 | 検出遅延 | crash/強制終了への耐性 | reboot後の耐性 | 実装コスト | 本PoCでの検証状況 |
|---|---|---|---|---|---|---|
| heartbeat(周期更新) | 小(周期書き込み追加) | 閾値分(数秒〜十数秒) | 良好(推論) | 良好(推論) | 低〜中 | 未実装・未検証(11節時点のまま) |
| PID + identity | なし | 即時 | 良好(`CONFIRMED`、本PoCで実証) | 良好(推論、reboot自体は未再現) | 低 | `CONFIRMED`(停止後の失敗・identity取得を実証) |
| 既存named Mutex | なし | 即時 | 良好(`CONFIRMED`、本PoCで実証) | 良好(推論、reboot自体は未再現) | 低(実装自体は容易) | `CONFIRMED`(checkerプロセスからは4項目成立)。実消費者(MOD/MelonLoader)からの成否は`UNRESOLVED` |

### 12.2 Heartbeat導入要否の再評価

- 「ready marker(pid + readyAtUtc、既存のまま)+ PID/process identity + 既存named Mutex」の組み合わせだけで、
  少なくとも**checker側の実装がPowerShell/素の.NETである限り**、stale marker問題を解決できる見込みが高い。
  Mutexは即時判定・Broker側無変更という点でheartbeat方式より優れており、PID/identityは
  Mutexが万一開けなかった場合の補助・crosscheckとして機能する。
- **Heartbeatを不要と結論する前に残っている唯一の検証**は、実際の消費者(`ExternalInputBridge.cs`、
  MelonLoaderホスト環境)から`Mutex.TryOpenExisting`を呼び出しても同様に成功するかどうかである。
  これが実機で確認できれば、heartbeatの新設は不要と判断できる可能性が高い。
  逆にMelonLoader/Il2Cppホスト環境でnamed Mutexのopenに何らかの制約が判明した場合は、
  heartbeat方式(Broker側の小変更のみで済み、実行コンテキストに依存しない)を採用する方が安全である。
- 次の推奨検証(Root-25候補、まだ実施しない): `ExternalInputBridge.cs`に**ログ出力のみ**を追加する
  最小限のdebugビルドを用意し、`Start()`内で`Mutex.TryOpenExisting`の成否をログへ記録した上で
  実機起動し、結果を確認する。これは既存のcontroller起動判定ロジックそのものは変更しない
  (常に現行の`File.Exists`判定のまま動作させ、Mutex結果は観測目的のログ追加に留める)ため、
  production挙動への影響を最小化しつつ`UNRESOLVED`を解消できる。ただし「production code変更」に該当するため、
  実施にはユーザーの明示的な許可が必要。

---

## 13. Root-25 — MOD実行コンテキストからのBroker Mutex Open検証(diagnostic-only)

**性質**: `src/ExternalInputBridge.cs`へdiagnostic-onlyのコードを追加し、clean build/deployまで実施した。
production判定ロジック(`File.Exists(BrokerReadyMarkerPath)`)、Helper launch可否の条件、Broker/Launcher/Helper、
`ready.json`仕様、device selectionは一切変更していない。commit/push: まだ行わない。

### 13.1 実装内容(diagnostic-only)

`src/ExternalInputBridge.cs`に以下を追加(全文はファイル参照):

- `using System.Threading;` を追加。
- `BrokerMutexName = "Local\\NocturneModernController.Broker.SingleInstance"` を定数として追加。
- `LogBrokerMutexProbe(MelonLogger.Instance logger)` を追加: `Mutex.TryOpenExisting(...)` の成否を
  `logger.Msg(...)` で記録し、例外発生時は`ex.GetType().FullName`と`ex.Message`を`logger.Warning(...)`で記録、
  `finally`でhandleを必ず`Dispose()`する。結果はログ出力のみに使い、他のいかなる分岐にも使用しない。
- `Start(MelonLogger.Instance logger)`の**先頭**(既存の`File.Exists`判定より前)で`LogBrokerMutexProbe(logger)`を呼ぶ。
  既存の`File.Exists(BrokerReadyMarkerPath)`によるHelper起動判定はこの後も無変更のまま実行される。

### 13.2 Build/Deploy Evidence

`CONFIRMED`:

- `dotnet build NocturneModernController.csproj -c Release`(事前に`bin`/`obj`削除、clean build): 0警告・0エラーで成功。
- Deploy: `bin\Release\net6.0\NocturneModernController.dll` を
  `C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.dll` へ上書きコピー。
- SHA-256: deploy前(旧) `6a834e87ef0d09833bef93b77c732598765bd66d74edad5784967677adab18ba` →
  deploy後(新) `518d63c7cae41b54180b1314fda6eb0b5080252186906e925dcf55d0148d5d7a`
  (build成果物と配置後ファイルのhashが完全一致することを確認)。
- `NocturneModernController.Broker.exe`(`3ad83f51...`)、`NocturneModernController.InputHelper.exe`(`b5967052...`)は
  いずれもRoot-23/24時点のhashから不変(本diagnosticはMOD DLLのみに閉じている)。
- `git status --short`: `src/ExternalInputBridge.cs`のみ本セッションで新規に変更(他の`modified`表示分は
  本セッション以前からの既存差分、1節記載の状態と一致)。`git add`/`git commit`/`git push`は実施していない。

### 13.3 現在のBrokerについて(注記)

現在稼働中のBroker(Root-24 PoCで手動再起動、PID 26836)は親processが本セッションのPowerShellであり、
Root-23本試験時点のStartup Folder(explorer.exe)ancestryとは異なる。Root-25の主目的は
「MOD processからMutexをopenできるか」の検証であるため、このBrokerをそのまま使用する
(ユーザー承認済み)。この実機試験はRoot-23 Startup lineageの再確認としては扱わない。

### 13.4 実機確認待ち事項(ユーザーへ依頼)

以下、Steam Input ONのまま、SMT3HDをSteam通常「プレイ」で起動してご確認をお願いします。

1. ゲームを通常通り起動する(専用Launcherは使わない)。
2. MODログ(MelonLoaderのログファイル、通常`smt3hd`インストールフォルダ配下の`MelonLoader\Latest.log`等)から
   `[NocturneModernController][Root25Probe] Mutex.TryOpenExisting(...)` の行を確認する。
3. 右スティックを軽く動かして問題なく機能することを確認する(regression有無の簡易確認)。
4. 通常通りゲームを終了する。

この間、私(Claude Code)は以下をread-onlyで確認する:

- MODログの`Root25Probe`行(成功/失敗、例外の有無)。
- 可能であれば、ゲーム起動中の`NocturneModernController.InputHelper.exe`への`gameoverlayrenderer64.dll`注入有無
  (Root-23で唯一残っていた`UNRESOLVED`の解消目的)。
- `InputHelper.log`による右スティック実入力の簡易確認。

---

## 14. Root-25 実機試験結果(PASS)

**試験日時**: 2026-09-06 15:52頃〜15:53頃。Steam Input ON、SMT3HDをSteam通常「プレイ」で起動(専用Launcher不使用)。
使用したBrokerはRoot-24 PoCで手動再起動したインスタンス(PID 26836、13節記載の通りStartup Folder lineageではない、
ユーザー承認済み)。

`CONFIRMED`:

- MelonLoaderホスト環境(`smt3hd.exe`内、`ExternalInputBridge.Start()`)から`Mutex.TryOpenExisting(...)`を
  呼び出した結果、MODログに以下が記録された:
  `[15:52:45.447] ... [Root25Probe] Mutex.TryOpenExisting("Local\NocturneModernController.Broker.SingleInstance") = True`
- 例外は一切発生しなかった(ログ全体を`Warning`/`Exception`/`Error`/`Fail`でfilterし、
  `NocturneModernController`関連の該当行は0件)。
- probe直後も既存の起動シーケンス(`Requested broker to launch the SDL input helper.` → Dash/右スティックロード
  メッセージ)がそのまま続いており、`finally`節でのhandle Disposeが起動フローに一切影響していない。
- 右スティック実機動作にregressionなし。MODログに`Q4 right stick Left/Right/Neutral`・`vertical Up/Down/Neutral`が
  多数記録され、`InputHelper.log`にも対応する`AXIS X/Y ENGAGED/NEUTRAL`・`CAMERA CONTEXT ACTIVE/INACTIVE`が記録された。
  ボタン(A/B/Y/DpadUp)、左スティックも正常。
- `InputHelper.log`は`deviceCount=1`のまま安定(Root-22型の「注入により2に増加」は再現せず)、
  末尾は`STOP`(15:53:12)で正常終了。
- `2026-09-06 15:52:00`以降のWindows Event Log(Application)を`InputHelper`/`Broker`/`0xc0000374`でfilterした結果、
  該当イベント0件(新規crashなし)。
- `Broker.log`に興味深い記録: `BROKER START pid=43040 ... SINGLE_INSTANCE_CHECK_FAILED: another broker instance is
  already running. Exiting.`(15:52:11)。ゲーム起動に伴い新しいBrokerが起動しようとしたが、既存のBroker
  (PID 26836、single-instance Mutexの所有者)により正しく弾かれ、LaunchRequestは既存Brokerが処理して
  `HELPER LAUNCHED pid=39160`(15:52:45)を実行した。**single-instance Mutexが意図通り機能していることの
  副次的な実機Evidence。**

`UNRESOLVED`(据え置き):

- `gameoverlayrenderer64.dll`のHelperへの注入有無は、今回もHelper process終了後の事後確認となったため
  直接確認はできなかった。`deviceCount=1`の安定、crash非発生という間接Evidenceは10節と同様に「注入なし」を
  支持するが、直接Evidenceではない。

### 14.1 Root-25 PASS/FAIL判定

ユーザー指定のPASS基準(MOD/MelonLoader contextからMutex open成功/exceptionなし/handle Dispose正常/
existing controller behaviorにregressionなし)を**すべて満たした**。

**結論: `PASS`**。既存named MutexをBroker liveness判定へproduction採用可能と判断するEvidenceが揃った。

## 15. Broker liveness最小hardening設計(ready.json + Mutex + PID/identity、design-only・未実装)

**性質**: 本節は設計のみ。ユーザー指示により、Root-25の結果を受けてもproduction判定ロジック
(`ExternalInputBridge.cs`の`File.Exists(BrokerReadyMarkerPath)`)へはまだ組み込まない。
Heartbeatは新設しない(Root-24/25の結果により不要と判断)。

### 15.1 設計方針

Root-24(checkerプロセス)・Root-25(MOD/MelonLoaderプロセス)の両方で`Mutex.TryOpenExisting`が
即時・無例外・Broker側無変更で機能することが実証されたため、**Mutexを生存判定の一次情報源とし、
ready.jsonのpidをPID/identityによる補助crosscheckとして残す**構成を採用する。heartbeatは不要と判断する
(12.2節で立てた仮説が実機で解消された)。

### 15.2 checker側(`ExternalInputBridge.Start()`)の変更設計(未実装)

現行(`src/ExternalInputBridge.cs:59-65`):

```
if (!File.Exists(BrokerReadyMarkerPath))
{
    logger.Warning(...);
    return;
}
```

設計案:

1. `File.Exists(BrokerReadyMarkerPath)`による事前チェックは残す(Broker一度も起動したことがない場合の
   早期return用、コスト無視できるレベル)。
2. その上で`Mutex.TryOpenExisting(BrokerMutexName, out mutex)`を生存判定の**主判定**とする。
   `false`ならBroker非生存として扱い、現行と同じ警告を出して`LaunchRequest.json`を書かない
   (stale markerが残っていても誤ってrequestを書かなくなる、これが11.2節で指摘した誤認識の直接的な解消)。
   handleは取得直後に`finally`で必ず`Dispose()`する(Root-25の`LogBrokerMutexProbe`と同じ作法)。
3. (任意・低優先) Mutexが開けた場合にのみ、`ready.json`の`pid`を読み、
   `Process.GetProcessById(pid).MainModule.FileName`が期待するBroker.exeパスと一致するかを
   診断ログとしてのみ記録する(Mutexが既に権威的な判定を下しているため、これは冗長性のための
   補助情報であり、判定を分岐させる条件にはしない)。

### 15.3 Broker.exe / Launcher.exe側

- **Broker.exe: 変更不要。** 既存の`Local\NocturneModernController.Broker.SingleInstance`
  Mutex作成コード(`tools/Broker/Program.cs:55`)がそのまま生存判定の基盤として機能する。
  heartbeat追加は不要と判断する。
- **Launcher.exe: 変更は任意・低優先。** Option Cは起動の都度`BrokerReadyMarkerPath`を削除してから
  新Brokerを起動し`WaitForFile`で待つ設計(`tools/Launcher/Program.cs:66-69`,`90`)であるため、
  stale marker問題は元々ほぼ発生しない。対称性のため`WaitForFile`にも同様のMutexチェックを
  追加する余地はあるが、11.5節の分析通りOption Cの正しさには影響しない。

### 15.4 この設計が解決するもの・解決しないもの

- 解決: 11.1/11.2節で指摘した「Brokerがcrash/強制終了/reboot後も起動しなかった場合に、
  stale `ready.json`だけを見たMODが誤って`LaunchRequest.json`を書いて終わる」問題。
  Mutexは`ready.json`の内容に関わらず、Broker実プロセスの生死をOSレベルで正確に反映する。
- 解決しない(想定通り、対象外): `ready.json`自体をBroker shutdown時に削除する処理は依然として
  実装されない。ただしMutexが権威的な判定を担う設計に移行するため、**この削除処理自体が
  不要になる**(stale fileが残り続けても誤判定の原因にならないため)。

---

## 16. Broker liveness hardening — production実装(build/deploy済み、実機確認待ち)

**性質**: 15節の設計をもとに、`src/ExternalInputBridge.cs`へproduction実装した。commit/pushは行っていない。

### 16.1 実装内容

- `Start()`内の判定順序: ①`File.Exists(path)`(Helper.exeの存在) → ②`File.Exists(BrokerReadyMarkerPath)`(現行のまま維持) →
  ③新規`IsBrokerAlive(logger)`(`Mutex.TryOpenExisting(BrokerMutexName, ...)`)。②③いずれかがfalseならwarningを出し
  `RequestBrokerLaunch`を呼ばずreturn。
- `IsBrokerAlive`: handleは`finally`で必ず`Dispose()`(`ReleaseMutex()`は呼ばない)。例外時はwarningを出し
  false扱い(fail-safe側に倒す)。
- Root-25用の`LogBrokerMutexProbe()`は削除し、`Start()`冒頭の呼び出しも削除した。
- PID/`ProcessName`/`MainModule.FileName`照合は、ご指示通り**判定条件に含めていない**(未実装)。
- Broker.exe/Launcher.exeは無変更。heartbeatは実装していない。`ready.json`削除処理も追加していない。

### 16.2 PID/identityを補助diagnostic logとしてのみ残す設計案(未実装、提示のみ)

必要であれば、`IsBrokerAlive`がtrueを返した場合にのみ(判定を分岐させず)、下記をログ1行追加する形で
補助情報として残せる:

```csharp
// 判定には使わない。診断ログのみ。
try
{
    var readyJson = JsonSerializer.Deserialize<...>(File.ReadAllText(BrokerReadyMarkerPath));
    var proc = Process.GetProcessById(readyJson.Pid);
    logger.Msg("[NocturneModernController] Broker identity check: pid=" + readyJson.Pid +
        " processName=" + proc.ProcessName + " path=" + proc.MainModule.FileName);
}
catch (Exception ex)
{
    logger.Msg("[NocturneModernController] Broker identity check unavailable: " + ex.GetType().Name);
}
```

Mutexが既に権威的にtrueを返している以上、このブロックの結果によって`RequestBrokerLaunch`の呼び出しを
変えないこと。`MainModule.FileName`アクセスが稀に失敗しうる(x86/x64クロスビットアクセス等)ため、
必ず`try/catch`で囲み、失敗してもログレベルの問題に留める。現時点では未実装。

### 16.3 Build/Deploy Evidence

`CONFIRMED`:

- `dotnet build NocturneModernController.csproj -c Release`(事前に`bin`/`obj`削除、clean build): 0警告・0エラー。
- Deploy: `bin\Release\net6.0\NocturneModernController.dll` → `smt3hd\Mods\NocturneModernController.dll`。
- SHA-256: deploy前(Root-25 diagnosticビルド) `518d63c7cae41b54180b1314fda6eb0b5080252186906e925dcf55d0148d5d7a` →
  deploy後(今回) `8a3acaf81cbd728d0b71a9fcacb48ca7b96d69168255ad61e06b95e203694cfe`
  (build成果物と配置後ファイルのhashが完全一致)。
- `Broker.exe`(`3ad83f51...`)/`InputHelper.exe`(`b5967052...`)は不変(Broker/Launcher無変更のご指示通り)。
- `git status --short`: 本セッションでの新規変更は`src/ExternalInputBridge.cs`のみ(他の`modified`表示は
  1節記載の既存差分)。`git add`/`commit`/`push`は未実施。

### 16.4 二重`TryOpenExisting`疑義の確認(該当なし)

実機試験前に、`IsBrokerAlive()`内で`Mutex.TryOpenExisting(...)`が2回呼ばれておりhandleがleakしうるのでは、
という指摘があった。`grep -n "TryOpenExisting" src/ExternalInputBridge.cs`で全文検索した結果、該当箇所は
99行目の1箇所のみ(`return Mutex.TryOpenExisting(BrokerMutexName, out mutex);`)であり、二重呼び出しは
**source上に存在しないことをconfirmed**。`mutex`は`out`パラメータとして1回だけ代入され、`finally`で
必ず`Dispose()`されるため、handle leakは発生しない。指摘は前メッセージで提示した`git diff`表示
(変更前後の行が並ぶ差分形式)の誤認によるものと推定される。16.1節記載の実装・16.3節記載のbuild/deploy
Evidenceに修正は不要と判断し、現行deploy済みDLL(SHA-256 `8a3acaf81cbd728d0b71a9fcacb48ca7b96d69168255ad61e06b95e203694cfe`)
のまま16.5節の実機試験へ進む。

### 16.5 実機確認待ち事項(ユーザーへ依頼)

1. **Broker稼働中 → Steam通常Play → 右スティック正常**の確認。
   現在Broker(PID 26836、Root-24 PoCで手動再起動したインスタンス)が稼働中のため、このままSteam通常「プレイ」で
   起動し、右スティックが今まで通り動作することを確認する。MODログに`Requested broker to launch the SDL input helper.`
   が出ること(＝`IsBrokerAlive`がtrueを返し、Fail-safe分岐に入っていないこと)を確認する。
2. **Broker停止 + stale ready.json残存 → Steam通常Play → 安全な警告**の確認。
   Broker停止後、ゲームを起動し、MODログに新設のwarning
   (`Broker marker file exists but the broker process is not alive (stale marker)...`)が出て、
   `LaunchRequest.json`が書き込まれないこと、Dash等の他機能が通常通り動作すること(regressionなし)を確認する。

いずれのケースも私(Claude Code)の方でMODログ・`InputHelper.log`・`Broker.log`・`LaunchRequest.json`の有無を
read-onlyで確認します。

---

## 16.6 実機試験結果(Case 1 / Case 2、両方PASS)

**試験日時**: 2026-09-06 16:45頃〜16:58頃。deploy済みDLL(SHA-256`8a3acaf81cbd728d0b71a9fcacb48ca7b96d69168255ad61e06b95e203694cfe`、
16.1/16.3節記載、16.4節で二重`TryOpenExisting`疑義は該当なしと確認済みのビルド)をそのまま使用。

### Case 1: Broker稼働中 → Steam通常Play → `PASS`

`CONFIRMED`:

- MODログ: `Requested broker to launch the SDL input helper.` → `External SDL gamepad input connected.`
  (`IsBrokerAlive()`がtrueを返し、fail-safe警告は出ていない)。
- `NocturneModernController`関連のWarning/Exception/Errorは0件。
- `Broker.log`: `HELPER LAUNCHED pid=14252`(16:45:45)。
- `InputHelper.log`: 右スティック(`AXIS X/Y ENGAGED`+`CAMERA CONTEXT ACTIVE`)、左スティック、
  RB(Quick Heal既定割当)、A/B/DpadUpすべて正常動作、末尾`STOP`で正常終了。ユーザーからも
  「右スティック正常動作、Quick Heal正常動作」を確認済み。
- 試験時間帯の新規crash・`0xc0000374`なし。

### Case 2: Broker停止(既存`ShutdownRequest.json`機構で正常停止)+ stale `ready.json`残存 → Steam通常Play → `PASS`

Case 1終了後、Broker(PID 26836)を`ShutdownRequest.json`で正常停止(`SHUTDOWN REQUESTED - exiting cleanly.`)。
`ready.json`は削除されず`{"pid":26836,...}`のまま残存(想定通りのstale状態)。この状態で`Mutex.TryOpenExisting`が
`False`を返すことを試験直前にread-onlyで再確認済み。

`CONFIRMED`:

- MODログ(16:57:23)に新設のfail-safe警告が正しく発火:
  `Broker marker file exists but the broker process is not alive (stale marker from a crashed or terminated
  broker) - external right-stick input will not be available this session; other controller features are
  unaffected. Restart the broker...`
- `Requested broker to launch the SDL input helper.`行は**出力されていない**(`RequestBrokerLaunch`が
  呼ばれなかったことを確認)。
- `LaunchRequest.json`は**生成されていない**。
- `Broker.log`はCase 1の`SHUTDOWN REQUESTED`以降変化なし(新しい`HELPER LAUNCHED`エントリなし、
  Brokerプロセス自体が存在しないため当然の結果)。
- `InputHelper.log`の最終更新はCase 1時点(16:46)のまま(Case 2で新規Helperプロセスは起動していない)。
- `ready.json`は引き続きstaleな`pid=26836`のまま。
- ユーザーよりQuick Heal等の他機能が正常動作したことを確認済み。ログ上も`Q7 AUTO-RECOVER`等が
  fail-safe警告後に通常通り記録されており、fail-safeが他機能を妨げていないことを確認。
- 試験時間帯の新規crash・`0xc0000374`なし。

### 結論

Case 1・Case 2ともに`PASS`。16節で実装したBroker liveness hardening(既存named Mutexによる`IsBrokerAlive()`)が、
実機で設計通り機能することを確認した。11.1/11.2節で指摘した「stale `ready.json`をMODが誤ってBroker稼働中と
誤認し、無駄な`LaunchRequest.json`を書いて終わる」問題は、本実装により解消されたと判断する。

**2026-09-06追記(Canonical反映)**: 上記の結論を受け、要約を`docs/broker-liveness-architecture.md`(Canonical)へ
ユーザー承認のうえ反映した。本文書(旧`ROOT-23_HANDOFF_TEMP.md`)はCanonical化の根拠となった生Evidence・
investigation historyのアーカイブとしてリポジトリに保持する(削除しない)。

Git commit/push: まだ行っていない。

---

## 17. Root-23〜25シリーズの状態(このアーカイブについて)

Root-23(Startup Broker実機PASS)からRoot-25(MOD実行コンテキストでのMutex検証・production実装・実機Case 1/2 PASS)
までの一連の調査は完了した。以降、本アーカイブへの追記は「将来の再調査で過去のEvidenceを更新・補強する場合」
(例: `gameoverlayrenderer64.dll`注入の直接確認、PID/identity補助diagnosticの追加実装・検証)に限定し、
確定した設計・production挙動の要約は常に`docs/broker-liveness-architecture.md`側を更新すること。

## 再開時にClaude Codeへ渡す一文(historical、Root-23〜25シリーズは完了済み)

> (参考、過去の再開用文言): Root-23引き継ぎ: `docs/research/ROOT-23_HANDOFF_TEMP.md`(現
> `docs/research/BROKER_LIVENESS_ROOT23_ROOT25_EVIDENCE.md`)を読み、Windows sign-out→sign-in後の
> Root-23実機試験結果を確認してください。
