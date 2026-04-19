using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Resolves <see cref="ViolationLocation"/> instances, including optional file and line
/// information from Roslyn syntax trees when source analysis is available.
/// </summary>
internal sealed class ViolationLocationResolver
{
    private readonly ValidationContext _context;

    // Cache: (className, methodName?, propertyName?) → (file, line)
    private readonly Dictionary<(string, string?, string?), (string? File, int? Line)> _cache
        = new();

    public ViolationLocationResolver(ValidationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Builds a location for an operation violation using the operation key (<c>"METHOD /path"</c>).
    /// </summary>
    public ViolationLocation? ForOperation(string operationKey)
    {
        if (_context.ActionByOperationKey == null)
            return null;

        if (!_context.ActionByOperationKey.TryGetValue(operationKey, out var info))
            return null;

        var className = info.Controller.Type.Name;
        var methodName = info.Action.Method.Name;
        var (file, line) = ResolveFileAndLine(className, methodName, null);
        return new ViolationLocation(className, methodName, null, file, line);
    }

    /// <summary>
    /// Builds a location for a schema-level violation (class description).
    /// </summary>
    public ViolationLocation? ForSchema(string schemaId)
    {
        if (_context.TypeBySchemaId == null)
            return ForSchemaStandalone(schemaId);

        if (!_context.TypeBySchemaId.TryGetValue(schemaId, out var type))
            return ForSchemaStandalone(schemaId);

        var className = type.Name;
        var (file, line) = ResolveFileAndLine(className, null, null);
        return new ViolationLocation(className, null, null, file, line);
    }

    /// <summary>
    /// Builds a location for a schema property violation.
    /// </summary>
    public ViolationLocation? ForSchemaProperty(string schemaId, string propertyName)
    {
        if (_context.TypeBySchemaId == null)
            return ForSchemaPropertyStandalone(schemaId, propertyName);

        if (!_context.TypeBySchemaId.TryGetValue(schemaId, out var type))
            return ForSchemaPropertyStandalone(schemaId, propertyName);

        var className = type.Name;
        var (file, line) = ResolveFileAndLine(className, null, propertyName);
        return new ViolationLocation(className, null, propertyName, file, line);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Standalone mode helpers (no CLR bindings — extract from schema IDs)
    // ─────────────────────────────────────────────────────────────────────────

    private static ViolationLocation? ForSchemaStandalone(string schemaId)
        => new ViolationLocation(schemaId, null, null, null, null);

    private static ViolationLocation? ForSchemaPropertyStandalone(string schemaId, string propertyName)
        => new ViolationLocation(schemaId, null, propertyName, null, null);

    // ─────────────────────────────────────────────────────────────────────────
    // Roslyn file+line resolution
    // ─────────────────────────────────────────────────────────────────────────

    private (string? File, int? Line) ResolveFileAndLine(
        string? className,
        string? methodName,
        string? propertyName)
    {
        if (className == null) return (null, null);
        if (_context.SourceContext?.IsAvailable != true) return (null, null);

        var key = (className, methodName, propertyName);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = ResolveFileAndLineCore(className, methodName, propertyName);
        _cache[key] = result;
        return result;
    }

    private (string? File, int? Line) ResolveFileAndLineCore(
        string className,
        string? methodName,
        string? propertyName)
    {
        var compilation = _context.SourceContext?.CompilationResult;
        if (compilation == null) return (null, null);

        // Collect ALL classes with the matching short name across all syntax trees.
        // If more than one match is found, the result is ambiguous — return null
        // rather than silently returning the wrong file/line.
        var matches = new List<(SyntaxTree Tree, ClassDeclarationSyntax Decl)>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var classDecl = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == className);

            if (classDecl != null)
                matches.Add((tree, classDecl));
        }

        // None or ambiguous — better no location than a wrong location.
        if (matches.Count != 1) return (null, null);

        var (matchedTree, matchedDecl) = matches[0];

        if (methodName != null)
        {
            var method = matchedDecl.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == methodName);

            if (method != null)
            {
                var span = method.SyntaxTree.GetLineSpan(method.Span);
                return (matchedTree.FilePath, span.StartLinePosition.Line + 1);
            }
        }

        if (propertyName != null)
        {
            // Try exact match first, then case-insensitive to handle camelCase schema keys
            // (e.g. "email") matching PascalCase C# property identifiers (e.g. "Email").
            var prop = matchedDecl.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(p =>
                    string.Equals(p.Identifier.Text, propertyName, StringComparison.Ordinal)
                    || string.Equals(p.Identifier.Text, propertyName, StringComparison.OrdinalIgnoreCase));

            if (prop != null)
            {
                var span = prop.SyntaxTree.GetLineSpan(prop.Span);
                return (matchedTree.FilePath, span.StartLinePosition.Line + 1);
            }
        }

        // Class-level only
        var clsSpan = matchedDecl.SyntaxTree.GetLineSpan(matchedDecl.Span);
        return (matchedTree.FilePath, clsSpan.StartLinePosition.Line + 1);
    }
}
