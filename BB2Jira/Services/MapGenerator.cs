using System.Text;
using System.Text.Json;
using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;
using Microsoft.Extensions.Logging;

namespace BB2Jira.Services;

/// <summary>Генерация и слияние файла маппинга map.json (ключ -m).</summary>
public static class MapGenerator
{
    /// <summary>
    /// Формирует map.json на основе экспорта Bitbucket и сохраняет его по указанному пути.
    /// Если файл уже существует, ручные правки сохраняются (новые значения добавляются,
    /// существующие не затираются и не удаляются).
    /// </summary>
    public static void Generate(BitbucketExport export, string outputPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(logger);

        var existing = MapLoader.Load(outputPath);
        if (File.Exists(outputPath))
        {
            logger.LogInformation("Найден существующий map.json: ручные правки будут сохранены.");
        }

        var map = Build(export, existing);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(map, JsonDefaults.Write);
        File.WriteAllText(outputPath, json, new UTF8Encoding(false));

        logger.LogInformation(
            "map.json сохранён: kind={Kind}, status={Status}, priority={Priority}, users={Users}, milestone={Milestone}, version={Version}",
            map.Kind.Count, map.Status.Count, map.Priority.Count, map.Users.Count, map.Milestone.Count, map.Version.Count);
    }

    /// <summary>
    /// Строит модель map.json, объединяя дефолтные значения, данные экспорта и существующий маппинг.
    /// </summary>
    public static MapFile Build(BitbucketExport export, MapFile existing)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(existing);

        var map = new MapFile();

        BuildKind(export, existing, map);
        BuildStatus(export, existing, map);
        BuildPriority(export, existing, map);
        BuildUsers(export, existing, map);
        BuildMilestone(export, existing, map);
        BuildVersion(export, existing, map);

        return map;
    }

    private static void BuildKind(BitbucketExport export, MapFile existing, MapFile map)
    {
        foreach (var kind in DistinctNonEmpty(export.Issues.Select(i => i.Kind)))
        {
            map.Kind[kind] = ResolveValue(existing.Kind, kind, MapDefaults.Kind, MapDefaults.DefaultKind);
        }

        // Сохраняем значения из существующего файла, даже если они больше не встречаются.
        MergeExisting(existing.Kind, map.Kind);
    }

    private static void BuildStatus(BitbucketExport export, MapFile existing, MapFile map)
    {
        foreach (var status in DistinctNonEmpty(export.Issues.Select(i => i.Status)))
        {
            map.Status[status] = ResolveValue(existing.Status, status, MapDefaults.Status, MapDefaults.DefaultStatus);
        }

        MergeExisting(existing.Status, map.Status);
    }

    private static void BuildPriority(BitbucketExport export, MapFile existing, MapFile map)
    {
        foreach (var priority in DistinctNonEmpty(export.Issues.Select(i => i.Priority)))
        {
            map.Priority[priority] = ResolveValue(existing.Priority, priority, MapDefaults.Priority, MapDefaults.DefaultPriority);
        }

        MergeExisting(existing.Priority, map.Priority);
    }

    private static void BuildUsers(BitbucketExport export, MapFile existing, MapFile map)
    {
        var users = new List<BitbucketUser?>();
        users.AddRange(export.Issues.Select(i => i.Reporter));
        users.AddRange(export.Issues.Select(i => i.Assignee));
        users.AddRange(export.Comments.Select(c => c.User));
        users.AddRange(export.Logs.Select(l => l.User));

        foreach (var user in users)
        {
            var key = user?.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var displayName = user!.DisplayName ?? key;

            if (existing.Users.TryGetValue(key, out var existingUser))
            {
                // Сохраняем ручные правки; обновляем только производное имя из Bitbucket.
                existingUser.BitbucketDisplayName = displayName;
                if (string.IsNullOrWhiteSpace(existingUser.JiraDisplayName))
                {
                    existingUser.JiraDisplayName = displayName;
                }

                map.Users[key] = existingUser;
                continue;
            }

            if (map.Users.ContainsKey(key))
            {
                continue;
            }

            map.Users[key] = new UserMapping
            {
                BitbucketDisplayName = displayName,
                JiraAccountId = string.Empty,
                JiraEmail = string.Empty,
                JiraDisplayName = displayName,
            };
        }

        // Не удаляем пользователей, добавленных вручную в существующий файл.
        foreach (var pair in existing.Users)
        {
            map.Users.TryAdd(pair.Key, pair.Value);
        }
    }

    private static void BuildMilestone(BitbucketExport export, MapFile existing, MapFile map)
    {
        var names = export.Milestones.Select(m => m.Name)
            .Concat(export.Issues.Select(i => i.Milestone?.Name));

        foreach (var name in DistinctNonEmpty(names))
        {
            map.Milestone[name] = existing.Milestone.TryGetValue(name, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : name;
        }

        MergeExisting(existing.Milestone, map.Milestone);
    }

    private static void BuildVersion(BitbucketExport export, MapFile existing, MapFile map)
    {
        var names = export.Versions.Select(v => v.Name)
            .Concat(export.Issues.Select(i => i.Version?.Name));

        foreach (var name in DistinctNonEmpty(names))
        {
            map.Version[name] = existing.Version.TryGetValue(name, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : name;
        }

        MergeExisting(existing.Version, map.Version);
    }

    /// <summary>
    /// Определяет значение для ключа: приоритет у существующего (ручного) маппинга,
    /// затем дефолтный словарь, затем дефолтное значение для неизвестного ключа.
    /// </summary>
    private static string ResolveValue(
        IReadOnlyDictionary<string, string> existing,
        string key,
        IReadOnlyDictionary<string, string> defaults,
        string fallback)
    {
        if (existing.TryGetValue(key, out var manual) && !string.IsNullOrWhiteSpace(manual))
        {
            return manual;
        }

        return defaults.TryGetValue(key, out var preset) ? preset : fallback;
    }

    /// <summary>Добавляет в результат значения из существующего файла, которых ещё нет (без перезаписи).</summary>
    private static void MergeExisting(
        IDictionary<string, string> existing,
        IDictionary<string, string> target)
    {
        foreach (var pair in existing)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                target.TryAdd(pair.Key, pair.Value);
            }
        }
    }

    private static IEnumerable<string> DistinctNonEmpty(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
              .Select(v => v!.Trim())
              .Distinct(StringComparer.Ordinal);
}
