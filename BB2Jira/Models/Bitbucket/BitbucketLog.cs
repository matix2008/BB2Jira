using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Запись истории изменений задачи (элемент logs[]).
/// </summary>
public sealed class BitbucketLog
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Идентификатор задачи, к которой относится запись истории.</summary>
    [JsonPropertyName("issue")]
    public int Issue { get; set; }

    [JsonPropertyName("user")]
    public BitbucketUser? User { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("changed_from")]
    public string? ChangedFrom { get; set; }

    [JsonPropertyName("changed_to")]
    public string? ChangedTo { get; set; }

    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }
}
