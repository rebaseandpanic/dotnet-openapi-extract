using DotNetOpenApiExtract.Core.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using static DotNetOpenApiExtract.Core.SourceAnalysis.TypeSyntaxHelper;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// JSON serializer options detected for one serialization context of ASP.NET Core.
/// </summary>
/// <remarks>
/// Every property is <see langword="null"/> (or empty) when the source does not set it; the
/// caller then applies the ASP.NET Core default (<c>JsonSerializerDefaults.Web</c>:
/// camelCase names, nothing ignored, strict numbers, no extra converters).
/// </remarks>
public sealed class JsonContextOptions
{
    /// <summary>The property naming policy, if detected.</summary>
    public JsonNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>The dictionary key policy, if detected.</summary>
    public JsonNamingPolicy? DictionaryKeyPolicy { get; init; }

    /// <summary>The default ignore condition, if detected.</summary>
    public JsonIgnoreCondition? DefaultIgnoreCondition { get; init; }

    /// <summary>The number handling flags, if detected.</summary>
    public JsonNumberHandling? NumberHandling { get; init; }

    /// <summary>
    /// Converter type names (short or fully qualified) collected from
    /// <c>Converters.Add(new XxxConverter())</c> calls, in source order.
    /// </summary>
    public IReadOnlyList<string> GlobalConverterTypeNames { get; init; } = [];

    /// <summary>
    /// The enum naming policy passed to each converter of <see cref="GlobalConverterTypeNames"/>, by
    /// position: System.Text.Json's <c>new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c>
    /// (also the generic form), Newtonsoft's <c>new StringEnumConverter(camelCaseText: true)</c> or a
    /// <c>NamingStrategy</c> (<c>CamelCaseNamingStrategy</c>, <c>SnakeCaseNamingStrategy</c>,
    /// <c>KebabCaseNamingStrategy</c>). <see langword="null"/> when none is passed or it is not
    /// recognised (that gives a warning).
    /// </summary>
    public IReadOnlyList<JsonNamingPolicy?> GlobalConverterEnumNamingPolicies { get; init; } = [];

    /// <summary>
    /// <see langword="true"/> when at least one of the options above was detected for this context.
    /// </summary>
    public bool IsConfigured =>
        PropertyNamingPolicy != null
        || DictionaryKeyPolicy != null
        || DefaultIgnoreCondition != null
        || NumberHandling != null
        || GlobalConverterTypeNames.Count > 0;

}

/// <summary>
/// Result of scanning Roslyn source for JSON serializer option registrations.
/// </summary>
/// <remarks>
/// ASP.NET Core keeps two independent option sets: MVC controllers serialize with
/// <c>AddControllers().AddJsonOptions(...)</c> (<see cref="Mvc"/>), while typed <c>IResult</c>
/// bodies and server-sent events serialize with <c>ConfigureHttpJsonOptions(...)</c>
/// (<see cref="Http"/>). One never affects the other.
/// </remarks>
public sealed class JsonOptionsExtractionResult
{
    /// <summary>
    /// Options of the MVC context, from <c>AddJsonOptions</c> calls. Never <see langword="null"/>.
    /// </summary>
    public JsonContextOptions Mvc { get; init; } = new();

    /// <summary>
    /// Options of the HTTP context (minimal APIs, <c>IResult</c>, server-sent events), from
    /// <c>ConfigureHttpJsonOptions</c> calls. Never <see langword="null"/>.
    /// </summary>
    public JsonContextOptions Http { get; init; } = new();

    /// <summary>
    /// Both contexts merged into one, as earlier versions reported them: the
    /// <c>AddJsonOptions</c> value wins over the <c>ConfigureHttpJsonOptions</c> value.
    /// </summary>
    [Obsolete("The two contexts are serialized independently; use Mvc or Http.")]
    public JsonNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>
    /// Both contexts merged into one, as earlier versions reported them: the
    /// <c>AddJsonOptions</c> value wins over the <c>ConfigureHttpJsonOptions</c> value.
    /// </summary>
    [Obsolete("The two contexts are serialized independently; use Mvc or Http.")]
    public JsonNamingPolicy? DictionaryKeyPolicy { get; init; }

    /// <summary>
    /// Both contexts merged into one, as earlier versions reported them: the
    /// <c>AddJsonOptions</c> value wins over the <c>ConfigureHttpJsonOptions</c> value.
    /// </summary>
    [Obsolete("The two contexts are serialized independently; use Mvc or Http.")]
    public JsonIgnoreCondition? DefaultIgnoreCondition { get; init; }

    /// <summary>
    /// Both contexts merged into one, as earlier versions reported them: the
    /// <c>AddJsonOptions</c> value wins over the <c>ConfigureHttpJsonOptions</c> value.
    /// </summary>
    [Obsolete("The two contexts are serialized independently; use Mvc or Http.")]
    public JsonNumberHandling? NumberHandling { get; init; }

    /// <summary>
    /// Converter type names of both contexts in one list, as earlier versions reported them:
    /// <c>ConfigureHttpJsonOptions</c> converters first, then <c>AddJsonOptions</c> converters.
    /// </summary>
    [Obsolete("The two contexts are serialized independently; use Mvc or Http.")]
    public IReadOnlyList<string> GlobalConverterTypeNames { get; init; } = [];
}

/// <summary>
/// Extracts JSON serializer options from <c>ConfigureHttpJsonOptions</c> or
/// <c>AddJsonOptions</c> registrations in the entry-point source.
/// Returns an empty result when the context is unavailable or no options are registered.
/// </summary>
/// <remarks>
/// All analysis is purely syntactic. The lambda parameter name is arbitrary — patterns are
/// matched on the property-access chain (e.g. <c>.SerializerOptions.PropertyNamingPolicy</c>),
/// not on the root identifier. Non-literal (variable, config-sourced) values are skipped with
/// a warning written to <c>stderr</c>.
/// </remarks>
public static class JsonOptionsExtractor
{
    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts JSON serializer options from <c>ConfigureHttpJsonOptions</c> or
    /// <c>AddJsonOptions</c> registrations in the entry-point source.
    /// Returns an empty result when the context is unavailable or no options are registered.
    /// </summary>
    public static JsonOptionsExtractionResult Extract(SourceAnalysisContext context)
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
    public static JsonOptionsExtractionResult Extract(
        SourceAnalysisContext context, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        if (!context.IsAvailable || context.EntryPointNode == null)
            return new JsonOptionsExtractionResult();

        // ConfigureHttpJsonOptions: o => o.SerializerOptions.X = ...
        var http = ExtractContext(context, "ConfigureHttpJsonOptions", "SerializerOptions", onDiagnostic);
        // AddControllers().AddJsonOptions: o => o.JsonSerializerOptions.X = ...
        var mvc = ExtractContext(context, "AddJsonOptions", "JsonSerializerOptions", onDiagnostic);

#pragma warning disable CS0618 // the merged view is kept for compatibility
        return new JsonOptionsExtractionResult
        {
            Mvc  = mvc,
            Http = http,
            PropertyNamingPolicy     = mvc.PropertyNamingPolicy ?? http.PropertyNamingPolicy,
            DictionaryKeyPolicy      = mvc.DictionaryKeyPolicy ?? http.DictionaryKeyPolicy,
            DefaultIgnoreCondition   = mvc.DefaultIgnoreCondition ?? http.DefaultIgnoreCondition,
            NumberHandling           = mvc.NumberHandling ?? http.NumberHandling,
            GlobalConverterTypeNames = [.. http.GlobalConverterTypeNames, .. mvc.GlobalConverterTypeNames],
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Reads every registration of one context (<paramref name="methodName"/>) in source order;
    /// a later assignment of the same option wins.
    /// </summary>
    private static JsonContextOptions ExtractContext(
        SourceAnalysisContext context,
        string methodName,
        string serializerOptionsPropertyName,
        Action<ExtractionDiagnostic>? onDiagnostic)
    {
        JsonNamingPolicy? propertyNamingPolicy = null;
        JsonNamingPolicy? dictionaryKeyPolicy = null;
        JsonIgnoreCondition? defaultIgnoreCondition = null;
        JsonNumberHandling? numberHandling = null;
        var converterTypeNames = new List<string>();
        var converterPolicies = new List<JsonNamingPolicy?>();

        foreach (var invocation in InvocationMatcher.FindInvocations(context, methodName))
        {
            var lambda = ExtractLambdaBody(invocation);
            if (lambda == null) continue;

            ParseOptionsBody(lambda, serializerOptionsPropertyName,
                ref propertyNamingPolicy,
                ref dictionaryKeyPolicy,
                ref defaultIgnoreCondition,
                ref numberHandling,
                converterTypeNames,
                converterPolicies,
                context,
                onDiagnostic);
        }

        return new JsonContextOptions
        {
            PropertyNamingPolicy     = propertyNamingPolicy,
            DictionaryKeyPolicy      = dictionaryKeyPolicy,
            DefaultIgnoreCondition   = defaultIgnoreCondition,
            NumberHandling           = numberHandling,
            GlobalConverterTypeNames = converterTypeNames,
            GlobalConverterEnumNamingPolicies = converterPolicies,
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lambda extraction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the body syntax node from the first lambda argument of an invocation.
    /// Handles both block lambdas <c>o => { ... }</c> and expression lambdas <c>o => expr</c>.
    /// </summary>
    private static Microsoft.CodeAnalysis.SyntaxNode? ExtractLambdaBody(
        InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        foreach (var arg in args)
        {
            if (arg.Expression is LambdaExpressionSyntax lambda)
                return lambda.Body;
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Options body parsing
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses assignments within a lambda body, scanning for known JSON option properties.
    /// </summary>
    /// <param name="body">The lambda body (BlockSyntax or ExpressionSyntax).</param>
    /// <param name="serializerOptionsPropertyName">
    /// The intermediate property name before the actual option:
    /// <c>"SerializerOptions"</c> for ConfigureHttpJsonOptions,
    /// <c>"JsonSerializerOptions"</c> for AddJsonOptions.
    /// </param>
    private static void ParseOptionsBody(
        Microsoft.CodeAnalysis.SyntaxNode body,
        string serializerOptionsPropertyName,
        ref JsonNamingPolicy? propertyNamingPolicy,
        ref JsonNamingPolicy? dictionaryKeyPolicy,
        ref JsonIgnoreCondition? defaultIgnoreCondition,
        ref JsonNumberHandling? numberHandling,
        List<string> converterTypeNames,
        List<JsonNamingPolicy?> converterPolicies,
        SourceAnalysisContext context,
        Action<ExtractionDiagnostic>? onDiagnostic)
    {
        // Scan all assignment expressions in the body, the body itself included: an expression
        // lambda (o => o.X = …) is the assignment.
        foreach (var assignment in body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax leftMae)
                continue;

            // The assigned property name (e.g. "PropertyNamingPolicy")
            var assignedPropName = leftMae.Name.Identifier.Text;

            // The receiver of the assignment must contain the serializerOptionsPropertyName
            // e.g. o.SerializerOptions or opts.JsonSerializerOptions
            // We check the receiver chain contains the expected intermediate property name.
            if (!ContainsPropertyInChain(leftMae.Expression, serializerOptionsPropertyName))
                continue;

            switch (assignedPropName)
            {
                case "PropertyNamingPolicy":
                    var namingPolicyValue = ParseNamingPolicy(assignment.Right);
                    if (namingPolicyValue.HasValue)
                        propertyNamingPolicy = namingPolicyValue.Value;
                    else
                        WarnNonLiteral(onDiagnostic, "PropertyNamingPolicy", assignment.Right);
                    break;

                case "DictionaryKeyPolicy":
                    var dictPolicyValue = ParseNamingPolicy(assignment.Right);
                    if (dictPolicyValue.HasValue)
                        dictionaryKeyPolicy = dictPolicyValue.Value;
                    else
                        WarnNonLiteral(onDiagnostic, "DictionaryKeyPolicy", assignment.Right);
                    break;

                case "DefaultIgnoreCondition":
                    var ignoreValue = ParseIgnoreCondition(assignment.Right);
                    if (ignoreValue.HasValue)
                        defaultIgnoreCondition = ignoreValue.Value;
                    else
                        WarnNonLiteral(onDiagnostic, "DefaultIgnoreCondition", assignment.Right);
                    break;

                case "NumberHandling":
                    var numberValue = ParseNumberHandling(assignment.Right);
                    if (numberValue.HasValue)
                        numberHandling = numberValue.Value;
                    else
                        WarnNonLiteral(onDiagnostic, "NumberHandling", assignment.Right);
                    break;
            }
        }

        // Scan Converters.Add(...) calls within the body, the body itself included: in an expression
        // lambda (o => o.X.Converters.Add(…)) the call is the body.
        foreach (var invocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax mae)
                continue;

            // Must be a call to .Add(...)
            if (mae.Name.Identifier.Text != "Add")
                continue;

            // The receiver of .Add must contain "Converters" somewhere in the chain
            // e.g. o.SerializerOptions.Converters.Add(...) or o.JsonSerializerOptions.Converters.Add(...)
            if (!ContainsPropertyInChain(mae.Expression, "Converters"))
                continue;

            // The argument to Add must be a new XxxConverter() object creation
            var args = invocation.ArgumentList.Arguments;
            if (args.Count != 1)
                continue;

            var arg = args[0].Expression;

            // Strip parentheses
            while (arg is ParenthesizedExpressionSyntax paren)
                arg = paren.Expression;

            string? converterTypeName = null;
            JsonNamingPolicy? converterPolicy = null;

            if (arg is ObjectCreationExpressionSyntax objCreation)
            {
                // Try semantic model first for FQN
                converterTypeName = TryGetFqnFromSemanticModel(objCreation.Type, context)
                    ?? GetUnqualifiedTypeName(objCreation.Type);
                if (!string.IsNullOrEmpty(converterTypeName))
                {
                    converterPolicy = ParseConverterEnumNamingPolicy(objCreation, converterTypeName!, out var unknown);
                    if (unknown != null)
                    {
                        DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
                        {
                            Code     = ExtractionDiagnosticCodes.JsonOptionsUnknownConverterNamingPolicy,
                            Message  = $"JsonOptions.Converters.Add({converterTypeName}): the naming policy {unknown} cannot be " +
                                       "determined statically; enum members are described by their names.",
                            Subjects = [converterTypeName!, unknown],
                        });
                    }
                }
            }
            else if (arg is ImplicitObjectCreationExpressionSyntax)
            {
                // new() — cannot determine type without semantic model
                DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
                {
                    Code    = ExtractionDiagnosticCodes.JsonOptionsUntypedConverter,
                    Message = "JsonOptions.Converters.Add(new()) — cannot determine converter type statically, skipped.",
                });
                continue;
            }

            if (!string.IsNullOrEmpty(converterTypeName))
            {
                converterTypeNames.Add(converterTypeName!);
                converterPolicies.Add(converterPolicy);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Chain membership check
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the expression chain (a sequence of MemberAccessExpressionSyntax)
    /// contains a member with the given name anywhere in the chain.
    /// </summary>
    private static bool ContainsPropertyInChain(
        Microsoft.CodeAnalysis.SyntaxNode expr,
        string propertyName)
    {
        var current = expr;
        while (current != null)
        {
            if (current is MemberAccessExpressionSyntax mae)
            {
                if (mae.Name.Identifier.Text == propertyName)
                    return true;
                current = mae.Expression;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Value parsers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The enum naming policy given to a string-enum converter's constructor or initializer:
    /// System.Text.Json's <c>JsonStringEnumConverter(namingPolicy)</c>; Newtonsoft's
    /// <c>StringEnumConverter(camelCaseText: true)</c>, a <c>NamingStrategy</c> instance or type, or
    /// <c>{ NamingStrategy = …, CamelCaseText = true }</c>. <see langword="null"/> when none is given,
    /// for other converters, and when the expression is not recognised — then
    /// <paramref name="unknown"/> holds its text.
    /// </summary>
    private static JsonNamingPolicy? ParseConverterEnumNamingPolicy(
        ObjectCreationExpressionSyntax creation, string converterTypeName, out string? unknown)
    {
        unknown = null;
        var shortName = converterTypeName.Split('[')[0].Split('`')[0];
        shortName = shortName[(shortName.LastIndexOf('.') + 1)..];
        var arguments = creation.ArgumentList?.Arguments ?? default;

        if (shortName == "JsonStringEnumConverter")
        {
            var policyArgument = arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "namingPolicy")
                ?? arguments.FirstOrDefault(a => a.NameColon == null);
            if (policyArgument == null)
                return null;

            var policy = ParseNamingPolicy(policyArgument.Expression);
            if (policy == null)
                unknown = policyArgument.Expression.ToString();
            return policy is JsonNamingPolicy.Preserve ? null : policy;
        }

        if (shortName != "StringEnumConverter")
            return null;

        // Newtonsoft 13 keeps one NamingStrategy; the constructor sets it, then each initializer
        // assignment in order, with the setters' semantics: the last one wins. "Unknown" is a strategy
        // the extractor cannot read — it names the members in some unknown way.
        var state = new StrategyState(null, null);
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var name = argument.NameColon?.Name.Identifier.Text;
            if (name is "allowIntegerValues" or "namingStrategyParameters")
                continue;
            if (argument.Expression is LiteralExpressionSyntax literal
                && (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression)))
            {
                // StringEnumConverter(bool camelCaseText): only a leading bool; a later one is allowIntegerValues.
                if (name == "camelCaseText" || (name == null && i == 0))
                    state = literal.IsKind(SyntaxKind.TrueLiteralExpression)
                        ? new StrategyState(JsonNamingPolicy.CamelCase, null)
                        : state;
                continue;
            }

            state = NamingStrategy(argument.Expression);
        }

        foreach (var assignment in creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? [])
        {
            switch ((assignment.Left as IdentifierNameSyntax)?.Identifier.Text)
            {
                case "NamingStrategy":
                    state = NamingStrategy(assignment.Right);
                    break;

                // CamelCaseText = true sets a camelCase strategy unless one is set; false clears a
                // camelCase strategy and leaves any other (an unknown one stays unknown).
                case "CamelCaseText" when assignment.Right is LiteralExpressionSyntax flag
                                          && flag.IsKind(SyntaxKind.TrueLiteralExpression):
                    state = new StrategyState(JsonNamingPolicy.CamelCase, null);
                    break;
                case "CamelCaseText" when assignment.Right is LiteralExpressionSyntax flag
                                          && flag.IsKind(SyntaxKind.FalseLiteralExpression):
                    if (state.Policy == JsonNamingPolicy.CamelCase)
                        state = new StrategyState(null, null);
                    break;
                case "CamelCaseText":
                    state = new StrategyState(null, assignment.Right.ToString());
                    break;
            }
        }

        unknown = state.Unknown;
        return state.Policy is JsonNamingPolicy.Preserve ? null : state.Policy;
    }

    /// <summary>
    /// The naming strategy of a Newtonsoft converter: its policy (<see cref="JsonNamingPolicy.Preserve"/>
    /// for <c>DefaultNamingStrategy</c>), none, or — when it cannot be read — the expression text.
    /// </summary>
    private readonly record struct StrategyState(JsonNamingPolicy? Policy, string? Unknown);

    /// <summary>
    /// A Newtonsoft naming strategy given as <c>new XNamingStrategy(…)</c>, <c>typeof(XNamingStrategy)</c>
    /// or <c>null</c> (no strategy); any other expression is unknown.
    /// </summary>
    private static StrategyState NamingStrategy(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax nullLiteral && nullLiteral.IsKind(SyntaxKind.NullLiteralExpression))
            return new StrategyState(null, null);

        var type = expression switch
        {
            ObjectCreationExpressionSyntax created => created.Type,
            TypeOfExpressionSyntax typeOf => typeOf.Type,
            _ => null,
        };
        var name = type?.ToString();
        name = name?[(name.LastIndexOf('.') + 1)..];
        JsonNamingPolicy? policy = name switch
        {
            "CamelCaseNamingStrategy" => JsonNamingPolicy.CamelCase,
            "SnakeCaseNamingStrategy" => JsonNamingPolicy.SnakeCaseLower,
            "KebabCaseNamingStrategy" => JsonNamingPolicy.KebabCaseLower,
            "DefaultNamingStrategy"   => JsonNamingPolicy.Preserve,
            _                         => null,
        };
        return policy != null ? new StrategyState(policy, null) : new StrategyState(null, expression.ToString());
    }

    /// <summary>
    /// Parses a naming policy from a member-access or null-literal expression.
    /// Returns null when the expression is not a recognisable literal pattern.
    /// </summary>
    private static JsonNamingPolicy? ParseNamingPolicy(ExpressionSyntax expr)
    {
        // null literal → Preserve
        if (expr is LiteralExpressionSyntax lit &&
            lit.Kind() == SyntaxKind.NullLiteralExpression)
        {
            return JsonNamingPolicy.Preserve;
        }

        // JsonNamingPolicy.CamelCase — or just CamelCase
        if (expr is MemberAccessExpressionSyntax mae)
        {
            return mae.Name.Identifier.Text switch
            {
                "CamelCase"      => JsonNamingPolicy.CamelCase,
                "SnakeCaseLower" => JsonNamingPolicy.SnakeCaseLower,
                "SnakeCaseUpper" => JsonNamingPolicy.SnakeCaseUpper,
                "KebabCaseLower" => JsonNamingPolicy.KebabCaseLower,
                "KebabCaseUpper" => JsonNamingPolicy.KebabCaseUpper,
                _                => (JsonNamingPolicy?)null,
            };
        }

        return null;
    }

    /// <summary>
    /// Parses a <see cref="JsonIgnoreCondition"/> from a member-access expression.
    /// Returns null when the expression is not a recognisable literal pattern.
    /// </summary>
    private static JsonIgnoreCondition? ParseIgnoreCondition(ExpressionSyntax expr)
    {
        if (expr is MemberAccessExpressionSyntax mae)
        {
            return mae.Name.Identifier.Text switch
            {
                "Never"              => JsonIgnoreCondition.Never,
                "Always"             => JsonIgnoreCondition.Always,
                "WhenWritingDefault" => JsonIgnoreCondition.WhenWritingDefault,
                "WhenWritingNull"    => JsonIgnoreCondition.WhenWritingNull,
                _                   => (JsonIgnoreCondition?)null,
            };
        }

        return null;
    }

    /// <summary>
    /// Parses a (possibly bitwise-OR-combined) <see cref="JsonNumberHandling"/> from an expression.
    /// Recursively unwraps <c>A | B | C</c> patterns.
    /// Returns null when the expression is not a recognisable literal pattern.
    /// </summary>
    private static JsonNumberHandling? ParseNumberHandling(ExpressionSyntax expr)
    {
        // Bitwise OR: A | B
        if (expr is BinaryExpressionSyntax binaryExpr
            && binaryExpr.Kind() == SyntaxKind.BitwiseOrExpression)
        {
            var left = ParseNumberHandling(binaryExpr.Left);
            var right = ParseNumberHandling(binaryExpr.Right);

            if (left == null && right == null) return null;
            if (left == null) return right;
            if (right == null) return left;
            return left.Value | right.Value;
        }

        if (expr is MemberAccessExpressionSyntax mae)
        {
            return mae.Name.Identifier.Text switch
            {
                "Strict"                         => JsonNumberHandling.Strict,
                "AllowReadingFromString"          => JsonNumberHandling.AllowReadingFromString,
                "WriteAsString"                   => JsonNumberHandling.WriteAsString,
                "AllowNamedFloatingPointLiterals" => JsonNumberHandling.AllowNamedFloatingPointLiterals,
                _                                => (JsonNumberHandling?)null,
            };
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Converter type name resolution
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to resolve the fully-qualified type name of a converter using the semantic model.
    /// Returns null when the semantic model is not available or resolution fails.
    /// </summary>
    private static string? TryGetFqnFromSemanticModel(
        Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax typeSyntax,
        SourceAnalysisContext context)
    {
        if (context.CompilationResult == null)
            return null;

        try
        {
            // Find the semantic model for the syntax tree containing this node
            var syntaxTree = typeSyntax.SyntaxTree;
            var semanticModel = context.CompilationResult.Compilation.GetSemanticModel(syntaxTree);
            var symbolInfo = semanticModel.GetSymbolInfo(typeSyntax);

            if (symbolInfo.Symbol is Microsoft.CodeAnalysis.INamedTypeSymbol typeSymbol)
            {
                var format = Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat
                    .WithGlobalNamespaceStyle(
                        Microsoft.CodeAnalysis.SymbolDisplayGlobalNamespaceStyle.Omitted);
                return typeSymbol.ToDisplayString(format);
            }
        }
        catch
        {
            // Semantic resolution is best-effort — fall back to syntactic
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static void WarnNonLiteral(
        Action<ExtractionDiagnostic>? onDiagnostic, string propName, ExpressionSyntax expr)
    {
        DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
        {
            Code     = ExtractionDiagnosticCodes.JsonOptionsNonLiteralSetting,
            Message  = $"JsonOptions.{propName} = {expr} — non-literal assignment cannot be resolved statically, skipped.",
            Subjects = [propName],
        });
    }
}
