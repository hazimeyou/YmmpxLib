# Ymmpx v2 Reflection 利用境界

## Context

YmmpxLib を利用する ConsumerPlugin は、YmmpxLib パッケージが未導入の環境でも、YMM4 全体を停止させずに利用者へ導入を案内できる必要があります。これは `ConsumerPlugin -> YmmpxLib` を optional dependency として扱うための要件です。

YMM4 v4.55.1.1 Lite、.NET 10.0.10 で実測したところ、YmmpxLib を通常参照する最小 ConsumerPlugin は、YmmpxLib.dll が未配置でも YMM4 起動時にはロードできました。一方、YmmpxLib API を呼び出すと `System.IO.FileNotFoundException` が発生しました。依存Assemblyの解決は Consumer のメソッド本体に置いた `try/catch` より前に発生し、catch 節には到達しませんでした。

したがって、通常参照と `try/catch` だけでは、未導入時の安全な案内を保証できません。ただし、API呼び出し全体をReflectionに置き換えると、型安全性、可読性、変更追跡性、保守性を損ないます。

この文書は上記の調査結果を前提とする v2 の設計方針です。調査の詳細は将来の参照用に `investigate/v2-optional-consumer-dependency` ブランチの `docs/investigations/v2-optional-consumer-dependency.md` に記録されています。

## Decision

v2 では、Reflection を **ConsumerPlugin 側での YmmpxLib 存在確認だけ** に許可します。それ以外の用途でReflectionを使用してはいけません。

存在確認後の実処理は、Reflection経由ではなく、型安全な通常APIまたは将来定義する明示的なBridge契約で行います。存在確認が成功しても、ReflectionでYmmpxLib APIを呼び出してはいけません。

## Allowed

ConsumerPlugin の互換境界では、次の最小限のReflectionだけを許可します。

- YmmpxLib Assembly が導入済みかを確認する。
- 確認対象のAssembly名、必要な互換性情報、確認失敗理由を取得する。
- 未導入時に、YmmpxLib APIへ触れず導入案内へ分岐する。

この境界は、存在確認だけを責務とする小さな層に閉じ込めます。実装時は、検出対象、結果、例外、案内へ分岐した理由を追跡可能なログに残します。

## Forbidden

以下でReflectionを使用することは禁止します。

- YmmpxLib API の呼び出し、public API の動的探索、`MethodInfo.Invoke` による実行。
- `PropertyInfo` / `FieldInfo` によるデータ読書き。
- PSD処理、Timeline操作、Character操作、FilePath処理、YMMP解析、YMMPX作成・展開、links処理、Resource収集、素材復元。
- Bridge呼び出し、YmmpxLibPluginとの通信、YMM4 API操作、Plugin Portal連携、Plugin自動取得。

この禁止はReflection自体を悪とみなすものではありません。存在確認というoptional dependency固有の問題を除き、通常の処理にはコンパイル時に追跡できる型安全な契約を使うための境界です。

## Dependency Direction

基本の依存方向は次のとおりです。

```text
ConsumerPlugin ───────────→ YmmpxLib

YmmpxLibPlugin ───────────→ YmmpxLib
YmmpxLibPlugin ───────────→ YMM4

YmmpxLib ──X──→ YmmpxLibPlugin
YmmpxLib ──X──→ YMM4
```

ConsumerPlugin が YmmpxLib 未導入を扱う入口だけは、YmmpxLibへの静的API利用より先に存在確認を行うためのReflection境界です。

YmmpxLibPlugin と YmmpxLib は同一のYMM4プラグインパッケージとして配布します。そのため、`YmmpxLibPlugin` だけが存在し `YmmpxLib.dll` だけがない壊れた同一パッケージ内欠損は、通常運用上の対応対象にしません。この方針と ConsumerPlugin のoptional dependency問題を混同してはいけません。

## Failure Behavior

YmmpxLib が未導入の場合、ConsumerPlugin は次の順序で動作します。

1. Reflection境界で YmmpxLib の不在を確認する。
2. YmmpxLib API を呼び出さない。
3. 利用者へYmmpxLibパッケージの導入を案内する。
4. ConsumerPluginは利用不能な機能を明確にし、YMM4自体は継続利用可能な状態にする。

存在確認を `try/catch` によるAPI呼び出しの失敗処理へ置き換えてはいけません。実測では、依存解決がConsumerメソッド内のcatchより前に発生したためです。

## Rationale

Reflectionを完全禁止にすると、YmmpxLibを未導入のConsumerPluginが、YmmpxLib APIに触れる前に安全に不在を判定する手段を失います。そのため、存在確認だけは明示的な例外として許可します。

反対にReflectionをAPI呼び出し全体へ広げると、メソッド名・引数・戻り値の不整合が実行時まで分からず、API変更の追跡と原因調査が難しくなります。v2では実処理の境界を通常APIまたは明示Bridge契約として保ち、Reflectionの影響をoptional dependency確認だけへ局所化します。

## Consequences

メリット:

- YmmpxLib未導入時にもYMM4起動継続を優先できる。
- ConsumerPluginが導入案内へ安全に分岐できる。
- 実処理は型安全な契約として保守できる。
- Reflectionの影響範囲が小さく、ログとレビューで追跡しやすい。

デメリット:

- ConsumerPluginには存在確認専用の互換境界が必要になる。
- YMM4のロード挙動が変わった場合は、YMM4の対象バージョンで再検証が必要になる。
- Reflectionを完全には排除できない。

## Future Work

次の項目は候補であり、この文書では実装しません。

- Consumer向け存在確認helper。
- Core / Plugin Bridge。
- PSD Timeline連携。
- v2 Dependency Recovery。

Bridge、Contracts、Adapterなどの方式は、Consumerが未導入時にも安全にロードできることを実測してから別課題として判断します。この文書はそれらの実装方式を先取りして固定するものではありません。

## Review Checklist

- Reflectionは存在確認のみに限定されている。
- APIの動的呼び出しは禁止されている。
- ConsumerPlugin と YmmpxLibPlugin の責務を混同していない。
- YmmpxLib未導入時にYMM4を停止させない挙動を定義している。
- 通常参照と `try/catch` が不十分だった実測理由を記録している。
- 判断はYMM4 v4.55.1.1 Liteでの実測に基づき、将来のYMM4変更時には再検証する。
- Bridgeは将来課題であり、この文書で実装を決めていない。
- v1.0.1のコード・仕様には影響しない。
