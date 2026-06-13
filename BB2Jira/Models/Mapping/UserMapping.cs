using System.Text.Json.Serialization;

namespace BB2Jira.Models.Mapping;

/// <summary>
/// Сопоставление пользователя Bitbucket с пользователем Jira (значение map.users[key]).
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
    /// Возвращает идентификатор пользователя Jira: jiraAccountId, при его отсутствии — jiraEmail.
    /// Пустая строка означает, что пользователь не сопоставлен.
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
