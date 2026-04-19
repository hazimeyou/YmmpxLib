using System.Text.Json;
using System.Text.Json.Nodes;

namespace YmmpxLib;

public static class YmmpxProjectJson
{
    public static bool RemoveUiSettings(JsonNode node)
    {
        if (node is not JsonObject root)
            return false;

        var removedLayoutXml = root.Remove("LayoutXml");
        var removedToolStates = root.Remove("ToolStates");
        return removedLayoutXml || removedToolStates;
    }

    public static IEnumerable<string> FindFilePaths(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("FilePath", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var path = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        yield return path;
                }
                else
                {
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

    public static int ReplaceFilePaths(JsonNode node, IReadOnlyDictionary<string, string> linkMap)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in linkMap)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;

            normalized[item.Key] = item.Value;

            var normalizedKey = TryNormalizePath(item.Key);
            if (!string.IsNullOrWhiteSpace(normalizedKey))
                normalized[normalizedKey] = item.Value;
        }

        return ReplaceFilePathsCore(node, normalized);
    }

    private static int ReplaceFilePathsCore(JsonNode node, Dictionary<string, string> linkMap)
    {
        var count = 0;

        if (node is JsonObject obj)
        {
            foreach (var item in obj.ToList())
            {
                if (item.Key.Equals("FilePath", StringComparison.OrdinalIgnoreCase) && item.Value is JsonValue value)
                {
                    var path = value.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        if (TryResolveMappedPath(linkMap, path, out var resolved))
                        {
                            obj["FilePath"] = resolved;
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

        if (linkMap.TryGetValue(path, out var directMappedPath))
        {
            mappedPath = directMappedPath;
            return true;
        }

        var normalizedPath = TryNormalizePath(path);
        if (!string.IsNullOrWhiteSpace(normalizedPath) && linkMap.TryGetValue(normalizedPath, out var normalizedMappedPath))
        {
            mappedPath = normalizedMappedPath;
            return true;
        }

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var matchedValue = linkMap
            .Where(x => string.Equals(Path.GetFileName(x.Key), fileName, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchedValue.Count != 1)
            return false;

        mappedPath = matchedValue[0];
        return true;
    }

    private static string? TryNormalizePath(string path)
    {
        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);

            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
