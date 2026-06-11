using System.Reflection;

namespace YmmpxLib;

/// <summary>
/// YmmpxLib の公開 API と配布用バージョン情報を提供します。
/// </summary>
public static class YmmpxLibraryInfo
{
    /// <summary>
    /// 公開 API のバージョンを表します。
    /// NuGet の PackageVersion や AssemblyVersion とは独立した互換性表記です。
    /// </summary>
    public const string ApiVersion = "0.1";

    /// <summary>
    /// 公開 API のバージョン番号を数値で表します。
    /// 将来の互換性判定や表示ロジックで利用できます。
    /// </summary>
    public const int ApiVersionCode = 100;

    /// <summary>
    /// 内部実装向けのバージョンを表します。
    /// API バージョンとは別に、検証やログ用途で使う想定です。
    /// </summary>
    public const string InternalVersion = "0.1.0-internal.1";

    /// <summary>
    /// 内部実装向けバージョン番号を数値で表します。
    /// </summary>
    public const int InternalVersionCode = 100;

    /// <summary>
    /// アセンブリのバージョンを取得します。
    /// </summary>
    public static Version AssemblyVersion =>
        typeof(YmmpxLibraryInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// アセンブリの InformationalVersion を取得します。
    /// PackageVersion や SourceRevisionId が含まれる場合があります。
    /// </summary>
    public static string InformationalVersion =>
        typeof(YmmpxLibraryInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? string.Empty;
}
