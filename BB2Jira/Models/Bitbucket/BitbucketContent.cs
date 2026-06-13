using System.Text.Json;
using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Bitbucket рендерит текстовые поля (content) как объект { raw, markup, html }.
/// В некоторых экспортах поле может быть простой строкой или null, поэтому
/// используется кастомный конвертер <see cref="BitbucketContentConverter"/>.
/// </summary>
[JsonConverter(typeof(BitbucketContentConverter))]
public sealed class BitbucketContent
{
    public string? Raw { get; set; }

    public string? Markup { get; set; }

    public string? Html { get; set; }

    /// <summary>Текстовое значение поля (приоритет: raw, затем html).</summary>
    public string Text => Raw ?? Html ?? string.Empty;

    public override string ToString() => Text;
}

/// <summary>
/// Поддерживает чтение content как объекта { raw, markup, html }, простой строки или null.
/// </summary>
public sealed class BitbucketContentConverter : JsonConverter<BitbucketContent?>
{
    public override BitbucketContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return new BitbucketContent { Raw = reader.GetString() };

            case JsonTokenType.StartObject:
            {
                var content = new BitbucketContent();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return content;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    var propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName)
                    {
                        case "raw":
                            content.Raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            break;
                        case "markup":
                            content.Markup = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            break;
                        case "html":
                            content.Html = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                return content;
            }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, BitbucketContent? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("raw", value.Raw);
        writer.WriteString("markup", value.Markup);
        writer.WriteString("html", value.Html);
        writer.WriteEndObject();
    }
}
