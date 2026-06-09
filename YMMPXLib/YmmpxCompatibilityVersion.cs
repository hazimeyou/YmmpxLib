namespace YmmpxLib;

/// <summary>
/// Ymmpx の互換モード識別子です。
/// </summary>
public enum YmmpxCompatibilityVersion
{
    /// <summary>
    /// 現在の既定互換モードです。
    /// </summary>
    Latest,
    /// <summary>
    /// 旧形式 V0_1 との互換用です。
    /// </summary>
    V0_1,
    /// <summary>
    /// 旧形式 V0_2 との互換用です。
    /// </summary>
    V0_2,
}
