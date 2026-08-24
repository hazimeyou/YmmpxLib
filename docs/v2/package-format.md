# YMMPX Format 2.0

Format 2.0 packageは次のentryを持つ。

```text
_ymmpx.json
manifest.v2.json
project.ymmp
resources/...
```

`_ymmpx.json` は既存の `YmmpxFormatDescriptorSerializer` により生成され、`format: ymmpx`、Format 2.0、`manifest.v2.json` を示す。manifest schema versionは1であり、library versionとは独立している。

`manifest.v2.json` は各resourceのPackagePath、Length、SHA-256、kind、必要ならgroupIdを保持する。さらに現在1件のProject Metadataとして、ZIP内の`PackagePath`とユーザー向けの`OriginalFileName`を保持できる。Writerの既定ではOriginalPathや元projectの絶対pathを保存しないため、元PCの絶対pathはpackageの必須情報ではない。

Package内部の`project.ymmp`と、展開時のproject名は別の概念である。たとえば`PackagePath`が`project.ymmp`でも、`OriginalFileName`が`同人誌ラクスルテンプレ.ymmp`なら、Extractorは後者をdestination root直下へ出力する。filenameはpath separator、rooted path、`..`、空値、`.ymmp`以外を許可しない。metadataを持たない既存の開発中Format 2.0 packageは、内部project entry名を出力名としてフォールバックする。

Project Metadataは現在単一projectを表す最小構成であり、resource metadataとは独立している。複数projectのpackageは今回非対応だが、将来はproject metadataを複数の`PackagePath`/`OriginalFileName`組へ拡張できる。`_ymmpx.json`は形式識別専用のStable Descriptorであるため、このmetadataを置かない。任意の`project`プロパティ追加は既存schema 1 readerとの互換性を保つため、YMMPX Format 2.0およびmanifest schema 1は維持する。

package内projectの対象`FilePath`はPackagePathへ置換される。展開時はmanifest resourceのPackagePathを`ProjectResourceReference`へ変換し、共通Resolverが展開先の絶対pathへ復元する。

同名resourceは`resources/name.ext`、`resources/name_2.ext`のように決定的なsuffixで区別する。同じsource pathの複数参照は1 resourceへまとめる。VideoItemのPNG連番は`resources/sequence_N/`へ各frameを格納し、各entryは同じgroupIdを持つ。ImageItemは連番化しない。

manifestはresource一覧の正本である。ZIPに存在してもmanifestにないresourceは展開対象にしない。manifest記載resourceがZIPにない、長さが異なる、unsafe path、critical entryの重複はReaderが安全に拒否する。future majorおよび未知minorは既存Detectorにより読まずに停止する。
