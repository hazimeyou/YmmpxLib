using System.IO.Compression;
using System.Text;

namespace YmmpxLibV2;

/// <summary>
/// Detects the package format without extracting archive entries or invoking a reader.
/// </summary>
public static class YmmpxFormatDetector
{
    /// <summary>Gets the largest accepted descriptor payload.</summary>
    public const int MaxDescriptorLength = 16 * 1024;

    private const string LegacyProjectMarker = "_ymmpx_project_path.txt";

    /// <summary>
    /// Detects a YMMPX package format from a readable, seekable ZIP stream.
    /// The stream remains open.
    /// </summary>
    public static async Task<YmmpxFormatDetectionResult> DetectAsync(Stream package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!package.CanRead || !package.CanSeek)
            throw new ArgumentException("Package stream must be readable and seekable.", nameof(package));

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            package.Seek(0, SeekOrigin.Begin);
            using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            var descriptorEntries = archive.Entries
                .Where(entry => string.Equals(entry.FullName, YmmpxFormatDescriptor.FileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (descriptorEntries.Length > 1)
                return YmmpxFormatDetectionResult.InvalidDescriptor();

            if (descriptorEntries.Length == 1)
                return await DetectDescriptorAsync(descriptorEntries[0], archive.Entries, cancellationToken).ConfigureAwait(false);

            return await DetectLegacyV1Async(archive.Entries, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return YmmpxFormatDetectionResult.InvalidArchive();
        }
    }

    private static async Task<YmmpxFormatDetectionResult> DetectDescriptorAsync(
        ZipArchiveEntry descriptorEntry,
        IReadOnlyCollection<ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        if (descriptorEntry.Length > MaxDescriptorLength)
            return YmmpxFormatDetectionResult.InvalidDescriptor();

        try
        {
            var json = await ReadSmallEntryAsync(descriptorEntry, cancellationToken).ConfigureAwait(false);
            var descriptor = YmmpxFormatDescriptorSerializer.Deserialize(json);

            if (!entries.Any(entry => string.Equals(entry.FullName, descriptor.Manifest, StringComparison.Ordinal)))
                return YmmpxFormatDetectionResult.InvalidDescriptor();

            if (descriptor.MajorVersion > YmmpxFormatDescriptor.SupportedMajorVersion)
                return YmmpxFormatDetectionResult.UnsupportedFutureVersion(descriptor);
            if (descriptor.MajorVersion < YmmpxFormatDescriptor.SupportedMajorVersion)
                return YmmpxFormatDetectionResult.UnsupportedMajorVersion(descriptor);
            if (descriptor.MinorVersion > YmmpxFormatDescriptor.SupportedMinorVersion)
                return YmmpxFormatDetectionResult.UnsupportedMinorVersion(descriptor);

            return YmmpxFormatDetectionResult.SupportedV2(descriptor);
        }
        catch (YmmpxFormatDescriptorException)
        {
            return YmmpxFormatDetectionResult.InvalidDescriptor();
        }
    }

    private static async Task<YmmpxFormatDetectionResult> DetectLegacyV1Async(
        IReadOnlyCollection<ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        var hasLinkDefinition = entries.Any(entry =>
            string.Equals(entry.FullName, "links.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.FullName, "links.txt", StringComparison.OrdinalIgnoreCase));
        if (!hasLinkDefinition)
            return YmmpxFormatDetectionResult.NotYmmpx();

        var markerEntries = entries
            .Where(entry => string.Equals(entry.FullName, LegacyProjectMarker, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (markerEntries.Length == 1 && markerEntries[0].Length <= MaxDescriptorLength)
        {
            var markerPath = (await ReadSmallEntryAsync(markerEntries[0], cancellationToken).ConfigureAwait(false)).Trim();
            try
            {
                var normalizedMarkerPath = PackagePathValidator.NormalizeRelativePath(markerPath, "markerPath");
                if (normalizedMarkerPath.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase) &&
                    entries.Any(entry => string.Equals(entry.FullName, normalizedMarkerPath, StringComparison.Ordinal)))
                {
                    return YmmpxFormatDetectionResult.LegacyV1();
                }
            }
            catch (ArgumentException)
            {
                return YmmpxFormatDetectionResult.NotYmmpx();
            }
        }

        var hasLegacyProject = entries.Any(entry => string.Equals(entry.FullName, "project.ymmp", StringComparison.OrdinalIgnoreCase));
        return hasLegacyProject
            ? YmmpxFormatDetectionResult.LegacyV1()
            : YmmpxFormatDetectionResult.NotYmmpx();
    }

    private static async Task<string> ReadSmallEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length > MaxDescriptorLength)
            throw new YmmpxFormatDescriptorException("Descriptor is too large.");

        await using var stream = entry.Open();
        using var buffer = new MemoryStream(capacity: (int)entry.Length);
        var bytes = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            if (buffer.Length + read > MaxDescriptorLength)
                throw new YmmpxFormatDescriptorException("Descriptor is too large.");
            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF');
    }
}

/// <summary>
/// Describes the detector's structured outcome. Unsupported formats are valid packages that are not readable here.
/// </summary>
public enum YmmpxFormatDetectionStatus
{
    /// <summary>A descriptor-less package matched the known v1 structure.</summary>
    LegacyV1,
    /// <summary>The package is YMMPX format 2.0 and can be routed to a future v2 reader.</summary>
    SupportedV2,
    /// <summary>A valid descriptor names a future package major version.</summary>
    UnsupportedFutureVersion,
    /// <summary>A valid descriptor names an unsupported older package major version.</summary>
    UnsupportedMajorVersion,
    /// <summary>A valid descriptor names an unsupported minor version of the current major.</summary>
    UnsupportedMinorVersion,
    /// <summary>The ZIP archive itself is invalid.</summary>
    InvalidArchive,
    /// <summary>The format descriptor is malformed, unsafe, or inconsistent with the archive.</summary>
    InvalidDescriptor,
    /// <summary>The archive has insufficient evidence to be recognized as YMMPX.</summary>
    NotYmmpx
}

/// <summary>
/// Identifies the future reader selected by detection. Detection and reader implementation remain separate.
/// </summary>
public enum YmmpxReaderRoute
{
    /// <summary>No reader must be invoked.</summary>
    None,
    /// <summary>A future LegacyV1Reader is the route.</summary>
    LegacyV1,
    /// <summary>A future V2Reader is the route.</summary>
    V2
}

/// <summary>
/// Contains the detected version, status, and safe reader route for a package.
/// </summary>
public sealed record YmmpxFormatDetectionResult(
    YmmpxFormatDetectionStatus Status,
    YmmpxFormatDescriptor? Descriptor,
    YmmpxReaderRoute ReaderRoute)
{
    /// <summary>Gets the detected descriptor major version, when present.</summary>
    public int? MajorVersion => Descriptor?.MajorVersion;

    /// <summary>Gets the detected descriptor minor version, when present.</summary>
    public int? MinorVersion => Descriptor?.MinorVersion;

    /// <summary>Gets whether detection selected a supported future reader route.</summary>
    public bool IsSupported => ReaderRoute != YmmpxReaderRoute.None;

    internal static YmmpxFormatDetectionResult LegacyV1() =>
        new(YmmpxFormatDetectionStatus.LegacyV1, null, YmmpxReaderRoute.LegacyV1);

    internal static YmmpxFormatDetectionResult SupportedV2(YmmpxFormatDescriptor descriptor) =>
        new(YmmpxFormatDetectionStatus.SupportedV2, descriptor, YmmpxReaderRoute.V2);

    internal static YmmpxFormatDetectionResult UnsupportedFutureVersion(YmmpxFormatDescriptor descriptor) =>
        new(YmmpxFormatDetectionStatus.UnsupportedFutureVersion, descriptor, YmmpxReaderRoute.None);

    internal static YmmpxFormatDetectionResult UnsupportedMajorVersion(YmmpxFormatDescriptor descriptor) =>
        new(YmmpxFormatDetectionStatus.UnsupportedMajorVersion, descriptor, YmmpxReaderRoute.None);

    internal static YmmpxFormatDetectionResult UnsupportedMinorVersion(YmmpxFormatDescriptor descriptor) =>
        new(YmmpxFormatDetectionStatus.UnsupportedMinorVersion, descriptor, YmmpxReaderRoute.None);

    internal static YmmpxFormatDetectionResult InvalidArchive() =>
        new(YmmpxFormatDetectionStatus.InvalidArchive, null, YmmpxReaderRoute.None);

    internal static YmmpxFormatDetectionResult InvalidDescriptor() =>
        new(YmmpxFormatDetectionStatus.InvalidDescriptor, null, YmmpxReaderRoute.None);

    internal static YmmpxFormatDetectionResult NotYmmpx() =>
        new(YmmpxFormatDetectionStatus.NotYmmpx, null, YmmpxReaderRoute.None);
}
