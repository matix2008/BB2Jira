using System.Text.Json.Serialization;

namespace BB2Jira.Models.Mapping;

/// <summary>
/// Jira connection settings and update flags stored in the "jira" section of map.json.
/// </summary>
public sealed class JiraSettings
{
    /// <summary>Base URL of the Jira Cloud instance, e.g. https://yoursite.atlassian.net</summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Jira project key, e.g. PROJ</summary>
    [JsonPropertyName("projectKey")]
    public string ProjectKey { get; set; } = string.Empty;

    /// <summary>Atlassian account email used for Basic auth.</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Atlassian API token (generated in Atlassian Account Settings).</summary>
    [JsonPropertyName("apiToken")]
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the Bitbucket repository used to locate issues in Jira descriptions,
    /// e.g. https://bitbucket.org/yourorg/yourrepo
    /// </summary>
    [JsonPropertyName("bitbucketRepoUrl")]
    public string BitbucketRepoUrl { get; set; } = string.Empty;

    /// <summary>When true, update the Status of each issue via Jira workflow transitions.</summary>
    [JsonPropertyName("updateStatus")]
    public bool UpdateStatus { get; set; } = true;

    /// <summary>When true, add missing comments (determined by date) to each issue.</summary>
    [JsonPropertyName("updateComments")]
    public bool UpdateComments { get; set; } = true;

    /// <summary>Returns true when all required connection fields are filled.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ProjectKey) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(ApiToken) &&
        !string.IsNullOrWhiteSpace(BitbucketRepoUrl);
}
