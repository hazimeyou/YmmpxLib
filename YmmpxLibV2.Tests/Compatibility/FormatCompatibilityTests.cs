using System.IO.Compression;
using System.Text;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests.Compatibility;

public sealed class FormatCompatibilityTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task FutureMajorVersion_IsRecognizedButNotRoutedToAReader(int majorVersion)
    {
        var result = await DetectAsync(CreateDescriptorPackage(majorVersion, 0));

        Assert.Equal(YmmpxFormatDetectionStatus.UnsupportedFutureVersion, result.Status);
        Assert.Equal(majorVersion, result.MajorVersion);
        Assert.Equal(YmmpxReaderRoute.None, result.ReaderRoute);
    }

    [Fact]
    public async Task UnknownV2MinorVersion_IsRecognizedButNotRoutedToAReader()
    {
        var result = await DetectAsync(CreateDescriptorPackage(2, 1));

        Assert.Equal(YmmpxFormatDetectionStatus.UnsupportedMinorVersion, result.Status);
        Assert.Equal(YmmpxReaderRoute.None, result.ReaderRoute);
    }

    [Fact]
    public async Task InvalidAndUnrelatedArchives_AreNotMisidentifiedAsFutureFormats()
    {
        var malformed = await DetectAsync(CreateArchive(archive => WriteText(archive, YmmpxFormatDescriptor.FileName, "{")));
        var unrelated = await DetectAsync(CreateArchive(archive => WriteText(archive, "readme.txt", "not ymmpx")));

        Assert.Equal(YmmpxFormatDetectionStatus.InvalidDescriptor, malformed.Status);
        Assert.Equal(YmmpxFormatDetectionStatus.NotYmmpx, unrelated.Status);
    }

    private static async Task<YmmpxFormatDetectionResult> DetectAsync(byte[] content) =>
        await YmmpxFormatDetector.DetectAsync(new MemoryStream(content), TestContext.Current.CancellationToken);

    private static byte[] CreateDescriptorPackage(int major, int minor) => CreateArchive(archive =>
    {
        WriteText(archive, YmmpxFormatDescriptor.FileName, YmmpxFormatDescriptorSerializer.Serialize(new YmmpxFormatDescriptor(major, minor, PackageManifest.FileName)));
        WriteText(archive, PackageManifest.FileName, "{}");
    });

    private static byte[] CreateArchive(Action<ZipArchive> configure)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) configure(archive);
        return stream.ToArray();
    }

    private static void WriteText(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
