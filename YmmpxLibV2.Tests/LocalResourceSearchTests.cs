using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class LocalResourceSearchTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "YmmpxLibV2.Tests", Guid.NewGuid().ToString("N"));

    public LocalResourceSearchTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task FindsASingleMatchingFile()
    {
        var identity = await CreateIdentityAsync("source.bin", new byte[] { 1, 2, 3 });
        var searchRoot = CreateDirectory("assets");
        await WriteFileAsync(searchRoot, "copy.bin", new byte[] { 1, 2, 3 });

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { searchRoot }, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceSearchOutcome.SingleMatch, result.Outcome);
        Assert.Equal(ResourceMatchKind.SingleMatch, result.MatchKind);
        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task ReturnsNotFoundWhenNoHashMatches()
    {
        var identity = await CreateIdentityAsync("source.bin", new byte[] { 1, 2, 3 });
        var searchRoot = CreateDirectory("assets");
        await WriteFileAsync(searchRoot, "other.bin", new byte[] { 9, 8, 7 });

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { searchRoot }, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceSearchOutcome.NotFound, result.Outcome);
        Assert.Equal(ResourceMatchKind.NotFound, result.MatchKind);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task ReturnsEveryMatchWithoutSelectingOne()
    {
        var identity = await CreateIdentityAsync("source.bin", new byte[] { 1, 2, 3 });
        var firstRoot = CreateDirectory("first");
        var secondRoot = CreateDirectory("second");
        await WriteFileAsync(firstRoot, "one.bin", new byte[] { 1, 2, 3 });
        await WriteFileAsync(secondRoot, "two.bin", new byte[] { 1, 2, 3 });

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { firstRoot, secondRoot }, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceSearchOutcome.MultipleMatches, result.Outcome);
        Assert.Equal(ResourceMatchKind.MultipleMatches, result.MatchKind);
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public async Task DoesNotMatchSameNameWithDifferentContent()
    {
        var identity = await CreateIdentityAsync("same-name.psd", new byte[] { 1, 2, 3 });
        var searchRoot = CreateDirectory("assets");
        await WriteFileAsync(searchRoot, "same-name.psd", new byte[] { 4, 5, 6 });

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { searchRoot }, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceMatchKind.NotFound, result.MatchKind);
    }

    [Fact]
    public async Task DoesNotMatchSameLengthWithDifferentContent()
    {
        var identity = await CreateIdentityAsync("source.bin", new byte[] { 1, 2, 3, 4 });
        var searchRoot = CreateDirectory("assets");
        await WriteFileAsync(searchRoot, "different.bin", new byte[] { 4, 3, 2, 1 });

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { searchRoot }, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceMatchKind.NotFound, result.MatchKind);
    }

    [Fact]
    public async Task SearchesSubdirectories()
    {
        var identity = await CreateIdentityAsync("source.bin", new byte[] { 1, 2, 3 });
        var searchRoot = CreateDirectory("assets");
        var nestedDirectory = CreateDirectory(Path.Combine("assets", "nested", "deeper"));
        await WriteFileAsync(nestedDirectory, "copy.bin", new byte[] { 1, 2, 3 });

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { searchRoot }, TestContext.Current.CancellationToken);

        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task SearchesJapanesePaths()
    {
        var identity = await CreateIdentityAsync("元.psd", new byte[] { 1, 2, 3 });
        var searchRoot = CreateDirectory("素材\\琴葉葵");
        await WriteFileAsync(searchRoot, "立ち絵.psd", new byte[] { 1, 2, 3 });

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { searchRoot }, TestContext.Current.CancellationToken);

        Assert.Single(result.Matches);
        Assert.EndsWith("立ち絵.psd", result.Matches[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputesLargeFileHashThroughAStream()
    {
        var bytes = new byte[3 * 1024 * 1024];
        new Random(12345).NextBytes(bytes);
        var identity = await CreateIdentityAsync("large.bin", bytes);
        var searchRoot = CreateDirectory("assets");
        await WriteFileAsync(searchRoot, "large-copy.bin", bytes);

        var result = await LocalResourceSearch.FindMatchesAsync(identity, new[] { searchRoot }, TestContext.Current.CancellationToken);

        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var identity = await CreateIdentityAsync("source.bin", new byte[] { 1, 2, 3 });
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => LocalResourceSearch.FindMatchesAsync(identity, new[] { CreateDirectory("assets") }, cancellation.Token));
    }

    [Fact]
    public async Task UsesMatchingOriginalPathAsACandidate()
    {
        var sourcePath = await WriteFileAsync(CreateDirectory("original"), "source.bin", new byte[] { 1, 2, 3 });
        var identity = await ResourceIdentity.CreateAsync(sourcePath, TestContext.Current.CancellationToken);

        var result = await LocalResourceSearch.FindMatchesAsync(identity, Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Equal(ResourceSearchOutcome.SingleMatch, result.Outcome);
        Assert.Single(result.Matches);
        Assert.Equal(Path.GetFullPath(sourcePath), result.Matches[0].Path);
    }

    private async Task<ResourceIdentity> CreateIdentityAsync(string fileName, byte[] content)
    {
        var sourcePath = await WriteFileAsync(CreateDirectory("expected"), fileName, content);
        var sourceIdentity = await ResourceIdentity.CreateAsync(sourcePath, TestContext.Current.CancellationToken);
        return new ResourceIdentity(Path.Combine(root, "missing", fileName), sourceIdentity.FileName, sourceIdentity.Length, sourceIdentity.Sha256);
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteFileAsync(string directory, string fileName, byte[] content)
    {
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}
