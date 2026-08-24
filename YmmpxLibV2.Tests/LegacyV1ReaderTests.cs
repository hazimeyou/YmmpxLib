using System.IO.Compression;
using System.Text;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class LegacyV1ReaderTests
{
    [Fact]
    public async Task ReadsCurrentV101StylePackageThroughFormatRouting()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{\"resources/image.png\":\"resources/image.png\"}", archive =>
            WriteEntry(archive, "resources/image.png", "image"));

        var loaded = await ReadAsync(package);

        Assert.Equal(LoadedYmmpxSourceFormat.LegacyV1, loaded.SourceFormat);
        Assert.Equal("project.ymmp", loaded.Project.PackagePath);
        Assert.Equal("project.ymmp", loaded.Project.OriginalFileName);
        Assert.Equal("{\"title\":\"test\"}", loaded.Project.Content);
        Assert.Single(loaded.Links);
        var resource = Assert.Single(loaded.Resources);
        Assert.Equal(ManifestResourceKind.Image, resource.Kind);
        Assert.Equal("resources/image.png", resource.PackagePath);
    }

    [Fact]
    public async Task ReadsLegacyManifestJsonVariant()
    {
        const string manifest = "{\"Files\":[{\"OriginalPath\":\"C:/source/a.psd\",\"BundlePath\":\"resources/a.psd\"}]}";
        var package = CreateV1Archive("project.ymmp", "manifest.json", manifest, archive => WriteEntry(archive, "resources/a.psd", "psd"), includeMarker: false);

        var loaded = await ReadAsync(package);

        Assert.Equal("C:/source/a.psd", Assert.Single(loaded.Links).OriginalReference);
        Assert.Equal(ManifestResourceKind.Psd, Assert.Single(loaded.Resources).Kind);
    }

    [Fact]
    public async Task UsesManifestWhenLinksJsonIsEmptyLikeV1Extraction()
    {
        const string manifest = "{\"Files\":[{\"OriginalPath\":\"C:/source/a.psd\",\"BundlePath\":\"resources/a.psd\"}]}";
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, "project.ymmp", "{\"title\":\"test\"}");
            WriteEntry(archive, "_ymmpx_project_path.txt", "project.ymmp");
            WriteEntry(archive, "links.json", "{}");
            WriteEntry(archive, "manifest.json", manifest);
            WriteEntry(archive, "resources/a.psd", "psd");
        });

        var loaded = await ReadAsync(package);

        Assert.Equal("C:/source/a.psd", Assert.Single(loaded.Links).OriginalReference);
    }

    [Fact]
    public async Task ReadsLegacyLinksTextVariantIncludingCommaInSource()
    {
        var package = CreateV1Archive("project.ymmp", "links.txt", "C:/source/a,b.wav,resources/a.wav", archive => WriteEntry(archive, "resources/a.wav", "audio"), includeMarker: false);

        var loaded = await ReadAsync(package);

        var link = Assert.Single(loaded.Links);
        Assert.Equal("C:/source/a,b.wav", link.OriginalReference);
        Assert.Equal("resources/a.wav", link.PackagePath);
    }

    [Fact]
    public async Task UsesProjectMarkerForNestedProject()
    {
        var package = CreateV1Archive("projects/nested.ymmp", "links.json", "{}", configure: null);

        var loaded = await ReadAsync(package);

        Assert.Equal("projects/nested.ymmp", loaded.Project.PackagePath);
        Assert.Equal("nested.ymmp", loaded.Project.OriginalFileName);
    }

    [Fact]
    public async Task ReadsJapaneseProjectResourceAndLink()
    {
        var package = CreateV1Archive("プロジェクト/琴葉葵.ymmp", "links.json", "{\"元/立ち絵.psd\":\"resources/素材/立ち絵.psd\"}", archive =>
            WriteEntry(archive, "resources/素材/立ち絵.psd", "psd"));

        var loaded = await ReadAsync(package);

        Assert.Equal("プロジェクト/琴葉葵.ymmp", loaded.Project.PackagePath);
        Assert.Equal("琴葉葵.ymmp", loaded.Project.OriginalFileName);
        Assert.Equal("元/立ち絵.psd", Assert.Single(loaded.Links).OriginalReference);
        Assert.Equal("立ち絵.psd", Assert.Single(loaded.Resources).FileName);
    }

    [Fact]
    public async Task ClassifiesImageAudioVideoAndPsdResources()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{}", archive =>
        {
            WriteEntry(archive, "resources/image.png", "image");
            WriteEntry(archive, "resources/audio.wav", "audio");
            WriteEntry(archive, "resources/video.mp4", "video");
            WriteEntry(archive, "resources/character.psd", "psd");
        });

        var loaded = await ReadAsync(package);

        Assert.Contains(loaded.Resources, resource => resource.Kind == ManifestResourceKind.Image);
        Assert.Contains(loaded.Resources, resource => resource.Kind == ManifestResourceKind.Audio);
        Assert.Contains(loaded.Resources, resource => resource.Kind == ManifestResourceKind.Video);
        Assert.Contains(loaded.Resources, resource => resource.Kind == ManifestResourceKind.Psd);
    }

    [Fact]
    public async Task PreservesImageSequenceGroupsAcrossDigitBoundaries()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{}", archive =>
        {
            foreach (var frame in new[] { "frame_8.png", "frame_9.png", "frame_10.png", "frame_11.png", "frame_99.png", "frame_100.png" })
                WriteEntry(archive, $"resources/sequence_1/{frame}", frame);
            WriteEntry(archive, "resources/sequence_2/other_1.png", "other");
        });

        var loaded = await ReadAsync(package);

        Assert.Equal(6, loaded.Resources.Count(resource => resource.GroupId == "sequence_1"));
        Assert.Single(loaded.Resources, resource => resource.GroupId == "sequence_2");
        Assert.All(loaded.Resources.Where(resource => resource.GroupId is not null), resource => Assert.Equal(ManifestResourceKind.ImageSequence, resource.Kind));
    }

    [Fact]
    public async Task KeepsSameNamedResourcesInDifferentDirectoriesSeparate()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{}", archive =>
        {
            WriteEntry(archive, "resources/one/a.png", "one");
            WriteEntry(archive, "resources/two/a.png", "two");
        });

        var loaded = await ReadAsync(package);

        Assert.Equal(2, loaded.Resources.Count(resource => resource.FileName == "a.png"));
        Assert.NotEqual(loaded.Resources[0].PackagePath, loaded.Resources[1].PackagePath);
    }

    [Fact]
    public async Task DoesNotLoadResourceContentIntoTheCommonModel()
    {
        var bytes = new byte[3 * 1024 * 1024];
        var package = CreateV1Archive("project.ymmp", "links.json", "{}", archive => WriteBytesEntry(archive, "resources/video.mp4", bytes));

        var loaded = await ReadAsync(package);

        var resource = Assert.Single(loaded.Resources);
        Assert.Equal(bytes.Length, resource.Length);
        Assert.Equal("video.mp4", resource.FileName);
    }

    [Fact]
    public async Task RejectsMissingProject()
    {
        var package = CreateArchive(archive => WriteEntry(archive, "links.json", "{}"));

        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(package));

        Assert.Equal(LegacyV1ReadError.NotLegacyV1, exception.Error);
    }

    [Fact]
    public async Task RejectsUnsafeMarkerPath()
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, "_ymmpx_project_path.txt", "../project.ymmp");
            WriteEntry(archive, "project.ymmp", "{}");
            WriteEntry(archive, "links.json", "{}");
        });

        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(package));

        Assert.Equal(LegacyV1ReadError.NotLegacyV1, exception.Error);
    }

    [Fact]
    public async Task RejectsUnsafeResourceEntryAfterDetection()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{}", archive => WriteEntry(archive, "../unsafe.bin", "unsafe"));

        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(package));

        Assert.Equal(LegacyV1ReadError.UnsafePath, exception.Error);
    }

    [Fact]
    public async Task RejectsDuplicateCriticalEntry()
    {
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, "project.ymmp", "{}");
            WriteEntry(archive, "_ymmpx_project_path.txt", "project.ymmp");
            WriteEntry(archive, "links.json", "{}");
            WriteEntry(archive, "LINKS.JSON", "{}");
        });

        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(package));

        Assert.Equal(LegacyV1ReadError.DuplicateEntry, exception.Error);
    }

    [Fact]
    public async Task RejectsMalformedLinksJson()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{", configure: null);

        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(package));

        Assert.Equal(LegacyV1ReadError.InvalidLinks, exception.Error);
    }

    [Fact]
    public async Task IgnoresMissingLinkedResourceLikeV1Extraction()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{\"C:/source/missing.wav\":\"resources/missing.wav\"}", configure: null);

        var loaded = await ReadAsync(package);

        Assert.Empty(loaded.Links);
        Assert.Empty(loaded.Resources);
    }

    [Fact]
    public async Task RejectsOversizedLinkMetadata()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", new string(' ', checked((int)LegacyV1Reader.MaxLinkMetadataLength + 1)), configure: null);

        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(package));

        Assert.Equal(LegacyV1ReadError.MetadataTooLarge, exception.Error);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var package = CreateV1Archive("project.ymmp", "links.json", "{}", configure: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => LegacyV1Reader.ReadAsync(new MemoryStream(package), cancellation.Token));
    }

    [Fact]
    public async Task RejectsV2Package()
    {
        var descriptor = YmmpxFormatDescriptorSerializer.Serialize(new YmmpxFormatDescriptor(2, 0, PackageManifest.FileName));
        var package = CreateArchive(archive =>
        {
            WriteEntry(archive, YmmpxFormatDescriptor.FileName, descriptor);
            WriteEntry(archive, PackageManifest.FileName, "{}");
        });

        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(package));

        Assert.Equal(LegacyV1ReadError.NotLegacyV1, exception.Error);
    }

    [Fact]
    public async Task RejectsUnrelatedZip()
    {
        var exception = await Assert.ThrowsAsync<LegacyV1ReadException>(() => ReadAsync(CreateArchive(archive => WriteEntry(archive, "readme.txt", "hello"))));

        Assert.Equal(LegacyV1ReadError.NotLegacyV1, exception.Error);
    }

    private static async Task<LoadedYmmpxPackage> ReadAsync(byte[] package) =>
        await LegacyV1Reader.ReadAsync(new MemoryStream(package), TestContext.Current.CancellationToken);

    private static byte[] CreateV1Archive(
        string projectPath,
        string linksPath,
        string linksContent,
        Action<ZipArchive>? configure,
        bool includeMarker = true) =>
        CreateArchive(archive =>
        {
            WriteEntry(archive, projectPath, "{\"title\":\"test\"}");
            if (includeMarker)
                WriteEntry(archive, "_ymmpx_project_path.txt", projectPath);
            WriteEntry(archive, linksPath, linksContent);
            configure?.Invoke(archive);
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

    private static void WriteEntry(ZipArchive archive, string path, string content) =>
        WriteBytesEntry(archive, path, Encoding.UTF8.GetBytes(content));

    private static void WriteBytesEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
