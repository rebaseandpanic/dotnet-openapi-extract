using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>
/// The document metadata of OpenAPI 3.1/3.2 set through <see cref="OpenApiDocumentOptions"/>: the
/// checks that make a wrong value a configuration error, and the JSON Schema dialects a document may
/// declare.
/// </summary>
internal static class DocumentMetadata
{
    /// <summary>The OAS 3.1 base dialect (OpenAPI 3.1, "OAS dialect schema id").</summary>
    public const string Dialect31 = "https://spec.openapis.org/oas/3.1/dialect/base";

    /// <summary>
    /// The OAS 3.2 dialect: the <c>$id</c> of the published dialect schema and the default of
    /// <c>jsonSchemaDialect</c> in the OpenAPI 3.2 schema (<c>https://spec.openapis.org/oas/3.2/schema/2025-09-17</c>).
    /// </summary>
    public const string Dialect32 = "https://spec.openapis.org/oas/3.2/dialect/2025-09-17";

    /// <summary>
    /// Whether <paramref name="text"/> is a well-formed URI reference, absolute or relative: no raw
    /// spaces or other characters that must be escaped, valid percent-encodings.
    /// </summary>
    public static bool IsUriReference(string text) =>
        !string.IsNullOrWhiteSpace(text) && Uri.IsWellFormedUriString(text, UriKind.RelativeOrAbsolute);

    /// <summary>
    /// Checks the options that need no assembly: server names, <c>$self</c>, the dialect for the
    /// target version, and a license identifier given together with a license URL.
    /// </summary>
    public static void Validate(OpenApiDocumentOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.LicenseIdentifier) && !string.IsNullOrWhiteSpace(options.LicenseUrl))
            throw new OpenApiConfigurationException(
                "LicenseIdentifier and LicenseUrl are mutually exclusive (OpenAPI license object): set only one of them.");

        ValidateServerNames(options.Servers, options.ServerNames);

        // $self: a well-formed URI reference (absolute or relative), without a fragment.
        if (options.SelfUrl != null
            && (options.SelfUrl.Contains('#', StringComparison.Ordinal) || !IsUriReference(options.SelfUrl)))
            throw new OpenApiConfigurationException(
                $"SelfUrl '{options.SelfUrl}' must be a URI reference without a fragment ($self).");

        if (options.JsonSchemaDialect != null)
            DialectFor(options.OpenApiVersion, options.JsonSchemaDialect);
    }

    /// <summary>
    /// The license after its sources are merged: an identifier and a URL together, or either of them
    /// without a name, is a configuration error — the license is never dropped silently.
    /// </summary>
    public static void ValidateLicense(string? name, string? url, string? identifier)
    {
        if (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(url))
            throw new OpenApiConfigurationException(
                "The license has both an identifier and a URL, which are mutually exclusive: keep one of them.");

        if (string.IsNullOrWhiteSpace(name) && (!string.IsNullOrWhiteSpace(url) || !string.IsNullOrWhiteSpace(identifier)))
            throw new OpenApiConfigurationException(
                "The license has a URL or an identifier but no name, which OpenAPI requires (set LicenseName / --license-name).");
    }

    /// <summary>
    /// Server names bind to <c>Servers</c> by position: none at all, or exactly one per server, each
    /// non-empty and unique.
    /// </summary>
    private static void ValidateServerNames(IReadOnlyList<string>? servers, IReadOnlyList<string>? names)
    {
        if (names is not { Count: > 0 })
            return;

        var serverCount = servers?.Count ?? 0;
        if (names.Count != serverCount)
            throw new OpenApiConfigurationException(
                $"{names.Count} server name(s) for {serverCount} server(s): give none, or exactly one name per server, in the same order.");

        if (names.Any(string.IsNullOrWhiteSpace))
            throw new OpenApiConfigurationException("A server name is empty.");

        var duplicate = names.GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new OpenApiConfigurationException($"The server name '{duplicate.Key}' is given more than once.");
    }

    /// <summary>
    /// The dialect a document of <paramref name="target"/> may declare: the base dialect of its version;
    /// for 3.0, which has no such field, the 3.1 and 3.2 dialects are accepted and the field is omitted.
    /// Any other value is a configuration error.
    /// </summary>
    public static Uri DialectFor(OpenApiSpecVersion target, string dialect)
    {
        var accepted = target switch
        {
            OpenApiSpecVersion.OpenApi3_1 => [Dialect31],
            OpenApiSpecVersion.OpenApi3_2 => [Dialect32],
            _ => new[] { Dialect31, Dialect32 },
        };
        if (!accepted.Contains(dialect, StringComparer.Ordinal))
            throw new OpenApiConfigurationException(
                $"JSON Schema dialect '{dialect}' is not supported for OpenAPI {TargetVersion.Describe(target)}: " +
                $"use {string.Join(" or ", accepted)}.");
        return new Uri(dialect);
    }
}
