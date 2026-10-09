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
        foreach (var (_, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                continue;

            foreach (var (method, operation) in pathItem.Operations)
            {
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
}
