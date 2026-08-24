using System.Security.Cryptography;

namespace YmmpxLibV2;

/// <summary>
/// Identifies a local resource by its original location, name, length, and SHA-256 hash.
/// </summary>
public sealed record ResourceIdentity
{
    /// <summary>
    /// Gets the original path recorded for the resource.
    /// </summary>
    public string OriginalPath { get; }

    /// <summary>
    /// Gets the original file name.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the resource length in bytes.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// Gets the uppercase hexadecimal SHA-256 hash.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Initializes a resource identity.
    /// </summary>
    public ResourceIdentity(string originalPath, string fileName, long length, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        OriginalPath = Path.GetFullPath(originalPath);
        FileName = fileName;
        Length = length;
        Sha256 = NormalizeSha256(sha256);
    }

    /// <summary>
    /// Creates a resource identity from an existing file without loading the whole file into memory.
    /// </summary>
    public static async Task<ResourceIdentity> CreateAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("The resource file was not found.", fullPath);

        var sha256 = await ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
        return new ResourceIdentity(fullPath, fileInfo.Name, fileInfo.Length, sha256);
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string NormalizeSha256(string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        var normalized = sha256.Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("SHA-256 must be a 64-character hexadecimal value.", nameof(sha256));

        return normalized;
    }
}
