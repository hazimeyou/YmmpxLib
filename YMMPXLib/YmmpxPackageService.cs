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
    private const int MaxArchiveEntryCount = 10_000;
    private const long MaxArchiveEntryLength = 50L * 1024 * 1024 * 1024;
    private const long MaxArchiveTotalLength = 100L * 1024 * 1024 * 1024;
    private const long MaxProjectFileLength = 512L * 1024 * 1024;
    private const long MaxLinkFileLength = 64L * 1024 * 1024;
    private const long MaxMarkerFileLength = 16L * 1024;
    private const long CompressionRatioCheckThreshold = 100L * 1024 * 1024;
    private const long MaxCompressionRatio = 1_000;

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
        var normalizedOutputPath = Path.GetFullPath(outputPath);
        if (string.Equals(normalizedProjectPath, normalizedOutputPath, GetPathComparison()))
            throw new ArgumentException("Output path must be different from the project file path.", nameof(outputPath));

        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)
            ?? throw new DirectoryNotFoundException($"Project directory was not found: {projectFilePath}");

        // 除外対象を絶対パスに正規化して比較可能にする。
        var excluded = excludedFiles is null
            ? new HashSet<string>(GetPathComparer())
            : excludedFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizePath(projectDirectory, path))
                .ToHashSet(GetPathComparer());

        var projectText = await File.ReadAllTextAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
        options ??= new YmmpxPackagingOptions();

        // 元 JSON を解析し、参照ファイル (FilePath) の一覧を作る。
        using var document = JsonDocument.Parse(projectText);
        var resourceEntries = YmmpxProjectJson
            .FindFilePaths(document.RootElement)
            .Select(originalPath => new
            {
                OriginalPath = originalPath,
                ResolvedPath = NormalizePath(projectDirectory, originalPath),
            })
            .Where(x =>
                File.Exists(x.ResolvedPath) &&
                !excluded.Contains(x.ResolvedPath) &&
                !string.Equals(x.ResolvedPath, normalizedProjectPath, GetPathComparison()))
            .GroupBy(x => x.ResolvedPath, GetPathComparer())
            .Select(group => group.First())
            .ToList();

        if (resourceEntries.Any(x => string.Equals(x.ResolvedPath, normalizedOutputPath, GetPathComparison())))
            throw new ArgumentException("Output path must be different from every packaged resource path.", nameof(outputPath));

        // 保存名 (ファイル名ベース + 連番) の対応表を先に作り、JSON 書き換えと links.json で共用する。
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagedNameByResolvedPath = new Dictionary<string, string>(GetPathComparer());
        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filesToPackage = new Dictionary<string, string>(GetPathComparer());
        foreach (var entry in resourceEntries)
        {
            var fileName = Path.GetFileName(entry.ResolvedPath);
            var uniqueFileName = GetUniqueFileName(fileName, usedNames);
            var packagedPath = $"resources/{uniqueFileName}";
            packagedNameByResolvedPath[entry.ResolvedPath] = uniqueFileName;
            fileMap[uniqueFileName] = packagedPath;
            filesToPackage[entry.ResolvedPath] = packagedPath;
        }

        // 必要に応じて UI 状態除外と FilePath のファイル名化を行った JSON をパッケージ化する。
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
        var outputDirectory = Path.GetDirectoryName(normalizedOutputPath)
            ?? throw new DirectoryNotFoundException($"Output directory was not found: {outputPath}");
        var temporaryOutputPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(normalizedOutputPath)}.{Guid.NewGuid():N}.tmp");

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

            // 完成するまで一時ファイルへ書き込み、既存出力を失わないようにする。
            using (var zip = ZipFile.Open(temporaryOutputPath, ZipArchiveMode.Create))
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
                // 旧バージョン互換性は同梱時不要。展開時のみ互換性を保つ。
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

            File.Move(temporaryOutputPath, normalizedOutputPath, overwrite: true);
            completedCount = totalCount;
            processedBytes = totalBytes > 0 ? totalBytes : 0;
            ReportProgress("Completed", force: true, isCompleted: true);

            return new YmmpxPackagingResult(outputPath, filesToPackage.Count, fileMap);
        }
        finally
        {
            if (File.Exists(temporaryOutputPath))
                File.Delete(temporaryOutputPath);

            // 一時フォルダは成功・失敗にかかわらず必ず削除する。
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
        var extractedFiles = ExtractArchiveSafely(ymmpxPath, extractDirectory);

        var cleanupPaths = new HashSet<string>(extractedFiles, GetPathComparer());
        try
        {
            // 今回展開した links.* / manifest.json のみを解釈してマップを読み込む。
            var linkMap = LoadLinkMap(extractDirectory, extractedFiles);
            var markerPath = Path.GetFullPath(Path.Combine(extractDirectory, "_ymmpx_project_path.txt"));
            var projectPath = string.Empty;
            if (extractedFiles.Contains(markerPath))
            {
                var relativeProjectPath = File.ReadAllText(markerPath).Trim();
                if (!string.IsNullOrWhiteSpace(relativeProjectPath))
                {
                    if (TryResolvePathWithinBaseDirectory(extractDirectory, relativeProjectPath, out var candidate) &&
                        candidate.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase) &&
                        extractedFiles.Contains(candidate))
                        projectPath = candidate;
                }
            }

            // 旧形式のフォールバック。
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                var legacyProject = Path.Combine(extractDirectory, "project.ymmp");
                if (extractedFiles.Contains(Path.GetFullPath(legacyProject)))
                    projectPath = legacyProject;
            }

            // さらに見つからない場合は今回展開したファイルのみを探索。
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                var projectCandidates = extractedFiles
                    .Where(path => path.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToArray();
                if (projectCandidates.Length > 1)
                    throw new InvalidDataException("Multiple project files (.ymmp) were found in the package.");

                projectPath = projectCandidates.FirstOrDefault() ?? string.Empty;
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
                    File.Move(projectPath, finalProjectPath);
                    projectPath = finalProjectPath;
                    cleanupPaths.Add(finalProjectPath);
                }
            }

            return new YmmpxUnpackResult(extractDirectory, projectPath, replacedCount, linkMap);
        }
        catch
        {
            CleanupExtractedFiles(cleanupPaths);
            throw;
        }
    }

    /// <summary>
    /// パッケージ内リンク定義を読み込み、元パスから展開後実パスへの対応表を返します。
    /// </summary>
    /// <remarks>
    /// 読み込み順は <c>links.json</c>、<c>manifest.json</c>、<c>links.txt</c> です。
    /// </remarks>
    public static Dictionary<string, string> LoadLinkMap(string baseDirectory)
    {
        return LoadLinkMap(baseDirectory, archiveFiles: null);
    }

    private static Dictionary<string, string> LoadLinkMap(
        string baseDirectory,
        IReadOnlySet<string>? archiveFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var linkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 現行形式: links.json
        var linksJsonPath = Path.GetFullPath(Path.Combine(baseDirectory, "links.json"));
        if (IsAllowedArchiveFile(linksJsonPath, archiveFiles))
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
                        if (!TryResolvePathWithinBaseDirectory(baseDirectory, item.Value, out var resolvedPath) ||
                            !IsAllowedArchiveFile(resolvedPath, archiveFiles))
                            continue;
                        linkMap[NormalizeProjectPath(item.Key)] = resolvedPath;
                    }

                    if (linkMap.Count > 0)
                        return linkMap;
                }
            }
            catch (JsonException)
            {
                // links.json が壊れている場合は次の形式にフォールバックする。
            }
        }

        // 旧互換形式: manifest.json
        var manifestPath = Path.GetFullPath(Path.Combine(baseDirectory, "manifest.json"));
        if (IsAllowedArchiveFile(manifestPath, archiveFiles))
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

                        if (!TryResolvePathWithinBaseDirectory(baseDirectory, bundlePath, out var resolvedPath) ||
                            !IsAllowedArchiveFile(resolvedPath, archiveFiles))
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
        var linksPath = Path.GetFullPath(Path.Combine(baseDirectory, "links.txt"));
        if (IsAllowedArchiveFile(linksPath, archiveFiles))
        {
            foreach (var line in File.ReadAllLines(linksPath))
            {
                if (TryParseLegacyLinksLine(baseDirectory, line, archiveFiles, out var source, out var resolvedPath))
                    linkMap[NormalizeProjectPath(source)] = resolvedPath;
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

    // 相対・絶対どちらでも絶対パスへ変換する。
    private static string ResolvePath(string baseDirectory, string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
            return Path.GetFullPath(relativeOrAbsolutePath);

        return Path.GetFullPath(Path.Combine(baseDirectory, relativeOrAbsolutePath));
    }

    private static IReadOnlySet<string> ExtractArchiveSafely(string ymmpxPath, string extractDirectory)
    {
        // Zip Slip 対策として展開先ベースの prefix を固定する。
        var baseDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(extractDirectory));

        using var archive = ZipFile.OpenRead(ymmpxPath);
        var extractedFiles = new HashSet<string>(GetPathComparer());

        try
        {
            ValidateArchiveEntries(archive, baseDirectory);
            EnsureDirectoryPathIsSafe(extractDirectory, baseDirectory);
            Directory.CreateDirectory(extractDirectory);

            long extractedTotalLength = 0;
            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.GetFullPath(Path.Combine(extractDirectory, entry.FullName));
                if (string.IsNullOrEmpty(entry.Name))
                {
                    EnsureDirectoryPathIsSafe(destinationPath, baseDirectory);
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    EnsureDirectoryPathIsSafe(destinationDirectory, baseDirectory);
                    Directory.CreateDirectory(destinationDirectory);
                }

                ExtractEntrySafely(entry, destinationPath, ref extractedTotalLength);
                extractedFiles.Add(destinationPath);
            }

            return extractedFiles;
        }
        catch
        {
            CleanupExtractedFiles(extractedFiles);
            throw;
        }
    }

    private static void ValidateArchiveEntries(ZipArchive archive, string baseDirectory)
    {
        if (archive.Entries.Count > MaxArchiveEntryCount)
            throw new InvalidDataException($"Archive contains too many entries: {archive.Entries.Count}.");

        long totalLength = 0;
        long totalCompressedLength = 0;
        var destinationPaths = new HashSet<string>(GetPathComparer());
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(baseDirectory, entry.FullName));
            if (!destinationPath.StartsWith(baseDirectory, GetPathComparison()))
                throw new InvalidDataException($"Entry path escapes extraction directory: {entry.FullName}");
            if (!destinationPaths.Add(destinationPath))
                throw new InvalidDataException($"Archive contains duplicate destination paths: {entry.FullName}");

            if (entry.Length > GetArchiveEntryLengthLimit(entry))
                throw new InvalidDataException($"Archive entry is too large: {entry.FullName}.");

            totalLength = checked(totalLength + entry.Length);
            if (totalLength > MaxArchiveTotalLength)
                throw new InvalidDataException("Archive expands beyond the allowed total size.");

            totalCompressedLength = checked(totalCompressedLength + entry.CompressedLength);
            ValidateCompressionRatio(entry.FullName, entry.Length, entry.CompressedLength);
        }

        ValidateCompressionRatio("archive", totalLength, totalCompressedLength);
    }

    private static void ExtractEntrySafely(ZipArchiveEntry entry, string destinationPath, ref long extractedTotalLength)
    {
        const int bufferSize = 131072;
        var entryLengthLimit = GetArchiveEntryLengthLimit(entry);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new DirectoryNotFoundException($"Destination directory was not found: {destinationPath}");
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        long extractedEntryLength = 0;
        try
        {
            using var source = entry.Open();
            using var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize);
            var buffer = new byte[bufferSize];
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;

                extractedEntryLength = checked(extractedEntryLength + read);
                extractedTotalLength = checked(extractedTotalLength + read);
                if (extractedEntryLength > entryLengthLimit)
                    throw new InvalidDataException($"Archive entry expands beyond the allowed size: {entry.FullName}.");
                if (extractedTotalLength > MaxArchiveTotalLength)
                    throw new InvalidDataException("Archive expands beyond the allowed total size.");

                destination.Write(buffer, 0, read);
            }

            destination.Close();
            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    private static void EnsureDirectoryPathIsSafe(string path, string baseDirectory)
    {
        var currentPath = Path.GetFullPath(path);
        var normalizedBaseDirectory = Path.GetFullPath(baseDirectory);

        while (true)
        {
            if (Directory.Exists(currentPath))
            {
                var attributes = File.GetAttributes(currentPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Path traverses a reparse point: {currentPath}");
            }

            if (string.Equals(currentPath, normalizedBaseDirectory, GetPathComparison()))
                return;

            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath))
                return;

            currentPath = parentPath;
        }
    }

    private static void CleanupExtractedFiles(IEnumerable<string> extractedFiles)
    {
        foreach (var path in extractedFiles.OrderByDescending(path => path.Length))
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 複数の後始末失敗で元の例外を潰さない。
            }
        }
    }
    private static bool TryResolvePathWithinBaseDirectory(string baseDirectory, string relativePath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        // マニフェスト上の相対パスのみ許可し、絶対パス注入を拒否する。
        if (Path.IsPathRooted(relativePath))
            return false;

        var candidate = ResolvePath(baseDirectory, relativePath);
        var normalizedBaseDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (!candidate.StartsWith(normalizedBaseDirectory, GetPathComparison()))
            return false;

        resolvedPath = candidate;
        return true;
    }

    private static bool TryParseLegacyLinksLine(
        string baseDirectory,
        string line,
        IReadOnlySet<string>? archiveFiles,
        out string source,
        out string resolvedPath)
    {
        source = string.Empty;
        resolvedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        // 旧 links.txt は source,bundlePath 形式。source 側に ',' を含むケースを考慮し、
        // 後ろから区切り位置を探索して実在する bundlePath を優先する。
        string? fallbackSource = null;
        string? fallbackResolvedPath = null;
        for (var index = line.Length - 1; index >= 0; index--)
        {
            if (line[index] != ',')
                continue;

            var left = line[..index].Trim();
            var right = line[(index + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                continue;

            if (!TryResolvePathWithinBaseDirectory(baseDirectory, right, out var candidatePath))
                continue;

            if (IsAllowedArchiveFile(candidatePath, archiveFiles))
            {
                source = left;
                resolvedPath = candidatePath;
                return true;
            }

            if (archiveFiles is null)
            {
                fallbackSource ??= left;
                fallbackResolvedPath ??= candidatePath;
            }
        }

        if (fallbackSource is null || fallbackResolvedPath is null)
            return false;

        source = fallbackSource;
        resolvedPath = fallbackResolvedPath;
        return true;
    }

    private static bool IsAllowedArchiveFile(string path, IReadOnlySet<string>? archiveFiles)
    {
        return archiveFiles?.Contains(Path.GetFullPath(path)) ?? File.Exists(path);
    }

    private static void ValidateCompressionRatio(string entryName, long length, long compressedLength)
    {
        if (length <= CompressionRatioCheckThreshold)
            return;

        if (compressedLength <= 0 || length / compressedLength > MaxCompressionRatio)
            throw new InvalidDataException($"Archive entry has an excessive compression ratio: {entryName}.");
    }

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

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static long GetArchiveEntryLengthLimit(ZipArchiveEntry entry)
    {
        if (entry.FullName.Equals("_ymmpx_project_path.txt", StringComparison.OrdinalIgnoreCase))
            return MaxMarkerFileLength;
        if (entry.FullName.Equals("links.json", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.Equals("links.txt", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return MaxLinkFileLength;
        }
        if (entry.FullName.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase))
            return MaxProjectFileLength;

        return MaxArchiveEntryLength;
    }

    private static string NormalizePath(string baseDirectory, string path)
    {
        // 環境変数と引用符を展開・除去して入力揺れを吸収する。
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
        // プロジェクト JSON 内では OS 差異を避けるため区切りを '/' に揃える。
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
