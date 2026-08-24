# YMMPX v2 Compatibility

Recovery機能を追加する前に、現在のpackage互換性を自動テストで固定する。
Compatibility Suiteはformatの内部実装を模倣せず、productionのDetector、Reader、Resolver、Extractor、Writerを入口から出口まで通す。

## Supported

- Legacy v1 packageの検出、読込、FilePath復元、展開
- YMMPX Format 2.0 / manifest schema 1のWriter、Reader、共通Resolver、Extractor
- v1/v2 assemblyおよびplugin assemblyの同時build・identity分離

| Resource | Legacy v1 | Format 2.0 |
| --- | --- | --- |
| Project name | Yes | Yes |
| Image | Yes | Yes |
| Audio | Yes | Yes |
| Video | Yes | Yes |
| PSD | Yes | Yes |
| ImageSequence | Yes | Yes |
| Japanese path | Yes | Yes |

Legacy v1では`_ymmpx_project_path.txt`とroot `project.ymmp`を扱い、`links.json`、`manifest.json`、`links.txt`を互換Readerで正規化する。linkの優先順位と空の上位形式からのfallbackはLegacyV1Readerの既存仕様を維持する。

v2 packageはZIP内部で`project.ymmp`を使う。manifestのProject Metadataが持つ`OriginalFileName`を、ReaderとExtractorがユーザー向け出力名として維持する。metadataのない旧開発中v2 packageは内部entry名へフォールバックする。

## Compatibility boundaries

Resolverが置換するのは対応する`FilePath`だけである。`null`、FilePath以外の同一文字列、未知property、PSDの`EnableLayers`、`EnableLayerPaths`、`$type`は保持する。ImageSequenceは代表FilePathを復元し、全frameとgroupを展開する。ImageItemをsequenceとして扱わない。

Format 2.0 Writerの既定ではresource `OriginalPath`をmanifestへ保存しない。source PCのabsolute pathを通常packageのmetadataに含めない。`ExcludedResources`、`IncludeProjectUiSettings`、Progressを使って作成したpackageも同じReader/Resolver/Extractor経路を通る。

## Future versions

Format v3、v10などのfuture majorと未知のv2 minorはversionを認識するが、Reader routeは持たない。現在のCoreはそれらをReaderやExtractorへ渡さず、強制展開しない。壊れたdescriptorはInvalid、無関係ZIPはNotYmmpxとして区別する。

RecognizableとReadableは別である。将来、旧Readerを提供しなくなる場合でも、利用側が形式と更新案内を判断できるようFormat Detectionは残す。

## Non-goals

- v2 packageをv1 Readerで読むこと
- future formatを現在のReaderで強制展開すること
- YMM4 private API互換やPSD Timeline自動再リンク
- Dependency Recovery、LocalResourceSearch統合、ResourceCache、自動FilePath変更
- v1からv2へのmigration、複数project、v3 Reader/Writer

YMM4 GUI上のPSD path変更時再認識問題は、このCore compatibilityの対象外である。

## Test suite

`YmmpxLibV2.Tests/Compatibility/` は、以下の長期維持境界を明示する。

- `LegacyV1CompatibilityTests`: v1 link 3形式をproduction v2 CoreでE2E展開する。
- `V2CompatibilityTests`: production Writerから作ったFormat 2.0 packageとWriter Options packageをE2E展開する。
- `FormatCompatibilityTests`: future versionの認識・非routingとInvalid/NotYmmpxの区別を確認する。

個別のpath safety、cancellation、assembly identity、dependency boundaryの詳細は既存unit testと[Extraction Architecture](extraction-architecture.md)、[Format Versioning](format-versioning.md)、[Reflection Boundary](reflection-boundary.md)で維持する。
