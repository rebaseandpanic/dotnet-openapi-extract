using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.OpenApi;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using static DotNetOpenApiExtract.Core.SourceAnalysis.TypeSyntaxHelper;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Result of scanning Roslyn source for security-scheme registrations.
/// </summary>
public sealed class SecuritySchemeExtractionResult
{
    /// <summary>
    /// Named security schemes detected from Program.cs-style registrations.
    /// Key is the scheme name as it would appear in <c>components/securitySchemes</c>.
    /// </summary>
    public IReadOnlyDictionary<string, OpenApiSecurityScheme> Schemes { get; init; }
        = new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal);

    /// <summary>
    /// Scheme names that appear in global <c>AddSecurityRequirement</c> calls.
    /// When non-empty these are used to populate <c>security</c> at the document level.
    /// </summary>
    public IReadOnlyList<string> GlobalRequirementSchemeNames { get; init; } = [];
}

/// <summary>
/// Scans a Roslyn <see cref="SourceAnalysisContext"/> for security-scheme registrations
/// and global security requirements declared in Program.cs (or the detected entry-point).
/// </summary>
/// <remarks>
/// All analysis is purely syntactic — no semantic resolution is performed. Unknown or
/// complex patterns (e.g. variables, configuration-sourced names) are skipped with a
/// warning to <c>stderr</c> rather than producing partial or incorrect output.
///
/// Limitations: only the entry-point node (and its descendants) is scanned. Security
/// registrations inside a separate <c>Startup.ConfigureServices</c> method that is not
/// inlined into the entry-point scope are not detected.
/// </remarks>
public static class SecuritySchemeExtractor
{
    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans the Roslyn source analysis context for security-scheme registrations and
    /// global security requirements declared in Program.cs.
    /// Returns an empty result when <paramref name="context"/> is unavailable.
    /// </summary>
    /// <remarks>
    /// Duplicate scheme registrations (same name from multiple <c>AddJwtBearer</c> /
    /// <c>AddSecurityDefinition</c> calls) are resolved first-wins; subsequent registrations
    /// are ignored with a warning to <c>Console.Error</c>.
    /// </remarks>
    public static SecuritySchemeExtractionResult Extract(SourceAnalysisContext context)
    {
        if (!context.IsAvailable || context.EntryPointNode == null)
            return new SecuritySchemeExtractionResult();

        var schemes = new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal);
        var globalRequirements = new List<string>();

        // ── 1. AddJwtBearer registrations ─────────────────────────────────────
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddJwtBearer"))
        {
            // Possible signatures:
            //   .AddJwtBearer(options => { ... })                      ← name defaults to "Bearer"
            //   .AddJwtBearer("SchemeName", options => { ... })        ← explicit name
            var args = invocation.ArgumentList.Arguments;
            string schemeName = "Bearer";

            if (args.Count >= 1)
            {
                // First arg may be a string literal (scheme name) or a lambda (options).
                var firstLiteral = InvocationMatcher.GetLiteralStringArgument(
                    invocation, 0, context.CompilationResult?.Compilation);
                if (!string.IsNullOrWhiteSpace(firstLiteral))
                    schemeName = firstLiteral!;
            }

            if (!schemes.TryAdd(schemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT Bearer authentication",
            }))
            {
                Console.Error.WriteLine(
                    $"Warning: Duplicate security scheme '{schemeName}' ignored (first registration wins).");
            }
        }

        // ── 2. AddSecurityDefinition registrations ────────────────────────────
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddSecurityDefinition"))
        {
            var name = InvocationMatcher.GetLiteralStringArgument(
                invocation, 0, context.CompilationResult?.Compilation);
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine(
                    "Warning: AddSecurityDefinition call with non-literal name — skipped.");
                continue;
            }

            var scheme = TryParseSecuritySchemeFromInvocation(invocation);
            if (scheme != null)
            {
                if (!schemes.TryAdd(name!, scheme))
                {
                    Console.Error.WriteLine(
                        $"Warning: Duplicate security scheme '{name}' ignored (first registration wins).");
                }
            }
        }

        // ── 3. AddSecurityRequirement registrations ───────────────────────────
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddSecurityRequirement"))
        {
            var names = TryExtractRequirementSchemeNames(invocation);
            globalRequirements.AddRange(names);
        }

        return new SecuritySchemeExtractionResult
        {
            Schemes = schemes,
            GlobalRequirementSchemeNames = globalRequirements,
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Object-initializer parsing
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse an <c>OpenApiSecurityScheme</c> from an
    /// <c>AddSecurityDefinition("Name", new OpenApiSecurityScheme { ... })</c> call.
    /// Returns null if the second argument is not a recognisable object-creation expression.
    /// </summary>
    private static OpenApiSecurityScheme? TryParseSecuritySchemeFromInvocation(
        InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2)
            return null;

        // The second argument must be an object-creation expression.
        var secondArg = args[1].Expression;

        // Strip parentheses / cast expressions defensively
        while (secondArg is ParenthesizedExpressionSyntax paren)
            secondArg = paren.Expression;

        if (secondArg is not ObjectCreationExpressionSyntax objCreation)
            return null;

        // The type name should reference OpenApiSecurityScheme (we do a loose check)
        var typeName = GetUnqualifiedTypeName(objCreation.Type);
        if (!typeName.Contains("SecurityScheme", StringComparison.Ordinal))
            return null;

        return ParseObjectInitializer(objCreation.Initializer);
    }

    /// <summary>
    /// Parses properties from an <c>InitializerExpressionSyntax</c> for
    /// <c>OpenApiSecurityScheme</c>. Returns a best-effort result — unknown or
    /// complex property values are silently skipped.
    /// </summary>
    private static OpenApiSecurityScheme? ParseObjectInitializer(
        InitializerExpressionSyntax? initializer)
    {
        if (initializer == null)
            return null;

        var scheme = new OpenApiSecurityScheme();

        foreach (var expr in initializer.Expressions)
        {
            if (expr is not AssignmentExpressionSyntax assignment)
                continue;

            var propName = (assignment.Left as IdentifierNameSyntax)?.Identifier.Text;
            if (propName == null)
                continue;

            var value = assignment.Right;

            switch (propName)
            {
                case "Type":
                    scheme.Type = ParseSecuritySchemeType(value);
                    break;

                case "Scheme":
                    if (value is LiteralExpressionSyntax schemeLit && schemeLit.Token.Value is string s)
                        scheme.Scheme = s;
                    break;

                case "BearerFormat":
                    if (value is LiteralExpressionSyntax bfLit && bfLit.Token.Value is string bf)
                        scheme.BearerFormat = bf;
                    break;

                case "Description":
                    if (value is LiteralExpressionSyntax descLit && descLit.Token.Value is string desc)
                        scheme.Description = desc;
                    break;

                case "Name":
                    if (value is LiteralExpressionSyntax nameLit && nameLit.Token.Value is string n)
                        scheme.Name = n;
                    break;

                case "In":
                    scheme.In = ParseParameterLocation(value);
                    break;
            }
        }

        // A scheme without a Type is not useful — skip it.
        if (scheme.Type == null)
            return null;

        return scheme;
    }

    /// <summary>
    /// Maps a syntax expression like <c>SecuritySchemeType.Http</c> to the
    /// corresponding <see cref="SecuritySchemeType"/> value, or null if unrecognised.
    /// Handles FQN-prefixed forms such as <c>Microsoft.OpenApi.SecuritySchemeType.Http</c>.
    /// </summary>
    private static SecuritySchemeType? ParseSecuritySchemeType(ExpressionSyntax expr)
    {
        var (typeName, memberName) = GetEnumReference(expr);
        if (typeName != "SecuritySchemeType" || memberName == null) return null;
        return memberName switch
        {
            "ApiKey"        => SecuritySchemeType.ApiKey,
            "Http"          => SecuritySchemeType.Http,
            "OAuth2"        => SecuritySchemeType.OAuth2,
            "OpenIdConnect" => SecuritySchemeType.OpenIdConnect,
            _               => null,
        };
    }

    /// <summary>
    /// Maps a syntax expression like <c>ParameterLocation.Header</c> to the
    /// corresponding <see cref="Microsoft.OpenApi.ParameterLocation"/> value, or null if unrecognised.
    /// Handles FQN-prefixed forms such as <c>Microsoft.OpenApi.ParameterLocation.Header</c>.
    /// </summary>
    private static Microsoft.OpenApi.ParameterLocation? ParseParameterLocation(ExpressionSyntax expr)
    {
        var (typeName, memberName) = GetEnumReference(expr);
        if (typeName != "ParameterLocation" || memberName == null) return null;
        return memberName switch
        {
            "Query"  => Microsoft.OpenApi.ParameterLocation.Query,
            "Header" => Microsoft.OpenApi.ParameterLocation.Header,
            "Path"   => Microsoft.OpenApi.ParameterLocation.Path,
            "Cookie" => Microsoft.OpenApi.ParameterLocation.Cookie,
            _        => null,
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Security requirement parsing
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to extract scheme names from an <c>AddSecurityRequirement(...)</c> invocation.
    /// Returns an empty list when the pattern is too complex to parse reliably.
    /// </summary>
    private static IReadOnlyList<string> TryExtractRequirementSchemeNames(
        InvocationExpressionSyntax invocation)
    {
        // We look for string literals used as keys inside the object initializer.
        // Two patterns are supported (additive):
        //
        // Pattern A — OpenApiSecuritySchemeReference constructor arg (Microsoft.OpenApi 2.x):
        //   new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("Bearer"), [] } }
        //
        // Pattern B — OpenApiReference.Id named property (canonical Swashbuckle / Microsoft.OpenApi 1.x):
        //   new OpenApiSecurityRequirement {
        //     { new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "ApiKey",
        //                                                Type = ReferenceType.SecurityScheme } }, [] }
        //   }
        //
        // DescendantNodes() descends into lambda bodies, so lambda-factory patterns
        // AddSecurityRequirement(doc => new OpenApiSecurityRequirement { ... })
        // are handled without special-casing.

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                names.Add(name);
        }

        // ── Pattern A: string literal ctor arg on SecuritySchemeReference / SecurityRequirement ──
        foreach (var objCreation in invocation.ArgumentList.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = GetUnqualifiedTypeName(objCreation.Type);
            if (!typeName.Contains("SecuritySchemeReference", StringComparison.Ordinal)
                && !typeName.Contains("SecurityRequirement", StringComparison.Ordinal))
                continue;

            if (objCreation.ArgumentList != null)
            {
                foreach (var arg in objCreation.ArgumentList.Arguments)
                {
                    if (arg.Expression is LiteralExpressionSyntax lit &&
                        lit.Token.Value is string schemeId)
                    {
                        AddName(schemeId);
                    }
                }
            }
        }

        // ── Pattern B: Id = "<literal>" inside an object initializer that also signals a
        //    security-scheme reference — either via Type = ReferenceType.SecurityScheme or
        //    because the containing ObjectCreation type text contains "OpenApiReference". ──
        foreach (var objCreation in invocation.ArgumentList.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>())
        {
            if (objCreation.Initializer == null)
                continue;

            // Collect all assignments in this initializer.
            var assignments = objCreation.Initializer.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .ToList();

            // Find Id = "<literal>" assignment.
            string? idValue = null;
            foreach (var assign in assignments)
            {
                if (assign.Left is IdentifierNameSyntax lhs &&
                    lhs.Identifier.Text == "Id" &&
                    assign.Right is LiteralExpressionSyntax rhs &&
                    rhs.Token.Value is string s &&
                    !string.IsNullOrWhiteSpace(s))
                {
                    idValue = s;
                    break;
                }
            }

            if (idValue == null)
                continue;

            // Gate: BOTH conditions must hold —
            //   1. The ObjectCreation type name contains "OpenApiReference", AND
            //   2. The same initializer contains Type = ReferenceType.SecurityScheme.
            // Using OR would let any OpenApiReference with an Id (e.g. a schema reference
            // inside AddSecurityRequirement) pollute the requirement list.
            var typeName = GetUnqualifiedTypeName(objCreation.Type);
            bool isReferenceType = typeName.Contains("OpenApiReference", StringComparison.Ordinal);

            bool hasSecuritySchemeType = assignments.Any(a =>
                a.Left is IdentifierNameSyntax l && l.Identifier.Text == "Type" &&
                a.Right.ToString().EndsWith(".SecurityScheme", StringComparison.Ordinal));

            if (isReferenceType && hasSecuritySchemeType)
                AddName(idValue);
        }

        return names;
    }
}
