using System.Text.Json;
using BB2Jira.Models.Mapping;

namespace BB2Jira.Services;

/// <summary>Загрузка существующего файла маппинга map.json.</summary>
public static class MapLoader
{
    /// <summary>
    /// Читает map.json. Если файл отсутствует, возвращает пустую модель
    /// (это допустимо при первой генерации).
    /// </summary>
    public static MapFile Load(string path)
    {
        if (!File.Exists(path))
        {
            return new MapFile();
        }

        var json = File.ReadAllText(path);
        return Parse(json);
    }

    /// <summary>Десериализует содержимое map.json из строки.</summary>
    public static MapFile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MapFile();
        }

        try
        {
            var map = JsonSerializer.Deserialize<MapFile>(json, JsonDefaults.Read);
            return map ?? new MapFile();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Не удалось разобрать map.json: {ex.Message}", ex);
        }
    }
}
