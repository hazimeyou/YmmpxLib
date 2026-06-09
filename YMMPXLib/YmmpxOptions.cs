namespace YmmpxLib;

/// <summary>
/// Ymmpx サービス全体の実行オプションです。
/// </summary>
public sealed class YmmpxOptions
{
    /// <summary>
    /// 互換モードのバージョンを指定します。
    /// </summary>
    public YmmpxCompatibilityVersion CompatibilityVersion { get; set; } = YmmpxCompatibilityVersion.Latest;
}
