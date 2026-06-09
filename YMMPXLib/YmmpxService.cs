namespace YmmpxLib;

/// <summary>
/// Ymmpx の公開サービスを生成します。
/// </summary>
public static class YmmpxService
{
    /// <summary>
    /// 指定オプションに応じた <see cref="IYmmpxService"/> 実装を返します。
    /// </summary>
    /// <param name="options">サービス生成時のオプション。</param>
    /// <returns>利用可能なサービス実装。</returns>
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
