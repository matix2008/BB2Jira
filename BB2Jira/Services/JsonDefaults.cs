using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BB2Jira.Services;

/// <summary>Shared JSON serialization/deserialization settings.</summary>
public static class JsonDefaults
{
    /// <summary>Settings for reading db-2.0.json and map.json.</summary>
    public static JsonSerializerOptions Read { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Settings for writing map.json (indented, without escaping Cyrillic).</summary>
    public static JsonSerializerOptions Write { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
