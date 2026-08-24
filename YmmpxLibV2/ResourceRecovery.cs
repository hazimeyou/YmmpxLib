namespace YmmpxLibV2;

/// <summary>
/// Finds read-only local recovery candidates for one manifest resource.
/// It never selects or applies a candidate.
/// </summary>
public static class ResourceRecoveryService
{
    /// <summary>
    /// Searches caller-supplied roots for SHA-256 matches for <paramref name="resource"/>.
    /// </summary>
    public static async Task<ResourceRecoveryResult> FindCandidatesAsync(
        PackageManifestResource resource,
        IEnumerable<string> searchRoots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(searchRoots);

        if (resource.Kind == ManifestResourceKind.Plugin)
        {
            return new ResourceRecoveryResult(
                resource,
                ResourceRecoveryOutcome.UnsupportedResourceKind,
                ResourceMatchKind.NotFound,
                [],
                [],
                []);
        }

        var searchResult = await LocalResourceSearch.FindMatchesAsync(
            resource.ToResourceIdentity(),
            searchRoots,
            cancellationToken).ConfigureAwait(false);

        var candidates = searchResult.Matches
            .Select(match => new ResourceRecoveryCandidate(resource, match))
            .ToArray();

        return new ResourceRecoveryResult(
            resource,
            searchResult.Outcome.ToRecoveryOutcome(),
            searchResult.MatchKind,
            candidates,
            searchResult.Issues,
            searchResult.SearchedRoots);
    }
}

/// <summary>Describes the search health for a resource recovery request.</summary>
public enum ResourceRecoveryOutcome
{
    /// <summary>No SHA-256 matching candidate was found.</summary>
    NotFound,
    /// <summary>Exactly one SHA-256 matching candidate was found.</summary>
    SingleCandidate,
    /// <summary>More than one SHA-256 matching candidate was found.</summary>
    MultipleCandidates,
    /// <summary>The search continued after one or more locations could not be read.</summary>
    PartialFailure,
    /// <summary>No supplied location could be searched.</summary>
    Failed,
    /// <summary>The resource requires separate plugin dependency recovery.</summary>
    UnsupportedResourceKind
}

/// <summary>
/// A SHA-256 matching local file. The caller decides whether it should be selected later.
/// </summary>
public sealed record ResourceRecoveryCandidate(PackageManifestResource Resource, ResourceSearchMatch Match)
{
    /// <summary>Gets the candidate file path.</summary>
    public string Path => Match.Path;

    /// <summary>Gets the candidate length.</summary>
    public long Length => Match.Length;

    /// <summary>Gets the verified candidate SHA-256.</summary>
    public string Sha256 => Match.Sha256;

    /// <summary>Gets the logical image-sequence group, when the resource is a frame.</summary>
    public string? GroupId => Resource.GroupId;
}

/// <summary>Contains local recovery candidates and non-fatal search issues.</summary>
public sealed record ResourceRecoveryResult(
    PackageManifestResource Resource,
    ResourceRecoveryOutcome Outcome,
    ResourceMatchKind MatchKind,
    IReadOnlyList<ResourceRecoveryCandidate> Candidates,
    IReadOnlyList<ResourceSearchIssue> Issues,
    IReadOnlyList<string> SearchedRoots);

internal static class ResourceSearchOutcomeExtensions
{
    public static ResourceRecoveryOutcome ToRecoveryOutcome(this ResourceSearchOutcome outcome) => outcome switch
    {
        ResourceSearchOutcome.NotFound => ResourceRecoveryOutcome.NotFound,
        ResourceSearchOutcome.SingleMatch => ResourceRecoveryOutcome.SingleCandidate,
        ResourceSearchOutcome.MultipleMatches => ResourceRecoveryOutcome.MultipleCandidates,
        ResourceSearchOutcome.PartialFailure => ResourceRecoveryOutcome.PartialFailure,
        ResourceSearchOutcome.Failed => ResourceRecoveryOutcome.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
