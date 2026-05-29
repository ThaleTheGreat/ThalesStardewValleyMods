using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThaleTheGreat.AssetPatcher;

public sealed class StringOrStringArrayConverter : JsonConverter<string[]?>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            string? value = reader.GetString();
            return string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
        }

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected a string or string array.");

        List<string> values = new();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return values.ToArray();

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Expected a string value in the array.");

            string? value = reader.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }

        throw new JsonException("Unterminated string array.");
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (string item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
