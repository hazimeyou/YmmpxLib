namespace YmmpxLib;

/// <summary>
/// YMMPX の作成と展開を行うサービスです。
/// </summary>
public interface IYmmpxService
{
    Task<YmmpxPackagingResult> CreatePackageAsync(
        string projectFilePath,
        string outputPath,
        ISet<string>? excludedFiles = null,
        YmmpxPackagingOptions? options = null,
        IProgress<YmmpxPackagingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    YmmpxUnpackResult ExtractAndRestoreProject(string ymmpxPath, string extractDirectory);
}
