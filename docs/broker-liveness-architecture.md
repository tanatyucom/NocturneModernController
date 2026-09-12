# Broker liveness architecture(Canonical)

**性質**: 本文書はCanonicalである。`docs/research/ROOT-23_HANDOFF_TEMP.md`(Root-23〜25の生ログ・Evidence詳細)を
人間の承認を得て要約・反映したものである。生の試験ログ・時系列詳細は引き続き同ファイルを参照すること。

**最終更新**: 2026-09-06(Root-25完了、production実装・実機Case 1/2 PASS後)

## 1. 背景 — なぜ独立Broker/Helperが必要か

`NocturneModernController.dll`(MODのIl2Cpp/MelonLoaderホスト側)は、物理コントローラーの入力(特に右スティック)を
`NocturneModernController.InputHelper.exe`(SDL3ベースの外部Helper process)経由で取得する。実機調査
(Root-17、Root-19R、Root-22)により、以下がCONFIRMEDとして確立している。

- HelperのprocessがSteam/`smt3hd.exe`のprocess treeから独立している場合(Root-17: 事前起動、Root-19R: 専用Launcher経由)、
  物理コントローラーの右スティックを含む全入力が正常に取得できる。
- HelperがSteamの`%command%` wrapper配下(`smt3hd.exe`の兄弟process)にある場合(Root-22)、
  `gameoverlayrenderer64.dll`がHelperへ注入され、SDL `deviceCount`が誤って2に増加して誤ったdeviceを選択し、
  右スティックが機能しなくなる。同条件下で`0xc0000374`のheap corruptionも再発した。**この経路は`REJECTED`。**

このため、Helperは`NocturneModernController.Broker.exe`という別の独立process(`tools/Broker/Program.cs`)経由でのみ
起動される。BrokerはMOD DLLからの一時ファイルrequest(`%TEMP%\NocturneModernController.Broker.LaunchRequest.json`)を
受けてHelperを`Process.Start`する。Broker自身の起動経路は2通り運用される。

- **Option C(標準)**: `NocturneModernController.Launcher.exe`(`tools/Launcher/Program.cs`)が、Steamでゲームを
  起動する前にBrokerを自分の子processとして起動する。
- **Option B(任意)**: Windows Startup FolderへBroker起動用shortcutを配置し、Windowsサインイン時にBrokerを
  常駐起動しておく。これによりSteamの通常「プレイ」ボタンでもBroker経由の入力が有効になる(Root-23で実機PASS確認済み)。

## 2. Stale marker問題(解決済み)

Brokerは起動時に`%TEMP%\NocturneModernController.Broker.ready.json`へ`{"pid":...,"readyAtUtc":...}`を書き込む。
MOD側(`ExternalInputBridge.Start()`)は従来、このファイルの`File.Exists`のみでBroker生存を判定していた。

`CONFIRMED`(コード読解・Root-24実機PoCの両方で確認):

- Brokerはshutdown request受理時を含むいかなる終了経路(正常終了・crash・強制終了)でもこのmarkerファイルを
  削除しない。
- Windows再起動やBroker強制終了があっても、`%TEMP%`配下のmarkerファイルは物理的に残存する。
- Option B(長時間常駐)運用下でBrokerが何らかの理由で停止したまま次のゲームセッションが始まった場合、
  MODはstale markerを見て「Broker稼働中」と誤認し、無駄な`LaunchRequest.json`を書いて何も起きないまま
  終わる(右スティックが理由不明のまま機能しない)。

## 3. 採用した解決策 — 既存named Mutexによるliveness判定

Brokerは元々、multiple-instance防止のため`Local\NocturneModernController.Broker.SingleInstance`という
named Mutexを自身の生存期間中保持している(`tools/Broker/Program.cs`)。このMutexはOSの仕組み上、
Brokerがどのように終了しても(正常終了・crash・強制終了いずれでも)process終了と同時にハンドルが解放される。

### 実機Evidence(`CONFIRMED`)

- **Root-24**(checkerプロセス、PowerShell/.NET): Broker稼働中に`Mutex.TryOpenExisting(...)`が`True`を返す。
  取得handleを`Dispose()`のみで解放してもBrokerの動作に影響なし。Brokerを正常停止させ`ready.json`がstaleな
  ままでも`TryOpenExisting`は正しく`False`を返す。Broker再起動後は再び`True`に復帰する。
- **Root-25**(実消費者、MelonLoaderホスト下の`smt3hd.exe`内・`ExternalInputBridge.Start()`): 同じ
  `Mutex.TryOpenExisting`呼び出しが実機で`True`を返し、例外は発生しなかった。Broker/Helperの既存起動シーケンス・
  右スティック動作に一切影響しなかった。

これにより、heartbeat方式(Broker側に周期的なmarker更新処理を追加する案)は**不要と判断し、採用しない**。
PID/`ProcessName`/`MainModule.FileName`によるprocess identity照合も、Mutex単独のEvidenceで十分なため
**判定条件には採用しない**(access制約や余計なfailure modeを増やすリスクを避けるため)。必要であれば
補助diagnostic logとしてのみ追加する余地は残すが、現時点では未実装。

## 4. Production実装

`src/ExternalInputBridge.cs`の`Start(MelonLogger.Instance logger)`:

1. Helper実行ファイルの存在確認(`File.Exists(path)`) — 従来通り。
2. `File.Exists(BrokerReadyMarkerPath)` — 従来通り、Brokerが一度も起動されていない場合の早期return用に残す。
3. **新規**: `IsBrokerAlive(logger)` — `Mutex.TryOpenExisting("Local\NocturneModernController.Broker.SingleInstance", out mutex)`。
   `false`の場合、Broker非生存として扱い、`LaunchRequest.json`を書かずwarningを出してreturnする
   (stale markerが残っていても誤ってrequestを書かなくなる)。handleは`finally`で必ず`Dispose()`する
   (`ReleaseMutex()`は呼ばない — このprocessはMutexの所有権を取得していないため)。例外発生時もfail-safe側
   (非生存扱い)に倒す。
4. 上記すべてを通過した場合のみ`RequestBrokerLaunch()`を呼ぶ(従来通り)。

Broker.exe / Launcher.exeへの変更は不要(既存のMutex作成コードをそのまま利用)。`ready.json`削除処理の追加も
行っていない — Mutexが権威的な生存判定を担うため、markerがstaleなまま残り続けても誤判定の原因にならない。

Fail-safe設計: Broker非生存時はexternal right-stick入力のみが利用不可になり、Dash/Quick Heal/Force Encounter等
MODの他機能には一切影響しない(`ExternalInputBridge.Start()`はfire-and-forgetで、この関数内でのreturnは
`ModMain.OnInitializeMelon()`の他の初期化処理を妨げない)。

## 5. 実機検証結果(Case 1 / Case 2、両方PASS)

**試験日**: 2026-09-06。

- **Case 1(Broker稼働中 → Steam通常Play)**: `PASS`。Helperが正常起動し、右スティック・左スティック・
  Quick Heal含む全入力が正常動作。warningは出力されない。
- **Case 2(Broker停止・stale `ready.json`残存 → Steam通常Play)**: `PASS`。新設のfail-safe warningが
  正しく発火し、`LaunchRequest.json`は生成されない。Dash/Quick Heal等の他機能は無影響で正常動作。

生ログ(MODログ・`Broker.log`・`InputHelper.log`の該当行)は`docs/research/ROOT-23_HANDOFF_TEMP.md`の
12・14〜16節を参照。

## 6. 残っている未解決事項

`UNRESOLVED`:

- Helperへの`gameoverlayrenderer64.dll`注入有無は、Root-23/25いずれもHelper process終了後の事後確認と
  なったため直接確認できていない。`deviceCount`が2に増加しないこと・crashが再発しないことは間接的に
  「注入なし」を支持するが、直接Evidenceではない。次回はHelper稼働中に直接確認することが望ましい。
- Option B(Startup Broker)の一般ユーザー向け導線(Installer自動登録等)は**採用しない方針**
  (2026-09-06、ユーザー決定)。production導線は「標準: 専用Launcher」「任意: README記載の手動Startup Folder
  配置」の二本立てとする。Installer・自動Startup登録・設定UI・常駐管理機能への拡張は本方針の対象外。

## 7. 補足(2026-09-12)

本文書は2026-09-06時点のアーキテクチャ状態を記録したものであり、上記の記述(手動Startup Folder配置を
「任意」の導線とする方針を含む)は歴史的記録としてそのまま保持する。

現在(2026-09-12)は、`agent/right-stick-vanilla-turn`ブランチ上で未リリースの代替アーキテクチャ
(Windows Task Schedulerを用いたBroker自動起動)を開発中である。詳細は
[`TASK_SCHEDULER_ALTERNATIVE.md`](TASK_SCHEDULER_ALTERNATIVE.md)を参照。
