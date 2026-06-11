namespace YmmpxLib;

/// <summary>
/// Ymmpx サービス全体の動作オプションです。
/// </summary>
public sealed class YmmpxOptions
{
    /// <summary>
    /// 利用する互換性バージョンです。
    /// 現状は Latest と同等の実装を返しますが、将来の形式差分に備えて保持しています。
    /// </summary>
    public YmmpxCompatibilityVersion CompatibilityVersion { get; set; } = YmmpxCompatibilityVersion.Latest;
}
