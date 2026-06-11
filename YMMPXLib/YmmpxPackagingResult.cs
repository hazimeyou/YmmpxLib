namespace YmmpxLib;

/// <summary>
/// YMMPX パッケージ作成処理の結果です。
/// </summary>
public sealed record YmmpxPackagingResult
{
    /// <summary>
    /// 出力された ymmpx ファイルのパス (絶対/相対)。
    /// </summary>
    public string OutputPath { get; init; }

    /// <summary>
    /// パッケージに格納された重複排除後のリソース数。
    /// </summary>
    public int ResourceCount { get; init; }

    /// <summary>
    /// 保存ファイル名 -> パッケージ内パスの対応表。
    /// </summary>
    public IReadOnlyDictionary<string, string> FileMap { get; init; }

    /// <summary>
    /// 新しい結果を作成します。
    /// </summary>
    public YmmpxPackagingResult(
        string outputPath,
        int resourceCount,
        IReadOnlyDictionary<string, string> fileMap)
    {
        OutputPath = outputPath;
        ResourceCount = resourceCount;
        FileMap = fileMap;
    }
}
