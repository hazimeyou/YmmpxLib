namespace YmmpxLib;

/// <summary>
/// Ymmpx サービス全般の実行オプションです。
/// </summary>
public sealed class YmmpxOptions
{
    /// <summary>
    /// .ymmpx の作成・展開・内部ファイル処理に使用する互換モードを指定します。
    /// これは YmmpxLib 自体の API バージョンではありません。
    /// </summary>
    public YmmpxCompatibilityVersion CompatibilityVersion { get; set; } = YmmpxCompatibilityVersion.Latest;
}
