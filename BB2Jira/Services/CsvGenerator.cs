using BB2Jira.Logging;
using System.Globalization;
using BB2Jira.Logging;
using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;

namespace BB2Jira.Services;

/// <summary>√енераци€ файла импорта import.csv (ключ -c).</summary>
public static class CsvGenerator
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] BaseColumns =
    {
        "Issue Type", "Summary", "Description", "Status", "Priority",
        "Reporter", "Assignee", "Created", "Updated", "Fix Version/s",
        "Bitbucket Milestone", "Bitbucket Issue ID",
    };

    /// <summary>
    /// ‘ормирует import.csv на основе экспорта Bitbucket и маппинга и сохран€ет его по указанному пути.
    /// »тогова€ статистика и проблемы записываютс€ в <paramref name="logger"/>.
    /// </summary>
    public static void Generate(BitbucketExport export, MapFile map, string outputPath, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(logger);

        var writer = BuildCsv(export, map, logger);
        writer.Save(outputPath);
        logger.Info($"import.csv сохранЄн: {outputPath}");
    }

    /// <summary>
    /// —троит содержимое import.csv. ѕубличный метод дл€ модульного тестировани€.
    /// </summary>
    public static CsvWriter BuildCsv(BitbucketExport export, MapFile map, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(logger);

        var stats = new GenerationStats();
        var commentsByIssue = export.Comments.ToLookup(c => c.Issue);
        var logsByIssue = export.Logs.ToLookup(l => l.Issue);

        var rows = new List<List<string>>();
        var maxEvents = 0;

        foreach (var issue in export.Issues.OrderBy(i => i.Id))
        {
            stats.ProcessedIssues++;

            if (!TryResolveIssueType(issue, map, logger, stats, out var issueType))
            {
                stats.SkippedIssues.Add(issue.Id);
                continue;
            }

            var events = BuildEvents(issue, commentsByIssue[issue.Id], logsByIssue[issue.Id], map, stats);
            maxEvents = Math.Max(maxEvents, events.Count);

            var row = new List<string>(BaseColumns.Length + events.Count)
            {
                issueType,
                BuildSummary(issue),
                BuildDescription(issue),
                ResolveStatus(issue, map, logger, stats),
                ResolvePriority(issue, map, logger, stats),
                ResolveReporter(issue, map, logger, stats),
                ResolveAssignee(issue, map),
                FormatDate(issue.CreatedOn),
                ResolveUpdated(issue),
                ResolveFixVersion(issue, map, logger, stats),
                ResolveMilestone(issue, map, logger, stats),
                issue.Id.ToString(CultureInfo.InvariantCulture),
            };

            row.AddRange(events);
            rows.Add(row);
            stats.ExportedRows++;
        }

        var commentColumns = Math.Max(1, maxEvents);
        var writer = new CsvWriter();
        writer.WriteRow(BuildHeader(commentColumns));

        foreach (var row in rows)
        {
            PadRow(row, BaseColumns.Length + commentColumns);
            writer.WriteRow(row);
        }

        WriteSummary(logger, stats);
        return writer;
    }

    /// <summary>Summary = issues.title, либо "Bitbucket issue {id}" дл€ пустого заголовка.</summary>
    public static string BuildSummary(BitbucketIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return string.IsNullOrWhiteSpace(issue.Title)
            ? $"Bitbucket issue {issue.Id}"
            : issue.Title;
    }

    /// <summary>Description = issues.content + служебный блок импорта.</summary>
    public static string BuildDescription(BitbucketIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var serviceBlock = $"---\r\n\r\nImported from Bitbucket\r\nBitbucket Issue ID: {issue.Id}";
        var content = issue.Content?.Text;

        return string.IsNullOrWhiteSpace(content)
            ? serviceBlock
            : $"{content}\r\n\r\n{serviceBlock}";
    }

    private static IEnumerable<string> BuildHeader(int commentColumns)
    {
        foreach (var column in BaseColumns)
        {
            yield return column;
        }

        for (var i = 0; i < commentColumns; i++)
        {
            yield return "Comment";
        }
    }

    private static bool TryResolveIssueType(
        BitbucketIssue issue,
        MapFile map,
        AppLogger logger,
        GenerationStats stats,
        out string issueType)
    {
        issueType = string.Empty;
        var kind = issue.Kind?.Trim();

        if (string.IsNullOrWhiteSpace(kind) || !map.Kind.TryGetValue(kind, out var mapped) || string.IsNullOrWhiteSpace(mapped))
        {
            stats.MissingMapValues.Add($"kind: '{kind}'");
            stats.EmptyRequiredFields++;
            logger.Warning($"Issue {issue.Id}: kind '{kind}' отсутствует в map.json Ч задача пропущена.");
            return false;
        }

        // ¬ CSV включаютс€ только задачи, у которых kind маппитс€ в Task или Bug.
        if (!mapped.Equals("Task", StringComparison.OrdinalIgnoreCase) &&
            !mapped.Equals("Bug", StringComparison.OrdinalIgnoreCase))
        {
            logger.Info($"Issue {issue.Id}: тип '{mapped}' не Task/Bug Ч задача пропущена.");
            return false;
        }

        issueType = mapped;
        return true;
    }

    private static string ResolveStatus(BitbucketIssue issue, MapFile map, AppLogger logger, GenerationStats stats)
    {
        var status = issue.Status?.Trim();
        if (!string.IsNullOrWhiteSpace(status) && map.Status.TryGetValue(status, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        stats.MissingMapValues.Add($"status: '{status}'");
        stats.EmptyRequiredFields++;
        logger.Warning($"Issue {issue.Id}: status '{status}' отсутствует в map.json Ч поле Status пустое.");
        return string.Empty;
    }

    private static string ResolvePriority(BitbucketIssue issue, MapFile map, AppLogger logger, GenerationStats stats)
    {
        var priority = issue.Priority?.Trim();
        if (string.IsNullOrWhiteSpace(priority))
        {
            return MapDefaults.DefaultPriority;
        }

        if (map.Priority.TryGetValue(priority, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        stats.MissingMapValues.Add($"priority: '{priority}'");
        logger.Warning($"Issue {issue.Id}: priority '{priority}' отсутствует в map.json Ч используетс€ {MapDefaults.DefaultPriority}.");
        return MapDefaults.DefaultPriority;
    }

    private static string ResolveReporter(BitbucketIssue issue, MapFile map, AppLogger logger, GenerationStats stats)
    {
        if (TryResolveUser(issue.Reporter, map, out var jiraUser, out var displayName))
        {
            return jiraUser;
        }

        stats.UnmappedUsers.Add(displayName);
        stats.EmptyRequiredFields++;
        logger.Warning($"Issue {issue.Id}: reporter '{displayName}' не сопоставлен Ч поле Reporter пустое.");
        return string.Empty;
    }

    private static string ResolveAssignee(BitbucketIssue issue, MapFile map)
    {
        // Assignee может отсутствовать или быть не сопоставлен Ч поле просто остаЄтс€ пустым.
        return TryResolveUser(issue.Assignee, map, out var jiraUser, out _) ? jiraUser : string.Empty;
    }

    private static string ResolveUpdated(BitbucketIssue issue) =>
        issue.UpdatedOn is not null ? FormatDate(issue.UpdatedOn) : FormatDate(issue.CreatedOn);

    private static string ResolveFixVersion(BitbucketIssue issue, MapFile map, AppLogger logger, GenerationStats stats)
    {
        var version = issue.Version?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        if (map.Version.TryGetValue(version, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        stats.MissingMapValues.Add($"version: '{version}'");
        logger.Warning($"Issue {issue.Id}: version '{version}' отсутствует в map.json Ч поле Fix Version/s пустое.");
        return string.Empty;
    }

    private static string ResolveMilestone(BitbucketIssue issue, MapFile map, AppLogger logger, GenerationStats stats)
    {
        var milestone = issue.Milestone?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(milestone))
        {
            return string.Empty;
        }

        if (map.Milestone.TryGetValue(milestone, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        stats.MissingMapValues.Add($"milestone: '{milestone}'");
        logger.Warning($"Issue {issue.Id}: milestone '{milestone}' отсутствует в map.json Ч поле Bitbucket Milestone пустое.");
        return string.Empty;
    }

    private static List<string> BuildEvents(
        BitbucketIssue issue,
        IEnumerable<BitbucketComment> comments,
        IEnumerable<BitbucketLog> logs,
        MapFile map,
        GenerationStats stats)
    {
        var events = new List<(DateTimeOffset Order, string Text)>();

        foreach (var comment in comments)
        {
            var text = comment.Content?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            stats.Comments++;
            events.Add((comment.CreatedOn ?? DateTimeOffset.MinValue, FormatComment(comment, text!, map)));
        }

        foreach (var log in logs)
        {
            stats.History++;
            events.Add((log.CreatedOn ?? DateTimeOffset.MinValue, FormatHistory(log, map)));
        }

        return events
            .OrderBy(e => e.Order)
            .Select(e => e.Text)
            .ToList();
    }

    private static string FormatComment(BitbucketComment comment, string text, MapFile map)
    {
        var date = FormatDate(comment.CreatedOn);
        if (TryResolveUser(comment.User, map, out var jiraUser, out _))
        {
            return $"{date};{jiraUser};{text}";
        }

        var author = ResolveDisplayName(comment.User);
        return $"{date};;[Original Bitbucket author: {author}]\r\n\r\n{text}";
    }

    private static string FormatHistory(BitbucketLog log, MapFile map)
    {
        var date = FormatDate(log.CreatedOn);
        var field = log.Field ?? string.Empty;
        var from = log.ChangedFrom ?? string.Empty;
        var to = log.ChangedTo ?? string.Empty;

        if (TryResolveUser(log.User, map, out var jiraUser, out _))
        {
            return $"{date};{jiraUser};[Bitbucket history] {field}: {from} \u2192 {to}";
        }

        var author = ResolveDisplayName(log.User);
        return $"{date};;[Bitbucket history]\r\nOriginal Bitbucket author: {author}\r\n{field}: {from} \u2192 {to}";
    }

    private static bool TryResolveUser(BitbucketUser? user, MapFile map, out string jiraUser, out string displayName)
    {
        displayName = ResolveDisplayName(user);
        jiraUser = string.Empty;

        var key = user?.Key;
        if (string.IsNullOrWhiteSpace(key) || !map.Users.TryGetValue(key, out var mapping))
        {
            return false;
        }

        jiraUser = mapping.ResolveJiraUser();
        return !string.IsNullOrWhiteSpace(jiraUser);
    }

    private static string ResolveDisplayName(BitbucketUser? user) =>
        user?.DisplayName ?? user?.Key ?? "unknown";

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    private static void PadRow(List<string> row, int totalColumns)
    {
        while (row.Count < totalColumns)
        {
            row.Add(string.Empty);
        }
    }

    private static void WriteSummary(AppLogger logger, GenerationStats stats)
    {
        logger.Raw("----- »тоги генерации import.csv -----");
        logger.Info($"ќбработано issues: {stats.ProcessedIssues}");
        logger.Info($"Ёкспортировано строк: {stats.ExportedRows}");
        logger.Info($" омментариев: {stats.Comments}");
        logger.Info($"«аписей истории: {stats.History}");
        logger.Info($"ѕустых об€зательных полей: {stats.EmptyRequiredFields}");

        if (stats.SkippedIssues.Count > 0)
        {
            logger.Info($"ѕропущено задач: {stats.SkippedIssues.Count} (id: {string.Join(", ", stats.SkippedIssues)})");
        }

        if (stats.MissingMapValues.Count > 0)
        {
            logger.Info($"«начени€, отсутствующие в map.json ({stats.MissingMapValues.Count}):");
            foreach (var value in stats.MissingMapValues.OrderBy(v => v, StringComparer.Ordinal))
            {
                logger.Info($"  {value}");
            }
        }

        if (stats.UnmappedUsers.Count > 0)
        {
            logger.Info($"ѕользователи без jiraAccountId/jiraEmail ({stats.UnmappedUsers.Count}):");
            foreach (var user in stats.UnmappedUsers.OrderBy(v => v, StringComparer.Ordinal))
            {
                logger.Info($"  {user}");
            }
        }
    }

    private sealed class GenerationStats
    {
        public int ProcessedIssues { get; set; }

        public int ExportedRows { get; set; }

        public int Comments { get; set; }

        public int History { get; set; }

        public int EmptyRequiredFields { get; set; }

        public List<int> SkippedIssues { get; } = new();

        public HashSet<string> MissingMapValues { get; } = new(StringComparer.Ordinal);

        public HashSet<string> UnmappedUsers { get; } = new(StringComparer.Ordinal);
    }
}
