namespace YmmpxLib;

/// <summary>
/// YMMPX の作成と展開を行うサービスです。
/// </summary>
public interface IYmmpxService
{
    /// <summary>
    /// 指定された YMMP プロジェクトから YMMPX パッケージを作成します。
    /// </summary>
    /// <param name="projectFilePath">入力となる .ymmp ファイルのパスです。</param>
    /// <param name="outputPath">出力する .ymmpx ファイルのパスです。</param>
    /// <param name="excludedFiles">パッケージ化から除外するファイルのパスです。</param>
    /// <param name="options">作成オプションです。</param>
    /// <param name="progress">進捗通知を受け取るコールバックです。</param>
    /// <param name="cancellationToken">処理を中断するためのトークンです。</param>
    /// <returns>作成結果を返します。</returns>
    Task<YmmpxPackagingResult> CreatePackageAsync(
        string projectFilePath,
        string outputPath,
        ISet<string>? excludedFiles = null,
        YmmpxPackagingOptions? options = null,
        IProgress<YmmpxPackagingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// YMMPX パッケージを展開し、プロジェクトの FilePath を復元します。
    /// </summary>
    /// <param name="ymmpxPath">入力となる .ymmpx ファイルのパスです。</param>
    /// <param name="extractDirectory">展開先ディレクトリです。</param>
    /// <returns>展開結果を返します。</returns>
    YmmpxUnpackResult ExtractAndRestoreProject(string ymmpxPath, string extractDirectory);
}
