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
