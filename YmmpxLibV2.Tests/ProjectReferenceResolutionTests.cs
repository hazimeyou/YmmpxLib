using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class ProjectReferenceResolutionTests : IDisposable
{
    private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "YmmpxLibV2Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolvesOnlyRecursiveFilePathValuesAndPreservesPsdState()
    {
        const string projectText = """
            {"$type":"YMM.Project","Description":"C:\\source\\character.psd","FilePath":null,"Psd":{"FilePath":"C:\\source\\character.psd","EnableLayers":["face","mouth"],"EnableLayerPaths":["顔/目"],"Unknown":{"number":3}},"Unmatched":{"FilePath":"C:\\source\\other.psd"}}
            """;
        var raw = new LoadedYmmpxProject("project.ymmp", projectText);
        var destination = CreateDestination();

        var result = YmmpxProjectReferenceResolver.Resolve(raw,
            [new ProjectResourceReference("C:/source/character.psd", "resources/character.psd")],
            destination,
            TestContext.Current.CancellationToken);

        var root = JsonNode.Parse(result.Project.Content)!.AsObject();
        var psd = root["Psd"]!.AsObject();
        Assert.Equal(1, result.ReplacedReferenceCount);
        Assert.Null(root["FilePath"]);
        Assert.Equal("C:\\source\\character.psd", root["Description"]!.GetValue<string>());
        Assert.Equal(Path.Combine(destination, "resources", "character.psd"), psd["FilePath"]!.GetValue<string>());
        Assert.Equal("face", psd["EnableLayers"]![0]!.GetValue<string>());
        Assert.Equal("顔/目", psd["EnableLayerPaths"]![0]!.GetValue<string>());
        Assert.Equal(3, psd["Unknown"]!["number"]!.GetValue<int>());
        Assert.Equal("C:\\source\\other.psd", root["Unmatched"]!["FilePath"]!.GetValue<string>());
        Assert.Equal(projectText, raw.Content);
    }

    [Fact]
    public void ResolvesAllUsesOfTheSameResourceWithoutMatchingByFileName()
    {
        var project = new LoadedYmmpxProject("project.ymmp", "{\"Items\":[{\"FilePath\":\"C:/A/aoi.psd\"},{\"Nested\":{\"FilePath\":\"C:/A/aoi.psd\"}},{\"FilePath\":\"C:/Other/aoi.psd\"}]}");
        var destination = CreateDestination();

        var result = YmmpxProjectReferenceResolver.Resolve(project,
            [new ProjectResourceReference("C:/A/aoi.psd", "resources/aoi-a.psd")],
            destination,
            TestContext.Current.CancellationToken);

        var items = JsonNode.Parse(result.Project.Content)!["Items"]!.AsArray();
        Assert.Equal(2, result.ReplacedReferenceCount);
        Assert.Equal(Path.Combine(destination, "resources", "aoi-a.psd"), items[0]!["FilePath"]!.GetValue<string>());
        Assert.Equal(Path.Combine(destination, "resources", "aoi-a.psd"), items[1]!["Nested"]!["FilePath"]!.GetValue<string>());
        Assert.Equal("C:/Other/aoi.psd", items[2]!["FilePath"]!.GetValue<string>());
    }

    [Fact]
    public void RejectsAmbiguousAndUnsafeReferenceMappings()
    {
        var project = new LoadedYmmpxProject("project.ymmp", "{\"FilePath\":\"C:/A/aoi.psd\"}");
        var destination = CreateDestination();

        var ambiguous = Assert.Throws<YmmpxProjectResolutionException>(() => YmmpxProjectReferenceResolver.Resolve(project,
            [new ProjectResourceReference("C:/A/aoi.psd", "resources/one.psd"), new ProjectResourceReference("C:/A/aoi.psd", "resources/two.psd")], destination, TestContext.Current.CancellationToken));
        var unsafePath = Assert.Throws<YmmpxProjectResolutionException>(() => YmmpxProjectReferenceResolver.Resolve(project,
            [new ProjectResourceReference("C:/A/aoi.psd", "../outside.psd")], destination, TestContext.Current.CancellationToken));

        Assert.Equal(YmmpxProjectResolutionError.AmbiguousReference, ambiguous.Error);
        Assert.Equal(YmmpxProjectResolutionError.UnsafePackagePath, unsafePath.Error);
    }

    [Fact]
    public void HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

#pragma warning disable xUnit1051 // This test intentionally supplies a caller-cancelled token.
        Assert.Throws<OperationCanceledException>(() => YmmpxProjectReferenceResolver.Resolve(
            new LoadedYmmpxProject("project.ymmp", "{}"), [], CreateDestination(), cancellation.Token));
#pragma warning restore xUnit1051
    }

    [Theory]
    [InlineData("links.json", "{\"resources/image.png\":\"resources/image.png\"}")]
    [InlineData("manifest.json", "{\"Files\":[{\"OriginalPath\":\"resources/image.png\",\"BundlePath\":\"resources/image.png\"}]}")]
    [InlineData("links.txt", "resources/image.png,resources/image.png")]
    public async Task CompletesV1DetectionReadResolutionAndExtractionForEveryLinkVariant(string linksPath, string linksContent)
    {
        var project = "{\"$type\":\"YMM.Project\",\"Item\":{\"FilePath\":\"resources/image.png\",\"Text\":\"resources/image.png\"}}";
        var bytes = CreateV1Archive(project, linksPath, linksContent, archive => WriteEntry(archive, "resources/image.png", "image"));
        using var input = new MemoryStream(bytes);

        var detected = await YmmpxFormatDetector.DetectAsync(input, TestContext.Current.CancellationToken);
        await using var session = await LegacyV1Reader.OpenAsync(input, TestContext.Current.CancellationToken);
        var destination = CreateDestination();
        var resolved = YmmpxProjectReferenceResolver.Resolve(
            session.Package.Project,
            ProjectResourceReferenceMapper.FromLegacyPackage(session.Package),
            destination,
            TestContext.Current.CancellationToken);

        await YmmpxPackageExtractor.ExtractAsync(session, destination,
            new YmmpxExtractionOptions { ProjectOverride = resolved.Project }, TestContext.Current.CancellationToken);

        var outputProject = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(destination, "project.ymmp"), TestContext.Current.CancellationToken))!;
        var restoredPath = outputProject["Item"]!["FilePath"]!.GetValue<string>();
        Assert.Equal(YmmpxFormatDetectionStatus.LegacyV1, detected.Status);
        Assert.Equal(1, resolved.ReplacedReferenceCount);
        Assert.Equal(Path.Combine(destination, "resources", "image.png"), restoredPath);
        Assert.True(File.Exists(restoredPath));
        Assert.Equal("resources/image.png", outputProject["Item"]!["Text"]!.GetValue<string>());
    }

    [Fact]
    public async Task RestoresImageSequenceRepresentativePathAndAllFrames()
    {
        var project = "{\"$type\":\"YukkuriMovieMaker.Project.Items.VideoItem, YukkuriMovieMaker\",\"FilePath\":\"resources/sequence_1/frame_9.png\"}";
        var bytes = CreateV1Archive(project, "links.json", "{\"resources/sequence_1/frame_9.png\":\"resources/sequence_1/frame_9.png\"}", archive =>
        {
            foreach (var name in new[] { "frame_8.png", "frame_9.png", "frame_10.png", "frame_11.png" })
                WriteEntry(archive, $"resources/sequence_1/{name}", name);
        });
        using var input = new MemoryStream(bytes);
        await using var session = await LegacyV1Reader.OpenAsync(input, TestContext.Current.CancellationToken);
        var destination = CreateDestination();
        var resolved = YmmpxProjectReferenceResolver.Resolve(session.Package.Project, ProjectResourceReferenceMapper.FromLegacyPackage(session.Package), destination, TestContext.Current.CancellationToken);

        await YmmpxPackageExtractor.ExtractAsync(session, destination, new YmmpxExtractionOptions { ProjectOverride = resolved.Project }, TestContext.Current.CancellationToken);

        var output = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(destination, "project.ymmp"), TestContext.Current.CancellationToken))!;
        Assert.Equal(Path.Combine(destination, "resources", "sequence_1", "frame_9.png"), output["FilePath"]!.GetValue<string>());
        Assert.All(new[] { "frame_8.png", "frame_9.png", "frame_10.png", "frame_11.png" }, name => Assert.True(File.Exists(Path.Combine(destination, "resources", "sequence_1", name))));
        Assert.All(session.Package.Resources, resource => Assert.Equal("sequence_1", resource.GroupId));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, recursive: true);
    }

    private string CreateDestination()
    {
        var destination = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        return destination;
    }

    private static byte[] CreateV1Archive(string project, string linksPath, string linksContent, Action<ZipArchive> configure)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "project.ymmp", project);
            WriteEntry(archive, "_ymmpx_project_path.txt", "project.ymmp");
            WriteEntry(archive, linksPath, linksContent);
            configure(archive);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }
}
