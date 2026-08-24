# v2 Project Reference Resolution

## 目的と境界

v1 YMMPX は project 内の `FilePath` を package-relative path にし、links 定義で展開後の resource と対応付ける。v2 では次の順序で扱う。

```text
FormatDetector -> LegacyV1Reader -> LoadedYmmpxPackage
                                      |
                                      v
                 ProjectResourceReferenceMapper
                                      |
                                      v
                    YmmpxProjectReferenceResolver
                                      |
                                      v
                       YmmpxPackageExtractor -> disk
```

`LegacyV1Reader` は links 形式を解釈して `LegacyResourceLink` へ正規化する。`ProjectResourceReferenceMapper` はそこから形式非依存の `ProjectResourceReference` を作る。Resolver はその Mapping だけを受け取るため、`links.json`、`manifest.json`、`links.txt` の構文を知らない。

Extractor は project と resource を書き出すだけであり、LegacyV1 用の分岐を持たない。準備済みprojectは `YmmpxExtractionOptions.ProjectOverride` で明示的に渡す。元の `LoadedYmmpxProject` は変更しない。

## v1 互換規則

v1 production の展開処理と同じく、link 定義の優先順位は `links.json`、`manifest.json`、`links.txt` である。LegacyV1Reader がこの優先順位を適用し、Resolver に渡る時点では一つの Mapping になっている。

project JSON は再帰的に走査し、名前が `FilePath`（大小文字を区別しない）の**文字列値**だけを置換する。区切り文字差を正規化し、Windowsでは大文字小文字を区別しない完全一致で対応付ける。filenameだけでは対応付けない。

- `FilePath: null`、非文字列値、未対応の FilePath は変更しない。
- 同じresourceを複数回参照していればすべて置換する。
- 同名でも OriginalReference / PackagePath が異なれば混同しない。
- linkだけがありprojectで参照されないresourceは通常どおり展開する。
- 同一OriginalReferenceが異なるPackagePathを指す曖昧Mappingは安全に失敗する。

## 出力pathとJSON保持

Resolverは共有の `PackageDestinationPathResolver` を使い、PackagePathからdestination root配下の絶対pathを計算する。path traversal、絶対path、drive/UNC path、destination外への出力は拒否する。Extractorも同じhelperを使う。

JSONは `JsonNode` で未知propertyを保持して更新する。JSONの整形・エスケープ表現は変わり得るが、意味的な変更は対応する `FilePath` だけに限定する。`$type`、`EnableLayers`、`EnableLayerPaths`、Character・Timeline等の既存状態を再構築しない。

## PSDとImageSequence

PSDは通常のFilePathとして展開先へ復元するだけであり、PSD layer設定には触れない。YMM4がpath変更時にPSD立ち絵を再認識して設定を失う既知の問題は、YMM4側の別課題でありこのResolverでは回避しない。

ImageSequenceは全frameをExtractorが出力し、ResolverはVideoItemの代表PNGの`FilePath`だけを展開先のsequence directoryへ向ける。projectへ他frameの参照を追加しない。

## 将来の利用

V2Readerはmanifestから同じ`ProjectResourceReference`を作り、同じResolverとExtractorを利用できる。package内resourceがない場合のLocalResourceSearch/Recovery Candidateも、将来は同じMappingの解決元を差し替えて接続する。今回それらは実装しない。
