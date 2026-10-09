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
    /// One action declares the same HTTP method and route twice (for example <c>[HttpGet]</c> and
    /// <c>[AcceptVerbs]</c>) with two different <c>Name</c> values; the first one is used.
    /// Subjects: the kept name, then the ignored one.
    /// </summary>
    public const string DiscoveryConflictingOperationNames = "discovery.conflicting-operation-names";

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

    /// <summary>
    /// For a 3.0/3.1 target, an operation whose method has no Path Item field there (QUERY, or a
    /// non-standard method) is written whole into <c>x-oai-additionalOperations</c>, invisible to
    /// tools of that version. One per operation. Location: the JSON pointer of the moved operation.
    /// </summary>
    public const string OperationMovedToAdditionalOperations = "operation.moved-to-additional-operations";

    /// <summary>
    /// A polymorphic base has derived types declared without a discriminator value: its union is
    /// <c>anyOf</c> without a discriminator object, and those alternatives are distinguishable only by
    /// structure. Location: the union component. Subjects: the derived types without a value.
    /// </summary>
    public const string PolymorphismAnyOfWithoutDiscriminator = "polymorphism.anyof-without-discriminator";

    /// <summary>
    /// The Swashbuckle polymorphism attributes of a base disagree with its System.Text.Json ones; the STJ
    /// attributes are used. Location: the union component. Subjects: the base type.
    /// </summary>
    public const string PolymorphismSourceDisagreement = "polymorphism.source-disagreement";

    /// <summary>
    /// For a 3.0/3.1 target, the union of a concrete polymorphic base has no <c>discriminator</c> object:
    /// its discriminator property is optional on the wire, which needs <c>defaultMapping</c> (3.2).
    /// One per union. Location: the union component. Subjects: the base type.
    /// </summary>
    public const string PolymorphismDiscriminatorNotExpressible = "polymorphism.discriminator-not-expressible";

    /// <summary>
    /// For a 3.0/3.1 target, the item schema of a sequential media type (<c>application/jsonl</c>,
    /// <c>application/x-ndjson</c>, <c>application/json-seq</c>, <c>text/event-stream</c>) is written as
    /// <c>x-oai-itemSchema</c>: <c>itemSchema</c> exists only since 3.2. One per media type, covering the
    /// keywords inside. Location: the JSON pointer of the media type.
    /// </summary>
    public const string MediaTypeItemSchemaMovedToExtension = "media-type.item-schema-moved-to-extension";

    /// <summary>
    /// An action returns <c>IAsyncEnumerable&lt;T&gt;</c> and declares <c>text/event-stream</c>: standard
    /// MVC has no server-sent events output formatter, so that media type is written without a schema
    /// (a custom formatter or <c>ServerSentEventsResult&lt;T&gt;</c> is needed). In every version.
    /// Location: the operation. Subjects: the element type.
    /// </summary>
    public const string ResponseEventStreamWithoutFormatter = "response.event-stream-without-formatter";

    /// <summary>
    /// The MVC and HTTP JSON options differ in what shapes a schema (naming policy, ignore condition,
    /// number handling, converters) and some CLR types are used in both contexts: each context gets
    /// its own schema of such a type. One per document. Subjects: the full names of those types.
    /// </summary>
    public const string SerializationContextsSharedTypes = "serialization-contexts.shared-types";

    /// <summary>
    /// An action returns an <c>IResult</c>, or a <c>Results&lt;…&gt;</c> with variants, whose status code is
    /// not statically known (an untyped <c>IResult</c>, a user-defined result, <c>JsonHttpResult&lt;T&gt;</c>,
    /// <c>ProblemHttpResult</c>, …) and declares no response: those responses are not described; the
    /// known variants still are, and only when none is known a 200 response without a schema stands in.
    /// One per operation. Location: the operation. Subjects: the results of unknown status.
    /// </summary>
    public const string ResponseResultStatusUnknown = "response.result-status-unknown";

    /// <summary>
    /// A <c>[Range]</c> bound cannot be written: the operand type is not numeric (for example
    /// <c>DateTime</c>; no bound is written) or a bound is infinite or NaN (that bound is not written).
    /// Location: the property. Subjects: <c>Type.Property</c>.
    /// </summary>
    public const string SchemaRangeNotExpressible = "schema.range-not-expressible";

    /// <summary>
    /// For a 3.1/3.2 target, dictionary keys may be written by a converter not in the registry: one on
    /// the key type, or a global one for a key type that would get a <c>propertyNames</c> constraint
    /// (<c>Guid</c>, integers). No constraint is written. Subjects: the key type and the converter.
    /// </summary>
    public const string SchemaUnknownKeyConverter = "schema.unknown-key-converter";

    /// <summary>
    /// A value of <c>[AllowedValues]</c> / <c>[DeniedValues]</c> is not a value of the property's JSON
    /// type (a string on an integer, …): that attribute's constraint is not written. Location: the
    /// property. Subjects: <c>Type.Property</c>.
    /// </summary>
    public const string SchemaValueNotConvertible = "schema.value-not-convertible";

    /// <summary>
    /// The string of <c>[DefaultValue(Type, string)]</c> does not convert to the type (invariant culture):
    /// no <c>default</c> is written. Location: the property, or the operation for parameters (one per
    /// operation). Subjects: <c>Type.Property</c>, or the names of the parameters.
    /// </summary>
    public const string SchemaDefaultNotConvertible = "schema.default-not-convertible";

    /// <summary>A <c>[JsonConverter]</c> type is not in the registry of known converters; the schema is left unchanged. Subjects: the converter type.</summary>
    public const string SchemaUnknownJsonConverter = "schema.unknown-json-converter";
}
