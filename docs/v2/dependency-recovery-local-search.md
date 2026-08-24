# v2 Dependency Recovery: ローカル再リンク候補検索

## 目的

Dependency Recovery の最初の段階として、失われたリソースと同一内容のローカルファイルを
安全に候補として検出する。対象は一般ファイルであり、PSD、画像、音声、動画などのYMM4固有処理は
含まない。

この段階では YMMP の書換え、FilePath の変更、YMM4 Timeline 操作、UI表示、自動選択を行わない。

## Core API

- `ResourceIdentity`: 元パス、ファイル名、長さ、SHA-256を表す値。
- `ResourceIdentity.CreateAsync`: ストリーム経由でSHA-256を計算する。
- `LocalResourceSearch.FindMatchesAsync`: 呼び出し側が明示した検索ルートと、存在する場合の元パスを
  読み取り専用で検索する。
- `ResourceSearchResult`: 一致候補、検索済みルート、読み取り時のissueを返す。

SHA-256は64文字の大文字16進表現に正規化する。ファイル名やLengthは候補削減に利用できるが、
一致の最終判定は必ずSHA-256で行う。

## 検索範囲と安全性

検索ルートは必ず呼び出し側が指定する。PC全体、全ドライブ、ユーザープロファイル全体を自動探索
しない。これは大量I/O、アクセス拒否、ネットワークドライブ、プライバシー上の問題を避けるためである。

検索は指定ルートを再帰するが、Windowsのreparse point（symlink、junction等）は辿らない。
アクセス不能なディレクトリまたはファイルは`ResourceSearchIssue`へ記録し、別の検索ルートや
サブディレクトリの検索は継続する。CancellationTokenによる中止は .NET 標準どおり
`OperationCanceledException`で通知する。

検索処理はファイルを開いてSHA-256を読むだけで、変更、削除、移動、名前変更、ACL変更をしない。

## 結果の扱い

- `NotFound`: 一致ファイルなし。例外ではない。
- `SingleMatch`: 一致候補が1件。呼び出し側が採用可否を決める。
- `MultipleMatches`: 一致候補が複数件。最初の候補を勝手に選ばず、全候補を返す。
- `PartialFailure`: 一部の場所で問題があったが、他の場所は検索できた。`MatchKind`で一致数を併せて確認する。
- `Failed`: 指定された場所を1つも検索できなかった。

元パスが存在し、LengthとSHA-256が一致する場合も、通常の候補として最優先で結果へ含める。

## 非対象と今後

この実装はYMM4 Pluginに依存せず、Reflectionも使用しない。ローカル検索の結果を使った安全な
再リンク候補選択、manifest v2のResource Metadata、Plugin Portal、GitHub Releases、素材配布ページは
別課題とする。外部取得や自動再リンクは、この段階では実装しない。
