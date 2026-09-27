# Nocturne Modern Controller

[English README](README_EN.md)

『真・女神転生III NOCTURNE HD REMASTER』（Steam版）のコントローラー操作を現代的にする、Windows向けMelonLoader MODです。右スティックカメラ、場面別キー割当、ゲーム本体のキーコンフィグ編集、統合設定GUIを提供し、ゲームプレイ機能のMODをまとめて設定できる土台になります。

現在のバージョンは **3.0.0** です。変更内容は[CHANGELOG](CHANGELOG.md)を参照してください。

> **v3.0.0の大きな変更**: ダッシュ、クイックヒール、強制エンカウント、Smart Auto Battleは、Controller本体から**独立したMOD**になりました。v2.0.3以前と同じ機能を使う場合は、対応するMODも導入してください。詳しくは「[v2.0.3からの更新](#v203からの更新)」を参照してください。

## 構成

| 配布物 | 内容 | 必須 |
|---|---|---|
| NocturneModernController | コントローラー操作・キー割当・設定GUI（この本体） | 本体 |
| NocturneForceEncounter | 強制エンカウント | 任意 |
| NocturneQuickHeal | クイックヒール | 任意 |
| NocturneModernDash | ダッシュ / ダッシュキープ | 任意 |
| NocturneSmartAutoBattle | Smart Auto Battle | 任意 |

推奨構成は5つすべてですが、ゲームプレイ機能の4つは必要なものだけ入れられます。4つともController無しでも単体で動作し、Controllerと一緒に入れると設定GUIとキー割当に統合されます。

## Controller本体の機能

### 右スティックと汎用入力

- 右スティック左右によるダンジョンの標準左右旋回
- ゲーム内蔵経路を利用した右スティック上下カメラ
- PUZZLEでの右スティックによる回転（論理入力として注入）
- `Full Camera`（左右＋上下）と`Horizontal Turn`（左右のみ）
- Invert X / Invert Y、X/Y感度、デッドゾーン設定
- ダンジョンのLB/RB旋回を抑止しつつ、BATTLEのRB Passを維持
- SDL3 Input Helperによる、特定VID/PIDに固定しないゲームパッド入力

右スティック上下はカメラ座標を独自に書き換えません。ゲーム内部の右スティック縦アナログ経路へ入力を渡すため、標準の補間、球面移動、上下限が利用されます。

### 設定とキー割当

- **統合設定GUI**: 右スティック設定、キー割当、GAMEキーコンフィグ、MOD機能の設定を一画面で管理
- **場面別キー割当**: FIELD/DUNGEON、BATTLE、PUZZLE、MENUごと、最大3ボタン同時押し
- **GAMEキーコンフィグ編集**: ゲーム本体（純正）のキーコンフィグを安全に変更
- **MOD連携**: ゲームプレイMODのON/OFF、選択式の設定値、キー割当を統合GUIで扱う

## ゲームプレイMOD

| MOD | 機能 | 単体での入力 | Controller併用時の既定割当 | 設定ファイル |
|---|---|---|---|---|
| NocturneForceEncounter 0.2.0 | 通常エンカウント可能な場所で、ゲーム本来の遭遇判定へ即時の戦闘開始を要求 | X | X（変更可） | `NocturneForceEncounter.settings.json` |
| NocturneQuickHeal 0.1.0 | 探索中に、所持回復スキルと実際のMPを使って仲間を回復。所持している場合だけ蘇生・状態異常回復 | SELECT | RB（変更可） | `NocturneQuickHeal.settings.json` |
| NocturneModernDash 0.1.0 | ダンジョンとワールドマップで移動1.5倍。LT+RTでキープ切替 | LT/RT長押し、LT+RTでキープ、キーボードP | LT/RT、キープLT+RT（変更可）、キーボードP | `NocturneModernDash.settings.json` |
| NocturneSmartAutoBattle 0.1.0 | ゲーム標準Autoの上で、弱点・耐性・MP・撃破予測からコマンドと単体対象を選ぶ。Auto中の速度変更 | ゲーム標準のAutoボタン | 独自の割当なし（ゲーム標準のAutoボタン） | `NocturneSmartAutoBattle.settings.json` |

- クイックヒールが単体時にSELECTを使うのは、Controllerが無い場合RBがゲーム本来の旋回に使われるためです。Controllerは旋回用のLB/RBを抑止するので、併用時はRBになります。
- Smart Auto Battleのモードは「通常攻撃のみ」（ゲーム標準Autoのまま）と「スキル優先」、速度はx1.0 / x1.5 / x2.0です。新規導入時の既定は「スキル優先」「x1.0」です。速度はゲーム標準AutoがONの戦闘中だけ適用されます。
- 各MODの設定はそれぞれの設定ファイルに保存されます。Controllerがあれば設定GUIの「MOD機能」タブで変更でき、無い場合は設定ファイルを直接編集します（ゲーム再起動後に反映）。

| MOD | 単体動作 | Controller連携 |
|---|---|---|
| NocturneForceEncounter | ○ | ○（Controller 3.0.0以降） |
| NocturneQuickHeal | ○ | ○（Controller 3.0.0以降） |
| NocturneModernDash | ○ | ○（Controller 3.0.0以降） |
| NocturneSmartAutoBattle | ○ | ○（Controller 3.0.0以降。モード・速度の選択はController 3.0.0が必要） |

## 対応コントローラー

SDL3がゲームパッドとして認識する機器を対象にしています。

- Xbox系コントローラー
- DualShock / DualSense
- Nintendo Switch Proコントローラー
- 一般的なUSB / Bluetoothゲームパッド

複数の物理・仮想パッドがある場合は、右スティック入力を返している機器を優先します。Xbox Elite Series 2の有線・無線は実機確認済みです。PS系、Switch系、一般パッドは設計上の対象ですが、個別機種すべてを実機確認したものではありません。reWASDは必須ではありません。

## 必要環境

- Steam版 SMT3HD
- MelonLoader
- Windows x64
- .NET 6 Desktop Runtime（MelonLoader環境に通常含まれます）

## インストール

各ZIPをゲームフォルダー（`smt3hd`）へそのまま展開します。ZIPの中は`Mods/`から始まる構成です。

Controller本体（`NocturneModernController-v3.0.0.zip`）:

```text
smt3hd/
  Mods/
    NocturneModernController.dll
    NocturneModernController.Helper/
      NocturneModernController.InputHelper.exe
      NocturneModernController.InputHelper.dll
      NocturneModernController.InputHelper.deps.json
      NocturneModernController.InputHelper.runtimeconfig.json
      NocturneModernController.Settings.exe
      NocturneModernController.Settings.dll
      NocturneModernController.Settings.deps.json
      NocturneModernController.Settings.runtimeconfig.json
      SDL3.dll
```

ゲームプレイMOD（例: `NocturneModernDash-v0.1.0.zip`）は、それぞれDLL 1つです。

```text
smt3hd/
  Mods/
    NocturneForceEncounter.dll
    NocturneQuickHeal.dll
    NocturneModernDash.dll
    NocturneSmartAutoBattle.dll
```

設定ファイルは初回起動時に`Mods`フォルダーへ作られます。ZIPには含まれません。

## v2.0.3からの更新

v3.0.0では、v2.0.3までController本体に内蔵されていたダッシュ／ダッシュキープ、クイックヒール、強制エンカウント、Smart Auto Battleが**Controllerから削除**され、独立MODになりました。Controllerだけを更新すると、これらの機能は使えなくなります。従来と同じように使うには、対応するMODのZIPも展開してください。

- **設定の引き継ぎ**: 各MODは、自分の設定ファイルが無い初回起動時に、Controllerの設定（`NocturneModernController.settings.json`）から旧設定を読み込みます。
  - 強制エンカウント: `ForceEncounterEnabled`
  - クイックヒール: `QuickHealEnabled`
  - ダッシュ: `DashEnabled`
  - Smart Auto Battle: `SmartAutoEnabled`、`AutoBattleMode`、`AutoBattleSpeed`と、学習済みの耐性情報（`NocturneModernController.smart-auto-knowledge.json`をコピー）

  引き継ぎ後は各MODの設定ファイルが優先されます。Controller側の旧設定と旧ファイルは削除されず、そのまま残ります。
- **キー割当の引き継ぎ**: 強制エンカウント、クイックヒール、ダッシュ、ダッシュキープは旧版と同じアクションIDを使うため、変更済みの割当は、MODを導入するとそのまま使われます。
- **旧「オートバトル」タブ**: 設定GUIから削除されました。Smart Auto Battleのモードと速度は「MOD機能」タブで変更します。
- **Smart Auto Battleの無効化**: 旧版では「無効」にしてもスキル優先の差し替えや学習が一部動き続けていました。独立版では「無効」にすると速度変更・コマンド差し替え・対象差し替え・学習がすべて止まり、ゲーム標準のAutoだけが動きます。

> **重要**: 新しいゲームプレイMODを**v2.0.3以前のControllerと組み合わせないでください**。旧Controllerには同じ機能が内蔵されているため、処理が二重に動きます（Smart Auto Battleのコマンド二重選択、速度の二重書き込みなど）。ゲームプレイMODを使う場合はController 3.0.0以降にしてください。

ゲームプレイMODを外しても、Controllerのキー割当ファイル（`bindings.json`）に残るそのMODの割当は正常な状態です。同じMODを再導入するとそのまま使われるので、手動で削除する必要はありません。

## 設定GUIの開き方

標準割当では、ゲーム中にSelectを約0.8秒長押しすると設定GUIが開きます。GUI表示中はMOD独自アクションを一時停止し、ゲームを最小化します。GUIを閉じると設定・割当・機能変更要求を再読込し、ゲームウィンドウを復元します。

設定は`Mods`内のController DLLと同じ場所にJSONとして保存されます。

## キー割当

- FIELD/DUNGEON、BATTLE、PUZZLE、MENUの場面別割当
- A/B/X/Y、LB/RB、LT/RT、L3/R3、Start/Select、方向キーに対応
- 最大3ボタンの同時押し
- `Press`、`Hold`、`LongPress`、`Toggle`、`DoublePress`のアクション定義
- R3を含むボタンを割当入力として扱う（ゲーム標準の「視点を正面に戻す」など、標準アクションの追加登録は今後の拡張方針）

Controller本体の既定割当は、設定画面を開く（Select長押し）だけです。ゲームプレイMODを導入すると、そのMODのアクションが既定割当つきで追加されます（上の表を参照）。コンテキスト分離により、FIELDの割当がBATTLEのRB Passなどを無条件に置き換えないようにしています。

## GAMEキーコンフィグ

設定GUIの「GAMEキーコンフィグ」タブで、ゲーム本体（純正）のキーコンフィグを変更できます。変更は純正の設定へ保存され、ゲームを再起動しても保持されます。

- 対象は、実機で対応関係を確認済みの15項目です（決定・アクション、キャンセル、UI表示ON/OFF、コマンドメニュー、視点変更（左回転／右回転）、視点を正面に戻す、客観/主観切替、オートマップ表示、スキルヘルプON/OFF、オートバトル、次に回す、テキストの早送り、PUZZLEのメニュー、パンチ）。
- 割り当てられるボタンは A / B / X / Y / LB / LT / RB / RT / L3 / R3 / SELECT / START の12種類です。
- 1回の「適用」で変更できるのは1項目です。「適用」で変更を予約し、「OK / 保存」で閉じるとゲーム側で反映されます。「取り消し」や「キャンセル」で閉じた場合は反映されません。
- 他の項目と重複する割当は、ゲーム本体の重複判定に従って拒否します（入れ替えは行いません）。
- 設定GUIがゲームのメモリへ直接書き込むことはありません。ゲーム内のController MODが、現在値との一致や状態の整合を確認してから、純正の保存処理で反映します。途中で失敗した場合は元の設定へ戻します。
- 結果は次に設定GUIを開いたときに「前回の変更」として表示されます。
- 12種類以外のボタンが割り当てられている項目は `Unknown (raw=N)` と表示し、その項目は変更できません。
- 現在の設定を正しく読み取るため、ゲーム内でフィールドへ入った後に設定GUIを開いてください。

## MOD機能タブ

設定GUIの「MOD機能」タブは、各MODが公開する機能情報からカードを動的に作ります。Controller本体のカードは「Right Stick Camera」だけで、ゲームプレイMODを導入するとそのカードが加わります（Smart Auto Battleは有効/無効、モード、速度の3枚）。

ON/OFFに加えて選択式の値（例: Smart Auto Battleのモードと速度）も扱えます。MODのファイルが破損・未導入でも、そのMODのカードだけを外し、GUI全体は失敗させません。

## 外部MOD連携

ゲームプレイMODはController DLLを参照せず単独で動作し、Controllerが読み込まれている場合だけ実行時に連携します（キー割当、機能カード、選択式の値）。別のMODは`NocturneModern*.features.json`形式のスナップショットでもカードを公開できます。これらは現時点の連携方式であり、第三者向けの安定SDK、NuGet、互換性保証ではありません。

## 確認済み動作

- ダンジョンで右スティック左右旋回・上下標準カメラ
- ダンジョンでLB/RB旋回を抑止し、BATTLEでRB Passを維持
- 統合設定GUI、キー割当、MOD機能タブ（ON/OFFと選択式の値）
- GAMEキーコンフィグの変更（コマンドメニューY→X→Y）: 実入力と純正キーコンフィグ表示への反映、再起動後の保持、往復後に純正設定ファイルが元とバイト単位で一致すること
- Controller＋ゲームプレイMOD4つ、Controllerのみ、各ゲームプレイMOD単体の各構成での起動と動作
- v2.0.3の設定・キー割当・Smart Auto学習データからの引き継ぎ

## 未確認・制限事項

- PUZZLEの右スティック回転ルートは実装済みですが、実プレイでの最終確認は未完了です。
- PS/Switch/一般パッドはSDL3対応方針ですが、全機種の実機互換性は未確認です。
- Smart Auto Battleの戦術判断はヒューリスティックであり、複雑な敵編成や特殊スキルに対する改善余地があります。
- Smart Auto Battleの速度は、勝利時とAuto解除時に元へ戻ることを実機で確認済みです。逃走・全滅・タイトルへ戻る場合は網羅的には確認していません。
- MOD連携の方式は開発中の連携面であり、安定した公開SDKではありません。

## 別プロジェクト: NocturneModernGameplay

ゲームプレイ規則を変更する実験的な機能（Skill Mutation: Learn as New など）は、別プロジェクト`NocturneModernGameplay`で開発しています。このリポジトリと配布物には含まれず、正式公開済みとは扱いません。

## ビルド

MelonLoaderとSMT3HDが生成したIl2Cppアセンブリが必要です。既定の`GameDir`はSteam標準配置を参照します。

```powershell
dotnet build .\NocturneModernController.csproj -c Release --no-restore
dotnet build .\helper\NocturneModernController.InputHelper.csproj -c Release --no-restore
dotnet build .\settings\NocturneModernController.Settings.csproj -c Release --no-restore
```

ゲームプレイMODは`mods/`以下のそれぞれのプロジェクトです。

主な出力先:

- Controller: `bin/Release/net6.0/`
- Input Helper: `helper/bin/Release/net6.0/`
- Settings GUI: `settings/bin/Release/net6.0-windows/`

`SDL3.dll`はHelperプロジェクトが生成するファイルではありません。`tools/ControllerSideRead/Fetch-Sdl.ps1`は公式SDL 3.4.14 x64 archiveを固定SHA-256で検証して調査用`native/SDL3.dll`を取得します。配布時はライセンス条件を確認し、Helperフォルダーへ同梱してください。

公開用ZIPは次のコマンドで生成できます。一時的なgit worktreeから再現可能な形でビルドし、`artifacts/release/`にController本体とゲームプレイMOD4つのZIPを出力します（バージョンは各プロジェクトの設定値）。

```powershell
.\tools\Build-Release.ps1
```

## 調査・開発記録

- [開発履歴](docs/DEVELOPMENT_HISTORY.md)（v2系までの記録を含む）
- [v3.0.0リリースノート](docs/releases/v3.0.0.md)
- [Feature Metadata完了報告](docs/FEATURE_METADATA_COMPLETION_REPORT.md)
- [右スティック・視点・Dash調査](docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md)
- [Controller入力レイヤー](docs/controller-input-layer.md)
- [標準旋回ルーティング](docs/vanilla-turn-routing.md)
- [第三者ソース調査ポリシー](docs/THIRD_PARTY_RESEARCH_POLICY.md)

## ライセンス

本リポジトリのコードは[LICENSE](LICENSE)に従います。SDLおよび第三者資料にはそれぞれのライセンスが適用されます。配布物には[THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES.txt)を同梱します。第三者MOD調査の扱いは[第三者ソース調査ポリシー](docs/THIRD_PARTY_RESEARCH_POLICY.md)を参照してください。

## 注意事項

- ゲーム本体、Atlus/Segaのアセット、生成されたゲームDLLは含みません。
- MODの利用は自己責任で行ってください。セーブデータのバックアップを推奨します。
- 本プロジェクトはAtlus、Sega、Valve、SDLプロジェクトとは無関係です。
