namespace YmmpxLib;

/// <summary>
/// .ymmpx の作成・展開・内部ファイル処理に使用する互換モード識別子です。
/// YmmpxLib 自体の API バージョンではありません。
/// </summary>
public enum YmmpxCompatibilityVersion
{
    /// <summary>
    /// 現在の既定フォーマット互換モードです。
    /// </summary>
    Latest,

    /// <summary>
    /// 旧形式 V0_1 の .ymmpx 内部フォーマット互換用です。
    /// 現時点では Latest と同じ実装に解決されます。
    /// </summary>
    V0_1,

    /// <summary>
    /// 旧形式 V0_2 の .ymmpx 内部フォーマット互換用です。
    /// 現時点では Latest と同じ実装に解決されます。
    /// </summary>
    V0_2,
}
