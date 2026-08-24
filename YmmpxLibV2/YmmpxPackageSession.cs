using System.IO.Compression;

namespace YmmpxLibV2;

/// <summary>Provides read-only resource streams for one loaded package session.</summary>
public interface IYmmpxResourceContentProvider
{
    /// <summary>Gets the package metadata served by this provider.</summary>
    LoadedYmmpxPackage Package { get; }

    /// <summary>Opens a resource stream. The caller disposes the returned stream before disposing the provider.</summary>
    ValueTask<Stream> OpenResourceReadAsync(LoadedYmmpxResource resource, CancellationToken cancellationToken = default);
}

/// <summary>
/// Holds the ZIP archive lifetime required to stream resource content.
/// The caller-owned input stream remains open when this session is disposed.
/// </summary>
public sealed class YmmpxPackageSession : IYmmpxResourceContentProvider, IAsyncDisposable, IDisposable
{
    private readonly ZipArchive archive;
    private readonly IReadOnlyDictionary<string, ZipArchiveEntry> entries;
    private bool disposed;

    internal YmmpxPackageSession(
        LoadedYmmpxPackage package,
        ZipArchive archive,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        Package = package;
        this.archive = archive;
        this.entries = entries;
    }

    /// <inheritdoc />
    public LoadedYmmpxPackage Package { get; }

    /// <inheritdoc />
    public ValueTask<Stream> OpenResourceReadAsync(LoadedYmmpxResource resource, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        cancellationToken.ThrowIfCancellationRequested();

        string packagePath;
        try
        {
            packagePath = PackagePathValidator.NormalizeRelativePath(resource.PackagePath, nameof(resource));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The package resource path is unsafe.", exception);
        }

        if (!entries.TryGetValue(packagePath, out var entry) || entry.Length != resource.Length)
            throw new InvalidDataException("The package resource does not match the loaded metadata.");

        return ValueTask.FromResult<Stream>(entry.Open());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
            return;

        archive.Dispose();
        disposed = true;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
