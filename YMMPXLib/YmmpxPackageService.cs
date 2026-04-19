using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YmmpxLib;

public static class YmmpxPackageService
{
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

        var normalizedProjectPath = Path.GetFullPath(projectFilePath);
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)
            ?? throw new DirectoryNotFoundException($"Project directory was not found: {projectFilePath}");

        var excluded = excludedFiles is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : excludedFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizePath(projectDirectory, path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projectText = await File.ReadAllTextAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
        options ??= new YmmpxPackagingOptions();

        var projectTextForPackage = projectText;
        if (!options.IncludeProjectUiSettings)
        {
            var projectNode = JsonNode.Parse(projectTextForPackage);
            if (projectNode is not null)
            {
                YmmpxProjectJson.RemoveUiSettings(projectNode);
                projectTextForPackage = projectNode.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
        }

        using var document = JsonDocument.Parse(projectText);
        var resources = YmmpxProjectJson
            .FindFilePaths(document.RootElement)
            .Select(path => NormalizePath(projectDirectory, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path =>
                File.Exists(path) &&
                !excluded.Contains(path) &&
                !string.Equals(path, normalizedProjectPath, GetPathComparison()))
            .ToList();

        progress?.Report(new YmmpxPackagingProgress(0, resources.Count, "Collecting resources"));

        var tempDir = Path.Combine(Path.GetTempPath(), "YmmpxLib", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var linksFile = Path.Combine(tempDir, "links.txt");
            var linksJsonFile = Path.Combine(tempDir, "links.json");

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using (var writer = new StreamWriter(linksFile, false))
            {
                for (var i = 0; i < resources.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourcePath = resources[i];
                    var originalName = Path.GetFileName(sourcePath);
                    var uniqueName = originalName;
                    var suffix = 1;

                    while (usedNames.Contains(uniqueName))
                    {
                        uniqueName =
                            $"{Path.GetFileNameWithoutExtension(originalName)}_{suffix++}{Path.GetExtension(originalName)}";
                    }

                    usedNames.Add(uniqueName);

                    var zipPath = $"resources/{uniqueName}";
                    fileMap[sourcePath] = zipPath;

                    await writer.WriteLineAsync($"{sourcePath},{zipPath}").ConfigureAwait(false);
                    progress?.Report(new YmmpxPackagingProgress(i + 1, resources.Count, "Building package"));
                }
            }

            await File.WriteAllTextAsync(
                linksJsonFile,
                JsonSerializer.Serialize(fileMap, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);

            var projectEntryName = Path.GetFileName(projectFilePath);
            if (string.IsNullOrWhiteSpace(projectEntryName))
                projectEntryName = "project.ymmp";
            if (!projectEntryName.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase))
                projectEntryName = Path.ChangeExtension(projectEntryName, ".ymmp");

            using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                var projectEntry = zip.CreateEntry(projectEntryName);
                await using (var projectStream = projectEntry.Open())
                await using (var projectWriter = new StreamWriter(projectStream))
                {
                    await projectWriter.WriteAsync(projectTextForPackage).ConfigureAwait(false);
                }

                var markerEntry = zip.CreateEntry("_ymmpx_project_path.txt");
                await using (var markerStream = markerEntry.Open())
                await using (var markerWriter = new StreamWriter(markerStream))
                {
                    await markerWriter.WriteAsync(projectEntryName).ConfigureAwait(false);
                }

                zip.CreateEntryFromFile(linksFile, "links.txt");
                zip.CreateEntryFromFile(linksJsonFile, "links.json");

                foreach (var (source, destination) in fileMap)
                    zip.CreateEntryFromFile(source, destination);
            }

            return new YmmpxPackagingResult(outputPath, resources.Count, fileMap);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    public static YmmpxUnpackResult ExtractAndRestoreProject(string ymmpxPath, string extractDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ymmpxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractDirectory);

        if (!File.Exists(ymmpxPath))
            throw new FileNotFoundException("Ymmpx file was not found.", ymmpxPath);

        ExtractArchiveSafely(ymmpxPath, extractDirectory);

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

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            var legacyProject = Path.Combine(extractDirectory, "project.ymmp");
            if (File.Exists(legacyProject))
                projectPath = legacyProject;
        }

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

        var replacedCount = YmmpxProjectJson.ReplaceFilePaths(root, linkMap);

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        File.WriteAllText(projectPath, root.ToJsonString(writeOptions));

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

    public static Dictionary<string, string> LoadLinkMap(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var linkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                        linkMap[item.Key] = resolvedPath;
                    }
                    return linkMap;
                }
            }
            catch (JsonException)
            {
                // Fallback to links.txt when links.json is malformed.
            }
        }

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

                        linkMap[Path.GetFullPath(originalPath)] = resolvedPath;
                    }

                    if (linkMap.Count > 0)
                        return linkMap;
                }
            }
            catch (JsonException)
            {
                // Fallback to links.txt when manifest.json is malformed.
            }
        }

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
                    linkMap[source] = resolvedPath;
                }
            }
        }

        return linkMap;
    }

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

    private static string ResolvePath(string baseDirectory, string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
            return Path.GetFullPath(relativeOrAbsolutePath);

        return Path.GetFullPath(Path.Combine(baseDirectory, relativeOrAbsolutePath));
    }

    private static void ExtractArchiveSafely(string ymmpxPath, string extractDirectory)
    {
        Directory.CreateDirectory(extractDirectory);

        var baseDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(extractDirectory));

        using var archive = ZipFile.OpenRead(ymmpxPath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(extractDirectory, entry.FullName));
            if (!destinationPath.StartsWith(baseDirectory, GetPathComparison()))
                throw new InvalidDataException($"Entry path escapes extraction directory: {entry.FullName}");

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
        if (Path.IsPathRooted(relativePath))
            return false;

        var candidate = ResolvePath(baseDirectory, relativePath);
        var normalizedBaseDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (!candidate.StartsWith(normalizedBaseDirectory, GetPathComparison()))
            return false;

        resolvedPath = candidate;
        return true;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return path;
        return path + Path.DirectorySeparatorChar;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static string NormalizePath(string baseDirectory, string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            return Path.GetFullPath(uri.LocalPath);

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}
