# YMMPX Format 2.0

Format 2.0 packageは次のentryを持つ。

```text
_ymmpx.json
manifest.v2.json
project.ymmp
resources/...
```

`_ymmpx.json` は既存の `YmmpxFormatDescriptorSerializer` により生成され、`format: ymmpx`、Format 2.0、`manifest.v2.json` を示す。manifest schema versionは1であり、library versionとは独立している。

`manifest.v2.json` は各resourceのPackagePath、Length、SHA-256、kind、必要ならgroupIdを保持する。Writerの既定ではOriginalPathを保存しないため、元PCの絶対pathはpackageの必須情報ではない。

package内projectの対象`FilePath`はPackagePathへ置換される。展開時はmanifest resourceのPackagePathを`ProjectResourceReference`へ変換し、共通Resolverが展開先の絶対pathへ復元する。

同名resourceは`resources/name.ext`、`resources/name_2.ext`のように決定的なsuffixで区別する。同じsource pathの複数参照は1 resourceへまとめる。VideoItemのPNG連番は`resources/sequence_N/`へ各frameを格納し、各entryは同じgroupIdを持つ。ImageItemは連番化しない。

manifestはresource一覧の正本である。ZIPに存在してもmanifestにないresourceは展開対象にしない。manifest記載resourceがZIPにない、長さが異なる、unsafe path、critical entryの重複はReaderが安全に拒否する。future majorおよび未知minorは既存Detectorにより読まずに停止する。
