using System.IO.Compression;
using System.Text;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class YmmpxFormatDetectorTests
{
    [Fact]
    public async Task DetectsCurrentV1Package()
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, "project.ymmp", "{}");
            WriteEntry(archive, "_ymmpx_project_path.txt", "project.ymmp");
            WriteEntry(archive, "links.json", "{}");
        });

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.LegacyV1, result.Status);
        Assert.Equal(YmmpxReaderRoute.LegacyV1, result.ReaderRoute);
        Assert.Null(result.Descriptor);
    }

    [Fact]
    public async Task DetectsCurrentV2Package()
    {
        var package = CreateDescriptorArchive(new YmmpxFormatDescriptor(2, 0, PackageManifest.FileName));

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.SupportedV2, result.Status);
        Assert.Equal(YmmpxReaderRoute.V2, result.ReaderRoute);
        Assert.Equal(2, result.MajorVersion);
        Assert.Equal(0, result.MinorVersion);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task DetectsFutureMajorVersionsWithoutRoutingThem(int majorVersion)
    {
        var package = CreateDescriptorArchive(new YmmpxFormatDescriptor(majorVersion, 0, PackageManifest.FileName));

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.UnsupportedFutureVersion, result.Status);
        Assert.Equal(majorVersion, result.MajorVersion);
        Assert.Equal(YmmpxReaderRoute.None, result.ReaderRoute);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(2, -1)]
    public async Task RejectsInvalidVersionNumbers(int majorVersion, int minorVersion)
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, YmmpxFormatDescriptor.FileName, DescriptorJson(majorVersion, minorVersion, PackageManifest.FileName));
            WriteEntry(archive, PackageManifest.FileName, "{}");
        });

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.InvalidDescriptor, result.Status);
    }

    [Fact]
    public async Task RejectsUnsupportedMinorVersion()
    {
        var package = CreateDescriptorArchive(new YmmpxFormatDescriptor(2, 1, PackageManifest.FileName));

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.UnsupportedMinorVersion, result.Status);
        Assert.Equal(YmmpxReaderRoute.None, result.ReaderRoute);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"format\":\"not-ymmpx\",\"majorVersion\":2,\"minorVersion\":0,\"manifest\":\"manifest.v2.json\"}")]
    public async Task RejectsMalformedOrWrongFormatDescriptor(string descriptorJson)
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, YmmpxFormatDescriptor.FileName, descriptorJson);
            WriteEntry(archive, PackageManifest.FileName, "{}");
        });

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.InvalidDescriptor, result.Status);
    }

    [Theory]
    [InlineData("../manifest.v2.json")]
    [InlineData("resources/../../manifest.v2.json")]
    [InlineData("C:\\temp\\manifest.v2.json")]
    [InlineData("/tmp/manifest.v2.json")]
    public async Task RejectsUnsafeManifestPath(string manifestPath)
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, YmmpxFormatDescriptor.FileName, DescriptorJson(2, 0, manifestPath));
            WriteEntry(archive, PackageManifest.FileName, "{}");
        });

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.InvalidDescriptor, result.Status);
    }

    [Fact]
    public async Task DoesNotTreatAnUnrelatedZipAsLegacyV1()
    {
        var package = CreateArchive(archive => WriteEntry(archive, "readme.txt", "not a package"));

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.NotYmmpx, result.Status);
    }

    [Fact]
    public async Task DoesNotTreatPartialV1StructureAsLegacyV1()
    {
        var package = CreateArchive(archive => WriteEntry(archive, "links.json", "{}"));

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.NotYmmpx, result.Status);
    }

    [Fact]
    public async Task DetectsJapaneseV1ProjectEntry()
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, "プロジェクト.ymmp", "{}");
            WriteEntry(archive, "_ymmpx_project_path.txt", "プロジェクト.ymmp");
            WriteEntry(archive, "links.json", "{\"resources/素材.wav\":\"resources/素材.wav\"}");
        });

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.LegacyV1, result.Status);
    }

    [Fact]
    public async Task RejectsOversizedDescriptor()
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, YmmpxFormatDescriptor.FileName, new string(' ', YmmpxFormatDetector.MaxDescriptorLength + 1));
            WriteEntry(archive, PackageManifest.FileName, "{}");
        });

        var result = await DetectAsync(package);

        Assert.Equal(YmmpxFormatDetectionStatus.InvalidDescriptor, result.Status);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var package = CreateDescriptorArchive(new YmmpxFormatDescriptor(2, 0, PackageManifest.FileName));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => YmmpxFormatDetector.DetectAsync(new MemoryStream(package), cancellation.Token));
    }

    [Fact]
    public void DescriptorRoundTripsDeterministically()
    {
        var descriptor = new YmmpxFormatDescriptor(2, 0, PackageManifest.FileName);

        var first = YmmpxFormatDescriptorSerializer.Serialize(descriptor);
        var second = YmmpxFormatDescriptorSerializer.Serialize(descriptor);
        var restored = YmmpxFormatDescriptorSerializer.Deserialize(first);

        Assert.Equal(first, second);
        Assert.Equal(descriptor.MajorVersion, restored.MajorVersion);
        Assert.Equal(descriptor.Manifest, restored.Manifest);
    }

    [Fact]
    public async Task ReturnsInvalidArchiveForNonZipData()
    {
        var result = await YmmpxFormatDetector.DetectAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("not a zip")),
            TestContext.Current.CancellationToken);

        Assert.Equal(YmmpxFormatDetectionStatus.InvalidArchive, result.Status);
    }

    private static async Task<YmmpxFormatDetectionResult> DetectAsync(byte[] package) =>
        await YmmpxFormatDetector.DetectAsync(new MemoryStream(package), TestContext.Current.CancellationToken);

    private static byte[] CreateDescriptorArchive(YmmpxFormatDescriptor descriptor) =>
        CreateArchive(archive =>
        {
            WriteEntry(archive, YmmpxFormatDescriptor.FileName, YmmpxFormatDescriptorSerializer.Serialize(descriptor));
            WriteEntry(archive, descriptor.Manifest, "{}");
        });

    private static byte[] CreateArchive(Action<ZipArchive> configure)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            configure(archive);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string DescriptorJson(int majorVersion, int minorVersion, string manifestPath) =>
        $"{{\"format\":\"ymmpx\",\"majorVersion\":{majorVersion},\"minorVersion\":{minorVersion},\"manifest\":\"{manifestPath.Replace("\\", "\\\\", StringComparison.Ordinal)}\"}}";
}
