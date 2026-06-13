using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BB2Jira.Services;

/// <summary>Общие настройки сериализации/десериализации JSON.</summary>
public static class JsonDefaults
{
    /// <summary>Настройки для чтения db-2.0.json и map.json.</summary>
    public static JsonSerializerOptions Read { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Настройки для записи map.json (с отступами, без экранирования кириллицы).</summary>
    public static JsonSerializerOptions Write { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
