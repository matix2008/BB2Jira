using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Bitbucket user (reporter, assignee, comment author, or history entry author).
/// </summary>
public sealed class BitbucketUser
{
    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>
    /// User key in priority order: account_id, then display_name.
    /// </summary>
    public string? Key
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AccountId))
            {
                return AccountId;
            }

            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            return null;
        }
    }
}
