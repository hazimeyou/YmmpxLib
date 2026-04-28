namespace YmmpxLib;

/// <summary>
/// パッケージ作成処理の進捗情報です。
/// </summary>
public sealed record YmmpxPackagingProgress(int CompletedCount, int TotalCount, string Message)
{
    /// <summary>
    /// 完了率 (%) を返します。
    /// </summary>
    public double Percentage => TotalCount <= 0 ? 0 : (double)CompletedCount / TotalCount * 100;
}
