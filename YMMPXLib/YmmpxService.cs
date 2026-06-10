namespace YmmpxLib;

/// <summary>
/// Ymmpx の入口サービスを構成します。
/// </summary>
public static class YmmpxService
{
    /// <summary>
    /// 指定したオプションに基づく <see cref="IYmmpxService"/> 実装を取得します。
    /// </summary>
    /// <param name="options">サービス実行時のオプション。</param>
    /// <returns>利用可能なサービス実装。</returns>
    public static IYmmpxService Create(YmmpxOptions? options = null)
    {
        options ??= new YmmpxOptions();

        // 現時点ではフォーマット互換モードごとの実装差はありません。
        // 将来 .ymmpx 内部フォーマットに破壊的変更が入った場合、
        // ここで互換モードごとのサービス実装に分岐します。
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
