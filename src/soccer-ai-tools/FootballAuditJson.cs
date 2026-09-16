using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoccerAi.Tools;

/// <summary>PostgreSQL may export an unknown nullable timestamp as -infinity.</summary>
public static class FootballAuditJson
{
    public static readonly JsonSerializerOptions Options = new() { Converters = { new NullableTimestamp() } };
    private sealed class NullableTimestamp : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null || reader.GetString() is "-infinity" or "infinity") return null;
            return reader.GetDateTimeOffset();
        }
        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is { } date) writer.WriteStringValue(date); else writer.WriteNullValue();
        }
    }
}
