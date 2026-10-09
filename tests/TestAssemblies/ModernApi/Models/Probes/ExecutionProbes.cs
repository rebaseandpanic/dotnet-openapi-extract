using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernApi.Models.Probes;

// Probes for "user code is never executed": each one leaves a marker file when its code runs.
// Extraction reads metadata only, so building a document must leave no marker. Do not use these
// types anywhere else (no serialization, no instantiation in tests).

/// <summary>Writes the marker files of the execution probes.</summary>
public static class ProbeMarker
{
    /// <summary>Directory under the temp path that holds the marker files.</summary>
    public const string DirectoryName = "openapi-extract-execution-probe";

    /// <summary>Creates the marker named <paramref name="probe"/>.</summary>
    public static void Touch(string probe)
    {
        var directory = Path.Combine(Path.GetTempPath(), DirectoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, probe), probe);
    }
}

/// <summary>Converter whose constructor leaves a marker.</summary>
public sealed class ExecutionProbeConverter : JsonConverter<string>
{
    /// <summary>Leaves the converter marker.</summary>
    public ExecutionProbeConverter() => ProbeMarker.Touch(nameof(ExecutionProbeConverter));

    /// <inheritdoc />
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() ?? string.Empty;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>Attribute whose constructor leaves a marker.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ExecutionProbeAttribute : Attribute
{
    /// <summary>Leaves the attribute marker.</summary>
    public ExecutionProbeAttribute() => ProbeMarker.Touch(nameof(ExecutionProbeAttribute));
}

/// <summary>DTO whose static constructor leaves a marker; its properties carry the other probes.</summary>
public sealed class ExecutionProbeDto
{
    static ExecutionProbeDto() => ProbeMarker.Touch(nameof(ExecutionProbeDto));

    /// <summary>Property carrying the probe attribute.</summary>
    [ExecutionProbe]
    public string Name { get; set; } = string.Empty;

    /// <summary>Property carrying the probe converter.</summary>
    [JsonConverter(typeof(ExecutionProbeConverter))]
    public string Value { get; set; } = string.Empty;
}
