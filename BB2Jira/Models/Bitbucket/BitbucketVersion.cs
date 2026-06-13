using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Запись справочника versions[] и значение issues[].version.
/// </summary>
public sealed class BitbucketVersion
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
