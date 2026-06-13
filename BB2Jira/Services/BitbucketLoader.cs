using System.Text.Json;
using BB2Jira.Models.Bitbucket;

namespace BB2Jira.Services;

/// <summary>Загрузка и десериализация файла экспорта Bitbucket (db-2.0.json).</summary>
public static class BitbucketLoader
{
    /// <summary>
    /// Читает db-2.0.json по указанному пути.
    /// </summary>
    /// <exception cref="FileNotFoundException">Файл не найден.</exception>
    /// <exception cref="InvalidDataException">Файл не удалось разобрать как JSON.</exception>
    public static BitbucketExport Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Файл экспорта Bitbucket не найден: {path}", path);
        }

        var json = File.ReadAllText(path);
        return Parse(json);
    }

    /// <summary>Десериализует содержимое db-2.0.json из строки.</summary>
    public static BitbucketExport Parse(string json)
    {
        try
        {
            var export = JsonSerializer.Deserialize<BitbucketExport>(json, JsonDefaults.Read);
            return export ?? new BitbucketExport();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Не удалось разобрать db-2.0.json: {ex.Message}", ex);
        }
    }
}
