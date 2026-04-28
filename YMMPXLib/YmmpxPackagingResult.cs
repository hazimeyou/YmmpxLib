namespace YmmpxLib;

/// <summary>
/// YMMPX パッケージ作成処理の結果です。
/// </summary>
public sealed record YmmpxPackagingResult(
    // 出力された ymmpx ファイルのパス (絶対/相対)。
    string OutputPath,
    // パッケージに格納された重複排除後のリソース数。
    int ResourceCount,
    // 元のプロジェクトパス -> パッケージ内パスの対応表。
    IReadOnlyDictionary<string, string> FileMap);
