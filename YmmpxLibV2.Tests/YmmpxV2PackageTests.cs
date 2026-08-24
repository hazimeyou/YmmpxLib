using System.IO.Compression;
using System.Text.Json.Nodes;
using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class YmmpxV2PackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "YmmpxLibV2Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WritesReadsResolvesAndExtractsFormat20RoundTrip()
    {
        var source = Path.Combine(root, "source");
        var assets = Path.Combine(source, "素材");
        var sequence = Path.Combine(source, "sequence");
        Directory.CreateDirectory(assets); Directory.CreateDirectory(sequence);
        var image = Path.Combine(assets, "画像.png");
        var audio = Path.Combine(assets, "audio.wav");
        var video = Path.Combine(assets, "video.mp4");
        var psd = Path.Combine(assets, "立ち絵.psd");
        await File.WriteAllBytesAsync(image, [1, 2, 3], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(audio, [4, 5], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(video, [6, 7, 8], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(psd, [9, 10], TestContext.Current.CancellationToken);
        for (var index = 1; index <= 110; index++)
            await File.WriteAllTextAsync(Path.Combine(sequence, $"frame_{index}.png"), index.ToString(), TestContext.Current.CancellationToken);

        var projectPath = Path.Combine(source, "日本語.ymmp");
        var project = $$"""
        {"$type":"YMM.Project","Unknown":{"enabled":true,"count":3},"Unused":{"FilePath":null},"Image":{"FilePath":{{System.Text.Json.JsonSerializer.Serialize(image)}}},"Audio":{"FilePath":{{System.Text.Json.JsonSerializer.Serialize(audio)}}},"Video":{"FilePath":{{System.Text.Json.JsonSerializer.Serialize(video)}}},"Psd":{"FilePath":{{System.Text.Json.JsonSerializer.Serialize(psd)}},"EnableLayers":["face"],"EnableLayerPaths":["顔/目"]},"Sequence":{"$type":"YukkuriMovieMaker.Project.Items.VideoItem, YukkuriMovieMaker","FilePath":{{System.Text.Json.JsonSerializer.Serialize(Path.Combine(sequence, "frame_1.png"))}}},"Text":{{System.Text.Json.JsonSerializer.Serialize(psd)}}}
        """;
        await File.WriteAllTextAsync(projectPath, project, TestContext.Current.CancellationToken);
        var packagePath = Path.Combine(root, "package.ymmpx");

        await YmmpxV2Writer.WriteAsync(new YmmpxV2WriteRequest(projectPath, packagePath), TestContext.Current.CancellationToken);

        PackageManifest manifest;
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            Assert.NotNull(archive.GetEntry(YmmpxFormatDescriptor.FileName));
            var manifestEntry = archive.GetEntry(PackageManifest.FileName)!;
            using var reader = new StreamReader(manifestEntry.Open());
            manifest = PackageManifestSerializer.Deserialize(await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
            Assert.Equal(114, manifest.Resources.Count);
            Assert.All(manifest.Resources, resource => Assert.Null(resource.OriginalPath));
            Assert.All(manifest.Resources, resource => Assert.Equal(64, resource.Sha256.Length));
            Assert.Equal(110, manifest.Resources.Count(resource => resource.GroupId == "sequence_1"));
        }

        using var input = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var detected = await YmmpxFormatDetector.DetectAsync(input, TestContext.Current.CancellationToken);
        await using var session = await YmmpxV2Reader.OpenAsync(input, TestContext.Current.CancellationToken);
        var destination = Path.Combine(root, "展開");
        var resolution = YmmpxProjectReferenceResolver.Resolve(session.Package.Project, ProjectResourceReferenceMapper.FromPackage(session.Package), destination, TestContext.Current.CancellationToken);
        await YmmpxPackageExtractor.ExtractAsync(session, destination, new YmmpxExtractionOptions { ProjectOverride = resolution.Project }, TestContext.Current.CancellationToken);

        var output = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(destination, "project.ymmp"), TestContext.Current.CancellationToken))!;
        Assert.Equal(YmmpxFormatDetectionStatus.SupportedV2, detected.Status);
        Assert.Equal(LoadedYmmpxSourceFormat.V2, session.Package.SourceFormat);
        Assert.Equal(5, resolution.ReplacedReferenceCount);
        Assert.True(File.Exists(output["Image"]!["FilePath"]!.GetValue<string>()));
        Assert.True(File.Exists(output["Audio"]!["FilePath"]!.GetValue<string>()));
        Assert.True(File.Exists(output["Video"]!["FilePath"]!.GetValue<string>()));
        Assert.True(File.Exists(output["Psd"]!["FilePath"]!.GetValue<string>()));
        Assert.True(File.Exists(output["Sequence"]!["FilePath"]!.GetValue<string>()));
        Assert.Equal("face", output["Psd"]!["EnableLayers"]![0]!.GetValue<string>());
        Assert.Equal("顔/目", output["Psd"]!["EnableLayerPaths"]![0]!.GetValue<string>());
        Assert.True(output["Unknown"]!["enabled"]!.GetValue<bool>());
        Assert.Null(output["Unused"]!["FilePath"]);
        Assert.Equal(psd, output["Text"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(destination, "resources", "sequence_1", "frame_110.png")));
        Assert.Equal(await File.ReadAllBytesAsync(psd, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(output["Psd"]!["FilePath"]!.GetValue<string>(), TestContext.Current.CancellationToken));
        var sourceIdentity = await ResourceIdentity.CreateAsync(psd, TestContext.Current.CancellationToken);
        var extractedIdentity = await ResourceIdentity.CreateAsync(output["Psd"]!["FilePath"]!.GetValue<string>(), TestContext.Current.CancellationToken);
        var manifestPsd = Assert.Single(manifest.Resources, resource => resource.Kind == ManifestResourceKind.Psd);
        Assert.Equal(sourceIdentity.Sha256, manifestPsd.Sha256);
        Assert.Equal(sourceIdentity.Sha256, extractedIdentity.Sha256);
    }

    [Fact]
    public async Task RejectsV1AndMissingManifestPackages()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var descriptor = archive.CreateEntry(YmmpxFormatDescriptor.FileName);
            await using var writer = new StreamWriter(descriptor.Open());
            await writer.WriteAsync(YmmpxFormatDescriptorSerializer.Serialize(new YmmpxFormatDescriptor(2, 0, PackageManifest.FileName)));
        }
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => YmmpxV2Reader.OpenAsync(stream, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
