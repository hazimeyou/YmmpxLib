using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class PackageManifestTests : IDisposable
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private readonly string root = Path.Combine(Path.GetTempPath(), "YmmpxLibV2.ManifestTests", Guid.NewGuid().ToString("N"));

    public PackageManifestTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void SerializesAndDeserializesAResource()
    {
        var manifest = new PackageManifest(new[] { CreateResource() });

        var restored = PackageManifestSerializer.Deserialize(PackageManifestSerializer.Serialize(manifest));

        var resource = Assert.Single(restored.Resources);
        Assert.Equal(PackageManifest.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal("resources/aoi.psd", resource.PackagePath);
        Assert.Equal(Hash, resource.Sha256);
    }

    [Fact]
    public void SerializesAndDeserializesProjectMetadata()
    {
        var manifest = new PackageManifest(
            [CreateResource()],
            new PackageManifestProject("project.ymmp", "同人誌ラクスルテンプレ.ymmp"));

        var restored = PackageManifestSerializer.Deserialize(PackageManifestSerializer.Serialize(manifest));

        Assert.NotNull(restored.Project);
        Assert.Equal("project.ymmp", restored.Project!.PackagePath);
        Assert.Equal("同人誌ラクスルテンプレ.ymmp", restored.Project.OriginalFileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape.ymmp")]
    [InlineData("folder/project.ymmp")]
    [InlineData("C:\\project.ymmp")]
    [InlineData("project.txt")]
    public void RejectsUnsafeOrNonYmmpOriginalProjectFileName(string fileName)
    {
        Assert.Throws<ArgumentException>(() => new PackageManifestProject("project.ymmp", fileName));
    }

    [Fact]
    public void KeepsMultipleResourcesInPackagePathOrder()
    {
        var manifest = new PackageManifest(new[]
        {
            CreateResource(packagePath: "resources/z.bin"),
            CreateResource(packagePath: "resources/a.bin")
        });

        var restored = PackageManifestSerializer.Deserialize(PackageManifestSerializer.Serialize(manifest));

        Assert.Collection(
            restored.Resources,
            resource => Assert.Equal("resources/a.bin", resource.PackagePath),
            resource => Assert.Equal("resources/z.bin", resource.PackagePath));
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void RejectsInvalidSha256(string sha256)
    {
        Assert.Throws<ArgumentException>(() => CreateResource(sha256: sha256));
    }

    [Fact]
    public void RejectsNegativeLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateResource(length: -1));
    }

    [Theory]
    [InlineData("../evil.dat")]
    [InlineData("resources/../../evil.dat")]
    [InlineData("resources\\..\\evil.dat")]
    public void RejectsPackagePathTraversal(string packagePath)
    {
        Assert.Throws<ArgumentException>(() => CreateResource(packagePath: packagePath));
    }

    [Theory]
    [InlineData("C:\\temp\\evil.dat")]
    [InlineData("/tmp/evil.dat")]
    [InlineData("\\\\server\\share\\evil.dat")]
    public void RejectsAbsolutePackagePath(string packagePath)
    {
        Assert.Throws<ArgumentException>(() => CreateResource(packagePath: packagePath));
    }

    [Fact]
    public void RejectsDuplicatePackagePath()
    {
        Assert.Throws<ArgumentException>(() => new PackageManifest(new[]
        {
            CreateResource(packagePath: "resources/a.bin"),
            CreateResource(packagePath: "resources/a.bin")
        }));
    }

    [Fact]
    public void RejectsUnknownSchemaVersion()
    {
        const string json = "{\"schemaVersion\":999,\"resources\":[]}";

        Assert.Throws<PackageManifestException>(() => PackageManifestSerializer.Deserialize(json));
    }

    [Fact]
    public void AllowsMissingOriginalPath()
    {
        var manifest = new PackageManifest(new[] { CreateResource(originalPath: null) });

        var restored = PackageManifestSerializer.Deserialize(PackageManifestSerializer.Serialize(manifest));

        Assert.Null(Assert.Single(restored.Resources).OriginalPath);
    }

    [Fact]
    public void RoundTripsJapaneseMetadata()
    {
        var resource = CreateResource(
            originalPath: "C:\\Users\\sample\\素材\\琴葉葵\\立ち絵.psd",
            fileName: "立ち絵.psd",
            packagePath: "resources/琴葉葵/立ち絵.psd",
            kind: ManifestResourceKind.Psd);

        var restored = PackageManifestSerializer.Deserialize(PackageManifestSerializer.Serialize(new PackageManifest(new[] { resource })));

        var restoredResource = Assert.Single(restored.Resources);
        Assert.Equal("立ち絵.psd", restoredResource.FileName);
        Assert.Equal("resources/琴葉葵/立ち絵.psd", restoredResource.PackagePath);
    }

    [Fact]
    public void KeepsImageSequenceFrameAssociation()
    {
        var manifest = new PackageManifest(new[]
        {
            CreateResource(fileName: "frame_001.png", packagePath: "resources/sequence_0/frame_001.png", kind: ManifestResourceKind.ImageSequence, groupId: "sequence_0"),
            CreateResource(fileName: "frame_002.png", packagePath: "resources/sequence_0/frame_002.png", kind: ManifestResourceKind.ImageSequence, groupId: "sequence_0")
        });

        var restored = PackageManifestSerializer.Deserialize(PackageManifestSerializer.Serialize(manifest));

        Assert.All(restored.Resources, resource =>
        {
            Assert.Equal(ManifestResourceKind.ImageSequence, resource.Kind);
            Assert.Equal("sequence_0", resource.GroupId);
        });
    }

    [Fact]
    public async Task ConnectsManifestResourceToLocalResourceSearch()
    {
        var originalDirectory = CreateDirectory("original");
        var sourcePath = await WriteFileAsync(originalDirectory, "source.bin", new byte[] { 1, 2, 3 });
        var identity = await ResourceIdentity.CreateAsync(sourcePath, TestContext.Current.CancellationToken);
        var manifestResource = new PackageManifestResource(null, identity.FileName, identity.Length, identity.Sha256, "resources/source.bin");
        var searchDirectory = CreateDirectory("search");
        await WriteFileAsync(searchDirectory, "renamed.bin", new byte[] { 1, 2, 3 });

        var result = await LocalResourceSearch.FindMatchesAsync(
            manifestResource.ToResourceIdentity(),
            new[] { searchDirectory },
            TestContext.Current.CancellationToken);

        Assert.Equal(ResourceMatchKind.SingleMatch, result.MatchKind);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void SerializesDeterministically()
    {
        var manifest = new PackageManifest(new[]
        {
            CreateResource(packagePath: "resources/z.bin"),
            CreateResource(packagePath: "resources/a.bin")
        });

        var first = PackageManifestSerializer.Serialize(manifest);
        var second = PackageManifestSerializer.Serialize(manifest);

        Assert.Equal(first, second);
    }

    private static PackageManifestResource CreateResource(
        string? originalPath = "C:\\Users\\sample\\aoi.psd",
        string fileName = "aoi.psd",
        long length = 123,
        string sha256 = Hash,
        string packagePath = "resources/aoi.psd",
        ManifestResourceKind kind = ManifestResourceKind.File,
        string? groupId = null) =>
        new(originalPath, fileName, length, sha256, packagePath, kind, groupId);

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteFileAsync(string directory, string fileName, byte[] content)
    {
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        return path;
    }
}
