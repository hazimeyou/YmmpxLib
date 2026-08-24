using System.Text.Json;
using System.Text.Json.Serialization;

namespace YmmpxLibV2;

/// <summary>
/// Describes the v2 package manifest used to identify resources for dependency recovery.
/// </summary>
public sealed class PackageManifest
{
    /// <summary>
    /// Gets the current manifest schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets the v2 manifest file name. This differs from the v1 compatibility manifest name.
    /// </summary>
    public const string FileName = "manifest.v2.json";

    /// <summary>
    /// Gets the schema version.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// Gets the resources in deterministic package-path order.
    /// </summary>
    public IReadOnlyList<PackageManifestResource> Resources { get; }

    /// <summary>
    /// Initializes a manifest using the current schema version.
    /// </summary>
    public PackageManifest(IEnumerable<PackageManifestResource> resources)
        : this(CurrentSchemaVersion, resources)
    {
    }

    /// <summary>
    /// Initializes a manifest.
    /// </summary>
    public PackageManifest(int schemaVersion, IEnumerable<PackageManifestResource> resources)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Unsupported manifest schema version.");

        ArgumentNullException.ThrowIfNull(resources);
        var resourceArray = resources.ToArray();
        if (resourceArray.Any(resource => resource is null))
            throw new ArgumentException("Manifest resources cannot contain null values.", nameof(resources));
        var orderedResources = resourceArray.OrderBy(resource => resource.PackagePath, StringComparer.Ordinal).ToArray();

        var duplicatePath = orderedResources
            .GroupBy(resource => resource.PackagePath, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePath is not null)
            throw new ArgumentException($"Duplicate package path: {duplicatePath.Key}", nameof(resources));

        SchemaVersion = schemaVersion;
        Resources = orderedResources;
    }
}

/// <summary>
/// Stores one recoverable resource in a <see cref="PackageManifest"/>.
/// </summary>
public sealed class PackageManifestResource
{
    /// <summary>Gets the optional original absolute path. It can be omitted for privacy.</summary>
    public string? OriginalPath { get; }

    /// <summary>Gets the resource file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the resource length in bytes.</summary>
    public long Length { get; }

    /// <summary>Gets the uppercase hexadecimal SHA-256 hash.</summary>
    public string Sha256 { get; }

    /// <summary>Gets the resource location inside a future v2 package.</summary>
    public string PackagePath { get; }

    /// <summary>Gets the broad resource kind.</summary>
    public ManifestResourceKind Kind { get; }

    /// <summary>
    /// Gets the optional logical image-sequence group identifier.
    /// Every physical frame is represented by an entry with the same group identifier.
    /// </summary>
    public string? GroupId { get; }

    /// <summary>
    /// Initializes a resource entry.
    /// </summary>
    public PackageManifestResource(
        string? originalPath,
        string fileName,
        long length,
        string sha256,
        string packagePath,
        ManifestResourceKind kind = ManifestResourceKind.File,
        string? groupId = null)
    {
        var identity = new ResourceIdentity(originalPath, fileName, length, sha256);
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));

        OriginalPath = identity.OriginalPath;
        FileName = identity.FileName;
        Length = identity.Length;
        Sha256 = identity.Sha256;
        PackagePath = NormalizePackagePath(packagePath);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown manifest resource kind.");
        Kind = kind;
        GroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId;
    }

    /// <summary>
    /// Creates the identity used by <see cref="LocalResourceSearch"/>.
    /// </summary>
    public ResourceIdentity ToResourceIdentity() => new(OriginalPath, FileName, Length, Sha256);

    private static string NormalizePackagePath(string packagePath)
    {
        var normalized = packagePath.Replace('\\', '/');
        if (Path.IsPathFullyQualified(packagePath) || normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException("Package path must be a relative path without traversal.", nameof(packagePath));
        }

        return normalized;
    }
}

/// <summary>
/// The broad type of a package resource.
/// </summary>
public enum ManifestResourceKind
{
    /// <summary>An unclassified file.</summary>
    File,
    /// <summary>An image file.</summary>
    Image,
    /// <summary>An audio file.</summary>
    Audio,
    /// <summary>A video file.</summary>
    Video,
    /// <summary>A Photoshop document.</summary>
    Psd,
    /// <summary>A physical frame belonging to an image sequence.</summary>
    ImageSequence,
    /// <summary>A plugin dependency. Its detailed identity is a future concern.</summary>
    Plugin,
    /// <summary>An unknown resource type.</summary>
    Unknown
}

/// <summary>
/// Serializes and deserializes <see cref="PackageManifest"/> using System.Text.Json.
/// </summary>
public static class PackageManifestSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Serializes a manifest in deterministic package-path order.</summary>
    public static string Serialize(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(ManifestDocument.FromManifest(manifest), SerializerOptions);
    }

    /// <summary>Deserializes and validates a manifest.</summary>
    /// <exception cref="PackageManifestException">The document is malformed or invalid.</exception>
    public static PackageManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var document = JsonSerializer.Deserialize<ManifestDocument>(json, SerializerOptions)
                ?? throw new PackageManifestException("Manifest document is empty.");
            return document.ToManifest();
        }
        catch (PackageManifestException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new PackageManifestException("Manifest JSON is malformed.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new PackageManifestException("Manifest validation failed.", exception);
        }
    }

    private sealed class ManifestDocument
    {
        public int SchemaVersion { get; set; }
        public List<ResourceDocument>? Resources { get; set; }

        public PackageManifest ToManifest()
        {
            if (SchemaVersion != PackageManifest.CurrentSchemaVersion)
                throw new PackageManifestException($"Unsupported manifest schema version: {SchemaVersion}.");
            if (Resources is null)
                throw new PackageManifestException("Manifest resources are required.");

            try
            {
                return new PackageManifest(SchemaVersion, Resources.Select(resource => resource.ToResource()));
            }
            catch (ArgumentException exception)
            {
                throw new PackageManifestException("Manifest validation failed.", exception);
            }
        }

        public static ManifestDocument FromManifest(PackageManifest manifest) => new()
        {
            SchemaVersion = manifest.SchemaVersion,
            Resources = manifest.Resources.Select(ResourceDocument.FromResource).ToList()
        };
    }

    private sealed class ResourceDocument
    {
        public string? OriginalPath { get; set; }
        public string? FileName { get; set; }
        public long Length { get; set; }
        public string? Sha256 { get; set; }
        public string? PackagePath { get; set; }
        public ManifestResourceKind Kind { get; set; }
        public string? GroupId { get; set; }

        public PackageManifestResource ToResource() => new(
            OriginalPath,
            FileName ?? throw new PackageManifestException("Resource fileName is required."),
            Length,
            Sha256 ?? throw new PackageManifestException("Resource sha256 is required."),
            PackagePath ?? throw new PackageManifestException("Resource packagePath is required."),
            Kind,
            GroupId);

        public static ResourceDocument FromResource(PackageManifestResource resource) => new()
        {
            OriginalPath = resource.OriginalPath,
            FileName = resource.FileName,
            Length = resource.Length,
            Sha256 = resource.Sha256,
            PackagePath = resource.PackagePath,
            Kind = resource.Kind,
            GroupId = resource.GroupId
        };
    }
}

/// <summary>
/// Represents a manifest parsing or validation failure.
/// </summary>
public sealed class PackageManifestException : Exception
{
    /// <summary>Initializes an exception with a message.</summary>
    public PackageManifestException(string message) : base(message)
    {
    }

    /// <summary>Initializes an exception with a message and cause.</summary>
    public PackageManifestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
