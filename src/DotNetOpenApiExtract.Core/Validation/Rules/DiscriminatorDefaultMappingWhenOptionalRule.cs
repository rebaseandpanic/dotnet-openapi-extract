using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>discriminator.default-mapping-when-optional</c>
/// OpenAPI 3.2: a <c>discriminator</c> whose property is not required must have a
/// <c>defaultMapping</c>, which names the schema of an instance without the property. Earlier
/// versions have no <c>defaultMapping</c>, so the rule applies to 3.2 only.
/// <para>
/// The property is required for the schema holding the discriminator when an instance valid against
/// that schema always has it: it is listed in the schema's own <c>required</c>; or some
/// <c>allOf</c> element requires it; or every <c>oneOf</c> alternative requires it; or every
/// <c>anyOf</c> alternative requires it — each element evaluated the same way, through <c>$ref</c>
/// to its target. A <c>oneOf</c> / <c>anyOf</c> where only some alternatives require it does not make
/// it required.
/// </para>
/// <para>
/// Every discriminator of the document is checked, wherever a schema can stand: component schemas,
/// parameters, request bodies, responses, headers, media types, path items and callbacks; path-item
/// and operation parameters (by <c>schema</c> or <c>content</c>), request bodies, responses with
/// their headers and the encoding headers of media types, in <c>paths</c>, <c>webhooks</c> and
/// callbacks; and inside a schema every subschema keyword of the model (<c>properties</c>,
/// <c>patternProperties</c>, <c>dependentSchemas</c>, <c>$defs</c>, <c>items</c>, <c>contains</c>,
/// <c>additionalProperties</c>, <c>unevaluatedProperties</c>, <c>propertyNames</c>,
/// <c>contentSchema</c>, <c>not</c>, <c>if</c> / <c>then</c> / <c>else</c>, <c>allOf</c> /
/// <c>oneOf</c> / <c>anyOf</c>) and the <c>itemSchema</c> of media types. References are not
/// followed: their targets are walked where they are declared. A schema object reached twice is
/// checked once, at the first place in that order. One violation per discriminator, at the
/// <c>discriminator</c> object.
/// </para>
/// </summary>
public sealed class DiscriminatorDefaultMappingWhenOptionalRule : IValidationRule
{
    public string Id => "discriminator.default-mapping-when-optional";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (context.OpenApiSpecVersion != OpenApiSpecVersion.OpenApi3_2)
            return [];

        var walker = new Walker(this, document, context.OpenApiSpecVersion.Value);
        walker.WalkDocument();
        return walker.Violations;
    }

    private static IEnumerable<KeyValuePair<string, T>> Ordered<T>(IDictionary<string, T>? map) =>
        map == null ? [] : map.OrderBy(kv => kv.Key, StringComparer.Ordinal);

    private static string Segment(string name) => JsonPointerHelper.EncodeSegment(name);

    /// <summary>One traversal of a document; <see cref="Violations"/> in traversal order.</summary>
    private sealed class Walker(DiscriminatorDefaultMappingWhenOptionalRule rule, OpenApiDocument document, OpenApiSpecVersion version)
    {
        private readonly HashSet<OpenApiSchema> _visited = new(ReferenceEqualityComparer.Instance);

        public List<ValidationViolation> Violations { get; } = [];

        public void WalkDocument()
        {
            var components = document.Components;
            if (components != null)
            {
                foreach (var (id, schema) in Ordered(components.Schemas))
                    Schema(schema, JsonPointerHelper.ForSchema(id));
                foreach (var (id, parameter) in Ordered(components.Parameters))
                    Parameter(parameter, $"#/components/parameters/{Segment(id)}");
                foreach (var (id, body) in Ordered(components.RequestBodies))
                    if (body is OpenApiRequestBody concrete) Content(concrete.Content, $"#/components/requestBodies/{Segment(id)}/content");
                foreach (var (id, response) in Ordered(components.Responses))
                    Response(response, $"#/components/responses/{Segment(id)}");
                foreach (var (id, header) in Ordered(components.Headers))
                    Header(header, $"#/components/headers/{Segment(id)}");
                foreach (var (id, mediaType) in Ordered(components.MediaTypes))
                    MediaType(mediaType, $"#/components/mediaTypes/{Segment(id)}");
                foreach (var (id, pathItem) in Ordered(components.PathItems))
                    PathItem(pathItem, $"components/pathItems/{Segment(id)}");
                foreach (var (id, callback) in Ordered(components.Callbacks))
                    Callback(callback, $"components/callbacks/{Segment(id)}");
            }

            foreach (var (path, pathItem) in Ordered(document.Paths))
                PathItem(pathItem, $"paths/{Segment(path)}");
            foreach (var (name, pathItem) in Ordered(document.Webhooks))
                PathItem(pathItem, $"webhooks/{Segment(name)}");
        }

        /// <summary>
        /// A path item at <paramref name="location"/> (a pointer without <c>#/</c>): its own parameters
        /// and its operations, each at the place the validated version writes it.
        /// </summary>
        private void PathItem(IOpenApiPathItem? pathItemInterface, string location)
        {
            if (pathItemInterface is not OpenApiPathItem pathItem)
                return;

            Parameters(pathItem.Parameters, $"#/{location}");

            var separator = location.LastIndexOf('/');
            var root = location[..separator];
            var name = DecodeSegment(location[(separator + 1)..]);
            foreach (var (method, operation) in (pathItem.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
                         .OrderBy(kv => kv.Key.Method, StringComparer.Ordinal))
            {
                var pointer = JsonPointerHelper.ForOperation(root, name, method.Method, version);
                Parameters(operation.Parameters, pointer);
                if (operation.RequestBody is OpenApiRequestBody body)
                    Content(body.Content, $"{pointer}/requestBody/content");
                foreach (var (status, response) in Ordered(operation.Responses))
                    Response(response, JsonPointerHelper.ForResponseOf(pointer, status));
                foreach (var (callbackName, callback) in Ordered(operation.Callbacks))
                    Callback(callback, $"{pointer[2..]}/callbacks/{Segment(callbackName)}");
            }
        }

        private void Callback(IOpenApiCallback? callbackInterface, string location)
        {
            if (callbackInterface is not OpenApiCallback { PathItems: { } pathItems })
                return;

            foreach (var (expression, pathItem) in pathItems.OrderBy(kv => kv.Key.Expression, StringComparer.Ordinal))
                PathItem(pathItem, $"{location}/{Segment(expression.Expression)}");
        }

        private void Parameters(IList<IOpenApiParameter>? parameters, string owner)
        {
            if (parameters == null)
                return;

            for (var i = 0; i < parameters.Count; i++)
                Parameter(parameters[i], $"{owner}/parameters/{i}");
        }

        private void Parameter(IOpenApiParameter? parameterInterface, string pointer)
        {
            if (parameterInterface is not OpenApiParameter parameter)
                return; // a reference: its target is walked under components
            Schema(parameter.Schema, $"{pointer}/schema");
            Content(parameter.Content, $"{pointer}/content");
        }

        private void Header(IOpenApiHeader? headerInterface, string pointer)
        {
            if (headerInterface is not OpenApiHeader header)
                return;
            Schema(header.Schema, $"{pointer}/schema");
            Content(header.Content, $"{pointer}/content");
        }

        private void Response(IOpenApiResponse? responseInterface, string pointer)
        {
            if (responseInterface is not OpenApiResponse response)
                return;
            foreach (var (name, header) in Ordered(response.Headers))
                Header(header, $"{pointer}/headers/{Segment(name)}");
            Content(response.Content, $"{pointer}/content");
        }

        private void Content(IDictionary<string, IOpenApiMediaType>? content, string pointer)
        {
            foreach (var (name, mediaType) in Ordered(content))
                MediaType(mediaType, $"{pointer}/{Segment(name)}");
        }

        private void MediaType(IOpenApiMediaType? mediaTypeInterface, string pointer)
        {
            if (mediaTypeInterface is not OpenApiMediaType mediaType)
                return;
            Schema(mediaType.Schema, $"{pointer}/schema");
            Schema(mediaType.ItemSchema, $"{pointer}/itemSchema");
            foreach (var (name, encoding) in Ordered(mediaType.Encoding))
                foreach (var (headerName, header) in Ordered(encoding?.Headers))
                    Header(header, $"{pointer}/encoding/{Segment(name)}/headers/{Segment(headerName)}");
        }

        /// <summary>Checks the discriminator of <paramref name="schemaInterface"/> and every subschema in it.</summary>
        private void Schema(IOpenApiSchema? schemaInterface, string pointer)
        {
            if (schemaInterface is not OpenApiSchema s || !_visited.Add(s))
                return;

            if (s.Discriminator is { PropertyName: { Length: > 0 } property, DefaultMapping: null }
                && !Requires(document, s, property, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance)))
            {
                Violations.Add(new ValidationViolation(
                    rule.Id,
                    rule.DefaultSeverity,
                    $"{pointer}/discriminator",
                    null,
                    $"The discriminator property '{property}' is not required, so an instance may lack it; " +
                    "OpenAPI 3.2 then requires a defaultMapping."));
            }

            SchemaMap(s.Properties, $"{pointer}/properties");
            SchemaMap(s.PatternProperties, $"{pointer}/patternProperties");
            SchemaMap(s.DependentSchemas, $"{pointer}/dependentSchemas");
            SchemaMap(s.Definitions, $"{pointer}/$defs");
            Schema(s.Items, $"{pointer}/items");
            Schema(s.Contains, $"{pointer}/contains");
            Schema(s.AdditionalProperties, $"{pointer}/additionalProperties");
            Schema(s.UnevaluatedPropertiesSchema, $"{pointer}/unevaluatedProperties");
            Schema(s.PropertyNames, $"{pointer}/propertyNames");
            Schema(s.ContentSchema, $"{pointer}/contentSchema");
            Schema(s.Not, $"{pointer}/not");
            Schema(s.If, $"{pointer}/if");
            Schema(s.Then, $"{pointer}/then");
            Schema(s.Else, $"{pointer}/else");
            SchemaList(s.AllOf, $"{pointer}/allOf");
            SchemaList(s.OneOf, $"{pointer}/oneOf");
            SchemaList(s.AnyOf, $"{pointer}/anyOf");
        }

        private void SchemaMap(IDictionary<string, IOpenApiSchema>? schemas, string pointer)
        {
            foreach (var (name, schema) in Ordered(schemas))
                Schema(schema, $"{pointer}/{Segment(name)}");
        }

        private void SchemaList(IList<IOpenApiSchema>? schemas, string pointer)
        {
            if (schemas == null)
                return;
            for (var i = 0; i < schemas.Count; i++)
                Schema(schemas[i], $"{pointer}/{i}");
        }

        private static string DecodeSegment(string segment) =>
            segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether every instance valid against <paramref name="schema"/> has <paramref name="property"/>.
    /// <paramref name="path"/> holds the schemas being evaluated; a schema reached again through a
    /// reference cycle proves nothing.
    /// </summary>
    private static bool Requires(OpenApiDocument document, IOpenApiSchema? schema, string property, HashSet<IOpenApiSchema> path)
    {
        var resolved = Resolve(document, schema);
        if (resolved == null || !path.Add(resolved))
            return false;

        try
        {
            if (resolved.Required?.Contains(property) == true)
                return true;

            if (resolved.AllOf?.Any(element => Requires(document, element, property, path)) == true)
                return true;

            if (resolved.OneOf is { Count: > 0 } oneOf && oneOf.All(element => Requires(document, element, property, path)))
                return true;

            return resolved.AnyOf is { Count: > 0 } anyOf && anyOf.All(element => Requires(document, element, property, path));
        }
        finally
        {
            path.Remove(resolved);
        }
    }

    /// <summary>The schema itself, or the target of a reference (resolved by the model or by component id).</summary>
    private static IOpenApiSchema? Resolve(OpenApiDocument document, IOpenApiSchema? schema)
    {
        if (schema is not OpenApiSchemaReference reference)
            return schema;

        if (reference.Target is { } target)
            return target is OpenApiSchemaReference ? Resolve(document, target) : target;

        var id = reference.Reference?.Id;
        return id != null && document.Components?.Schemas?.TryGetValue(id, out var component) == true
            ? Resolve(document, component)
            : null;
    }
}
