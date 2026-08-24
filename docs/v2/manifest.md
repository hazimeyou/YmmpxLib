# v2 manifest / Resource Metadata

## 目的

v2 manifestは、YMMPXが参照したリソースをDependency Recoveryで識別するための正本である。
ローカル検索と将来の外部Recoveryへ渡せる情報を保持するが、この段階ではYMMPの書換え、YMM4操作、
自動再リンク、外部ダウンロードを行わない。

## ファイル名とschema

v2のファイル名は`manifest.v2.json`である。v1は`links.json`と、旧互換として`manifest.json`を
解釈するため、同名を避けた。

`schemaVersion`はAssembly versionと独立したmanifest schema versionであり、現在は`1`である。
v2正式リリース前は変更可能だが、正式化後は互換性をテストで固定する。

## Resource Metadata

各entryは以下を持つ。

- `originalPath`: 任意。保存する場合だけ元の絶対パスを保持する。
- `fileName`: 元ファイル名。
- `length`: バイト長。
- `sha256`: 大文字16進64文字。`ResourceIdentity`と同じ検証規則を使用する。
- `packagePath`: 将来のパッケージ内相対パス。
- `kind`: `File`、`Image`、`Audio`、`Video`、`Psd`、`ImageSequence`、`Plugin`、`Unknown`。
- `groupId`: 任意。ImageSequenceの物理フレームを論理的に関連付ける。

entryから`ToResourceIdentity()`で`ResourceIdentity`を作成でき、直接
`LocalResourceSearch.FindMatchesAsync`へ渡せる。

## OriginalPathとPrivacy

`originalPath`は必須ではない。絶対パスにはユーザー名、フォルダ構成、プロジェクト名などが含まれ得るため、
パッケージ作成側が必要な場合だけ保存する。保存方式を選ぶPackaging Optionは、今回実装しない。

ファイル名、Length、SHA-256だけでもローカル候補検索は可能である。元パスを保存した場合は、
存在確認とSHA-256検証を経た候補として利用できる。

## ImageSequence

初期モデルでは各PNGを物理resource entryとして記録し、同じ`groupId`と`ImageSequence` kindで
論理的な連番グループを表す。resource graphを先行導入せず、復元UIが「連番1件」として扱うために必要な
関連だけを保持する。

## 検証と決定性

serializerはSystem.Text.Jsonを使用する。resourcesは`packagePath`のOrdinal順に整列して出力するため、
同じmanifestは同じJSONになる。

load時には以下を検証する。

- `schemaVersion`が1であること
- SHA-256形式とLengthが`ResourceIdentity`の規則に従うこと
- PackagePathが空でなく、絶対パス、UNC、`..`を含むtraversalでないこと
- PackagePathが重複しないこと
- 必須項目が欠落していないこと

不正JSONまたは検証失敗は`PackageManifestException`として報告する。

## v1および将来拡張

v1の`links.json`、package format、productionコードは変更しない。v2 manifestはCoreのみの責務であり、
YMM4型やReflectionに依存しない。

将来はmanifest resourceからローカル検索結果を提示し、その後に安全な再リンク候補選択を検討する。
Plugin Portal、GitHub Releases、素材配布ページのprovider情報・自動取得は、このschema versionでは
実装しない。
