using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.paths-or-webhooks-or-components</c>
/// OpenAPI 3.0 requires <c>paths</c>; OpenAPI 3.1 and 3.2 require at least one of <c>paths</c>,
/// <c>webhooks</c> and <c>components</c>. A missing object and an empty one differ: an empty object
/// is present (<c>paths: {}</c> satisfies 3.0, <c>components: {}</c> satisfies 3.1/3.2). In the model a
/// missing object is <see langword="null"/>. An unknown version is checked as 3.0.
/// </summary>
public sealed class SpecPathsOrWebhooksOrComponentsRule : IValidationRule
{
    public string Id => "spec.paths-or-webhooks-or-components";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths != null)
            yield break;

        if (context.OpenApiSpecVersion is OpenApiSpecVersion.OpenApi3_1 or OpenApiSpecVersion.OpenApi3_2)
        {
            if (document.Webhooks != null || document.Components != null)
                yield break;

            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                "#",
                null,
                "The document has none of paths, webhooks and components; OpenAPI 3.1 and later require at least one of them.");
            yield break;
        }

        yield return new ValidationViolation(
            Id,
            DefaultSeverity,
            "#",
            null,
            "The document has no paths object; OpenAPI 3.0 requires it (an empty paths object is allowed).");
    }
}
