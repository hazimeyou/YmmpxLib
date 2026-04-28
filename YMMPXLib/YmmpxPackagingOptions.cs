namespace YmmpxLib;

/// <summary>
/// YMMPX パッケージ作成時の挙動を制御するオプションです。
/// </summary>
public sealed record YmmpxPackagingOptions
{
    /// <summary>
    /// <c>false</c> の場合、プロジェクト JSON から UI 関連設定
    /// (<c>LayoutXml</c> / <c>ToolStates</c>) を除外して保存します。
    /// </summary>
    public bool IncludeProjectUiSettings { get; init; } = true;
}
