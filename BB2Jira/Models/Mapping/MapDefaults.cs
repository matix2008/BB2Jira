namespace BB2Jira.Models.Mapping;

/// <summary>
/// Default mapping values and fallback values for unknown keys
/// according to the map.json generation rules.
/// </summary>
public static class MapDefaults
{
    public const string DefaultKind = "Task";
    public const string DefaultStatus = "Backlog";
    public const string DefaultPriority = "Medium";

    public static IReadOnlyDictionary<string, string> Kind { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bug"] = "Bug",
        ["task"] = "Task",
        ["enhancement"] = "Task",
        ["proposal"] = "Task",
    };

    public static IReadOnlyDictionary<string, string> Status { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["new"] = "Backlog",
        ["open"] = "In Development",
        ["resolved"] = "Ready for Release",
        ["on hold"] = "Planned",
        ["invalid"] = "Canceled",
        ["duplicate"] = "Canceled",
        ["wontfix"] = "Canceled",
    };

    public static IReadOnlyDictionary<string, string> Priority { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["trivial"] = "Lowest",
        ["minor"] = "Low",
        ["major"] = "Medium",
        ["critical"] = "High",
        ["blocker"] = "Highest",
    };
}
