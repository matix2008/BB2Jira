using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Root object of the Bitbucket export file (db-2.0.json).
/// </summary>
public sealed class BitbucketExport
{
    [JsonPropertyName("issues")]
    public List<BitbucketIssue> Issues { get; set; } = new();

    [JsonPropertyName("comments")]
    public List<BitbucketComment> Comments { get; set; } = new();

    [JsonPropertyName("logs")]
    public List<BitbucketLog> Logs { get; set; } = new();

    [JsonPropertyName("milestones")]
    public List<BitbucketMilestone> Milestones { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<BitbucketVersion> Versions { get; set; } = new();
}
