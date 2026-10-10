using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Documentation;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Versioning;
using DotNetOpenApiExtract.Core.Diagnostics;
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
    /// When <see langword="null"/> or whitespace, the builder falls back to the <c>Title</c> of the
    /// <c>OpenApiInfo</c> in <c>SwaggerDoc</c> / <c>AddOpenApi</c>, then <c>[AssemblyTitle]</c>, then
    /// <c>[AssemblyProduct]</c>, then the DLL file name.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Version of the API (<c>info.version</c>). When <see langword="null"/> or whitespace, the builder falls
    /// back to the <c>Version</c> of the <c>OpenApiInfo</c> in <c>SwaggerDoc</c> / <c>AddOpenApi</c>, then
    /// to <c>"v1"</c>.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Optional description for the API. When <see langword="null"/> or whitespace, the builder falls
    /// back to the <c>Description</c> of the <c>OpenApiInfo</c> in <c>SwaggerDoc</c> / <c>AddOpenApi</c>,
    /// then <c>[AssemblyDescription]</c>.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// JSON property naming policy. Defaults to <see langword="null"/> which resolves to
    /// <see cref="JsonNamingPolicy.CamelCase"/> (the ASP.NET Core default).
    /// Applies only when Roslyn analysis finds no JSON options at all in the entry assembly's
    /// source: once <c>AddJsonOptions</c> or <c>ConfigureHttpJsonOptions</c> sets anything, each
    /// context uses its own setting or the ASP.NET Core default.
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
    /// Maps to <c>info.contact.name</c> in the generated document. When <see langword="null"/> or
    /// whitespace, the builder falls back to the <c>Contact.Name</c> of the <c>OpenApiInfo</c> in
    /// <c>SwaggerDoc</c> / <c>AddOpenApi</c>, then <c>[AssemblyCompany]</c>.
    /// </summary>
    public string? ContactName { get; init; }

    /// <summary>
    /// Optional email address of the contact person or organisation.
    /// Maps to <c>info.contact.email</c> in the generated document.
    /// No format validation is performed — any string is accepted by OpenAPI.
    /// When <see langword="null"/>, the <c>Contact.Email</c> of the <c>OpenApiInfo</c> in <c>SwaggerDoc</c> /
    /// <c>AddOpenApi</c> is used.
    /// </summary>
    public string? ContactEmail { get; init; }

    /// <summary>
    /// Optional URL pointing to the contact information page.
    /// Must be a valid absolute URI; if the value cannot be parsed the URL
    /// is silently omitted and a warning is written to <c>stderr</c>.
    /// Maps to <c>info.contact.url</c> in the generated document. When <see langword="null"/>, the
    /// <c>Contact.Url</c> of the <c>OpenApiInfo</c> in <c>SwaggerDoc</c> / <c>AddOpenApi</c> is used.
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
    /// Maps to <c>info.termsOfService</c>. When <see langword="null"/>, the <c>TermsOfService</c> of the
    /// <c>OpenApiInfo</c> in <c>SwaggerDoc</c> / <c>AddOpenApi</c> is used.
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
    /// Optional short summary of the API (<c>info.summary</c>, OpenAPI 3.1+). For a 3.0 target, which
    /// has no such field, it is omitted with a warning.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Optional SPDX license identifier (<c>info.license.identifier</c>, OpenAPI 3.1+). Mutually
    /// exclusive with <see cref="LicenseUrl"/>; requires <see cref="LicenseName"/>. A 3.0 target gets it
    /// as <c>x-oai-license-identifier</c>, with a warning.
    /// </summary>
    /// <exception cref="OpenApiConfigurationException">
    /// From <see cref="OpenApiDocumentBuilder.Build"/> when it is set together with
    /// <see cref="LicenseUrl"/>, or without a license name.
    /// </exception>
    public string? LicenseIdentifier { get; init; }

    /// <summary>
    /// Optional names of the <see cref="Servers"/>, by position: none, or exactly one non-empty, unique
    /// name per server (<c>servers[].name</c>, OpenAPI 3.2; <c>x-oai-name</c> with a warning before).
    /// The server a <see cref="PathBaseEmission.ServersEntry"/> path base adds gets no name.
    /// </summary>
    /// <exception cref="OpenApiConfigurationException">
    /// From <see cref="OpenApiDocumentBuilder.Build"/> on another count, an empty or a repeated name.
    /// </exception>
    public IReadOnlyList<string>? ServerNames { get; init; }

    /// <summary>
    /// Optional URI reference of the document itself (<c>$self</c>, OpenAPI 3.2; <c>x-oai-$self</c> with
    /// a warning before). A fragment or a value that is not a URI reference is a configuration error.
    /// </summary>
    public string? SelfUrl { get; init; }

    /// <summary>
    /// Optional JSON Schema dialect of the document's schemas (<c>jsonSchemaDialect</c>, OpenAPI 3.1+):
    /// the OAS base dialect of the target version (<c>https://spec.openapis.org/oas/3.1/dialect/base</c>
    /// for 3.1, <c>https://spec.openapis.org/oas/3.2/dialect/2025-09-17</c> for 3.2). For a 3.0 target
    /// either is accepted and the field is omitted with a warning. Any other value is a configuration
    /// error. When <see langword="null"/>, no dialect is written (the specification's default applies).
    /// </summary>
    public string? JsonSchemaDialect { get; init; }

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

    /// <summary>
    /// The OpenAPI version the document is built for: <see cref="OpenApiSpecVersion.OpenApi3_0"/>
    /// (default), <see cref="OpenApiSpecVersion.OpenApi3_1"/> or <see cref="OpenApiSpecVersion.OpenApi3_2"/>.
    /// Corresponds to the <c>--openapi-version</c> CLI flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The version is a build parameter, not a serialization detail: the document is built for
    /// this version and must be serialized into the same version. Serializing it into another
    /// version is not supported.
    /// </para>
    /// <para>
    /// Any other value (<see cref="OpenApiSpecVersion.OpenApi2_0"/> or a value outside the enum)
    /// makes <see cref="OpenApiDocumentBuilder.Build"/> and
    /// <see cref="OpenApiDocumentBuilder.BuildWithValidation"/> throw
    /// <see cref="OpenApiConfigurationException"/> before the assembly is loaded.
    /// </para>
    /// </remarks>
    public OpenApiSpecVersion OpenApiVersion { get; init; } = TargetVersion.Default;

    /// <summary>
    /// Receives the warnings produced while the document is built, each delivered once per
    /// build. When <see langword="null"/> (default), warnings are printed to <c>Console.Error</c>
    /// as before. Warnings never stop the build.
    /// </summary>
    public Action<ExtractionDiagnostic>? OnDiagnostic { get; init; }
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
    /// <exception cref="OpenApiConfigurationException">
    /// Thrown before the assembly is loaded when <see cref="OpenApiDocumentOptions.OpenApiVersion"/>
    /// is not 3.0, 3.1 or 3.2.
    /// </exception>
    /// <summary>
    /// Internal build result used by both <see cref="Build"/> and <see cref="BuildWithValidation"/>.
    /// </summary>
    private sealed record BuildCoreResult(
        OpenApiDocument Document,
        IReadOnlyList<ControllerInfo> Controllers,
        IReadOnlyList<ActionInfo> Actions,
        IReadOnlyDictionary<string, Type> SchemaTypes,
        SourceAnalysisContext SourceContext);

    /// <summary>
    /// Builds a complete OpenAPI document and runs validation.
    /// Calls the same pipeline as <see cref="Build"/>, then applies <see cref="OpenApiValidator.Validate"/>.
    /// </summary>
    /// <param name="options">Build options.</param>
    /// <param name="validationContext">Validation options. CLR bindings are populated automatically.</param>
    /// <param name="validationResult">Receives the validation result after building.</param>
    /// <returns>The fully-populated <see cref="OpenApiDocument"/>.</returns>
    /// <remarks>
    /// Validation runs for the version the document is built for. When
    /// <see cref="ValidationContext.OpenApiSpecVersion"/> is <see langword="null"/>, it is taken from
    /// <see cref="OpenApiDocumentOptions.OpenApiVersion"/>.
    /// </remarks>
    /// <exception cref="OpenApiConfigurationException">
    /// Thrown before the assembly is loaded when <see cref="OpenApiDocumentOptions.OpenApiVersion"/>
    /// is not 3.0, 3.1 or 3.2, or when <see cref="ValidationContext.OpenApiSpecVersion"/> names a
    /// different explicit version.
    /// </exception>
    public static OpenApiDocument BuildWithValidation(
        OpenApiDocumentOptions options,
        ValidationContext validationContext,
        out ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validationContext);

        TargetVersion.EnsureSupported(options.OpenApiVersion, nameof(OpenApiDocumentOptions.OpenApiVersion));
        DocumentMetadata.Validate(options);
        var validationVersion = TargetVersion.ResolveValidationVersion(
            options.OpenApiVersion, validationContext.OpenApiSpecVersion);

        using var loader = new AssemblyLoader(options.AssemblyPath);
        var core = BuildCore(options, loader);

        // ── Build CLR bindings for validation ────────────────────────────────
        // ActionByOperationKey: "METHOD /path" → (Controller, Action)
        // Bound to the same winner the document uses for each path and method.
        var actionByKey = new Dictionary<string, (ControllerInfo, ActionInfo)>(StringComparer.Ordinal);
        foreach (var group in OperationConflicts.Group(core.Actions))
        {
            var key = $"{group.HttpMethod} {group.Path}";
            actionByKey.TryAdd(key, (group.Winner.Controller, group.Winner));
        }

        // TypeBySchemaId: schema component ID → CLR Type
        var typeBySchemaId = new Dictionary<string, Type>(
            core.SchemaTypes, StringComparer.Ordinal);

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
            OpenApiSpecVersion       = validationVersion,
        };

        validationResult = Validation.OpenApiValidator.Validate(core.Document, enrichedContext);
        return core.Document;
    }

    public static OpenApiDocument Build(OpenApiDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        TargetVersion.EnsureSupported(options.OpenApiVersion, nameof(OpenApiDocumentOptions.OpenApiVersion));
        DocumentMetadata.Validate(options);

        using var loader = new AssemblyLoader(options.AssemblyPath);
        return BuildCore(options, loader).Document;
    }

    // =========================================================================
    // Serialization contexts
    // =========================================================================

    /// <summary>Naming of one serialization context after defaults are applied.</summary>
    private readonly record struct ContextJson(JsonNamingPolicy NamingPolicy, JsonNamingPolicy DictionaryKeyPolicy);

    /// <summary>
    /// Effective naming of <paramref name="context"/>: its own Program.cs setting; otherwise the
    /// <paramref name="fallbackNamingPolicy"/> option, but only when Program.cs configures no JSON
    /// options in either context; otherwise the ASP.NET Core default (camelCase).
    /// </summary>
    private static ContextJson ResolveContextJson(
        JsonContextOptions context, JsonOptionsExtractionResult all, JsonNamingPolicy? fallbackNamingPolicy)
    {
        var anyConfigured = all.Mvc.IsConfigured || all.Http.IsConfigured;
        var naming = context.PropertyNamingPolicy
            ?? (anyConfigured ? null : fallbackNamingPolicy)
            ?? JsonNamingPolicy.CamelCase;
        return new ContextJson(naming, context.DictionaryKeyPolicy ?? naming);
    }

    /// <summary>
    /// Whether the two contexts shape a schema differently: naming policy, ignore condition, number
    /// handling or the set of converters. The dictionary key policy does not count.
    /// </summary>
    private static bool ContextsShapeSchemasDifferently(
        ContextJson mvcJson, JsonContextOptions mvc, ContextJson httpJson, JsonContextOptions http)
    {
        return mvcJson.NamingPolicy != httpJson.NamingPolicy
            || (mvc.DefaultIgnoreCondition ?? JsonIgnoreCondition.Never) != (http.DefaultIgnoreCondition ?? JsonIgnoreCondition.Never)
            || (mvc.NumberHandling ?? JsonNumberHandling.Strict) != (http.NumberHandling ?? JsonNumberHandling.Strict)
            || !new HashSet<string>(mvc.GlobalConverterTypeNames, StringComparer.Ordinal)
                .SetEquals(http.GlobalConverterTypeNames)
            || !mvc.GlobalConverterEnumNamingPolicies.SequenceEqual(http.GlobalConverterEnumNamingPolicies);
    }

    /// <summary>
    /// The full name of <paramref name="type"/> in C# form: <c>Ns.Envelope&lt;Ns.Item&gt;</c> for a
    /// closed generic type, nested types with <c>.</c>.
    /// </summary>
    private static string TypeDisplayName(Type type)
    {
        if (!type.IsGenericType)
            return (type.FullName ?? type.Name).Replace('+', '.');

        var definition = type.GetGenericTypeDefinition();
        var name = (definition.FullName ?? definition.Name).Replace('+', '.');
        var tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeDisplayName))}>";
    }

    /// <summary>
    /// One warning on the document naming every CLR type that has schemas in both contexts: each
    /// context describes it by its own options, under its own component.
    /// </summary>
    private static void RecordSharedContextTypes(LossLedger ledger, SchemaGenerator mvc, SchemaGenerator http)
    {
        var mvcTypes = mvc.SchemaTypes.Values.Select(TypeDisplayName).ToHashSet(StringComparer.Ordinal);
        var shared = http.SchemaTypes.Values
            .Select(TypeDisplayName)
            .Where(mvcTypes.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (shared.Count == 0)
            return;

        ledger.Add(new PendingLoss
        {
            Class    = LossClass.Source,
            Code     = ExtractionDiagnosticCodes.SerializationContextsSharedTypes,
            Anchor   = LossAnchor.Document.Instance,
            Message  = "the MVC JSON options (AddJsonOptions) and the HTTP JSON options (ConfigureHttpJsonOptions) " +
                       "shape schemas differently; types used in both contexts get a schema per context " +
                       $"(the HTTP one with the suffix Http when both exist): {string.Join(", ", shared)}.",
            Feature  = "mediaType.schema",
            Subjects = shared,
        });
    }

    // =========================================================================
    // Core build pipeline
    // =========================================================================

    private static BuildCoreResult BuildCore(OpenApiDocumentOptions options, AssemblyLoader loader)
    {
        // Every warning of this build goes through one bag: delivered once, to the
        // subscriber or to stderr.
        var diagnostics = new DiagnosticBag(options.OnDiagnostic);

        // Warnings that depend on the target version or on the finished document wait here
        // until the document is final (see DownlevelPass at the end).
        var ledger = new LossLedger(options.OpenApiVersion);

        // ── Source analysis (best-effort, never throws) ──────────────────────
        var sourceContext = TryBuildSourceAnalysisContext(options, loader, diagnostics);

        // ── Security extraction (Roslyn, best-effort, before operation loop) ──
        var securityResult = SecuritySchemeExtractor.Extract(sourceContext, diagnostics.Report);

        // ── JSON options extraction (Roslyn, best-effort) ────────────────────
        var jsonOptions = JsonOptionsExtractor.Extract(sourceContext, diagnostics.Report);
        var docTagsResult = DocumentTagsExtractor.Extract(sourceContext, diagnostics.Report);
        EntryPointConfigurationCheck.Check(sourceContext, loader.Assembly, diagnostics.Report);

        // ── Resolve effective naming policy ───────────────────────────────────
        // Controller bodies serialize with the MVC options (AddJsonOptions) only; the HTTP
        // options (ConfigureHttpJsonOptions) never reach them.
        var mvcJson = ResolveContextJson(jsonOptions.Mvc, jsonOptions, options.NamingPolicy);
        var httpJson = ResolveContextJson(jsonOptions.Http, jsonOptions, options.NamingPolicy);
        var effectiveNamingPolicy = mvcJson.NamingPolicy;
        // Typed IResult bodies and server-sent events data serialize with the HTTP options; they get
        // schemas of their own only when the options differ in what shapes a schema.
        var contextsDiffer = ContextsShapeSchemasDifferently(mvcJson, jsonOptions.Mvc, httpJson, jsonOptions.Http);

        // ── Resolve XML documentation paths (priority: XmlPaths > XmlPath > auto-detect > framework) ──
        var xmlPaths = BuildXmlPathList(options, loader, diagnostics);

        var xmlParser = XmlDocParser.FromSources(xmlPaths);
        var docResolver = new DocumentationResolver(xmlParser);
        var schemaGenerator = new SchemaGenerator(new SchemaOptions
        {
            NamingPolicy             = effectiveNamingPolicy,
            EnumAsString             = options.EnumAsString,
            DictionaryKeyPolicy      = mvcJson.DictionaryKeyPolicy,
            DefaultIgnoreCondition   = jsonOptions.Mvc.DefaultIgnoreCondition,
            NumberHandling           = jsonOptions.Mvc.NumberHandling,
            GlobalConverterTypeNames = jsonOptions.Mvc.GlobalConverterTypeNames,
            GlobalConverterEnumNamingPolicies = jsonOptions.Mvc.GlobalConverterEnumNamingPolicies,
            EnumAutoDescription      = options.EnumAutoDescription,
            EnumVarnames             = options.EnumVarnames,
            OpenApiVersion           = options.OpenApiVersion,
            OnDiagnostic             = diagnostics.Report,
        }, docResolver);
        // Schema warnings (polymorphism) wait in the build's ledger for the finished document.
        schemaGenerator.AttachLedger(ledger);

        // Schemas of the HTTP context are generated after the operation loop, once every MVC
        // schema exists, so a type both contexts describe is recognized when its HTTP id is chosen.
        var httpSlots = new List<Action<SchemaGenerator>>();

        // ── Step 1: Discovery ───────────────────────────────────────────────
        var controllers = ControllerDiscovery.DiscoverControllers(loader.Assembly);
        var actions = ActionDiscovery.DiscoverActions(controllers, diagnostics.Report);

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

        // Precedence chains, field by field: the option, then the OpenApiInfo of SwaggerDoc / AddOpenApi in
        // Program.cs, then the assembly attributes (IsNullOrWhiteSpace rejects both null and empty/whitespace).
        static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        var programContact = docTagsResult.Contact;
        var resolvedTitle =
            Given(options.Title)
            ?? Given(docTagsResult.InfoTitle)
            ?? Given(asmTitle)
            ?? Given(asmProduct)
            ?? loader.Assembly.GetName().Name
            ?? "API";

        var resolvedDescription =
            Given(options.Description)
            ?? Given(docTagsResult.InfoDescription)
            ?? Given(asmDescription);

        var resolvedVersion =
            Given(options.Version)
            ?? Given(docTagsResult.InfoVersion)
            ?? "v1";

        // contact.name: option wins, then Program.cs, then [AssemblyCompany] as last resort.
        var resolvedContactName =
            Given(options.ContactName)
            ?? Given(programContact?.Name)
            ?? Given(asmCompany);

        // contact.email and contact.url have no assembly attribute: the option (an empty email option still
        // creates the block, as before), then Program.cs.
        var resolvedContactEmail = options.ContactEmail ?? Given(programContact?.Email);
        var (contactUrl, contactUrlSource) = options.ContactUrl != null
            ? (options.ContactUrl, "--contact-url")
            : (Given(programContact?.Url), "OpenApiContact.Url in Program.cs");

        var info = new OpenApiInfo
        {
            Title       = resolvedTitle,
            Version     = resolvedVersion,
            Description = resolvedDescription,
        };

        // Contact: build the block when any of its fields resolves (preserves existing behaviour including
        // ContactEmail = "" creating Contact).
        if (resolvedContactName != null || resolvedContactEmail != null || contactUrl != null)
        {
            Uri? contactUri = null;
            if (contactUrl != null)
            {
                if (Uri.TryCreate(contactUrl, UriKind.Absolute, out var parsed))
                    contactUri = parsed;
                else
                    WarnInvalidInfoUri(diagnostics, contactUrlSource, contactUrl, "#/info/contact/url");
            }

            info.Contact = new OpenApiContact
            {
                Name  = resolvedContactName,
                Email = resolvedContactEmail,
                Url   = contactUri,
            };
        }

        // License: the options and the Program.cs OpenApiLicense initializer, merged field by field, the
        // options first. The URL and the identifier exclude each other: both in Program.cs is a
        // configuration error; an option for either replaces both Program.cs fields without an error.
        // A URL or identifier without a name after the merge is a configuration error, never dropped.
        var programLicense = docTagsResult.License;
        if (programLicense is { Url: { Length: > 0 }, Identifier: { Length: > 0 } })
            throw new OpenApiConfigurationException(
                "The OpenApiLicense in Program.cs sets both Identifier and Url, which are mutually exclusive: keep one of them.");
        var optionsLinkLicense = !string.IsNullOrWhiteSpace(options.LicenseUrl) || !string.IsNullOrWhiteSpace(options.LicenseIdentifier);
        var licenseName = !string.IsNullOrWhiteSpace(options.LicenseName) ? options.LicenseName : programLicense?.Name;
        var licenseUrl = optionsLinkLicense ? options.LicenseUrl : programLicense?.Url;
        var licenseIdentifier = optionsLinkLicense ? options.LicenseIdentifier : programLicense?.Identifier;
        DocumentMetadata.ValidateLicense(licenseName, licenseUrl, licenseIdentifier);
        if (!string.IsNullOrWhiteSpace(licenseName))
        {
            Uri? licenseUri = null;
            if (!string.IsNullOrWhiteSpace(licenseUrl))
            {
                if (Uri.TryCreate(licenseUrl, UriKind.Absolute, out var parsed))
                    licenseUri = parsed;
                else
                    WarnInvalidInfoUri(diagnostics, "--license-url", licenseUrl, "#/info/license/url");
            }

            info.License = new OpenApiLicense
            {
                Name       = licenseName,
                Url        = licenseUri,
                Identifier = string.IsNullOrWhiteSpace(licenseIdentifier) ? null : licenseIdentifier,
            };
        }

        // info.summary (the option, else Program.cs) exists from 3.1: a 3.0 document omits it (the form
        // of the version), with a warning.
        var summary = !string.IsNullOrWhiteSpace(options.Summary) ? options.Summary : docTagsResult.InfoSummary;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            if (ledger.TargetVersion == OpenApiSpecVersion.OpenApi3_0)
                ledger.Add(OmittedMetadata("info.summary", "#/info/summary", ExtractionDiagnosticCodes.DocumentSummaryOmitted,
                    "info.summary is omitted (requires 3.1).", OpenApiSpecVersion.OpenApi3_1));
            else
                info.Summary = summary;
        }

        // Terms of Service: the option, then Program.cs.
        var (termsOfService, termsOfServiceSource) = options.TermsOfService != null
            ? (options.TermsOfService, "--terms-of-service")
            : (Given(docTagsResult.TermsOfService), "OpenApiInfo.TermsOfService in Program.cs");
        if (termsOfService != null)
        {
            if (Uri.TryCreate(termsOfService, UriKind.Absolute, out var tosUri))
                info.TermsOfService = tosUri;
            else
                WarnInvalidInfoUri(diagnostics, termsOfServiceSource, termsOfService, "#/info/termsOfService");
        }

        var document = new OpenApiDocument
        {
            Info  = info,
            Paths = new OpenApiPaths(),
        };

        // Written verbatim: the serializer writes $self with Uri.ToString(), which unescapes it.
        if (options.SelfUrl != null)
            document.Self = new VerbatimUri(options.SelfUrl, UriKind.RelativeOrAbsolute);

        // jsonSchemaDialect exists from 3.1: a 3.0 document omits an accepted dialect, with a warning.
        if (options.JsonSchemaDialect != null)
        {
            var dialect = DocumentMetadata.DialectFor(ledger.TargetVersion, options.JsonSchemaDialect);
            if (ledger.TargetVersion == OpenApiSpecVersion.OpenApi3_0)
                ledger.Add(OmittedMetadata("jsonSchemaDialect", "#/jsonSchemaDialect", ExtractionDiagnosticCodes.DocumentJsonSchemaDialectOmitted,
                    "jsonSchemaDialect is omitted (requires 3.1); OpenAPI 3.0 schemas follow its own Schema Object.", OpenApiSpecVersion.OpenApi3_1));
            else
                document.JsonSchemaDialect = dialect;
        }

        // Servers, with their names by position (a blank server is dropped with its name).
        var servers = new List<OpenApiServer>();
        for (var i = 0; options.Servers != null && i < options.Servers.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(options.Servers[i]))
                continue;
            servers.Add(new OpenApiServer
            {
                Url  = options.Servers[i],
                Name = options.ServerNames is { Count: > 0 } names ? names[i] : null,
            });
        }

        if (servers.Count > 0)
            document.Servers = servers;

        // ── Step 3: Build paths and operations ──────────────────────────────
        // Also collect (action, actionAttrs, controllerAttrs, operation) tuples for post-processing.
        var builtOperations = new List<(ActionInfo Action,
            IList<System.Reflection.CustomAttributeData> ActionAttrs,
            IList<System.Reflection.CustomAttributeData> ControllerAttrs,
            OpenApiOperation Operation)>();

        // One operation per path and method: the winner by the ordinal key, independent of
        // discovery order; a conflict is reported once, naming every action involved.
        foreach (var group in OperationConflicts.Group(actions))
        {
            var action = group.Winner;
            var path = group.Path;

            // Standard methods map to the shared HttpMethod instances; any other method from
            // [AcceptVerbs] keeps its literal (the capitalization that goes into the request).
            var httpMethod = HttpMethod.Parse(action.HttpMethod);

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

                var operation = BuildOperation(action, actionAttrs, controllerAttrs, docResolver, schemaGenerator, securityResult, document, ledger, httpSlots);
                ApplyApiVersionExtension(operation, actionAttrs, controllerAttrs);
                ApplyRateLimitingAndCaching(operation, actionAttrs, controllerAttrs);
                pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
                pathItem.Operations[httpMethod] = operation;
                builtOperations.Add((action, actionAttrs, controllerAttrs, operation));
                RecordRequestBodyOnGetHeadDelete(ledger, httpMethod, operation);
                RecordPathMethodConflict(ledger, group, operation);
            }
            catch (Exception ex) when (ex is FileNotFoundException
                                        or FileLoadException
                                        or TypeLoadException
                                        or BadImageFormatException)
            {
                // Skip operations whose types have unresolvable dependencies
            }
        }

        // ── Step 3a: Schemas of the HTTP serialization context ──────────────
        var httpGenerator = schemaGenerator;
        if (httpSlots.Count > 0 && contextsDiffer)
        {
            httpGenerator = SchemaGenerator.ForHttpContext(new SchemaOptions
            {
                NamingPolicy             = httpJson.NamingPolicy,
                EnumAsString             = options.EnumAsString,
                DictionaryKeyPolicy      = httpJson.DictionaryKeyPolicy,
                DefaultIgnoreCondition   = jsonOptions.Http.DefaultIgnoreCondition,
                NumberHandling           = jsonOptions.Http.NumberHandling,
                GlobalConverterTypeNames = jsonOptions.Http.GlobalConverterTypeNames,
                GlobalConverterEnumNamingPolicies = jsonOptions.Http.GlobalConverterEnumNamingPolicies,
                EnumAutoDescription      = options.EnumAutoDescription,
                EnumVarnames             = options.EnumVarnames,
                OpenApiVersion           = options.OpenApiVersion,
                OnDiagnostic             = diagnostics.Report,
            }, docResolver, schemaGenerator);
        }

        foreach (var fill in httpSlots)
            fill(httpGenerator);

        if (!ReferenceEquals(httpGenerator, schemaGenerator))
            RecordSharedContextTypes(ledger, schemaGenerator, httpGenerator);

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
        // The generators' Schemas are populated as a side-effect of building operations above;
        // each context names properties by its own policy.
        var componentSources = ReferenceEquals(httpGenerator, schemaGenerator)
            ? new[] { (Generator: schemaGenerator, Naming: effectiveNamingPolicy) }
            : [(schemaGenerator, effectiveNamingPolicy), (httpGenerator, httpJson.NamingPolicy)];
        if (componentSources.Any(c => c.Generator.Schemas.Count > 0))
        {
            document.Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
            };

            foreach (var (generator, namingPolicy) in componentSources)
            foreach (var (id, schema) in generator.Schemas)
            {
                // Apply type-level description from documentation sources
                if (generator.SchemaTypes.TryGetValue(id, out var schemaType))
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
                            var serialized = ResolvePropertyName(p, namingPolicy);
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
                                     && (string.IsNullOrEmpty(inlineProp.Description)
                                         || inlineProp.Description == DocumentationResolver.AttributeDescription(
                                             AttributeHelper.GetMergedPropertyAttributes(prop))))
                            {
                                // An empty description, or the one the attributes gave: the XML
                                // <summary> replaces it (ResolveProperty puts XML first). A default
                                // of a converter hint or an enum stays when there is no attribute.
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
                // [SwaggerTag(description, externalDocsUrl)]; an AddTag(...) in Program.cs fills only what is left.
                ExternalDocs = Uri.TryCreate(controller.TagExternalDocsUrl, UriKind.Absolute, out var tagDocsUrl)
                    ? new OpenApiExternalDocs { Url = tagDocsUrl }
                    : null,
            });
        }

        // ── Step 6: PathBase ─────────────────────────────────────────────────
        var pathBase = PathBaseExtractor.ExtractPathBase(sourceContext, diagnostics.Report);
        if (!string.IsNullOrEmpty(pathBase))
            ApplyPathBase(document, pathBase, options.PathBaseEmission);

        // ── Step 7: Security schemes ─────────────────────────────────────────
        ApplySecuritySchemes(document, securityResult);
        RecordOmittedSecuritySchemes(securityResult, ledger);
        var removedMutualTls = RemoveMutualTlsBefore31(document, ledger);
        OmitUndeclaredSecuritySchemes(document, diagnostics,
            [.. securityResult.OmittedSchemes, .. securityResult.SchemesWithInvalidUri.Keys, .. removedMutualTls]);

        // ── Step 8: ProblemDetails ──────────────────────────────────────────
        if (ProblemDetailsDetector.IsRegistered(sourceContext))
            ApplyProblemDetails(document);

        // ── Step 9: Global response headers ─────────────────────────────────
        var responseHeaders = ResponseHeaderExtractor.Extract(sourceContext, diagnostics.Report);
        ApplyGlobalResponseHeaders(document, responseHeaders);

        // ── Step 10: Global media types ──────────────────────────────────────
        var globalMediaTypes = GlobalMediaTypesExtractor.Extract(sourceContext, diagnostics.Report);
        ApplyGlobalMediaTypes(builtOperations, globalMediaTypes, schemaGenerator, ledger);

        // ── Step 11: Document-level tags metadata (descriptions + externalDocs) ─
        ApplyDocumentTagsMetadata(document, docTagsResult);

        // ── Step 12: Deliver pending warnings against the finished document ──
        DownlevelPass.Run(document, ledger, diagnostics);

        var schemaTypes = new Dictionary<string, Type>(schemaGenerator.SchemaTypes, StringComparer.Ordinal);
        if (!ReferenceEquals(httpGenerator, schemaGenerator))
        {
            foreach (var (id, type) in httpGenerator.SchemaTypes)
                schemaTypes.TryAdd(id, type);
        }

        return new BuildCoreResult(document, controllers, actions, schemaTypes, sourceContext);
    }

    /// <summary>
    /// OpenAPI 3.0 says GET, HEAD and DELETE request bodies have no defined semantics, so 3.0
    /// consumers ignore them. The body stays in the output; the warning names the operation.
    /// </summary>
    private static void RecordRequestBodyOnGetHeadDelete(
        LossLedger ledger, HttpMethod method, OpenApiOperation operation)
    {
        if (ledger.TargetVersion != OpenApiSpecVersion.OpenApi3_0 || operation.RequestBody == null)
            return;

        if (method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Delete)
            return;

        ledger.Add(new PendingLoss
        {
            Class           = LossClass.Source,
            Code            = ExtractionDiagnosticCodes.RequestBodyOnGetHeadDelete,
            Anchor          = new LossAnchor.Operation(operation),
            Message         = $"requestBody on {method.Method.ToUpperInvariant()} is not supported by OpenAPI 3.0 " +
                              "(only methods whose HTTP semantics define a body); 3.0 consumers must ignore it. The body is kept.",
            Feature         = "requestBody",
            Action          = DiagnosticAction.SemanticsChanged,
            RequiredVersion = OpenApiSpecVersion.OpenApi3_1,
        });
    }

    /// <summary>
    /// Several actions declare one path and method: the document keeps the winner by the ordinal
    /// key, and one warning names every action involved. Anchored on the kept operation, so a
    /// conflict on an excluded path is not reported.
    /// </summary>
    private static void RecordPathMethodConflict(LossLedger ledger, OperationGroup group, OpenApiOperation operation)
    {
        if (group.Others.Count == 0)
            return;

        var names = group.All.Select(OperationConflicts.DisplayName).ToList();
        ledger.Add(new PendingLoss
        {
            Class    = LossClass.Source,
            Code     = ExtractionDiagnosticCodes.OperationPathMethodConflict,
            Anchor   = new LossAnchor.Operation(operation),
            Message  = $"declared by {names.Count} actions ({string.Join(", ", names)}); the document uses " +
                       $"{names[0]} (ordinal key: controller type, method name, parameter types, attribute order).",
            Feature  = "operation",
            Subjects = names,
        });
    }

    /// <summary>Reports a document-metadata option whose value is not an absolute URI and is ignored.</summary>
    private static void WarnInvalidInfoUri(DiagnosticBag diagnostics, string flag, string value, string location)
        => diagnostics.Report(new ExtractionDiagnostic
        {
            Code     = ExtractionDiagnosticCodes.InfoInvalidUri,
            Message  = $"{flag} '{value}' is not a valid absolute URI and will be ignored.",
            Location = location,
            Subjects = [value],
        });

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
    private static IReadOnlyList<string> BuildXmlPathList(
        OpenApiDocumentOptions options, AssemblyLoader loader, DiagnosticBag diagnostics)
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
            diagnostics.Report(new ExtractionDiagnostic
            {
                Code     = ExtractionDiagnosticCodes.FrameworkXmlDocsMissing,
                Message  = "framework XML documentation not found in SDK ref packs at: " +
                           string.Join(", ", missingHints) +
                           "; descriptions for framework types (e.g. ProblemDetails) will be empty. " +
                           "Ref packs ship with the .NET SDK — install the SDK rather than only the runtime " +
                           "(e.g. base your Docker image on mcr.microsoft.com/dotnet/sdk:N.0 instead of aspnet:N.0).",
                Subjects = missingHints.ToList(),
            });
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
        SecuritySchemeExtractionResult securityResult,
        OpenApiDocument document,
        LossLedger ledger,
        List<Action<SchemaGenerator>> httpSlots)
    {
        var docs = docResolver.ResolveOperation(action);
        var parameters = ParameterExtractor.ExtractParameters(action);
        var responses = ResponseExtractor.ExtractResponses(action, out var unknownResults);

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
        // Defaults that do not convert: one warning for the operation, naming every parameter.
        var unconvertedDefaults = new List<(string Name, string Error)>();
        foreach (var param in parameters)
        {
            // Body and form parameters are handled separately as requestBody.
            if (param.Location is OurParameterLocation.Body or OurParameterLocation.Form)
                continue;

            // Bound by model binding (type converters), not by the JSON serializer.
            var paramSchema = schemaGenerator.GenerateBoundValueSchema(param.Type);

            // Validation attributes of the parameter, as on a DTO property; the parameter is the
            // next entry of the operation's list.
            var parameterAnchor = new LossAnchor.Node(new LossAnchor.Operation(operation),
                ["parameters", (operation.Parameters?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            var reflectionParameter = param.ReflectionParameter;
            paramSchema = schemaGenerator.ApplyParameterValidation(
                paramSchema, reflectionParameter.GetCustomAttributesData(), param.Type,
                reflectionParameter.Member.DeclaringType?.FullName ?? string.Empty,
                $"{reflectionParameter.Member.Name}({reflectionParameter.Name})", parameterAnchor);

            if (param.PathDeclaredOptional)
            {
                ledger.Add(new PendingLoss
                {
                    Class    = LossClass.Source,
                    Code     = ExtractionDiagnosticCodes.ParameterPathRequiredKept,
                    Anchor   = parameterAnchor,
                    Message  = $"Path parameter {param.Name} is declared [SwaggerParameter(Required = false)]; " +
                               "OpenAPI requires every path parameter, so it stays required.",
                    Feature  = "parameter.required",
                    Action   = DiagnosticAction.Omitted,
                    Subjects = [param.Name],
                });
            }

            var openApiIn = param.Location switch
            {
                OurParameterLocation.Path   => OpenApiParameterLocation.Path,
                OurParameterLocation.Query  => OpenApiParameterLocation.Query,
                OurParameterLocation.Header => OpenApiParameterLocation.Header,
                _                           => OpenApiParameterLocation.Query,
            };

            // Write schema.Default when the parameter has a default value, through the converter
            // shared with DTO properties. Only a mutable OpenApiSchema (not a $ref) carries Default.
            if (paramSchema is OpenApiSchema mutableParamSchema)
            {
                if (param.DefaultValueAttribute != null)
                {
                    var converted = DefaultValueConverter.FromAttribute(param.DefaultValueAttribute, mutableParamSchema.Type);
                    if (converted.HasValue)
                        mutableParamSchema.Default = converted.Value;
                    else if (converted.Error != null)
                        unconvertedDefaults.Add((param.Name, converted.Error));
                }
                else if (param.DefaultValue is not null)
                {
                    // An enum parameter's C# default is its raw value: written in the enum schema's form.
                    var enumType = param.Type.IsEnum ? param.Type
                        : param.Type.IsGenericType && param.Type.GetGenericTypeDefinition().FullName == "System.Nullable`1"
                          && param.Type.GetGenericArguments()[0].IsEnum ? param.Type.GetGenericArguments()[0]
                        : null;
                    if (enumType != null)
                    {
                        var converted = DefaultValueConverter.FromEnumDefault(enumType, param.DefaultValue, mutableParamSchema.Type);
                        if (converted.HasValue)
                            mutableParamSchema.Default = converted.Value;
                    }
                    else
                    {
                        mutableParamSchema.Default = DefaultValueConverter.FromLiteral(param.DefaultValue);
                    }
                }
            }

            // XML <param> is keyed by the C# name, also when Name= renames the parameter.
            var cSharpName = param.ReflectionParameter.Name ?? param.Name;
            var openApiParam = new OpenApiParameter
            {
                Name = param.Name,
                In = openApiIn,
                Required = param.IsRequired,
                Schema = paramSchema,
                Description = param.Description
                    ?? docs.ParameterDescriptions.GetValueOrDefault(cSharpName),
            };

            operation.Parameters ??= new List<IOpenApiParameter>();
            if (docs.ParameterExamples.TryGetValue(cSharpName, out var paramExample))
            {
                if (schemaGenerator.TryParseExample(paramSchema, paramExample, out var exampleValue))
                    openApiParam.Example = exampleValue;
                else
                    RecordUnparsableParameterExample(ledger, parameterAnchor, $"parameter {param.Name}", [param.Name, paramExample]);
            }

            operation.Parameters.Add(openApiParam);
        }

        if (unconvertedDefaults.Count > 0)
        {
            ledger.Add(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SchemaDefaultNotConvertible,
                Anchor   = new LossAnchor.Operation(operation),
                Message  = "[DefaultValue] on " +
                           string.Join("; ", unconvertedDefaults.Select(d => $"parameter {d.Name}: {d.Error}")) +
                           "; no default is written for them.",
                Feature  = "parameter.schema.default",
                Action   = DiagnosticAction.Omitted,
                Subjects = unconvertedDefaults.Select(d => d.Name).ToList(),
            });
        }

        // ── Request body ─────────────────────────────────────────────────────
        var bodyParam = parameters.FirstOrDefault(
            p => p.Location == OurParameterLocation.Body);

        var formParams = parameters
            .Where(p => p.Location == OurParameterLocation.Form)
            .ToList();

        // [Consumes] on the action, else on the controller; global filters are applied later.
        var consumes = ResolveConsumesContentTypes(actionAttrs) ?? ResolveConsumesContentTypes(controllerAttrs);

        if (bodyParam != null)
        {
            var bodySchema = schemaGenerator.GenerateSchema(bodyParam.Type);

            // Validation attributes on the [FromBody] parameter itself constrain the body; a
            // reference is wrapped in allOf, the shared component is left as it is.
            var bodyReflection = bodyParam.ReflectionParameter;
            bodySchema = schemaGenerator.ApplyParameterValidation(
                bodySchema, bodyReflection.GetCustomAttributesData(), bodyParam.Type,
                bodyReflection.Member.DeclaringType?.FullName ?? string.Empty,
                $"{bodyReflection.Member.Name}({bodyReflection.Name})",
                new LossAnchor.Node(new LossAnchor.Operation(operation), ["requestBody"]), jsonBody: true);

            // <param example> of a body is the example of the request body's media types.
            JsonNode? bodyExample = null;
            var bodyName = bodyParam.ReflectionParameter.Name ?? bodyParam.Name;
            if (docs.ParameterExamples.TryGetValue(bodyName, out var bodyExampleText)
                && !schemaGenerator.TryParseExample(bodySchema, bodyExampleText, out bodyExample))
                RecordUnparsableParameterExample(ledger, new LossAnchor.Node(new LossAnchor.Operation(operation), ["requestBody"]),
                    $"body parameter {bodyName}", [bodyName, bodyExampleText]);

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = bodyParam.IsRequired,
                Description = bodyParam.Description,
                Content = RequestContent(consumes ?? ["application/json"], bodySchema, bodyExample),
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

            // The form's example: an object of the fields that have an example, by their names in the form.
            var formExample = new JsonObject();
            var unparsableFields = new List<string>();
            var requiredFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fp in formParams)
            {
                // A field is bound by model binding; its validation attributes constrain its schema.
                var fieldReflection = fp.ReflectionParameter;
                var fpSchema = schemaGenerator.ApplyParameterValidation(
                    schemaGenerator.GenerateBoundValueSchema(fp.Type), fieldReflection.GetCustomAttributesData(), fp.Type,
                    fieldReflection.Member.DeclaringType?.FullName ?? string.Empty,
                    $"{fieldReflection.Member.Name}({fieldReflection.Name})",
                    // Located at the media type the finished document has (a global [Consumes] may replace it).
                    new LossAnchor.Node(new LossAnchor.RequestBodyContent(operation), ["schema", "properties", fp.Name]));
                formSchema.Properties![fp.Name] = fpSchema;
                if (fp.IsRequired)
                    requiredFields.Add(fp.Name);

                if (docs.ParameterExamples.TryGetValue(fp.ReflectionParameter.Name ?? fp.Name, out var fieldExample))
                {
                    if (schemaGenerator.TryParseExample(fpSchema, fieldExample, out var fieldValue))
                        formExample[fp.Name] = JsonNullSentinel.IsJsonNullSentinel(fieldValue) ? null : fieldValue; // the sentinel is shared and cannot get a parent
                    else
                        unparsableFields.AddRange([fp.Name, fieldExample]);
                }
            }

            if (requiredFields.Count > 0)
                formSchema.Required = requiredFields;

            if (unparsableFields.Count > 0)
                RecordUnparsableParameterExample(ledger, new LossAnchor.Node(new LossAnchor.Operation(operation), ["requestBody"]),
                    "form field " + string.Join(", ", unparsableFields.Where((_, i) => i % 2 == 0)), unparsableFields);

            operation.RequestBody = new OpenApiRequestBody
            {
                Content = RequestContent(consumes ?? ["multipart/form-data"], formSchema, formExample.Count > 0 ? formExample : null),
            };
        }

        // ── Responses ────────────────────────────────────────────────────────
        operation.Responses = new OpenApiResponses();

        foreach (var resp in responses)
        {
            var statusKey = resp.StatusCode == ResponseExtractor.DefaultStatusCode
                ? "default"
                : resp.StatusCode.ToString();

            // The resolved descriptions keep the action's sources (attributes, then XML) ahead of
            // the controller's; ResponseInfo.Description may already hold a controller value.
            var description = docs.ResponseDescriptions.GetValueOrDefault(statusKey)
                ?? resp.Description
                ?? GetDefaultStatusDescription(resp.StatusCode);

            var apiResponse = new OpenApiResponse { Description = description };


            if (resp.ContentTypes.Count > 0 && (resp.BodyType != null || resp.ContentTypesExplicit))
            {
                // Emit a Content section when there is a typed body, or when the content types
                // were declared explicitly via [Produces] (e.g. text/event-stream with no body).
                apiResponse.Content = BuildResponseContent(
                    resp.ContentTypes, resp.BodyType, schemaGenerator, ledger, operation, httpSlots,
                    httpContext: resp.BodyFromHttpResult, elementNullable: resp.SequenceElementNullable);
            }

            operation.Responses[statusKey] = apiResponse;
        }

        if (unknownResults.Count > 0)
            RecordUnknownResultStatus(ledger, operation, action.Method.ReturnType, unknownResults);

        // ── Per-operation security ────────────────────────────────────────────
        ApplyOperationSecurity(operation, actionAttrs, controllerAttrs, securityResult, document, ledger);

        return operation;
    }

    // =========================================================================
    // Request body media types
    // =========================================================================

    /// <summary>
    /// The media types of the first <c>[Consumes]</c> in <paramref name="attributes"/>, from both
    /// constructors (<c>(string, params string[])</c> and <c>(Type, string, params string[])</c>);
    /// <see langword="null"/> when there is none or it names no media type.
    /// </summary>
    private static IReadOnlyList<string>? ResolveConsumesContentTypes(IList<System.Reflection.CustomAttributeData> attributes)
    {
        var consumes = AttributeHelper.GetAttribute(attributes, AttributeHelper.Names.Consumes);
        if (consumes == null)
            return null;

        var result = new List<string>();
        foreach (var argument in consumes.ConstructorArguments)
        {
            switch (argument.Value)
            {
                case string single when !string.IsNullOrWhiteSpace(single):
                    result.Add(single);
                    break;
                case IReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument> many:
                    result.AddRange(many.Select(m => m.Value).OfType<string>().Where(m => !string.IsNullOrWhiteSpace(m)));
                    break;
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>One media type entry per content type, all with <paramref name="schema"/>.</summary>
    private static Dictionary<string, IOpenApiMediaType> RequestContent(
        IEnumerable<string> contentTypes, IOpenApiSchema schema, JsonNode? example = null)
    {
        var content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal);
        foreach (var contentType in contentTypes)
            content[contentType] = new OpenApiMediaType { Schema = schema, Example = example };
        return content;
    }

    /// <summary>
    /// One warning (class «source») for XML parameter examples that do not parse as values of their
    /// schemas: <paramref name="subjects"/> names each element with its example text.
    /// </summary>
    private static void RecordUnparsableParameterExample(
        LossLedger ledger, LossAnchor anchor, string elements, IReadOnlyList<string> subjects) =>
        ledger.Add(new PendingLoss
        {
            Class    = LossClass.Source,
            Code     = ExtractionDiagnosticCodes.ParameterExampleNotParsable,
            Anchor   = anchor,
            Message  = $"The XML example of {elements} is not a value of its schema: no example is written for it.",
            Feature  = "parameter.example",
            Action   = DiagnosticAction.Omitted,
            Subjects = subjects,
        });

    // =========================================================================
    // Response content by media type
    // =========================================================================

    /// <summary>
    /// One warning on the operation naming the results whose status is not statically known (the
    /// action's <c>IResult</c> itself or variants of its <c>Results&lt;…&gt;</c>): their responses are
    /// missing from the document; when no response is known at all, a 200 without a schema stands in.
    /// </summary>
    private static void RecordUnknownResultStatus(
        LossLedger ledger, OpenApiOperation operation, Type returnType, IReadOnlyList<Type> unknownResults)
    {
        var names = unknownResults.Select(TypeDisplayName).Distinct(StringComparer.Ordinal).ToList();
        ledger.Add(new PendingLoss
        {
            Class    = LossClass.Source,
            Code     = ExtractionDiagnosticCodes.ResponseResultStatusUnknown,
            Anchor   = new LossAnchor.Operation(operation),
            Message  = $"the action returns {TypeDisplayName(returnType)}; the status code and body of " +
                       $"{string.Join(", ", names)} are not statically known, so their responses are not described " +
                       "(without any known response: a 200 response without a schema). Use typed results with a " +
                       "fixed status or declare [ProducesResponseType].",
            Feature  = "responses",
            Action   = DiagnosticAction.Omitted,
            Subjects = names,
        });
    }

    /// <summary>Sequential media types whose items are JSON texts (spec 3.2 §4.14.3.1, as .NET formatters produce them).</summary>
    private static readonly HashSet<string> SequentialJsonMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/jsonl",
        "application/x-ndjson",
        "application/json-seq",
    };

    private const string EventStreamMediaType = "text/event-stream";

    /// <summary>The media type without parameters (<c>application/x-ndjson; charset=utf-8</c> → <c>application/x-ndjson</c>).</summary>
    private static string BaseMediaType(string contentType)
    {
        var separator = contentType.IndexOf(';');
        return (separator < 0 ? contentType : contentType[..separator]).Trim();
    }

    /// <summary>
    /// The content of one response, chosen per media type. <c>ServerSentEventsResult&lt;T&gt;</c> gets
    /// <c>text/event-stream</c> with the event schema as <c>itemSchema</c>, filled in later through
    /// <paramref name="httpSlots"/>. When the body type is an asynchronous
    /// sequence (<c>IAsyncEnumerable&lt;T&gt;</c>): a sequential JSON media type gets
    /// <c>itemSchema: T</c> and no <c>schema</c>; <c>text/event-stream</c> gets no schema, because
    /// standard MVC has no formatter for it (warning on the operation); any other media type gets
    /// the body schema (an array of <c>T</c>). Other body types get the body schema everywhere.
    /// </summary>
    private static Dictionary<string, IOpenApiMediaType> BuildResponseContent(
        IEnumerable<string> contentTypes,
        Type? bodyType,
        SchemaGenerator schemaGenerator,
        LossLedger ledger,
        OpenApiOperation operation,
        List<Action<SchemaGenerator>>? httpSlots,
        bool httpContext = false,
        bool elementNullable = false)
    {
        var content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal);

        // Bodies written by a typed result are described in the HTTP context, whose schemas are
        // generated after the operation loop; MVC bodies right away.
        void WithGenerator(Action<SchemaGenerator> fill)
        {
            if (httpContext)
                httpSlots!.Add(fill);
            else
                fill(schemaGenerator);
        }

        // ServerSentEventsResult<T> writes text/event-stream itself, whatever is declared; its
        // events are described by itemSchema, the data in the HTTP serialization context, which is
        // generated after the operation loop.
        if (bodyType != null && httpSlots != null && SseEventSchema.TryGetItemType(bodyType, out var sseItemType))
        {
            var eventStream = new OpenApiMediaType();
            content[EventStreamMediaType] = eventStream;
            httpSlots.Add(httpGenerator => eventStream.ItemSchema = SseEventSchema.Create(sseItemType, httpGenerator));
            return content;
        }

        Type? elementType = null;
        var isSequence = bodyType != null && StreamingTypes.TryGetAsyncEnumerableElementType(bodyType, out elementType);
        IOpenApiSchema? bodySchema = null;

        foreach (var contentType in contentTypes)
        {
            var mediaType = BaseMediaType(contentType);
            if (isSequence && SequentialJsonMediaTypes.Contains(mediaType))
            {
                var sequential = new OpenApiMediaType();
                content[contentType] = sequential;
                WithGenerator(generator => sequential.ItemSchema = ElementSchema(generator, elementType!, elementNullable));
            }
            else if (isSequence && string.Equals(mediaType, EventStreamMediaType, StringComparison.OrdinalIgnoreCase))
            {
                content[contentType] = new OpenApiMediaType();
                ledger.Add(new PendingLoss
                {
                    Class    = LossClass.Source,
                    Code     = ExtractionDiagnosticCodes.ResponseEventStreamWithoutFormatter,
                    Anchor   = new LossAnchor.Operation(operation),
                    Message  = httpContext
                        ? $"IAsyncEnumerable<{elementType!.Name}> response declared as {EventStreamMediaType}: " +
                          "a JSON result writes the sequence as a JSON array, not as server-sent events; " +
                          "ServerSentEventsResult<T> is needed. The media type is written without a schema."
                        : $"IAsyncEnumerable<{elementType!.Name}> response declared as {EventStreamMediaType}: " +
                          "standard MVC has no server-sent events output formatter; a custom formatter or " +
                          "ServerSentEventsResult<T> is needed. The media type is written without a schema.",
                    Feature  = "mediaType.schema",
                    Action   = DiagnosticAction.Omitted,
                    Subjects = [elementType.FullName ?? elementType.Name],
                });
            }
            else
            {
                var plain = new OpenApiMediaType();
                content[contentType] = plain;
                if (bodyType != null)
                {
                    WithGenerator(generator => plain.Schema = bodySchema ??= isSequence && elementNullable
                        ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = ElementSchema(generator, elementType!, nullable: true) }
                        : generator.GenerateSchema(bodyType));
                }
            }
        }

        return content;
    }

    /// <summary>The schema of a sequence element; nullable by the version's rules when annotated <c>T?</c>.</summary>
    private static IOpenApiSchema ElementSchema(SchemaGenerator generator, Type elementType, bool nullable)
    {
        var schema = generator.GenerateSchema(elementType);
        return nullable ? SchemaGenerator.MakeNullable(schema, generator.OpenApiVersion) : schema;
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

        // One Security Requirement Object per AddSecurityRequirement call: names within it
        // are combined (AND), separate objects in the array are alternatives (OR).
        foreach (var entries in securityResult.GlobalRequirementEntries)
        {
            var requirement = new OpenApiSecurityRequirement();
            foreach (var (schemeName, scopes) in entries)
            {
                // The host document is required for serialization: Microsoft.OpenApi writes a
                // requirement key only if its reference resolves against the host document's
                // components/securitySchemes (OpenApiSecurityRequirement.CanSerializeSecurityScheme);
                // with a null host document every key is dropped and the requirement becomes {}.
                var reference = new OpenApiSecuritySchemeReference(schemeName, document, null);

                // The values of an OAuth2 / OpenID Connect scheme are its scopes; other schemes keep [].
                var isOAuth = securityResult.Schemes.TryGetValue(schemeName, out var declared)
                              && declared.Type is SecuritySchemeType.OAuth2 or SecuritySchemeType.OpenIdConnect;
                requirement[reference] = isOAuth ? scopes.ToList() : [];
            }

            document.Security ??= new List<OpenApiSecurityRequirement>();
            document.Security.Add(requirement);
        }
    }

    /// <summary>
    /// For a 3.0 target, removes the <c>mutualTLS</c> schemes, which OpenAPI 3.0 cannot express (the
    /// serializer would throw). Before the removal one degradation warning per scheme records the change
    /// of the auth contract — every requirement that names it, simplified or gone — owned by the document
    /// and located at the removed scheme. The requirements themselves are cleaned afterwards by
    /// <see cref="OmitUndeclaredSecuritySchemes"/>; the names removed here are returned so that it does
    /// not report them a second time.
    /// </summary>
    private static IReadOnlyList<string> RemoveMutualTlsBefore31(OpenApiDocument document, LossLedger ledger)
    {
        if (ledger.TargetVersion != OpenApiSpecVersion.OpenApi3_0 || document.Components?.SecuritySchemes is not { } schemes)
            return [];

        var removed = schemes
            .Where(s => s.Value is OpenApiSecurityScheme { Type: SecuritySchemeType.MutualTLS })
            .Select(s => s.Key)
            .ToList();

        foreach (var name in removed)
        {
            var affected = new List<string>();
            void Describe(IList<OpenApiSecurityRequirement>? requirements, string where)
            {
                for (var i = 0; requirements != null && i < requirements.Count; i++)
                {
                    var names = requirements[i].Keys.Select(k => k.Reference.Id ?? string.Empty).ToList();
                    if (!names.Contains(name))
                        continue;
                    var rest = names.Where(n => n != name).ToList();
                    affected.Add($"{where}/{i}: {{{string.Join(", ", names)}}} → " +
                                 (rest.Count > 0 ? $"{{{string.Join(", ", rest)}}}" : "removed"));
                }
            }

            Describe(document.Security, "#/security");
            foreach (var (path, pathItemInterface) in document.Paths)
            {
                if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                    continue;
                foreach (var (method, operation) in pathItem.Operations)
                    Describe(operation.Security, $"{method.Method.ToUpperInvariant()} {path} security");
            }

            ledger.Add(new PendingLoss
            {
                Class           = LossClass.Degradation,
                Code            = ExtractionDiagnosticCodes.SecurityMutualTlsRemoved,
                Anchor          = LossAnchor.Document.Instance,
                Location        = $"#/components/securitySchemes/{Validation.JsonPointerHelper.EncodeSegment(name)}",
                Message         = $"mutualTLS security scheme '{name}' is omitted (requires 3.1), and the auth contract changes: " +
                                  (affected.Count == 0
                                      ? "no requirement names it."
                                      : string.Join("; ", affected) +
                                        " (a requirement that is removed no longer demands authentication through it)."),
                Feature         = "securityScheme.type",
                Action          = DiagnosticAction.SemanticsChanged,
                RequiredVersion = OpenApiSpecVersion.OpenApi3_1,
                Subjects        = [name, .. affected],
            });
            schemes.Remove(name);
        }

        return removed;
    }

    /// <summary>
    /// A degradation warning for document metadata a 3.0 target omits: owned by the document, located
    /// where the field would have been.
    /// </summary>
    private static PendingLoss OmittedMetadata(string feature, string location, string code, string message, OpenApiSpecVersion requiredVersion) => new()
    {
        Class           = LossClass.Degradation,
        Code            = code,
        Anchor          = LossAnchor.Document.Instance,
        Location        = location,
        Message         = message,
        Feature         = feature,
        Action          = DiagnosticAction.Omitted,
        RequiredVersion = requiredVersion,
    };

    /// <summary>
    /// One warning (class «source») per security scheme omitted because its declaration needs a value
    /// that cannot be resolved statically. Its owner is the document (the scheme is global and never
    /// reaches the output), so it is kept when every path is excluded; its location is the place the
    /// scheme would have had.
    /// </summary>
    private static void RecordOmittedSecuritySchemes(SecuritySchemeExtractionResult securityResult, LossLedger ledger)
    {
        foreach (var name in securityResult.OmittedSchemes)
        {
            var where = securityResult.DefinitionLocations.GetValueOrDefault(name);
            ledger.Add(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SecuritySchemeNotStatic,
                Anchor   = LossAnchor.Document.Instance,
                Location = $"#/components/securitySchemes/{Validation.JsonPointerHelper.EncodeSegment(name)}",
                SourceLocation = where,
                Message  = $"security scheme '{name}'{(where is null ? "" : $" ({where})")} needs a value that cannot be resolved statically " +
                           "(a variable, a call, configuration): the scheme is omitted, and every requirement that names it loses that scheme.",
                Feature  = "securityScheme",
                Action   = DiagnosticAction.Omitted,
                Subjects = [name],
            });
        }

        foreach (var (name, text) in securityResult.SchemesWithInvalidUri)
        {
            ledger.Add(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SecuritySchemeInvalidUri,
                Anchor   = LossAnchor.Document.Instance,
                Location = $"#/components/securitySchemes/{Validation.JsonPointerHelper.EncodeSegment(name)}",
                Message  = $"security scheme '{name}' has the URL \"{text}\", which is not a URI reference: the scheme is omitted, " +
                           "and every requirement that names it loses that scheme.",
                Feature  = "securityScheme.url",
                Action   = DiagnosticAction.Omitted,
                Subjects = [name, text],
            });
        }
    }

    /// <summary>
    /// Removes scheme names that are not declared in <c>components/securitySchemes</c> from
    /// the document-level and per-operation security requirements, with a warning per name.
    /// </summary>
    /// <remarks>
    /// OpenAPI requires every requirement name to correspond to a declared scheme, and the
    /// serializer drops undeclared names anyway — leaving <c>{}</c>, which (like <c>[]</c>)
    /// asserts anonymous access. A requirement left without names is removed; a
    /// <c>security</c> list left without requirements is unset, so an operation inherits the
    /// document-level requirement and the document claims no requirement at all. An explicit
    /// <c>security: []</c> from <c>[AllowAnonymous]</c> holds no requirements and is untouched.
    /// Must run after <see cref="ApplySecuritySchemes"/>, path exclusion and path base, so that
    /// declared schemes are final and warnings name the paths written to the spec.
    /// </remarks>
    private static void OmitUndeclaredSecuritySchemes(
        OpenApiDocument document, DiagnosticBag diagnostics, IReadOnlyList<string> reportedElsewhere)
    {
        var declared = document.Components?.SecuritySchemes;

        document.Security = OmitUndeclared(document.Security, declared, "document-level", "#/security", diagnostics, reportedElsewhere);

        foreach (var (path, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                continue;

            foreach (var (method, operation) in pathItem.Operations)
            {
                var operationKey = $"{method.Method.ToUpperInvariant()} {path}";
                operation.Security = OmitUndeclared(
                    operation.Security, declared, operationKey, operationKey, diagnostics, reportedElsewhere);
            }
        }
    }

    /// <summary>
    /// Returns <paramref name="requirements"/> without the scheme names missing from
    /// <paramref name="declared"/>, or <see langword="null"/> when no requirement is left.
    /// An empty or <see langword="null"/> input is returned unchanged.
    /// </summary>
    private static IList<OpenApiSecurityRequirement>? OmitUndeclared(
        IList<OpenApiSecurityRequirement>? requirements,
        IDictionary<string, IOpenApiSecurityScheme>? declared,
        string location,
        string diagnosticLocation,
        DiagnosticBag diagnostics,
        IReadOnlyList<string> reportedElsewhere)
    {
        if (requirements is not { Count: > 0 })
            return requirements;

        var kept = new List<OpenApiSecurityRequirement>();
        foreach (var requirement in requirements)
        {
            var undeclared = requirement.Keys
                .Where(reference => declared == null
                                    || reference.Reference.Id == null
                                    || !declared.ContainsKey(reference.Reference.Id))
                .ToList();

            foreach (var reference in undeclared)
            {
                // A declared scheme the extractor omitted has its own warning, naming the requirements.
                if (reference.Reference.Id is { } omittedName && reportedElsewhere.Contains(omittedName))
                {
                    requirement.Remove(reference);
                    continue;
                }

                diagnostics.Report(new ExtractionDiagnostic
                {
                    Code     = ExtractionDiagnosticCodes.SecurityRequirementUndeclaredScheme,
                    Message  = $"security requirement references undeclared scheme '{reference.Reference.Id}' " +
                               $"({location}) — omitted; declare it with AddSecurityDefinition.",
                    Location = diagnosticLocation,
                    Subjects = reference.Reference.Id is { } schemeName ? [schemeName] : [],
                });
                requirement.Remove(reference);
            }

            if (requirement.Count > 0)
                kept.Add(requirement);
        }

        return kept.Count > 0 ? kept : null;
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
        SecuritySchemeExtractionResult securityResult,
        OpenApiDocument document,
        LossLedger ledger)
    {
        var auth = AuthorizationExtractor.Extract(actionAttrs, controllerAttrs);

        if (auth.IsAnonymous)
        {
            // Empty security list overrides any global security requirement.
            operation.Security = [];
            return;
        }

        if (auth.RoleAlternatives is { Count: > 0 } roles)
        {
            ApplyRoleRequirements(operation, auth, roles, securityResult, document, ledger);
            return;
        }

        if (auth.AuthenticationSchemes is { Count: > 0 })
        {
            var requirement = new OpenApiSecurityRequirement();
            foreach (var schemeName in auth.AuthenticationSchemes)
            {
                // The host document is required for serialization: Microsoft.OpenApi writes a
                // requirement key only if its reference resolves against the host document's
                // components/securitySchemes (OpenApiSecurityRequirement.CanSerializeSecurityScheme);
                // with a null host document every key is dropped and the requirement becomes {}.
                var reference = new OpenApiSecuritySchemeReference(schemeName, document, null);
                requirement[reference] = [];
            }

            operation.Security = [requirement];
        }

        // If only RequiresAuthorization (no explicit schemes), we do nothing —
        // the operation inherits the global security requirement if one is set.
        // This avoids emitting a requirement with an unknown scheme name.
    }

    /// <summary>
    /// The requirements of an operation with <c>[Authorize(Roles)]</c>. The effective schemes are the
    /// attribute's explicit <c>AuthenticationSchemes</c> (one requirement) or, without them, the
    /// document's requirements, copied onto the operation. Every scheme requirement is combined with
    /// every role alternative (both lists are OR, the product is written in full): a non-OAuth scheme
    /// carries the alternative's roles (3.1+), an OAuth2 / OpenID Connect scheme keeps its scopes.
    /// Roles the document cannot carry are reported once per operation and case; without any
    /// requirement the roles are reported and no scheme is invented.
    /// </summary>
    private static void ApplyRoleRequirements(
        OpenApiOperation operation,
        AuthorizationInfo auth,
        IReadOnlyList<IReadOnlyList<string>> roleAlternatives,
        SecuritySchemeExtractionResult securityResult,
        OpenApiDocument document,
        LossLedger ledger)
    {
        var allRoles = roleAlternatives.SelectMany(r => r).Distinct(StringComparer.Ordinal).ToList();
        IReadOnlyList<IReadOnlyList<SecurityRequirementEntry>> schemeRequirements = auth.AuthenticationSchemes is { Count: > 0 } explicitSchemes
            ? [explicitSchemes.Select(name => new SecurityRequirementEntry(name, [])).ToList()]
            : securityResult.GlobalRequirementEntries;

        if (schemeRequirements.Count == 0)
        {
            ledger.Add(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SecurityRolesWithoutRequirement,
                Anchor   = new LossAnchor.Operation(operation),
                Message  = $"[Authorize(Roles)] requires {string.Join(", ", allRoles)}, but the operation has no security " +
                           "requirement (no AuthenticationSchemes, none in the document): the roles are not written.",
                Feature  = "security.roles",
                Action   = DiagnosticAction.Omitted,
                Subjects = allRoles,
            });
            return;
        }

        bool IsOAuth(string name) => securityResult.Schemes.TryGetValue(name, out var scheme)
                                     && scheme.Type is SecuritySchemeType.OAuth2 or SecuritySchemeType.OpenIdConnect;
        var writesRoles = ledger.TargetVersion != OpenApiSpecVersion.OpenApi3_0;

        var requirements = new List<OpenApiSecurityRequirement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schemeRequirement in schemeRequirements)
        foreach (var roles in roleAlternatives)
        {
            var values = schemeRequirement
                .Select(entry => (entry.SchemeName, Values: IsOAuth(entry.SchemeName) ? entry.Scopes : writesRoles ? roles : []))
                .ToList();

            // Without roles in the values (3.0, OAuth schemes) alternatives repeat: written once.
            var key = string.Join("\u0001", values.Select(v => v.SchemeName + "\u0000" + string.Join("\u0000", v.Values)));
            if (!seen.Add(key))
                continue;

            var requirement = new OpenApiSecurityRequirement();
            foreach (var (schemeName, scheme) in values)
                requirement[new OpenApiSecuritySchemeReference(schemeName, document, null)] = scheme.ToList();
            requirements.Add(requirement);
        }

        operation.Security = requirements;

        var schemes = schemeRequirements.SelectMany(r => r).Select(e => e.SchemeName).Distinct(StringComparer.Ordinal).ToList();
        var oauth = schemes.Where(IsOAuth).ToList();
        var other = schemes.Where(s => !IsOAuth(s)).ToList();
        if (!writesRoles && other.Count > 0)
        {
            ledger.Add(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SecurityRolesNotWritten,
                Anchor   = new LossAnchor.Operation(operation),
                Message  = $"roles {string.Join(", ", allRoles)} of [Authorize(Roles)] are not written for {string.Join(", ", other)}: " +
                           "OpenAPI 3.0 requires empty values for schemes other than oauth2 and openIdConnect (roles need 3.1).",
                Feature  = "security.roles",
                Action   = DiagnosticAction.Omitted,
                RequiredVersion = OpenApiSpecVersion.OpenApi3_1,
                Subjects = [.. other, .. allRoles],
            });
        }

        if (oauth.Count > 0)
        {
            ledger.Add(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SecurityRolesNotWritten,
                Anchor   = new LossAnchor.Operation(operation),
                Message  = $"roles {string.Join(", ", allRoles)} of [Authorize(Roles)] are not written for {string.Join(", ", oauth)}: " +
                           "the values of oauth2 and openIdConnect schemes are scopes.",
                Feature  = "security.roles.oauth",
                Action   = DiagnosticAction.Omitted,
                Subjects = [.. oauth, .. allRoles],
            });
        }
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

                // OpenAPI 3.2 tag fields; for 3.0/3.1 the serializer writes them as x-oas-* (warned by the model rules).
                if (string.IsNullOrEmpty(tag.Summary) && !string.IsNullOrEmpty(metadata.Summary))
                    tag.Summary = metadata.Summary;
                if (string.IsNullOrEmpty(tag.Kind) && !string.IsNullOrEmpty(metadata.Kind))
                    tag.Kind = metadata.Kind;
                if (tag.Parent == null && !string.IsNullOrEmpty(metadata.Parent))
                    tag.Parent = new OpenApiTagReference(metadata.Parent, document);

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
        GlobalMediaTypesExtractionResult globalMediaTypes,
        SchemaGenerator schemaGenerator,
        LossLedger ledger)
    {
        bool hasGlobalProduces = globalMediaTypes.ProducesContentTypes.Count > 0;
        bool hasGlobalConsumes = globalMediaTypes.ConsumesContentTypes.Count > 0;

        if (!hasGlobalProduces && !hasGlobalConsumes)
            return;

        foreach (var (action, actionAttrs, controllerAttrs, operation) in builtOperations)
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
                    // Body types per status, so each global media type gets its own form
                    // (an asynchronous sequence differs between JSON and sequential media types).
                    var extracted = ResponseExtractor.ExtractResponses(action)
                        .GroupBy(r => r.StatusCode == ResponseExtractor.DefaultStatusCode ? "default" : r.StatusCode.ToString())
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                    // Typed results and files write their own content type, and a response attribute
                    // that names media types of its own has the last word: the global [Produces]
                    // filter does not apply to them. (Per-action [Produces] skips the whole operation above.)
                    var ownContentType = extracted
                        .Where(e => e.Value.ContentTypesExplicit
                                    || e.Value.BodyFromHttpResult
                                    || (e.Value.BodyType != null
                                        && (SseEventSchema.TryGetItemType(e.Value.BodyType, out _) || FileTypes.IsFile(e.Value.BodyType))))
                        .Select(e => e.Key)
                        .ToHashSet(StringComparer.Ordinal);
                    var bodyTypes = extracted
                        .Where(e => e.Value.BodyType != null && !ownContentType.Contains(e.Key))
                        .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

                    foreach (var (statusKey, responseInterface) in operation.Responses)
                    {
                        if (responseInterface is not OpenApiResponse response)
                            continue;

                        // Only process responses that have a body (Content is non-null and non-empty).
                        if (response.Content is not { Count: > 0 })
                            continue;

                        // Replace the content entries with the global content types. A response
                        // whose body type is known is rebuilt per media type; otherwise (e.g. an
                        // injected ProblemDetails response) the schema of the first entry is kept.
                        if (ownContentType.Contains(statusKey))
                            continue;

                        if (bodyTypes.TryGetValue(statusKey, out var bodyResponse))
                        {
                            response.Content = BuildResponseContent(
                                globalMediaTypes.ProducesContentTypes, bodyResponse.BodyType, schemaGenerator, ledger, operation,
                                httpSlots: null, elementNullable: bodyResponse.SequenceElementNullable);
                            continue;
                        }

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
                    var first = operation.RequestBody.Content.Values.First();

                    operation.RequestBody.Content.Clear();
                    foreach (var ct in globalMediaTypes.ConsumesContentTypes)
                        operation.RequestBody.Content[ct] = new OpenApiMediaType { Schema = first.Schema, Example = first.Example };
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
        AssemblyLoader loader,
        DiagnosticBag diagnostics)
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

            // 2. Compile via Roslyn the files compiled into the assembly, as its PDB names them; without a
            //    PDB that matches the source root, every file under it.
            var compiledFiles = CompiledSourceFiles.TryFind(options.AssemblyPath, sourceRoot, SourceCompiler.EnumerateCsFiles(sourceRoot));
            var compilationResult = compiledFiles != null
                ? SourceCompiler.Compile(sourceRoot, compiledFiles)
                : SourceCompiler.Compile(sourceRoot);

            // 3. Locate entry-point syntax node.
            var candidates = EntryPointFinder.FindAll(loader.Assembly.EntryPoint, compilationResult.Compilation);
            var entryPointNode = ChooseEntryPoint(candidates, sourceRoot, diagnostics);

            return new SourceAnalysisContext(compilationResult, entryPointNode);
        }
        catch
        {
            // Any failure in source analysis must not break the main extraction pipeline.
            return SourceAnalysisContext.Empty;
        }
    }

    /// <summary>
    /// The entry point among <paramref name="candidates"/>. One is the entry point. Several happen only when
    /// the compiled files are unknown and the source root holds files the assembly was not built from (a
    /// <c>Program.Old.cs</c> copy, a file excluded with <c>&lt;Compile Remove&gt;</c>): the <c>Program.cs</c>
    /// nearest to the source root is taken, with a warning naming the candidates; when there is no such
    /// single file, none is taken — a guess could read the configuration of code the assembly does not
    /// contain.
    /// </summary>
    private static SyntaxNode? ChooseEntryPoint(IReadOnlyList<SyntaxNode> candidates, string sourceRoot, DiagnosticBag diagnostics)
    {
        if (candidates.Count <= 1)
            return candidates.FirstOrDefault();

        string Relative(SyntaxNode node) => Path.GetRelativePath(sourceRoot, node.SyntaxTree.FilePath).Replace('\\', '/');
        int Depth(SyntaxNode node) => Relative(node).Count(c => c == '/');

        var programs = candidates
            .Where(c => string.Equals(Path.GetFileName(c.SyntaxTree.FilePath), "Program.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var nearest = programs.Count == 0 ? [] : programs.Where(p => Depth(p) == programs.Min(Depth)).ToList();
        var chosen = nearest.Count == 1 ? nearest[0] : null;

        var names = candidates.Select(Relative).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        diagnostics.Report(new ExtractionDiagnostic
        {
            Code     = ExtractionDiagnosticCodes.SourceEntryPointAmbiguous,
            Message  = chosen != null
                ? $"Several source files can be the entry point ({string.Join(", ", names)}) and the portable PDB that names the files compiled into the assembly is missing or does not match the source root: {Relative(chosen)} is read. Remove copies that are not compiled, or keep the PDB next to the assembly."
                : $"Several source files can be the entry point ({string.Join(", ", names)}) and the portable PDB that names the files compiled into the assembly is missing or does not match the source root: none is read, Program.cs configuration is missing from the document. Remove copies that are not compiled, or keep the PDB next to the assembly.",
            Subjects = chosen != null ? [.. names, Relative(chosen)] : [.. names],
        });
        return chosen;
    }

}
