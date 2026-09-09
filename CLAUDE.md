# Claude Code作業指示

このファイルは、Claude Code / Claudchordが`NocturneModernController`リポジトリで作業するときの応答方針を定める。リポジトリ内の既存ルールと個別のユーザー指示を尊重し、この言語ルールを理由に既存の開発方針を弱体化または上書きしないこと。

## 言語ルール

- 原則として、ユーザー向けの説明は日本語で行う。
- 見出し、番号付き項目、結論、警告、質問、選択肢、TODO、作業報告、要約も日本語を優先する。
- 本文だけを日本語にして、見出しや番号付き項目の内容を英語のまま残さない。
- Claude自身が生成する`Summary`、`Findings`、`Next steps`、`Recommendation`、`Issue`、`Plan`などの一般的な見出しは、原則として「要約」「調査結果」「次の手順」「推奨」「問題」「計画」などの日本語にする。
- 英語の思考過程や内部説明を、ユーザー向け回答として大量に表示しない。
- ユーザーが明示的に英語を要求した場合のみ、ユーザー向け説明に英語を使用する。

## 技術識別子

以下は正確性を優先し、原文を維持する。技術識別子そのものを翻訳・改名せず、必要に応じて周囲または直後へ日本語の説明を付ける。

- code
- class、method、field、namespace、symbol
- path、ファイル名
- command、Git command
- VA、RVA、address
- exact error message
- raw log
- API名、型名
- 日本語化すると意味が不明瞭になる技術用語

悪い例:

1. Analyze the native call path.
2. Check the state transition.

良い例:

1. native呼び出し経路を解析する。
2. state遷移を確認する。

## shell・tool出力

- shell、compiler、Git、external toolが直接出した生出力は、改変や翻訳をせず原文のまま提示してよい。
- 生出力とClaude自身の説明を明確に区別する。
- 生出力の後に結果を説明する場合、その説明は日本語にする。
- build errorやwarningを引用する場合は原文を維持し、その意味や対応方針を日本語で説明する。

## Evidence表現

native reverse engineeringや実機調査では、既存のEvidence disciplineを尊重する。以下のEvidence classは、意味を固定するため英語表記のまま使用してよい。

- `CONFIRMED`
- `STRONGLY SUPPORTED`
- `HYPOTHESIS`
- `REJECTED`
- `UNRESOLVED`

Evidence classに続く根拠や説明は日本語にする。

例:

`CONFIRMED`:
`ExternalInputBridge`の初期化経路を2箇所確認した。

`UNRESOLVED`:
2経路が同一stateを共有するかは未確認。

## 作業報告

調査や実装の完了報告も日本語で記述する。必要な項目だけを使用し、英語のテンプレート見出しを機械的に並べない。

推奨形式:

```text
変更内容:

- ...

確認結果:

- ...

Evidence:

- ...

未解決:

- ...

次の候補:

- ...

Git writes:
NONE
```

## 質問と確認

- ユーザーへ確認、選択、承認を求める文章も日本語で記述する。
- 選択肢の見出しや説明も、技術識別子を除いて可能な限り日本語にする。

悪い例:

`Would you like me to continue with option 1 or option 2?`

良い例:

```text
次はどちらで進めますか？

1. 既存経路を詳しく調査する。
2. 最小構成のPoCを作成する。
```

## NocturneModernController固有の方針

リポジトリ内に既存の開発ルールや個別指示がある場合、それらを優先して尊重する。特に、以下の方針を日本語化ルールによって変更、削除、弱体化しない。

- controller QoL / inputを中心とするprojectであること。
- gameplay logicを別projectへ分離すること。
- keyboardがout of scopeと定められている作業では、その境界を維持すること。
- native reverse engineeringではEvidence disciplineを守り、推測と確認済み事実を区別すること。
- Git write policyを守り、許可されていない`git add`、`git commit`、`git push`などを行わないこと。
- build / deploy policyを守り、必要な検証や配置手順を省略しないこと。
- 最終的な動作判定は、ユーザーによる実機確認を重視すること。

## サンプル応答

```text
調査結果:

1. `Foo()`の呼び出し元を2箇所確認しました。
2. 1箇所目はfield処理、2箇所目はcamera処理です。
3. 両者が同一stateを共有するかはUNRESOLVEDです。

次の調査:
`barState` writerを追跡します。
```
