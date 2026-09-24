using System.Text.Json;
using System.Text.Json.Serialization;

namespace GK2Trainer.App.Core;

/// <summary>
/// The plugin writes JSON with Newtonsoft, the app reads with System.Text.Json.
/// Lua/Newtonsoft numbers may arrive as strings, empty collections as objects and
/// booleans as 0/1, so every converter here is deliberately forgiving.
/// </summary>
public sealed class LenientNumberConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => double.TryParse(reader.GetString(), out var parsed) ? parsed : null,
            JsonTokenType.True => 1,
            JsonTokenType.False => 0,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

public sealed class LenientIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.TryGetInt32(out var value) ? value : (int)reader.GetDouble(),
            JsonTokenType.String => int.TryParse(reader.GetString(), out var parsed) ? parsed : null,
            JsonTokenType.True => 1,
            JsonTokenType.False => 0,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

public sealed class LenientBoolConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.GetDouble() != 0,
            JsonTokenType.String => bool.TryParse(reader.GetString(), out var parsed) ? parsed : null,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteBooleanValue(value.Value);
        else writer.WriteNullValue();
    }
}

/// <summary>Strings may hold numbers or objects; coerce instead of throwing.</summary>
public sealed class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDouble().ToString("0.###"),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.StartObject or JsonTokenType.StartArray =>
                JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>An empty JSON object is accepted where a list is expected.</summary>
public sealed class LenientListConverter<T> : JsonConverter<List<T>>
{
    public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return new List<T>();

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var depth = 1;
            while (depth > 0 && reader.Read())
            {
                depth += reader.TokenType switch
                {
                    JsonTokenType.StartObject => 1,
                    JsonTokenType.EndObject => -1,
                    _ => 0,
                };
            }
            return new List<T>();
        }

        var list = new List<T>();
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var item = JsonSerializer.Deserialize<T>(ref reader, options);
                if (item != null) list.Add(item);
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}

/// <summary>Numbers may also arrive as strings inside dictionaries.</summary>
public sealed class LenientNumberDictionaryConverter : JsonConverter<Dictionary<string, double?>>
{
    public override Dictionary<string, double?> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var result = new Dictionary<string, double?>();
        if (reader.TokenType != JsonTokenType.StartObject) return result;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var key = reader.GetString() ?? "";
            reader.Read();

            result[key] = reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String => double.TryParse(reader.GetString(), out var parsed) ? parsed : null,
                JsonTokenType.True => 1,
                JsonTokenType.False => 0,
                _ => null,
            };
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, double?> value,
        JsonSerializerOptions options) => throw new NotSupportedException();
}
