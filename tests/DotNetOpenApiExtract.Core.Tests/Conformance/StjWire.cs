using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetOpenApiExtract.Core.Tests.Conformance;

/// <summary>
/// What actually goes over the wire: serializes fixture objects with the real System.Text.Json and
/// the options of a serialization context, so schemas are checked against real output rather than
/// against what the extractor believes.
/// </summary>
internal static class StjWire
{
    /// <summary>
    /// Options of the MVC context (<c>AddControllers().AddJsonOptions</c>): ASP.NET Core starts from
    /// <see cref="JsonSerializerDefaults.Web"/>; <paramref name="configure"/> applies the fixture's settings.
    /// </summary>
    public static JsonSerializerOptions Mvc(Action<JsonSerializerOptions>? configure = null) => Create(configure);

    /// <summary>
    /// Options of the HTTP context (<c>ConfigureHttpJsonOptions</c>, used by <c>IResult</c> and
    /// server-sent events): also <see cref="JsonSerializerDefaults.Web"/> plus <paramref name="configure"/>.
    /// </summary>
    public static JsonSerializerOptions Http(Action<JsonSerializerOptions>? configure = null) => Create(configure);

    /// <summary>Serializes <paramref name="value"/> as <typeparamref name="T"/> and parses the JSON text.</summary>
    public static JsonNode? Serialize<T>(T value, JsonSerializerOptions options) =>
        JsonNode.Parse(JsonSerializer.Serialize(value, options));

    /// <summary>Serializes <paramref name="value"/> as <paramref name="declaredType"/> (polymorphism follows the declared type).</summary>
    public static JsonNode? Serialize(object? value, Type declaredType, JsonSerializerOptions options) =>
        JsonNode.Parse(JsonSerializer.Serialize(value, declaredType, options));

    /// <summary>
    /// Whether System.Text.Json accepts <paramref name="json"/> as <paramref name="type"/> with
    /// <paramref name="options"/>; for inputs the serializer reads but never writes.
    /// </summary>
    public static bool Accepts(string json, Type type, JsonSerializerOptions options)
    {
        try
        {
            JsonSerializer.Deserialize(json, type, options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static JsonSerializerOptions Create(Action<JsonSerializerOptions>? configure)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        configure?.Invoke(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
