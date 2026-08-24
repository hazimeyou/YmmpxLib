namespace YmmpxLibV2;

/// <summary>Maps a validated package-relative path to a safe destination path.</summary>
internal static class PackageDestinationPathResolver
{
    public static string Resolve(string destinationDirectory, string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var normalized = PackagePathValidator.NormalizeRelativePath(packagePath, nameof(packagePath));
        var destination = Path.GetFullPath(Path.Combine(rootWithSeparator, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Package path escapes the destination directory.", nameof(packagePath));

        return destination;
    }
}
