namespace YmmpxLib;

public sealed record YmmpxPackagingResult(
    string OutputPath,
    int ResourceCount,
    IReadOnlyDictionary<string, string> FileMap);
