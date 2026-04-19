namespace YmmpxLib;

public sealed record YmmpxUnpackResult(
    string ExtractDirectory,
    string ProjectFilePath,
    int ReplacedPathCount,
    IReadOnlyDictionary<string, string> LinkMap);
