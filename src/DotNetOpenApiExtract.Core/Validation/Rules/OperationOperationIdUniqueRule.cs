using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.operation-id-unique</c>
/// All <c>operationId</c> values across the entire document must be unique.
/// Non-unique operationIds break SDK generators (method-name collisions) and all tools
/// that index operations by their ID.
/// </summary>
public sealed class OperationOperationIdUniqueRule : IValidationRule
{
    public string Id => "operation.operation-id-unique";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {

        // Collect all operationIds with their pointers
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new List<(string operationId, string pointer)>();

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            var opId = operation.OperationId;
            if (string.IsNullOrWhiteSpace(opId)) continue;

            var pointer = op.Pointer;

            if (seen.ContainsKey(opId))
            {
                duplicates.Add((opId, pointer));
            }
            else
            {
                seen[opId] = pointer;
            }
        }

        foreach (var (opId, pointer) in duplicates)
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                pointer,
                null,
                $"operationId '{opId}' is not unique — it is used by more than one operation.");
        }
    }
}
