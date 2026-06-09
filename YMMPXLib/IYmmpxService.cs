namespace YmmpxLib;

/// <summary>
/// YMMPX の作成と展開を行うサービスの共通インターフェースです。
/// </summary>
public interface IYmmpxService
{
    /// <summary>
    /// 指定した .ymmp プロジェクトから .ymmpx パッケージを作成します。
    /// </summary>
    /// <param name="projectFilePath">入力 .ymmp ファイルのパス。</param>
    /// <param name="outputPath">出力 .ymmpx ファイルのパス。</param>
    /// <param name="excludedFiles">パッケージ対象から除外するファイルの集合。</param>
    /// <param name="options">パッケージング時のオプション。</param>
    /// <param name="progress">進捗通知先。</param>
    /// <param name="cancellationToken">キャンセル要求。</param>
    /// <returns>パッケージング結果。</returns>
    Task<YmmpxPackagingResult> CreatePackageAsync(
        string projectFilePath,
        string outputPath,
        ISet<string>? excludedFiles = null,
        YmmpxPackagingOptions? options = null,
        IProgress<YmmpxPackagingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// .ymmpx パッケージを展開し、プロジェクト JSON 内の FilePath を復元します。
    /// </summary>
    /// <param name="ymmpxPath">入力 .ymmpx ファイルのパス。</param>
    /// <param name="extractDirectory">展開先ディレクトリ。</param>
    /// <returns>展開と復元の結果。</returns>
    YmmpxUnpackResult ExtractAndRestoreProject(string ymmpxPath, string extractDirectory);
}
