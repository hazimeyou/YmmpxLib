# YMMPX Format Detection / Version Routing

## 目的

v2は既存v1 `.ymmpx` を認識し、v2 packageを明確に識別する。将来の未知形式は展開せず、
利用側が必要な更新を案内できる構造化結果を返す。

検出とReaderは分離する。形式を認識できることは、現在のライブラリが実際に読めることを意味しない。

## Stable Format Descriptor

descriptor entry名は`_ymmpx.json`である。v1の`_ymmpx_project_path.txt`とは別名で、v1の
`manifest.json`やv2 resource manifestとも衝突しない。

```json
{
  "format": "ymmpx",
  "majorVersion": 2,
  "minorVersion": 0,
  "manifest": "manifest.v2.json"
}
```

descriptorにはresource一覧を格納しない。resource metadataは`manifest.v2.json`側の責務である。

## 3つのversion

- Library Version: `YmmpxLibV2`自体のリリースversion。
- YMMPX Format Version: package構造の`majorVersion.minorVersion`。現在は`2.0`。
- Manifest Schema Version: `manifest.v2.json`内部の`schemaVersion`。現在は`1`。

これらは独立している。Library Versionが上がっても、package formatやmanifest schemaを必ず変更する
必要はない。

## v1の検出条件

v1にはstable descriptorがないため、descriptorが存在しないだけでv1とは判定しない。以下のどちらかを
満たす場合だけ`LegacyV1`とする。

1. `links.json`、`manifest.json`、`links.txt`のいずれかと、`_ymmpx_project_path.txt`があり、markerが
   指す相対`.ymmp` entryが存在する。
2. 上記のlink定義とルートの`project.ymmp`が存在する旧構造。

現在のv1 Writerは`.ymmp`、`_ymmpx_project_path.txt`、`links.json`を作成する。無関係ZIPや
link定義だけの破損したv1風ZIPは`NotYmmpx`として安全停止する。

## Version Routing

- descriptorなし・v1条件成立: `LegacyV1`、将来の`LegacyV1Reader`へroute。
- descriptorのformat 2.0: `SupportedV2`、将来の`V2Reader`へroute。
- majorが2より大きい: `UnsupportedFutureVersion`。versionを返すがreader routeは持たない。
- majorが2より小さいdescriptor形式: `UnsupportedMajorVersion`。
- major=2かつminorが0より大きい: `UnsupportedMinorVersion`。
- 壊れたZIP: `InvalidArchive`。
- 壊れた、安全でない、またはmanifest entry不在のdescriptor: `InvalidDescriptor`。

Unsupportedは有効なYMMPX形式だが、このライブラリが読めない状態であり、Invalidとは区別する。
未知major/minorではmanifestを推測して読まず、ZIPを展開せず、projectやresourcesを書き出さない。

## 安全性

`YmmpxFormatDetector`はZIP entryを展開しない。`_ymmpx.json`とv1 markerだけを最大16 KiBまで読取り、
descriptorのmanifest pathには絶対パス、UNC、`..`を含むpath traversalを許可しない。CancellationTokenを
受け取る。YMM4型、Reflection、Plugin仕様、外部素材Recoveryには依存しない。

## 互換性

v2はv1 packageを検出し、将来のv1 Readerへrouteできるようにする。v2 packageをv1が読むことは要求しない。
将来旧Readerを削除する場合でも、Detectorは`LegacyV1`として認識を残せるため、利用側は適切な案内を表示できる。

Pluginの同梱・管理、Plugin Portal、ニコニ・コモンズ等の外部素材はこのformat detectionの責務外である。
