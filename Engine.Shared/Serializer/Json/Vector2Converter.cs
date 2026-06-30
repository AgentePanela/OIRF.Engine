using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace Engine.Shared.Serializer.Json;

public class Vector2Converter : JsonConverter
{
    public override bool CanConvert(Type objectType) 
        => objectType == typeof(Vector2) || objectType == typeof(Vector2?);

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var obj = JObject.Load(reader);

        var vector = new Vector2(
            obj["x"]!.Value<float>(),
            obj["y"]!.Value<float>()
        );

        if (objectType == typeof(Vector2?))
            return (Vector2?)vector;

        return vector;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var vector = (Vector2)value;

        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(vector.X);
        writer.WritePropertyName("y");
        writer.WriteValue(vector.Y);
        writer.WriteEndObject();
    }
}
