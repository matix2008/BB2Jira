using System.Text.Json;
using BB2Jira.Models.Bitbucket;

namespace BB2Jira.Services;

/// <summary>Loads and deserializes the Bitbucket export file (db-2.0.json).</summary>
public static class BitbucketLoader
{
    /// <summary>
    /// Reads db-2.0.json from the specified path.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file was not found.</exception>
    /// <exception cref="InvalidDataException">The file could not be parsed as JSON.</exception>
    public static BitbucketExport Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Bitbucket export file not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        return Parse(json);
    }

    /// <summary>Deserializes db-2.0.json content from a string.</summary>
    public static BitbucketExport Parse(string json)
    {
        try
        {
            var export = JsonSerializer.Deserialize<BitbucketExport>(json, JsonDefaults.Read);
            return export ?? new BitbucketExport();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to parse db-2.0.json: {ex.Message}", ex);
        }
    }
}
