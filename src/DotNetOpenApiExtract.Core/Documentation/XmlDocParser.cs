using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotNetOpenApiExtract.Core.Documentation;

/// <summary>
/// Parsed XML documentation for a single member.
/// </summary>
public sealed class XmlDocEntry
{
    /// <summary>The &lt;summary&gt; text.</summary>
    public string? Summary { get; init; }

    /// <summary>The &lt;remarks&gt; text.</summary>
    public string? Remarks { get; init; }

    /// <summary>Parameter descriptions: name → description text.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Response descriptions: status code → description text.</summary>
    public IReadOnlyDictionary<string, string> Responses { get; init; } =
        new Dictionary<string, string>();

    /// <summary>The &lt;example&gt; text, if present.</summary>
    public string? Example { get; init; }

    /// <summary>Parameter examples: name → example value string.</summary>
    public IReadOnlyDictionary<string, string> ParameterExamples { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Parses XML documentation files (.xml) produced by the C# compiler.
/// Thread-safe for reads after construction.
/// </summary>
public sealed class XmlDocParser
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private readonly Dictionary<string, XmlDocEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Load and parse an XML documentation file.
    /// </summary>
    /// <param name="xmlPath">Path to the .xml file. If null or non-existent, creates an empty parser.</param>
    public XmlDocParser(string? xmlPath)
    {
        if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
            return;

        XDocument doc;
        try
        {
            doc = XDocument.Load(xmlPath);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            // XML doc is optional — proceed without it if the file is malformed or locked
            return;
        }

        var members = doc.Descendants("member");

        foreach (var member in members)
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            _entries[name] = ParseMember(member);
        }
    }

    /// <summary>Get docs for a type (class, struct, enum).</summary>
    /// <remarks>
    /// For closed generic types (e.g. <c>ApiResponse&lt;UserDto&gt;</c>) the compiler emits
    /// the XML doc entry under the open generic definition's FullName
    /// (e.g. <c>T:SampleApi.Models.ApiResponse`1</c>). When the direct lookup by the
    /// closed type's FullName misses, this method retries with the open generic definition.
    /// </remarks>
    public XmlDocEntry? GetTypeDoc(Type type)
    {
        // Reflection emits nested types with '+' (e.g. "Outer+Inner") but the C# XML doc
        // compiler uses '.' — normalize so nested-type lookups succeed.
        var key = $"T:{NormalizeTypeName(type)}";
        if (_entries.TryGetValue(key, out var entry))
            return entry;

        // Fallback for closed generic types: retry with the open generic definition's FullName.
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var openKey = $"T:{NormalizeTypeName(type.GetGenericTypeDefinition())}";
            return _entries.GetValueOrDefault(openKey);
        }

        return null;
    }

    /// <summary>Get docs for a method.</summary>
    public XmlDocEntry? GetMethodDoc(MethodInfo method)
    {
        var key = BuildMethodKey(method);
        return _entries.GetValueOrDefault(key);
    }

    /// <summary>Get docs for a property.</summary>
    /// <remarks>
    /// For closed generic types (e.g. <c>ApiResponse&lt;UserDto&gt;</c>) the compiler emits
    /// the XML doc entry under the open generic definition's FullName. When the direct lookup
    /// misses, this method retries with the open generic definition.
    /// </remarks>
    public XmlDocEntry? GetPropertyDoc(Type declaringType, string propertyName)
    {
        // Normalize '+' → '.' so nested-type property lookups succeed.
        var key = $"P:{NormalizeTypeName(declaringType)}.{propertyName}";
        if (_entries.TryGetValue(key, out var entry))
            return entry;

        // Fallback for closed generic types.
        if (declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition)
        {
            var openKey = $"P:{NormalizeTypeName(declaringType.GetGenericTypeDefinition())}.{propertyName}";
            return _entries.GetValueOrDefault(openKey);
        }

        return null;
    }

    /// <summary>Get docs for an enum field.</summary>
    /// <remarks>
    /// For closed generic types, retries with the open generic definition's FullName when
    /// the direct lookup misses (same pattern as <see cref="GetPropertyDoc"/>).
    /// </remarks>
    public XmlDocEntry? GetFieldDoc(Type declaringType, string fieldName)
    {
        // Normalize '+' → '.' so nested-type field lookups succeed.
        var key = $"F:{NormalizeTypeName(declaringType)}.{fieldName}";
        if (_entries.TryGetValue(key, out var entry))
            return entry;

        // Fallback for closed generic types.
        if (declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition)
        {
            var openKey = $"F:{NormalizeTypeName(declaringType.GetGenericTypeDefinition())}.{fieldName}";
            return _entries.GetValueOrDefault(openKey);
        }

        return null;
    }

    private static string BuildMethodKey(MethodInfo method)
    {
        var declaringType = method.DeclaringType!;
        var parameters = method.GetParameters();

        // Normalize '+' → '.' so nested-type method lookups succeed.
        if (parameters.Length == 0)
            return $"M:{NormalizeTypeName(declaringType)}.{method.Name}";

        // Use StringBuilder to avoid LINQ iterator allocation (I2).
        var sb = new System.Text.StringBuilder();
        sb.Append("M:");
        sb.Append(NormalizeTypeName(declaringType));
        sb.Append('.');
        sb.Append(method.Name);
        sb.Append('(');
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(GetXmlTypeName(parameters[i].ParameterType));
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Convert a <see cref="Type"/> to the XML documentation name format.
    /// Generics use curly braces, arrays keep their [] suffix.
    /// </summary>
    private static string GetXmlTypeName(Type type)
    {
        // Array types: recurse on element type and append rank-aware suffix
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            int rank = type.GetArrayRank();
            var suffix = rank == 1
                ? "[]"
                : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]";
            return $"{GetXmlTypeName(elementType)}{suffix}";
        }

        // Generic types (including Nullable<T>)
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            // FullName of generic definition contains backtick: e.g. System.Nullable`1
            // Strip the `N suffix to get the base name, and normalize '+' → '.' for nested types.
            var fullName = NormalizeTypeName(genericDef);
            var backtickIndex = fullName.IndexOf('`');
            var baseName = backtickIndex >= 0 ? fullName[..backtickIndex] : fullName;

            var typeArgs = string.Join(",", type.GetGenericArguments().Select(GetXmlTypeName));
            return $"{baseName}{{{typeArgs}}}";
        }

        // Simple types — normalize '+' → '.' so nested-type parameter names match XML keys.
        return NormalizeTypeName(type);
    }

    /// <summary>
    /// Converts a type's FullName to the format used by the C# XML doc compiler.
    /// Reflection emits nested types with '+' (e.g. "Outer+Inner") while the compiler
    /// emits 'T:Outer.Inner' — replacing '+' with '.' makes lookups succeed.
    /// </summary>
    private static string NormalizeTypeName(Type type) =>
        (type.FullName ?? type.Name).Replace('+', '.');

    private static XmlDocEntry ParseMember(XElement member)
    {
        var summary = GetInnerText(member.Element("summary"));
        var remarks = GetInnerText(member.Element("remarks"));
        var example = GetInnerText(member.Element("example"));

        var parameters = new Dictionary<string, string>();
        var paramExamples = new Dictionary<string, string>();
        foreach (var param in member.Elements("param"))
        {
            var name = param.Attribute("name")?.Value;
            if (name == null) continue;

            var text = GetInnerText(param);
            if (text != null) parameters[name] = text;

            var exampleAttr = param.Attribute("example")?.Value;
            if (exampleAttr != null) paramExamples[name] = exampleAttr;
        }

        var responses = new Dictionary<string, string>();
        foreach (var response in member.Elements("response"))
        {
            var code = response.Attribute("code")?.Value;
            if (code == null) continue;

            var text = GetInnerText(response);
            if (text != null) responses[code] = text;
        }

        return new XmlDocEntry
        {
            Summary = summary,
            Remarks = remarks,
            Example = example,
            Parameters = parameters,
            ParameterExamples = paramExamples,
            Responses = responses,
        };
    }

    private static string? GetInnerText(XElement? element)
    {
        if (element == null) return null;

        // Build text content via StringBuilder to avoid LINQ closure allocation (I3).
        var sb = new System.Text.StringBuilder();
        AppendInnerText(element, sb);
        var text = sb.ToString();

        // Normalize whitespace: collapse runs of whitespace into a single space, trim ends
        text = WhitespaceRegex.Replace(text.Trim(), " ");

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void AppendInnerText(XElement element, System.Text.StringBuilder sb)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText textNode)
            {
                sb.Append(textNode.Value);
            }
            else if (node is XElement childElement)
            {
                switch (childElement.Name.LocalName)
                {
                    case "see":
                        sb.Append(childElement.Attribute("cref")?.Value?.Split('.').Last() ?? childElement.Value);
                        break;
                    case "paramref":
                    case "typeparamref":
                        sb.Append(childElement.Attribute("name")?.Value ?? childElement.Value);
                        break;
                    case "c":
                    case "code":
                        sb.Append(childElement.Value);
                        break;
                    case "para":
                        sb.Append(' ');
                        AppendInnerText(childElement, sb);
                        break;
                    default:
                        AppendInnerText(childElement, sb);
                        break;
                }
            }
            // Other node types (XComment etc.) produce no text.
        }
    }
}
