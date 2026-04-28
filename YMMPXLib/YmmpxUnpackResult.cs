namespace YmmpxLib;

/// <summary>
/// YMMPX 展開およびパス復元処理の結果です。
/// </summary>
public sealed record YmmpxUnpackResult(
    // 展開先ディレクトリ。
    string ExtractDirectory,
    // 復元後のプロジェクト (.ymmp) ファイルパス。
    string ProjectFilePath,
    // FilePath を置換できた件数。
    int ReplacedPathCount,
    // 元パス -> 展開後ファイルパスの対応表。
    IReadOnlyDictionary<string, string> LinkMap);
