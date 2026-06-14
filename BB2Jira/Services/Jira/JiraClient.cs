using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BB2Jira.Models.Mapping;
using Microsoft.Extensions.Logging;

namespace BB2Jira.Services.Jira;

/// <summary>
/// Jira REST API v3 client that uses Basic authentication (email + API token).
/// </summary>
public sealed class JiraClient : IJiraClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    // Cache of accountId → displayName to avoid repeated API calls.
    private readonly Dictionary<string, string> _displayNameCache = new(StringComparer.Ordinal);

    public JiraClient(JiraSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{settings.Email}:{settings.ApiToken}"));

        _http = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _http.DefaultRequestHeaders.Accept
            .Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogDebug("Jira client: baseUrl={BaseUrl}, email={Email}", settings.BaseUrl, settings.Email);
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var url = "rest/api/3/myself";
        try
        {
            var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Jira connection OK.");
                return true;
            }

            // Log the full request URL so a wrong baseUrl is immediately visible.
            var requestUrl = new Uri(_http.BaseAddress!, url);
            _logger.LogError(
                "Jira auth check failed: {StatusCode} ({Reason}). Request URL: {Url}",
                (int)response.StatusCode, response.ReasonPhrase, requestUrl);

            // Read the Jira error body for additional context (e.g. "Basic auth with password
            // is not allowed" or "Your session has expired").
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                // Try to extract errorMessages from Jira JSON response.
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("errorMessages", out var msgs))
                    {
                        foreach (var msg in msgs.EnumerateArray())
                        {
                            _logger.LogError("Jira: {ErrorMessage}", msg.GetString());
                        }
                    }
                    else
                    {
                        // Fall back to raw body (truncated to 500 chars to avoid log spam).
                        var truncated = body.Length > 500 ? body[..500] + "…" : body;
                        _logger.LogError("Jira response body: {Body}", truncated);
                    }
                }
                catch (JsonException)
                {
                    var truncated = body.Length > 500 ? body[..500] + "…" : body;
                    _logger.LogError("Jira response body: {Body}", truncated);
                }
            }

            LogAuthHints(response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                "Jira connection error: {Message}. Check that baseUrl '{BaseUrl}' is reachable.",
                ex.Message, _http.BaseAddress);
            return false;
        }
    }

    /// <summary>Logs actionable hints based on the HTTP status code.</summary>
    private void LogAuthHints(System.Net.HttpStatusCode status)
    {
        switch (status)
        {
            case System.Net.HttpStatusCode.Unauthorized:
                _logger.LogError(
                    "HTTP 401 – verify that 'email' and 'apiToken' in map.json are correct. "
                    + "API tokens are generated at https://id.atlassian.com/manage-profile/security/api-tokens");
                break;

            case System.Net.HttpStatusCode.Forbidden:
                _logger.LogError(
                    "HTTP 403 – the token is valid but the account lacks permission to access this Jira instance. "
                    + "Ensure the user has 'Browse Projects' permission.");
                break;

            case System.Net.HttpStatusCode.NotFound:
                _logger.LogError(
                    "HTTP 404 – 'baseUrl' in map.json may be wrong. "
                    + "Expected format: https://yoursite.atlassian.net");
                break;
        }
    }

    /// <inheritdoc/>
    public async Task<List<JiraIssueRef>> SearchAsync(
        string jql, int startAt, int maxResults, CancellationToken ct = default)
    {
        // POST /rest/api/3/search/jql is the current Atlassian endpoint.
        // The older GET /rest/api/3/search was deprecated and returns 410 Gone.
        var requestBody = JsonSerializer.Serialize(new
        {
            jql,
            startAt,
            maxResults,
            fields = new[] { "key", "description" },
        });

        _logger.LogDebug(
            "Search POST rest/api/3/search/jql startAt={StartAt} maxResults={MaxResults} jql={Jql}",
            startAt, maxResults, jql);

        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _http
            .PostAsync("rest/api/3/search/jql", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        var results = new List<JiraIssueRef>();
        foreach (var issue in doc.RootElement.GetProperty("issues").EnumerateArray())
        {
            var key = issue.GetProperty("key").GetString() ?? string.Empty;

            // description is an ADF document; extract plain text from all "text" nodes.
            var description = string.Empty;
            if (issue.GetProperty("fields").TryGetProperty("description", out var descElem) &&
                descElem.ValueKind == JsonValueKind.Object)
            {
                description = ExtractAdfText(descElem);
            }

            results.Add(new JiraIssueRef(key, description));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<List<JiraTransition>> GetTransitionsAsync(
        string issueKey, CancellationToken ct = default)
    {
        var response = await _http
            .GetAsync($"rest/api/3/issue/{issueKey}/transitions", ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        var transitions = new List<JiraTransition>();
        foreach (var t in doc.RootElement.GetProperty("transitions").EnumerateArray())
        {
            var id = t.GetProperty("id").GetString() ?? string.Empty;
            var name = t.GetProperty("to").GetProperty("name").GetString() ?? string.Empty;
            transitions.Add(new JiraTransition(id, name));
        }

        return transitions;
    }

    /// <inheritdoc/>
    public async Task ApplyTransitionAsync(
        string issueKey, string transitionId, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            transition = new { id = transitionId },
        });

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _http
            .PostAsync($"rest/api/3/issue/{issueKey}/transitions", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<List<JiraComment>> GetCommentsAsync(
        string issueKey, CancellationToken ct = default)
    {
        var response = await _http
            .GetAsync($"rest/api/3/issue/{issueKey}/comment?maxResults=100&orderBy=created", ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        var comments = new List<JiraComment>();
        foreach (var c in doc.RootElement.GetProperty("comments").EnumerateArray())
        {
            var createdStr = c.GetProperty("created").GetString() ?? string.Empty;
            DateTimeOffset.TryParse(createdStr, out var created);

            var plainText = string.Empty;
            if (c.TryGetProperty("body", out var bodyElem) &&
                bodyElem.ValueKind == JsonValueKind.Object)
            {
                plainText = ExtractAdfText(bodyElem);
            }

            comments.Add(new JiraComment(created, plainText));
        }

        return comments;
    }

    /// <inheritdoc/>
    public async Task AddCommentAsync(
        string issueKey, string text, CancellationToken ct = default)
    {
        // Wrap plain text in an Atlassian Document Format (ADF) paragraph.
        var adf = new
        {
            body = new
            {
                version = 1,
                type = "doc",
                content = new[]
                {
                    new
                    {
                        type = "paragraph",
                        content = new[]
                        {
                            new { type = "text", text },
                        },
                    },
                },
            },
        };

        var body = JsonSerializer.Serialize(adf);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _http
            .PostAsync($"rest/api/3/issue/{issueKey}/comment", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<string> ResolveDisplayNameAsync(
        string accountId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return string.Empty;
        }

        if (_displayNameCache.TryGetValue(accountId, out var cached))
        {
            return cached;
        }

        try
        {
            var response = await _http
                .GetAsync($"rest/api/3/user?accountId={Uri.EscapeDataString(accountId)}", ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);

            var displayName = doc.RootElement.GetProperty("displayName").GetString() ?? accountId;
            _displayNameCache[accountId] = displayName;
            return displayName;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogDebug("Could not resolve display name for {AccountId}: {Message}", accountId, ex.Message);
            _displayNameCache[accountId] = accountId;
            return accountId;
        }
    }

    /// <summary>Recursively extracts all plain text from an ADF document element.</summary>
    private static string ExtractAdfText(JsonElement element)
    {
        var sb = new StringBuilder();
        ExtractTextNodes(element, sb);
        return sb.ToString().Trim();
    }

    private static void ExtractTextNodes(JsonElement element, StringBuilder sb)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("type", out var type) &&
            type.GetString() == "text" &&
            element.TryGetProperty("text", out var text))
        {
            sb.Append(text.GetString());
            return;
        }

        if (element.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                ExtractTextNodes(child, sb);
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
