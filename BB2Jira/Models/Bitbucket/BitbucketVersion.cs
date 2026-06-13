using System.Text.Json;
using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Entry of the versions[] reference and the issues[].version value.
/// </summary>
[JsonConverter(typeof(BitbucketVersionConverter))]
public sealed class BitbucketVersion
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Supports reading a version as an object { id, name }, a plain string name, or null.
/// In real Bitbucket exports issues[].version is a string while versions[] uses objects.
/// </summary>
public sealed class BitbucketVersionConverter : JsonConverter<BitbucketVersion?>
{
    public override BitbucketVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return new BitbucketVersion { Name = reader.GetString() };

            case JsonTokenType.StartObject:
            {
                var version = new BitbucketVersion();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return version;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    var propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName)
                    {
                        case "id":
                            version.Id = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : null;
                            break;
                        case "name":
                            version.Name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                return version;
            }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, BitbucketVersion? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (value.Id is not null)
        {
            writer.WriteNumber("id", value.Id.Value);
        }

        writer.WriteString("name", value.Name);
        writer.WriteEndObject();
    }
}
