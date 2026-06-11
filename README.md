# YmmpxLib

`YmmpxLib` は、`.ymmp` の作成と `.ymmpx` の展開、そして YMMP プロジェクト JSON 内の `FilePath` 置換を行うライブラリです。

## 主な機能

- `.ymmp` から `.ymmpx` を作成します
- `.ymmpx` を展開し、プロジェクト内の `FilePath` を復元します
- `FilePath` の列挙と置換を行います
- UI 状態関連の項目 (`LayoutXml`, `ToolStates`) を除外できます

## API

### `YmmpxPackageService.CreatePackageAsync`

```csharp
Task<YmmpxPackagingResult> CreatePackageAsync(
    string projectFilePath,
    string outputPath,
    ISet<string>? excludedFiles = null,
    YmmpxPackagingOptions? options = null,
    IProgress<YmmpxPackagingProgress>? progress = null,
    CancellationToken cancellationToken = default)
```

- `projectFilePath`: 入力 `.ymmp` のパス
- `outputPath`: 出力 `.ymmpx` のパス
- `excludedFiles`: パッケージから除外するファイル
- `options`: パッケージ作成オプション
- `progress`: 進捗通知

`YmmpxPackagingResult`:

- `OutputPath`: 作成した `.ymmpx` のパス
- `ResourceCount`: 同梱した素材数
- `FileMap`: `保存ファイル名 -> パッケージ内パス(resources/...)`

### `YmmpxPackageService.ExtractAndRestoreProject`

```csharp
YmmpxUnpackResult ExtractAndRestoreProject(
    string ymmpxPath,
    string extractDirectory)
```

- `ymmpxPath`: 入力 `.ymmpx` のパス
- `extractDirectory`: 展開先
- `extractDirectory` が相対パスなら、返り値も相対文字列のまま返ります

`YmmpxUnpackResult`:

- `ExtractDirectory`: 展開先
- `ProjectFilePath`: 復元後 `.ymmp` のパス
- `ReplacedPathCount`: `FilePath` を置換した件数
- `LinkMap`: 元の `FilePath -> 展開後の素材パス`

### `YmmpxProjectJson`

```csharp
IEnumerable<string> FindFilePaths(JsonElement element)
int ReplaceFilePaths(JsonNode node, IReadOnlyDictionary<string, string> linkMap)
bool RemoveUiSettings(JsonNode node)
```

- `FindFilePaths`: JSON から `FilePath` を再帰的に列挙します
- `ReplaceFilePaths`: `linkMap` に一致する `FilePath` を置換します。`resources/a.txt` と `resources\\a.txt`、Windows 上の大小文字差は互換的に扱います
- `RemoveUiSettings`: ルートの `LayoutXml` と `ToolStates` を削除します

## 使い方

### パッケージ作成

```csharp
using YmmpxLib;

var result = await YmmpxPackageService.CreatePackageAsync(
    projectFilePath: @"C:\work\sample.ymmp",
    outputPath: @"C:\work\sample.ymmpx");

Console.WriteLine(result.OutputPath);
```

### 除外ファイル付きで作成

```csharp
using YmmpxLib;

var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    @"C:\work\temp.wav"
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

### 展開と復元

```csharp
using YmmpxLib;

var unpack = YmmpxPackageService.ExtractAndRestoreProject(
    ymmpxPath: @"C:\work\sample.ymmpx",
    extractDirectory: @"C:\work\sample_unpack");

Console.WriteLine(unpack.ProjectFilePath);
```

## 補足

- 展開時は `links.json` を優先し、互換用に `manifest.json`、`links.txt` も読み込みます
- パッケージには `_ymmpx_project_path.txt` を含め、元のプロジェクトファイル名を保持します
- 出力ファイル名や展開先は、既存ファイル・既存フォルダと衝突しないように調整されます

## CLI

サンプルとして `YMMPXCli` が付属しています。

```powershell
dotnet run --project .\YMMPXCli\YMMPXCli.csproj -- "C:\path\to\project.ymmp"
dotnet run --project .\YMMPXCli\YMMPXCli.csproj -- "C:\path\to\package.ymmpx"
```

