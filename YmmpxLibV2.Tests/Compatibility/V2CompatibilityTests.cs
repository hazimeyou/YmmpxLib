using System.IO.Compression;
using System.Text.Json.Nodes;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests.Compatibility;

public sealed class V2CompatibilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "YmmpxLibV2Compatibility", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Format20Package_PreservesResourcesProjectNameAndPsdStateThroughV2Core()
    {
        var source = Path.Combine(root, "source");
        var assets = Path.Combine(source, "素材");
        var sequence = Path.Combine(source, "連番");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(sequence);
        var image = await WriteAsync(assets, "画像.png", [1]);
        var audio = await WriteAsync(assets, "音声.wav", [2]);
        var video = await WriteAsync(assets, "動画.mp4", [3]);
        var psd = await WriteAsync(assets, "立ち絵.psd", [4]);
        var imageItem = await WriteAsync(assets, "静止画.png", [5]);
        foreach (var number in new[] { 8, 9, 10, 11, 99, 100 }) await WriteAsync(sequence, $"frame_{number}.png", [(byte)number]);
        var projectPath = Path.Combine(source, "日本語プロジェクト.ymmp");
        var project = new JsonObject
        {
            ["$type"] = "YMM.Project",
            ["Image"] = FilePathNode(image),
            ["Audio"] = FilePathNode(audio),
            ["Video"] = FilePathNode(video),
            ["Psd"] = new JsonObject
            {
                ["FilePath"] = psd,
                ["EnableLayers"] = new JsonArray("face"),
                ["EnableLayerPaths"] = new JsonArray("顔/目"),
                ["Unknown"] = new JsonObject { ["number"] = 1 }
            },
            ["ImageItem"] = new JsonObject { ["$type"] = "YukkuriMovieMaker.Project.Items.ImageItem, YukkuriMovieMaker", ["FilePath"] = imageItem },
            ["Sequence"] = new JsonObject { ["$type"] = "YukkuriMovieMaker.Project.Items.VideoItem, YukkuriMovieMaker", ["FilePath"] = Path.Combine(sequence, "frame_8.png") },
            ["Null"] = new JsonObject { ["FilePath"] = null },
            ["Text"] = psd
        }.ToJsonString();
        await File.WriteAllTextAsync(projectPath, project, TestContext.Current.CancellationToken);
        var packagePath = Path.Combine(root, "format20.ymmpx");

        await YmmpxV2Writer.WriteAsync(new YmmpxV2WriteRequest(projectPath, packagePath), TestContext.Current.CancellationToken);
        using var input = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var detection = await YmmpxFormatDetector.DetectAsync(input, TestContext.Current.CancellationToken);
        await using var session = await YmmpxV2Reader.OpenAsync(input, TestContext.Current.CancellationToken);
        var destination = Path.Combine(root, "output");
        var resolved = YmmpxProjectReferenceResolver.Resolve(session.Package.Project, ProjectResourceReferenceMapper.FromPackage(session.Package), destination, TestContext.Current.CancellationToken);
        await YmmpxPackageExtractor.ExtractAsync(session, destination, new YmmpxExtractionOptions { ProjectOverride = resolved.Project }, TestContext.Current.CancellationToken);

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            Assert.NotNull(archive.GetEntry("project.ymmp"));
            var manifest = PackageManifestSerializer.Deserialize(await new StreamReader(archive.GetEntry(PackageManifest.FileName)!.Open()).ReadToEndAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PackageManifest.CurrentSchemaVersion, manifest.SchemaVersion);
            Assert.Equal("日本語プロジェクト.ymmp", manifest.Project!.OriginalFileName);
            Assert.All(manifest.Resources, resource => Assert.Null(resource.OriginalPath));
            Assert.All(manifest.Resources, resource => Assert.Equal(64, resource.Sha256.Length));
        }

        var output = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(destination, "日本語プロジェクト.ymmp"), TestContext.Current.CancellationToken))!;
        Assert.Equal(YmmpxFormatDetectionStatus.SupportedV2, detection.Status);
        Assert.Equal(LoadedYmmpxSourceFormat.V2, session.Package.SourceFormat);
        Assert.Equal("日本語プロジェクト.ymmp", session.Package.Project.OriginalFileName);
        Assert.Equal(6, resolved.ReplacedReferenceCount);
        Assert.Equal("face", output["Psd"]!["EnableLayers"]![0]!.GetValue<string>());
        Assert.Equal("顔/目", output["Psd"]!["EnableLayerPaths"]![0]!.GetValue<string>());
        Assert.Equal(1, output["Psd"]!["Unknown"]!["number"]!.GetValue<int>());
        Assert.Null(output["Null"]!["FilePath"]);
        Assert.Equal(psd, output["Text"]!.GetValue<string>());
        Assert.Equal(Path.Combine(destination, "resources", "sequence_1", "frame_8.png"), output["Sequence"]!["FilePath"]!.GetValue<string>());
        Assert.All(new[] { 8, 9, 10, 11, 99, 100 }, number => Assert.True(File.Exists(Path.Combine(destination, "resources", "sequence_1", $"frame_{number}.png"))));
        Assert.DoesNotContain(session.Package.Resources, resource => resource.FileName == "静止画.png" && resource.Kind == ManifestResourceKind.ImageSequence);
        Assert.Equal(await File.ReadAllBytesAsync(psd, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(output["Psd"]!["FilePath"]!.GetValue<string>(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriterOptionsPackage_CanStillBeReadResolvedAndExtracted()
    {
        var source = Path.Combine(root, "options");
        Directory.CreateDirectory(source);
        var included = await WriteAsync(source, "included.png", [1]);
        var excluded = await WriteAsync(source, "excluded.png", [2]);
        var projectPath = Path.Combine(source, "options.ymmp");
        var project = "{\"LayoutXml\":\"layout\",\"ToolStates\":{},\"Included\":{\"FilePath\":" + System.Text.Json.JsonSerializer.Serialize(included) + "},\"Excluded\":{\"FilePath\":" + System.Text.Json.JsonSerializer.Serialize(excluded) + "}}";
        await File.WriteAllTextAsync(projectPath, project, TestContext.Current.CancellationToken);
        var updates = new List<YmmpxV2WriteProgress>();
        var packagePath = Path.Combine(root, "options.ymmpx");

        await YmmpxV2Writer.WriteAsync(new YmmpxV2WriteRequest(projectPath, packagePath)
        {
            Options = new YmmpxV2WriteOptions { ExcludedResources = [excluded], IncludeProjectUiSettings = false, Progress = new ImmediateProgress(updates) }
        }, TestContext.Current.CancellationToken);
        using var input = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var session = await YmmpxV2Reader.OpenAsync(input, TestContext.Current.CancellationToken);
        var destination = Path.Combine(root, "options-output");
        var resolved = YmmpxProjectReferenceResolver.Resolve(session.Package.Project, ProjectResourceReferenceMapper.FromPackage(session.Package), destination, TestContext.Current.CancellationToken);
        await YmmpxPackageExtractor.ExtractAsync(session, destination, new YmmpxExtractionOptions { ProjectOverride = resolved.Project }, TestContext.Current.CancellationToken);

        var output = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(destination, "options.ymmp"), TestContext.Current.CancellationToken))!;
        Assert.Equal(project, await File.ReadAllTextAsync(projectPath, TestContext.Current.CancellationToken));
        Assert.Single(session.Package.Resources);
        Assert.Null(output["LayoutXml"]);
        Assert.Null(output["ToolStates"]);
        Assert.True(File.Exists(output["Included"]!["FilePath"]!.GetValue<string>()));
        Assert.Equal(excluded, output["Excluded"]!["FilePath"]!.GetValue<string>());
        Assert.Contains(updates, update => update.Stage == YmmpxV2WriteStage.Completed && update.Fraction == 1);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static async Task<string> WriteAsync(string directory, string fileName, byte[] content)
    {
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        return path;
    }

    private static JsonObject FilePathNode(string path) => new() { ["FilePath"] = path };

    private sealed class ImmediateProgress(List<YmmpxV2WriteProgress> updates) : IProgress<YmmpxV2WriteProgress>
    {
        public void Report(YmmpxV2WriteProgress value) => updates.Add(value);
    }
}
