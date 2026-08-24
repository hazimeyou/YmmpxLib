using System.Text;

namespace YmmpxLibV2;

/// <summary>Controls whether extraction may replace an existing output file.</summary>
public enum YmmpxOverwritePolicy
{
    /// <summary>Fail rather than changing an existing file.</summary>
    FailIfExists,
    /// <summary>Replace an existing file only when explicitly requested.</summary>
    Overwrite
}

/// <summary>Options for common package extraction.</summary>
public sealed class YmmpxExtractionOptions
{
    /// <summary>Gets or sets the output overwrite policy. The safe default is <see cref="YmmpxOverwritePolicy.FailIfExists"/>.</summary>
    public YmmpxOverwritePolicy OverwritePolicy { get; init; } = YmmpxOverwritePolicy.FailIfExists;

    /// <summary>Gets or sets an immutable prepared project to write instead of the source package project.</summary>
    public LoadedYmmpxProject? ProjectOverride { get; init; }
}

/// <summary>Extracts a loaded package through a format-independent content provider.</summary>
public static class YmmpxPackageExtractor
{
    /// <summary>Writes project text and resource streams below the destination directory.</summary>
    public static async Task ExtractAsync(
        IYmmpxResourceContentProvider source,
        string destinationDirectory,
        YmmpxExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new YmmpxExtractionOptions();

        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        var project = options.ProjectOverride ?? source.Package.Project;

        await WriteTextAsync(project.PackagePath, project.Content, root, options, cancellationToken).ConfigureAwait(false);
        foreach (var resource in source.Package.Resources.OrderBy(resource => resource.PackagePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = GetDestinationPath(root, resource.PackagePath);
            EnsureDestinationAvailable(destinationPath, options);

            try
            {
                await using var input = await source.OpenResourceReadAsync(resource, cancellationToken).ConfigureAwait(false);
                await CopyToFileAsync(input, destinationPath, options, cancellationToken).ConfigureAwait(false);
            }
            catch (YmmpxExtractionException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new YmmpxExtractionException(YmmpxExtractionError.ResourceWriteFailed, $"Failed to extract resource: {resource.PackagePath}", exception);
            }
        }
    }

    private static async Task WriteTextAsync(string packagePath, string content, string root, YmmpxExtractionOptions options, CancellationToken cancellationToken)
    {
        var destinationPath = GetDestinationPath(root, packagePath);
        EnsureDestinationAvailable(destinationPath, options);
        await WriteFileAtomicallyAsync(destinationPath, async stream =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }, options, cancellationToken).ConfigureAwait(false);
    }

    private static Task CopyToFileAsync(Stream input, string destinationPath, YmmpxExtractionOptions options, CancellationToken cancellationToken) =>
        WriteFileAtomicallyAsync(destinationPath, output => input.CopyToAsync(output, 1024 * 128, cancellationToken), options, cancellationToken);

    private static async Task WriteFileAtomicallyAsync(string destinationPath, Func<Stream, Task> write, YmmpxExtractionOptions options, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? throw new YmmpxExtractionException(YmmpxExtractionError.UnsafePath, "Destination directory is invalid.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
            {
                await write(output).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destinationPath, options.OverwritePolicy == YmmpxOverwritePolicy.Overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string GetDestinationPath(string root, string packagePath)
    {
        try
        {
            return PackageDestinationPathResolver.Resolve(root, packagePath);
        }
        catch (ArgumentException exception)
        {
            throw new YmmpxExtractionException(YmmpxExtractionError.UnsafePath, "Package path is unsafe.", exception);
        }
    }

    private static void EnsureDestinationAvailable(string path, YmmpxExtractionOptions options)
    {
        if (options.OverwritePolicy == YmmpxOverwritePolicy.FailIfExists && File.Exists(path))
            throw new YmmpxExtractionException(YmmpxExtractionError.DestinationExists, $"Destination file already exists: {path}");
    }
}

/// <summary>Classifies common extraction errors.</summary>
public enum YmmpxExtractionError
{
    /// <summary>The package model contains an unsafe output path.</summary>
    UnsafePath,
    /// <summary>A destination already exists under the safe default policy.</summary>
    DestinationExists,
    /// <summary>Reading or writing a resource failed.</summary>
    ResourceWriteFailed
}

/// <summary>Represents a structured common extraction failure.</summary>
public sealed class YmmpxExtractionException : Exception
{
    /// <summary>Gets the structured failure reason.</summary>
    public YmmpxExtractionError Error { get; }

    /// <summary>Initializes an exception with a reason and message.</summary>
    public YmmpxExtractionException(YmmpxExtractionError error, string message) : base(message) => Error = error;

    /// <summary>Initializes an exception with a reason, message, and cause.</summary>
    public YmmpxExtractionException(YmmpxExtractionError error, string message, Exception innerException) : base(message, innerException) => Error = error;
}
