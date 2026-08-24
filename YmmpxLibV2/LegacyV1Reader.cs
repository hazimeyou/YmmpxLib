using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace YmmpxLibV2;

/// <summary>
/// Reads the known v1 YMMPX package variants into the v2 common package representation.
/// </summary>
public static class LegacyV1Reader
{
    /// <summary>Gets the maximum v1 project metadata length accepted by the reader.</summary>
    public const long MaxProjectLength = 512L * 1024 * 1024;

    /// <summary>Gets the maximum v1 link metadata length accepted by the reader.</summary>
    public const long MaxLinkMetadataLength = 64L * 1024 * 1024;

    /// <summary>Gets the maximum v1 project marker length accepted by the reader.</summary>
    public const long MaxProjectMarkerLength = 16L * 1024;

    private const int MaxArchiveEntryCount = 10_000;
    private const string ProjectMarkerPath = "_ymmpx_project_path.txt";

    /// <summary>
    /// Reads a detector-confirmed v1 package without extracting files or changing the input stream.
    /// The caller retains ownership of <paramref name="package"/>.
    /// </summary>
    public static async Task<LoadedYmmpxPackage> ReadAsync(Stream package, CancellationToken cancellationToken = default)
    {
        await using var session = await OpenAsync(package, cancellationToken).ConfigureAwait(false);
        return session.Package;
    }

    /// <summary>
    /// Opens a read-only package session. Dispose the returned session after every resource stream is disposed.
    /// </summary>
    public static async Task<YmmpxPackageSession> OpenAsync(Stream package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!package.CanRead || !package.CanSeek)
            throw new ArgumentException("Package stream must be readable and seekable.", nameof(package));

        var detection = await YmmpxFormatDetector.DetectAsync(package, cancellationToken).ConfigureAwait(false);
        if (detection.Status != YmmpxFormatDetectionStatus.LegacyV1)
            throw new LegacyV1ReadException(LegacyV1ReadError.NotLegacyV1, "The package is not a recognized LegacyV1 YMMPX package.");

        ZipArchive? archive = null;
        try
        {
            package.Seek(0, SeekOrigin.Begin);
            archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            var entries = ValidateAndIndexEntries(archive);
            var projectEntry = await FindProjectEntryAsync(entries, cancellationToken).ConfigureAwait(false);
            var projectText = await ReadTextEntryAsync(projectEntry, MaxProjectLength, cancellationToken, preserveContent: true).ConfigureAwait(false);
            var links = await ReadLinksAsync(entries, cancellationToken).ConfigureAwait(false);
            var resources = CreateResources(entries, links);

            var loadedPackage = new LoadedYmmpxPackage(
                LoadedYmmpxSourceFormat.LegacyV1,
                new LoadedYmmpxProject(projectEntry.FullName, projectText),
                resources,
                links);
            var session = new YmmpxPackageSession(loadedPackage, archive, entries);
            archive = null;
            return session;
        }
        catch (LegacyV1ReadException)
        {
            archive?.Dispose();
            throw;
        }
        catch (InvalidDataException exception)
        {
            archive?.Dispose();
            throw new LegacyV1ReadException(LegacyV1ReadError.InvalidArchive, "The legacy package archive is invalid.", exception);
        }
        catch
        {
            archive?.Dispose();
            throw;
        }
    }

    private static IReadOnlyDictionary<string, ZipArchiveEntry> ValidateAndIndexEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxArchiveEntryCount)
            throw new LegacyV1ReadException(LegacyV1ReadError.InvalidArchive, "The legacy package contains too many entries.");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;

            string path;
            try
            {
                path = PackagePathValidator.NormalizeRelativePath(entry.FullName, "entryPath");
            }
            catch (ArgumentException exception)
            {
                throw new LegacyV1ReadException(LegacyV1ReadError.UnsafePath, "The legacy package contains an unsafe entry path.", exception);
            }

            if (!entries.TryAdd(path, entry))
                throw new LegacyV1ReadException(LegacyV1ReadError.DuplicateEntry, $"The legacy package contains a duplicate entry: {path}");
        }

        return entries;
    }

    private static async Task<ZipArchiveEntry> FindProjectEntryAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.TryGetValue(ProjectMarkerPath, out var markerEntry))
        {
            string markerPath;
            try
            {
                markerPath = (await ReadTextEntryAsync(markerEntry, MaxProjectMarkerLength, cancellationToken, preserveContent: false).ConfigureAwait(false)).Trim();
                markerPath = PackagePathValidator.NormalizeRelativePath(markerPath, "projectPath");
            }
            catch (ArgumentException exception)
            {
                throw new LegacyV1ReadException(LegacyV1ReadError.InvalidProjectPath, "The legacy project marker contains an unsafe path.", exception);
            }

            if (!markerPath.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase) || !entries.TryGetValue(markerPath, out var markedProject))
                throw new LegacyV1ReadException(LegacyV1ReadError.MissingProject, "The project referenced by the legacy marker was not found.");

            return markedProject;
        }

        if (entries.TryGetValue("project.ymmp", out var legacyProject))
            return legacyProject;

        throw new LegacyV1ReadException(LegacyV1ReadError.MissingProject, "The legacy package project entry was not found.");
    }

    private static async Task<IReadOnlyList<LegacyResourceLink>> ReadLinksAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LegacyResourceLink>? emptyLinks = null;
        if (entries.TryGetValue("links.json", out var linksJson))
        {
            var parsed = await TryReadLinksJsonAsync(linksJson, entries, cancellationToken).ConfigureAwait(false);
            if (parsed is { Count: > 0 })
                return parsed;
            emptyLinks ??= parsed;
        }

        if (entries.TryGetValue("manifest.json", out var manifestJson))
        {
            var parsed = await TryReadLegacyManifestAsync(manifestJson, entries, cancellationToken).ConfigureAwait(false);
            if (parsed is { Count: > 0 })
                return parsed;
            emptyLinks ??= parsed;
        }

        if (entries.TryGetValue("links.txt", out var linksText))
            return await ReadLinksTextAsync(linksText, entries, cancellationToken).ConfigureAwait(false);

        if (emptyLinks is not null)
            return emptyLinks;

        throw new LegacyV1ReadException(LegacyV1ReadError.InvalidLinks, "No readable legacy link definition was found.");
    }

    private static async Task<IReadOnlyList<LegacyResourceLink>?> TryReadLinksJsonAsync(
        ZipArchiveEntry entry,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await ReadTextEntryAsync(entry, MaxLinkMetadataLength, cancellationToken, preserveContent: false).ConfigureAwait(false);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
            if (map is null)
                return null;
            return NormalizeLinks(map.Select(pair => (pair.Key, pair.Value)), entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<LegacyResourceLink>?> TryReadLegacyManifestAsync(
        ZipArchiveEntry entry,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await ReadTextEntryAsync(entry, MaxLinkMetadataLength, cancellationToken, preserveContent: false).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Files", out var files) ||
                files.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = new List<(string OriginalReference, string PackagePath)>();
            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object ||
                    !file.TryGetProperty("OriginalPath", out var original) || original.ValueKind != JsonValueKind.String ||
                    !file.TryGetProperty("BundlePath", out var bundle) || bundle.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var originalPath = original.GetString();
                var bundlePath = bundle.GetString();
                if (!string.IsNullOrWhiteSpace(originalPath) && !string.IsNullOrWhiteSpace(bundlePath))
                    values.Add((originalPath, bundlePath));
            }

            return NormalizeLinks(values, entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<LegacyResourceLink>> ReadLinksTextAsync(
        ZipArchiveEntry entry,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        var text = await ReadTextEntryAsync(entry, MaxLinkMetadataLength, cancellationToken, preserveContent: false).ConfigureAwait(false);
        var values = new List<(string OriginalReference, string PackagePath)>();
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            for (var index = line.Length - 1; index >= 0; index--)
            {
                if (line[index] != ',')
                    continue;

                var originalReference = line[..index].Trim();
                var packagePath = line[(index + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(originalReference) || string.IsNullOrWhiteSpace(packagePath))
                    continue;

                values.Add((originalReference, packagePath));
                break;
            }
        }

        return NormalizeLinks(values, entries);
    }

    private static IReadOnlyList<LegacyResourceLink> NormalizeLinks(
        IEnumerable<(string OriginalReference, string PackagePath)> values,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var links = new List<LegacyResourceLink>();
        foreach (var (originalReference, packagePath) in values)
        {
            if (string.IsNullOrWhiteSpace(originalReference) || string.IsNullOrWhiteSpace(packagePath))
                continue;

            try
            {
                var normalizedPath = PackagePathValidator.NormalizeRelativePath(packagePath, "packagePath");
                // This matches v1 extraction behavior: a link whose target is absent is ignored.
                if (entries.ContainsKey(normalizedPath))
                    links.Add(new LegacyResourceLink(originalReference, normalizedPath));
            }
            catch (ArgumentException)
            {
                // This matches v1 extraction behavior: unsafe individual link values are ignored.
            }
        }

        return links
            .DistinctBy(link => (link.OriginalReference, link.PackagePath), LegacyLinkComparer.Instance)
            .OrderBy(link => link.PackagePath, StringComparer.Ordinal)
            .ThenBy(link => link.OriginalReference, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<LoadedYmmpxResource> CreateResources(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IReadOnlyList<LegacyResourceLink> links)
    {
        var linkedPaths = links.Select(link => link.PackagePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourcePaths = entries.Keys
            .Where(path => path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase) || linkedPaths.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return resourcePaths.Select(path =>
        {
            var entry = entries[path];
            var groupId = TryGetSequenceGroup(path);
            var kind = groupId is null ? DetectKind(path) : ManifestResourceKind.ImageSequence;
            return new LoadedYmmpxResource(path, Path.GetFileName(path), entry.Length, kind, groupId);
        }).ToArray();
    }

    private static string? TryGetSequenceGroup(string packagePath)
    {
        var segments = packagePath.Split('/');
        return segments.Length >= 3 &&
               string.Equals(segments[0], "resources", StringComparison.OrdinalIgnoreCase) &&
               segments[1].StartsWith("sequence_", StringComparison.OrdinalIgnoreCase)
            ? segments[1]
            : null;
    }

    private static ManifestResourceKind DetectKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => ManifestResourceKind.Image,
        ".wav" or ".mp3" or ".flac" or ".ogg" or ".m4a" => ManifestResourceKind.Audio,
        ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv" => ManifestResourceKind.Video,
        ".psd" => ManifestResourceKind.Psd,
        _ => ManifestResourceKind.File
    };

    private static async Task<string> ReadTextEntryAsync(
        ZipArchiveEntry entry,
        long maximumLength,
        CancellationToken cancellationToken,
        bool preserveContent)
    {
        if (entry.Length > maximumLength)
            throw new LegacyV1ReadException(LegacyV1ReadError.MetadataTooLarge, $"Legacy metadata is too large: {entry.FullName}");

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return preserveContent ? text : text.TrimStart('\uFEFF');
    }

    private sealed class LegacyLinkComparer : IEqualityComparer<(string OriginalReference, string PackagePath)>
    {
        public static LegacyLinkComparer Instance { get; } = new();

        public bool Equals((string OriginalReference, string PackagePath) left, (string OriginalReference, string PackagePath) right) =>
            string.Equals(left.OriginalReference, right.OriginalReference, StringComparison.Ordinal) &&
            string.Equals(left.PackagePath, right.PackagePath, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string OriginalReference, string PackagePath) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.OriginalReference), StringComparer.OrdinalIgnoreCase.GetHashCode(value.PackagePath));
    }
}

/// <summary>Identifies the package format that produced a loaded common package model.</summary>
public enum LoadedYmmpxSourceFormat
{
    /// <summary>A descriptor-less legacy v1 package.</summary>
    LegacyV1,
    /// <summary>A future descriptor-based v2 package.</summary>
    V2
}

/// <summary>Contains the metadata loaded from a package without extracting resources.</summary>
public sealed record LoadedYmmpxPackage(
    LoadedYmmpxSourceFormat SourceFormat,
    LoadedYmmpxProject Project,
    IReadOnlyList<LoadedYmmpxResource> Resources,
    IReadOnlyList<LegacyResourceLink> Links)
{
    /// <summary>Gets format-independent references supplied directly by a reader when available.</summary>
    public IReadOnlyList<ProjectResourceReference> ProjectReferences { get; init; } = Array.Empty<ProjectResourceReference>();
}

/// <summary>Contains the unmodified text of the project entry.</summary>
public sealed record LoadedYmmpxProject(string PackagePath, string Content);

/// <summary>Contains metadata for one resource entry without loading its content.</summary>
public sealed record LoadedYmmpxResource(
    string PackagePath,
    string FileName,
    long Length,
    ManifestResourceKind Kind,
    string? GroupId);

/// <summary>Normalizes one legacy original-reference to package-resource mapping.</summary>
public sealed record LegacyResourceLink(string OriginalReference, string PackagePath);

/// <summary>Classifies a legacy package read failure.</summary>
public enum LegacyV1ReadError
{
    /// <summary>The detector did not recognize the input as LegacyV1.</summary>
    NotLegacyV1,
    /// <summary>The ZIP archive is invalid or exceeds structural limits.</summary>
    InvalidArchive,
    /// <summary>An archive entry path is unsafe.</summary>
    UnsafePath,
    /// <summary>Case-insensitive duplicate entries make the package ambiguous.</summary>
    DuplicateEntry,
    /// <summary>The project marker path is invalid.</summary>
    InvalidProjectPath,
    /// <summary>The project entry is absent.</summary>
    MissingProject,
    /// <summary>Legacy link metadata is missing or invalid.</summary>
    InvalidLinks,
    /// <summary>A metadata entry exceeds its safe size limit.</summary>
    MetadataTooLarge
}

/// <summary>Represents a structured LegacyV1Reader failure.</summary>
public sealed class LegacyV1ReadException : Exception
{
    /// <summary>Gets the structured failure reason.</summary>
    public LegacyV1ReadError Error { get; }

    /// <summary>Initializes an exception with a reason and message.</summary>
    public LegacyV1ReadException(LegacyV1ReadError error, string message) : base(message)
    {
        Error = error;
    }

    /// <summary>Initializes an exception with a reason, message, and cause.</summary>
    public LegacyV1ReadException(LegacyV1ReadError error, string message, Exception innerException) : base(message, innerException)
    {
        Error = error;
    }
}
