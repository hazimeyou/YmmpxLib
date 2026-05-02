# YmmpxLib

`YmmpxLib` は `.ymmpx` の作成・展開と、YMM プロジェクト JSON 内の `FilePath` 解決を提供するライブラリです。

## 対象機能

- `.ymmp` + 素材ファイルを `.ymmpx` にパッケージング（同梱時 `FilePath` はファイル名化）
- `.ymmpx` を展開し、`FilePath` を展開先の実ファイルへ復元
- プロジェクト JSON の `FilePath` 列挙/置換
- 任意で UI 設定 (`LayoutXml`, `ToolStates`) を除外してパッケージ化

## 主要 API

### YmmpxPackageService.CreatePackageAsync

```csharp
Task<YmmpxPackagingResult> CreatePackageAsync(
    string projectFilePath,
    string outputPath,
    ISet<string>? excludedFiles = null,
    YmmpxPackagingOptions? options = null,
    IProgress<YmmpxPackagingProgress>? progress = null,
    CancellationToken cancellationToken = default)
```

- `projectFilePath`: 入力 `.ymmp` パス
- `outputPath`: 出力 `.ymmpx` パス
- `excludedFiles`: パッケージから除外する素材パス集合（絶対/相対パス、`file://` URI、環境変数展開に対応）
- `options`: パッケージオプション
  - `IncludeProjectUiSettings` (`bool`, default: `true`)
- `progress`: 進捗通知
  - `CompletedCount`, `TotalCount`, `Message`, `Percentage`

戻り値 `YmmpxPackagingResult`:

- `OutputPath`: 作成した `.ymmpx` パス
- `ResourceCount`: 同梱した素材数
- `FileMap`: `保存ファイル名 -> パッケージ内パス(resources/...)`

### YmmpxPackageService.ExtractAndRestoreProject

```csharp
YmmpxUnpackResult ExtractAndRestoreProject(
    string ymmpxPath,
    string extractDirectory)
```

- `.ymmpx` を `extractDirectory` へ展開
- `links.json` / `links.txt` を読み込み
- `.ymmp` 内 `FilePath` を展開先の素材パスに置換

戻り値 `YmmpxUnpackResult`:

- `ExtractDirectory`: 展開先
- `ProjectFilePath`: 復元後 `.ymmp` のフルパス
- `ReplacedPathCount`: 置換件数
- `LinkMap`: `保存ファイル名 -> 展開先素材パス`

### YmmpxProjectJson

```csharp
IEnumerable<string> FindFilePaths(JsonElement element)
int ReplaceFilePaths(JsonNode node, IReadOnlyDictionary<string, string> linkMap)
bool RemoveUiSettings(JsonNode node)
```

- `FindFilePaths`: JSON から `FilePath` を再帰列挙
- `ReplaceFilePaths`: `linkMap` を使って `FilePath` を置換
- `RemoveUiSettings`: ルートの `LayoutXml`, `ToolStates` を削除

### 補助 API

```csharp
Dictionary<string, string> LoadLinkMap(string baseDirectory)
string GetAvailableDirectoryPath(string desiredPath)
string GetAvailableFilePath(string desiredPath)
```

- `LoadLinkMap`: `links.json` 優先、次に `manifest.json`、最後に `links.txt` を互換読み込み
- `GetAvailableDirectoryPath`: 既存時に `_1`, `_2`... を付けて空きパス返却
- `GetAvailableFilePath`: 既存時に `_1`, `_2`... を付けて空きファイル名返却

## 使用例

### 1) 基本パッケージング

```csharp
using YmmpxLib;

var result = await YmmpxPackageService.CreatePackageAsync(
    projectFilePath: @"C:\work\sample.ymmp",
    outputPath: @"C:\work\sample.ymmpx");

Console.WriteLine(result.OutputPath);
```

### 2) 除外ファイル + UI 設定除外でパッケージング

```csharp
using YmmpxLib;

var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    @"C:\work\素材\temp.wav"
};

var result = await YmmpxPackageService.CreatePackageAsync(
    projectFilePath: @"C:\work\sample.ymmp",
    outputPath: @"C:\work\sample.ymmpx",
    excludedFiles: excluded,
    options: new YmmpxPackagingOptions
    {
        IncludeProjectUiSettings = false
    });
```

### 3) 展開と `FilePath` 復元

```csharp
using YmmpxLib;

var unpack = YmmpxPackageService.ExtractAndRestoreProject(
    ymmpxPath: @"C:\work\sample.ymmpx",
    extractDirectory: @"C:\work\sample_unpack");

Console.WriteLine(unpack.ProjectFilePath);
```

## 備考

- 展開時は `links.json` を優先し、次に `manifest.json`、最後に `links.txt`（後方互換）を参照します。
- パッケージには `_ymmpx_project_path.txt` を含め、元のプロジェクトファイル名を保持します。
- 配布は GitHub Releases の ZIP（`YmmpxLib-v*.zip`）を利用してください。

## 対応拡張子

- .ymmp: パッケージ入力（同梱元）
- .ymmpx: パッケージ出力 / 展開入力

## CLI

サンプルとして YMMPXCli を利用できます。

- プロジェクト: [YMMPXCli/YMMPXCli.csproj](YMMPXCli/YMMPXCli.csproj)
- ライブラリ: [YMMPXLib/YmmpxLib.csproj](YMMPXLib/YmmpxLib.csproj)
- 実行例: dotnet run --project .\\YMMPXCli\\YMMPXCli.csproj -- "C:\\path\\to\\project.ymmp"
- 実行例: dotnet run --project .\\YMMPXCli\\YMMPXCli.csproj -- "C:\\path\\to\\package.ymmpx"

## ライセンス

本リポジトリのライセンスは [LICENSE](LICENSE) を参照してください。
## 互換モード

YmmpxLib は **単一の YmmpxLib.dll** で動作し、互換モードで挙動を切り替える設計です。
複数の YmmpxLib.dll を同時配置して共存させる運用は推奨しません。

- `YmmpxCompatibilityVersion`
- `YmmpxOptions`
- `YmmpxService.Create(options)`

現在は `Latest` 実装を基準にし、`V0_1` / `V0_2` は `Latest` へフォールバックします。

## YmmpxLibPlugin

`YmmpxLibPlugin` は YMM4 向けの前提プラグインです。
他の hazimeyou 製 YMM プラグインが共有する `YmmpxLib.dll` を提供します。

- 表示名: `YmmpxLib Shared Library`
- 説明: `Shared YmmpxLib runtime library for hazimeyou YMM plugins.`
- 目的: YMM 側へ確実に読み込ませるための薄いエントリプラグイン

運用方針:
- 他プラグインは `YmmpxLib.dll` を同梱しない
- 他プラグインはビルド時のみ参照し、配布時は `Private=false` で同梱しない
- YmmpxLib は単一DLL + 互換モード方式で運用する

## YMM4 ライブラリ取得

YMM4 のビルド用 DLL はリポジトリへ同梱せず、CI またはローカルスクリプトで取得します。

```powershell
.\scripts\fetch-ymm4-libs.ps1
dotnet build
```

## Release 成果物

- `YmmpxLib-vX.Y.Z.zip`
  - `YmmpxLib.dll`
  - `YmmpxLib.deps.json`
  - `README.md`
  - `LICENSE.txt`

- `YmmpxLibPlugin-vX.Y.Z.ymme`
  - `YmmpxLibPlugin.dll`
  - `YmmpxLibPlugin.deps.json` (存在する場合)
  - `YmmpxLib.dll`
  - `YmmpxLib.deps.json`
  - `README.md`
  - `LICENSE.txt`
