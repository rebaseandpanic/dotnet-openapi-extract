using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.has-required-response-codes</c> (off by default, Error severity)
/// Checks that each operation declares specific HTTP status codes as required by user configuration.
/// If <see cref="ValidationContext.RequiredResponseCodes"/> is null or empty, no violations are emitted.
/// <para>
/// Method filters: <c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c>, <c>DELETE</c>,
/// <c>HEAD</c>, <c>OPTIONS</c>, <c>TRACE</c>, <c>QUERY</c>, <c>safe</c> (GET/HEAD/OPTIONS/TRACE/QUERY),
/// <c>mutating</c> (any other method, non-standard ones such as LINK included), <c>*</c> (any method).
/// Operations with any method are checked, in <c>paths</c> and <c>webhooks</c>.
/// </para>
/// </summary>
public sealed class OperationHasRequiredResponseCodesRule : IValidationRule
{
    public string Id => "operation.has-required-response-codes";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    // Safe methods do not change state; every other method, including non-standard ones such
    // as LINK, counts as mutating.
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE", "QUERY" };

    private static readonly HashSet<string> AllKnownMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "QUERY" };

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        // Rule emits nothing when no configuration is provided
        if (context.RequiredResponseCodes == null || context.RequiredResponseCodes.Count == 0)
            yield break;

        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            if (op.Root == OperationRoot.Paths && context.ExcludedPathPrefixes.Any(prefix =>
                    op.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            var path = op.Name;
            var operation = op.Operation;
            var methodStr = op.MethodName;

            // Collect the union of required codes for this method across all matching filters.
            // Using a HashSet ensures overlapping filters (e.g. ["*", 422] and ["mutating", 422])
            // produce at most one violation per missing (method, code) pair.
            var requiredCodes = new HashSet<int>();
            foreach (var (methodFilter, code) in context.RequiredResponseCodes)
            {
                if (MethodMatchesFilter(methodStr, methodFilter))
                    requiredCodes.Add(code);
            }

            foreach (var code in requiredCodes.OrderBy(c => c))
            {
                var codeStr = code.ToString();
                var hasCode = operation.Responses != null &&
                    operation.Responses.ContainsKey(codeStr);

                if (!hasCode)
                {
                    var key = $"{methodStr} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForResponseOf(op.Pointer, codeStr),
                        resolver.ForOperation(key),
                        $"Operation '{methodStr} {path}' is missing required response code {code}.");
                }
            }
        }
    }

    /// <summary>
    /// Returns true if <paramref name="httpMethod"/> (uppercase) matches the given filter.
    /// </summary>
    private static bool MethodMatchesFilter(string httpMethod, string methodFilter)
    {
        return methodFilter.ToUpperInvariant() switch
        {
            "*" => true,
            "MUTATING" => !SafeMethods.Contains(httpMethod),
            "SAFE" => SafeMethods.Contains(httpMethod),
            var m => string.Equals(m, httpMethod, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Returns true if <paramref name="methodFilter"/> is a known filter value.
    /// Used by CLI input validation.
    /// </summary>
    public static bool IsValidMethodFilter(string methodFilter) =>
        methodFilter.Equals("*", StringComparison.OrdinalIgnoreCase) ||
        methodFilter.Equals("mutating", StringComparison.OrdinalIgnoreCase) ||
        methodFilter.Equals("safe", StringComparison.OrdinalIgnoreCase) ||
        AllKnownMethods.Contains(methodFilter);
}
