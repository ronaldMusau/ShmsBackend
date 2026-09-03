using System;

namespace ShmsBackend.Api.Utilities;

/// <summary>
/// Forces every DateTime written to JSON to carry an explicit UTC ("Z") marker.
/// The dataset is uniformly UTC-valued, but EF Core materializes SQL Server datetime2
/// columns as DateTimeKind.Unspecified, which System.Text.Json otherwise emits with no
/// timezone marker — making clients parse it as browser-local time. Registered globally
/// via AddJsonOptions, so it applies to every DateTime / DateTime? field in the API.
/// </summary>
public class UtcDateTimeConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
{
    public override DateTime Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime value, System.Text.Json.JsonSerializerOptions options)
    {
        var utcValue = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        writer.WriteStringValue(utcValue);
    }
}
