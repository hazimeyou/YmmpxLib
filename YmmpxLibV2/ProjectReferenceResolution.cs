using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YmmpxLibV2;

/// <summary>Maps one project file reference to its package resource path.</summary>
public sealed record ProjectResourceReference(string OriginalReference, string PackagePath);

/// <summary>Creates format-independent project references from a loaded legacy package.</summary>
public static class ProjectResourceReferenceMapper
{
    /// <summary>
    /// Converts normalized legacy links to common references and excludes links whose resource is absent.
    /// </summary>
    public static IReadOnlyList<ProjectResourceReference> FromLegacyPackage(LoadedYmmpxPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var resourcePaths = package.Resources.Select(resource => resource.PackagePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, ProjectResourceReference>(GetPathComparer());

        foreach (var link in package.Links)
        {
            if (string.IsNullOrWhiteSpace(link.OriginalReference) || !resourcePaths.Contains(link.PackagePath))
                continue;

            var key = NormalizeReference(link.OriginalReference);
            var reference = new ProjectResourceReference(link.OriginalReference, link.PackagePath);
            if (references.TryGetValue(key, out var existing) &&
                !string.Equals(existing.PackagePath, reference.PackagePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new YmmpxProjectResolutionException(
                    YmmpxProjectResolutionError.AmbiguousReference,
                    $"A project reference maps to multiple package paths: {link.OriginalReference}");
            }

            references[key] = reference;
        }

        return references.Values.OrderBy(reference => reference.OriginalReference, StringComparer.Ordinal).ToArray();
    }

    internal static string NormalizeReference(string value) => value.Replace('\\', '/').Trim();

    internal static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

/// <summary>Resolves only JSON <c>FilePath</c> values to extracted package-resource paths.</summary>
public static class YmmpxProjectReferenceResolver
{
    /// <summary>Creates an immutable resolved project copy without changing the source project.</summary>
    public static YmmpxProjectResolutionResult Resolve(
        LoadedYmmpxProject project,
        IReadOnlyList<ProjectResourceReference> references,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var mappings = CreateResolvedMappings(references, destinationDirectory);
        JsonNode root;
        try
        {
            root = JsonNode.Parse(project.Content) ?? throw new JsonException("Project JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new YmmpxProjectResolutionException(YmmpxProjectResolutionError.InvalidProjectJson, "Project JSON is invalid.", exception);
        }

        var replacedCount = ReplaceFilePaths(root, mappings, cancellationToken);
        var resolvedText = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        return new YmmpxProjectResolutionResult(new LoadedYmmpxProject(project.PackagePath, resolvedText), replacedCount);
    }

    private static IReadOnlyDictionary<string, string> CreateResolvedMappings(
        IReadOnlyList<ProjectResourceReference> references,
        string destinationDirectory)
    {
        var mappings = new Dictionary<string, string>(ProjectResourceReferenceMapper.GetPathComparer());
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.OriginalReference))
                continue;

            string destinationPath;
            try
            {
                destinationPath = PackageDestinationPathResolver.Resolve(destinationDirectory, reference.PackagePath);
            }
            catch (ArgumentException exception)
            {
                throw new YmmpxProjectResolutionException(YmmpxProjectResolutionError.UnsafePackagePath, "A project reference has an unsafe package path.", exception);
            }

            var key = ProjectResourceReferenceMapper.NormalizeReference(reference.OriginalReference);
            if (mappings.TryGetValue(key, out var existing) &&
                !string.Equals(existing, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new YmmpxProjectResolutionException(
                    YmmpxProjectResolutionError.AmbiguousReference,
                    $"A project reference resolves to multiple destination paths: {reference.OriginalReference}");
            }

            mappings[key] = destinationPath;
        }

        return mappings;
    }

    private static int ReplaceFilePaths(JsonNode node, IReadOnlyDictionary<string, string> mappings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = 0;
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (property.Key.Equals("FilePath", StringComparison.OrdinalIgnoreCase) &&
                    property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var path) &&
                    !string.IsNullOrWhiteSpace(path) &&
                    mappings.TryGetValue(ProjectResourceReferenceMapper.NormalizeReference(path), out var destination))
                {
                    obj[property.Key] = destination;
                    count++;
                }
                else if (property.Value is not null)
                {
                    count += ReplaceFilePaths(property.Value, mappings, cancellationToken);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                    count += ReplaceFilePaths(item, mappings, cancellationToken);
            }
        }

        return count;
    }
}

/// <summary>Contains a resolved, immutable project copy and its replacement count.</summary>
public sealed record YmmpxProjectResolutionResult(LoadedYmmpxProject Project, int ReplacedReferenceCount);

/// <summary>Classifies project reference resolution failures.</summary>
public enum YmmpxProjectResolutionError
{
    /// <summary>Project JSON cannot be parsed.</summary>
    InvalidProjectJson,
    /// <summary>A package path is unsafe for the requested destination.</summary>
    UnsafePackagePath,
    /// <summary>A project reference maps to more than one destination.</summary>
    AmbiguousReference
}

/// <summary>Represents a structured project reference resolution failure.</summary>
public sealed class YmmpxProjectResolutionException : Exception
{
    /// <summary>Gets the structured failure reason.</summary>
    public YmmpxProjectResolutionError Error { get; }

    /// <summary>Initializes an exception with a reason and message.</summary>
    public YmmpxProjectResolutionException(YmmpxProjectResolutionError error, string message) : base(message) => Error = error;

    /// <summary>Initializes an exception with a reason, message, and cause.</summary>
    public YmmpxProjectResolutionException(YmmpxProjectResolutionError error, string message, Exception innerException) : base(message, innerException) => Error = error;
}
