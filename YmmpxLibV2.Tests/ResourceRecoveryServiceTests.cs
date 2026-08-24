using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class ResourceRecoveryServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "YmmpxLibV2Recovery", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FindsSingleCandidateForRenamedFileWithoutUsingFileNameAsIdentity()
    {
        var resource = await CreateManifestResourceAsync("old_name.png", [1, 2, 3], ManifestResourceKind.Image);
        var searchRoot = CreateDirectory("assets");
        var candidate = await WriteAsync(searchRoot, "renamed.png", [1, 2, 3]);

        var result = await ResourceRecoveryService.FindCandidatesAsync(resource, [searchRoot], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.SingleCandidate, result.Outcome);
        Assert.Equal(ResourceMatchKind.SingleMatch, result.MatchKind);
        Assert.Equal(Path.GetFullPath(candidate), Assert.Single(result.Candidates).Path);
        Assert.Equal(resource.Sha256, result.Candidates[0].Sha256);
    }

    [Theory]
    [InlineData("same-name.bin", new byte[] { 4, 5, 6 })]
    [InlineData("different-name.bin", new byte[] { 3, 2, 1 })]
    public async Task DoesNotCreateCandidateForSameNameOrLengthWithDifferentContent(string candidateName, byte[] candidateContent)
    {
        var resource = await CreateManifestResourceAsync("same-name.bin", [1, 2, 3], ManifestResourceKind.File);
        var searchRoot = CreateDirectory("assets");
        await WriteAsync(searchRoot, candidateName, candidateContent);

        var result = await ResourceRecoveryService.FindCandidatesAsync(resource, [searchRoot], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.NotFound, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task ReturnsEveryCandidateAcrossMultipleRootsWithoutSelectingOne()
    {
        var resource = await CreateManifestResourceAsync("source.wav", [1, 2, 3], ManifestResourceKind.Audio);
        var first = CreateDirectory("first");
        var second = CreateDirectory("second");
        await WriteAsync(first, "one.wav", [1, 2, 3]);
        await WriteAsync(second, "two.wav", [1, 2, 3]);

        var result = await ResourceRecoveryService.FindCandidatesAsync(resource, [first, second], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.MultipleCandidates, result.Outcome);
        Assert.Equal(ResourceMatchKind.MultipleMatches, result.MatchKind);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task UsesOriginalPathOnlyAfterHashVerification()
    {
        var original = await WriteAsync(CreateDirectory("original"), "source.psd", [1, 2, 3]);
        var identity = await ResourceIdentity.CreateAsync(original, TestContext.Current.CancellationToken);
        var matching = new PackageManifestResource(original, identity.FileName, identity.Length, identity.Sha256, "resources/source.psd", ManifestResourceKind.Psd);

        var match = await ResourceRecoveryService.FindCandidatesAsync(matching, [], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(original, [4, 5, 6], TestContext.Current.CancellationToken);
        var mismatch = await ResourceRecoveryService.FindCandidatesAsync(matching, [], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.SingleCandidate, match.Outcome);
        Assert.Equal(ResourceRecoveryOutcome.NotFound, mismatch.Outcome);
    }

    [Fact]
    public async Task ReturnsPartialFailureWithCandidatesAndSearchIssues()
    {
        var resource = await CreateManifestResourceAsync("source.bin", [1, 2, 3], ManifestResourceKind.File);
        var searchable = CreateDirectory("searchable");
        await WriteAsync(searchable, "copy.bin", [1, 2, 3]);
        var missing = Path.Combine(root, "missing");

        var result = await ResourceRecoveryService.FindCandidatesAsync(resource, [missing, searchable], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.PartialFailure, result.Outcome);
        Assert.Single(result.Candidates);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public async Task ReturnsFailedWhenNoSearchRootIsAvailable()
    {
        var resource = await CreateManifestResourceAsync("source.bin", [1, 2, 3], ManifestResourceKind.File);

        var result = await ResourceRecoveryService.FindCandidatesAsync(resource, [Path.Combine(root, "missing")], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.Failed, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task PreservesImageSequenceGroupForFrameCandidate()
    {
        var resource = await CreateManifestResourceAsync("frame_10.png", [1, 2, 3], ManifestResourceKind.ImageSequence, "sequence_1");
        var nested = CreateDirectory(Path.Combine("素材", "連番"));
        await WriteAsync(nested, "renamed-frame.png", [1, 2, 3]);

        var result = await ResourceRecoveryService.FindCandidatesAsync(resource, [Path.Combine(root, "素材")], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.SingleCandidate, result.Outcome);
        Assert.Equal("sequence_1", Assert.Single(result.Candidates).GroupId);
    }

    [Fact]
    public async Task ExcludesPluginResourcesFromLocalMaterialRecovery()
    {
        var resource = await CreateManifestResourceAsync("plugin.dll", [1, 2, 3], ManifestResourceKind.Plugin);

        var result = await ResourceRecoveryService.FindCandidatesAsync(resource, [CreateDirectory("assets")], TestContext.Current.CancellationToken);

        Assert.Equal(ResourceRecoveryOutcome.UnsupportedResourceKind, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.SearchedRoots);
    }

    [Fact]
    public async Task HonorsCancellationAndDoesNotModifyCandidateFiles()
    {
        var resource = await CreateManifestResourceAsync("source.bin", [1, 2, 3], ManifestResourceKind.File);
        var directory = CreateDirectory("assets");
        var candidate = await WriteAsync(directory, "candidate.bin", [1, 2, 3]);
        var before = File.GetLastWriteTimeUtc(candidate);
        var searchResult = await ResourceRecoveryService.FindCandidatesAsync(resource, [directory], TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(ResourceRecoveryOutcome.SingleCandidate, searchResult.Outcome);
        await Assert.ThrowsAsync<OperationCanceledException>(() => ResourceRecoveryService.FindCandidatesAsync(resource, [directory], cancellation.Token));
        Assert.Equal(before, File.GetLastWriteTimeUtc(candidate));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(candidate, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private async Task<PackageManifestResource> CreateManifestResourceAsync(string fileName, byte[] content, ManifestResourceKind kind, string? groupId = null)
    {
        var source = await WriteAsync(CreateDirectory("source"), fileName, content);
        var identity = await ResourceIdentity.CreateAsync(source, TestContext.Current.CancellationToken);
        return new PackageManifestResource(null, identity.FileName, identity.Length, identity.Sha256, $"resources/{fileName}", kind, groupId);
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteAsync(string directory, string fileName, byte[] content)
    {
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        return path;
    }
}
