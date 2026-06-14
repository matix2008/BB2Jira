namespace BB2Jira.Services.Jira;

/// <summary>A Jira issue reference returned by a search query.</summary>
public sealed record JiraIssueRef(string Key, string Description);

/// <summary>
/// One page of JQL search results.
/// <see cref="NextPageToken"/> is null when there are no further pages.
/// </summary>
public sealed record JiraSearchPage(IReadOnlyList<JiraIssueRef> Issues, string? NextPageToken);

/// <summary>A workflow transition available for a Jira issue.</summary>
public sealed record JiraTransition(string Id, string Name);

/// <summary>A comment on a Jira issue.</summary>
public sealed record JiraComment(DateTimeOffset Created, string PlainText);

/// <summary>
/// Abstraction over the Jira REST API v3 operations required by <see cref="JiraUpdater"/>.
/// Extracted as an interface to allow unit testing without live HTTP calls.
/// </summary>
public interface IJiraClient
{
    /// <summary>
    /// Verifies connectivity and authentication.
    /// Returns true when the API responds with a valid user object.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes a JQL search and returns one page of issue references.
    /// Pagination uses Jira Cloud's token-based model: pass <paramref name="nextPageToken"/>
    /// as null for the first page, then the token returned by the previous page.
    /// Each reference contains the issue key and its raw description text.
    /// </summary>
    Task<JiraSearchPage> SearchAsync(string jql, int maxResults, string? nextPageToken, CancellationToken ct = default);

    /// <summary>Returns all workflow transitions currently available for the given issue.</summary>
    Task<List<JiraTransition>> GetTransitionsAsync(string issueKey, CancellationToken ct = default);

    /// <summary>Applies the specified workflow transition to the given issue.</summary>
    Task ApplyTransitionAsync(string issueKey, string transitionId, CancellationToken ct = default);

    /// <summary>Returns up to 100 comments for the given issue, ordered by creation date.</summary>
    Task<List<JiraComment>> GetCommentsAsync(string issueKey, CancellationToken ct = default);

    /// <summary>Adds a new comment (ADF paragraph) to the given issue.</summary>
    Task AddCommentAsync(string issueKey, string text, CancellationToken ct = default);

    /// <summary>
    /// Resolves a Jira account ID to a display name.
    /// Results are cached; repeated calls for the same ID do not make additional HTTP requests.
    /// </summary>
    Task<string> ResolveDisplayNameAsync(string accountId, CancellationToken ct = default);
}
