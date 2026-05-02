namespace YmmpxLib;

public static class YmmpxService
{
    public static IYmmpxService Create(YmmpxOptions? options = null)
    {
        options ??= new YmmpxOptions();

        return options.CompatibilityVersion switch
        {
            YmmpxCompatibilityVersion.Latest => LatestYmmpxService.Instance,
            YmmpxCompatibilityVersion.V0_1 => LatestYmmpxService.Instance,
            YmmpxCompatibilityVersion.V0_2 => LatestYmmpxService.Instance,
            _ => LatestYmmpxService.Instance,
        };
    }

    private sealed class LatestYmmpxService : IYmmpxService
    {
        public static readonly LatestYmmpxService Instance = new();

        public Task<YmmpxPackagingResult> CreatePackageAsync(
            string projectFilePath,
            string outputPath,
            ISet<string>? excludedFiles = null,
            YmmpxPackagingOptions? options = null,
            IProgress<YmmpxPackagingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return YmmpxPackageService.CreatePackageAsync(
                projectFilePath,
                outputPath,
                excludedFiles,
                options,
                progress,
                cancellationToken);
        }

        public YmmpxUnpackResult ExtractAndRestoreProject(string ymmpxPath, string extractDirectory)
        {
            return YmmpxPackageService.ExtractAndRestoreProject(ymmpxPath, extractDirectory);
        }
    }
}
