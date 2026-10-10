using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.Diagnostics;
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

    /// <summary>Tag summary from <c>OpenApiTag.Summary</c> (OpenAPI 3.2).</summary>
    public string? Summary { get; init; }

    /// <summary>Name of the parent tag from <c>OpenApiTag.Parent = new OpenApiTagReference("…")</c> (OpenAPI 3.2).</summary>
    public string? Parent { get; init; }

    /// <summary>Tag kind from <c>OpenApiTag.Kind</c> (OpenAPI 3.2).</summary>
    public string? Kind { get; init; }
}

/// <summary>The license of an <c>OpenApiInfo</c> initializer in <c>Program.cs</c>.</summary>
/// <param name="Name">The license name, if given as a literal or constant.</param>
/// <param name="Url">The license URL.</param>
/// <param name="Identifier">The SPDX license identifier (OpenAPI 3.1).</param>
public sealed record LicenseMetadata(string? Name, string? Url, string? Identifier);

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

    /// <summary>
    /// <c>info.summary</c> from an <c>OpenApiInfo { Summary = … }</c> initializer of <c>SwaggerDoc</c> /
    /// <c>AddOpenApi</c>, chosen as <see cref="ExternalDocsUrl"/> is: the first declaration that sets it.
    /// </summary>
    public string? InfoSummary { get; init; }

    /// <summary>
    /// The license of an <c>OpenApiInfo { License = new OpenApiLicense { … } }</c> initializer, the first
    /// declaration that sets one; <see langword="null"/> when none does.
    /// </summary>
    public LicenseMetadata? License { get; init; }
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
    public static DocumentTagsExtractionResult Extract(SourceAnalysisContext context) => Extract(context, onDiagnostic: null);

    /// <summary>
    /// Scans the Roslyn source context as <see cref="Extract(SourceAnalysisContext)"/> does and reports
    /// to <paramref name="onDiagnostic"/> the metadata it cannot read (code
    /// <see cref="ExtractionDiagnosticCodes.DocumentMetadataNotStatic"/>).
    /// </summary>
    /// <param name="context">The source analysis context built from the entry-point source.</param>
    /// <param name="onDiagnostic">Receives the warnings; <see langword="null"/> to ignore them.</param>
    public static DocumentTagsExtractionResult Extract(SourceAnalysisContext context, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        if (!context.IsAvailable || context.EntryPointNode == null)
            return new DocumentTagsExtractionResult();

        ReportUnreadMetadata(context, onDiagnostic);

        var tagsByName = new Dictionary<string, TagMetadata>(StringComparer.Ordinal);
        string? rootExternalDocsUrl = null;
        string? rootExternalDocsDesc = null;
        string? infoSummary = null;
        LicenseMetadata? license = null;
        var compilation = context.CompilationResult?.Compilation;

        // ── 1. AddTag(new OpenApiTag { ... }) ─────────────────────────────────
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddTag"))
        {
            var metadata = TryParseAddTagInvocation(invocation, compilation);
            if (metadata?.Name is { } name && !string.IsNullOrWhiteSpace(name))
            {
                // First registration wins; subsequent duplicates are ignored.
                tagsByName.TryAdd(name, new TagMetadata
                {
                    Description = metadata.Description,
                    ExternalDocsUrl = metadata.ExternalDocsUrl,
                    ExternalDocsDescription = metadata.ExternalDocsDescription,
                    Summary = metadata.Summary,
                    Parent = metadata.Parent,
                    Kind = metadata.Kind,
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

                // The other info fields follow the same choice: the first declaration that sets them.
                var (summary, declaredLicense) = TryExtractInfoMetadata(invocation, compilation);
                infoSummary ??= summary;
                license ??= declaredLicense;
            }
        }

        return new DocumentTagsExtractionResult
        {
            TagsByName = tagsByName,
            ExternalDocsUrl = rootExternalDocsUrl,
            ExternalDocsDescription = rootExternalDocsDesc,
            InfoSummary = infoSummary,
            License = license,
        };
    }

    /// <summary>
    /// <c>Summary</c> and <c>License</c> of the <c>OpenApiInfo</c> initializer in a <c>SwaggerDoc</c> /
    /// <c>AddOpenApi</c> call, from literals and in-project constants; other values are skipped, never
    /// evaluated.
    /// </summary>
    private static (string? Summary, LicenseMetadata? License) TryExtractInfoMetadata(
        InvocationExpressionSyntax invocation, CSharpCompilation? compilation)
    {
        foreach (var objCreation in InfoCreations(invocation))
        {
            string? summary = null;
            LicenseMetadata? license = null;
            foreach (var assignment in objCreation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? [])
            {
                switch ((assignment.Left as IdentifierNameSyntax)?.Identifier.Text)
                {
                    case "Summary":
                        summary = InvocationMatcher.GetStringValue(assignment.Right, compilation);
                        break;
                    // The same normalization as the diagnostics: parentheses around the creation are read through.
                    case "License" when ObjectCreations.Of(assignment.Right) is { Initializer: { } licenseInit }:
                        string? name = null, url = null, identifier = null;
                        foreach (var field in licenseInit.Expressions.OfType<AssignmentExpressionSyntax>())
                        {
                            switch ((field.Left as IdentifierNameSyntax)?.Identifier.Text)
                            {
                                case "Name":       name = InvocationMatcher.GetStringValue(field.Right, compilation); break;
                                case "Identifier": identifier = InvocationMatcher.GetStringValue(field.Right, compilation); break;
                                case "Url":        url = TryExtractUriLiteral(field.Right, compilation); break;
                            }
                        }
                        if (name != null || url != null || identifier != null)
                            license = new LicenseMetadata(name, url, identifier);
                        break;
                }
            }

            if (summary != null || license != null)
                return (summary, license);
        }

        return (null, null);
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
        public string? Summary { get; set; }
        public string? Parent { get; set; }
        public string? Kind { get; set; }
    }

    /// <summary>
    /// Attempts to parse a <c>ParsedTag</c> from an <c>AddTag(new OpenApiTag { ... })</c>
    /// invocation. Returns null if the first argument is not a recognisable object-creation
    /// expression for <c>OpenApiTag</c>.
    /// </summary>
    private static ParsedTag? TryParseAddTagInvocation(InvocationExpressionSyntax invocation, CSharpCompilation? compilation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return null;

        // new OpenApiTag { … } or the target-typed new() { … } (the parameter is an OpenApiTag);
        // loose type-name check on the explicit form.
        if (ObjectCreations.Of(args[0].Expression) is not { } objCreation || !ObjectCreations.Creates(objCreation, IsTagTypeName))
            return null;

        return ParseOpenApiTagInitializer(objCreation.Initializer, compilation);
    }

    /// <summary>
    /// Parses properties from an <c>InitializerExpressionSyntax</c> for
    /// <c>OpenApiTag</c>. Returns best-effort results; unknown or complex values
    /// are silently skipped.
    /// </summary>
    private static ParsedTag? ParseOpenApiTagInitializer(InitializerExpressionSyntax? initializer, CSharpCompilation? compilation)
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

                // OpenAPI 3.2 tag fields: literals and in-project constants only.
                case "Summary":
                    tag.Summary = InvocationMatcher.GetStringValue(assignment.Right, compilation);
                    break;

                case "Kind":
                    tag.Kind = InvocationMatcher.GetStringValue(assignment.Right, compilation);
                    break;

                case "Parent" when assignment.Right is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments: [var parentName, ..] }:
                    tag.Parent = InvocationMatcher.GetStringValue(parentName.Expression, compilation);
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
        // new OpenApiExternalDocs { … } or the target-typed new() { … } (the member is an
        // OpenApiExternalDocs); loose type check on the explicit form.
        if (ObjectCreations.Of(expression) is not { } objCreation
            || !ObjectCreations.Creates(objCreation, name => name.Contains("ExternalDoc", StringComparison.Ordinal)))
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
    private static string? TryExtractUriLiteral(ExpressionSyntax expression, CSharpCompilation? compilation = null)
    {
        while (expression is ParenthesizedExpressionSyntax paren)
            expression = paren.Expression;

        // new Uri("https://...") or the target-typed new("https://...")
        if (expression is BaseObjectCreationExpressionSyntax uriCreation)
        {
            var arg0 = uriCreation.ArgumentList?.Arguments.FirstOrDefault();
            if (arg0?.Expression is LiteralExpressionSyntax uriLit &&
                uriLit.Token.Value is string uriStr)
                return uriStr;
            if (arg0 != null && compilation != null)
                return InvocationMatcher.GetStringValue(arg0.Expression, compilation);
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
        foreach (var objCreation in InfoCreations(invocation))
        {
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

    // ──────────────────────────────────────────────────────────────────────────
    // Object creations of the document metadata, both forms
    // ──────────────────────────────────────────────────────────────────────────

    private static bool IsInfoTypeName(string name) =>
        name.Contains("OpenApiInfo", StringComparison.Ordinal) || name.EndsWith("Info", StringComparison.Ordinal);

    private static bool IsTagTypeName(string name) =>
        name.Contains("OpenApiTag", StringComparison.Ordinal) || name.EndsWith("Tag", StringComparison.Ordinal);

    /// <summary>
    /// The <c>OpenApiInfo</c> creations in a <c>SwaggerDoc</c> / <c>AddOpenApi</c> call: an explicit
    /// <c>new OpenApiInfo { … }</c> anywhere in its arguments, or a target-typed <c>new() { … }</c> where
    /// an <c>OpenApiInfo</c> is required — the second argument of <c>SwaggerDoc</c>, or the value of an
    /// <c>Info = …</c> assignment (a document transformer of <c>AddOpenApi</c>).
    /// </summary>
    private static IEnumerable<BaseObjectCreationExpressionSyntax> InfoCreations(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Where(creation => creation switch
        {
            ObjectCreationExpressionSyntax explicitCreation => IsInfoTypeName(GetUnqualifiedTypeName(explicitCreation.Type)),
            _ => (SwaggerDocInfoArgument(invocation) is { } info && ObjectCreations.Of(info.Expression) == creation)
                 || ObjectCreations.AssignedMember(creation) == "Info",
        });

    /// <summary>
    /// The <c>info</c> argument of <c>SwaggerDoc(name, info)</c>: the argument named <c>info:</c>, else the
    /// second positional one; <see langword="null"/> for any other call or when there is none. Reading
    /// and the diagnostics resolve it the same way.
    /// </summary>
    private static ArgumentSyntax? SwaggerDocInfoArgument(InvocationExpressionSyntax invocation)
    {
        if (InvocationMatcher.GetSimpleMethodName(invocation.Expression) != "SwaggerDoc")
            return null;

        var arguments = invocation.ArgumentList.Arguments;
        return arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "info")
            ?? (arguments.Count > 1 && arguments[1].NameColon == null ? arguments[1] : null);
    }

    /// <summary>
    /// Warns about document metadata that is not an object creation the extractor reads, instead of
    /// losing it silently: the info of <c>SwaggerDoc</c> or of an <c>Info = …</c> assignment, the
    /// <c>License</c> / <c>ExternalDocs</c> of an info, the argument of <c>AddTag</c> and a tag's
    /// <c>ExternalDocs</c>. <c>null</c> and <c>default</c> are a known absence, not a loss.
    /// </summary>
    private static void ReportUnreadMetadata(SourceAnalysisContext context, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        if (onDiagnostic == null)
            return;

        void Report(string place, ExpressionSyntax value)
        {
            if (IsNoValue(value))
                return;
            DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
            {
                Code     = ExtractionDiagnosticCodes.DocumentMetadataNotStatic,
                Message  = $"{place}: {value} is not an object creation the extractor can read — its fields are not written.",
                Subjects = [place, value.ToString()],
            });
        }

        void CheckMembers(BaseObjectCreationExpressionSyntax creation, string owner, params string[] members)
        {
            foreach (var assignment in creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? [])
            {
                if ((assignment.Left as IdentifierNameSyntax)?.Identifier.Text is { } member
                    && members.Contains(member) && ObjectCreations.Of(assignment.Right) == null)
                    Report($"{owner}.{member}", assignment.Right);
            }
        }

        foreach (var methodName in SwaggerDocMethodNames)
        foreach (var invocation in InvocationMatcher.FindInvocations(context, methodName))
        {
            if (SwaggerDocInfoArgument(invocation) is { } info && ObjectCreations.Of(info.Expression) == null)
                Report("SwaggerDoc info", info.Expression);

            foreach (var assignment in invocation.ArgumentList.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: "Info" }
                    && ObjectCreations.Of(assignment.Right) == null)
                    Report($"{methodName} Info", assignment.Right);
            }

            foreach (var creation in InfoCreations(invocation))
                CheckMembers(creation, "OpenApiInfo", "License", "ExternalDocs");
        }

        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddTag"))
        {
            if (invocation.ArgumentList.Arguments is not [var tag, ..])
                continue;
            if (ObjectCreations.Of(tag.Expression) is { } creation)
                CheckMembers(creation, "OpenApiTag", "ExternalDocs");
            else
                Report("AddTag", tag.Expression);
        }
    }

    /// <summary>Whether <paramref name="value"/> is <c>null</c>, <c>default</c> or <c>default(T)</c>.</summary>
    private static bool IsNoValue(ExpressionSyntax value) =>
        ObjectCreations.Unwrap(value) is LiteralExpressionSyntax literal
            && (literal.IsKind(SyntaxKind.NullLiteralExpression) || literal.IsKind(SyntaxKind.DefaultLiteralExpression))
        || ObjectCreations.Unwrap(value) is DefaultExpressionSyntax;
}
