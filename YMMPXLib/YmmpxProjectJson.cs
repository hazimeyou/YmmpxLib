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
                if (property.Name == "FilePath" && property.Value.ValueKind == JsonValueKind.String)
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
        var normalized = linkMap is Dictionary<string, string> dict && dict.Comparer.Equals(StringComparer.OrdinalIgnoreCase)
            ? dict
            : new Dictionary<string, string>(linkMap, StringComparer.OrdinalIgnoreCase);

        return ReplaceFilePathsCore(node, normalized);
    }

    private static int ReplaceFilePathsCore(JsonNode node, Dictionary<string, string> linkMap)
    {
        var count = 0;

        if (node is JsonObject obj)
        {
            foreach (var item in obj.ToList())
            {
                if (item.Key == "FilePath" && item.Value is JsonValue value)
                {
                    var path = value.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        if (linkMap.TryGetValue(path, out var resolved))
                        {
                            obj["FilePath"] = resolved;
                            count++;
                        }
                        else
                        {
                            var match = linkMap
                                .Where(x => path.Contains(x.Key, StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(x => x.Key.Length)
                                .FirstOrDefault();

                            if (!string.IsNullOrWhiteSpace(match.Key))
                            {
                                obj["FilePath"] = match.Value;
                                count++;
                            }
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
}
