using System.IO.Compression;
using System.Text;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class YmmpxPackageExtractorTests : IDisposable
{
    private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "YmmpxLibV2Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpensResourceStreamsWithoutLoadingPackageContentIntoTheModel()
    {
        var bytes = Encoding.UTF8.GetBytes("PSD content");
        await using var session = await OpenV1SessionAsync(archive => WriteBytesEntry(archive, "resources/素材/立ち絵.psd", bytes));
        var resource = Assert.Single(session.Package.Resources);

        await using var stream = await session.OpenResourceReadAsync(resource, TestContext.Current.CancellationToken);
        using var copied = new MemoryStream();
        await stream.CopyToAsync(copied, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, copied.ToArray());
        Assert.Equal(ManifestResourceKind.Psd, resource.Kind);
    }

    [Fact]
    public async Task KeepsCallerOwnedInputStreamOpenAndRejectsAccessAfterSessionDispose()
    {
        var input = new MemoryStream(CreateV1Archive(archive => WriteEntry(archive, "resources/image.png", "image")));
        var session = await LegacyV1Reader.OpenAsync(input, TestContext.Current.CancellationToken);
        var resource = Assert.Single(session.Package.Resources);

        await session.DisposeAsync();

        Assert.True(input.CanRead);
        Assert.Equal(0, input.Seek(0, SeekOrigin.Begin));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.OpenResourceReadAsync(resource, TestContext.Current.CancellationToken));
        input.Dispose();
    }

    [Fact]
    public async Task ExtractsProjectResourcesAndImageSequenceThroughTheCommonExtractor()
    {
        await using var session = await OpenV1SessionAsync(archive =>
        {
            WriteEntry(archive, "resources/素材/立ち絵.psd", "psd");
            WriteEntry(archive, "resources/sequence_1/frame_9.png", "9");
            WriteEntry(archive, "resources/sequence_1/frame_10.png", "10");
        }, projectPath: "projects/葵.ymmp");

        var destination = CreateDestination();
        await YmmpxPackageExtractor.ExtractAsync(session, destination, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("{\"title\":\"test\"}", await File.ReadAllTextAsync(Path.Combine(destination, "葵.ymmp"), TestContext.Current.CancellationToken));
        Assert.Equal("psd", await File.ReadAllTextAsync(Path.Combine(destination, "resources", "素材", "立ち絵.psd"), TestContext.Current.CancellationToken));
        Assert.Equal("10", await File.ReadAllTextAsync(Path.Combine(destination, "resources", "sequence_1", "frame_10.png"), TestContext.Current.CancellationToken));
        Assert.All(session.Package.Resources.Where(resource => resource.GroupId == "sequence_1"), resource => Assert.Equal(ManifestResourceKind.ImageSequence, resource.Kind));
    }

    [Fact]
    public async Task StreamsAMultiMegabyteResourceToDisk()
    {
        var content = new byte[3 * 1024 * 1024];
        for (var index = 0; index < content.Length; index++)
            content[index] = (byte)(index % 251);

        await using var session = await OpenV1SessionAsync(archive => WriteBytesEntry(archive, "resources/video.mp4", content));
        var destination = CreateDestination();

        await YmmpxPackageExtractor.ExtractAsync(session, destination, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(content.Length, new FileInfo(Path.Combine(destination, "resources", "video.mp4")).Length);
    }

    [Fact]
    public async Task FailsByDefaultWithoutOverwritingExistingFiles()
    {
        await using var session = await OpenV1SessionAsync(archive => WriteEntry(archive, "resources/image.png", "package"));
        var destination = CreateDestination();
        var existing = Path.Combine(destination, "resources", "image.png");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "existing", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<YmmpxExtractionException>(() => YmmpxPackageExtractor.ExtractAsync(session, destination, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(YmmpxExtractionError.DestinationExists, exception.Error);
        Assert.Equal("existing", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OverwritesOnlyWhenExplicitlyRequested()
    {
        await using var session = await OpenV1SessionAsync(archive => WriteEntry(archive, "resources/image.png", "package"));
        var destination = CreateDestination();
        var existing = Path.Combine(destination, "resources", "image.png");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "existing", TestContext.Current.CancellationToken);

        await YmmpxPackageExtractor.ExtractAsync(session, destination, new YmmpxExtractionOptions { OverwritePolicy = YmmpxOverwritePolicy.Overwrite }, TestContext.Current.CancellationToken);

        Assert.Equal("package", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("C:/escape.bin")]
    public async Task DefendsDestinationBoundaryEvenForNonReaderProviders(string unsafePath)
    {
        var package = new LoadedYmmpxPackage(
            LoadedYmmpxSourceFormat.V2,
            new LoadedYmmpxProject("project.ymmp", "{}"),
            [new LoadedYmmpxResource(unsafePath, "escape.bin", 1, ManifestResourceKind.File, null)],
            []);
        var destination = CreateDestination();
        var provider = new TestContentProvider(package, [42]);

        var exception = await Assert.ThrowsAsync<YmmpxExtractionException>(() => YmmpxPackageExtractor.ExtractAsync(provider, destination, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(YmmpxExtractionError.UnsafePath, exception.Error);
        Assert.False(File.Exists(Path.Combine(temporaryRoot, "escape.bin")));
    }

    [Fact]
    public async Task RejectsUnsafeOriginalProjectFileNameBeforeWriting()
    {
        var project = new LoadedYmmpxProject("project.ymmp", "{}") { OriginalFileName = "../escape.ymmp" };
        var package = new LoadedYmmpxPackage(LoadedYmmpxSourceFormat.V2, project, [], []);
        var provider = new TestContentProvider(package, []);

        var exception = await Assert.ThrowsAsync<YmmpxExtractionException>(() => YmmpxPackageExtractor.ExtractAsync(provider, CreateDestination(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(YmmpxExtractionError.UnsafePath, exception.Error);
    }

    [Fact]
    public async Task HonorsCancellationBeforeWriting()
    {
        await using var session = await OpenV1SessionAsync(archive => WriteEntry(archive, "resources/image.png", "image"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => YmmpxPackageExtractor.ExtractAsync(session, CreateDestination(), cancellationToken: cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, recursive: true);
    }

    private string CreateDestination()
    {
        var path = Path.Combine(temporaryRoot, "output");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<YmmpxPackageSession> OpenV1SessionAsync(Action<ZipArchive> configure, string projectPath = "project.ymmp") =>
        await LegacyV1Reader.OpenAsync(new MemoryStream(CreateV1Archive(configure, projectPath)), TestContext.Current.CancellationToken);

    private static byte[] CreateV1Archive(Action<ZipArchive> configure, string projectPath = "project.ymmp")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, projectPath, "{\"title\":\"test\"}");
            WriteEntry(archive, "_ymmpx_project_path.txt", projectPath);
            WriteEntry(archive, "links.json", "{}");
            configure(archive);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content) => WriteBytesEntry(archive, path, Encoding.UTF8.GetBytes(content));

    private static void WriteBytesEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private sealed class TestContentProvider(LoadedYmmpxPackage package, byte[] content) : IYmmpxResourceContentProvider
    {
        public LoadedYmmpxPackage Package { get; } = package;

        public ValueTask<Stream> OpenResourceReadAsync(LoadedYmmpxResource resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false));
        }
    }
}
