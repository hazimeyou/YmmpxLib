# R4 YMM4 プラグインの素材参照との互換性

次期開発では、YMM4 ホスト側で参照元を把握し、SDK 非依存の YmmpxLib Core に格納と展開を委ねる方式を推奨する。R4 では設計と合成検証までを実施した。SDK v4.53.0.2 と v4.57.0.1 の実 DLL を参照した試験は、Windows 上で各 16 件に成功した。実 YMM4 での読み込み、保存、再読み込み、描画は未検証。

## SDK と本体の動作

`YukkuriMovieMaker.Plugin.dll` の `YukkuriMovieMaker.Project.IFileItem` は `IResourceItem` を継承する。両版で確認した公開シグネチャは次のとおり。

```csharp
public interface IFileItem : IResourceItem
{
    IEnumerable<string> GetFiles();
    void ReplaceFile(string from, string to);
}
```

- `GetFiles()` は、呼び出した所有者の参照パス文字列を列挙する。非 null が注釈された戻り値に、参照位置、ファイル種別、基準ディレクトリ、必須依存、順序、存在、重複排除の情報は含まれない。`IResourceItem.GetResources()` は `IEnumerable<TimelineResource>` を返す別の契約。
- `ReplaceFile(from, to)` は所有者の参照変更に使う。戻り値は `void` で、置換件数、成功状態、通知、永続化を受け取る共通手段はない。元の文字列を保管し、正規化後の表現はディスク上の素材解決や重複排除に使う。正規化後の文字列を `from` に渡すと、文字列完全一致の実装では変更が適用されない。
- 基底クラスが `IFileItem` を実装すれば、派生インスタンスを `IFileItem` として呼び出せる。子プロパティや配列の要素への委譲は、親の実装または呼び出し側が把握する型付きの経路が必要。`YukkuriMovieMaker.Commons.Animatable.GetAnimatables()` は `protected abstract IEnumerable<IAnimatable>` のアニメーション用契約。公式サンプルも子を明示的に返す。 [公式サンプル](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples/blob/master/YMM4SamplePlugin/PropertyEditor/SampleParameterClass.cs)

以下は v4.53.0.2 と v4.57.0.1 の配布 DLL の静的確認。`YukkuriMovieMaker.dll` のクラスや補助処理は、安定した SDK 契約の根拠として扱わない。 [公式 v4.53.0.2 配布](https://github.com/manju-summoner/YukkuriMovieMaker4/releases/tag/v4.53.0.2) [公式 v4.57.0.1 配布](https://github.com/manju-summoner/YukkuriMovieMaker4/releases/tag/v4.57.0.1) [公開アセンブリの扱い](https://github.com/manju-summoner/YukkuriMovieMaker.Plugin.Community#参照可能なアセンブリ)

| 対象 | SDK または確認した本体の動作 |
| --- | --- |
| 図形 | `YukkuriMovieMaker.Plugin.Shape.IShapePlugin.CreateShapeParameter(YukkuriMovieMaker.Project.SharedDataStore? sharedData)` が `IShapeParameter` を返す。`IShapeParameter` は `IFileItem` を継承しない。本体 `Project.Items.ShapeItem` と図形を扱う `Project.Effects.MaskEffect` は具象 `ShapeParameter` が実装すると、その `GetFiles` と `ReplaceFile` に委譲する。`SharedDataStore.Load<T>()` と `Save(object)` は共有データの契約。 |
| 立ち絵 | SDK の `YukkuriMovieMaker.Plugin.Tachie` には `ITachieCharacterParameter`、`ITachieItemParameter`、`ITachieFaceParameter` がある。具象パラメータからの列挙には別途 `IFileItem` の実装が必要。本体 `Project.Character` は立ち絵設定、既定アイテムと表情パラメータ、効果の既知の集合を扱う。`Project.Items.TachieItem` と `TachieFaceItem` も、対応する具象パラメータが実装する場合に委譲する。 |
| エフェクト | SDK の `YukkuriMovieMaker.Plugin.Effects.IVideoEffect` と `IAudioEffect` は `IFileItem` を継承する。`VideoEffectBase` の既定実装は列挙が空で置換は何もしない。派生型でオーバーライドできる。`AudioEffectBase` の同メソッドは既定で空・無操作かつオーバーライド不可。音声効果で素材を持つ場合は、具象型のインターフェース再実装がホストの実際の呼び出し経路で選ばれるか確認する。 |
| ディレクトリ | 同梱プラグインの `AnimationTachie.CharacterParameter` と `SimpleTachie.CharacterParameter` は `Directory` の文字列を返し、`ReplaceFile()` で一致時に `Directory` を更新する。一方、本体 `Project.ReplaceFilePath` が使う内部 `IFileItemEx.UpdateFilePath` は `Distinct()`、空白・実在する旧ファイルの除外、基準ディレクトリからの再計算を行い、`File.Exists(to)` を満たす置換先を採用する。この経路ではディレクトリを移設できない。 |

### プラグイン実装者向け

- 図形は生成して返すパラメータ、立ち絵はキャラクター・アイテム・表情のうち素材を所有するパラメータを公開する。エフェクトは、実際に呼び出されるインターフェース実装を確認する。
- 複数の子を集約して列挙する所有者では、対応する変更も同じ子へ委譲する。一つの素材を複数箇所が参照するなら、その所有者内で該当するすべての参照を更新する。同じパスを何度返すかに参照数の意味を持たせない。
- パスの比較規則、相対パスの基準、ディレクトリの構成、外部の副素材を明文化する。空参照や未選択の文字列は素材として扱わない。欠損した有効な参照は利用者に識別できるようにする。
- 変更は必要なプロパティセッターを通して、通知、キャッシュ更新、保存対象の更新を具体的な実装で確認する。動作中のプロジェクトに変更を加えて確認する手順は避け、独立したテストプロジェクトで保存後に再読込して照合する。

## 方式比較

| 方式 | Core・CLI | 取得、変更、永続化 | 参照とディレクトリ | API・形式・旧版 | 実装、保守、テスト |
| --- | --- | --- | --- | --- | --- |
| A JSON 拡張 | Core の SDK 非依存と CLI を維持 | 既知の型と保存キーを支持。プラグインの通知や実行時専用の参照は取得・検証できない。原本から読み、出力用 JSON の一致箇所のみを変更できる | JSON の位置を識別できる。ディレクトリ属性や依存階層には型と版ごとの仕様が必要 | 既存形式で表せて旧展開側が理解する範囲は維持可能 | 小規模な互換補完に適する。対象版が増えると実装とフィクスチャが増える |
| B ホスト完結 | SDK 機能に対応する CLI は別途必要。Core を維持してもホスト側に格納・展開の実装が増える | 対応プラグインを `GetFiles` / `ReplaceFile` で扱える。通知と保存は独立モデル、正規の保存経路、再読込で検証する | 所有者単位の変更。ファイルとディレクトリ、必須依存、階層は別途解決 | 既存実装と挙動が分かれやすく、旧形式・外部展開互換の維持コストが高い | ホスト検証が必須。保全や整合性を二重管理するため大きい |
| C ホストで解決し Core で格納・展開 | Core、既存 API、CLI を維持。ホスト拡張は別の入口 | SDK 対応所有者の列挙と変更はホスト側。独立モデル上で事後確認し、正規の保存、再読込まで検証する | 所有者と元の表記を維持し、ファイル実体の重複排除と分離。ディレクトリは明示的な依存一覧で階層を維持 | 初期検証は既存形式で表せる範囲。独自参照を既存展開側が復元できない場合は形式・対応範囲を別途承認 | アダプターと数値・文字列の内部契約を追加。Core 回帰を再利用し、ホスト受入を隔離できる |
| D 型・版ごとの JSON アダプターを併用 | A と同様に維持 | SDK 非対応の旧版のうち、保存と再読込で確認できたパスに適用 | 既知の位置と依存関係だけを追跡 | 対応する既存形式の範囲で選択的に補完 | 対象、バージョン、変換フィクスチャを絞れば保守可能 |

### 推奨する責務と公開判定

1. **ホスト側の取得**。タイムライン・キャラクターの既知のルートと型付きの子経路を用いる。親が子を集約すると確認できた範囲は親に委譲する。独立した子はオブジェクト同一性で訪問管理し、探索数と列挙件数を制限する。任意のプロパティゲッターを総当たりしない。同期プラグイン呼び出しの停止不能や副作用には、件数上限に加えて別プロセス等の隔離判断が必要。
2. **Core 側の格納**。所有者と元の表記を持つ参照を、解決済みパスと格納エントリから分ける。複数の所有者や異なる元表記を同じ物理素材に対応づけられるようにする。除外、欠損、種別不明と格納済みを区別する。ディレクトリは利用者または型対応の解決器が明示した構成ファイルのみを対象とし、階層を保持する。除外との整合、件数・容量上限、境界外やリパースポイントの判定も要件に含める。
3. **ホスト側の復元**。原本バイトや稼働中プロジェクトを変更せずに検証できる、独立した保存可能モデルを確立する。変更前の参照から一意の置換計画を作成し、同じ所有者への重複呼び出しや置換の連鎖を避ける。材料・出力先を検証してから `ReplaceFile` を実行し、置換後の列挙、保存済み内容、再読込後の参照を照合する。途中失敗、無操作、共有状態への波及を検出した成果物は公開しない。
4. **現行形式で表せる範囲を先行する**。現行の展開は JSON の `FilePath` を格納パスから復元する。`Directory` や独自名を含む SDK 由来参照が保存後も同じ JSON の復元経路で解決できることを個別に確認する。より広い参照を扱う場合は、ホスト付き展開の責務、保存情報、CLI と旧読み込み側の扱いを承認事項として先に確定する。 [現行の制約](known-limitations.md) [JSON の参照処理](../YMMPXLib/YmmpxProjectJson.cs)

最初の製品化候補は、明示的に確認したファイル参照を、保存・再読込可能な隔離したプロジェクトで往復する範囲。ディレクトリと旧版用アダプターは、依存の境界と実データに依存しない正規フィクスチャを確立してから段階的に追加する。確認済みの型付き所有者を扱うために `Animatable` の新しい API は不要。

## sin型立ち絵

2026-11-25 に確認した固定コミットの公開ソースに基づく。3 つの参照の C# ソースで、プラグイン独自の `IFileItem` 実装は確認できなかった。v1.2 系の `PsdFileInfo` が使う SDK 側の `PsdShapeParameter` は `IFileItem` を実装する。外側の所有者が当該オブジェクトに到達する経路が必要。公開ソースのプロパティと移行処理を確認した。各版で実 YMM4 が出力する `.ymmp` のキー・型識別子・相対パスの基準、移設後の描画は未検証。

| 参照 | 公開ソースで確認した保持・移行 | 現行の見込みと必要な検証 |
| --- | --- | --- |
| 公開リリース `STP-v1.1.1.0` (`1f4b2b4`) | キャラクターの `Directory`、立ち絵アイテム・表情の `Parts`、図形の `PartsAndRoot.Root` と `PartsAndRoot.Parts`。`PartBlock` が継承する `ImagePath` に各素材がある。画像と同じ場所から同名の `.stpi` を導出する処理もある | `FilePath` 走査では `Directory`、`Root`、`ImagePath` を取得・復元しない。版を限定した旧 JSON 対応は、保存済みフィクスチャと `.stpi` が必要な機能の確認を条件とする。 [公開リリース](https://github.com/sinBetaKun/SinTachiePlugin/releases/tag/STP-v1.1.1.0) [図形](https://github.com/sinBetaKun/SinTachiePlugin/blob/1f4b2b463db474a67ee41265004d38b5feb9aa2b/SinTachiePlugin/ShapePludin/PartsListControllerForShape/PartsOfShapeItem.cs) [素材パス](https://github.com/sinBetaKun/SinTachiePlugin/blob/1f4b2b463db474a67ee41265004d38b5feb9aa2b/SinTachiePlugin/Parts/ControlledParamsOfPart.cs#L38) [関連ファイル](https://github.com/sinBetaKun/SinTachiePlugin/blob/1f4b2b463db474a67ee41265004d38b5feb9aa2b/SinTachiePlugin/Parts/PartBlock.cs#L16) |
| 公開ブランチ `v1.2.0.0` (`b3c62cc`) | キャラクターの `Assets[].Path` は `AssetType` で `Directory` または `Psd`。`Directory` は `Assets` に移行する setter。図形では `PartValuesAndAssets.Assets` と `PartValues` に保持し、旧 `PartsAndRoot` の setter で移行する。深部に `ImageFilePath`、`VideoFilePath`、`AudioFilePath`、`PsdFileInfo.FilePath` | 生成 JSON に `PsdFileInfo.FilePath` が通常の文字列で残る場合は既存処理の候補。`Assets[].Path`、画像・動画・音声の独自名、PSD の追加レイヤー参照は別途認識と復元の確認が必要。 `Directory` が現在の保存キーとして出力される想定では処理しない。 [キャラクター](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/TachiePlugin/SinTachieCharacterParameter.cs#L15) [素材モデル](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/CharacterAsset/CharacterAssetsData.cs#L12) [図形の移行](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/ShapePludin/ShapeParameterOfSinTachie.cs#L18) [PSD](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/Part/LayerInformation/SourceSelectArg/Parameter/PsdFileParameter.cs#L80) |
| 公開開発ブランチ `v1.2.0.0_dev` (`a461cec`) | 上記パスの保持と実装状況は、確認した 2 コミット間で同じ | 別のリリースとみなさず、対象ビルドと保存フィクスチャを特定して受け入れる。 [差分](https://github.com/sinBetaKun/SinTachiePlugin/compare/b3c62cc1fe52424ed409042792032c8ac62cbe0f...a461cecb89afe708609899440e31e7b4a8698efa) |
| 未公開の試作 | `IFileItem` 対応を検討中との情報。実装とシリアライズの中身は未確認 | 具象パラメータがパスと必要な子を返し、対称に置換し、保存と再読込で保てる場合に共通アダプターの対象になる |

v1.2 系の素材モデルの `ChangePath` には旧パス履歴に関する条件、ダイアログ、状態更新がある。参照変更に流用できるかは実装とホスト受入で確認する。フォルダ型素材は `Assets[].Path` の文字列だけで必要なファイルを確定できない。パーツや PSD、音声・動画と、旧パス履歴や未使用ファイルの扱いを区別し、必要素材の一覧を確認するまでは対象ディレクトリを自動収集しない。 [パス変更の実装](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/CharacterAsset/CharacterAssetsData.cs#L40) [画像](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/Part/LayerInformation/SourceSelectArg/Parameter/ImageFileParameter.cs#L30) [動画](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/Part/LayerInformation/SourceSelectArg/Parameter/VideoFileParameter.cs#L31) [音声](https://github.com/sinBetaKun/SinTachiePlugin/blob/b3c62cc1fe52424ed409042792032c8ac62cbe0f/SinTachiePlugin/PartAnimation/PartAnimationOpeArg/Parameter/AudioFileParameter.cs#L31)

公開リリースの描画処理は、代表画像の親フォルダ直下から同じベース名接頭辞を持つ画像を選びます。この依存群は代表画像1枚や編集用の `.stpi` と分けて検証します。 [描画時の同階層探索](https://github.com/sinBetaKun/SinTachiePlugin/blob/1f4b2b463db474a67ee41265004d38b5feb9aa2b/SinTachiePlugin/LayerValueListController/LayerNodeManager.cs#L74-L105)

## 合成環境での確認

[検証用プロジェクト](../scripts/sdk-file-reference-probe/SdkFileReferenceProbe.csproj) はソリューションと配布物から独立する。Windows / .NET SDK 10.0.404 の一時ディレクトリで版ごとに独立してビルドし、`YukkuriMovieMaker.Plugin.dll` の実契約を使った。v4.53.0.2 は CI 固定 SHA-256 と一致した公式アーカイブ、v4.57.0.1 はローカル Lite 配布物から参照した。両版とも Release ビルドの警告・エラーは 0、合成ケースは 16/16 成功。

| ケース | 合成試験で確認した挙動 |
| --- | --- |
| 1–3 直接、継承、親の集約 | インターフェース経由で列挙・置換した。通知は合成オブジェクトのセッターで観測。 |
| 4–8 子のみ、コレクション、共有、二重列挙、循環 | 型付きの子経路、オブジェクト参照同一性、所有者と元の値、探索上限で管理した。不要なゲッターは呼ばれず、既知の集約を通した二重走査を回避した。 |
| 9–10 不正値、例外 | null・空白・不正・欠損を診断した。即時例外・列挙途中の例外・件数上限を超えた列挙を区別し、失敗した所有者の部分結果を破棄した。 |
| 11–12 無操作、途中失敗 | 再列挙で無操作を識別した。部分変更が発生した複製を失敗扱いにし、原本オブジェクトのシリアライズ結果と原本ファイル・素材のバイトが一致した。 |
| 13 同じファイルへの複数参照 | 1 個の素材に対する 2 所有者の参照を維持し、両方に置換を適用した。 |
| 14 ディレクトリ | 明示した 2 ファイルだけを相対階層と内容を維持して別ディレクトリへ写し、複製の参照を更新した。 |
| 15–16 独自名、永続化の失敗 | 独自名の JSON に反映する合成実装と、メモリ内で変更しても保存した JSON に反映しない実装を識別した。 |

試験の列挙・変更・JSON 保存は合成実装が担当する。実際のプロジェクトの隔離コピー、ホストの保存処理、実プラグインの参照、描画、製品フォーマットの拡張は受入試験で扱う。

再現には `Program.cs` と `SdkFileReferenceProbe.csproj` を SDK 版ごとに異なる一時フォルダにコピーし、そのディレクトリで次を実行する。`$sdkDir` には該当版の公式 `YukkuriMovieMaker.Plugin.dll` を含むディレクトリを設定する。

```powershell
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $PWD 'dotnet-home'
dotnet build .\SdkFileReferenceProbe.csproj -c Release "-p:YmmSdkDir=$sdkDir"
if ($LASTEXITCODE -ne 0) { throw "SDK probe build failed." }
dotnet .\bin\Release\net10.0\SdkFileReferenceProbe.dll
```

## ホスト受入試験と仕様確認

- 許可された専用ホストで、対象の二つの YMM4 版と合成プラグイン、標準の図形・立ち絵・映像／音声エフェクト、対象の sin型版の組み合わせを固定する。元プロジェクトと素材のハッシュ、稼働中モデルの参照、編集・共有状態を前後で比較する。
- 取得後に元の素材を参照できない隔離先で、格納・展開・参照変更・保存・再読込・描画まで確認する。同一素材の別所有者、同名別素材、相対／絶対、欠損・除外、共有パラメータ、連番、ディレクトリの相対階層を含める。
- プラグイン欠如、取得のみ対応、無操作、途中失敗、変更通知不成立、保存未反映、共有データへの副作用を失敗として検出する。部分成果物や誤った成功件数を公開しない。終端で原本・既存成果物がバイト単位で保全されることを確認する。
- 旧版の CLI と外部展開から同じ成果物を読み、互換な参照が従来と同様に復元されることを確認する。互換表に保存形式、必要プラグイン、失敗理由、未対応参照を記載する。

YMM4 本体の仕様として確認したい点は、安定したプロジェクトの読み込み・独立コピー・保存 API、`ReplaceFile` の呼び出しスレッドと編集／Undo 通知、図形・立ち絵・効果の所有者境界、ディレクトリと相対パスの基準、`GetFiles` と `GetResources` の用途。既存契約で不足する事例を実機で確認した場合は、参照種別、必要依存、置換結果の明示方法を提案候補にする。

YMMProjectManager と YMMResourcePackager の統合では、ホスト側の検出範囲、Core が格納した素材、再リンク検証後の件数が一致する入口を検証する。現在の CLI、既存の公開 API、旧形式・外部展開に必要な挙動を変更する場合は、対象製品との整合を確認してから承認を得る。
