using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.SourceAnalysis;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Extracts global <c>[Produces]</c> and <c>[Consumes]</c> content-type defaults
/// registered as MVC filters in the application entry point.
/// </summary>
/// <remarks>
/// Scans for patterns like
/// <c>builder.Services.AddControllers(o =&gt; o.Filters.Add(new ProducesAttribute("application/json")))</c>
/// and similar <c>AddMvc</c> variants. If source analysis is not available or no
/// matching pattern is found, the result has empty lists.
/// </remarks>
public static class GlobalMediaTypesExtractor
{
    /// <summary>
    /// Scans the Roslyn context for global MVC filter registrations like
    /// <c>AddControllers(o =&gt; o.Filters.Add(new ProducesAttribute("application/json")))</c>
    /// and returns the default content types.
    /// </summary>
    /// <param name="context">
    /// The source analysis context. When <see cref="SourceAnalysisContext.IsAvailable"/> is
    /// <see langword="false"/> or <see cref="SourceAnalysisContext.EntryPointNode"/> is
    /// <see langword="null"/>, both lists in the result are empty.
    /// </param>
    /// <returns>
    /// A <see cref="GlobalMediaTypesExtractionResult"/> with content types found in
    /// global filter registrations.
    /// </returns>
    public static GlobalMediaTypesExtractionResult Extract(SourceAnalysisContext context)
    {
        if (!context.IsAvailable || context.EntryPointNode is null)
            return GlobalMediaTypesExtractionResult.Empty;

        var producesTypes = new List<string>();
        var consumesTypes = new List<string>();

        // Search for AddControllers(...) and AddMvc(...) invocations.
        foreach (var methodName in new[] { "AddControllers", "AddMvc" })
        {
            foreach (var invocation in InvocationMatcher.FindInvocations(context, methodName))
            {
                // The single argument must be a lambda: o => o.Filters.Add(new ProducesAttribute(...))
                // We look for all Filters.Add(...) calls nested inside this invocation.
                CollectFiltersFromInvocation(invocation, producesTypes, consumesTypes);
            }
        }

        return new GlobalMediaTypesExtractionResult
        {
            ProducesContentTypes = producesTypes.AsReadOnly(),
            ConsumesContentTypes = consumesTypes.AsReadOnly(),
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a single <c>AddControllers(...)</c> / <c>AddMvc(...)</c> invocation for
    /// nested <c>Filters.Add(new XxxAttribute(...))</c> calls and extracts the
    /// content-type strings from their constructor arguments.
    /// </summary>
    private static void CollectFiltersFromInvocation(
        InvocationExpressionSyntax addControllersCall,
        List<string> producesTypes,
        List<string> consumesTypes)
    {
        // Walk all descendant invocations looking for: <receiver>.Add(<object creation>)
        foreach (var inner in addControllersCall.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // The called method must be "Add"
            if (inner.Expression is not MemberAccessExpressionSyntax mae)
                continue;

            if (mae.Name.Identifier.Text != "Add")
                continue;

            // The receiver must end in ".Filters"
            if (!EndsWithFilters(mae.Expression))
                continue;

            // The single argument must be an object creation expression.
            var args = inner.ArgumentList.Arguments;
            if (args.Count != 1)
                continue;

            var arg = args[0].Expression;
            if (arg is not ObjectCreationExpressionSyntax objCreation)
                continue;

            // Determine type: ProducesAttribute/Produces or ConsumesAttribute/Consumes
            var typeName = GetLastIdentifier(objCreation.Type);
            if (typeName is null)
                continue;

            bool isProduces = typeName is "ProducesAttribute" or "Produces";
            bool isConsumes = typeName is "ConsumesAttribute" or "Consumes";

            if (!isProduces && !isConsumes)
                continue;

            var contentTypes = ExtractStringArguments(objCreation);
            if (isProduces)
                producesTypes.AddRange(contentTypes);
            else
                consumesTypes.AddRange(contentTypes);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the expression ends with a <c>.Filters</c>
    /// member access (e.g. <c>o.Filters</c>, <c>options.Filters</c>).
    /// </summary>
    private static bool EndsWithFilters(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.Text == "Filters",
            IdentifierNameSyntax id          => id.Identifier.Text == "Filters",
            _                                => false,
        };
    }

    /// <summary>
    /// Extracts all string literal constructor arguments from an
    /// <see cref="ObjectCreationExpressionSyntax"/>. The first positional argument and any
    /// additional <c>params</c> arguments are all collected. Non-literal arguments produce
    /// a warning and are skipped.
    /// </summary>
    private static IEnumerable<string> ExtractStringArguments(ObjectCreationExpressionSyntax objCreation)
    {
        if (objCreation.ArgumentList is null)
            yield break;

        foreach (var arg in objCreation.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax lit && lit.Token.Value is string s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    yield return s;
            }
            else
            {
                // Non-literal argument — cannot resolve statically.
                Console.Error.WriteLine(
                    $"Warning: Non-literal content-type argument in {GetLastIdentifier(objCreation.Type) ?? "attribute"} constructor. " +
                    "Global media type entry will be skipped.");
            }
        }
    }

    /// <summary>
    /// Returns the rightmost simple identifier text from a type syntax node.
    /// For <c>Microsoft.AspNetCore.Mvc.ProducesAttribute</c> returns <c>"ProducesAttribute"</c>.
    /// For <c>ProducesAttribute</c> returns <c>"ProducesAttribute"</c>.
    /// </summary>
    private static string? GetLastIdentifier(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax id              => id.Identifier.Text,
            QualifiedNameSyntax qualified         => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax aliasQual    => aliasQual.Name.Identifier.Text,
            _                                     => null,
        };
    }
}

/// <summary>
/// The result of <see cref="GlobalMediaTypesExtractor.Extract"/>.
/// </summary>
public sealed class GlobalMediaTypesExtractionResult
{
    /// <summary>Content types from global <c>ProducesAttribute</c> filters.</summary>
    public IReadOnlyList<string> ProducesContentTypes { get; init; } = [];

    /// <summary>Content types from global <c>ConsumesAttribute</c> filters.</summary>
    public IReadOnlyList<string> ConsumesContentTypes { get; init; } = [];

    /// <summary>
    /// The empty/unavailable singleton returned when source analysis is not possible.
    /// </summary>
    internal static readonly GlobalMediaTypesExtractionResult Empty = new();
}
