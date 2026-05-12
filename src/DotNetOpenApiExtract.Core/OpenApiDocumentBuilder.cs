using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Documentation;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.CodeAnalysis;

// Alias to resolve ambiguity: our ParameterLocation vs Microsoft.OpenApi.ParameterLocation
using OurParameterLocation = DotNetOpenApiExtract.Core.Extraction.ParameterLocation;
using OpenApiParameterLocation = Microsoft.OpenApi.ParameterLocation;

namespace DotNetOpenApiExtract.Core;

/// <summary>
/// Controls how a path base detected via <c>app.UsePathBase()</c> is emitted into
/// the generated OpenAPI document.
/// </summary>
public enum PathBaseEmission
{
    /// <summary>
    /// Prepend the path base to every path key in <c>paths</c>.
    /// This is the default and the safer choice for client code-generators that
    /// ignore the <c>servers</c> array.
    /// </summary>
    PathPrefix,

    /// <summary>
    /// Add the path base as a relative server URL in <c>servers[]</c> and leave
    /// path keys unchanged.
    /// </summary>
    ServersEntry,
}

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
    /// For multiple sources, prefer <see cref="XmlPaths"/>.
    /// </summary>
    public string? XmlPath { get; init; }

    /// <summary>
    /// Ordered list of XML documentation file paths to merge. First-added source wins on key collision,
    /// so higher-priority sources (e.g. project XML, explicit user paths) should be listed first.
    /// When set, this takes precedence over <see cref="XmlPath"/>.
    /// When <see langword="null"/> or empty, the builder falls back to <see cref="XmlPath"/> and
    /// auto-detection.
    /// </summary>
    public IReadOnlyList<string>? XmlPaths { get; init; }

    /// <summary>
    /// Title of the API (used in OpenAPI Info).
    /// When <see langword="null"/> or whitespace, the builder falls back to
    /// <c>[AssemblyTitle]</c>, then <c>[AssemblyProduct]</c>, then the DLL file name.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>Version of the API (used in OpenAPI Info).</summary>
    public string Version { get; init; } = "v1";

    /// <summary>Optional description for the API.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// JSON property naming policy. Defaults to <see langword="null"/> which resolves to
    /// <see cref="JsonNamingPolicy.CamelCase"/> (the ASP.NET Core default).
    /// Overridden by Roslyn analysis if <c>ConfigureHttpJsonOptions</c> or
    /// <c>AddJsonOptions</c> is detected in the entry assembly's source.
    /// </summary>
    public JsonNamingPolicy? NamingPolicy { get; init; }

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

    /// <summary>
    /// Optional path to a specific source file (e.g. the entry point).
    /// Reserved for future use; currently not consumed by the builder.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Optional override for the source root directory (the folder containing the
    /// <c>.csproj</c>). When set, skips the automatic source-root detection.
    /// Corresponds to the <c>--source-root</c> CLI flag.
    /// </summary>
    public string? SourceRoot { get; init; }

    /// <summary>
    /// Optional name of the contact person or organisation responsible for the API.
    /// Maps to <c>info.contact.name</c> in the generated document.
    /// </summary>
    public string? ContactName { get; init; }

    /// <summary>
    /// Optional email address of the contact person or organisation.
    /// Maps to <c>info.contact.email</c> in the generated document.
    /// No format validation is performed — any string is accepted by OpenAPI.
    /// </summary>
    public string? ContactEmail { get; init; }

    /// <summary>
    /// Optional URL pointing to the contact information page.
    /// Must be a valid absolute URI; if the value cannot be parsed the URL
    /// is silently omitted and a warning is written to <c>stderr</c>.
    /// Maps to <c>info.contact.url</c> in the generated document.
    /// </summary>
    public string? ContactUrl { get; init; }

    /// <summary>
    /// Optional SPDX license name (e.g. <c>"MIT"</c>, <c>"Apache 2.0"</c>).
    /// Required when <see cref="LicenseUrl"/> is also set; if omitted but
    /// <see cref="LicenseUrl"/> is present the license block is skipped entirely.
    /// Maps to <c>info.license.name</c>.
    /// </summary>
    public string? LicenseName { get; init; }

    /// <summary>
    /// Optional URL pointing to the full license text.
    /// Must be a valid absolute URI; if the value cannot be parsed the URL
    /// is silently omitted and a warning is written to <c>stderr</c>.
    /// Ignored when <see cref="LicenseName"/> is not set.
    /// Maps to <c>info.license.url</c>.
    /// </summary>
    public string? LicenseUrl { get; init; }

    /// <summary>
    /// Optional URL to the Terms of Service for the API.
    /// Must be a valid absolute URI; if the value cannot be parsed the field
    /// is silently omitted and a warning is written to <c>stderr</c>.
    /// Maps to <c>info.termsOfService</c>.
    /// </summary>
    public string? TermsOfService { get; init; }

    /// <summary>
    /// Optional list of server base URLs to include in the <c>servers</c> array.
    /// Blank or whitespace-only entries are filtered out automatically.
    /// Maps to <c>servers[].url</c> in the generated document.
    /// </summary>
    public IReadOnlyList<string>? Servers { get; init; }

    /// <summary>
    /// Controls how a path base detected via <c>app.UsePathBase()</c> in the entry-point
    /// source is emitted into the document:
    /// <list type="bullet">
    ///   <item><see cref="PathBaseEmission.PathPrefix"/> (default) — prepend to every path key.</item>
    ///   <item><see cref="PathBaseEmission.ServersEntry"/> — add as a relative server URL.</item>
    /// </list>
    /// Has no effect when no <c>UsePathBase</c> call with a literal argument is found.
    /// </summary>
    public PathBaseEmission PathBaseEmission { get; init; } = PathBaseEmission.PathPrefix;

    /// <summary>
    /// When <see langword="true"/> (default), enum schemas automatically get a markdown-formatted
    /// <c>description</c> that combines the type-level XML summary with a bullet list of
    /// per-value descriptions. When <see langword="false"/>, the description is left as-is
    /// (type-level summary applied by the builder, no per-value bullet list).
    /// Corresponds to the <c>--no-enum-auto-description</c> CLI flag (which disables the feature).
    /// </summary>
    public bool EnumAutoDescription { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), emits a <c>x-enum-varnames</c> extension on
    /// enum schemas parallel to the <c>enum[]</c> array. When <see langword="false"/>, the
    /// extension is omitted.
    /// Corresponds to the <c>--no-enum-varnames</c> CLI flag (which disables the feature).
    /// </summary>
    public bool EnumVarnames { get; init; } = true;
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
    /// <summary>
    /// Internal build result used by both <see cref="Build"/> and <see cref="BuildWithValidation"/>.
    /// </summary>
    private sealed record BuildCoreResult(
        OpenApiDocument Document,
        IReadOnlyList<ControllerInfo> Controllers,
        IReadOnlyList<ActionInfo> Actions,
        SchemaGenerator SchemaGenerator,
        SourceAnalysisContext SourceContext);

    /// <summary>
    /// Builds a complete OpenAPI document and runs validation.
    /// Calls the same pipeline as <see cref="Build"/>, then applies <see cref="OpenApiValidator.Validate"/>.
    /// </summary>
    /// <param name="options">Build options.</param>
    /// <param name="validationContext">Validation options. CLR bindings are populated automatically.</param>
    /// <param name="validationResult">Receives the validation result after building.</param>
    /// <returns>The fully-populated <see cref="OpenApiDocument"/>.</returns>
    public static OpenApiDocument BuildWithValidation(
        OpenApiDocumentOptions options,
        ValidationContext validationContext,
        out ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validationContext);

        using var loader = new AssemblyLoader(options.AssemblyPath);
        var core = BuildCore(options, loader);

        // ── Build CLR bindings for validation ────────────────────────────────
        // ActionByOperationKey: "METHOD /path" → (Controller, Action)
        var actionByKey = new Dictionary<string, (ControllerInfo, ActionInfo)>(StringComparer.Ordinal);
        foreach (var action in core.Actions)
        {
            var path = RouteBuilder.BuildPath(
                action.Controller.RouteTemplate,
                action.RouteTemplate,
                action.Controller.Type.Name,
                action.Name);
            var key = $"{action.HttpMethod} {path}";
            actionByKey.TryAdd(key, (action.Controller, action));
        }

        // TypeBySchemaId: schema component ID → CLR Type
        var typeBySchemaId = new Dictionary<string, Type>(
            core.SchemaGenerator.SchemaTypes, StringComparer.Ordinal);

        var enrichedContext = new ValidationContext
        {
            MinDescriptionLength     = validationContext.MinDescriptionLength,
            ExcludedPathPrefixes     = validationContext.ExcludedPathPrefixes,
            SkippedRuleIds           = validationContext.SkippedRuleIds,
            EnabledRuleIds           = validationContext.EnabledRuleIds,
            SeverityOverrides        = validationContext.SeverityOverrides,
            ActionByOperationKey     = actionByKey,
            TypeBySchemaId           = typeBySchemaId,
            SourceContext            = core.SourceContext,
            OpenApiSpecVersion       = validationContext.OpenApiSpecVersion,
        };

        validationResult = Validation.OpenApiValidator.Validate(core.Document, enrichedContext);
        return core.Document;
    }

    public static OpenApiDocument Build(OpenApiDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var loader = new AssemblyLoader(options.AssemblyPath);
        return BuildCore(options, loader).Document;
    }

    // =========================================================================
    // Core build pipeline
    // =========================================================================

    private static BuildCoreResult BuildCore(OpenApiDocumentOptions options, AssemblyLoader loader)
    {
        // ── Source analysis (best-effort, never throws) ──────────────────────
        var sourceContext = TryBuildSourceAnalysisContext(options, loader);

        // ── Security extraction (Roslyn, best-effort, before operation loop) ──
        var securityResult = SecuritySchemeExtractor.Extract(sourceContext);

        // ── JSON options extraction (Roslyn, best-effort) ────────────────────
        var jsonOptions = JsonOptionsExtractor.Extract(sourceContext);

        // ── Resolve effective naming policy ───────────────────────────────────
        // Precedence: Roslyn > explicit NamingPolicy option > default CamelCase
        var effectiveNamingPolicy = jsonOptions.PropertyNamingPolicy
            ?? options.NamingPolicy
            ?? JsonNamingPolicy.CamelCase;

        // ── Resolve XML documentation paths (priority: XmlPaths > XmlPath > auto-detect > framework) ──
        var xmlPaths = BuildXmlPathList(options, loader);

        var xmlParser = XmlDocParser.FromSources(xmlPaths);
        var docResolver = new DocumentationResolver(xmlParser);
        var schemaGenerator = new SchemaGenerator(new SchemaOptions
        {
            NamingPolicy             = effectiveNamingPolicy,
            EnumAsString             = options.EnumAsString,
            DictionaryKeyPolicy      = jsonOptions.DictionaryKeyPolicy ?? effectiveNamingPolicy,
            DefaultIgnoreCondition   = jsonOptions.DefaultIgnoreCondition,
            NumberHandling           = jsonOptions.NumberHandling,
            GlobalConverterTypeNames = jsonOptions.GlobalConverterTypeNames,
            EnumAutoDescription      = options.EnumAutoDescription,
            EnumVarnames             = options.EnumVarnames,
        }, docResolver);

        // ── Step 1: Discovery ───────────────────────────────────────────────
        var controllers = ControllerDiscovery.DiscoverControllers(loader.Assembly);
        var actions = ActionDiscovery.DiscoverActions(controllers);

        // ── Step 2: Initialise document skeleton ────────────────────────────

        // Read assembly-level identity attributes (MetadataLoadContext: via CustomAttributeData,
        // never constructed/invoked).
        var asmAttrs = loader.Assembly.GetCustomAttributesData();
        static string? ReadAsmStringAttr(IList<System.Reflection.CustomAttributeData> attrs, string fullName)
            => attrs.FirstOrDefault(a => a.AttributeType.FullName == fullName)
                   ?.ConstructorArguments.ElementAtOrDefault(0).Value as string;

        var asmTitle       = ReadAsmStringAttr(asmAttrs, AttributeHelper.Names.AssemblyTitle);
        var asmDescription = ReadAsmStringAttr(asmAttrs, AttributeHelper.Names.AssemblyDescription);
        var asmProduct     = ReadAsmStringAttr(asmAttrs, AttributeHelper.Names.AssemblyProduct);
        var asmCompany     = ReadAsmStringAttr(asmAttrs, AttributeHelper.Names.AssemblyCompany);

        // Precedence chains (IsNullOrWhiteSpace rejects both null and empty/whitespace).
        var resolvedTitle =
            (!string.IsNullOrWhiteSpace(options.Title)       ? options.Title       : null)
            ?? (!string.IsNullOrWhiteSpace(asmTitle)         ? asmTitle            : null)
            ?? (!string.IsNullOrWhiteSpace(asmProduct)       ? asmProduct          : null)
            ?? loader.Assembly.GetName().Name
            ?? "API";

        var resolvedDescription =
            (!string.IsNullOrWhiteSpace(options.Description) ? options.Description : null)
            ?? (!string.IsNullOrWhiteSpace(asmDescription)   ? asmDescription      : null);

        // contact.name: option wins, then [AssemblyCompany] as last resort.
        var resolvedContactName =
            (!string.IsNullOrWhiteSpace(options.ContactName) ? options.ContactName : null)
            ?? (!string.IsNullOrWhiteSpace(asmCompany)       ? asmCompany          : null);

        var info = new OpenApiInfo
        {
            Title       = resolvedTitle,
            Version     = options.Version,
            Description = resolvedDescription,
        };

        // Contact: build the block when any of the three CLI fields is set (preserves existing
        // behaviour including ContactEmail = "" creating Contact) OR when [AssemblyCompany] resolves.
        if (!string.IsNullOrWhiteSpace(options.ContactName)
            || options.ContactEmail != null
            || options.ContactUrl != null
            || resolvedContactName != null)
        {
            Uri? contactUri = null;
            if (options.ContactUrl != null)
            {
                if (Uri.TryCreate(options.ContactUrl, UriKind.Absolute, out var parsed))
                    contactUri = parsed;
                else
                    Console.Error.WriteLine(
                        $"Warning: --contact-url '{options.ContactUrl}' is not a valid absolute URI and will be ignored.");
            }

            info.Contact = new OpenApiContact
            {
                Name  = resolvedContactName,
                Email = options.ContactEmail,
                Url   = contactUri,
            };
        }

        // License
        if (options.LicenseName != null)
        {
            Uri? licenseUri = null;
            if (options.LicenseUrl != null)
            {
                if (Uri.TryCreate(options.LicenseUrl, UriKind.Absolute, out var parsed))
                    licenseUri = parsed;
                else
                    Console.Error.WriteLine(
                        $"Warning: --license-url '{options.LicenseUrl}' is not a valid absolute URI and will be ignored.");
            }

            info.License = new OpenApiLicense
            {
                Name = options.LicenseName,
                Url  = licenseUri,
            };
        }
        // LicenseUrl without LicenseName: ignore the license block entirely (OpenAPI requires Name).

        // Terms of Service
        if (options.TermsOfService != null)
        {
            if (Uri.TryCreate(options.TermsOfService, UriKind.Absolute, out var tosUri))
                info.TermsOfService = tosUri;
            else
                Console.Error.WriteLine(
                    $"Warning: --terms-of-service '{options.TermsOfService}' is not a valid absolute URI and will be ignored.");
        }

        var document = new OpenApiDocument
        {
            Info  = info,
            Paths = new OpenApiPaths(),
        };

        // Servers
        var validServers = options.Servers?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (validServers is { Count: > 0 })
        {
            document.Servers = validServers
                .Select(url => new OpenApiServer { Url = url })
                .ToList<OpenApiServer>();
        }

        // ── Step 3: Build paths and operations ──────────────────────────────
        // Also collect (action, actionAttrs, controllerAttrs, operation) tuples for post-processing.
        var builtOperations = new List<(ActionInfo Action,
            IList<System.Reflection.CustomAttributeData> ActionAttrs,
            IList<System.Reflection.CustomAttributeData> ControllerAttrs,
            OpenApiOperation Operation)>();

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
                // Pre-fetch attribute lists once per action — avoids 8+ redundant
                // GetCustomAttributesData() parses in individual extractors.
                var actionAttrs     = action.Method.GetCustomAttributesData();
                var controllerAttrs = action.Controller.Type.GetCustomAttributesData();

                var operation = BuildOperation(action, actionAttrs, controllerAttrs, docResolver, schemaGenerator, securityResult);
                ApplyApiVersionExtension(operation, actionAttrs, controllerAttrs);
                ApplyRateLimitingAndCaching(operation, actionAttrs, controllerAttrs);
                pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
                pathItem.Operations[httpMethod] = operation;
                builtOperations.Add((action, actionAttrs, controllerAttrs, operation));
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
                            var serialized = ResolvePropertyName(p, effectiveNamingPolicy);
                            // First property with this name wins (matches CollectProperties derived-first ordering).
                            propMap.TryAdd(serialized, p);
                        }

                        foreach (var (propName, propSchema) in schema.Properties.ToList())
                        {
                            if (!propMap.TryGetValue(propName, out var prop))
                                continue;

                            var propDoc = docResolver.ResolveProperty(schemaType, prop);
                            if (string.IsNullOrEmpty(propDoc.Description))
                                continue;

                            if (propSchema is OpenApiSchemaReference)
                            {
                                // $ref cannot carry sibling keywords directly — wrap in allOf.
                                var wrapped = SchemaGenerator.EnsureMutableSchema(propSchema);
                                wrapped.Description = propDoc.Description;
                                schema.Properties[propName] = wrapped;
                            }
                            else if (propSchema is OpenApiSchema inlineProp
                                     && string.IsNullOrEmpty(inlineProp.Description))
                            {
                                inlineProp.Description = propDoc.Description;
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

        // ── Step 6: PathBase ─────────────────────────────────────────────────
        var pathBase = PathBaseExtractor.ExtractPathBase(sourceContext);
        if (!string.IsNullOrEmpty(pathBase))
            ApplyPathBase(document, pathBase, options.PathBaseEmission);

        // ── Step 7: Security schemes ─────────────────────────────────────────
        ApplySecuritySchemes(document, securityResult);

        // ── Step 8: ProblemDetails ──────────────────────────────────────────
        if (ProblemDetailsDetector.IsRegistered(sourceContext))
            ApplyProblemDetails(document);

        // ── Step 9: Global response headers ─────────────────────────────────
        var responseHeaders = ResponseHeaderExtractor.Extract(sourceContext);
        ApplyGlobalResponseHeaders(document, responseHeaders);

        // ── Step 10: Global media types ──────────────────────────────────────
        var globalMediaTypes = GlobalMediaTypesExtractor.Extract(sourceContext);
        ApplyGlobalMediaTypes(builtOperations, globalMediaTypes);

        // ── Step 11: Document-level tags metadata (descriptions + externalDocs) ─
        var docTagsResult = DocumentTagsExtractor.Extract(sourceContext);
        ApplyDocumentTagsMetadata(document, docTagsResult);

        return new BuildCoreResult(document, controllers, actions, schemaGenerator, sourceContext);
    }

    // =========================================================================
    // XML path list builder
    // =========================================================================

    /// <summary>
    /// Builds the ordered list of XML documentation file paths to load, in priority order:
    /// <list type="number">
    ///   <item>Explicit paths from <see cref="OpenApiDocumentOptions.XmlPaths"/> (user-provided, highest priority).</item>
    ///   <item>Auto-detected project XML alongside the assembly DLL (or explicit <see cref="OpenApiDocumentOptions.XmlPath"/>).</item>
    ///   <item>Framework / SDK ref-pack XML files discovered from the resolver's search paths.</item>
    /// </list>
    /// First-wins merging in <see cref="XmlDocParser"/> ensures project docs override framework docs.
    /// </summary>
    private static IReadOnlyList<string> BuildXmlPathList(OpenApiDocumentOptions options, AssemblyLoader loader)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPath(string? path)
        {
            if (!string.IsNullOrEmpty(path) && seen.Add(path))
                paths.Add(path);
        }

        // 1. Explicit user-provided paths (highest priority)
        if (options.XmlPaths is { Count: > 0 })
        {
            foreach (var p in options.XmlPaths)
                AddPath(p);
        }

        // 2. Auto-detected project XML (or explicit XmlPath for single-path back-compat)
        var autoDetectedXml = options.XmlPath
            ?? Path.ChangeExtension(options.AssemblyPath, ".xml");
        AddPath(autoDetectedXml);

        // 3. Framework / SDK ref-pack XML files (lowest priority — fill in descriptions for framework types)
        var frameworkXmls = loader.GetXmlDocumentationFiles();
        foreach (var p in frameworkXmls)
            AddPath(p);

        // Emit stderr warning if no ref-pack XML was discovered despite ref-pack paths being
        // expected. We compare against RefPackXmlCount (not the combined list size) because
        // the combined list also includes project XML found in Phase 1 — checking it would
        // suppress the warning whenever the project has its own XML doc.
        var missingHints = loader.MissingRefPackHints;
        if (missingHints.Count > 0 && loader.RefPackXmlCount == 0)
        {
            Console.Error.WriteLine(
                "Warning: framework XML documentation not found in SDK ref packs at: " +
                string.Join(", ", missingHints) +
                "; descriptions for framework types (e.g. ProblemDetails) will be empty. " +
                "Ref packs ship with the .NET SDK — install the SDK rather than only the runtime " +
                "(e.g. base your Docker image on mcr.microsoft.com/dotnet/sdk:N.0 instead of aspnet:N.0).");
        }

        return paths;
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
        IList<System.Reflection.CustomAttributeData> actionAttrs,
        IList<System.Reflection.CustomAttributeData> controllerAttrs,
        DocumentationResolver docResolver,
        SchemaGenerator schemaGenerator,
        SecuritySchemeExtractionResult securityResult)
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

            // Write schema.Default when the parameter has a default value.
            // Guard against OpenApiSchemaReference — only mutable OpenApiSchema supports Default.
            if (param.DefaultValue is not null && paramSchema is OpenApiSchema mutableParamSchema)
            {
                mutableParamSchema.Default = param.DefaultValue switch
                {
                    bool b    => JsonValue.Create(b),
                    int i     => JsonValue.Create(i),
                    long l    => JsonValue.Create(l),
                    float f   => JsonValue.Create(f),
                    double d  => JsonValue.Create(d),
                    string s  => JsonValue.Create(s),
                    decimal dec => JsonValue.Create(dec),
                    uint ui   => JsonValue.Create(ui),
                    short s16 => JsonValue.Create(s16),
                    ushort u16 => JsonValue.Create(u16),
                    ulong u64 => JsonValue.Create(u64),
                    sbyte sb  => JsonValue.Create(sb),
                    byte b8   => JsonValue.Create(b8),
                    _         => JsonValue.Create(param.DefaultValue.ToString()),
                };
            }

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

            if (resp.ContentTypes.Count > 0 && (resp.BodyType != null || resp.ContentTypesExplicit))
            {
                // Emit a Content section when there is a typed body, or when the content types
                // were declared explicitly via [Produces] (e.g. text/event-stream with no body).
                IOpenApiSchema? bodySchema = resp.BodyType != null
                    ? schemaGenerator.GenerateSchema(resp.BodyType)
                    : null;
                apiResponse.Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal);

                foreach (var ct in resp.ContentTypes)
                    apiResponse.Content[ct] = bodySchema != null
                        ? new OpenApiMediaType { Schema = bodySchema }
                        : new OpenApiMediaType();
            }

            operation.Responses[statusKey] = apiResponse;
        }

        // ── Per-operation security ────────────────────────────────────────────
        ApplyOperationSecurity(operation, actionAttrs, controllerAttrs, securityResult);

        return operation;
    }

    // =========================================================================
    // API versioning
    // =========================================================================

    /// <summary>
    /// Adds the <c>x-api-version</c> extension to an operation based on
    /// <c>Asp.Versioning</c> attributes found on the controller or action.
    /// </summary>
    /// <remarks>
    /// Extension format:
    /// <list type="bullet">
    ///   <item><c>x-api-version: "neutral"</c> — when <c>[ApiVersionNeutral]</c> is present.</item>
    ///   <item><c>x-api-version: ["1.0", "2.0"]</c> — JSON array of version strings.</item>
    ///   <item>Extension absent — when no versioning attributes exist on the endpoint.</item>
    /// </list>
    /// </remarks>
    private static void ApplyApiVersionExtension(
        OpenApiOperation operation,
        IList<System.Reflection.CustomAttributeData> actionAttrs,
        IList<System.Reflection.CustomAttributeData> controllerAttrs)
    {
        if (ApiVersionExtractor.IsVersionNeutral(actionAttrs, controllerAttrs))
        {
            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
            operation.Extensions["x-api-version"] = new JsonNodeExtension(JsonValue.Create("neutral")!);
            return;
        }

        var versions = ApiVersionExtractor.GetSupportedVersions(actionAttrs, controllerAttrs);
        if (versions.Count == 0)
            return;

        var jsonArray = new JsonArray(versions.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions["x-api-version"] = new JsonNodeExtension(jsonArray);
    }

    // =========================================================================
    // Rate limiting and response caching
    // =========================================================================

    /// <summary>
    /// Applies rate-limiting and response-caching metadata extracted from
    /// <c>[EnableRateLimiting]</c>, <c>[DisableRateLimiting]</c>, <c>[ResponseCache]</c>,
    /// and <c>[OutputCache]</c> attributes to <paramref name="operation"/>.
    /// </summary>
    /// <remarks>
    /// Rate limiting is emitted as an operation extension:
    /// <list type="bullet">
    ///   <item><c>x-rate-limit-disabled: true</c> when <c>[DisableRateLimiting]</c> is present.</item>
    ///   <item><c>x-rate-limit-policy: "policyName"</c> for active <c>[EnableRateLimiting]</c>.</item>
    /// </list>
    /// Response caching is emitted as a <c>Cache-Control</c> header on 2xx responses (status codes
    /// 200–299). The header description is built from the caching parameters (duration, no-store, etc.).
    /// Existing <c>Cache-Control</c> headers are not overwritten (first-wins semantics).
    /// </remarks>
    private static void ApplyRateLimitingAndCaching(
        OpenApiOperation operation,
        IList<System.Reflection.CustomAttributeData> actionAttrs,
        IList<System.Reflection.CustomAttributeData> controllerAttrs)
    {
        // ── Rate limiting ────────────────────────────────────────────────────
        var rateLimit = RateLimitingExtractor.Extract(actionAttrs, controllerAttrs);
        if (rateLimit != null)
        {
            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
            if (rateLimit.IsDisabled)
                operation.Extensions["x-rate-limit-disabled"] = new JsonNodeExtension(JsonValue.Create(true)!);
            else
                operation.Extensions["x-rate-limit-policy"] = new JsonNodeExtension(JsonValue.Create(rateLimit.PolicyName)!);
        }

        // ── Response caching ─────────────────────────────────────────────────
        var cache = ResponseCachingExtractor.Extract(actionAttrs, controllerAttrs);
        if (cache == null || operation.Responses == null)
            return;

        var cacheControlDescription = BuildCacheControlDescription(cache);

        foreach (var (statusKey, responseInterface) in operation.Responses)
        {
            // OpenAPI wildcard response keys ("default", "2XX") are intentionally skipped here.
            // ResponseExtractor currently emits only integer status codes — this filter is defensive.
            if (!int.TryParse(statusKey, out var statusCode) || statusCode < 200 || statusCode >= 300)
                continue;

            if (responseInterface is not OpenApiResponse openApiResponse)
                continue;

            openApiResponse.Headers ??= new Dictionary<string, IOpenApiHeader>(
                StringComparer.OrdinalIgnoreCase);

            if (!openApiResponse.Headers.ContainsKey("Cache-Control"))
            {
                openApiResponse.Headers["Cache-Control"] = new OpenApiHeader
                {
                    Description = cacheControlDescription,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                };
            }
        }
    }

    /// <summary>
    /// Builds a human-readable <c>Cache-Control</c> description string from
    /// the extracted caching metadata.
    /// </summary>
    private static string BuildCacheControlDescription(ResponseCacheInfo info)
    {
        var parts = new List<string>();

        if (info.NoStore)
            parts.Add("no-store");

        if (info.Location == "Client")
            parts.Add("private");

        if (info.DurationSeconds is { } duration)
            parts.Add($"max-age={duration}");

        if (info.Location == "None")
            parts.Add("no-cache");

        if (parts.Count == 0)
            return "Cache-Control";

        return $"Cache-Control: {string.Join(", ", parts)}";
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

    private static string ResolvePropertyName(System.Reflection.PropertyInfo prop, JsonNamingPolicy policy)
    {
        // Check [JsonPropertyName] first
        var jsonPropAttr = AttributeHelper.GetAttribute(prop, AttributeHelper.Names.JsonPropertyName);
        if (jsonPropAttr != null)
        {
            var name = AttributeHelper.GetConstructorArgument<string>(jsonPropAttr, 0);
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return Schema.SchemaGenerator.ApplyNamingPolicy(prop.Name, policy);
    }

    // =========================================================================
    // PathBase emission
    // =========================================================================

    /// <summary>
    /// Applies a detected path base to <paramref name="document"/> according to
    /// <paramref name="emission"/>.
    /// </summary>
    internal static void ApplyPathBase(
        OpenApiDocument document,
        string pathBase,
        PathBaseEmission emission)
    {
        if (emission == PathBaseEmission.PathPrefix)
        {
            PrependPathBase(document, pathBase);
        }
        else
        {
            AppendServerEntry(document, pathBase);
        }
    }

    /// <summary>
    /// Rebuilds <c>document.Paths</c> with <paramref name="pathBase"/> prepended to
    /// every path key.
    /// </summary>
    private static void PrependPathBase(OpenApiDocument document, string pathBase)
    {
        if (document.Paths is null || document.Paths.Count == 0)
            return;

        var prefixed = new OpenApiPaths();
        foreach (var (key, value) in document.Paths)
        {
            // key always starts with "/" per OpenAPI spec; pathBase also starts with "/"
            // so the concatenation is correct (e.g. "/api/v1" + "/users" → "/api/v1/users").
            prefixed[pathBase + key] = value;
        }

        document.Paths = prefixed;
    }

    /// <summary>
    /// Appends a relative server entry for <paramref name="pathBase"/> to
    /// <c>document.Servers</c>, avoiding duplicates.
    /// </summary>
    private static void AppendServerEntry(OpenApiDocument document, string pathBase)
    {
        if (document.Servers is not null &&
            document.Servers.Any(s => string.Equals(s.Url, pathBase, StringComparison.Ordinal)))
        {
            return;
        }

        document.Servers ??= new List<OpenApiServer>();
        document.Servers.Add(new OpenApiServer
        {
            Url = pathBase,
            Description = "Path base from UsePathBase()",
        });
    }

    // =========================================================================
    // ProblemDetails injection
    // =========================================================================

    /// <summary>
    /// Adds the RFC 7807 <c>ProblemDetails</c> schema to the document's components and
    /// injects default <c>application/problem+json</c> responses (400, 422, 500) into
    /// every operation that does not already declare those status codes.
    /// </summary>
    internal static void ApplyProblemDetails(OpenApiDocument document)
    {
        // 1. Ensure Components and Schemas exist.
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        // 2. Register the ProblemDetails schema (skip if already present, e.g. from a DTO).
        if (!document.Components.Schemas.ContainsKey(ProblemDetailsSchema.SchemaId))
            document.Components.Schemas[ProblemDetailsSchema.SchemaId] = ProblemDetailsSchema.CreateSchema();

        // 3. Build a $ref pointing to the registered component schema.
        var schemaRef = new OpenApiSchemaReference(ProblemDetailsSchema.SchemaId, null);

        // 4. Inject default error responses into every operation.
        if (document.Paths is null)
            return;

        foreach (var (_, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem pathItem || pathItem.Operations is null)
                continue;

            foreach (var (_, operation) in pathItem.Operations)
                ProblemDetailsResponseInjector.Inject(operation, schemaRef);
        }
    }

    // =========================================================================
    // Security schemes
    // =========================================================================

    /// <summary>
    /// Applies security schemes and global security requirements extracted from Roslyn
    /// source analysis to the document's <c>components/securitySchemes</c> and
    /// top-level <c>security</c> fields.
    /// </summary>
    private static void ApplySecuritySchemes(
        OpenApiDocument document,
        SecuritySchemeExtractionResult securityResult)
    {
        if (securityResult.Schemes.Count > 0)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??=
                new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

            foreach (var (name, scheme) in securityResult.Schemes)
                document.Components.SecuritySchemes.TryAdd(name, scheme);
        }

        if (securityResult.GlobalRequirementSchemeNames.Count > 0)
        {
            var requirement = new OpenApiSecurityRequirement();
            foreach (var schemeName in securityResult.GlobalRequirementSchemeNames)
            {
                var reference = new OpenApiSecuritySchemeReference(schemeName, null, null);
                requirement[reference] = [];
            }

            document.Security ??= new List<OpenApiSecurityRequirement>();
            document.Security.Add(requirement);
        }
    }

    /// <summary>
    /// Applies per-operation security based on <c>[Authorize]</c> /
    /// <c>[AllowAnonymous]</c> attributes on the action and its controller.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>[AllowAnonymous]</c> → <c>security: []</c> (empty list, overrides global requirement).</item>
    ///   <item><c>[Authorize(AuthenticationSchemes = "Bearer")]</c> → explicit security requirement.</item>
    ///   <item>No override → nothing set (inherits global security if present).</item>
    /// </list>
    /// </remarks>
    private static void ApplyOperationSecurity(
        OpenApiOperation operation,
        IList<System.Reflection.CustomAttributeData> actionAttrs,
        IList<System.Reflection.CustomAttributeData> controllerAttrs,
        SecuritySchemeExtractionResult securityResult)
    {
        var auth = AuthorizationExtractor.Extract(actionAttrs, controllerAttrs);

        if (auth.IsAnonymous)
        {
            // Empty security list overrides any global security requirement.
            operation.Security = [];
            return;
        }

        if (auth.AuthenticationSchemes is { Count: > 0 })
        {
            var requirement = new OpenApiSecurityRequirement();
            foreach (var schemeName in auth.AuthenticationSchemes)
            {
                var reference = new OpenApiSecuritySchemeReference(schemeName, null, null);
                requirement[reference] = [];
            }

            operation.Security = [requirement];
        }

        // If only RequiresAuthorization (no explicit schemes), we do nothing —
        // the operation inherits the global security requirement if one is set.
        // This avoids emitting a requirement with an unknown scheme name.
    }

    // =========================================================================
    // Global response headers
    // =========================================================================

    /// <summary>
    /// Adds <paramref name="headerNames"/> as global response headers to every
    /// response object in <paramref name="document"/>. Existing headers with the
    /// same name are not overwritten (first-wins semantics).
    /// </summary>
    /// <remarks>
    /// This method is called with header names extracted from middleware
    /// registrations via <see cref="ResponseHeaderExtractor.Extract"/>. The
    /// resulting headers have a generic <c>string</c> schema and a description
    /// indicating their middleware origin.
    /// </remarks>
    internal static void ApplyGlobalResponseHeaders(
        OpenApiDocument document,
        IReadOnlyList<string> headerNames)
    {
        if (headerNames.Count == 0)
            return;

        if (document.Paths is null)
            return;

        foreach (var (_, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem pathItem || pathItem.Operations == null)
                continue;

            foreach (var (_, operation) in pathItem.Operations)
            {
                if (operation.Responses == null)
                    continue;

                foreach (var (_, responseInterface) in operation.Responses)
                {
                    if (responseInterface is not OpenApiResponse openApiResponse)
                        continue;

                    openApiResponse.Headers ??= new Dictionary<string, IOpenApiHeader>(
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var headerName in headerNames)
                    {
                        if (!openApiResponse.Headers.ContainsKey(headerName))
                        {
                            openApiResponse.Headers[headerName] = new OpenApiHeader
                            {
                                Description = $"Response header '{headerName}' set by middleware.",
                                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                            };
                        }
                    }
                }
            }
        }
    }

    // =========================================================================
    // Document-level tags metadata
    // =========================================================================

    /// <summary>
    /// Enriches <c>document.Tags</c> with descriptions and externalDocs extracted from
    /// Roslyn source analysis and sets the document-level <c>externalDocs</c> when found.
    /// </summary>
    /// <remarks>
    /// Priority: existing non-null values win. Roslyn data is applied only when the
    /// corresponding field on the tag is currently null/empty. This preserves
    /// <c>[SwaggerTag]</c> attribute descriptions and XML-doc comments that were resolved
    /// earlier in the pipeline.
    /// </remarks>
    internal static void ApplyDocumentTagsMetadata(
        OpenApiDocument document,
        DocumentTagsExtractionResult docTagsResult)
    {
        // Enrich individual tags.
        if (document.Tags is { Count: > 0 } && docTagsResult.TagsByName.Count > 0)
        {
            foreach (var tag in document.Tags)
            {
                if (!docTagsResult.TagsByName.TryGetValue(tag.Name ?? string.Empty, out var metadata))
                    continue;

                // Description: only fill when currently empty.
                if (string.IsNullOrEmpty(tag.Description) &&
                    !string.IsNullOrEmpty(metadata.Description))
                {
                    tag.Description = metadata.Description;
                }

                // ExternalDocs: only fill when not already present.
                if (tag.ExternalDocs == null &&
                    !string.IsNullOrEmpty(metadata.ExternalDocsUrl) &&
                    Uri.TryCreate(metadata.ExternalDocsUrl, UriKind.Absolute, out var extUri))
                {
                    tag.ExternalDocs = new OpenApiExternalDocs
                    {
                        Url = extUri,
                        Description = metadata.ExternalDocsDescription,
                    };
                }
            }
        }

        // Document-level externalDocs.
        if (document.ExternalDocs == null &&
            !string.IsNullOrEmpty(docTagsResult.ExternalDocsUrl) &&
            Uri.TryCreate(docTagsResult.ExternalDocsUrl, UriKind.Absolute, out var docExtUri))
        {
            document.ExternalDocs = new OpenApiExternalDocs
            {
                Url = docExtUri,
                Description = docTagsResult.ExternalDocsDescription,
            };
        }
    }

    // =========================================================================
    // Global media types
    // =========================================================================

    /// <summary>
    /// Applies global Produces/Consumes content types to all operations that have not
    /// explicitly overridden them via per-action or per-controller <c>[Produces]</c> /
    /// <c>[Consumes]</c> attributes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Produces (response):</b> For each response entry whose <c>Content</c> dictionary
    /// was built using the <c>["application/json"]</c> default (i.e. the action and its
    /// controller have no <c>[Produces]</c> attribute), the content keys are replaced with
    /// the global list.  Responses that already use an explicit per-action content-type list
    /// are left unchanged.  Responses without a body (<c>Content</c> is null or empty) are
    /// also left unchanged.
    /// </para>
    /// <para>
    /// <b>Consumes (request body):</b> When the request body was built using the hardcoded
    /// <c>"application/json"</c> key (no per-action/controller <c>[Consumes]</c>), the
    /// content key is replaced with the global list.
    /// </para>
    /// </remarks>
    private static void ApplyGlobalMediaTypes(
        IReadOnlyList<(ActionInfo Action,
            IList<System.Reflection.CustomAttributeData> ActionAttrs,
            IList<System.Reflection.CustomAttributeData> ControllerAttrs,
            OpenApiOperation Operation)> builtOperations,
        GlobalMediaTypesExtractionResult globalMediaTypes)
    {
        bool hasGlobalProduces = globalMediaTypes.ProducesContentTypes.Count > 0;
        bool hasGlobalConsumes = globalMediaTypes.ConsumesContentTypes.Count > 0;

        if (!hasGlobalProduces && !hasGlobalConsumes)
            return;

        foreach (var (_, actionAttrs, controllerAttrs, operation) in builtOperations)
        {
            // ── Produces (responses) ──────────────────────────────────────────
            if (hasGlobalProduces && operation.Responses != null)
            {
                // Only apply if the action/controller has no per-action [Produces].
                bool hasPerActionProduces =
                    AttributeHelper.HasAttribute(actionAttrs, AttributeHelper.Names.Produces)
                    || AttributeHelper.HasAttribute(controllerAttrs, AttributeHelper.Names.Produces);

                if (!hasPerActionProduces)
                {
                    foreach (var (_, responseInterface) in operation.Responses)
                    {
                        if (responseInterface is not OpenApiResponse response)
                            continue;

                        // Only process responses that have a body (Content is non-null and non-empty).
                        if (response.Content is not { Count: > 0 })
                            continue;

                        // Replace the content entries with the global content types.
                        // We keep the existing schema from the first entry and create
                        // a new OpenApiMediaType wrapper per content type — consistent
                        // with BuildOperation which creates new instances per entry.
                        var firstSchema = response.Content.Values.First().Schema;

                        response.Content.Clear();
                        foreach (var ct in globalMediaTypes.ProducesContentTypes)
                            response.Content[ct] = new OpenApiMediaType { Schema = firstSchema };
                    }
                }
            }

            // ── Consumes (request body) ────────────────────────────────────────
            if (hasGlobalConsumes && operation.RequestBody?.Content is { Count: > 0 })
            {
                // Only apply if the action/controller has no per-action [Consumes].
                bool hasPerActionConsumes =
                    AttributeHelper.HasAttribute(actionAttrs, AttributeHelper.Names.Consumes)
                    || AttributeHelper.HasAttribute(controllerAttrs, AttributeHelper.Names.Consumes);

                if (!hasPerActionConsumes)
                {
                    var firstSchema = operation.RequestBody.Content.Values.First().Schema;

                    operation.RequestBody.Content.Clear();
                    foreach (var ct in globalMediaTypes.ConsumesContentTypes)
                        operation.RequestBody.Content[ct] = new OpenApiMediaType { Schema = firstSchema };
                }
            }
        }
    }


    // =========================================================================
    // Source analysis
    // =========================================================================

    /// <summary>
    /// Attempts to create a <see cref="SourceAnalysisContext"/> by resolving the source root
    /// and building a Roslyn compilation. Returns <see cref="SourceAnalysisContext.Empty"/>
    /// on any failure; never throws.
    /// </summary>
    private static SourceAnalysisContext TryBuildSourceAnalysisContext(
        OpenApiDocumentOptions options,
        AssemblyLoader loader)
    {
        try
        {
            // 1. Determine source root: explicit override > auto-detect > give up.
            string? sourceRoot = options.SourceRoot;

            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                if (!SourceRootResolver.TryResolve(options.AssemblyPath, out sourceRoot, out _))
                    return SourceAnalysisContext.Empty;
            }

            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
                return SourceAnalysisContext.Empty;

            // 2. Compile sources via Roslyn.
            var compilationResult = SourceCompiler.Compile(sourceRoot);

            // 3. Locate entry-point syntax node.
            var entryPoint = loader.Assembly.EntryPoint;
            SyntaxNode? entryPointNode = null;
            if (entryPoint != null)
            {
                entryPointNode = EntryPointFinder.Find(entryPoint, compilationResult.Compilation);
            }

            return new SourceAnalysisContext(compilationResult, entryPointNode);
        }
        catch
        {
            // Any failure in source analysis must not break the main extraction pipeline.
            return SourceAnalysisContext.Empty;
        }
    }

}
