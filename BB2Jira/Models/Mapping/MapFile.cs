using System.Text.Json.Serialization;

namespace BB2Jira.Models.Mapping;

/// <summary>
/// Model of the map.json mapping file.
/// Property order matches the format from readme: kind, status, priority, users, milestone, version.
/// </summary>
public sealed class MapFile
{
    [JsonPropertyName("kind")]
    public SortedDictionary<string, string> Kind { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("status")]
    public SortedDictionary<string, string> Status { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("priority")]
    public SortedDictionary<string, string> Priority { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("users")]
    public SortedDictionary<string, UserMapping> Users { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("milestone")]
    public SortedDictionary<string, string> Milestone { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("version")]
    public SortedDictionary<string, string> Version { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Jira connection settings for the -u update mode. Null when not configured.</summary>
    [JsonPropertyName("jira")]
    public JiraSettings? Jira { get; set; }
}
