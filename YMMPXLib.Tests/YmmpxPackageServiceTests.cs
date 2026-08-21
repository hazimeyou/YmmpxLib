using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using YmmpxLib;
using Xunit;

namespace YMMPXLib.Tests;

public sealed class YmmpxPackageServiceTests
{
    [Fact]
    public void FindFilePaths_EnumeratesNestedFilePathValues()
    {
        using var document = JsonDocument.Parse("""
        {
          "Scenes": [
            {
              "Items": [
                { "FilePath": "C:/media/voice.wav" },
                { "Nested": { "FilePath": "C:/media/effect.wav" } }
              ]
            }
          ],
          "Ignored": { "FilePath": 123 }
        }
        """);

        var paths = YmmpxProjectJson.FindFilePaths(document.RootElement).ToArray();

        Assert.Equal(new[]
        {
            "C:/media/voice.wav",
            "C:/media/effect.wav"
        }, paths);
    }

    [Fact]
    public void ReplaceFilePaths_OnlyReplacesEntriesFoundInLinkMap()
    {
        var node = JsonNode.Parse("""
        {
          "FilePath": "C:/source/keep.wav",
          "Children": [
            { "FilePath": "C:/source/replace.wav" },
            { "Nested": { "FilePath": "C:/source/skip.wav" } }
          ]
        }
        """)!;

        var replaced = YmmpxProjectJson.ReplaceFilePaths(
            node,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["C:/source/replace.wav"] = "resources/replace.wav"
            });

        Assert.Equal(1, replaced);
        Assert.Equal("C:/source/keep.wav", node["FilePath"]!.GetValue<string>());
        Assert.Equal("resources/replace.wav", node["Children"]![0]!["FilePath"]!.GetValue<string>());
        Assert.Equal("C:/source/skip.wav", node["Children"]![1]!["Nested"]!["FilePath"]!.GetValue<string>());
    }

    [Fact]
    public void ReplaceFilePaths_IgnoresNonStringValues()
    {
        var node = JsonNode.Parse("""{"FilePath":123}""")!;

        var replaced = YmmpxProjectJson.ReplaceFilePaths(
            node,
            new Dictionary<string, string> { ["123"] = "resources/123" });
        var packaged = YmmpxProjectJson.ReplaceFilePathsForPackaging(node, _ => "converted");

        Assert.Equal(0, replaced);
        Assert.Equal(0, packaged);
        Assert.Equal(123, node["FilePath"]!.GetValue<int>());
    }

    [Fact]
    public void ReplaceFilePaths_IgnoresInvalidPathStrings()
    {
        var node = JsonNode.Parse("""{"FilePath":"\u0000"}""")!;

        var replaced = YmmpxProjectJson.ReplaceFilePaths(
            node,
            new Dictionary<string, string> { ["invalid"] = "resources/invalid" });

        Assert.Equal(0, replaced);
        Assert.Equal("\u0000", node["FilePath"]!.GetValue<string>());
    }

    [Fact]
    public void ReplaceFilePaths_DoesNotMatchDifferentPathByFileName()
    {
        var node = JsonNode.Parse("""{"FilePath":"C:/other/same.wav"}""")!;

        var replaced = YmmpxProjectJson.ReplaceFilePaths(
            node,
            new Dictionary<string, string>
            {
                ["C:/source/same.wav"] = "resources/same.wav"
            });

        Assert.Equal(0, replaced);
        Assert.Equal("C:/other/same.wav", node["FilePath"]!.GetValue<string>());
    }

    [Fact]
    public void ReplaceFilePaths_PreservesPropertyNameCasing()
    {
        var node = JsonNode.Parse("""{"filepath":"C:/source/replace.wav"}""")!;

        var replaced = YmmpxProjectJson.ReplaceFilePaths(
            node,
            new Dictionary<string, string>
            {
                ["C:/source/replace.wav"] = "resources/replace.wav"
            });

        Assert.Equal(1, replaced);
        Assert.Null(node["FilePath"]);
        Assert.Equal("resources/replace.wav", node["filepath"]!.GetValue<string>());
    }

    [Fact]
    public void ReplaceFilePathsForPackaging_PreservesPropertyNameCasing()
    {
        var node = JsonNode.Parse("""{"filepath":"C:/source/replace.wav"}""")!;

        var replaced = YmmpxProjectJson.ReplaceFilePathsForPackaging(
            node,
            path => path == "C:/source/replace.wav" ? "resources/replace.wav" : null);

        Assert.Equal(1, replaced);
        Assert.Null(node["FilePath"]);
        Assert.Equal("resources/replace.wav", node["filepath"]!.GetValue<string>());
    }

    [Fact]
    public void ReplaceFilePaths_ResolvesSlashAndCaseVariants()
    {
        var node = JsonNode.Parse("""{"FilePath":"Resources\\a.txt"}""")!;

        var replaced = YmmpxProjectJson.ReplaceFilePaths(
            node,
            new Dictionary<string, string>
            {
                ["resources/a.txt"] = "C:/work/extract/resources/a.txt"
            });

        Assert.Equal(1, replaced);
        Assert.Equal("C:/work/extract/resources/a.txt", node["FilePath"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreatePackageAsync_RejectsProjectAsOutput()
    {
        using var workspace = new TemporaryDirectory();
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        File.WriteAllText(projectPath, "{}");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            YmmpxPackageService.CreatePackageAsync(
                projectPath,
                projectPath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("{}", File.ReadAllText(projectPath));
    }

    [Fact]
    public async Task CreatePackageAsync_RejectsResourceAsOutputAndPreservesIt()
    {
        using var workspace = new TemporaryDirectory();
        var resourcePath = Path.Combine(workspace.Path, "sample.ymmpx");
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        File.WriteAllText(resourcePath, "important resource");
        File.WriteAllText(projectPath, JsonSerializer.Serialize(new { FilePath = resourcePath }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            YmmpxPackageService.CreatePackageAsync(
                projectPath,
                resourcePath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("important resource", File.ReadAllText(resourcePath));
    }

    [Fact]
    public async Task CreatePackageAsync_PreservesExistingOutputWhenCancelled()
    {
        using var workspace = new TemporaryDirectory();
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var outputPath = Path.Combine(workspace.Path, "sample.ymmpx");
        File.WriteAllText(projectPath, "{}");
        File.WriteAllText(outputPath, "existing package");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            YmmpxPackageService.CreatePackageAsync(
                projectPath,
                outputPath,
                cancellationToken: cancellation.Token));

        Assert.Equal("existing package", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task CreatePackageAsync_DeduplicatesEquivalentResourcePaths()
    {
        using var workspace = new TemporaryDirectory();
        var resourcePath = Path.Combine(workspace.Path, "a.txt");
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var outputPath = Path.Combine(workspace.Path, "sample.ymmpx");
        File.WriteAllText(resourcePath, "a");
        File.WriteAllText(
            projectPath,
            JsonSerializer.Serialize(new
            {
                Items = new[]
                {
                    new { FilePath = resourcePath },
                    new { FilePath = "./a.txt" }
                }
            }));

        var result = await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            outputPath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ResourceCount);
        Assert.Single(result.FileMap);
        Assert.Equal("resources/a.txt", result.FileMap["a.txt"]);
        using var archive = ZipFile.OpenRead(outputPath);
        Assert.Single(archive.Entries, x => x.FullName.StartsWith("resources/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreatePackageAsync_RejectsOversizedProjectBeforeReading()
    {
        using var workspace = new TemporaryDirectory();
        var projectPath = Path.Combine(workspace.Path, "huge.ymmp");
        var outputPath = Path.Combine(workspace.Path, "huge.ymmpx");

        using (var stream = new FileStream(projectPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(512L * 1024 * 1024 + 1);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            YmmpxPackageService.CreatePackageAsync(
                projectPath,
                outputPath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task PackageAndExtract_RoundTripsResourcePath()
    {
        using var workspace = new TemporaryDirectory();
        var resourcePath = Path.Combine(workspace.Path, "a.txt");
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var packagePath = Path.Combine(workspace.Path, "sample.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        File.WriteAllText(resourcePath, "a");
        File.WriteAllText(projectPath, JsonSerializer.Serialize(new { FilePath = resourcePath }));

        await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            packagePath,
            cancellationToken: TestContext.Current.CancellationToken);
        var result = YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath);
        var restored = JsonNode.Parse(File.ReadAllText(result.ProjectFilePath))!;
        var restoredResourcePath = restored["FilePath"]!.GetValue<string>();

        Assert.Equal(1, result.ReplacedPathCount);
        Assert.True(File.Exists(restoredResourcePath));
        Assert.StartsWith(Path.GetFullPath(extractPath), restoredResourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageAndExtract_PreservesUnpackOnlyRelativePath()
    {
        using var workspace = new TemporaryDirectory();
        var resourceDirectory = Path.Combine(workspace.Path, "other");
        var resourcePath = Path.Combine(resourceDirectory, "a.txt");
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var packagePath = Path.Combine(workspace.Path, "sample.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var relativeExtractPath = Path.GetRelativePath(Environment.CurrentDirectory, extractPath);

        Directory.CreateDirectory(resourceDirectory);
        File.WriteAllText(resourcePath, "a");
        File.WriteAllText(
            projectPath,
            JsonSerializer.Serialize(new
            {
                Existing = new { FilePath = "other/a.txt" },
                Missing = new { FilePath = "a.txt" }
            }));

        await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            packagePath,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = YmmpxPackageService.ExtractAndRestoreProject(packagePath, relativeExtractPath);
        var restored = JsonNode.Parse(File.ReadAllText(result.ProjectFilePath))!;

        Assert.Equal(relativeExtractPath, result.ExtractDirectory);
        Assert.Equal(Path.Combine(relativeExtractPath, "sample.ymmp"), result.ProjectFilePath);
        Assert.True(File.Exists(restored["Existing"]!["FilePath"]!.GetValue<string>()));
        Assert.StartsWith(Path.GetFullPath(extractPath), restored["Existing"]!["FilePath"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("a.txt", restored["Missing"]!["FilePath"]!.GetValue<string>());
    }

    [Fact]
    public void ExtractAndRestoreProject_DoesNotFollowMarkerOutsideExtractionDirectory()
    {
        using var workspace = new TemporaryDirectory();
        var victimPath = Path.Combine(workspace.Path, "victim.ymmp");
        var packagePath = Path.Combine(workspace.Path, "attack.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        File.WriteAllText(victimPath, "{}");
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "_ymmpx_project_path.txt", victimPath);
        });

        Assert.Throws<InvalidDataException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.True(File.Exists(victimPath));
        Assert.Equal("{}", File.ReadAllText(victimPath));
    }

    [Fact]
    public void ExtractAndRestoreProject_DoesNotFollowMarkerToPreexistingFileInExtractionDirectory()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "attack.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var victimPath = Path.Combine(extractPath, "victim.ymmp");
        Directory.CreateDirectory(extractPath);
        File.WriteAllText(victimPath, "{}");
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "_ymmpx_project_path.txt", "victim.ymmp");
        });

        Assert.Throws<InvalidDataException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.True(File.Exists(victimPath));
        Assert.Equal("{}", File.ReadAllText(victimPath));
    }

    [Fact]
    public void ExtractAndRestoreProject_DoesNotDeletePreexistingFileOnNameCollision()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "attack.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var victimPath = Path.Combine(extractPath, "victim.txt");
        Directory.CreateDirectory(extractPath);
        File.WriteAllText(victimPath, "keep");
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "victim.txt", "replace");
            WriteEntry(archive, "project.ymmp", "{}");
        });

        Assert.Throws<IOException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.True(File.Exists(victimPath));
        Assert.Equal("keep", File.ReadAllText(victimPath));
        Assert.Empty(Directory.GetFiles(extractPath, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void ExtractAndRestoreProject_IgnoresPreexistingLinkDefinitions()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "package.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var victimPath = Path.Combine(extractPath, "victim.txt");
        Directory.CreateDirectory(extractPath);
        File.WriteAllText(victimPath, "victim");
        File.WriteAllText(
            Path.Combine(extractPath, "links.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["original.txt"] = "victim.txt"
            }));
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "project.ymmp", JsonSerializer.Serialize(new { FilePath = "original.txt" }));
        });

        var result = YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath);
        var restored = JsonNode.Parse(File.ReadAllText(result.ProjectFilePath))!;

        Assert.Equal(0, result.ReplacedPathCount);
        Assert.Empty(result.LinkMap);
        Assert.Equal("original.txt", restored["FilePath"]!.GetValue<string>());
        Assert.Equal("victim", File.ReadAllText(victimPath));
    }

    [Fact]
    public void ExtractAndRestoreProject_IgnoresLinksToPreexistingResources()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "package.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var victimPath = Path.Combine(extractPath, "victim.txt");
        Directory.CreateDirectory(extractPath);
        File.WriteAllText(victimPath, "victim");
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(
                archive,
                "links.json",
                JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["original.txt"] = "victim.txt"
                }));
            WriteEntry(archive, "project.ymmp", JsonSerializer.Serialize(new { FilePath = "original.txt" }));
        });

        var result = YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath);
        var restored = JsonNode.Parse(File.ReadAllText(result.ProjectFilePath))!;

        Assert.Equal(0, result.ReplacedPathCount);
        Assert.Empty(result.LinkMap);
        Assert.Equal("original.txt", restored["FilePath"]!.GetValue<string>());
        Assert.Equal("victim", File.ReadAllText(victimPath));
    }

    [Fact]
    public async Task CreatePackageAsync_ProducesExtractablePackageForHighlyCompressibleResource()
    {
        using var workspace = new TemporaryDirectory();
        var resourcePath = Path.Combine(workspace.Path, "zeros.bin");
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var packagePath = Path.Combine(workspace.Path, "package.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        WriteRepeatedFile(resourcePath, 128L * 1024 * 1024);
        File.WriteAllText(projectPath, JsonSerializer.Serialize(new { FilePath = resourcePath }));

        await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            packagePath,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath);
        var restored = JsonNode.Parse(File.ReadAllText(result.ProjectFilePath))!;
        var restoredResourcePath = restored["FilePath"]!.GetValue<string>();

        Assert.True(File.Exists(restoredResourcePath));
        Assert.StartsWith(Path.GetFullPath(extractPath), restoredResourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractAndRestoreProject_RejectsAlternateDataStreamEntry()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "ads.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "project.ymmp", "{}");
            WriteEntry(archive, "resources/file.txt:stream", "hidden");
        });

        Assert.Throws<InvalidDataException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.False(Directory.Exists(extractPath));
    }

    [Fact]
    public void ExtractAndRestoreProject_RejectsExcessiveCompressionRatio()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "bomb.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        CreateArchive(packagePath, archive =>
        {
            WriteRepeatedEntry(archive, "resources/zeros.bin", 128L * 1024 * 1024);
            WriteEntry(archive, "project.ymmp", "{}");
        });

        Assert.Throws<InvalidDataException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.False(Directory.Exists(extractPath));
    }

    [Fact]
    public void ExtractAndRestoreProject_CleansUpPartialExtractionWhenProjectJsonIsInvalid()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "broken.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "first.txt", "first");
            WriteEntry(archive, "project.ymmp", "{");
        });

        Assert.ThrowsAny<JsonException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.False(File.Exists(Path.Combine(extractPath, "first.txt")));
        Assert.False(File.Exists(Path.Combine(extractPath, "project.ymmp")));
        Assert.False(Directory.Exists(extractPath));
    }

    [Fact]
    public void ExtractAndRestoreProject_DoesNotDeletePreexistingEmptyDirectoriesOnFailure()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "broken.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var resourceDirectory = Path.Combine(extractPath, "resources");

        Directory.CreateDirectory(resourceDirectory);
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "resources/first.txt", "first");
            WriteEntry(archive, "project.ymmp", "{");
        });

        Assert.ThrowsAny<JsonException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.True(Directory.Exists(extractPath));
        Assert.True(Directory.Exists(resourceDirectory));
        Assert.False(File.Exists(Path.Combine(resourceDirectory, "first.txt")));
    }

    [Fact]
    public void ExtractAndRestoreProject_DoesNotDeletePreexistingEmptyDirectoriesForDirectoryEntries()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "broken.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var resourceDirectory = Path.Combine(extractPath, "resources");

        Directory.CreateDirectory(resourceDirectory);
        CreateArchive(packagePath, archive =>
        {
            archive.CreateEntry("resources/");
            WriteEntry(archive, "project.ymmp", "{");
        });

        Assert.ThrowsAny<JsonException>(() =>
            YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

        Assert.True(Directory.Exists(resourceDirectory));
    }

    [Fact]
    public void ExtractAndRestoreProject_IgnoresMalformedLinkPaths()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "malformed.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        CreateArchive(packagePath, archive =>
        {
            WriteEntry(archive, "project.ymmp", "{}");
            WriteEntry(archive, "links.json", JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["sample.txt"] = "\u0000"
            }));
        });

        var result = YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath);

        Assert.True(File.Exists(result.ProjectFilePath));
        Assert.Equal(0, result.ReplacedPathCount);
    }

    [Fact]
    public async Task CreatePackageAsync_RejectsPackagesThatWouldExceedEntryCountLimit()
    {
        using var workspace = new TemporaryDirectory();
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var outputPath = Path.Combine(workspace.Path, "sample.ymmpx");
        var resourcePaths = new List<string>();

        for (var i = 0; i < 9_998; i++)
        {
            var resourcePath = Path.Combine(workspace.Path, $"r{i:D4}.txt");
            File.WriteAllText(resourcePath, string.Empty);
            resourcePaths.Add(resourcePath);
        }

        File.WriteAllText(
            projectPath,
            JsonSerializer.Serialize(new
            {
                Items = resourcePaths.Select(path => new { FilePath = path }).ToArray()
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            YmmpxPackageService.CreatePackageAsync(
                projectPath,
                outputPath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void ExtractAndRestoreProject_RejectsExtractionThroughReparsePoint()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var workspace = new TemporaryDirectory();
        var packagePath = Path.Combine(workspace.Path, "attack.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        var outsidePath = Path.Combine(workspace.Path, "outside");
        var junctionPath = Path.Combine(extractPath, "resources");

        Directory.CreateDirectory(outsidePath);
        Directory.CreateDirectory(extractPath);
        CreateDirectoryJunction(junctionPath, outsidePath);
        try
        {
            CreateArchive(packagePath, archive =>
            {
                WriteEntry(archive, "resources/escaped.txt", "escape");
                WriteEntry(archive, "project.ymmp", "{}");
            });

            Assert.Throws<InvalidDataException>(() =>
                YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath));

            Assert.False(File.Exists(Path.Combine(outsidePath, "escaped.txt")));
        }
        finally
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath);
        }
    }

    [Fact]
    public void RemoveUiSettings_RemovesLayoutXmlAndToolStates()
    {
        var node = JsonNode.Parse("""
        {
          "LayoutXml": "<layout />",
          "ToolStates": { "Dock": true },
          "Other": 123
        }
        """)!;

        var removed = YmmpxProjectJson.RemoveUiSettings(node);

        Assert.True(removed);
        Assert.Null(node["LayoutXml"]);
        Assert.Null(node["ToolStates"]);
        Assert.Equal(123, node["Other"]!.GetValue<int>());
    }

    [Fact]
    public void GetAvailableFilePath_ReturnsFirstUnusedName()
    {
        using var workspace = new TemporaryDirectory();
        var desiredPath = Path.Combine(workspace.Path, "sample.txt");
        File.WriteAllText(desiredPath, "a");
        File.WriteAllText(Path.Combine(workspace.Path, "sample_1.txt"), "b");

        var result = YmmpxPackageService.GetAvailableFilePath(desiredPath);

        Assert.Equal(Path.Combine(workspace.Path, "sample_2.txt"), result);
    }

    [Fact]
    public void GetAvailableFilePath_SkipsExistingDirectory()
    {
        using var workspace = new TemporaryDirectory();
        var desiredPath = Path.Combine(workspace.Path, "sample.txt");
        Directory.CreateDirectory(desiredPath);

        var result = YmmpxPackageService.GetAvailableFilePath(desiredPath);

        Assert.Equal(Path.Combine(workspace.Path, "sample_1.txt"), result);
    }

    [Fact]
    public void GetAvailableDirectoryPath_ReturnsFirstUnusedName()
    {
        using var workspace = new TemporaryDirectory();
        var desiredPath = Path.Combine(workspace.Path, "sample");
        Directory.CreateDirectory(desiredPath);
        Directory.CreateDirectory($"{desiredPath}_1");

        var result = YmmpxPackageService.GetAvailableDirectoryPath(desiredPath);

        Assert.Equal($"{desiredPath}_2", result);
    }

    [Fact]
    public void GetAvailableDirectoryPath_SkipsExistingFile()
    {
        using var workspace = new TemporaryDirectory();
        var desiredPath = Path.Combine(workspace.Path, "sample");
        File.WriteAllText(desiredPath, "existing");

        var result = YmmpxPackageService.GetAvailableDirectoryPath(desiredPath);

        Assert.Equal($"{desiredPath}_1", result);
    }

    [Fact]
    public void LoadLinkMap_ParsesLinksJson_WhenPresent()
    {
        using var workspace = new TemporaryDirectory();
        var baseDirectory = workspace.Path;
        var resourcePath = Path.Combine(baseDirectory, "resources", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);
        File.WriteAllText(resourcePath, "a");

        File.WriteAllText(
            Path.Combine(baseDirectory, "links.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["a.txt"] = "resources/a.txt"
            }));

        var map = YmmpxPackageService.LoadLinkMap(baseDirectory);

        Assert.Single(map);
        Assert.Equal(resourcePath, map["a.txt"]);
    }

    [Fact]
    public void LoadLinkMap_FallsBackToManifest_WhenLinksJsonHasNoUsableEntries()
    {
        using var workspace = new TemporaryDirectory();
        var baseDirectory = workspace.Path;
        var resourcePath = Path.Combine(baseDirectory, "resources", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);
        File.WriteAllText(resourcePath, "a");

        File.WriteAllText(Path.Combine(baseDirectory, "links.json"), "{}");
        File.WriteAllText(
            Path.Combine(baseDirectory, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                Files = new[]
                {
                    new
                    {
                        OriginalPath = "a.txt",
                        BundlePath = "resources/a.txt"
                    }
                }
            }));

        var map = YmmpxPackageService.LoadLinkMap(baseDirectory);

        Assert.Single(map);
        Assert.Equal(resourcePath, map["a.txt"]);
    }

    [Fact]
    public void LoadLinkMap_ParsesLegacyLinksTxt_WhenSourceAndBundleContainCommas()
    {
        using var workspace = new TemporaryDirectory();
        var baseDirectory = workspace.Path;
        var resourcePath = Path.Combine(baseDirectory, "resources", "a,b.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);
        File.WriteAllText(resourcePath, "a");

        File.WriteAllText(
            Path.Combine(baseDirectory, "links.txt"),
            "foo,bar.wav,resources/a,b.txt");

        var map = YmmpxPackageService.LoadLinkMap(baseDirectory);

        Assert.Single(map);
        Assert.Equal(resourcePath, map["foo,bar.wav"]);
    }

    [Fact]
    public void LoadLinkMap_IgnoresMalformedLinksJsonValues()
    {
        using var workspace = new TemporaryDirectory();
        var baseDirectory = workspace.Path;

        File.WriteAllText(
            Path.Combine(baseDirectory, "links.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["sample.txt"] = "\u0000"
            }));

        var map = YmmpxPackageService.LoadLinkMap(baseDirectory);

        Assert.Empty(map);
    }

    [Fact]
    public async Task CreatePackageAsync_PackagesOnlyVideoItemPngSequenceAndRestoresRepresentativePath()
    {
        using var workspace = new TemporaryDirectory();
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var packagePath = Path.Combine(workspace.Path, "sample.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        WriteTestFiles(workspace.Path, "無題_0.png", "無題_1.png", "無題_2.png", "thumbnail.png", "logo.png");
        File.WriteAllText(projectPath, CreateProjectJson(("VideoItem", Path.Combine(workspace.Path, "無題_0.png"))));

        var result = await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            packagePath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ResourceCount);
        Assert.Equal(new[]
        {
            "resources/sequence_1/無題_0.png",
            "resources/sequence_1/無題_1.png",
            "resources/sequence_1/無題_2.png"
        }, GetResourceEntryNames(packagePath));

        var unpacked = YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath);
        var restored = JsonNode.Parse(File.ReadAllText(unpacked.ProjectFilePath))!;
        var representativePath = restored["Items"]![0]!["FilePath"]!.GetValue<string>();

        Assert.True(File.Exists(representativePath));
        Assert.Equal(new[] { "無題_0.png", "無題_1.png", "無題_2.png" },
            Directory.EnumerateFiles(Path.GetDirectoryName(representativePath)!)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task CreatePackageAsync_DoesNotTreatImageItemPngAsSequence()
    {
        using var workspace = new TemporaryDirectory();
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var packagePath = Path.Combine(workspace.Path, "sample.ymmpx");
        WriteTestFiles(workspace.Path, "無題_0.png", "無題_1.png", "無題_2.png");
        File.WriteAllText(projectPath, CreateProjectJson(("ImageItem", Path.Combine(workspace.Path, "無題_0.png"))));

        var result = await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            packagePath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ResourceCount);
        Assert.Equal(new[] { "resources/無題_0.png" }, GetResourceEntryNames(packagePath));
    }

    [Fact]
    public async Task CreatePackageAsync_DeduplicatesAndSeparatesVideoItemPngSequences()
    {
        using var workspace = new TemporaryDirectory();
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var packagePath = Path.Combine(workspace.Path, "sample.ymmpx");
        WriteTestFiles(workspace.Path, "image_0000.png", "image_0001.png", "image_0002.png", "A_0.png", "A_1.png", "B_0.png", "B_1.png");
        File.WriteAllText(projectPath, CreateProjectJson(
            ("VideoItem", Path.Combine(workspace.Path, "image_0000.png")),
            ("VideoItem", Path.Combine(workspace.Path, "image_0000.png")),
            ("VideoItem", Path.Combine(workspace.Path, "A_0.png")),
            ("VideoItem", Path.Combine(workspace.Path, "B_0.png"))));

        var result = await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            packagePath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ResourceCount);
        Assert.Equal(7, GetResourceEntryNames(packagePath).Count);
        Assert.Contains("resources/sequence_1/image_0000.png", GetResourceEntryNames(packagePath));
        Assert.Contains("resources/sequence_2/A_0.png", GetResourceEntryNames(packagePath));
        Assert.Contains("resources/sequence_3/B_0.png", GetResourceEntryNames(packagePath));
    }

    [Fact]
    public async Task CreatePackageAsync_KeepsSameNamedSequencesFromDifferentDirectoriesSeparate()
    {
        using var workspace = new TemporaryDirectory();
        var firstDirectory = Path.Combine(workspace.Path, "A");
        var secondDirectory = Path.Combine(workspace.Path, "B");
        var projectPath = Path.Combine(workspace.Path, "sample.ymmp");
        var packagePath = Path.Combine(workspace.Path, "sample.ymmpx");
        var extractPath = Path.Combine(workspace.Path, "extract");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        WriteTestFiles(firstDirectory, "image_0.png", "image_1.png");
        WriteTestFiles(secondDirectory, "image_0.png", "image_1.png");
        File.WriteAllText(projectPath, CreateProjectJson(
            ("VideoItem", Path.Combine(firstDirectory, "image_0.png")),
            ("VideoItem", Path.Combine(secondDirectory, "image_0.png"))));

        await YmmpxPackageService.CreatePackageAsync(
            projectPath,
            packagePath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new[]
        {
            "resources/sequence_1/image_0.png",
            "resources/sequence_1/image_1.png",
            "resources/sequence_2/image_0.png",
            "resources/sequence_2/image_1.png"
        }, GetResourceEntryNames(packagePath));

        var unpacked = YmmpxPackageService.ExtractAndRestoreProject(packagePath, extractPath);
        var restored = JsonNode.Parse(File.ReadAllText(unpacked.ProjectFilePath))!;
        var firstPath = restored["Items"]![0]!["FilePath"]!.GetValue<string>();
        var secondPath = restored["Items"]![1]!["FilePath"]!.GetValue<string>();
        Assert.NotEqual(Path.GetDirectoryName(firstPath), Path.GetDirectoryName(secondPath));
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
    }

    private static string CreateProjectJson(params (string TypeName, string FilePath)[] items)
    {
        return JsonSerializer.Serialize(new
        {
            Items = items.Select(item => new Dictionary<string, string>
            {
                ["$type"] = $"YukkuriMovieMaker.Project.Items.{item.TypeName}, YukkuriMovieMaker",
                ["FilePath"] = item.FilePath
            })
        });
    }

    private static IReadOnlyList<string> GetResourceEntryNames(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("resources/", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteTestFiles(string directory, params string[] fileNames)
    {
        foreach (var fileName in fileNames)
            File.WriteAllBytes(Path.Combine(directory, fileName), [0]);
    }

    private static void CreateArchive(string path, Action<ZipArchive> configure)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        configure(archive);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteRepeatedEntry(ZipArchive archive, string name, long length)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using var stream = entry.Open();
        var buffer = new byte[1024 * 1024];
        for (long written = 0; written < length; written += buffer.Length)
        {
            var count = (int)Math.Min(buffer.Length, length - written);
            stream.Write(buffer, 0, count);
        }
    }

    private static void WriteRepeatedFile(string path, long length)
    {
        using var stream = File.Create(path);
        var buffer = new byte[1024 * 1024];
        for (long written = 0; written < length; written += buffer.Length)
        {
            var count = (int)Math.Min(buffer.Length, length - written);
            stream.Write(buffer, 0, count);
        }
    }

    private static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
            throw new InvalidOperationException("Failed to start mklink.");

        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Failed to create junction: {junctionPath} -> {targetPath}");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "YmmpxLib.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
