using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using static DotNetOpenApiExtract.Core.SourceAnalysis.TypeSyntaxHelper;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Metadata for a single document-level tag enrichment.
/// </summary>
public sealed record TagMetadata
{
    /// <summary>Tag description from <c>OpenApiTag.Description</c>.</summary>
    public string? Description { get; init; }

    /// <summary>ExternalDocs URL extracted from <c>OpenApiTag.ExternalDocs.Url</c>.</summary>
    public string? ExternalDocsUrl { get; init; }

    /// <summary>ExternalDocs description extracted from <c>OpenApiTag.ExternalDocs.Description</c>.</summary>
    public string? ExternalDocsDescription { get; init; }
}

/// <summary>
/// Result of scanning Roslyn source for document-level tag registrations and
/// root-level <c>externalDocs</c>.
/// </summary>
public sealed class DocumentTagsExtractionResult
{
    /// <summary>
    /// Tag name → enrichment metadata collected from <c>AddTag(new OpenApiTag {...})</c>
    /// and similar registrations.
    /// </summary>
    public IReadOnlyDictionary<string, TagMetadata> TagsByName { get; init; }
        = new Dictionary<string, TagMetadata>(StringComparer.Ordinal);

    /// <summary>
    /// Root-level <c>externalDocs.url</c> if detected in the source (e.g. from
    /// <c>SwaggerDoc("v1", new OpenApiInfo { ExternalDocs = ... })</c>).
    /// </summary>
    public string? ExternalDocsUrl { get; init; }

    /// <summary>Root-level <c>externalDocs.description</c>.</summary>
    public string? ExternalDocsDescription { get; init; }
}

/// <summary>
/// Scans a Roslyn <see cref="SourceAnalysisContext"/> for document-level tag metadata
/// (descriptions and externalDocs links) and root-level externalDocs registered via
/// <c>AddSwaggerGen(c =&gt; ...)</c> or <c>AddOpenApi(o =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// All analysis is purely syntactic. Complex patterns (e.g. variables, configuration
/// accessors) are skipped silently. Best-effort: if main <c>AddTag(...)</c> patterns are
/// parseable, the result is populated; otherwise an empty result is returned without
/// throwing.
/// </remarks>
public static class DocumentTagsExtractor
{
    private static readonly string[] SwaggerDocMethodNames = { "SwaggerDoc", "AddOpenApi" };

    /// <summary>
    /// Scans the Roslyn source context for document-level tag registrations and
    /// root-level externalDocs.
    /// Returns an empty result when <paramref name="context"/> is unavailable.
    /// </summary>
    /// <param name="context">The source analysis context built from the entry-point source.</param>
    /// <returns>
    /// A <see cref="DocumentTagsExtractionResult"/> with any tag enrichments and
    /// optional root-level externalDocs found.
    /// </returns>
    public static DocumentTagsExtractionResult Extract(SourceAnalysisContext context)
    {
        if (!context.IsAvailable || context.EntryPointNode == null)
            return new DocumentTagsExtractionResult();

        var tagsByName = new Dictionary<string, TagMetadata>(StringComparer.Ordinal);
        string? rootExternalDocsUrl = null;
        string? rootExternalDocsDesc = null;

        // ── 1. AddTag(new OpenApiTag { ... }) ─────────────────────────────────
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddTag"))
        {
            var metadata = TryParseAddTagInvocation(invocation);
            if (metadata?.Name is { } name && !string.IsNullOrWhiteSpace(name))
            {
                // First registration wins; subsequent duplicates are ignored.
                tagsByName.TryAdd(name, new TagMetadata
                {
                    Description = metadata.Description,
                    ExternalDocsUrl = metadata.ExternalDocsUrl,
                    ExternalDocsDescription = metadata.ExternalDocsDescription,
                });
            }
        }

        // ── 2. Root-level externalDocs from SwaggerDoc / AddOpenApi ───────────
        // Look for new OpenApiInfo { ExternalDocs = new OpenApiExternalDocs { ... } }
        // inside SwaggerDoc or AddOpenApi calls.
        foreach (var methodName in SwaggerDocMethodNames)
        {
            foreach (var invocation in InvocationMatcher.FindInvocations(context, methodName))
            {
                var (url, desc) = TryExtractRootExternalDocs(invocation);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    rootExternalDocsUrl ??= url;
                    rootExternalDocsDesc ??= desc;
                }
            }
        }

        return new DocumentTagsExtractionResult
        {
            TagsByName = tagsByName,
            ExternalDocsUrl = rootExternalDocsUrl,
            ExternalDocsDescription = rootExternalDocsDesc,
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AddTag parsing
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Intermediate holder for parsed tag properties (including the Name, which is
    /// needed to key the result dictionary).
    /// </summary>
    private sealed class ParsedTag
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ExternalDocsUrl { get; set; }
        public string? ExternalDocsDescription { get; set; }
    }

    /// <summary>
    /// Attempts to parse a <c>ParsedTag</c> from an <c>AddTag(new OpenApiTag { ... })</c>
    /// invocation. Returns null if the first argument is not a recognisable object-creation
    /// expression for <c>OpenApiTag</c>.
    /// </summary>
    private static ParsedTag? TryParseAddTagInvocation(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return null;

        var firstArg = args[0].Expression;

        // Strip parentheses defensively.
        while (firstArg is ParenthesizedExpressionSyntax paren)
            firstArg = paren.Expression;

        if (firstArg is not ObjectCreationExpressionSyntax objCreation)
            return null;

        // Loose type-name check.
        var typeName = GetUnqualifiedTypeName(objCreation.Type);
        if (!typeName.Contains("OpenApiTag", StringComparison.Ordinal) && !typeName.EndsWith("Tag", StringComparison.Ordinal))
            return null;

        return ParseOpenApiTagInitializer(objCreation.Initializer);
    }

    /// <summary>
    /// Parses properties from an <c>InitializerExpressionSyntax</c> for
    /// <c>OpenApiTag</c>. Returns best-effort results; unknown or complex values
    /// are silently skipped.
    /// </summary>
    private static ParsedTag? ParseOpenApiTagInitializer(InitializerExpressionSyntax? initializer)
    {
        if (initializer == null)
            return null;

        var tag = new ParsedTag();

        foreach (var expr in initializer.Expressions)
        {
            if (expr is not AssignmentExpressionSyntax assignment)
                continue;

            var propName = (assignment.Left as IdentifierNameSyntax)?.Identifier.Text;
            if (propName == null)
                continue;

            switch (propName)
            {
                case "Name":
                    if (assignment.Right is LiteralExpressionSyntax nameLit &&
                        nameLit.Token.Value is string name)
                        tag.Name = name;
                    break;

                case "Description":
                    if (assignment.Right is LiteralExpressionSyntax descLit &&
                        descLit.Token.Value is string desc)
                        tag.Description = desc;
                    break;

                case "ExternalDocs":
                    var (url, extDesc) = ParseExternalDocsExpression(assignment.Right);
                    tag.ExternalDocsUrl = url;
                    tag.ExternalDocsDescription = extDesc;
                    break;
            }
        }

        // A tag without a Name cannot be keyed — treat as unparseable.
        if (string.IsNullOrWhiteSpace(tag.Name))
            return null;

        return tag;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ExternalDocs parsing
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses <c>new OpenApiExternalDocs { Url = new Uri("..."), Description = "..." }</c>
    /// from an expression. Returns (null, null) when parsing fails.
    /// </summary>
    private static (string? url, string? description) ParseExternalDocsExpression(
        ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax paren)
            expression = paren.Expression;

        if (expression is not ObjectCreationExpressionSyntax objCreation)
            return (null, null);

        // Loose type check: OpenApiExternalDocs or ExternalDocs suffix.
        var typeName = GetUnqualifiedTypeName(objCreation.Type);
        if (!typeName.Contains("ExternalDocs", StringComparison.Ordinal) && !typeName.Contains("ExternalDoc", StringComparison.Ordinal))
            return (null, null);

        return ParseExternalDocsInitializer(objCreation.Initializer);
    }

    /// <summary>
    /// Parses <c>Url</c> and <c>Description</c> from an <c>OpenApiExternalDocs</c>
    /// object initializer.
    /// </summary>
    private static (string? url, string? description) ParseExternalDocsInitializer(
        InitializerExpressionSyntax? initializer)
    {
        if (initializer == null)
            return (null, null);

        string? url = null;
        string? description = null;

        foreach (var expr in initializer.Expressions)
        {
            if (expr is not AssignmentExpressionSyntax assignment)
                continue;

            var propName = (assignment.Left as IdentifierNameSyntax)?.Identifier.Text;
            if (propName == null)
                continue;

            switch (propName)
            {
                case "Url":
                    // Handle: new Uri("https://...") or just a string literal.
                    url = TryExtractUriLiteral(assignment.Right);
                    break;

                case "Description":
                    if (assignment.Right is LiteralExpressionSyntax descLit &&
                        descLit.Token.Value is string desc)
                        description = desc;
                    break;
            }
        }

        return (url, description);
    }

    /// <summary>
    /// Extracts a URI string from either <c>new Uri("...")</c> or a plain string literal.
    /// </summary>
    private static string? TryExtractUriLiteral(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax paren)
            expression = paren.Expression;

        // new Uri("https://...")
        if (expression is ObjectCreationExpressionSyntax uriCreation)
        {
            var arg0 = uriCreation.ArgumentList?.Arguments.FirstOrDefault();
            if (arg0?.Expression is LiteralExpressionSyntax uriLit &&
                uriLit.Token.Value is string uriStr)
                return uriStr;
        }

        // Plain string literal (unlikely but handled gracefully)
        if (expression is LiteralExpressionSyntax lit && lit.Token.Value is string s)
            return s;

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Root-level ExternalDocs (SwaggerDoc / AddOpenApi)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to extract root-level externalDocs from a <c>SwaggerDoc</c> or
    /// <c>AddOpenApi</c> call by scanning its arguments for an <c>OpenApiInfo</c>
    /// object initializer that contains an <c>ExternalDocs</c> property.
    /// </summary>
    private static (string? url, string? description) TryExtractRootExternalDocs(
        InvocationExpressionSyntax invocation)
    {
        // Scan all object-creation expressions in the argument list for OpenApiInfo
        // with an ExternalDocs property.
        foreach (var objCreation in invocation.ArgumentList.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = GetUnqualifiedTypeName(objCreation.Type);
            if (!typeName.Contains("OpenApiInfo", StringComparison.Ordinal) && !typeName.EndsWith("Info", StringComparison.Ordinal))
                continue;

            if (objCreation.Initializer == null)
                continue;

            foreach (var expr in objCreation.Initializer.Expressions)
            {
                if (expr is not AssignmentExpressionSyntax assignment)
                    continue;

                var propName = (assignment.Left as IdentifierNameSyntax)?.Identifier.Text;
                if (propName != "ExternalDocs")
                    continue;

                return ParseExternalDocsExpression(assignment.Right);
            }
        }

        return (null, null);
    }
}
