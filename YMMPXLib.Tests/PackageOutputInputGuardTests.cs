using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace YmmpxLib.Tests;

public sealed class PackageOutputInputGuardTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectsProjectOrResourcePathBeforeWriting(bool useResource, bool excludeResource)
    {
        using var workspace = new Workspace();
        var input = workspace.WriteFile(useResource ? "resource.ymmpx" : "project.ymmp", [0, 1, 255]);
        var project = useResource ? workspace.WriteProject("resource.ymmpx") : workspace.WriteProject();
        var projectBytes = File.ReadAllBytes(project);
        var inputBytes = File.ReadAllBytes(input);
        var outputName = Path.GetFileName(input);
        if (OperatingSystem.IsWindows())
            outputName = outputName.ToUpperInvariant();
        Directory.CreateDirectory(workspace.GetPath("nested"));
        var output = Path.Combine(workspace.Path, "nested", "..", outputName);
        var excluded = excludeResource ? new HashSet<string> { "resource.ymmpx" } : null;

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => YmmpxPackageService.CreatePackageAsync(project, output, excluded, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("outputPath", error.ParamName);
        Assert.Equal(inputBytes, File.ReadAllBytes(input));
        Assert.Equal(projectBytes, File.ReadAllBytes(project));
        workspace.AssertNoTemporaryOutput();
    }

    [Theory]
    [InlineData("hard link")]
    [InlineData("file symbolic link")]
    [InlineData("directory alias")]
    public async Task RejectsExistingOutputAliasAndPreservesExcludedInput(string aliasKind)
    {
        using var workspace = new Workspace();
        var input = workspace.WriteFile("source/resource.ymmpx", [0, 11, 200, 255]);
        var project = workspace.WriteProject("source/resource.ymmpx");
        var projectBytes = File.ReadAllBytes(project);
        var inputBytes = File.ReadAllBytes(input);
        var output = workspace.GetPath("output.ymmpx");

        if (aliasKind == "directory alias")
        {
            var alias = workspace.GetPath("alias");
            CreateDirectoryAlias(alias, workspace.GetPath("source"));
            workspace.DirectoryAlias = alias;
            output = Path.Combine(alias, Path.GetFileName(input));
        }
        else if (aliasKind == "file symbolic link")
        {
            try
            {
                File.CreateSymbolicLink(output, input);
            }
            catch (IOException linkError) when (OperatingSystem.IsWindows() && (linkError.HResult & 0xffff) == 1314)
            {
                Assert.Skip("The Windows task does not have permission to create file symbolic links.");
            }
        }
        else
        {
            CreateHardLink(output, input);
        }

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => YmmpxPackageService.CreatePackageAsync(project, output, new HashSet<string> { input }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("outputPath", error.ParamName);
        Assert.Equal(inputBytes, File.ReadAllBytes(input));
        Assert.Equal(inputBytes, File.ReadAllBytes(output));
        Assert.Equal(projectBytes, File.ReadAllBytes(project));
        workspace.AssertNoTemporaryOutput();
    }

    [Fact]
    public async Task RejectsAliasCreatedAfterResourceCopyAndRemovesTemporaryOutput()
    {
        using var workspace = new Workspace();
        var source = workspace.WriteFile("resource.ymmpx", [132, 0, 55]);
        var project = workspace.WriteProject("resource.ymmpx");
        var output = workspace.GetPath("output.ymmpx");
        var projectBytes = File.ReadAllBytes(project);
        var inputBytes = File.ReadAllBytes(source);
        var aliasCreated = false;
        var progress = new InlineProgress(value =>
        {
            if (value.Message == "Packed resource.ymmpx")
            {
                CreateHardLink(output, source);
                aliasCreated = true;
            }
        });

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => YmmpxPackageService.CreatePackageAsync(project, output, progress: progress, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("outputPath", error.ParamName);
        Assert.True(aliasCreated);
        Assert.Equal(inputBytes, File.ReadAllBytes(source));
        Assert.Equal(inputBytes, File.ReadAllBytes(output));
        Assert.Equal(projectBytes, File.ReadAllBytes(project));
        workspace.AssertNoTemporaryOutput();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesUnrelatedOutputThatAppearsDuringPackaging(bool replaceExisting)
    {
        using var workspace = new Workspace();
        var source = workspace.WriteFile("resource.ymmpx", [132, 0, 55]);
        var project = workspace.WriteProject("resource.ymmpx");
        var output = workspace.GetPath("output.ymmpx");
        var projectBytes = File.ReadAllBytes(project);
        var inputBytes = File.ReadAllBytes(source);
        if (replaceExisting)
            workspace.WriteFile("output.ymmpx", [15, 16, 17]);
        var backup = workspace.GetPath("previous.ymmpx");
        byte[] concurrentBytes = [44, 77, 66];
        var progress = new InlineProgress(value =>
        {
            if (value.Message == "Packed resource.ymmpx")
            {
                if (replaceExisting)
                    File.Move(output, backup);
                File.WriteAllBytes(output, concurrentBytes);
            }
        });

        await Assert.ThrowsAsync<IOException>(
            () => YmmpxPackageService.CreatePackageAsync(
                project, output, progress: progress, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(concurrentBytes, File.ReadAllBytes(output));
        if (replaceExisting)
            Assert.Equal(new byte[] { 15, 16, 17 }, File.ReadAllBytes(backup));
        Assert.Equal(inputBytes, File.ReadAllBytes(source));
        Assert.Equal(projectBytes, File.ReadAllBytes(project));
        workspace.AssertNoTemporaryOutput();
    }

    [Fact]
    public async Task ReplacesUnrelatedExistingOutputAndPreservesInputBytes()
    {
        using var workspace = new Workspace();
        var input = workspace.WriteFile("resource.ymmpx", [55, 0, 93]);
        var output = workspace.WriteFile("output.ymmpx", File.ReadAllBytes(input));
        var project = workspace.WriteProject("resource.ymmpx", "invalid*asset.png");
        var projectBytes = File.ReadAllBytes(project);
        var inputBytes = File.ReadAllBytes(input);

        var package = await YmmpxPackageService.CreatePackageAsync(project, output, cancellationToken: TestContext.Current.CancellationToken);
        var unpacked = YmmpxPackageService.ExtractAndRestoreProject(output, workspace.GetPath("unpacked"));

        Assert.Equal(1, package.ResourceCount);
        Assert.Equal(1, unpacked.ReplacedPathCount);
        Assert.Equal(inputBytes, File.ReadAllBytes(input));
        Assert.Equal(projectBytes, File.ReadAllBytes(project));
        workspace.AssertNoTemporaryOutput();
    }

    private static void CreateDirectoryAlias(string alias, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, target);
            return;
        }

        var start = new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{alias}\" \"{target}\"")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{output}{error}");
    }

    private static void CreateHardLink(string alias, string target)
    {
        var succeeded = OperatingSystem.IsWindows()
            ? CreateHardLinkWindows(alias, target, IntPtr.Zero)
            : CreateHardLinkUnix(target, alias) == 0;
        Assert.True(succeeded, $"Hard link creation failed with OS error {Marshal.GetLastPInvokeError()}.");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string alias, string target, IntPtr reserved);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string alias);

    private sealed class InlineProgress(Action<YmmpxPackagingProgress> report) : IProgress<YmmpxPackagingProgress>
    {
        public void Report(YmmpxPackagingProgress value) => report(value);
    }

    private sealed class Workspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("ymmpx-output-guard-").FullName;
        public string? DirectoryAlias { get; set; }

        public string GetPath(string relativePath) => System.IO.Path.Combine(Path, relativePath);

        public string WriteFile(string relativePath, byte[] bytes)
        {
            var path = GetPath(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public string WriteProject(params string[] paths)
        {
            var project = GetPath("project.ymmp");
            File.WriteAllText(project, JsonSerializer.Serialize(new { Items = paths.Select(path => new { FilePath = path }) }));
            return project;
        }

        public void AssertNoTemporaryOutput() => Assert.Empty(Directory.GetFiles(Path, ".*.tmp", SearchOption.AllDirectories));

        public void Dispose()
        {
            if (DirectoryAlias is not null)
                Directory.Delete(DirectoryAlias);
            Directory.Delete(Path, recursive: true);
        }
    }
}
