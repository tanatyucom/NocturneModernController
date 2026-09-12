# Task Scheduler Right-Stick Alternative(未リリース代替実装)

## Status

- **状態**: 未リリース(unreleased alternative)。一般配布(Nexus Mods / GameBanana)には含まれていない。
- **所在**: ブランチ `agent/right-stick-vanilla-turn` のみ。`main`ブランチへは未マージ、マージ予定もない。
- **位置づけ**: 現在公開中のExplorer-basedな自動右スティック実装(`v2.0.1`、詳細は
  [`EXPLORER_KNOWN_GOOD.md`](EXPLORER_KNOWN_GOOD.md)参照)の後継・上位互換ではない。両者は独立した並行実装
  であり、どちらか一方が優れているという序列関係にはない。目的はAV(アンチウイルス)誤検知やdistribution周りの
  制約に対する代替手段を用意しておくことである。
- Explorer版のCONFIRMED情報(本ドキュメントとの相互参照用):
  - バージョン: `v2.0.1`
  - タグ: `v2.0.1`
  - commit: `a941a66762e3f145766d459b74d5120d4afd7573`
  - 上記commitは本ドキュメント作成時点で`origin/main`の実質的な実装内容と一致する
    (`origin/main`のtipそのものは、この後`docs/EXPLORER_KNOWN_GOOD.md`のコミットにより1つ進んでいるが、
    production sourceの実体はExplorer方式のままである)。

## アーキテクチャ概要

Windows Task Scheduler(Task Scheduler 2.0 COM API、`Schedule.Service`)を使い、MOD(smt3hd.exeのプロセス内で
動作するMelonLoader DLL)が`NocturneModernController.Broker.exe`をSteam/`smt3hd.exe`のプロセスツリーから
完全に独立した形で起動する方式。`explorer.exe`は一切使用しない。

プロセス系譜(process ancestry)は以下のようになる:

```
svchost.exe (Task Scheduler service)
  └─ NocturneModernController.Broker.exe (--origin=task-scheduler)
       └─ NocturneModernController.InputHelper.exe
```

Broker/Helper間のIPCは既存のMemory-Mapped File(`NocturneModernController_SDL_v2`)をそのまま流用しており、
Explorer版・Launcher版・Task Scheduler版のいずれであってもこの部分は完全に共通である。本代替実装が変更した
のは「Brokerをどうやって、どのプロセス系譜の下で起動するか」のみである。

## コンポーネント

- **`tools/TaskSchedulerBroker/BrokerTaskService.cs`**: Task登録・検証・起動・削除を担う唯一のクラス。
  `IsRegistered`/`Validate`/`Register`/`Unregister`/`Run`/`GetState`を公開する。`Program.cs`(本プロジェクトの
  P1検証用ハーネス)には依存せず、MOD DLLプロジェクト・Settings UIプロジェクトの双方から
  `<Compile Include>`で直接参照される設計。
- **`tools/TaskSchedulerBroker/Program.cs`**: Phase P1/P2の受け入れテスト・診断用ハーネス。
  `register-only`/`unregister-only`モードを持つが、**production runtimeの一部ではない**
  (`TaskSchedulerBroker.exe`自体は本番動作パスで実行されることはなく、`BrokerTaskService.cs`のみが
  実際に消費される)。同一プロジェクト内に置くことで再検証・再テストに再利用できるよう保存している。
- **`src/ExternalInputBridge.cs`**: MOD側のエントリポイント。既存のBroker生存確認(`Mutex.TryOpenExisting`)が
  失敗した場合に、Task Schedulerタスクの検証・(必要なら)起動・非同期での準備完了待ちを行う
  (詳細は次節「非同期起動(Phase P4.1)」)。
- **`tools/Broker/Program.cs`**: `--origin=task-scheduler`引数を認識し、Task Scheduler経由で起動された場合のみ
  アイドルタイムアウト自動終了を有効化する(次々節「Brokerライフサイクル(Phase P4)」)。
- **`settings/`**: Settings UIの「自動右スティックサポートを有効にする」チェックボックス。Task定義の存在自体を
  権威(authoritative)とし、設定JSONにON/OFFの真偽値を永続化しない。

## Task登録の設計(所有権安全性)

`BrokerTaskService`は`settings/StartupShortcutManager.cs`(削除済み。旧Startup Folder方式で確立していた
所有権判定パターンを踏襲)と同じ設計思想に基づく:

- 同名のタスクが既に存在し、それが自分自身の登録したものと識別できない場合(`RegistrationInfo.Description`/
  `Author`のマーカー不一致)、**絶対に上書き・削除しない**(`ForeignConflict`として扱う)。
- タスクは`RegisterTaskDefinition`の`TASK_CREATE_OR_UPDATE`で登録するが、更新対象は「自分が所有していると
  確認できたタスク」のみ。
- トリガーは一切追加しない(トリガー数0)。タスクは`Run()`が明示的に呼ばれない限り何もしない
  ("on-demand only")。
- MOD runtime(`ExternalInputBridge`)は**`Register()`/`Unregister()`を絶対に呼ばない**。呼ぶのはSettings UI
  のみであり、ユーザーの明示的なチェックボックス操作に対してのみ実行される。

## Brokerライフサイクル(Phase P4: origin-based idle auto-exit)

Task Schedulerルートはon-demand設計(トリガーなし)であるため、MODがセッション中に`Run()`した
Brokerは、そのセッションが終わった後も無期限に常駐し続けるべきではない。一方、Launcher.exe経由で起動された
Brokerは、Launcher.exeが`smt3hd.exe`の終了を検知して明示的にshutdown requestを送る既存の仕組みを持っており、
この挙動を変更する必要はない。

この2つを区別するため、Task定義のAction引数に`--origin=task-scheduler`を1つ追加した
(`Launcher.exe`は引数なしでBroker.exeを起動するため無変更のまま影響を受けない)。`--origin=task-scheduler`
で起動されたBrokerのみ、Helperプロセスの生存監視・アイドルタイムアウト(10秒、Launcher.exeの既存の
`BrokerReadyTimeoutSeconds`と同じ値を再利用)による自動終了ロジックが有効化される。

## 非同期起動(Phase P4.1)

実機検証で、Task Schedulerの`Run()`要求からBroker.exeの実プロセス起動までに約12.8秒のディスパッチ遅延が
発生するケースが確認された。これは既存の同期待機(旧来のBroker準備完了待ちタイムアウトである10秒)を超えるため、
MOD初期化処理をブロックしたまま失敗してしまうリスクがあった。

調査の結果、この同期待機はゲーム自身の起動シーケンスと共有されるスレッド上で実行されている強い状況証拠が
得られたため(`MelonStartScreen`文字列の存在、本プロジェクトの既存設計コメントとの整合)、`Thread.Sleep`による
同期待機を廃止し、`ModMain.OnUpdate()`から毎フレーム呼ばれる`ExternalInputBridge.Tick()`によるポーリング方式へ
再設計した。

- 新規スレッド・`Task.Run`・`Timer`は一切使用しない(既存の`OnUpdate()`フレームループのみを利用)。
- ポーリング間隔は200ms(旧`Thread.Sleep`の間隔をそのまま再利用)。
- 非同期タイムアウトは60秒(同期待機と異なりstartup遅延のコストがなくなったため、余裕を持った値を採用)。
- タイムアウト到達時は1回だけ警告を出し、以後は同一セッション内で再試行しない。
- ゲーム終了時(`OnDeinitializeMelon`経由の`Stop()`)は待機状態を無条件でクリアする。

実機で、Broker.exeの起動を意図的に遅延させた約49秒の遅延成功ケース、および60秒タイムアウトケースの両方を
確認済み。

## セキュリティ・AV(アンチウイルス)に関する注記

Explorer版の`explorer.exe "<path>"`起動手法は、正規のプロセス分離テクニックであると同時に、一部のマルウェアが
親プロセス偽装(parent-process spoofing)に用いるパターンとヒューリスティック的に類似するため、AV製品による
誤検知(false positive)のリスクを伴う。Task Scheduler方式はこのリスクを回避する代替手段として設計された
(Task Scheduler経由のプロセス起動はWindows標準の管理者/ユーザー操作パターンであり、同種のヒューリスティック
対象になりにくい)。ただし、これはExplorer版が「劣っている」ことを意味しない。両方式とも実機で右スティック
入力の正常動作を確認済みであり、状況に応じて選択可能な独立した代替実装として維持する。

## ビルド・復元手順

本ブランチ(`agent/right-stick-vanilla-turn`)をcheckoutし、以下の6プロジェクトを`dotnet build -c Release`で
ビルドすることで、Task Scheduler版一式が再構築できる:

- `NocturneModernController.csproj`(MOD DLL)
- `tools/Broker/NocturneModernController.Broker.csproj`
- `tools/TaskSchedulerBroker/NocturneModernController.TaskSchedulerBroker.csproj`
- `settings/NocturneModernController.Settings.csproj`
- `tools/Launcher/NocturneModernController.Launcher.csproj`
- `helper/NocturneModernController.InputHelper.csproj`

Commit `36ce068`(production変更一式、feat: add Task Scheduler right-stick alternative)時点でこの6プロジェクト
すべてが独立したgit worktreeから0警告・0エラーでビルド可能であることを確認済み。

## 保存に関する注意

**本実装(Task Scheduler版)を`main`ブランチへマージしないこと。`main`ブランチ・Explorer版のソース
(`v2.0.1`タグが指すcommit)・`v2.0.1`タグ自体を変更・上書き・削除しないこと。**

本ブランチは将来いつでも作業を再開・ビルド・デプロイできるKnown Good状態として保存されているものであり、
Explorer版公開実装の代替候補として独立に評価・検証されるべきものである。
