using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using YukkuriMovieMaker.Project;

[assembly: SupportedOSPlatform("windows10.0.19041")]

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
{
    Console.Error.WriteLine("This SDK probe requires Windows 10 build 19041 or later.");
    return 1;
}

var sdk = typeof(IFileItem).Assembly;
Console.WriteLine($"SDK {sdk.GetName().Name} {sdk.GetName().Version} " +
    FileVersionInfo.GetVersionInfo(sdk.Location).ProductVersion);

using var fixtures = new Fixtures();
var cases = new (string Name, Action Verify)[]
{
    ("Direct IFileItem and notification", () =>
    {
        var owner = new DirectItem(fixtures.File);
        var notifications = 0;
        owner.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DirectItem.Location)) notifications++; };
        Check(Collect(owner).References.Single().Owner == owner, "direct owner found");
        Check(Replace(owner, fixtures.File, fixtures.StagedFile), "replacement observable");
        Check(notifications == 1, "fixture setter issued one notification");
        Check(JsonSerializer.SerializeToUtf8Bytes(owner).AsSpan().SequenceEqual(
            JsonSerializer.SerializeToUtf8Bytes(new DirectItem(fixtures.StagedFile))), "fixture serialized changed path");
    }),
    ("Inherited IFileItem", () =>
    {
        var derived = new DerivedItem(fixtures.File);
        Check(Collect(derived).References.Single().Owner is DerivedItem, "inherited dispatch");
        Check(Replace(derived, fixtures.File, fixtures.StagedFile), "inherited replacement");
    }),
    ("Parent delegates to children", () =>
    {
        var child = new DirectItem(fixtures.File);
        var parent = new AggregateItem(null, child);
        Check(Collect(parent).References.Single().Owner == parent, "aggregating owner used once");
        Check(Replace(parent, fixtures.File, fixtures.StagedFile) && child.Location == fixtures.StagedFile,
            "aggregation delegates replacement too");
    }),
    ("Only the child implements IFileItem", () =>
    {
        var child = new DirectItem(fixtures.File);
        var root = new CollectionNode(child);
        Check(Collect(root).References.Single().Owner == child, "typed child edge discovered");
        Check(Replace(child, fixtures.File, fixtures.StagedFile), "child replacement");
        Check(root.UnrelatedReads == 0, "discovery leaves unrelated getters untouched");
    }),
    ("Multiple children in a collection", () =>
    {
        var first = new DirectItem(fixtures.File);
        var second = new DirectItem(fixtures.OtherFile);
        Check(Collect(new CollectionNode(first, second)).References.Select(r => r.Owner)
            .SequenceEqual(new IFileItem[] { first, second }), "both members discovered");
    }),
    ("One object reachable from several edges", () =>
    {
        var child = new DirectItem(fixtures.File);
        var root = new CollectionNode(child, new CollectionNode(child));
        Check(Collect(root).References.Count == 1, "shared object yielded once");
        Check(child.GetFilesCalls == 1, "shared object queried once");
    }),
    ("Parent and child report the same path", () =>
    {
        var child = new DirectItem(fixtures.File);
        var parent = new AggregateItem(fixtures.File, child);
        var discovery = Collect(parent);
        Check(discovery.References.Count == 1 && discovery.References.Single().Owner == parent,
            "one known aggregate owner/path despite two reported values");
        Check(child.GetFilesCalls == 1, "graph traversal didn't repeat the delegated call");
        Check(Replace(parent, fixtures.File, fixtures.StagedFile) && parent.Location == fixtures.StagedFile &&
            child.Location == fixtures.StagedFile, "both underlying fields changed");
    }),
    ("Cycle and traversal budget", () =>
    {
        var root = new CollectionNode();
        var child = new CollectionNode(root, new DirectItem(fixtures.File));
        root.Children.Add(child);
        Check(Collect(root).References.Count == 1, "cyclic graph terminates");
        Check(Collect(root, maxNodes: 1).Diagnostics.Contains("node-limit"), "budget makes incompleteness visible");
    }),
    ("Invalid, empty, null and missing values", () =>
    {
        var discovery = Collect(new FaultyItem(() => [null!, "", " ", "\0", "missing.bin",
            Path.GetFileName(fixtures.File), fixtures.File]));
        Check(discovery.References.Count == 3 && discovery.References.Any(r => r.Kind == ReferenceKind.Missing),
            "missing and distinct original spellings retained for diagnosis");
        var aliases = discovery.References.Where(r => r.Kind == ReferenceKind.File).ToArray();
        Check(aliases.Length == 2 && aliases[0].Resolved == aliases[1].Resolved && aliases[0].Raw != aliases[1].Raw,
            "disk identity and raw replacement argument are separate");
        Check(discovery.Diagnostics.Count(d => d == "invalid-path") == 4, "each invalid value recorded");
        Check(Collect(new FaultyItem(() => null!)).Diagnostics.Contains("null-sequence"), "null sequence recorded");
    }),
    ("GetFiles throws immediately or during enumeration", () =>
    {
        var immediate = new FaultyItem(() => throw new InvalidOperationException("fixture failure"));
        var deferred = new FaultyItem(() => EnumerateThenThrow(fixtures.File));
        var healthy = new DirectItem(fixtures.OtherFile);
        var discovery = Collect(new CollectionNode(immediate, deferred, healthy));
        Check(discovery.Diagnostics.Count(d => d == "enumeration-failed") == 2, "both failure paths diagnosed");
        Check(discovery.References.Count == 1 && discovery.References.Single().Owner == healthy,
            "unreliable owner snapshots discarded, independent owner retained");
        var excess = new FaultyItem(() => Enumerable.Repeat(fixtures.File, 65));
        Check(Collect(excess).Diagnostics.Contains("value-limit"), "sequence budget makes incompleteness visible");
    }),
    ("ReplaceFile returns without changing anything", () =>
    {
        var owner = new FaultyItem(() => [fixtures.File]);
        Check(Collect(owner).References.Count == 1, "material exists");
        Check(!Replace(owner, fixtures.File, fixtures.StagedFile), "successful return alone was insufficient");
    }),
    ("ReplaceFile fails after a partial mutation", () =>
    {
        var original = new DirectItem(fixtures.File);
        var originalProjectBytes = JsonSerializer.SerializeToUtf8Bytes(original);
        var originalProjectPath = Path.Combine(fixtures.Root, "synthetic-original.json");
        File.WriteAllBytes(originalProjectPath, originalProjectBytes);
        var originalAssetBytes = File.ReadAllBytes(fixtures.File);
        var copy = new DirectItem(original.Location);
        var faultyCopy = new FaultyItem(copy.GetFiles, (from, to) =>
        {
            copy.ReplaceFile(from, to);
            throw new InvalidOperationException("fixture failure after mutation");
        });
        Check(!Replace(faultyCopy, fixtures.File, fixtures.StagedFile), "failed mutation prevented publication");
        Check(copy.Location == fixtures.StagedFile, "partial mutation is real");
        Check(JsonSerializer.SerializeToUtf8Bytes(original).AsSpan().SequenceEqual(originalProjectBytes),
            "original fixture serialized bytes preserved");
        Check(File.ReadAllBytes(originalProjectPath).AsSpan().SequenceEqual(originalProjectBytes),
            "original project file preserved");
        Check(File.ReadAllBytes(fixtures.File).AsSpan().SequenceEqual(originalAssetBytes), "original asset preserved");
    }),
    ("Several parameter objects reference one file", () =>
    {
        var first = new DirectItem(fixtures.File);
        var second = new DirectItem(fixtures.File);
        var references = Collect(new CollectionNode(first, second)).References;
        Check(references.Count == 2 && references.Select(r => r.Resolved).Distinct().Count() == 1,
            "one asset and two owners");
        foreach (var reference in references)
            Check(Replace(reference.Owner, reference.Raw, fixtures.StagedFile), "each owner independently relinked");
        Check(first.Location == fixtures.StagedFile && second.Location == fixtures.StagedFile, "both owners retained");
    }),
    ("Explicit directory dependencies and relative layout", () =>
    {
        var original = new DirectItem(fixtures.SourceDirectory);
        var copy = new DirectItem(original.Location);
        var reference = Collect(copy).References.Single();
        Check(reference.Kind == ReferenceKind.Directory, "directory distinguished by filesystem");
        var staged = fixtures.StageSelectedDirectoryFiles();
        Check(Replace(copy, reference.Raw, staged), "directory owner received a directory target");
        Check(original.Location == fixtures.SourceDirectory, "original owner still points at source");
        Check(Directory.GetFiles(staged, "*", SearchOption.AllDirectories).Length == 2,
            "only explicitly selected files staged");
        Check(fixtures.SelectedFiles.All(path =>
            File.ReadAllBytes(Path.Combine(staged, path)).AsSpan().SequenceEqual(
                File.ReadAllBytes(Path.Combine(fixtures.SourceDirectory, path)))), "relative layout and bytes matched");
        Check(File.Exists(Path.Combine(fixtures.SourceDirectory, "unselected.bin")), "source tree preserved");
    }),
    ("Custom JSON property name", () =>
    {
        var owner = new DirectItem(fixtures.File);
        Check(Collect(owner).References.Count == 1, "path discovered through actual SDK interface");
        Check(Replace(owner, fixtures.File, fixtures.StagedFile), "custom property changed");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(owner));
        Check(!json.RootElement.TryGetProperty("FilePath", out _) &&
            json.RootElement.GetProperty("CustomAsset").GetString() == fixtures.StagedFile, "custom property persisted");
    }),
    ("Runtime change is absent from saved JSON", () =>
    {
        var owner = new NonPersistentItem(fixtures.File);
        Check(Replace(owner, fixtures.File, fixtures.StagedFile), "memory-level verification succeeds");
        var outputPath = Path.Combine(fixtures.Root, "synthetic-project.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(owner));
        using var saved = JsonDocument.Parse(File.ReadAllText(outputPath));
        Check(((IFileItem)owner).GetFiles().Single() == fixtures.StagedFile &&
            saved.RootElement.GetProperty("FilePath").GetString() == fixtures.File,
            "fixture's persisted state exposes a false success");
    }),
};

var failures = 0;
for (var i = 0; i < cases.Length; i++)
{
    var (name, verify) = cases[i];
    try
    {
        verify();
        Console.WriteLine($"PASS {i + 1:D2} {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {i + 1:D2} {name}: {error.Message}");
    }
}

Console.WriteLine($"{cases.Length - failures}/{cases.Length} synthetic cases passed");
return failures == 0 ? 0 : 1;

Discovery Collect(FixtureNode root, int maxNodes = 64)
{
    var discovery = new Discovery();
    var visited = new HashSet<FixtureNode>(ReferenceEqualityComparer.Instance);
    var pending = new Queue<FixtureNode>();
    pending.Enqueue(root);
    while (pending.TryDequeue(out var node))
    {
        if (!visited.Add(node))
            continue;
        if (visited.Count > maxNodes)
        {
            discovery.Diagnostics.Add("node-limit");
            break;
        }
        if (node is IFileItem owner)
        {
            var references = ReadOwner(owner, discovery.Diagnostics);
            if (references is not null)
                discovery.References.AddRange(references);
        }
        // In these fixtures only, an aggregate explicitly owns collection and replacement of its children.
        if (node is CollectionNode collection)
            foreach (var child in collection.Children)
                pending.Enqueue(child);
    }
    return discovery;
}

List<Reference>? ReadOwner(IFileItem owner, List<string> diagnostics)
{
    var result = new List<Reference>();
    var rawPaths = new HashSet<string>(StringComparer.Ordinal);
    try
    {
        var paths = owner.GetFiles();
        if (paths is null)
        {
            diagnostics.Add("null-sequence");
            return null;
        }
        var count = 0;
        foreach (var raw in paths)
        {
            if (++count > 64)
            {
                diagnostics.Add("value-limit");
                return null;
            }
            if (string.IsNullOrWhiteSpace(raw))
            {
                diagnostics.Add("invalid-path");
                continue;
            }
            if (!rawPaths.Add(raw))
                continue;
            string resolved;
            try
            {
                resolved = Path.GetFullPath(raw, fixtures.Root);
            }
            catch (ArgumentException)
            {
                diagnostics.Add("invalid-path");
                continue;
            }
            var kind = File.Exists(resolved) ? ReferenceKind.File :
                Directory.Exists(resolved) ? ReferenceKind.Directory : ReferenceKind.Missing;
            result.Add(new Reference(owner, raw, resolved, kind));
        }
        return result;
    }
    catch (InvalidOperationException)
    {
        diagnostics.Add("enumeration-failed");
        return null;
    }
}

bool Replace(IFileItem isolatedOwner, string from, string to)
{
    var before = ReadOwner(isolatedOwner, []);
    if (before is null || !before.Any(r => r.Raw == from))
        return false;

    try
    {
        isolatedOwner.ReplaceFile(from, to);
    }
    catch (InvalidOperationException)
    {
        return false;
    }
    var after = ReadOwner(isolatedOwner, []);
    return after is not null && after.All(r => r.Raw != from) && after.Any(r => r.Raw == to);
}

static IEnumerable<string> EnumerateThenThrow(string path)
{
    yield return path;
    throw new InvalidOperationException("fixture enumeration failure");
}

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

record Reference(IFileItem Owner, string Raw, string Resolved, ReferenceKind Kind);
enum ReferenceKind { File, Directory, Missing }

sealed class Discovery
{
    public List<Reference> References { get; } = [];
    public List<string> Diagnostics { get; } = [];
}

abstract class FixtureNode;

sealed class CollectionNode(params FixtureNode[] children) : FixtureNode
{
    public List<FixtureNode> Children { get; } = [.. children];
    public int UnrelatedReads { get; private set; }
    public string Unrelated
    {
        get
        {
            UnrelatedReads++;
            throw new InvalidOperationException("Unrelated getters are outside the traversal.");
        }
    }
}

class DirectItem(string? location) : FixtureNode, IFileItem, INotifyPropertyChanged
{
    [JsonPropertyName("CustomAsset")]
    public string? Location { get; private set; } = location;
    [JsonIgnore]
    public int GetFilesCalls { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual IEnumerable<string> GetFiles()
    {
        GetFilesCalls++;
        if (Location is not null)
            yield return Location;
    }

    public virtual void ReplaceFile(string from, string to)
    {
        if (Location == from)
        {
            Location = to;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }
    }

    public IEnumerable<TimelineResource> GetResources() => [];
}

sealed class DerivedItem(string location) : DirectItem(location);

sealed class AggregateItem(string? location, DirectItem child) : DirectItem(location)
{
    public override IEnumerable<string> GetFiles() => base.GetFiles().Concat(child.GetFiles());

    public override void ReplaceFile(string from, string to)
    {
        base.ReplaceFile(from, to);
        child.ReplaceFile(from, to);
    }
}

sealed class FaultyItem(Func<IEnumerable<string>> read, Action<string, string>? replace = null) : FixtureNode, IFileItem
{
    public IEnumerable<string> GetFiles() => read();
    public void ReplaceFile(string from, string to) => replace?.Invoke(from, to);
    public IEnumerable<TimelineResource> GetResources() => [];
}

sealed class NonPersistentItem(string original) : FixtureNode, IFileItem
{
    private string runtimePath = original;
    public string FilePath { get; } = original;

    IEnumerable<string> IFileItem.GetFiles() => [runtimePath];
    void IFileItem.ReplaceFile(string from, string to)
    {
        if (runtimePath == from)
            runtimePath = to;
    }
    public IEnumerable<TimelineResource> GetResources() => [];
}

sealed class Fixtures : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("ymmpx-sdk-probe-").FullName;
    public string File => Path.Combine(Root, "source.bin");
    public string OtherFile => Path.Combine(Root, "other.bin");
    public string StagedFile => Path.Combine(Root, "staged.bin");
    public string SourceDirectory => Path.Combine(Root, "source-directory");
    public string[] SelectedFiles { get; } = ["meta.bin", Path.Combine("parts", "eye.bin")];

    public Fixtures()
    {
        System.IO.File.WriteAllText(File, "fixture");
        System.IO.File.WriteAllText(OtherFile, "other fixture");
        System.IO.File.Copy(File, StagedFile);
        foreach (var relative in SelectedFiles.Append("unselected.bin"))
        {
            var path = Path.Combine(SourceDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, relative);
        }
    }

    public string StageSelectedDirectoryFiles()
    {
        var destination = Path.Combine(Root, "staged-directory");
        foreach (var relative in SelectedFiles)
        {
            var path = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.Copy(Path.Combine(SourceDirectory, relative), path);
        }
        return destination;
    }

    public void Dispose()
    {
        var temporaryDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Path.GetDirectoryName(Root), temporaryDirectory, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(Root).StartsWith("ymmpx-sdk-probe-", StringComparison.Ordinal))
            throw new InvalidOperationException("The fixture directory is outside the expected temp location.");
        Directory.Delete(Root, recursive: true);
    }
}
