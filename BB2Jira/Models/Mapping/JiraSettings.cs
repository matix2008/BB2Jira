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

    /// <summary>
    /// Strategy used in phase 1 to link a Bitbucket issue ID to a Jira issue:
    /// <list type="bullet">
    /// <item><c>serviceBlock</c> (default) — matches the "Bitbucket Issue ID: {id}" block
    /// written by this utility's CSV import.</item>
    /// <item><c>url</c> — matches the Bitbucket issue URL "{bitbucketRepoUrl}/issues/{id}"
    /// written by Jira's native Bitbucket importer.</item>
    /// </list>
    /// </summary>
    [JsonPropertyName("matchBy")]
    public string MatchBy { get; set; } = MatchByServiceBlock;

    /// <summary>Match strategy value: the "Bitbucket Issue ID: {id}" service block.</summary>
    public const string MatchByServiceBlock = "serviceBlock";

    /// <summary>Match strategy value: the "{bitbucketRepoUrl}/issues/{id}" issue URL.</summary>
    public const string MatchByUrl = "url";

    /// <summary>When true, update the Status of each issue via Jira workflow transitions.</summary>
    [JsonPropertyName("updateStatus")]
    public bool UpdateStatus { get; set; } = true;

    /// <summary>When true, add missing comments (determined by date) to each issue.</summary>
    [JsonPropertyName("updateComments")]
    public bool UpdateComments { get; set; } = true;

    /// <summary>True when <see cref="MatchBy"/> selects the Bitbucket issue URL strategy.</summary>
    public bool MatchByIssueUrl =>
        MatchByUrl.Equals(MatchBy, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when all required connection fields are filled.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ProjectKey) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(ApiToken) &&
        // bitbucketRepoUrl is only required for the "url" match strategy.
        (!MatchByIssueUrl || !string.IsNullOrWhiteSpace(BitbucketRepoUrl));
}
