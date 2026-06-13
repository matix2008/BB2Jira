using System.Text.Json;
using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Entry of the milestones[] reference and the issues[].milestone value.
/// </summary>
[JsonConverter(typeof(BitbucketMilestoneConverter))]
public sealed class BitbucketMilestone
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Supports reading a milestone as an object { id, name }, a plain string name, or null.
/// In real Bitbucket exports issues[].milestone is a string while milestones[] uses objects.
/// </summary>
public sealed class BitbucketMilestoneConverter : JsonConverter<BitbucketMilestone?>
{
    public override BitbucketMilestone? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return new BitbucketMilestone { Name = reader.GetString() };

            case JsonTokenType.StartObject:
            {
                var milestone = new BitbucketMilestone();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return milestone;
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
                            milestone.Id = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : null;
                            break;
                        case "name":
                            milestone.Name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                return milestone;
            }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, BitbucketMilestone? value, JsonSerializerOptions options)
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
