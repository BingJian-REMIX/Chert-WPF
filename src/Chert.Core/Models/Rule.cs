using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chert.Core.Models;

/// <summary>条件规则（来自 arguments / libraries.rules）。</summary>
public class Rule
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "allow"; // allow | disallow

    [JsonPropertyName("os")]
    public OsRule? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public class OsRule
{
    [JsonPropertyName("name")]
    public string? Name { get; set; } // windows | linux | osx

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// 将 arguments 元素解析为 ArgumentItem。
/// 字符串 -> Values=[str], Rules=[]；对象 -> 解析 rules 与 value（字符串或字符串数组）。
/// 使用 System.Text.Json 手动读取以支持多态（string | object）。
/// </summary>
[JsonConverter(typeof(ArgumentItemConverter))]
public class ArgumentItem
{
    public List<Rule> Rules { get; set; } = new();
    public List<string> Values { get; set; } = new();
}

public class ArgumentItemConverter : JsonConverter<ArgumentItem>
{
    public override ArgumentItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var item = new ArgumentItem();

        if (reader.TokenType == JsonTokenType.String)
        {
            item.Values.Add(reader.GetString()!);
            return item;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return item;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return item;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var prop = reader.GetString();
            if (!reader.Read()) break;

            switch (prop)
            {
                case "rules":
                    if (reader.TokenType == JsonTokenType.StartArray)
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            if (reader.TokenType == JsonTokenType.StartObject)
                                item.Rules.Add(JsonSerializer.Deserialize<Rule>(ref reader, options)!);
                    break;

                case "value":
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        item.Values.Add(reader.GetString()!);
                    }
                    else if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            if (reader.TokenType == JsonTokenType.String)
                                item.Values.Add(reader.GetString()!);
                    }
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return item;
    }

    public override void Write(Utf8JsonWriter writer, ArgumentItem value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Rules.Count > 0)
        {
            writer.WritePropertyName("rules");
            JsonSerializer.Serialize(writer, value.Rules, options);
        }
        writer.WritePropertyName("value");
        if (value.Values.Count == 1)
            writer.WriteStringValue(value.Values[0]);
        else
            JsonSerializer.Serialize(writer, value.Values, options);
        writer.WriteEndObject();
    }
}
