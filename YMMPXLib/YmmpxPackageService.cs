using System.IO.Compression;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YmmpxLib;

/// <summary>
/// YMMP プロジェクトを YMMPX 形式で圧縮・展開するサービスです。
/// </summary>
public static class YmmpxPackageService
{
    /// <summary>
    /// プロジェクト JSON と参照リソースを収集し、YMMPX アーカイブを作成します。
    /// </summary>
    public static async Task<YmmpxPackagingResult> CreatePackageAsync(
        string projectFilePath,
        string outputPath,
        ISet<string>? excludedFiles = null,
        YmmpxPackagingOptions? options = null,
        IProgress<YmmpxPackagingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (!File.Exists(projectFilePath))
            throw new FileNotFoundException("Project file was not found.", projectFilePath);

        // 入力プロジェクトの基準パスを決定する。
        var normalizedProjectPath = Path.GetFullPath(projectFilePath);
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)
            ?? throw new DirectoryNotFoundException($"Project directory was not found: {projectFilePath}");

        // 除外対象を絶対パスに正規化して比較可能にする。
        var excluded = excludedFiles is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : excludedFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizePath(projectDirectory, path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projectText = await File.ReadAllTextAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
        options ??= new YmmpxPackagingOptions();

        // 元 JSON を解析し、参照ファイル (FilePath) の一覧を作る。
        using var document = JsonDocument.Parse(projectText);
        var resourceEntries = YmmpxProjectJson
            .FindFilePaths(document.RootElement)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(originalPath => new
            {
                OriginalPath = originalPath,
                ResolvedPath = NormalizePath(projectDirectory, originalPath),
            })
            .Where(x =>
                File.Exists(x.ResolvedPath) &&
                !excluded.Contains(x.ResolvedPath) &&
                !string.Equals(x.ResolvedPath, normalizedProjectPath, GetPathComparison()))
            .ToList();

        // 保存名 (ファイル名ベース + 連番) の対応表を先に作り、JSON 書き換えと links.json で共用する。
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagedNameByResolvedPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filesToPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in resourceEntries)
        {
            var fileName = Path.GetFileName(entry.ResolvedPath);
            var uniqueFileName = GetUniqueFileName(fileName, usedNames);
            var packagedPath = $"resources/{uniqueFileName}";
            packagedNameByResolvedPath[entry.ResolvedPath] = uniqueFileName;
            fileMap[uniqueFileName] = packagedPath;
            filesToPackage[entry.ResolvedPath] = packagedPath;
        }

        // 必要に応じて UI 状態除外 + FilePath のファイル名化を行った JSON をパッケージ化する。
        var projectTextForPackage = projectText;
        var projectNode = JsonNode.Parse(projectTextForPackage);
        if (projectNode is not null)
        {
            if (!options.IncludeProjectUiSettings)
                YmmpxProjectJson.RemoveUiSettings(projectNode);

            YmmpxProjectJson.ReplaceFilePathsForPackaging(projectNode, sourcePath =>
            {
                var resolved = NormalizePath(projectDirectory, sourcePath);
                return packagedNameByResolvedPath.TryGetValue(resolved, out var packagedName)
                    ? packagedName
                    : null;
            });

            projectTextForPackage = projectNode.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        var totalCount = filesToPackage.Count;
        // links ファイルを一時生成するための作業ディレクトリ。
        var tempDir = Path.Combine(Path.GetTempPath(), "YmmpxLib", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var linksJsonFile = Path.Combine(tempDir, "links.json");

            // links.json は扱いやすい JSON 形式のマニフェストとして同梱する。
            await File.WriteAllTextAsync(
                linksJsonFile,
                JsonSerializer.Serialize(fileMap, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var totalBytes = filesToPackage.Keys
                .Select(source => new FileInfo(source).Length)
                .Append(new FileInfo(linksJsonFile).Length)
                .Sum();

            long processedBytes = 0;
            long lastReportedProcessedBytes = 0;
            var completedCount = 0;
            var reportGate = new ProgressReportGate();
            void ReportProgress(string message, bool force = false, bool isCompleted = false)
            {
                if (progress is null)
                    return;

                processedBytes = Math.Max(processedBytes, lastReportedProcessedBytes);

                if (!force && !reportGate.ShouldReport(processedBytes))
                    return;

                lastReportedProcessedBytes = processedBytes;

                progress.Report(new YmmpxPackagingProgress(
                    completedCount,
                    totalCount,
                    message,
                    processedBytes,
                    totalBytes)
                {
                    IsCompleted = isCompleted
                });
            }

            ReportProgress("Collecting resources", force: true);

            // プロジェクトエントリ名は必ず .ymmp 拡張子に統一する。
            var projectEntryName = Path.GetFileName(projectFilePath);
            if (string.IsNullOrWhiteSpace(projectEntryName))
                projectEntryName = "project.ymmp";
            if (!projectEntryName.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase))
                projectEntryName = Path.ChangeExtension(projectEntryName, ".ymmp");

            // 既存出力がある場合は上書きできるよう事前に削除する。
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                // プロジェクト本体 JSON を書き込む。
                var projectEntry = zip.CreateEntry(projectEntryName);
                await using (var projectStream = projectEntry.Open())
                await using (var projectWriter = new StreamWriter(projectStream))
                {
                    await projectWriter.WriteAsync(projectTextForPackage).ConfigureAwait(false);
                }

                // 展開時にプロジェクトファイルを特定するためのマーカー。
                var markerEntry = zip.CreateEntry("_ymmpx_project_path.txt");
                await using (var markerStream = markerEntry.Open())
                await using (var markerWriter = new StreamWriter(markerStream))
                {
                    await markerWriter.WriteAsync(projectEntryName).ConfigureAwait(false);
                }
                // 上で組み立てた fileMap / filesToPackage を使って、
                // 「相対 FilePath -> resources 内実体」の対応を同梱する。


                ReportProgress("Starting links.json", force: true);
                // 旧バージョン互換性は同梱時不要。展開時のみ互換性を保つ
                await CopyFileToEntryWithProgressAsync(
                    zip,
                    linksJsonFile,
                    "links.json",
                    bytesWritten =>
                    {
                        processedBytes += bytesWritten;
                        ReportProgress("Writing links.json");
                    },
                    cancellationToken).ConfigureAwait(false);
                ReportProgress("Writing links.json", force: true);

                // 実ファイルを resources/ 配下へ格納。
                foreach (var (source, destination) in filesToPackage)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReportProgress($"Starting {Path.GetFileName(source)}", force: true);
                    await CopyFileToEntryWithProgressAsync(
                        zip,
                        source,
                        destination,
                        bytesWritten =>
                        {
                            processedBytes += bytesWritten;
                            ReportProgress($"Packing {Path.GetFileName(source)}");
                        },
                        cancellationToken).ConfigureAwait(false);

                    completedCount++;
                    ReportProgress($"Packed {Path.GetFileName(source)}", force: true);
                }
            }

            completedCount = totalCount;
            processedBytes = totalBytes > 0 ? totalBytes : 0;
            ReportProgress("Completed", force: true, isCompleted: true);

            return new YmmpxPackagingResult(outputPath, filesToPackage.Count, fileMap);
        }
        finally
        {
            // 一時フォルダは成功/失敗にかかわらず必ず削除する。
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// YMMPX を展開し、プロジェクト JSON 内の FilePath を展開先へ復元します。
    /// </summary>
    public static YmmpxUnpackResult ExtractAndRestoreProject(string ymmpxPath, string extractDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ymmpxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractDirectory);

        if (!File.Exists(ymmpxPath))
            throw new FileNotFoundException("Ymmpx file was not found.", ymmpxPath);

        // Zip Slip 対策付きの安全展開を行う。
        ExtractArchiveSafely(ymmpxPath, extractDirectory);

        // links.* / manifest.json を解釈してマップを読み込む。
        var linkMap = LoadLinkMap(extractDirectory);
        var markerPath = Path.Combine(extractDirectory, "_ymmpx_project_path.txt");
        var projectPath = string.Empty;
        if (File.Exists(markerPath))
        {
            var relativeProjectPath = File.ReadAllText(markerPath).Trim();
            if (!string.IsNullOrWhiteSpace(relativeProjectPath))
            {
                var candidate = ResolvePath(extractDirectory, relativeProjectPath);
                if (File.Exists(candidate))
                    projectPath = candidate;
            }
        }

        // 旧形式のフォールバック。
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            var legacyProject = Path.Combine(extractDirectory, "project.ymmp");
            if (File.Exists(legacyProject))
                projectPath = legacyProject;
        }

        // さらに見つからない場合は展開先全体を探索。
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            projectPath = Directory
                .GetFiles(extractDirectory, "*.ymmp", SearchOption.AllDirectories)
                .FirstOrDefault() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(projectPath))
            throw new InvalidDataException("Project file (.ymmp) was not found in the package.");

        var json = File.ReadAllText(projectPath);
        var root = JsonNode.Parse(json);

        if (root is null)
            throw new InvalidDataException("Failed to parse project JSON.");

        // プロジェクト内 FilePath を展開済み実ファイルへ差し替える。
        var replacedCount = YmmpxProjectJson.ReplaceFilePaths(root, linkMap);

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        File.WriteAllText(projectPath, root.ToJsonString(writeOptions));

        // デフォルトでは「アーカイブ名.ymmp」に寄せて扱いやすくする。
        var ymmpxBaseName = Path.GetFileNameWithoutExtension(ymmpxPath);
        if (!string.IsNullOrWhiteSpace(ymmpxBaseName))
        {
            var desiredProjectPath = Path.Combine(extractDirectory, $"{ymmpxBaseName}.ymmp");
            if (!string.Equals(projectPath, desiredProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                var finalProjectPath = GetAvailableFilePath(desiredProjectPath);
                if (File.Exists(finalProjectPath))
                    File.Delete(finalProjectPath);

                File.Move(projectPath, finalProjectPath);
                projectPath = finalProjectPath;
            }
        }

        return new YmmpxUnpackResult(extractDirectory, projectPath, replacedCount, linkMap);
    }

    /// <summary>
    /// パッケージ内リンク定義を読み込み、元パス -> 展開後実パスの対応表を返します。
    /// </summary>
    /// <remarks>
    /// 読み込み順は <c>links.json</c> -> <c>manifest.json</c> -> <c>links.txt</c> です。
    /// </remarks>
    public static Dictionary<string, string> LoadLinkMap(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var linkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 現行形式: links.json
        var linksJsonPath = Path.Combine(baseDirectory, "links.json");
        if (File.Exists(linksJsonPath))
        {
            try
            {
                var jsonMap = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(linksJsonPath));
                if (jsonMap is not null)
                {
                    foreach (var item in jsonMap)
                    {
                        if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
                            continue;
                        if (!TryResolvePathWithinBaseDirectory(baseDirectory, item.Value, out var resolvedPath))
                            continue;
                        linkMap[NormalizeProjectPath(item.Key)] = resolvedPath;
                    }
                    return linkMap;
                }
            }
            catch (JsonException)
            {
                // links.json が壊れている場合は links.txt にフォールバックする。
            }
        }

        // 旧互換形式: manifest.json
        var manifestPath = Path.Combine(baseDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (manifestDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    manifestDoc.RootElement.TryGetProperty("Files", out var filesElement) &&
                    filesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fileEntry in filesElement.EnumerateArray())
                    {
                        if (fileEntry.ValueKind != JsonValueKind.Object)
                            continue;

                        if (!fileEntry.TryGetProperty("OriginalPath", out var originalPathElement) ||
                            originalPathElement.ValueKind != JsonValueKind.String)
                            continue;

                        if (!fileEntry.TryGetProperty("BundlePath", out var bundlePathElement) ||
                            bundlePathElement.ValueKind != JsonValueKind.String)
                            continue;

                        var originalPath = originalPathElement.GetString();
                        var bundlePath = bundlePathElement.GetString();
                        if (string.IsNullOrWhiteSpace(originalPath) || string.IsNullOrWhiteSpace(bundlePath))
                            continue;

                        if (!TryResolvePathWithinBaseDirectory(baseDirectory, bundlePath, out var resolvedPath))
                            continue;
                        linkMap[NormalizeProjectPath(originalPath)] = resolvedPath;
                    }

                    if (linkMap.Count > 0)
                        return linkMap;
                }
            }
            catch (JsonException)
            {
                // manifest.json が壊れている場合は links.txt にフォールバックする。
            }
        }

        // 最終フォールバック: CSV 形式の links.txt
        var linksPath = Path.Combine(baseDirectory, "links.txt");
        if (File.Exists(linksPath))
        {
            foreach (var line in File.ReadAllLines(linksPath))
            {
                var parts = line.Split(',', 2);
                if (parts.Length == 2)
                {
                    var source = parts[0].Trim();
                    var packagedPath = parts[1].Trim();
                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(packagedPath))
                        continue;
                    if (!TryResolvePathWithinBaseDirectory(baseDirectory, packagedPath, out var resolvedPath))
                        continue;
                    linkMap[NormalizeProjectPath(source)] = resolvedPath;
                }
            }
        }

        return linkMap;
    }

    /// <summary>
    /// 既存フォルダと衝突しないディレクトリ名を生成します。
    /// </summary>
    public static string GetAvailableDirectoryPath(string desiredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredPath);

        var candidate = desiredPath;
        var suffix = 1;
        while (Directory.Exists(candidate))
        {
            candidate = $"{desiredPath}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// 既存ファイルと衝突しないファイルパスを生成します。
    /// </summary>
    public static string GetAvailableFilePath(string desiredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredPath);

        if (!File.Exists(desiredPath))
            return desiredPath;

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);

        var suffix = 1;
        var candidate = desiredPath;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{fileNameWithoutExtension}_{suffix}{extension}");
            suffix++;
        }

        return candidate;
    }

    // 相対/絶対どちらでも絶対パスへ変換する。
    private static string ResolvePath(string baseDirectory, string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
            return Path.GetFullPath(relativeOrAbsolutePath);

        return Path.GetFullPath(Path.Combine(baseDirectory, relativeOrAbsolutePath));
    }

    private static void ExtractArchiveSafely(string ymmpxPath, string extractDirectory)
    {
        Directory.CreateDirectory(extractDirectory);

        // Zip Slip 対策として展開先ベースの prefix を固定する。
        var baseDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(extractDirectory));

        using var archive = ZipFile.OpenRead(ymmpxPath);
        foreach (var entry in archive.Entries)
        {
            // エントリの最終展開先を解決し、ベース配下か検証する。
            var destinationPath = Path.GetFullPath(Path.Combine(extractDirectory, entry.FullName));
            if (!destinationPath.StartsWith(baseDirectory, GetPathComparison()))
                throw new InvalidDataException($"Entry path escapes extraction directory: {entry.FullName}");

            // Name が空ならディレクトリエントリ。
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            entry.ExtractToFile(destinationPath, overwrite: false);
        }
    }

    private static bool TryResolvePathWithinBaseDirectory(string baseDirectory, string relativePath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        // マニフェスト上は相対パスのみ許可し、絶対パス注入を拒否する。
        if (Path.IsPathRooted(relativePath))
            return false;

        var candidate = ResolvePath(baseDirectory, relativePath);
        var normalizedBaseDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (!candidate.StartsWith(normalizedBaseDirectory, GetPathComparison()))
            return false;

        resolvedPath = candidate;
        return true;
    }

    // 文字列比較時の誤一致を防ぐため、末尾区切りを必ず付与する。
    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return path;
        return path + Path.DirectorySeparatorChar;
    }

    // OS ごとのパス比較ルールを統一する。
    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static string NormalizePath(string baseDirectory, string path)
    {
        // 環境変数と引用符を展開/除去して入力ゆれを吸収する。
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        // file:// URI はローカルパスへ戻す。
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            return Path.GetFullPath(uri.LocalPath);

        // 相対パスはプロジェクト基準で解決する。
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static string NormalizeProjectPath(string path)
    {
        // プロジェクト JSON 内では OS 差分を避けるため区切りを '/' に揃える。
        return path.Replace('\\', '/');
    }

    private static string GetUniqueFileName(string fileName, ISet<string> usedNames)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        var suffix = 1;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName}_{suffix}{extension}";
            suffix++;
        }

        return candidate;
    }
    private static async Task CopyFileToEntryWithProgressAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        Action<int> onBytesWritten,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 131072;

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            useAsync: true);
        await using var destinationStream = entry.Open();

        var buffer = new byte[bufferSize];
        while (true)
        {
            var read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            onBytesWritten(read);
        }
    }

    private sealed class ProgressReportGate
    {
        private const long ReportBytesThreshold = 128 * 1024;
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(40);

        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private long lastReportedBytes;
        private TimeSpan lastReportedTime = TimeSpan.Zero;

        public bool ShouldReport(long currentBytes)
        {
            var elapsed = stopwatch.Elapsed;
            var bytesDelta = currentBytes - lastReportedBytes;
            var timeDelta = elapsed - lastReportedTime;
            if (bytesDelta < ReportBytesThreshold && timeDelta < ReportInterval)
                return false;

            lastReportedBytes = currentBytes;
            lastReportedTime = elapsed;
            return true;
        }
    }
}
