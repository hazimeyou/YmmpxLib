namespace YmmpxLibV2;

/// <summary>
/// Finds local files matching a <see cref="ResourceIdentity"/> within caller-supplied roots.
/// </summary>
public static class LocalResourceSearch
{
    /// <summary>
    /// Searches the original path and the supplied roots for SHA-256 matches.
    /// Reparse points are not traversed.
    /// </summary>
    public static async Task<ResourceSearchResult> FindMatchesAsync(
        ResourceIdentity resource,
        IEnumerable<string> searchRoots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(searchRoots);

        cancellationToken.ThrowIfCancellationRequested();

        var matches = new List<ResourceSearchMatch>();
        var issues = new List<ResourceSearchIssue>();
        var searchedRoots = new List<string>();
        var examinedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readableLocations = 0;

        if (resource.OriginalPath is not null && File.Exists(resource.OriginalPath))
        {
            readableLocations++;
            await TryAddMatchAsync(resource.OriginalPath, resource, matches, issues, examinedFiles, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var root in searchRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root))
            {
                issues.Add(new ResourceSearchIssue(root ?? string.Empty, "Search root is empty.", null));
                continue;
            }

            var fullRoot = Path.GetFullPath(root);
            searchedRoots.Add(fullRoot);

            if (!Directory.Exists(fullRoot))
            {
                issues.Add(new ResourceSearchIssue(fullRoot, "Search root does not exist.", nameof(DirectoryNotFoundException)));
                continue;
            }

            readableLocations++;
            await SearchRootAsync(fullRoot, resource, matches, issues, examinedFiles, cancellationToken).ConfigureAwait(false);
        }

        matches.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        var matchKind = matches.Count switch
        {
            0 => ResourceMatchKind.NotFound,
            1 => ResourceMatchKind.SingleMatch,
            _ => ResourceMatchKind.MultipleMatches
        };

        var outcome = issues.Count == 0
            ? matchKind.ToOutcome()
            : readableLocations == 0
                ? ResourceSearchOutcome.Failed
                : ResourceSearchOutcome.PartialFailure;

        return new ResourceSearchResult(outcome, matchKind, matches, issues, searchedRoots);
    }

    private static async Task SearchRootAsync(
        string root,
        ResourceIdentity resource,
        ICollection<ResourceSearchMatch> matches,
        ICollection<ResourceSearchIssue> issues,
        ISet<string> examinedFiles,
        CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        issues.Add(ResourceSearchIssue.FromException(entry, exception));
                        continue;
                    }
                    catch (IOException exception)
                    {
                        issues.Add(ResourceSearchIssue.FromException(entry, exception));
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Push(entry);
                        continue;
                    }

                    await TryAddMatchAsync(entry, resource, matches, issues, examinedFiles, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                issues.Add(ResourceSearchIssue.FromException(directory, exception));
            }
            catch (DirectoryNotFoundException exception)
            {
                issues.Add(ResourceSearchIssue.FromException(directory, exception));
            }
            catch (IOException exception)
            {
                issues.Add(ResourceSearchIssue.FromException(directory, exception));
            }
        }
    }

    private static async Task TryAddMatchAsync(
        string path,
        ResourceIdentity resource,
        ICollection<ResourceSearchMatch> matches,
        ICollection<ResourceSearchIssue> issues,
        ISet<string> examinedFiles,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!examinedFiles.Add(fullPath))
            return;

        try
        {
            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists || fileInfo.Length != resource.Length)
                return;

            var sha256 = await ResourceIdentity.ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
            if (StringComparer.Ordinal.Equals(sha256, resource.Sha256))
                matches.Add(new ResourceSearchMatch(fullPath, fileInfo.Name, fileInfo.Length, sha256));
        }
        catch (UnauthorizedAccessException exception)
        {
            issues.Add(ResourceSearchIssue.FromException(fullPath, exception));
        }
        catch (IOException exception)
        {
            issues.Add(ResourceSearchIssue.FromException(fullPath, exception));
        }
    }
}

/// <summary>
/// Describes how many SHA-256 matches were found.
/// </summary>
public enum ResourceMatchKind
{
    /// <summary>No matching files were found.</summary>
    NotFound,
    /// <summary>Exactly one matching file was found.</summary>
    SingleMatch,
    /// <summary>More than one matching file was found.</summary>
    MultipleMatches
}

/// <summary>
/// Describes the health of a local resource search.
/// </summary>
public enum ResourceSearchOutcome
{
    /// <summary>The search completed and no matching files were found.</summary>
    NotFound,
    /// <summary>The search completed and exactly one matching file was found.</summary>
    SingleMatch,
    /// <summary>The search completed and more than one matching file was found.</summary>
    MultipleMatches,
    /// <summary>The search continued after one or more locations could not be read.</summary>
    PartialFailure,
    /// <summary>No supplied location could be searched.</summary>
    Failed
}

/// <summary>
/// A matching resource candidate. It is only a candidate; callers choose whether to use it.
/// </summary>
public sealed record ResourceSearchMatch(string Path, string FileName, long Length, string Sha256);

/// <summary>
/// A non-fatal problem encountered while searching a location.
/// </summary>
public sealed record ResourceSearchIssue(string Path, string Message, string? ExceptionType)
{
    internal static ResourceSearchIssue FromException(string path, Exception exception) =>
        new(path, exception.Message, exception.GetType().Name);
}

/// <summary>
/// The read-only result of a local resource search.
/// </summary>
public sealed record ResourceSearchResult(
    ResourceSearchOutcome Outcome,
    ResourceMatchKind MatchKind,
    IReadOnlyList<ResourceSearchMatch> Matches,
    IReadOnlyList<ResourceSearchIssue> Issues,
    IReadOnlyList<string> SearchedRoots);

internal static class ResourceMatchKindExtensions
{
    public static ResourceSearchOutcome ToOutcome(this ResourceMatchKind matchKind) => matchKind switch
    {
        ResourceMatchKind.NotFound => ResourceSearchOutcome.NotFound,
        ResourceMatchKind.SingleMatch => ResourceSearchOutcome.SingleMatch,
        ResourceMatchKind.MultipleMatches => ResourceSearchOutcome.MultipleMatches,
        _ => throw new ArgumentOutOfRangeException(nameof(matchKind), matchKind, null)
    };
}
