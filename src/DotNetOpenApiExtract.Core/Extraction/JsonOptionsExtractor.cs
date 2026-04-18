using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using static DotNetOpenApiExtract.Core.SourceAnalysis.TypeSyntaxHelper;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Result of scanning Roslyn source for JSON serializer option registrations.
/// </summary>
public sealed class JsonOptionsExtractionResult
{
    /// <summary>
    /// The effective property naming policy, if detected.
    /// <see langword="null"/> means not detected — caller should fall back to CLI/defaults.
    /// </summary>
    public JsonNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>
    /// The effective dictionary key policy, if detected.
    /// <see langword="null"/> means not detected.
    /// </summary>
    public JsonNamingPolicy? DictionaryKeyPolicy { get; init; }

    /// <summary>
    /// The global default ignore condition, if detected.
    /// </summary>
    public JsonIgnoreCondition? DefaultIgnoreCondition { get; init; }

    /// <summary>
    /// The global number handling flags, if detected.
    /// </summary>
    public JsonNumberHandling? NumberHandling { get; init; }

    /// <summary>
    /// Globally registered converter type names (short or FQN) collected from
    /// <c>options.Converters.Add(new XxxConverter())</c> calls.
    /// Consumed by T6 registry lookup.
    /// </summary>
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
    {
        if (!context.IsAvailable || context.EntryPointNode == null)
            return new JsonOptionsExtractionResult();

        JsonNamingPolicy? propertyNamingPolicy = null;
        JsonNamingPolicy? dictionaryKeyPolicy = null;
        JsonIgnoreCondition? defaultIgnoreCondition = null;
        JsonNumberHandling? numberHandling = null;
        var converterTypeNames = new List<string>();

        // ── 1. ConfigureHttpJsonOptions ────────────────────────────────────────
        // Pattern: builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; })
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "ConfigureHttpJsonOptions"))
        {
            var lambda = ExtractLambdaBody(invocation);
            if (lambda == null) continue;

            ParseOptionsBody(lambda, "SerializerOptions",
                ref propertyNamingPolicy,
                ref dictionaryKeyPolicy,
                ref defaultIgnoreCondition,
                ref numberHandling,
                converterTypeNames,
                context);
        }

        // ── 2. AddJsonOptions ──────────────────────────────────────────────────
        // Pattern: .AddControllers().AddJsonOptions(o => { o.JsonSerializerOptions.PropertyNamingPolicy = ...; })
        foreach (var invocation in InvocationMatcher.FindInvocations(context, "AddJsonOptions"))
        {
            var lambda = ExtractLambdaBody(invocation);
            if (lambda == null) continue;

            ParseOptionsBody(lambda, "JsonSerializerOptions",
                ref propertyNamingPolicy,
                ref dictionaryKeyPolicy,
                ref defaultIgnoreCondition,
                ref numberHandling,
                converterTypeNames,
                context);
        }

        return new JsonOptionsExtractionResult
        {
            PropertyNamingPolicy    = propertyNamingPolicy,
            DictionaryKeyPolicy     = dictionaryKeyPolicy,
            DefaultIgnoreCondition  = defaultIgnoreCondition,
            NumberHandling          = numberHandling,
            GlobalConverterTypeNames = converterTypeNames,
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
        SourceAnalysisContext context)
    {
        // Scan all assignment expressions in the body.
        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
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
                        WarnNonLiteral("PropertyNamingPolicy", assignment.Right);
                    break;

                case "DictionaryKeyPolicy":
                    var dictPolicyValue = ParseNamingPolicy(assignment.Right);
                    if (dictPolicyValue.HasValue)
                        dictionaryKeyPolicy = dictPolicyValue.Value;
                    else
                        WarnNonLiteral("DictionaryKeyPolicy", assignment.Right);
                    break;

                case "DefaultIgnoreCondition":
                    var ignoreValue = ParseIgnoreCondition(assignment.Right);
                    if (ignoreValue.HasValue)
                        defaultIgnoreCondition = ignoreValue.Value;
                    else
                        WarnNonLiteral("DefaultIgnoreCondition", assignment.Right);
                    break;

                case "NumberHandling":
                    var numberValue = ParseNumberHandling(assignment.Right);
                    if (numberValue.HasValue)
                        numberHandling = numberValue.Value;
                    else
                        WarnNonLiteral("NumberHandling", assignment.Right);
                    break;
            }
        }

        // Scan Converters.Add(...) calls within the body.
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
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

            if (arg is ObjectCreationExpressionSyntax objCreation)
            {
                // Try semantic model first for FQN
                converterTypeName = TryGetFqnFromSemanticModel(objCreation.Type, context)
                    ?? GetUnqualifiedTypeName(objCreation.Type);
            }
            else if (arg is ImplicitObjectCreationExpressionSyntax)
            {
                // new() — cannot determine type without semantic model
                Console.Error.WriteLine(
                    "Warning: JsonOptions.Converters.Add(new()) — cannot determine converter type statically, skipped.");
                continue;
            }

            if (!string.IsNullOrEmpty(converterTypeName))
                converterTypeNames.Add(converterTypeName!);
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

    private static void WarnNonLiteral(string propName, ExpressionSyntax expr)
    {
        Console.Error.WriteLine(
            $"Warning: JsonOptions.{propName} = {expr} — non-literal assignment cannot be resolved statically, skipped.");
    }
}
