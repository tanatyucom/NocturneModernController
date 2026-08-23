# GUI / Feature Metadata Integration Completion Report

Date: 2026-08-22

## Outcome

NocturneModernController の設定GUIを、MOD固有の機能名や説明文を固定で持たないメタデータ駆動構造へ拡張した。

NocturneModernController 自身はプロセス内 Provider として登録される。別MODは Controller DLLへの必須参照を持たず、`NocturneModern*.features.json` を公開することで同じGUIへ参加できる。

NocturneModernGameplay は単独動作可能な新規プロジェクトとして作成し、外部Providerスナップショットの公開経路を実装した。未実装Featureは公開しない。

## Metadata contract

Provider:

- `ProviderId`
- `ProviderName`
- `Version`
- `Features`
- `Error`

Feature:

- `Id`
- `Name`
- `Description`
- `Enabled`
- `Category`
- `SortOrder`
- `RequiresRestart`
- `ReadOnly`
- `Version`
- `Warning`
- `Notes`

Feature IDはProvider IDとの組み合わせで識別する。GUIは未知のProvider IDおよびFeature IDを固定リストと照合しない。

## Responsibility separation

- MOD側がFeatureメタデータと現在状態の正本を持つ。
- GUIはスナップショットの件数ぶんカードを動的生成する。
- GUIは変更要求だけをJSONへ保存する。
- ゲーム内Controllerは自身のProvider要求を内部Setterへ渡す。
- 外部Provider向け要求は削除せず、対象MODが自身の設定経路で処理する。
- Provider単位の破損・未導入はGUI全体の失敗にしない。

## Persistence

- Controller設定: `NocturneModernController.settings.json`
- Controller Provider統合スナップショット: `NocturneModernController.features.json`
- GUI変更要求: `NocturneModernController.feature-requests.json`
- Gameplay Providerスナップショット: `NocturneModernGameplay.features.json`

GUI起動直前に外部Providerを再走査する。これによりMelonLoaderのMODロード順に依存せず、後から公開されたProviderも現在のGUI起動へ反映される。

## Implemented Controller features

- Right Stick Camera
- Dash
- Quick Heal
- Force Encounter
- Smart Auto Battle

## Verification

- Controller DLL build: 0 warnings / 0 errors
- Settings GUI build: 0 warnings / 0 errors
- Gameplay DLL build: 0 warnings / 0 errors
- Controller Provider 5件の動的表示: 実機確認済み
- Dash OFF -> 保存 -> 再読込 -> 実動作停止: 実機確認済み
- Gameplay外部Provider検出: 実機確認済み
- Feature 0件表示: 実機確認済み
- Feature 20件表示: 目視確認済み
- 長文descriptionのカード内表示: 目視確認済み
- マウスホイール／スクロールバーでFeature 20まで到達: 目視確認済み
- 未導入Providerを前提としない起動: 確認済み

## Remaining work

- NocturneModernGameplayの実機能はまだ0件。完成した機能だけをRegistryへ登録する。
- Gameplay実機能追加後、外部ProviderのON/OFF要求、保存、再起動、再取得の往復を実機確認する。
- 2件・10件は20件と同じ動的生成経路だが、個別の目視テストは未実施。
- 検索、カテゴリ絞り込み、ONのみ表示は今回のスコープ外。
- 第三者向け安定SDK、NuGet、互換性保証は提供しない。

## Main changed files

- `src/ModernControllerApi.cs`
- `src/BuiltInFeatureProvider.cs`
- `src/ControllerSettings.cs`
- `src/SettingsGuiController.cs`
- `settings/Program.cs`
- `tools/gui-fixtures/features-20.json`
- external `NocturneModernGameplay` repository: `GameplayFeatureRegistry.cs`
- external `NocturneModernGameplay` repository: `GuiMetadataBridge.cs`

## 2026-08-23 addendum

上記「Gameplayの実機能はまだ0件」は、2026-08-22の完了報告時点の記録として維持する。

2026-08-23、外部`NocturneModernGameplay`プロジェクトに最初の実験Feature **Skill Mutation: Learn as New** が登録された。スキル変化時に元スキルを残し、変化後スキルを新規習得として扱う機能である。空き枠への追加は実機成功済み。8枠満杯時にゲーム標準の忘却選択画面を再利用する経路は、複数仲魔の結果処理、表示対象、メッセージ、終了遷移を含めて調査・試験中である。

したがってGameplay Feature数は0件ではなくなったが、このFeatureを完成・安定・正式公開済みとは記載しない。Gameplayは引き続き別リポジトリ、別DLL、別配布単位であり、本Controllerリポジトリにはそのソースやバイナリを含めない。
