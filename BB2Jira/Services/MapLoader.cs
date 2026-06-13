using System.Text.Json;
using BB2Jira.Models.Mapping;

namespace BB2Jira.Services;

/// <summary>Loads an existing map.json mapping file.</summary>
public static class MapLoader
{
    /// <summary>
    /// Reads map.json. If the file is missing, returns an empty model
    /// (acceptable on the first generation).
    /// </summary>
    public static MapFile Load(string path)
    {
        if (!File.Exists(path))
        {
            return new MapFile();
        }

        var json = File.ReadAllText(path);
        return Parse(json);
    }

    /// <summary>Deserializes map.json content from a string.</summary>
    public static MapFile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MapFile();
        }

        try
        {
            var map = JsonSerializer.Deserialize<MapFile>(json, JsonDefaults.Read);
            return map ?? new MapFile();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to parse map.json: {ex.Message}", ex);
        }
    }
}
