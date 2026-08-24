using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests.Compatibility;

public sealed class LegacyV1CompatibilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "YmmpxLibV2Compatibility", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("links.json")]
    [InlineData("manifest.json")]
    [InlineData("links.txt")]
    public async Task LegacyV1LinkVariant_CanBeDetectedResolvedAndExtractedByV2Core(string linkFormat)
    {
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\source\\素材\\画像.png"] = "resources/素材/画像.png",
            ["C:\\source\\音声.wav"] = "resources/音声.wav",
            ["C:\\source\\動画.mp4"] = "resources/動画.mp4",
            ["C:\\source\\立ち絵.psd"] = "resources/素材/立ち絵.psd",
            ["C:\\source\\sequence\\frame_8.png"] = "resources/sequence_1/frame_8.png"
        };
        var resources = new Dictionary<string, byte[]>
        {
            ["resources/素材/画像.png"] = [1, 2],
            ["resources/音声.wav"] = [3, 4],
            ["resources/動画.mp4"] = [5, 6],
            ["resources/素材/立ち絵.psd"] = [7, 8],
            ["resources/sequence_1/frame_8.png"] = [8],
            ["resources/sequence_1/frame_9.png"] = [9],
            ["resources/sequence_1/frame_10.png"] = [10],
            ["resources/sequence_1/frame_11.png"] = [11],
            ["resources/sequence_1/frame_99.png"] = [99],
            ["resources/sequence_1/frame_100.png"] = [100]
        };
        var project = """
            {"$type":"YMM.Project","Image":{"FilePath":"c:/SOURCE/素材/画像.png"},"Audio":{"FilePath":"C:\\source\\音声.wav"},"Video":{"FilePath":"C:\\source\\動画.mp4"},"Psd":{"FilePath":"C:\\source\\立ち絵.psd","EnableLayers":["face"],"EnableLayerPaths":["顔/目"],"Unknown":true},"Sequence":{"$type":"YukkuriMovieMaker.Project.Items.VideoItem, YukkuriMovieMaker","FilePath":"C:\\source\\sequence\\frame_8.png"},"Text":"C:\\source\\立ち絵.psd","Null":{"FilePath":null}}
            """;
        var package = CreateLegacyPackage("projects/日本語プロジェクト.ymmp", project, linkFormat, references, resources);
        using var input = new MemoryStream(package);

        var detection = await YmmpxFormatDetector.DetectAsync(input, TestContext.Current.CancellationToken);
        await using var session = await LegacyV1Reader.OpenAsync(input, TestContext.Current.CancellationToken);
        var destination = Path.Combine(root, linkFormat);
        var resolution = YmmpxProjectReferenceResolver.Resolve(session.Package.Project, ProjectResourceReferenceMapper.FromLegacyPackage(session.Package), destination, TestContext.Current.CancellationToken);
        await YmmpxPackageExtractor.ExtractAsync(session, destination, new YmmpxExtractionOptions { ProjectOverride = resolution.Project }, TestContext.Current.CancellationToken);

        var outputPath = Path.Combine(destination, "日本語プロジェクト.ymmp");
        var output = JsonNode.Parse(await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken))!;
        Assert.Equal(YmmpxFormatDetectionStatus.LegacyV1, detection.Status);
        Assert.Equal("日本語プロジェクト.ymmp", session.Package.Project.OriginalFileName);
        Assert.Equal(5, resolution.ReplacedReferenceCount);
        Assert.Equal(Path.Combine(destination, "resources", "素材", "立ち絵.psd"), output["Psd"]!["FilePath"]!.GetValue<string>());
        Assert.Equal("face", output["Psd"]!["EnableLayers"]![0]!.GetValue<string>());
        Assert.Equal("顔/目", output["Psd"]!["EnableLayerPaths"]![0]!.GetValue<string>());
        Assert.True(output["Psd"]!["Unknown"]!.GetValue<bool>());
        Assert.Equal("C:\\source\\立ち絵.psd", output["Text"]!.GetValue<string>());
        Assert.Null(output["Null"]!["FilePath"]);
        Assert.Equal(Path.Combine(destination, "resources", "sequence_1", "frame_8.png"), output["Sequence"]!["FilePath"]!.GetValue<string>());
        Assert.All(resources, resource => Assert.Equal(resource.Value, File.ReadAllBytes(GetDestinationPath(destination, resource.Key))));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static byte[] CreateLegacyPackage(string projectPath, string project, string linkFormat, IReadOnlyDictionary<string, string> references, IReadOnlyDictionary<string, byte[]> resources)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, projectPath, project);
            WriteText(archive, "_ymmpx_project_path.txt", projectPath);
            WriteText(archive, linkFormat, CreateLinks(linkFormat, references));
            foreach (var resource in resources) WriteBytes(archive, resource.Key, resource.Value);
        }
        return stream.ToArray();
    }

    private static string CreateLinks(string linkFormat, IReadOnlyDictionary<string, string> references) => linkFormat switch
    {
        "links.json" => JsonSerializer.Serialize(references),
        "manifest.json" => JsonSerializer.Serialize(new { Files = references.Select(pair => new { OriginalPath = pair.Key, BundlePath = pair.Value }) }),
        "links.txt" => string.Join(Environment.NewLine, references.Select(pair => $"{pair.Key},{pair.Value}")),
        _ => throw new ArgumentOutOfRangeException(nameof(linkFormat))
    };

    private static void WriteText(ZipArchive archive, string path, string content) => WriteBytes(archive, path, Encoding.UTF8.GetBytes(content));

    private static void WriteBytes(ZipArchive archive, string path, byte[] content)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(content);
    }

    private static string GetDestinationPath(string destination, string packagePath) =>
        Path.Combine([destination, .. packagePath.Split('/')]);
}
