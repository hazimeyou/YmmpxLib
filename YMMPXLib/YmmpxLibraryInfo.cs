using System.Reflection;

namespace YmmpxLib;

/// <summary>
/// YmmpxLib 自体の公開 API / 実装情報を提供します。
/// </summary>
public static class YmmpxLibraryInfo
{
    /// <summary>
    /// YmmpxLib の公開 API 世代を表します。
    /// NuGet PackageVersion や AssemblyVersion とは独立した、ライブラリ利用者向けの API バージョンです。
    /// </summary>
    public const string ApiVersion = "0.1";

    /// <summary>
    /// YmmpxLib の公開 API 世代を数値比較しやすい形で表した値です。
    /// 互換性診断や将来の API 互換判定に使用できます。
    /// </summary>
    public const int ApiVersionCode = 100;

    /// <summary>
    /// YmmpxLib の内部実装バージョンです。
    /// API バージョンとは異なり、実装・診断・ログ用途の識別子です。
    /// </summary>
    public const string InternalVersion = "0.1.0-internal.1";

    /// <summary>
    /// YmmpxLib の内部実装バージョンを数値比較しやすい形で表した値です。
    /// </summary>
    public const int InternalVersionCode = 100;

    /// <summary>
    /// YmmpxLib アセンブリのバージョンです。
    /// </summary>
    public static Version AssemblyVersion =>
        typeof(YmmpxLibraryInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// YmmpxLib アセンブリの情報バージョンです。
    /// NuGet PackageVersion や SourceRevisionId が反映される場合があります。
    /// </summary>
    public static string InformationalVersion =>
        typeof(YmmpxLibraryInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? string.Empty;
}
