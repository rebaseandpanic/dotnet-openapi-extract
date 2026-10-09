namespace DotNetOpenApiExtract.Core.Diagnostics;

/// <summary>Stable values of <see cref="ExtractionDiagnostic.Code"/>.</summary>
public static class ExtractionDiagnosticCodes
{
    /// <summary>A security scheme name is registered more than once; the first registration wins. Subjects: the scheme name.</summary>
    public const string SecurityDuplicateScheme = "security.duplicate-scheme";

    /// <summary><c>AddSecurityDefinition</c> is called with a name that is neither a literal nor a constant; the call is skipped.</summary>
    public const string SecurityDefinitionNonLiteralName = "security.definition-non-literal-name";

    /// <summary><c>AddSecurityRequirement</c> names a scheme with a value that is neither a literal nor a constant; the name is skipped.</summary>
    public const string SecurityRequirementNonLiteralScheme = "security.requirement-non-literal-scheme";

    /// <summary>
    /// A security requirement names a scheme missing from <c>components/securitySchemes</c>; the name is omitted.
    /// Subjects: the scheme name. Location: <c>METHOD /path</c>, or <c>#/security</c> for the document-level requirement.
    /// </summary>
    public const string SecurityRequirementUndeclaredScheme = "security.requirement-undeclared-scheme";

    /// <summary>
    /// A contact URL, license URL or terms-of-service value is not an absolute URI and is ignored.
    /// Subjects: the rejected value. Location: the field in <c>info</c>.
    /// </summary>
    public const string InfoInvalidUri = "info.invalid-uri";

    /// <summary>No framework XML documentation was found in the SDK ref packs. Subjects: the paths that were searched.</summary>
    public const string FrameworkXmlDocsMissing = "xml-docs.framework-missing";

    /// <summary>A global content-type filter has a non-literal argument; the entry is skipped. Subjects: the attribute type.</summary>
    public const string MediaTypesNonLiteralContentType = "media-types.non-literal-content-type";

    /// <summary>A JSON options setting is assigned a non-literal value and is ignored. Subjects: the setting name.</summary>
    public const string JsonOptionsNonLiteralSetting = "json-options.non-literal-setting";

    /// <summary><c>JsonOptions.Converters.Add(new())</c>: the converter type cannot be determined statically and is skipped.</summary>
    public const string JsonOptionsUntypedConverter = "json-options.untyped-converter";

    /// <summary>The entry point calls <c>UsePathBase()</c> more than once; the first call is used.</summary>
    public const string PathBaseMultipleCalls = "path-base.multiple-calls";

    /// <summary>The <c>UsePathBase()</c> argument is not a string literal; no path base is emitted.</summary>
    public const string PathBaseNonLiteral = "path-base.non-literal";

    /// <summary>A global response header is set with a non-literal name and is skipped. Subjects: the call form.</summary>
    public const string ResponseHeaderNonLiteralName = "response-headers.non-literal-name";

    /// <summary>
    /// The target is OpenAPI 3.0 and a GET, HEAD or DELETE operation has a request body, which
    /// 3.0 consumers must ignore. The output is unchanged. Location: <c>METHOD /path</c>.
    /// </summary>
    public const string RequestBodyOnGetHeadDelete = "request-body.get-head-delete";

    /// <summary><c>[AcceptVerbs()]</c> lists no method; the action is not emitted. Subjects: the action (<c>Controller.Method</c>).</summary>
    public const string DiscoveryEmptyAcceptVerbs = "discovery.empty-accept-verbs";

    /// <summary>
    /// An attribute derived from <c>HttpMethodAttribute</c> whose methods are not statically visible; the
    /// action is not emitted (the method is never guessed from the attribute's name). Subjects: the attribute type.
    /// </summary>
    public const string DiscoveryUnknownHttpMethodAttribute = "discovery.unknown-http-method-attribute";

    /// <summary>
    /// Several actions declare one path and HTTP method; the document keeps one, chosen by an ordinal key
    /// independent of discovery order. Location: <c>METHOD /path</c>. Subjects: every action involved
    /// (<c>Controller.Method</c>), the kept one first.
    /// </summary>
    public const string OperationPathMethodConflict = "operation.path-method-conflict";

    /// <summary>A <c>[JsonConverter]</c> type is not in the registry of known converters; the schema is left unchanged. Subjects: the converter type.</summary>
    public const string SchemaUnknownJsonConverter = "schema.unknown-json-converter";
}
