using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YmmpxLibV2;

/// <summary>Describes one v2 package write operation.</summary>
public sealed record YmmpxV2WriteRequest(string ProjectPath, string OutputPath, bool Overwrite = false);

/// <summary>Creates YMMPX Format 2.0 packages without modifying the source project.</summary>
public static class YmmpxV2Writer
{
    /// <summary>Creates a v2 package containing the project, descriptor, manifest, and referenced local resources.</summary>
    public static async Task WriteAsync(YmmpxV2WriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectPath = Path.GetFullPath(request.ProjectPath);
        var outputPath = Path.GetFullPath(request.OutputPath);
        if (!File.Exists(projectPath)) throw new FileNotFoundException("Project file was not found.", projectPath);
        if (File.Exists(outputPath) && !request.Overwrite) throw new IOException($"Output already exists: {outputPath}");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var raw = await File.ReadAllTextAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var root = JsonNode.Parse(raw) ?? throw new InvalidDataException("Project JSON is empty.");
        var sources = CollectResources(root, projectDirectory, cancellationToken);
        var entries = await CreateEntriesAsync(sources, cancellationToken).ConfigureAwait(false);
        ReplaceFilePaths(root, entries, projectDirectory);
        var manifest = new PackageManifest(entries.Select(entry => entry.Manifest));
        var temporary = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                await WriteTextAsync(archive, YmmpxFormatDescriptor.FileName,
                    YmmpxFormatDescriptorSerializer.Serialize(new YmmpxFormatDescriptor(2, 0, PackageManifest.FileName)), cancellationToken).ConfigureAwait(false);
                await WriteTextAsync(archive, PackageManifest.FileName, PackageManifestSerializer.Serialize(manifest), cancellationToken).ConfigureAwait(false);
                await WriteTextAsync(archive, "project.ymmp", root.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), cancellationToken).ConfigureAwait(false);
                foreach (var entry in entries.OrderBy(entry => entry.PackagePath, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var zipEntry = archive.CreateEntry(entry.PackagePath, CompressionLevel.Optimal);
                    await using var input = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
                    await using var output = zipEntry.Open();
                    await input.CopyToAsync(output, 1024 * 128, cancellationToken).ConfigureAwait(false);
                }
            }
            File.Move(temporary, outputPath, request.Overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static List<SourceReference> CollectResources(JsonNode root, string projectDirectory, CancellationToken cancellationToken)
    {
        var values = new List<SourceReference>();
        Collect(root, projectDirectory, values, cancellationToken, isVideoItem: false);
        return values;
    }

    private static void Collect(JsonNode? node, string baseDirectory, List<SourceReference> values, CancellationToken token, bool isVideoItem)
    {
        token.ThrowIfCancellationRequested();
        if (node is JsonObject obj)
        {
            var type = obj["$type"]?.GetValue<string>();
            var video = type?.StartsWith("YukkuriMovieMaker.Project.Items.VideoItem,", StringComparison.Ordinal) == true;
            foreach (var property in obj)
            {
                if (property.Key.Equals("FilePath", StringComparison.OrdinalIgnoreCase) && property.Value is JsonValue value && value.TryGetValue<string>(out var reference) && !string.IsNullOrWhiteSpace(reference))
                {
                    var source = ResolveExisting(baseDirectory, reference);
                    if (source is not null) values.Add(new SourceReference(reference, source, video && Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase)));
                }
                else Collect(property.Value, baseDirectory, values, token, video);
            }
        }
        else if (node is JsonArray array) foreach (var child in array) Collect(child, baseDirectory, values, token, isVideoItem);
    }

    private static string? ResolveExisting(string baseDirectory, string reference)
    {
        try { var path = Path.GetFullPath(Path.IsPathRooted(reference) ? reference : Path.Combine(baseDirectory, reference)); return File.Exists(path) ? path : null; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static async Task<List<PackageEntry>> CreateEntriesAsync(List<SourceReference> references, CancellationToken token)
    {
        var sourceToEntry = new Dictionary<string, PackageEntry>(ProjectResourceReferenceMapper.GetPathComparer());
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sequenceIndex = 0;
        foreach (var reference in references)
        {
            token.ThrowIfCancellationRequested();
            if (sourceToEntry.ContainsKey(reference.SourcePath)) continue;
            if (reference.IsVideoPng)
            {
                var frames = FindSequence(reference.SourcePath);
                if (frames.Count >= 2)
                {
                    var group = $"sequence_{++sequenceIndex}";
                    foreach (var frame in frames)
                    {
                        if (sourceToEntry.ContainsKey(frame)) continue;
                        var path = $"resources/{group}/{Path.GetFileName(frame)}";
                        sourceToEntry[frame] = await CreateEntryAsync(frame, path, ManifestResourceKind.ImageSequence, group, token).ConfigureAwait(false);
                    }
                    continue;
                }
            }
            var fileName = UniqueName(Path.GetFileName(reference.SourcePath), names);
            sourceToEntry[reference.SourcePath] = await CreateEntryAsync(reference.SourcePath, $"resources/{fileName}", DetectKind(reference.SourcePath), null, token).ConfigureAwait(false);
        }
        return sourceToEntry.Values.ToList();
    }

    private static async Task<PackageEntry> CreateEntryAsync(string source, string packagePath, ManifestResourceKind kind, string? group, CancellationToken token)
    {
        var identity = await ResourceIdentity.CreateAsync(source, token).ConfigureAwait(false);
        return new PackageEntry(source, packagePath, new PackageManifestResource(null, identity.FileName, identity.Length, identity.Sha256, packagePath, kind, group));
    }

    private static List<string> FindSequence(string source)
    {
        var directory = Path.GetDirectoryName(source)!; var name = Path.GetFileNameWithoutExtension(source);
        var match = System.Text.RegularExpressions.Regex.Match(name, "^(.*?)(\\d+)$");
        if (!match.Success) return [];
        var prefix = match.Groups[1].Value;
        return Directory.EnumerateFiles(directory, "*.png").Where(path => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path), $"^{System.Text.RegularExpressions.Regex.Escape(prefix)}\\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string UniqueName(string name, ISet<string> used)
    {
        var baseName = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name); var candidate = name; var index = 2;
        while (!used.Add(candidate)) candidate = $"{baseName}_{index++}{ext}";
        return candidate;
    }
    private static ManifestResourceKind DetectKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".png" or ".jpg" or ".jpeg" => ManifestResourceKind.Image, ".wav" or ".mp3" => ManifestResourceKind.Audio, ".mp4" or ".avi" => ManifestResourceKind.Video, ".psd" => ManifestResourceKind.Psd, _ => ManifestResourceKind.File };
    private static void ReplaceFilePaths(JsonNode node, IReadOnlyList<PackageEntry> entries, string projectDirectory)
    {
        var map = entries.ToDictionary(entry => entry.SourcePath, entry => entry.PackagePath, ProjectResourceReferenceMapper.GetPathComparer());
        Replace(node, map, projectDirectory);
    }
    private static void Replace(JsonNode? node, IReadOnlyDictionary<string,string> map, string projectDirectory)
    {
        if (node is JsonObject obj) foreach (var property in obj.ToList())
        {
            if (property.Key.Equals("FilePath", StringComparison.OrdinalIgnoreCase) && property.Value is JsonValue value && value.TryGetValue<string>(out var reference))
            {
                var source = ResolveExisting(projectDirectory, reference);
                if (source is not null && map.TryGetValue(source, out var packagePath)) obj[property.Key] = packagePath;
            }
            else Replace(property.Value, map, projectDirectory);
        }
        else if (node is JsonArray array) foreach (var child in array) Replace(child, map, projectDirectory);
    }
    private static async Task WriteTextAsync(ZipArchive archive, string path, string text, CancellationToken token)
    { var entry = archive.CreateEntry(path); await using var stream = entry.Open(); await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, false); await writer.WriteAsync(text.AsMemory(), token).ConfigureAwait(false); }
    private sealed record SourceReference(string OriginalReference, string SourcePath, bool IsVideoPng);
    private sealed record PackageEntry(string SourcePath, string PackagePath, PackageManifestResource Manifest);
}

/// <summary>Reads supported Format 2.0 packages into the common package session.</summary>
public static class YmmpxV2Reader
{
    /// <summary>Opens a supported v2 package without extracting it.</summary>
    public static async Task<YmmpxPackageSession> OpenAsync(Stream package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var detection = await YmmpxFormatDetector.DetectAsync(package, cancellationToken).ConfigureAwait(false);
        if (detection.Status != YmmpxFormatDetectionStatus.SupportedV2) throw new InvalidDataException("The package is not supported YMMPX Format 2.0.");
        package.Seek(0, SeekOrigin.Begin); ZipArchive? archive = null;
        try
        {
            archive = new ZipArchive(package, ZipArchiveMode.Read, true);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal)))
            { var path = PackagePathValidator.NormalizeRelativePath(entry.FullName, "entryPath"); if (!entries.TryAdd(path, entry)) throw new InvalidDataException($"Duplicate entry: {path}"); }
            var manifestEntry = entries.TryGetValue(PackageManifest.FileName, out var value) ? value : throw new InvalidDataException("Manifest is missing.");
            var projectEntry = entries.TryGetValue("project.ymmp", out value) ? value : throw new InvalidDataException("Project is missing.");
            var manifest = PackageManifestSerializer.Deserialize(await ReadTextAsync(manifestEntry, 64L * 1024 * 1024, cancellationToken).ConfigureAwait(false));
            var project = await ReadTextAsync(projectEntry, LegacyV1Reader.MaxProjectLength, cancellationToken).ConfigureAwait(false);
            foreach (var resource in manifest.Resources) if (!entries.TryGetValue(resource.PackagePath, out var zip) || zip.Length != resource.Length) throw new InvalidDataException($"Manifest resource is missing or mismatched: {resource.PackagePath}");
            var loaded = new LoadedYmmpxPackage(LoadedYmmpxSourceFormat.V2, new LoadedYmmpxProject("project.ymmp", project), manifest.Resources.Select(resource => new LoadedYmmpxResource(resource.PackagePath, resource.FileName, resource.Length, resource.Kind, resource.GroupId)).ToArray(), [])
            { ProjectReferences = manifest.Resources.Select(resource => new ProjectResourceReference(resource.PackagePath, resource.PackagePath)).ToArray() };
            var session = new YmmpxPackageSession(loaded, archive, entries); archive = null; return session;
        }
        finally { archive?.Dispose(); }
    }
    private static async Task<string> ReadTextAsync(ZipArchiveEntry entry, long limit, CancellationToken token)
    { if(entry.Length>limit) throw new InvalidDataException("Metadata is too large."); await using var stream=entry.Open(); using var reader=new StreamReader(stream, new UTF8Encoding(false,true), true); return await reader.ReadToEndAsync(token).ConfigureAwait(false); }
}
