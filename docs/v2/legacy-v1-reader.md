# LegacyV1Reader / 既存YMMPX読み込み互換

## 目的

`LegacyV1Reader`は、v1形式の`.ymmpx`をv2 Coreの共通package表現へ読み込む互換Readerである。
`YmmpxLibV2`はv1 assemblyを参照せず、v1 WriterとExtractorのpackage仕様だけを実装する。
これはv1 programming APIの互換層ではない。

Readerは`YmmpxFormatDetector`が`LegacyV1`と判定した入力だけを読む。v2 packageや無関係ZIPを
v1として推測して読まない。

## v1仕様

現在のv1 Writerは、project `.ymmp`、`_ymmpx_project_path.txt`、`links.json`、`resources/`を
生成する。project markerは相対`.ymmp` pathを示す。markerがない旧構造ではルートの`project.ymmp`を
使用する。

link定義はv1 Extractorと同じ順で解釈する。

1. `links.json`: `originalReference -> packagePath` dictionary
2. `manifest.json`: `Files[]`内の`OriginalPath`と`BundlePath`
3. `links.txt`: `source,bundlePath`。source内のカンマに対応するため最後の有効なカンマで分割

上位形式が空なら下位形式へフォールバックする。個別のunsafeまたは存在しないlink先はv1 Extractorと
同様に無視する。link metadataが壊れていて、利用可能な下位形式もない場合は`InvalidLinks`で停止する。

## 共通Package表現

- `LoadedYmmpxPackage`: source format、project、resources、links
- `LoadedYmmpxProject`: package内pathと変更しないproject text
- `LoadedYmmpxResource`: package path、ファイル名、Length、kind、sequence group
- `LegacyResourceLink`: 元参照とpackage内resource path

`SourceFormat`は`LegacyV1`であり、将来のV2Readerも同じ共通表現を返す予定である。
Readerはresource内容を`byte[]`として保持せず、resourceを展開・書出し・FilePath変更もしない。
入力Streamの所有権は呼び出し側にあり、Readerはdisposeしない。

## ResourceとImageSequence

`resources/`配下とlink先entryからresource metadataを作る。拡張子からImage、Audio、Video、PSD等の
大まかなkindを付与する。`resources/sequence_N/`配下のentryは`ImageSequence`とし、`sequence_N`を
groupIdとして保持する。各物理frameを保持するため、桁跨ぎや複数sequence、同名別directoryを失わない。

## 安全性

- 読み取り専用。ZIP展開、temporary directory、disk書込みを行わない。
- archive entryはWindows互換のcase-insensitive pathでindex化し、重複を拒否する。
- entry、project marker、link先は共通`PackagePathValidator`で検証し、絶対path、UNC、`..`、colonを拒否する。
- project: 512 MiB、link metadata: 64 MiB、marker: 16 KiBの上限を設ける。
- 大容量resourceはLengthだけを取得し、内容を読み込まない。
- CancellationTokenを受け取り、キャンセル時は`OperationCanceledException`を返す。

`LegacyV1`として認識できても、安全に読めないvariantは`LegacyV1ReadException`の構造化reasonで停止できる。
RecognizableとReadableは同一ではない。

## 非対象

LegacyV1Extractor、v2 Writer / Reader / Extractor、v1からv2 manifestへの変換、全resource SHA-256計算、
Recovery Candidate、自動再リンク、YMM4操作、Plugin、外部素材RecoveryはこのReaderの責務外である。
