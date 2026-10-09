using DotNetOpenApiExtract.Core.Diagnostics;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>
/// Model rules: fields of a later version that are still in the finished model and that the
/// serializer moves or drops for the target version. Each hit is a <see cref="LossClass.Degradation"/>
/// record for <see cref="DownlevelPass"/>, anchored on the topmost lost or moved node.
/// </summary>
internal static class DownlevelRules
{
    public static IEnumerable<PendingLoss> Evaluate(OpenApiDocument document, OpenApiSpecVersion targetVersion)
    {
        foreach (var loss in SecuritySchemeLosses(document, targetVersion))
            yield return loss;

        foreach (var (_, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                continue;

            foreach (var (method, operation) in pathItem.Operations)
            {
                foreach (var loss in ItemSchemaLosses(operation, targetVersion))
                    yield return loss;

                // An operation whose method has no Path Item field before 3.2 is written whole into
                // x-oai-additionalOperations: one record for the operation covers its subtree.
                if (OperationPlacement.SlotOf(method.Method, targetVersion) != OperationSlot.ExtensionAdditionalOperations)
                    continue;

                var isQuery = string.Equals(method.Method, "QUERY", StringComparison.OrdinalIgnoreCase);
                yield return new PendingLoss
                {
                    Class           = LossClass.Degradation,
                    Code            = ExtractionDiagnosticCodes.OperationMovedToAdditionalOperations,
                    Anchor          = new LossAnchor.Operation(operation),
                    Message         = $"{method.Method} operation emitted as {OperationPlacement.ExtensionName}.{method.Method} " +
                                      $"(requires 3.2); OpenAPI {TargetVersion.Describe(targetVersion)} tools do not see it.",
                    Feature         = isQuery ? "pathItem.query" : "pathItem.additionalOperations",
                    Action          = DiagnosticAction.MovedToExtension,
                    ExtensionName   = OperationPlacement.ExtensionName,
                    RequiredVersion = OpenApiSpecVersion.OpenApi3_2,
                };
            }
        }
    }

    /// <summary>
    /// Security scheme fields of OpenAPI 3.2 that the serializer writes as extensions for 3.0/3.1:
    /// the <c>deviceAuthorization</c> flow (one record per scheme, its URLs and scopes inside it),
    /// <c>oauth2MetadataUrl</c> and <c>deprecated</c>. Anchored on the scheme under the document.
    /// </summary>
    private static IEnumerable<PendingLoss> SecuritySchemeLosses(OpenApiDocument document, OpenApiSpecVersion targetVersion)
    {
        if (targetVersion == OpenApiSpecVersion.OpenApi3_2 || document.Components?.SecuritySchemes is not { Count: > 0 } schemes)
            yield break;

        foreach (var (name, schemeInterface) in schemes)
        {
            if (schemeInterface is not OpenApiSecurityScheme scheme)
                continue;

            if (scheme.Flows?.DeviceAuthorization != null)
                yield return SchemeLoss(name, ["flows", DeviceAuthorizationExtensionName], "flows.deviceAuthorization",
                    ExtractionDiagnosticCodes.SecurityDeviceAuthorizationMovedToExtension, DeviceAuthorizationExtensionName,
                    $"deviceAuthorization flow of '{name}' emitted as {DeviceAuthorizationExtensionName} (requires 3.2)", targetVersion);

            if (scheme.OAuth2MetadataUrl != null)
                yield return SchemeLoss(name, [OAuth2MetadataUrlExtensionName], "securityScheme.oauth2MetadataUrl",
                    ExtractionDiagnosticCodes.SecurityOAuth2MetadataUrlMovedToExtension, OAuth2MetadataUrlExtensionName,
                    $"oauth2MetadataUrl of '{name}' emitted as {OAuth2MetadataUrlExtensionName} (requires 3.2)", targetVersion);

            if (scheme.Deprecated)
                yield return SchemeLoss(name, [DeprecatedExtensionName], "securityScheme.deprecated",
                    ExtractionDiagnosticCodes.SecurityDeprecatedMovedToExtension, DeprecatedExtensionName,
                    $"deprecated of '{name}' emitted as {DeprecatedExtensionName} (requires 3.2)", targetVersion);
        }
    }

    private static PendingLoss SchemeLoss(
        string scheme, string[] path, string feature, string code, string extension, string message, OpenApiSpecVersion targetVersion) => new()
    {
        Class           = LossClass.Degradation,
        Code            = code,
        Anchor          = new LossAnchor.Node(LossAnchor.Document.Instance, ["components", "securitySchemes", scheme, .. path]),
        Message         = $"{message}; OpenAPI {TargetVersion.Describe(targetVersion)} tools do not see it.",
        Feature         = feature,
        Action          = DiagnosticAction.MovedToExtension,
        ExtensionName   = extension,
        RequiredVersion = OpenApiSpecVersion.OpenApi3_2,
        Subjects        = [scheme],
    };

    /// <summary>The extensions the serializer writes these 3.2 security fields to before 3.2.</summary>
    public const string DeviceAuthorizationExtensionName = "x-oai-deviceAuthorization";
    public const string OAuth2MetadataUrlExtensionName = "x-oai-oauth2-metadata-url";
    public const string DeprecatedExtensionName = "x-oai-deprecated";

    /// <summary>
    /// <c>itemSchema</c> exists only since 3.2; for 3.0/3.1 the serializer writes it as
    /// <c>x-oai-itemSchema</c>. One record per media type, anchored on the media type: it covers
    /// every keyword inside the item schema.
    /// </summary>
    private static IEnumerable<PendingLoss> ItemSchemaLosses(OpenApiOperation operation, OpenApiSpecVersion targetVersion)
    {
        if (targetVersion == OpenApiSpecVersion.OpenApi3_2)
            yield break;

        var anchor = new LossAnchor.Operation(operation);

        foreach (var (mediaTypeName, mediaType) in operation.RequestBody?.Content ?? new Dictionary<string, IOpenApiMediaType>())
        {
            if (mediaType.ItemSchema != null)
                yield return ItemSchemaLoss(new LossAnchor.Node(anchor, ["requestBody", "content", mediaTypeName]), targetVersion);
        }

        foreach (var (statusKey, response) in operation.Responses ?? new OpenApiResponses())
        {
            foreach (var (mediaTypeName, mediaType) in response.Content ?? new Dictionary<string, IOpenApiMediaType>())
            {
                if (mediaType.ItemSchema != null)
                    yield return ItemSchemaLoss(new LossAnchor.Node(anchor, ["responses", statusKey, "content", mediaTypeName]), targetVersion);
            }
        }
    }

    private static PendingLoss ItemSchemaLoss(LossAnchor.Node anchor, OpenApiSpecVersion targetVersion) => new()
    {
        Class           = LossClass.Degradation,
        Code            = ExtractionDiagnosticCodes.MediaTypeItemSchemaMovedToExtension,
        Anchor          = anchor,
        Message         = $"itemSchema emitted as {ItemSchemaExtensionName} (requires 3.2); OpenAPI " +
                          $"{TargetVersion.Describe(targetVersion)} tools do not see the schema of the sequence items.",
        Feature         = "mediaType.itemSchema",
        Action          = DiagnosticAction.MovedToExtension,
        ExtensionName   = ItemSchemaExtensionName,
        RequiredVersion = OpenApiSpecVersion.OpenApi3_2,
    };

    /// <summary>The extension the serializer writes <c>itemSchema</c> to before 3.2.</summary>
    public const string ItemSchemaExtensionName = "x-oai-itemSchema";
}
