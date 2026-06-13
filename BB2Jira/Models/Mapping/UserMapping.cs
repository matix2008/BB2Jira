using System.Text.Json.Serialization;

namespace BB2Jira.Models.Mapping;

/// <summary>
/// Mapping of a Bitbucket user to a Jira user (the map.users[key] value).
/// </summary>
public sealed class UserMapping
{
    [JsonPropertyName("bitbucketDisplayName")]
    public string BitbucketDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("jiraAccountId")]
    public string JiraAccountId { get; set; } = string.Empty;

    [JsonPropertyName("jiraEmail")]
    public string JiraEmail { get; set; } = string.Empty;

    [JsonPropertyName("jiraDisplayName")]
    public string JiraDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Returns the Jira user identifier: jiraAccountId, or jiraEmail when it is absent.
    /// An empty string means the user is not mapped.
    /// </summary>
    public string ResolveJiraUser()
    {
        if (!string.IsNullOrWhiteSpace(JiraAccountId))
        {
            return JiraAccountId;
        }

        if (!string.IsNullOrWhiteSpace(JiraEmail))
        {
            return JiraEmail;
        }

        return string.Empty;
    }
}
