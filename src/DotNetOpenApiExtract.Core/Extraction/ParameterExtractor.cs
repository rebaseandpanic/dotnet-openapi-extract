using System.Reflection;
using System.Text.RegularExpressions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Where a parameter value comes from in the HTTP request.
/// </summary>
public enum ParameterLocation
{
    Path,
    Query,
    Header,
    Body,
    Form
}

/// <summary>
/// Extracted parameter information from a controller action method.
/// </summary>
public sealed class ActionParameterInfo
{
    /// <summary>Parameter name (from attribute Name property or C# parameter name).</summary>
    public required string Name { get; init; }

    /// <summary>Where the parameter comes from (path, query, header, body, form).</summary>
    public required ParameterLocation Location { get; init; }

    /// <summary>The CLR type of the parameter.</summary>
    public required Type Type { get; init; }

    /// <summary>Whether the parameter is required.</summary>
    public required bool IsRequired { get; init; }

    /// <summary>Default value, if the parameter has one.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>Description from [SwaggerRequestBody], [SwaggerParameter], or [Description] attribute, in that priority order.</summary>
    public string? Description { get; init; }

    /// <summary>The original System.Reflection.ParameterInfo for advanced inspection.</summary>
    public required System.Reflection.ParameterInfo ReflectionParameter { get; init; }
}

/// <summary>
/// Extracts parameter information from controller action methods loaded via
/// <c>MetadataLoadContext</c> (reflection-only). All type and attribute inspection
/// is performed through <see cref="AttributeHelper"/> and FullName comparisons to
/// avoid instantiating types from the foreign assembly.
/// </summary>
public static class ParameterExtractor
{
    // Matches a single route parameter token, e.g. {id}, {id:int}, {*slug}.
    // Group 1 captures the plain parameter name (without constraints or catch-all markers).
    private static readonly Regex RouteParamRegex =
        new(@"\{(?:\*{1,2})?([^:}*?]+)\?*(?::[^}]*)?\}", RegexOptions.Compiled);

    // Full names of framework types that are injected by ASP.NET Core and must be skipped.
    private static readonly HashSet<string> SkippedTypeFullNames = new(StringComparer.Ordinal)
    {
        "System.Threading.CancellationToken",
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.AspNetCore.Http.HttpRequest",
        "Microsoft.AspNetCore.Http.HttpResponse",
    };

    // Simple types treated as scalars for [ApiController] binding inference.
    private static readonly HashSet<string> SimpleTypeFullNames = new(StringComparer.Ordinal)
    {
        "System.String",
        "System.Boolean",
        "System.Byte",
        "System.SByte",
        "System.Int16",
        "System.UInt16",
        "System.Int32",
        "System.UInt32",
        "System.Int64",
        "System.UInt64",
        "System.Single",
        "System.Double",
        "System.Decimal",
        "System.DateTime",
        "System.DateTimeOffset",
        "System.DateOnly",
        "System.TimeOnly",
        "System.TimeSpan",
        "System.Guid",
        "System.Uri",
        "System.Char",
    };

    /// <summary>
    /// Extracts parameter descriptors for every bindable parameter in <paramref name="action"/>.
    /// Parameters injected by ASP.NET Core (<c>CancellationToken</c>, <c>HttpContext</c>, etc.)
    /// and parameters decorated with <c>[FromServices]</c> are excluded automatically.
    /// </summary>
    /// <param name="action">The action whose method parameters are to be extracted.</param>
    /// <returns>
    /// An ordered, read-only list of <see cref="ParameterInfo"/> descriptors, one per
    /// bindable parameter.
    /// </returns>
    public static IReadOnlyList<ActionParameterInfo> ExtractParameters(ActionInfo action)
    {
        var reflectionParams = action.Method.GetParameters();
        var result = new List<ActionParameterInfo>(reflectionParams.Length);

        // Pre-compute the set of route parameter names present in either the action or
        // controller route template so inference can be done with a fast HashSet lookup.
        var routeParamNames = CollectRouteParameterNames(
            action.RouteTemplate,
            action.Controller.RouteTemplate);

        foreach (var param in reflectionParams)
        {
            // --- 1. Skip service-injected parameters and unnamed parameters ---
            if (ShouldSkip(param))
                continue;

            if (string.IsNullOrEmpty(param.Name))
                continue;

            // --- 2. Determine binding location ---
            var location = DetermineLocation(param, routeParamNames);

            // --- 3. Resolve the effective parameter name ---
            var name = ResolveName(param, location);

            // --- 4. Determine whether the parameter is required ---
            bool isRequired = DetermineIsRequired(param, location);

            // --- 5. Default value ---
            // [DefaultValue(x)] attribute takes precedence over the compiler-inferred inline default.
            // MetadataLoadContext types throw on param.DefaultValue; use RawDefaultValue instead.
            object? defaultValue = null;
            var defaultValueAttr = AttributeHelper.GetAttribute(param, AttributeHelper.Names.DefaultValue);
            if (defaultValueAttr != null)
                defaultValue = AttributeHelper.GetConstructorArgument<object>(defaultValueAttr, 0);
            else if (param.HasDefaultValue)
                defaultValue = param.RawDefaultValue;

            // --- 6. Description ---
            string? description = ResolveDescription(param);

            result.Add(new ActionParameterInfo
            {
                Name = name,
                Location = location,
                Type = param.ParameterType,
                IsRequired = isRequired,
                DefaultValue = defaultValue,
                Description = description,
                ReflectionParameter = param,
            });
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when the parameter should be excluded from
    /// the extracted parameter list (e.g. service-injected or framework infrastructure).
    /// </summary>
    private static bool ShouldSkip(System.Reflection.ParameterInfo param)
    {
        // [FromServices] → always skip
        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.FromServices))
            return true;

        // Unwrap Nullable<T> before checking the FullName so that
        // CancellationToken? is also skipped correctly.
        var effectiveType = UnwrapNullable(param.ParameterType);

        return SkippedTypeFullNames.Contains(effectiveType.FullName ?? string.Empty);
    }

    /// <summary>
    /// Determines the <see cref="ParameterLocation"/> for <paramref name="param"/> by
    /// checking explicit binding attributes first, then falling back to [ApiController]
    /// inference rules.
    /// </summary>
    private static ParameterLocation DetermineLocation(
        System.Reflection.ParameterInfo param,
        HashSet<string> routeParamNames)
    {
        // Explicit binding attributes take absolute precedence.
        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.FromRoute))
            return ParameterLocation.Path;

        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.FromQuery))
            return ParameterLocation.Query;

        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.FromHeader))
            return ParameterLocation.Header;

        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.FromBody))
            return ParameterLocation.Body;

        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.FromForm))
            return ParameterLocation.Form;

        // --- [ApiController] inference ---

        // If the parameter name appears in the combined route template → Path
        if (routeParamNames.Contains(param.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            return ParameterLocation.Path;

        // IFormFile / IFormFileCollection → Form (ASP.NET Core binds these from form automatically)
        if (IsFormFileType(param.ParameterType))
            return ParameterLocation.Form;

        // Complex type (not a simple/scalar type) → Body
        if (!IsSimpleType(param.ParameterType))
            return ParameterLocation.Body;

        // Scalar type with no matching route segment → Query
        return ParameterLocation.Query;
    }

    /// <summary>
    /// Resolves the effective parameter name. For binding attributes that expose a
    /// <c>Name</c> named argument (e.g. <c>[FromQuery(Name = "page_size")]</c>) the
    /// attribute value is preferred; otherwise the C# parameter name is used.
    /// </summary>
    private static string ResolveName(System.Reflection.ParameterInfo param, ParameterLocation location)
    {
        // Only [FromRoute], [FromQuery], and [FromHeader] support a Name override.
        string? attrName = location switch
        {
            ParameterLocation.Path => GetNameFromBindingAttribute(param, AttributeHelper.Names.FromRoute),
            ParameterLocation.Query => GetNameFromBindingAttribute(param, AttributeHelper.Names.FromQuery),
            ParameterLocation.Header => GetNameFromBindingAttribute(param, AttributeHelper.Names.FromHeader),
            _ => null,
        };

        return attrName ?? param.Name ?? string.Empty;
    }

    /// <summary>
    /// Reads the <c>Name</c> named argument from a binding attribute, if present.
    /// Returns <see langword="null"/> when the attribute is absent or the argument was
    /// not explicitly set.
    /// </summary>
    private static string? GetNameFromBindingAttribute(
        System.Reflection.ParameterInfo param,
        string attributeFullName)
    {
        var attr = AttributeHelper.GetAttribute(param, attributeFullName);
        if (attr == null)
            return null;

        // [FromQuery(Name = "x")] uses a named argument.
        var named = AttributeHelper.GetNamedArgument<string>(attr, "Name");
        if (!string.IsNullOrEmpty(named))
            return named;

        // Some overloads pass the name as a constructor argument (uncommon but valid).
        var ctor = AttributeHelper.GetConstructorArgument<string>(attr, 0);
        return string.IsNullOrEmpty(ctor) ? null : ctor;
    }

    /// <summary>
    /// Determines whether a parameter is required according to OpenAPI semantics.
    /// </summary>
    private static bool DetermineIsRequired(System.Reflection.ParameterInfo param, ParameterLocation location)
    {
        // Path parameters are unconditionally required by the OpenAPI specification.
        if (location == ParameterLocation.Path)
            return true;

        // [Required] data-annotation → required regardless of type or default.
        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.Required))
            return true;

        // A parameter with a default value is optional by definition.
        if (param.HasDefaultValue)
            return false;

        // [DefaultValue] attribute also makes the parameter optional — a default
        // value implies the caller may omit the parameter.
        if (AttributeHelper.HasAttribute(param, AttributeHelper.Names.DefaultValue))
            return false;

        // Nullable<T> value types are optional.
        if (IsNullableType(param.ParameterType))
            return false;

        // Reference types: check NRT annotations to determine nullability.
        if (!param.ParameterType.IsValueType)
            return !IsNullableReferenceParameter(param);

        // Non-nullable value type without a default → required.
        return true;
    }

    /// <summary>
    /// Reads the parameter description from <c>[SwaggerRequestBody]</c> (constructor arg 0),
    /// <c>[SwaggerParameter]</c> (constructor arg 0), or <c>[Description]</c> (constructor arg 0),
    /// in that priority order.
    /// </summary>
    private static string? ResolveDescription(System.Reflection.ParameterInfo param)
    {
        var swaggerRequestBody = AttributeHelper.GetAttribute(param, AttributeHelper.Names.SwaggerRequestBody);
        if (swaggerRequestBody != null)
        {
            var desc = AttributeHelper.GetConstructorArgument<string>(swaggerRequestBody, 0);
            if (string.IsNullOrEmpty(desc))
                desc = AttributeHelper.GetNamedArgument<string>(swaggerRequestBody, "Description");
            if (!string.IsNullOrEmpty(desc))
                return desc;
        }

        var swaggerParam = AttributeHelper.GetAttribute(param, AttributeHelper.Names.SwaggerParameter);
        if (swaggerParam != null)
        {
            var desc = AttributeHelper.GetConstructorArgument<string>(swaggerParam, 0);
            if (!string.IsNullOrEmpty(desc))
                return desc;
        }

        var descAttr = AttributeHelper.GetAttribute(param, AttributeHelper.Names.Description);
        if (descAttr != null)
        {
            var desc = AttributeHelper.GetConstructorArgument<string>(descAttr, 0);
            if (!string.IsNullOrEmpty(desc))
                return desc;
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is a simple / scalar
    /// type that ASP.NET Core binds from the query string or route rather than the body.
    /// Handles <c>Nullable&lt;T&gt;</c> by unwrapping the underlying type first.
    /// Works correctly with <c>MetadataLoadContext</c> types.
    /// </summary>
    private static bool IsSimpleType(Type type)
    {
        var underlying = UnwrapNullable(type);

        if (underlying.IsEnum)
            return true;

        return SimpleTypeFullNames.Contains(underlying.FullName ?? string.Empty);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is <c>Nullable&lt;T&gt;</c>.
    /// For reference types, inspects NullableAttribute/NullableContextAttribute to determine
    /// NRT nullability via MetadataLoadContext-safe CustomAttributeData inspection.
    /// </summary>
    private static bool IsNullableType(Type type)
    {
        // Nullable<T> value types
        if (type.IsGenericType
            && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
        {
            return true;
        }

        // Non-value types: not nullable by Nullable<T> — need NRT check below
        return false;
    }

    /// <summary>
    /// Checks whether a parameter is a nullable reference type using NullableAttribute.
    /// Returns true if the parameter is annotated as nullable (byte value 2).
    /// Falls back to NullableContextAttribute on declaring type/assembly.
    /// Returns true (conservative = nullable) if NRT info is unavailable.
    /// </summary>
    private static bool IsNullableReferenceParameter(System.Reflection.ParameterInfo param)
    {
        // Check NullableAttribute on the parameter itself
        var nullableAttr = param.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");

        if (nullableAttr != null && nullableAttr.ConstructorArguments.Count == 1)
        {
            var arg = nullableAttr.ConstructorArguments[0];
            // Single byte: 1 = not-null, 2 = nullable
            if (arg.Value is byte b)
                return b == 2;
            // byte[] for generic types: first element is the outer type
            if (arg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> bytes && bytes.Count > 0)
            {
                var first = bytes.First();
                if (first.Value is byte fb)
                    return fb == 2;
            }
        }

        // Fallback: NullableContextAttribute on declaring method, then type, then assembly
        byte? context = GetNullableContext(param.Member)
                     ?? GetNullableContext(param.Member.DeclaringType)
                     ?? GetAssemblyNullableContext(param.Member.DeclaringType?.Assembly);

        if (context.HasValue)
            return context.Value == 2; // 1 = not-null by default, 2 = nullable by default

        // No NRT info at all — conservatively treat as nullable
        return true;
    }

    private static byte? GetNullableContext(MemberInfo? member)
    {
        if (member == null) return null;
        var attr = member.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
        if (attr != null && attr.ConstructorArguments.Count == 1 && attr.ConstructorArguments[0].Value is byte b)
            return b;
        return null;
    }

    private static byte? GetNullableContext(Type? type)
    {
        if (type == null) return null;
        var attr = type.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
        if (attr != null && attr.ConstructorArguments.Count == 1 && attr.ConstructorArguments[0].Value is byte b)
            return b;
        return null;
    }

    private static byte? GetAssemblyNullableContext(Assembly? assembly)
    {
        if (assembly == null) return null;
        var attr = assembly.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
        if (attr != null && attr.ConstructorArguments.Count == 1 && attr.ConstructorArguments[0].Value is byte b)
            return b;
        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is IFormFile or IFormFileCollection.
    /// </summary>
    private static bool IsFormFileType(Type type)
    {
        var fullName = type.FullName;
        return fullName is "Microsoft.AspNetCore.Http.IFormFile"
                        or "Microsoft.AspNetCore.Http.IFormFileCollection";
    }

    /// <summary>
    /// Unwraps <c>Nullable&lt;T&gt;</c> to <c>T</c>.
    /// Uses FullName comparison instead of <c>Nullable.GetUnderlyingType()</c> to remain
    /// compatible with <c>MetadataLoadContext</c> types.
    /// </summary>
    private static Type UnwrapNullable(Type type)
    {
        if (type.IsGenericType
            && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    /// <summary>
    /// Extracts all route parameter names (without braces) from the supplied route templates
    /// and returns them in a case-insensitive hash set ready for O(1) lookup.
    /// </summary>
    /// <param name="actionTemplate">The action-level route template, or <see langword="null"/>.</param>
    /// <param name="controllerTemplate">The controller-level route template, or <see langword="null"/>.</param>
    private static HashSet<string> CollectRouteParameterNames(
        string? actionTemplate,
        string? controllerTemplate)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ExtractFromTemplate(actionTemplate, names);
        ExtractFromTemplate(controllerTemplate, names);

        return names;
    }

    /// <summary>
    /// Adds every route parameter name found in <paramref name="template"/> to
    /// <paramref name="target"/>.
    /// </summary>
    private static void ExtractFromTemplate(string? template, HashSet<string> target)
    {
        if (string.IsNullOrEmpty(template))
            return;

        foreach (Match match in RouteParamRegex.Matches(template))
        {
            var name = match.Groups[1].Value;
            if (!string.IsNullOrEmpty(name))
                target.Add(name);
        }
    }
}
