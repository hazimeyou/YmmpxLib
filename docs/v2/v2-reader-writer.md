# v2 Reader / Writer

`YmmpxV2Writer` はsource projectのcopyだけを変更してFormat 2.0 ZIPを作る。resourceのSHA-256はstreamで計算し、ZIPへのコピーもstreamで行う。source projectとsource resourceは変更しない。source projectのファイル名はmanifestのProject Metadataへ`OriginalFileName`として記録するが、絶対pathは記録しない。ZIP内の論理pathは引き続き`project.ymmp`である。出力はtemporary fileを完成させてからmoveするため、失敗時に最終packageを残さない。

`YmmpxV2Reader` は`YmmpxFormatDetector`がSupportedV2と判定したpackageだけを開き、descriptor、manifest、project、resource metadataをcommon `LoadedYmmpxPackage` と`YmmpxPackageSession`へ変換する。resource本体は`byte[]`保持せず、sessionのstream accessで読む。入力streamの所有権は呼び出し側に残る。

Resolverは`ProjectResourceReference`を受けてFilePathだけを展開先に復元し、Extractorは準備済みprojectとresource streamsをdiskへ出力する。`LoadedYmmpxProject.PackagePath`は内部論理path、`OriginalFileName`は出力filenameであり、Resolverは後者を保持する。v1/v2とも同じResolver／Extractorを使い、format分岐を持ち込まない。metadataなしの旧v2 packageでは`project.ymmp`を出力名に使う。

CancellationはWriter、Reader、Resolver、Extractorの公開async処理で尊重する。Extractorのdefault overwrite policyは`FailIfExists`である。package全体のrollbackは対象外で、展開途中に失敗した場合に完了済みファイルは残り得る。

Readerはmanifest hashの形式を検証するが、open時にresource全体のhash再計算は行わない。Round-trip testsではsource、manifest、展開resourceの一致を検証する。Reflection、YMM4 Core依存、Plugin・外部素材Recoveryはこの層に含めない。

## Consumer Writer Options

`YmmpxV2WriteRequest.Options` はConsumerがWriter内部のresource discoveryやZIP操作を再実装せずに利用するためのAPIである。

- `ExcludedResources` はproject directory基準で絶対pathへ正規化して照合する。Windowsでは大小文字を区別しない。空値と存在しない指定は無視する。除外resourceはmanifestとZIPへ含めず、package projectの該当`FilePath`は元参照のまま残す。
- PNG ImageSequenceはatomicである。sequenceの任意frameが除外される場合、sequence全体をpackageへ含めない。brokenな一部frame packageを作らない。
- `IncludeProjectUiSettings` のdefaultは`true`で、v1と同じくrootの`LayoutXml`と`ToolStates`を含める。`false`ではpackage project copyからこの2 propertyだけを削除し、source projectは変更しない。
- `Progress` は`IProgress<YmmpxV2WriteProgress>`であり、stage・current・total・resource名を通知する。stageはresource discovery、project処理、hash、package/resource書込、finalize、completedである。CoreはUI文言やUI frameworkへ依存しない。callback例外は呼び出し側の例外として伝播する。

Cancellationは`CancellationToken`だけが担当し、progressは結果やcancelの代替ではない。これらのoptionsはFormat 2.0やmanifest schema 1を変更しない。
