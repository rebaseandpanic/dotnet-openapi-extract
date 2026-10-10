using DotNetOpenApiExtract.Core.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// Document-level security requirements, one entry per <c>AddSecurityRequirement</c> call
    /// that yielded at least one scheme name, in source order. Each entry holds the scheme
    /// names of that call. Mirrors the OpenAPI <c>security</c> array: entries are
    /// alternatives (any one satisfies the requirement, OR); the names inside one entry must
    /// all be satisfied together (AND).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> GlobalRequirements { get; init; } = [];

    /// <summary>
    /// The document-level requirements with their scopes, in the order of
    /// <see cref="GlobalRequirements"/>: one entry per <c>AddSecurityRequirement</c> call, each a list
    /// of (scheme name, scopes) pairs — the scopes listed next to the scheme reference
    /// (<c>{ ref, ["read"] }</c> or <c>[ref] = ["read"]</c>); empty when none are listed. When set,
    /// <see cref="GlobalRequirements"/> holds the same names.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<SecurityRequirementEntry>> GlobalRequirementEntries
    {
        get => _entries ?? GlobalRequirements.Select(names => (IReadOnlyList<SecurityRequirementEntry>)names
            .Select(name => new SecurityRequirementEntry(name, [])).ToList()).ToList();
        init
        {
            _entries = value;
            GlobalRequirements = value.Select(entry => (IReadOnlyList<string>)entry.Select(e => e.SchemeName).ToList()).ToList();
        }
    }

    private readonly IReadOnlyList<IReadOnlyList<SecurityRequirementEntry>>? _entries;

    /// <summary>
    /// Names of <c>AddSecurityDefinition</c> declarations that are omitted because a value they
    /// need (OAuth2 flows and their URLs and scopes, the OpenID Connect URL, the OAuth2 metadata
    /// URL, <c>Deprecated</c>) cannot be resolved statically: a variable, a call, configuration.
    /// </summary>
    public IReadOnlyList<string> OmittedSchemes { get; init; } = [];

    /// <summary>
    /// [DEPRECATED] All scheme names from <see cref="GlobalRequirements"/>, flattened in
    /// order. The flattening loses which names are alternatives and which must be combined.
    /// Setting it replaces <see cref="GlobalRequirements"/> with a single requirement that
    /// combines all given names (the former interpretation).
    /// </summary>
    [Obsolete("Flattens the requirements and loses their OR/AND grouping. Use GlobalRequirements instead.")]
    public IReadOnlyList<string> GlobalRequirementSchemeNames
    {
        get => GlobalRequirements.SelectMany(names => names).ToList();
        init => GlobalRequirements = value.Count > 0 ? [value] : [];
    }
}

/// <summary>One scheme of a security requirement and the scopes listed for it.</summary>
/// <param name="SchemeName">The scheme name.</param>
/// <param name="Scopes">The scopes (OAuth2 / OpenID Connect) listed next to the scheme reference.</param>
public sealed record SecurityRequirementEntry(string SchemeName, IReadOnlyList<string> Scopes);

/// <summary>
/// Scans a Roslyn <see cref="SourceAnalysisContext"/> for security-scheme registrations
/// and global security requirements declared in Program.cs (or the detected entry-point).
/// </summary>
/// <remarks>
/// Analysis is syntactic, except that scheme names given as in-project <c>const string</c>
/// members are folded through the semantic model. Unknown or complex patterns (e.g.
/// variables, configuration-sourced names) are skipped with a warning to <c>stderr</c>
/// rather than producing partial or incorrect output.
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
        => Extract(context, onDiagnostic: null);

    /// <summary>
    /// Same as <see cref="Extract(SourceAnalysisContext)"/>, but delivers warnings to
    /// <paramref name="onDiagnostic"/> instead of printing them to <c>Console.Error</c>.
    /// </summary>
    /// <param name="context">The source analysis context of the entry point.</param>
    /// <param name="onDiagnostic">
    /// Receives each warning. When <see langword="null"/>, warnings are printed to
    /// <c>Console.Error</c>, as <see cref="Extract(SourceAnalysisContext)"/> does.
    /// </param>
    public static SecuritySchemeExtractionResult Extract(
        SourceAnalysisContext context, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        if (!context.IsAvailable || context.EntryPointNode == null)
            return new SecuritySchemeExtractionResult();

        var schemes = new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal);
        var globalRequirements = new List<IReadOnlyList<SecurityRequirementEntry>>();
        var omitted = new List<string>();

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
                WarnDuplicateScheme(onDiagnostic, schemeName);
            }
        }

        // ── 2. AddSecurityDefinition registrations ────────────────────────────
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddSecurityDefinition"))
        {
            var name = InvocationMatcher.GetLiteralStringArgument(
                invocation, 0, context.CompilationResult?.Compilation);
            if (string.IsNullOrWhiteSpace(name))
            {
                DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
                {
                    Code    = ExtractionDiagnosticCodes.SecurityDefinitionNonLiteralName,
                    Message = "AddSecurityDefinition call with non-literal name — skipped.",
                });
                continue;
            }

            var scheme = TryParseSecuritySchemeFromInvocation(invocation, name!, context.CompilationResult?.Compilation, out var notStatic);
            if (notStatic)
            {
                if (!schemes.ContainsKey(name!) && !omitted.Contains(name!))
                    omitted.Add(name!);
                continue;
            }

            if (scheme != null)
            {
                if (!schemes.TryAdd(name!, scheme))
                {
                    WarnDuplicateScheme(onDiagnostic, name!);
                }
            }
        }

        // ── 3. AddSecurityRequirement registrations ───────────────────────────
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddSecurityRequirement"))
        {
            // One call = one Security Requirement Object: its names are combined (AND),
            // separate calls are alternatives (OR).
            var entries = TryExtractRequirementSchemeNames(
                invocation, context.CompilationResult?.Compilation, onDiagnostic);
            if (entries.Count > 0)
                globalRequirements.Add(entries);
        }

        return new SecuritySchemeExtractionResult
        {
            Schemes = schemes,
            GlobalRequirementEntries = globalRequirements,
            OmittedSchemes = omitted.Where(n => !schemes.ContainsKey(n)).ToList(),
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Object-initializer parsing
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse an <c>OpenApiSecurityScheme</c> from an
    /// <c>AddSecurityDefinition("Name", new OpenApiSecurityScheme { ... })</c> call.
    /// Returns null if the second argument is not a recognisable object-creation expression, or
    /// with <paramref name="notStatic"/> set when a value the scheme needs cannot be resolved statically.
    /// </summary>
    private static OpenApiSecurityScheme? TryParseSecuritySchemeFromInvocation(
        InvocationExpressionSyntax invocation, string schemeName, CSharpCompilation? compilation, out bool notStatic)
    {
        notStatic = false;
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

        return ParseObjectInitializer(objCreation.Initializer, schemeName, compilation, out notStatic);
    }

    /// <summary>
    /// Parses properties from an <c>InitializerExpressionSyntax</c> for <c>OpenApiSecurityScheme</c>.
    /// Descriptive values that are not literals are skipped. The values an OAuth2 or OpenID Connect
    /// scheme needs — <c>Flows</c> (every flow, its URLs and scopes), <c>OpenIdConnectUrl</c> — and
    /// <c>OAuth2MetadataUrl</c> / <c>Deprecated</c> must be resolved statically: otherwise
    /// <paramref name="notStatic"/> is set and the scheme is omitted. A fully literal OAuth2 declaration
    /// without flows, or OpenID Connect declaration without its URL, is an extraction error: no OpenAPI
    /// document can hold it.
    /// </summary>
    private static OpenApiSecurityScheme? ParseObjectInitializer(
        InitializerExpressionSyntax? initializer, string schemeName, CSharpCompilation? compilation, out bool notStatic)
    {
        notStatic = false;
        if (initializer == null)
            return null;

        var scheme = new OpenApiSecurityScheme();
        var unresolved = false;

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

                case "Flows":
                    scheme.Flows = ParseFlows(value, compilation, ref unresolved);
                    break;

                case "OpenIdConnectUrl":
                    scheme.OpenIdConnectUrl = ParseUri(value, compilation, ref unresolved);
                    break;

                case "OAuth2MetadataUrl":
                    scheme.OAuth2MetadataUrl = ParseUri(value, compilation, ref unresolved);
                    break;

                case "Deprecated":
                    if (IsNoValue(value))
                        scheme.Deprecated = false;
                    else if (value is LiteralExpressionSyntax deprecated
                        && (deprecated.IsKind(SyntaxKind.TrueLiteralExpression) || deprecated.IsKind(SyntaxKind.FalseLiteralExpression)))
                        scheme.Deprecated = deprecated.IsKind(SyntaxKind.TrueLiteralExpression);
                    else
                        unresolved = true;
                    break;
            }
        }

        // A scheme without a Type is not useful — skip it.
        if (scheme.Type == null)
            return null;

        if (unresolved)
        {
            notStatic = true;
            return null;
        }

        if (scheme.Type == SecuritySchemeType.OAuth2
            && scheme.Flows is not { Implicit: not null } and not { Password: not null } and not { ClientCredentials: not null }
                and not { AuthorizationCode: not null } and not { DeviceAuthorization: not null })
        {
            throw new OpenApiExtractionException(
                $"Security scheme '{schemeName}' is declared as OAuth2 without flows: OpenAPI requires 'flows' " +
                "for an oauth2 scheme. Set Flows = new OpenApiOAuthFlows { … } in AddSecurityDefinition.",
                "Microsoft.OpenApi.OpenApiSecurityScheme", "Flows");
        }

        if (scheme.Type == SecuritySchemeType.OAuth2)
            RequireFlowUrls(scheme.Flows!, schemeName);

        if (scheme.Type == SecuritySchemeType.OpenIdConnect && scheme.OpenIdConnectUrl == null)
        {
            throw new OpenApiExtractionException(
                $"Security scheme '{schemeName}' is declared as OpenID Connect without OpenIdConnectUrl: OpenAPI " +
                "requires 'openIdConnectUrl' for an openIdConnect scheme.",
                "Microsoft.OpenApi.OpenApiSecurityScheme", "OpenIdConnectUrl");
        }

        return scheme;
    }

    /// <summary>
    /// Every flow of a fully known OAuth2 declaration must have the URLs OpenAPI requires for it:
    /// <c>implicit</c> an authorization URL, <c>password</c> and <c>clientCredentials</c> a token URL,
    /// <c>authorizationCode</c> both, <c>deviceAuthorization</c> a device authorization URL and a token
    /// URL (OpenAPI 3.2). A missing one is an extraction error naming the scheme, the flow and the member.
    /// </summary>
    private static void RequireFlowUrls(OpenApiOAuthFlows flows, string schemeName)
    {
        void Require(OpenApiOAuthFlow? flow, string flowName, string member, Uri? value)
        {
            if (flow != null && value == null)
                throw new OpenApiExtractionException(
                    $"Security scheme '{schemeName}': the {flowName} OAuth2 flow has no {member}, which OpenAPI requires for it.",
                    "Microsoft.OpenApi.OpenApiOAuthFlow", member);
        }

        Require(flows.Implicit, "implicit", "AuthorizationUrl", flows.Implicit?.AuthorizationUrl);
        Require(flows.Password, "password", "TokenUrl", flows.Password?.TokenUrl);
        Require(flows.ClientCredentials, "clientCredentials", "TokenUrl", flows.ClientCredentials?.TokenUrl);
        Require(flows.AuthorizationCode, "authorizationCode", "AuthorizationUrl", flows.AuthorizationCode?.AuthorizationUrl);
        Require(flows.AuthorizationCode, "authorizationCode", "TokenUrl", flows.AuthorizationCode?.TokenUrl);
        Require(flows.DeviceAuthorization, "deviceAuthorization", "DeviceAuthorizationUrl", flows.DeviceAuthorization?.DeviceAuthorizationUrl);
        Require(flows.DeviceAuthorization, "deviceAuthorization", "TokenUrl", flows.DeviceAuthorization?.TokenUrl);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is <c>null</c>, <c>default</c> or <c>default(T)</c>: a known absence
    /// of a value, not a value that cannot be resolved.
    /// </summary>
    private static bool IsNoValue(ExpressionSyntax value)
    {
        while (value is ParenthesizedExpressionSyntax paren)
            value = paren.Expression;
        return value is LiteralExpressionSyntax literal
                   && (literal.IsKind(SyntaxKind.NullLiteralExpression) || literal.IsKind(SyntaxKind.DefaultLiteralExpression))
               || value is DefaultExpressionSyntax;
    }

    /// <summary>The object creation <c>new T { … }</c> / <c>new() { … }</c> behind <paramref name="value"/>, if it is one.</summary>
    private static BaseObjectCreationExpressionSyntax? Creation(ExpressionSyntax value)
    {
        while (value is ParenthesizedExpressionSyntax paren)
            value = paren.Expression;
        return value as BaseObjectCreationExpressionSyntax;
    }

    /// <summary>
    /// <c>new OpenApiOAuthFlows { Implicit = …, Password = …, ClientCredentials = …, AuthorizationCode = …,
    /// DeviceAuthorization = … }</c>; anything else sets <paramref name="unresolved"/>.
    /// </summary>
    private static OpenApiOAuthFlows? ParseFlows(ExpressionSyntax value, CSharpCompilation? compilation, ref bool unresolved)
    {
        if (IsNoValue(value))
            return null; // known: no flows

        if (Creation(value) is not { } creation)
        {
            unresolved = true;
            return null;
        }

        var flows = new OpenApiOAuthFlows();
        foreach (var assignment in creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? [])
        {
            switch ((assignment.Left as IdentifierNameSyntax)?.Identifier.Text)
            {
                case "Implicit":            flows.Implicit = ParseFlow(assignment.Right, compilation, ref unresolved); break;
                case "Password":            flows.Password = ParseFlow(assignment.Right, compilation, ref unresolved); break;
                case "ClientCredentials":   flows.ClientCredentials = ParseFlow(assignment.Right, compilation, ref unresolved); break;
                case "AuthorizationCode":   flows.AuthorizationCode = ParseFlow(assignment.Right, compilation, ref unresolved); break;
                case "DeviceAuthorization": flows.DeviceAuthorization = ParseFlow(assignment.Right, compilation, ref unresolved); break;
            }
        }

        return flows;
    }

    /// <summary>
    /// <c>new OpenApiOAuthFlow { AuthorizationUrl, TokenUrl, RefreshUrl, DeviceAuthorizationUrl, Scopes }</c>;
    /// a value that is not a literal (or an in-project constant) sets <paramref name="unresolved"/>.
    /// </summary>
    private static OpenApiOAuthFlow? ParseFlow(ExpressionSyntax value, CSharpCompilation? compilation, ref bool unresolved)
    {
        if (IsNoValue(value))
            return null; // known: no such flow

        if (Creation(value) is not { } creation)
        {
            unresolved = true;
            return null;
        }

        var flow = new OpenApiOAuthFlow();
        foreach (var assignment in creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? [])
        {
            switch ((assignment.Left as IdentifierNameSyntax)?.Identifier.Text)
            {
                case "AuthorizationUrl":       flow.AuthorizationUrl = ParseUri(assignment.Right, compilation, ref unresolved); break;
                case "TokenUrl":               flow.TokenUrl = ParseUri(assignment.Right, compilation, ref unresolved); break;
                case "RefreshUrl":             flow.RefreshUrl = ParseUri(assignment.Right, compilation, ref unresolved); break;
                case "DeviceAuthorizationUrl": flow.DeviceAuthorizationUrl = ParseUri(assignment.Right, compilation, ref unresolved); break;
                case "Scopes":                 flow.Scopes = ParseScopes(assignment.Right, compilation, ref unresolved); break;
            }
        }

        // OpenAPI requires the scopes map; a flow that names none has an empty one.
        flow.Scopes ??= new Dictionary<string, string>(StringComparer.Ordinal);
        return flow;
    }

    /// <summary>
    /// <c>new Uri("…")</c> (or <c>new("…")</c>, an optional <c>UriKind</c>) with a literal or in-project
    /// constant string; anything else sets <paramref name="unresolved"/>.
    /// </summary>
    private static Uri? ParseUri(ExpressionSyntax value, CSharpCompilation? compilation, ref bool unresolved)
    {
        if (IsNoValue(value))
            return null; // known: no URL

        if (Creation(value) is { ArgumentList.Arguments: [var first, ..] }
            && InvocationMatcher.GetStringValue(first.Expression, compilation) is { } text
            && Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out var uri))
            return uri;

        unresolved = true;
        return null;
    }

    /// <summary>
    /// <c>new Dictionary&lt;string, string&gt; { ["scope"] = "description", { "scope", "description" } }</c>
    /// with literal or constant strings; anything else sets <paramref name="unresolved"/>.
    /// </summary>
    private static Dictionary<string, string>? ParseScopes(ExpressionSyntax value, CSharpCompilation? compilation, ref bool unresolved)
    {
        if (IsNoValue(value))
            return null; // known: no scopes (written as the empty map)

        if (Creation(value) is not { } creation)
        {
            unresolved = true;
            return null;
        }

        var scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in creation.Initializer?.Expressions ?? [])
        {
            (ExpressionSyntax Key, ExpressionSyntax Value)? pair = entry switch
            {
                AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax { ArgumentList.Arguments: [var key] } } indexed
                    => (key.Expression, indexed.Right),
                InitializerExpressionSyntax { Expressions: [var key, var description] } => (key, description),
                _ => null,
            };
            if (pair is not { } p
                || InvocationMatcher.GetStringValue(p.Key, compilation) is not { } scope
                || InvocationMatcher.GetStringValue(p.Value, compilation) is not { } text)
            {
                unresolved = true;
                return null;
            }

            scopes[scope] = text;
        }

        return scopes;
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
            "MutualTLS"     => SecuritySchemeType.MutualTLS,
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
    /// <param name="invocation">The <c>AddSecurityRequirement</c> call.</param>
    /// <param name="compilation">
    /// Optional compilation used to resolve scheme names given as in-project
    /// <c>const string</c> members. Pass <see langword="null"/> to accept literals only.
    /// </param>
    private static IReadOnlyList<SecurityRequirementEntry> TryExtractRequirementSchemeNames(
        InvocationExpressionSyntax invocation,
        CSharpCompilation? compilation,
        Action<ExtractionDiagnostic>? onDiagnostic)
    {
        // We look for scheme names (string literals or in-project string constants)
        // used as keys inside the object initializer.
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

        var names = new List<SecurityRequirementEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddName(string name, SyntaxNode reference)
        {
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                names.Add(new SecurityRequirementEntry(name, ScopesOf(reference, invocation, compilation, onDiagnostic)));
        }

        // ── Pattern A: referenceId argument of new OpenApiSecuritySchemeReference(...) ──
        // Only referenceId names a scheme; hostDocument and externalResource (a document
        // URI) are not scheme names. The argument may be a literal or an in-project const
        // (Consts.SchemeName); anything else is reported as skipped.
        foreach (var objCreation in invocation.ArgumentList.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = GetUnqualifiedTypeName(objCreation.Type);
            if (!typeName.Contains("SecuritySchemeReference", StringComparison.Ordinal)
                || objCreation.ArgumentList == null)
                continue;

            var referenceIdArg = GetReferenceIdArgument(objCreation.ArgumentList);
            if (referenceIdArg == null)
                continue;

            var resolvedName = InvocationMatcher.GetStringValue(referenceIdArg, compilation);
            if (resolvedName == null)
                WarnNonLiteralRequirementSchemeName(onDiagnostic);
            else
                AddName(resolvedName, objCreation);
        }

        // ── Pattern B: Id = "<literal>" or Id = <const> inside an object initializer that
        //    also signals a security-scheme reference — either via Type = ReferenceType.SecurityScheme or
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

            // Find the Id = <expr> assignment (C# forbids initializing a member twice).
            var idExpression = assignments
                .FirstOrDefault(a => a.Left is IdentifierNameSyntax lhs && lhs.Identifier.Text == "Id")
                ?.Right;

            if (idExpression == null)
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

            if (!isReferenceType || !hasSecuritySchemeType)
                continue;

            // Id is a literal or an in-project const (Consts.SchemeName); anything else
            // cannot be resolved statically and is reported as skipped.
            var idValue = InvocationMatcher.GetStringValue(idExpression, compilation);
            if (idValue == null)
                WarnNonLiteralRequirementSchemeName(onDiagnostic);
            else
                AddName(idValue, objCreation);
        }

        return names;
    }

    /// <summary>
    /// The scopes listed for the scheme reference <paramref name="reference"/> in its requirement: the
    /// value of its pair <c>{ key, scopes }</c> or <c>[key] = scopes</c>, where the key is the reference
    /// or the expression that contains it. Scopes are a collection expression, an array or a list
    /// initializer of literal or constant strings; empty when the pair is not found. Scopes that are
    /// not literals are dropped with a warning.
    /// </summary>
    private static IReadOnlyList<string> ScopesOf(
        SyntaxNode reference, InvocationExpressionSyntax invocation, CSharpCompilation? compilation, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        for (var node = reference; node != null && node != invocation; node = node.Parent)
        {
            ExpressionSyntax? value = node.Parent switch
            {
                InitializerExpressionSyntax { RawKind: (int)SyntaxKind.ComplexElementInitializerExpression, Expressions: [var key, var scopeValue] }
                    when key == node => scopeValue,
                BracketedArgumentListSyntax { Parent: ImplicitElementAccessSyntax { Parent: AssignmentExpressionSyntax indexed } }
                    when node is ArgumentSyntax => indexed.Right,
                _ => null,
            };
            if (value == null)
                continue;

            IEnumerable<ExpressionSyntax>? items = value switch
            {
                CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression),
                ArrayCreationExpressionSyntax { Initializer: { } array } => array.Expressions,
                ImplicitArrayCreationExpressionSyntax { Initializer: { } implicitArray } => implicitArray.Expressions,
                BaseObjectCreationExpressionSyntax created => created.Initializer?.Expressions ?? [],
                _ => null,
            };
            var scopes = items?.Select(item => InvocationMatcher.GetStringValue(item, compilation)).ToList();
            if (scopes == null || scopes.Any(scope => scope == null))
            {
                DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
                {
                    Code    = ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScopes,
                    Message = $"AddSecurityRequirement: the scopes {value} cannot be resolved statically — written as an empty list.",
                    Subjects = [value.ToString()],
                });
                return [];
            }

            return scopes!;
        }

        return [];
    }

    /// <summary>
    /// Returns the <c>referenceId</c> argument of an <c>OpenApiSecuritySchemeReference</c>
    /// constructor call: the named <c>referenceId:</c> argument, otherwise the first
    /// positional argument. Returns <see langword="null"/> when there is none.
    /// </summary>
    private static ExpressionSyntax? GetReferenceIdArgument(ArgumentListSyntax argumentList)
    {
        var named = argumentList.Arguments.FirstOrDefault(a =>
            a.NameColon?.Name.Identifier.Text == "referenceId");
        if (named != null)
            return named.Expression;

        var first = argumentList.Arguments.FirstOrDefault();
        return first != null && first.NameColon == null ? first.Expression : null;
    }

    private static void WarnNonLiteralRequirementSchemeName(Action<ExtractionDiagnostic>? onDiagnostic)
        => DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
        {
            Code    = ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScheme,
            Message = "AddSecurityRequirement call with non-literal scheme name — skipped.",
        });

    private static void WarnDuplicateScheme(Action<ExtractionDiagnostic>? onDiagnostic, string schemeName)
        => DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
        {
            Code     = ExtractionDiagnosticCodes.SecurityDuplicateScheme,
            Message  = $"Duplicate security scheme '{schemeName}' ignored (first registration wins).",
            Location = Validation.JsonPointerHelper.ForSecurityScheme(schemeName),
            Subjects = [schemeName],
        });
}
