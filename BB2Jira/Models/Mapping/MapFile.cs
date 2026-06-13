using System.Text.Json.Serialization;

namespace BB2Jira.Models.Mapping;

/// <summary>
/// Модель файла маппинга map.json.
/// Порядок свойств соответствует формату из readme: kind, status, priority, users, milestone, version.
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
}
