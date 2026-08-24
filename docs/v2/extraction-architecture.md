# v2 共通 Extraction 境界

## 目的

YMMPX の世代ごとに Extractor を増やさず、各 Reader が共通の
`LoadedYmmpxPackage` と Resource Content Access を提供し、
`YmmpxPackageExtractor` が project と resource を展開する構造にする。

```text
LegacyV1Reader / V2Reader / 将来の Reader
                 ↓
        YmmpxPackageSession
                 ↓
          LoadedYmmpxPackage
                 ↓
      YmmpxPackageExtractor
                 ↓
          destination directory
```

`LegacyV1Reader` だけが v1 の links、project marker、ZIP 内構造を解釈する。
展開処理は format 非依存なので、`LegacyV1Extractor` は作らない。
将来の V2Reader も同じ package model と content provider を返す。

## Reader と Session

Reader は archive を変更せず、project、resource metadata、link 情報を読み取る。
resource 本体を `byte[]` として package model に保持しない。

`LegacyV1Reader.OpenAsync` は `YmmpxPackageSession` を返す。session は
`IYmmpxResourceContentProvider` として `OpenResourceReadAsync` を提供し、
resource の ZIP entry を stream として開く。Reader 固有の `ZipArchive` や
`ZipArchiveEntry` は public model に露出しない。

- 入力 stream は呼び出し側所有であり、session を dispose しても閉じない。
- session は ZIP archive を所有する。返された resource stream を先に dispose し、
  その後 session を dispose する。
- session dispose 後の resource access は `ObjectDisposedException` で失敗する。
- 同一 session の同時 resource read は今回保証しない。Extractor は逐次的に読む。

従来の `LegacyV1Reader.ReadAsync` は metadata だけが必要な既存利用のため残し、
内部で session を閉じて `LoadedYmmpxPackage` を返す。

## Extractor

`YmmpxPackageExtractor.ExtractAsync` は `IYmmpxResourceContentProvider` を受け取り、
logical project path と resource package path に従って出力先へ書き出す。
project text は変更せず、resource は stream から `CopyToAsync` でコピーする。
そのため動画、音声、PSD、連番 PNG を全量メモリへ読み込まない。

Extractor は Source Format を判定せず、`LegacyV1Reader` の concrete type にも依存しない。
Reader は format 固有の差を common model へ正規化してから渡す。

### 出力安全性

- package path は共有の `PackagePathValidator` で検証する。
- 出力 full path が destination root 配下にあることを Extractor でも再確認する。
- path traversal、絶対 path、drive/UNC path は拒否する。
- resource は `PackagePath` の ordinal 順で展開する。
- default の overwrite policy は `FailIfExists`。明示した `Overwrite` のみ既存ファイルを置換する。
- 各ファイルは同一 directory の temporary file に書き切ってから move するため、
  1 ファイル単位で中途半端な出力を残さない。

package 全体の transaction / rollback は今回の対象外である。途中失敗または
キャンセル時は例外で停止し、すでに正常に展開されたファイルは残る可能性がある。
キャンセルは `OperationCanceledException` として扱う。

## 今回の境界

Extractor は package 内 content を安全に disk へ出す。LegacyV1 の links に基づく YMMP 内 `FilePath` 復元は、Extractorの外側にある `YmmpxProjectReferenceResolver` が行う。Resolverが作ったimmutable project copyを `YmmpxExtractionOptions.ProjectOverride` として明示的に渡せるため、Extractor自身はformat-specific linkを解釈しない。詳細は [Project Reference Resolution](project-reference-resolution.md) を参照する。

YMM4 型、Reflection、Plugin Portal、外部素材 Recovery はこの層へ持ち込まない。

## 関連文書

- [LegacyV1Reader](legacy-v1-reader.md)
- [Format Versioning](format-versioning.md)
