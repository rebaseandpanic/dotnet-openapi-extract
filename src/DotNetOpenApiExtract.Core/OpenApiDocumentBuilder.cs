using Microsoft.OpenApi;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Documentation;

// Alias to resolve ambiguity: our ParameterLocation vs Microsoft.OpenApi.ParameterLocation
using OurParameterLocation = DotNetOpenApiExtract.Core.Extraction.ParameterLocation;
using OpenApiParameterLocation = Microsoft.OpenApi.ParameterLocation;

namespace DotNetOpenApiExtract.Core;

/// <summary>
/// Configuration for building an OpenAPI document.
/// </summary>
public sealed class OpenApiDocumentOptions
{
    /// <summary>Path to the compiled assembly (.dll) to inspect.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Path to the XML documentation file. When <see langword="null"/>,
    /// the path is auto-detected by replacing the assembly extension with ".xml".
    /// </summary>
    public string? XmlPath { get; init; }

    /// <summary>Title of the API (used in OpenAPI Info).</summary>
    public string Title { get; init; } = "API";

    /// <summary>Version of the API (used in OpenAPI Info).</summary>
    public string Version { get; init; } = "v1";

    /// <summary>Optional description for the API.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// When <see langword="true"/> (the default), property names are emitted
    /// in camelCase to match the ASP.NET Core default JSON serializer behavior.
    /// </summary>
    public bool CamelCasePropertyNames { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, enum values are rendered as strings rather
    /// than integers in the generated schemas.
    /// </summary>
    public bool EnumAsString { get; init; } = false;

    /// <summary>
    /// Optional list of path prefixes to exclude from the generated document.
    /// Any path whose string representation starts with one of these prefixes
    /// (case-insensitive) is removed from the final <see cref="OpenApiDocument.Paths"/>.
    /// </summary>
    public IReadOnlyList<string>? ExcludePathPrefixes { get; init; }
}

/// <summary>
/// Builds a complete <see cref="OpenApiDocument"/> by orchestrating discovery,
/// extraction, schema generation, and documentation resolution over a compiled
/// .NET assembly loaded via <c>MetadataLoadContext</c>.
/// </summary>
/// <remarks>
/// The entry point is the static <see cref="Build"/> method. The assembly is loaded
/// in a temporary <see cref="AssemblyLoader"/> that is disposed upon return, so no
/// code from the target assembly is ever executed.
/// </remarks>
public sealed class OpenApiDocumentBuilder
{
    /// <summary>
    /// Builds a complete OpenAPI document from the assembly specified in
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="options">
    /// Configuration that controls assembly path, XML documentation, title/version,
    /// and schema options.
    /// </param>
    /// <returns>
    /// A fully-populated <see cref="OpenApiDocument"/> containing paths, operations,
    /// parameters, request bodies, responses, component schemas, and tags.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.IO.FileNotFoundException">
    /// Thrown when the assembly specified by <see cref="OpenApiDocumentOptions.AssemblyPath"/>
    /// does not exist on disk.
    /// </exception>
    public static OpenApiDocument Build(OpenApiDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var loader = new AssemblyLoader(options.AssemblyPath);

        // Resolve XML path: use the explicitly provided path, or auto-detect alongside the DLL.
        var xmlPath = options.XmlPath
            ?? Path.ChangeExtension(options.AssemblyPath, ".xml");

        var xmlParser = new XmlDocParser(xmlPath);
        var docResolver = new DocumentationResolver(xmlParser);
        var schemaGenerator = new SchemaGenerator(new SchemaOptions
        {
            CamelCasePropertyNames = options.CamelCasePropertyNames,
            EnumAsString = options.EnumAsString,
        });

        // ── Step 1: Discovery ───────────────────────────────────────────────
        var controllers = ControllerDiscovery.DiscoverControllers(loader.Assembly);
        var actions = ActionDiscovery.DiscoverActions(controllers);

        // ── Step 2: Initialise document skeleton ────────────────────────────
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = options.Title,
                Version = options.Version,
                Description = options.Description,
            },
            Paths = new OpenApiPaths(),
        };

        // ── Step 3: Build paths and operations ──────────────────────────────
        foreach (var action in actions)
        {
            var path = RouteBuilder.BuildPath(
                action.Controller.RouteTemplate,
                action.RouteTemplate,
                action.Controller.Type.Name,
                action.Name);

            // HttpMethod values in ActionInfo are already uppercase (from HttpAttributeMap).
            var httpMethod = action.HttpMethod switch
            {
                "GET"     => HttpMethod.Get,
                "POST"    => HttpMethod.Post,
                "PUT"     => HttpMethod.Put,
                "DELETE"  => HttpMethod.Delete,
                "PATCH"   => HttpMethod.Patch,
                "HEAD"    => HttpMethod.Head,
                "OPTIONS" => HttpMethod.Options,
                _         => HttpMethod.Get,
            };

            // Get or create the path item for this path.
            if (!document.Paths.TryGetValue(path, out var pathItemInterface))
            {
                var newPathItem = new OpenApiPathItem();
                document.Paths[path] = newPathItem;
                pathItemInterface = newPathItem;
            }

            // OpenApiPaths stores IOpenApiPathItem; cast to the concrete type to access Operations.
            var pathItem = pathItemInterface as OpenApiPathItem
                ?? throw new InvalidOperationException(
                    $"Path item for '{path}' is not an OpenApiPathItem.");

            try
            {
                var operation = BuildOperation(action, docResolver, schemaGenerator);
                pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
                pathItem.Operations[httpMethod] = operation;
            }
            catch (Exception ex) when (ex is FileNotFoundException
                                        or FileLoadException
                                        or TypeLoadException
                                        or BadImageFormatException)
            {
                // Skip operations whose types have unresolvable dependencies
            }
        }

        // ── Step 3b: Exclude paths by prefix ────────────────────────────────
        if (options.ExcludePathPrefixes is { Count: > 0 })
        {
            var toRemove = document.Paths.Keys
                .Where(p => options.ExcludePathPrefixes.Any(prefix =>
                    p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var key in toRemove)
                document.Paths.Remove(key);
        }

        // ── Step 4: Component schemas ────────────────────────────────────────
        // schemaGenerator.Schemas is populated as a side-effect of building operations above.
        if (schemaGenerator.Schemas.Count > 0)
        {
            document.Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
            };

            foreach (var (id, schema) in schemaGenerator.Schemas)
            {
                // Apply type-level description from documentation sources
                if (schemaGenerator.SchemaTypes.TryGetValue(id, out var schemaType))
                {
                    var typeDesc = docResolver.ResolveTypeDescription(schemaType);
                    if (!string.IsNullOrEmpty(typeDesc) && string.IsNullOrEmpty(schema.Description))
                        schema.Description = typeDesc;

                    // Apply property-level descriptions.
                    // Build a serialized-name → PropertyInfo map once per type (I9: avoids O(n²) GetProperties calls).
                    if (schema.Properties != null)
                    {
                        var propMap = new Dictionary<string, System.Reflection.PropertyInfo>(
                            StringComparer.Ordinal);
                        foreach (var p in schemaType.GetProperties(
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            var serialized = ResolvePropertyName(p, options.CamelCasePropertyNames);
                            // First property with this name wins (matches CollectProperties derived-first ordering).
                            propMap.TryAdd(serialized, p);
                        }

                        foreach (var (propName, propSchema) in schema.Properties)
                        {
                            if (propSchema is OpenApiSchema inlineProp && string.IsNullOrEmpty(inlineProp.Description))
                            {
                                if (propMap.TryGetValue(propName, out var prop))
                                {
                                    var propDoc = docResolver.ResolveProperty(schemaType, prop);
                                    if (!string.IsNullOrEmpty(propDoc.Description))
                                        inlineProp.Description = propDoc.Description;
                                }
                            }
                        }
                    }
                }

                document.Components.Schemas[id] = schema;
            }
        }

        // ── Step 5: Tags (one per controller) ───────────────────────────────
        document.Tags = new HashSet<OpenApiTag>();
        foreach (var controller in controllers)
        {
            var tagDesc = docResolver.ResolveTagDescription(controller);
            document.Tags.Add(new OpenApiTag
            {
                Name = controller.Name,
                Description = tagDesc,
            });
        }

        return document;
    }

    // =========================================================================
    // Operation builder
    // =========================================================================

    /// <summary>
    /// Constructs a single <see cref="OpenApiOperation"/> from an <see cref="ActionInfo"/>
    /// by combining extracted parameters/responses with resolved documentation.
    /// </summary>
    private static OpenApiOperation BuildOperation(
        ActionInfo action,
        DocumentationResolver docResolver,
        SchemaGenerator schemaGenerator)
    {
        var docs = docResolver.ResolveOperation(action);
        var parameters = ParameterExtractor.ExtractParameters(action);
        var responses = ResponseExtractor.ExtractResponses(action);

        var operation = new OpenApiOperation
        {
            Summary = docs.Summary,
            Description = docs.Description,
            OperationId = docs.OperationId,
            Deprecated = docs.Deprecated,
        };

        // ── Tags ─────────────────────────────────────────────────────────────
        if (docs.Tags is { Count: > 0 })
        {
            operation.Tags = new HashSet<OpenApiTagReference>(
                docs.Tags.Select(tagName => new OpenApiTagReference(tagName, null)));
        }

        // ── Parameters (path / query / header) ───────────────────────────────
        foreach (var param in parameters)
        {
            // Body and form parameters are handled separately as requestBody.
            if (param.Location is OurParameterLocation.Body or OurParameterLocation.Form)
                continue;

            var paramSchema = schemaGenerator.GenerateSchema(param.Type);

            var openApiIn = param.Location switch
            {
                OurParameterLocation.Path   => OpenApiParameterLocation.Path,
                OurParameterLocation.Query  => OpenApiParameterLocation.Query,
                OurParameterLocation.Header => OpenApiParameterLocation.Header,
                _                           => OpenApiParameterLocation.Query,
            };

            var openApiParam = new OpenApiParameter
            {
                Name = param.Name,
                In = openApiIn,
                Required = param.IsRequired,
                Schema = paramSchema,
                Description = param.Description
                    ?? docs.ParameterDescriptions.GetValueOrDefault(param.Name),
            };

            operation.Parameters ??= new List<IOpenApiParameter>();
            operation.Parameters.Add(openApiParam);
        }

        // ── Request body ─────────────────────────────────────────────────────
        var bodyParam = parameters.FirstOrDefault(
            p => p.Location == OurParameterLocation.Body);

        var formParams = parameters
            .Where(p => p.Location == OurParameterLocation.Form)
            .ToList();

        if (bodyParam != null)
        {
            var bodySchema = schemaGenerator.GenerateSchema(bodyParam.Type);
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = bodyParam.IsRequired,
                Description = bodyParam.Description,
                Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["application/json"] = new OpenApiMediaType { Schema = bodySchema },
                },
            };
        }
        else if (formParams.Count > 0)
        {
            // Build a synthetic object schema for multipart/form-data.
            var formSchema = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
            };

            foreach (var fp in formParams)
            {
                var fpSchema = schemaGenerator.GenerateSchema(fp.Type);
                formSchema.Properties![fp.Name] = fpSchema;
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["multipart/form-data"] = new OpenApiMediaType { Schema = formSchema },
                },
            };
        }

        // ── Responses ────────────────────────────────────────────────────────
        operation.Responses = new OpenApiResponses();

        foreach (var resp in responses)
        {
            var statusKey = resp.StatusCode == ResponseExtractor.DefaultStatusCode
                ? "default"
                : resp.StatusCode.ToString();

            var description = resp.Description
                ?? docs.ResponseDescriptions.GetValueOrDefault(statusKey)
                ?? GetDefaultStatusDescription(resp.StatusCode);

            var apiResponse = new OpenApiResponse { Description = description };

            if (resp.BodyType != null && resp.ContentTypes.Count > 0)
            {
                var bodySchema = schemaGenerator.GenerateSchema(resp.BodyType);
                apiResponse.Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal);

                foreach (var ct in resp.ContentTypes)
                    apiResponse.Content[ct] = new OpenApiMediaType { Schema = bodySchema };
            }

            operation.Responses[statusKey] = apiResponse;
        }

        return operation;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Returns a human-readable description for well-known HTTP status codes,
    /// used as a fallback when neither attribute-sourced nor XML-doc descriptions
    /// are available.
    /// </summary>
    private static string GetDefaultStatusDescription(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        301 => "Moved Permanently",
        302 => "Found",
        304 => "Not Modified",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        410 => "Gone",
        415 => "Unsupported Media Type",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _   => "Response",
    };

    private static string ResolvePropertyName(System.Reflection.PropertyInfo prop, bool camelCase)
    {
        // Check [JsonPropertyName] first
        var jsonPropAttr = AttributeHelper.GetAttribute(prop, AttributeHelper.Names.JsonPropertyName);
        if (jsonPropAttr != null)
        {
            var name = AttributeHelper.GetConstructorArgument<string>(jsonPropAttr, 0);
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        var propName = prop.Name;
        if (camelCase && propName.Length > 0)
            return char.ToLowerInvariant(propName[0]) + propName[1..];
        return propName;
    }
}
