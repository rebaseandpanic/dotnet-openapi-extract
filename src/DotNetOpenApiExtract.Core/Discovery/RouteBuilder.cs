using System.Text;
using System.Text.RegularExpressions;

namespace DotNetOpenApiExtract.Core.Discovery;

/// <summary>
/// Builds fully-resolved OpenAPI path strings from ASP.NET Core controller and action route templates.
/// </summary>
/// <remarks>
/// Handles the full routing pipeline:
/// <list type="bullet">
///   <item>Combining controller-level and action-level route templates</item>
///   <item>Resolving route tokens: [controller], [action]</item>
///   <item>Stripping route constraints from path parameters (e.g. {id:int} → {id})</item>
///   <item>Normalising catch-all parameters ({*slug}, {**slug} → {slug})</item>
///   <item>Lowercasing static segments while preserving parameter casing</item>
///   <item>Enforcing OpenAPI path formatting rules (leading slash, no trailing slash, no double slashes)</item>
/// </list>
/// </remarks>
public static class RouteBuilder
{
    // Matches every route parameter token anywhere in the full combined template,
    // used for constraint-stripping and catch-all normalisation after the path is assembled.
    // Group 1: optional catch-all prefix
    // Group 2: parameter name
    // Group 3: optional constraint portion
    private static readonly Regex InlineParameterRegex =
        new(@"\{(\*{1,2})?([^:}*]+)(:[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>
    /// Build the full OpenAPI path from controller and action route templates.
    /// </summary>
    /// <param name="controllerRoute">
    /// The template from the controller-level <c>[Route]</c> attribute, e.g. <c>"api/v1/[controller]"</c>.
    /// May be <see langword="null"/> when no controller-level route is present.
    /// </param>
    /// <param name="actionRoute">
    /// The template from the action-level HTTP method attribute (<c>[HttpGet]</c>, etc.) or
    /// a secondary <c>[Route]</c> attribute, e.g. <c>"{id:int}"</c>.
    /// May be <see langword="null"/> when the action carries no explicit template.
    /// </param>
    /// <param name="controllerName">
    /// The controller class name <em>including</em> the "Controller" suffix (e.g. <c>"WeatherForecastController"</c>).
    /// Used to resolve the <c>[controller]</c> token.
    /// </param>
    /// <param name="actionName">
    /// The effective action name — the method name, or the value of <c>[ActionName]</c> if present.
    /// Used to resolve the <c>[action]</c> token.
    /// </param>
    /// <returns>
    /// A normalised OpenAPI path string that starts with <c>/</c>, has no trailing slash, no double slashes,
    /// lowercased static segments, and stripped route constraints.
    /// </returns>
    public static string BuildPath(
        string? controllerRoute,
        string? actionRoute,
        string controllerName,
        string actionName)
    {
        // Normalise empty strings to null so all null-checks below are sufficient.
        controllerRoute = NullIfEmpty(controllerRoute);
        actionRoute = NullIfEmpty(actionRoute);

        // --- Step 1: Determine the raw combined template ---
        string rawTemplate = CombineTemplates(controllerRoute, actionRoute);

        // --- Step 2: Resolve route tokens ([controller], [action]) ---
        string tokenResolved = ResolveTokens(rawTemplate, controllerName, actionName);

        // --- Step 3: Normalise path parameters (strip constraints, catch-all markers) ---
        string paramNormalised = NormaliseParameters(tokenResolved);

        // --- Step 4: Split into segments, lowercase static segments, then reassemble ---
        string formatted = FormatPath(paramNormalised);

        return formatted;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Combine controller and action templates according to ASP.NET Core routing rules.
    /// </summary>
    private static string CombineTemplates(string? controllerRoute, string? actionRoute)
    {
        // Action template starting with '/' is an absolute path — ignore controller entirely.
        if (actionRoute is not null && actionRoute.StartsWith('/'))
            return actionRoute;

        // Action template starting with '~/' is also absolute — strip the tilde and use as-is.
        if (actionRoute is not null && actionRoute.StartsWith("~/", StringComparison.Ordinal))
            return actionRoute[2..]; // strip leading "~/"

        if (controllerRoute is null && actionRoute is null)
            return string.Empty; // will become "/" after formatting

        if (controllerRoute is null)
            return actionRoute!;

        if (actionRoute is null)
            return controllerRoute;

        // Both present — join with a single slash, letting FormatPath clean up doubles.
        return $"{controllerRoute.TrimEnd('/')}/{actionRoute.TrimStart('/')}";
    }

    /// <summary>
    /// Replace ASP.NET Core route tokens with their runtime values.
    /// <list type="bullet">
    ///   <item><c>[controller]</c> → class name without "Controller" suffix (case-preserved)</item>
    ///   <item><c>[action]</c> → effective action name (case-preserved)</item>
    ///   <item><c>[area]</c> → left untouched (resolved by caller when area support is added)</item>
    /// </list>
    /// </summary>
    private static string ResolveTokens(string template, string controllerName, string actionName)
    {
        // Strip the "Controller" suffix from the class name (case-insensitive suffix match).
        string controllerToken = StripControllerSuffix(controllerName);

        // Tokens are case-insensitive in ASP.NET Core routing.
        string result = template
            .Replace("[controller]", controllerToken, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);

        // [area] is intentionally left as-is — area support is deferred.
        return result;
    }

    /// <summary>
    /// Normalise every route parameter token in the template:
    /// <list type="bullet">
    ///   <item>Strip route constraints after the colon: <c>{id:int}</c> → <c>{id}</c></item>
    ///   <item>Strip catch-all markers: <c>{*slug}</c> and <c>{**slug}</c> → <c>{slug}</c></item>
    /// </list>
    /// </summary>
    private static string NormaliseParameters(string template)
    {
        // Replace every parameter token using the inline regex.
        // The replacement discards groups 1 (catch-all) and 3 (constraints), keeping only group 2 (name).
        return InlineParameterRegex.Replace(template, match =>
        {
            // Group 2 is the plain parameter name (no asterisks, no constraints).
            string paramName = match.Groups[2].Value;
            return $"{{{paramName}}}";
        });
    }

    /// <summary>
    /// Split the template into slash-delimited segments, lowercase any static (non-parameter) segments,
    /// then reassemble with exactly one leading slash and no trailing slash.
    /// </summary>
    private static string FormatPath(string template)
    {
        // Trim any leading or trailing slashes before splitting so we don't get empty first/last segments.
        string trimmed = template.Trim('/');

        if (string.IsNullOrEmpty(trimmed))
            return "/";

        // Split on '/' — individual empty segments (from double slashes) are filtered out below.
        string[] segments = trimmed.Split('/');

        var sb = new StringBuilder(template.Length + 1);

        foreach (string segment in segments)
        {
            // Skip empty segments that arise from accidental double slashes.
            if (string.IsNullOrEmpty(segment))
                continue;

            sb.Append('/');

            // A segment is a path parameter if it is surrounded by braces.
            // Parameters keep their original casing; static segments are lowercased.
            if (IsParameterSegment(segment))
                sb.Append(segment);
            else
                sb.Append(segment.ToLowerInvariant());
        }

        // If every segment was empty (degenerate input), return root.
        return sb.Length == 0 ? "/" : sb.ToString();
    }

    /// <summary>
    /// Returns <see langword="true"/> when the segment is a route parameter token,
    /// i.e. it is wrapped in curly braces <c>{...}</c> — after constraint normalisation
    /// the form is always <c>{name}</c>.
    /// </summary>
    private static bool IsParameterSegment(string segment)
    {
        // After NormaliseParameters the only remaining braces form is {name}.
        // Use a simple prefix/suffix check for performance.
        return segment.Length >= 3
            && segment[0] == '{'
            && segment[^1] == '}';
    }

    /// <summary>
    /// Remove the "Controller" suffix from a class name, preserving the original casing of the prefix.
    /// If the class name does not end with "Controller" it is returned unchanged.
    /// </summary>
    private static string StripControllerSuffix(string className)
    {
        const string suffix = "Controller";

        if (className.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && className.Length > suffix.Length)
        {
            return className[..^suffix.Length];
        }

        return className;
    }

    /// <summary>
    /// Trims whitespace and returns <see langword="null"/> when the string is null, empty,
    /// or consists entirely of whitespace.
    /// </summary>
    private static string? NullIfEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
