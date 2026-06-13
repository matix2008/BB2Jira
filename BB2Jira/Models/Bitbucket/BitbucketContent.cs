using System.Text.Json;
using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Bitbucket renders text fields (content) as an object { raw, markup, html }.
/// In some exports the field may be a plain string or null, so a custom
/// converter <see cref="BitbucketContentConverter"/> is used.
/// </summary>
[JsonConverter(typeof(BitbucketContentConverter))]
public sealed class BitbucketContent
{
    public string? Raw { get; set; }

    public string? Markup { get; set; }

    public string? Html { get; set; }

    /// <summary>Text value of the field (priority: raw, then html).</summary>
    public string Text => Raw ?? Html ?? string.Empty;

    public override string ToString() => Text;
}

/// <summary>
/// Supports reading content as an object { raw, markup, html }, a plain string, or null.
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
