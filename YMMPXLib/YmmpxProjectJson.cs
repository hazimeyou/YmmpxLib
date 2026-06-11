using System.Text.Json;
using System.Text.Json.Nodes;

namespace YmmpxLib;

/// <summary>
/// YMMP プロジェクト JSON の読み取り・書き換えを行うユーティリティです。
/// </summary>
public static class YmmpxProjectJson
{
    /// <summary>
    /// プロジェクト JSON から UI 状態関連の項目を削除します。
    /// </summary>
    /// <remarks>
    /// UI レイアウト状態は環境依存になりやすいため、配布用途では除外することがあります。
    /// </remarks>
    public static bool RemoveUiSettings(JsonNode node)
    {
        if (node is not JsonObject root)
            return false;

        var removedLayoutXml = root.Remove("LayoutXml");
        var removedToolStates = root.Remove("ToolStates");
        return removedLayoutXml || removedToolStates;
    }

    /// <summary>
    /// JSON を再帰的に走査し、<c>FilePath</c> プロパティの値を列挙します。
    /// </summary>
    public static IEnumerable<string> FindFilePaths(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                // FilePath プロパティの文字列値を返す。
                if (property.Name.Equals("FilePath", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var path = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        yield return path;
                }
                else
                {
                    // 子要素を継続して走査する。
                    foreach (var childPath in FindFilePaths(property.Value))
                        yield return childPath;
                }
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in element.EnumerateArray())
        {
            foreach (var childPath in FindFilePaths(item))
                yield return childPath;
        }
    }

    /// <summary>
    /// JSON 内の <c>FilePath</c> を対応マップに従って置換します。
    /// </summary>
    /// <returns>置換できたパス数。</returns>
    public static int ReplaceFilePaths(JsonNode node, IReadOnlyDictionary<string, string> linkMap)
    {
        var lookup = new Dictionary<string, string>(GetPathComparer());
        foreach (var item in linkMap)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;

            lookup[NormalizePathKey(item.Key)] = item.Value;
        }

        return ReplaceFilePathsCore(node, lookup);
    }

    /// <summary>
    /// パッケージ作成時に JSON 内の <c>FilePath</c> を任意の形式へ変換します。
    /// </summary>
    /// <param name="node">対象 JSON ノード。</param>
    /// <param name="pathConverter">変換関数。null を返した場合は未変更のままにします。</param>
    /// <returns>置換件数。</returns>
    public static int ReplaceFilePathsForPackaging(JsonNode node, Func<string, string?> pathConverter)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(pathConverter);
        return ReplaceFilePathsForPackagingCore(node, pathConverter);
    }

    private static int ReplaceFilePathsCore(JsonNode node, Dictionary<string, string> linkMap)
    {
        var count = 0;

        if (node is JsonObject obj)
        {
            foreach (var item in obj.ToList())
            {
                if (item.Key.Equals("FilePath", StringComparison.OrdinalIgnoreCase) &&
                    item.Value is JsonValue value &&
                    value.TryGetValue<string>(out var path))
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        // 完全一致したパスだけを解決する。
                        if (TryResolveMappedPath(linkMap, path, out var resolved))
                        {
                            obj[item.Key] = resolved;
                            count++;
                        }
                    }
                }
                else if (item.Value is not null)
                {
                    count += ReplaceFilePathsCore(item.Value, linkMap);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
            {
                if (child is not null)
                    count += ReplaceFilePathsCore(child, linkMap);
            }
        }

        return count;
    }

    private static bool TryResolveMappedPath(Dictionary<string, string> linkMap, string path, out string mappedPath)
    {
        mappedPath = string.Empty;

        var normalizedPath = NormalizePathKey(path);
        if (linkMap.TryGetValue(normalizedPath, out var directMappedPath))
        {
            mappedPath = directMappedPath;
            return true;
        }

        return false;
    }

    private static int ReplaceFilePathsForPackagingCore(JsonNode node, Func<string, string?> pathConverter)
    {
        var count = 0;

        if (node is JsonObject obj)
        {
            foreach (var item in obj.ToList())
            {
                if (item.Key.Equals("FilePath", StringComparison.OrdinalIgnoreCase) &&
                    item.Value is JsonValue value &&
                    value.TryGetValue<string>(out var path))
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var converted = pathConverter(path);
                        if (!string.IsNullOrWhiteSpace(converted) && !string.Equals(path, converted, StringComparison.Ordinal))
                        {
                            obj[item.Key] = converted;
                            count++;
                        }
                    }
                }
                else if (item.Value is not null)
                {
                    count += ReplaceFilePathsForPackagingCore(item.Value, pathConverter);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
            {
                if (child is not null)
                    count += ReplaceFilePathsForPackagingCore(child, pathConverter);
            }
        }

        return count;
    }

    private static string NormalizePathKey(string path)
    {
        // 相対パスは壊さず、区切り差だけを吸収する。
        return path.Replace('\\', '/').Trim();
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static string? TryNormalizePath(string path)
    {
        try
        {
            // file:// URI はローカルパスへ変換する。
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);

            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
