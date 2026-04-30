namespace YmmpxLib;

/// <summary>
/// パッケージ作成処理の進捗情報です。
/// </summary>
public sealed record YmmpxPackagingProgress(int CompletedCount, int TotalCount, string Message)
{
    /// <summary>
    /// 処理済みバイト数です。
    /// </summary>
    public long ProcessedBytes { get; init; }

    /// <summary>
    /// 処理対象の総バイト数です。
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// バイト情報付きで進捗を初期化します。
    /// </summary>
    public YmmpxPackagingProgress(
        int completedCount,
        int totalCount,
        string message,
        long processedBytes,
        long totalBytes)
        : this(completedCount, totalCount, message)
    {
        ProcessedBytes = processedBytes;
        TotalBytes = totalBytes;
    }

    /// <summary>
    /// 完了率 (%) を返します。
    /// </summary>
    public double Percentage
    {
        get
        {
            if (TotalBytes > 0)
                return (double)ProcessedBytes / TotalBytes * 100;

            return TotalCount <= 0 ? 0 : (double)CompletedCount / TotalCount * 100;
        }
    }
}
