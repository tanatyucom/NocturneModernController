# Nocturne Modern Controller

[English README](README_EN.md)

『真・女神転生III NOCTURNE HD REMASTER』（Steam版）の入力と探索操作を現代的にする、Windows向けMelonLoader MODです。右スティックカメラ、ダッシュ、探索支援、Smart Auto Battleに加え、場面別キー割当と外部MOD連携に対応した統合設定GUIを提供します。

現在の公開バージョンは **2.0.2** です。変更内容は[CHANGELOG](CHANGELOG.md)を参照してください。

## 主な機能

### 右スティックと汎用入力

- 右スティック左右によるダンジョンの標準左右旋回
- ゲーム内蔵経路を利用した右スティック上下カメラ
- `Full Camera`（左右＋上下）と`Horizontal Turn`（左右のみ）
- Invert X / Invert Y、X/Y感度、デッドゾーン設定
- ダンジョンのLB/RB旋回を抑止しつつ、BATTLEのRB Passを維持
- SDL3 Input Helperによる、特定VID/PIDに固定しないゲームパッド入力

右スティック上下はカメラ座標を独自に書き換えません。ゲーム内部の右スティック縦アナログ経路へ入力を渡すため、標準の補間、球面移動、上下限が利用されます。

### Controller内蔵機能

- **Dash**: FIELD/DUNGEONとワールドマップで移動速度を上げる。標準はLTまたはRT長押し、LT+RTでKeep切替
- **Quick Heal**: 探索中に、所持回復スキルと実際のMPを使って前衛・控えを回復。所持している場合だけ蘇生・状態異常回復を行う
- **Force Encounter（Xボタンで即戦闘）**: 通常エンカウント可能な場所でXを押すと、ゲーム本来の遭遇判定へ即時の戦闘開始要求を渡す
- **Smart Auto Battle**: ゲーム標準Auto経路を使い、弱点・耐性・反射・吸収・MP・後続メンバーの撃破予測を考慮して行動を選ぶ
- **統合設定GUI**: 右スティック設定、キー割当、機能ON/OFF、外部Provider表示を一画面で管理

Smart Auto Battleは実機で動作していますが、戦術判断はヒューリスティックであり、改善余地があります。

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

配布ZIPをゲームフォルダーへ展開し、次の構成にします。HelperとSettingsの出力一式、および公式SDL配布物の`SDL3.dll`が必要です。

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

PDBは開発用で、通常配布の実行には不要です。Controller DLLは自身と同じディレクトリを基準に、上記Helperフォルダー内のInput HelperとSettings GUIを参照します。

## 設定GUIの開き方

標準割当では、ゲーム中にSelectを約0.8秒長押しすると設定GUIが開きます。GUI表示中はMOD独自アクションを一時停止し、ゲームを最小化します。GUIを閉じると設定・割当・機能変更要求を再読込し、ゲームウィンドウを復元します。

設定は`Mods`内のController DLLと同じ場所にJSONとして保存されます。

## キー割当

- FIELD/DUNGEON、BATTLE、PUZZLE、MENUの場面別割当
- A/B/X/Y、LB/RB、LT/RT、L3/R3、Start/Select、方向キーに対応
- 最大3ボタンの同時押し
- `Press`、`Hold`、`LongPress`、`Toggle`、`DoublePress`のアクション定義
- R3を含むボタンを割当入力として扱う（ゲーム標準の「視点を正面に戻す」など、標準アクションの追加登録は今後の拡張方針）

既定割当はDashがLT/RT、Dash KeepがLT+RT、Quick HealがRB、Xボタンの即戦闘がForce Encounter、設定画面がSelect長押しです。コンテキスト分離により、FIELDの割当がBATTLEのRB Passなどを無条件に置き換えないようにしています。

## MOD機能タブ

設定GUIの「MOD機能」タブはFeatureメタデータからカードを動的生成します。Controller自身は次の5件をProviderとして公開します。

- Right Stick Camera
- Dash
- Quick Heal
- Force Encounter
- Smart Auto Battle

Feature 0件、Controller 5件、検証用20件の表示経路とスクロールを確認済みです。Provider単位のJSONが破損・未導入でも、GUI全体を失敗させないよう分離しています。詳細は[Feature Metadata完了報告](docs/FEATURE_METADATA_COMPLETION_REPORT.md)を参照してください。

## 外部Provider連携

別MODは`NocturneModern*.features.json`形式のスナップショットを公開することで、Controller DLLを必須参照せず統合GUIへ参加できます。GUIはON/OFFの変更要求をJSONへ保存し、各Providerが自分の設定経路で処理します。これは現時点のメタデータ連携であり、第三者向けの安定SDK、NuGet、互換性保証ではありません。

## 確認済み動作

- ダンジョンで右スティック左右旋回・上下標準カメラ
- ダンジョンでLB/RB旋回を抑止し、BATTLEでRB Passを維持
- 病院内とワールドマップでDash
- LT/RT長押し、LT+RTのDash Keep、速度調整
- Quick Healの所持スキル・MPに従う連続回復
- 通常エンカウント可能な場所で、Xボタンから即時の戦闘開始を要求するForce Encounter
- ゲーム標準Auto経路を利用するSmart Auto BattleとAuto中だけの速度変更・終了時復元
- 統合設定GUI、キー割当、Controller Feature 5件と外部Provider検出

## 未確認・実験中

- PUZZLEの右スティック回転ルートは実装済みですが、実プレイでの最終確認は未完了です。
- PS/Switch/一般パッドはSDL3対応方針ですが、全機種の実機互換性は未確認です。
- Smart Auto Battleは動作済みですが、複雑な敵編成や特殊スキルに対する判断改善が残っています。
- Feature Provider APIは開発中の連携面であり、安定した公開SDKではありません。

## ControllerとGameplayの関係

`NocturneModernController`は入力、設定GUI、既存Controller機能を担当します。ゲームプレイ規則を変更する機能は、別プロジェクト・別配布単位の`NocturneModernGameplay`で開発します。

GameplayはController DLLを必須参照せず単独動作し、Controllerがある場合だけJSONメタデータ経由で統合GUIへ参加します。現在Gameplay側では **Skill Mutation: Learn as New** を実験中です。空き枠への新規追加は実機成功済みですが、8枠満杯時に標準忘却画面を再利用する処理は調査・試験中であり、完成・正式公開済みとは扱いません。GameplayのソースやDLLはこのリポジトリに含まれません。

各MOD単位のZIPと、それらをまとめる任意のパックZIPを想定しています。

## ビルド

MelonLoaderとSMT3HDが生成したIl2Cppアセンブリが必要です。既定の`GameDir`はSteam標準配置を参照します。

```powershell
dotnet build .\NocturneModernController.csproj -c Release --no-restore
dotnet build .\helper\NocturneModernController.InputHelper.csproj -c Release --no-restore
dotnet build .\settings\NocturneModernController.Settings.csproj -c Release --no-restore
```

主な出力先:

- Controller: `bin/Release/net6.0/`
- Input Helper: `helper/bin/Release/net6.0/`
- Settings GUI: `settings/bin/Release/net6.0-windows/`

`SDL3.dll`はHelperプロジェクトが生成するファイルではありません。`tools/ControllerSideRead/Fetch-Sdl.ps1`は公式SDL 3.4.14 x64 archiveを固定SHA-256で検証して調査用`native/SDL3.dll`を取得します。配布時はライセンス条件を確認し、Helperフォルダーへ同梱してください。

公開用ZIPは次のコマンドで生成できます。出力先は`artifacts/release/NocturneModernController-v2.0.2.zip`です。

```powershell
.\tools\Build-Release.ps1 -Version 2.0.2
```

## 調査・開発記録

- [開発履歴](docs/DEVELOPMENT_HISTORY.md)
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
