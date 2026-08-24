using System.Text.Json;

namespace YmmpxLibV2;

/// <summary>
/// Identifies a descriptor-based YMMPX package format independently of library and manifest versions.
/// </summary>
public sealed class YmmpxFormatDescriptor
{
    /// <summary>Gets the stable descriptor entry name in a v2 package.</summary>
    public const string FileName = "_ymmpx.json";

    /// <summary>Gets the required YMMPX format identifier.</summary>
    public const string FormatIdentifier = "ymmpx";

    /// <summary>Gets the currently supported package format major version.</summary>
    public const int SupportedMajorVersion = 2;

    /// <summary>Gets the currently supported package format minor version.</summary>
    public const int SupportedMinorVersion = 0;

    /// <summary>Gets the package format identifier.</summary>
    public string Format { get; }

    /// <summary>Gets the package format major version.</summary>
    public int MajorVersion { get; }

    /// <summary>Gets the package format minor version.</summary>
    public int MinorVersion { get; }

    /// <summary>Gets the relative path to the resource manifest.</summary>
    public string Manifest { get; }

    /// <summary>Initializes a descriptor.</summary>
    public YmmpxFormatDescriptor(int majorVersion, int minorVersion, string manifest)
        : this(FormatIdentifier, majorVersion, minorVersion, manifest)
    {
    }

    /// <summary>Initializes a descriptor.</summary>
    public YmmpxFormatDescriptor(string format, int majorVersion, int minorVersion, string manifest)
    {
        if (!string.Equals(format, FormatIdentifier, StringComparison.Ordinal))
            throw new ArgumentException("Format identifier must be ymmpx.", nameof(format));
        if (majorVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(majorVersion), majorVersion, "Major version must be positive.");
        if (minorVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(minorVersion), minorVersion, "Minor version cannot be negative.");

        Format = FormatIdentifier;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        Manifest = PackagePathValidator.NormalizeRelativePath(manifest, nameof(manifest));
    }
}

/// <summary>
/// Serializes stable YMMPX format descriptors.
/// </summary>
public static class YmmpxFormatDescriptorSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Serializes a descriptor deterministically.</summary>
    public static string Serialize(YmmpxFormatDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return JsonSerializer.Serialize(new DescriptorDocument
        {
            Format = descriptor.Format,
            MajorVersion = descriptor.MajorVersion,
            MinorVersion = descriptor.MinorVersion,
            Manifest = descriptor.Manifest
        }, SerializerOptions);
    }

    /// <summary>Deserializes and validates a descriptor.</summary>
    /// <exception cref="YmmpxFormatDescriptorException">The descriptor is malformed or invalid.</exception>
    public static YmmpxFormatDescriptor Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var document = JsonSerializer.Deserialize<DescriptorDocument>(json, SerializerOptions)
                ?? throw new YmmpxFormatDescriptorException("Format descriptor is empty.");
            return new YmmpxFormatDescriptor(
                document.Format ?? throw new YmmpxFormatDescriptorException("Descriptor format is required."),
                document.MajorVersion,
                document.MinorVersion,
                document.Manifest ?? throw new YmmpxFormatDescriptorException("Descriptor manifest is required."));
        }
        catch (YmmpxFormatDescriptorException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new YmmpxFormatDescriptorException("Format descriptor JSON is malformed.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new YmmpxFormatDescriptorException("Format descriptor validation failed.", exception);
        }
    }

    private sealed class DescriptorDocument
    {
        public string? Format { get; set; }
        public int MajorVersion { get; set; }
        public int MinorVersion { get; set; }
        public string? Manifest { get; set; }
    }
}

/// <summary>Represents a format descriptor parsing or validation failure.</summary>
public sealed class YmmpxFormatDescriptorException : Exception
{
    /// <summary>Initializes an exception with a message.</summary>
    public YmmpxFormatDescriptorException(string message) : base(message)
    {
    }

    /// <summary>Initializes an exception with a message and cause.</summary>
    public YmmpxFormatDescriptorException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
