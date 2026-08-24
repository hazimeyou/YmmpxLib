namespace YmmpxLibV2;

internal static class PackagePathValidator
{
    public static string NormalizeRelativePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Package path is required.", parameterName);

        var normalized = path.Replace('\\', '/');
        if (normalized.Contains(':') || Path.IsPathFullyQualified(path) || normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException("Package path must be a relative path without traversal.", parameterName);
        }

        return normalized;
    }
}
