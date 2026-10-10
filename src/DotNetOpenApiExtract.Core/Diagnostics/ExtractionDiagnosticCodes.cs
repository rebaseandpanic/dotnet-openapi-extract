namespace DotNetOpenApiExtract.Core.Diagnostics;

/// <summary>Stable values of <see cref="ExtractionDiagnostic.Code"/>.</summary>
public static class ExtractionDiagnosticCodes
{
    /// <summary>
    /// Several source files can be the entry point of the assembly — files with top-level statements, or
    /// <c>Main</c> methods of its entry-point type — and the files compiled into the assembly are not known
    /// (no portable PDB, or its paths do not match the source root). A copy such as <c>Program.Old.cs</c>
    /// or a file excluded with <c>&lt;Compile Remove&gt;</c> is not in the assembly. The file named
    /// <c>Program.cs</c> nearest to the source root is read; when there is none or more than one at that
    /// depth, no entry point is read and Program.cs configuration is missing from the document. Subjects:
    /// the candidate files, relative to the source root, then the file read, if any.
    /// </summary>
    public const string SourceEntryPointAmbiguous = "source.entry-point-ambiguous";

    /// <summary>A security scheme name is registered more than once; the first registration wins. Subjects: the scheme name.</summary>
    public const string SecurityDuplicateScheme = "security.duplicate-scheme";

    /// <summary>
    /// <c>AddSecurityDefinition</c> is called with a name that is neither a literal nor a constant; the call is skipped.
    /// <see cref="ExtractionDiagnostic.SourceLocation"/>: the call.
    /// </summary>
    public const string SecurityDefinitionNonLiteralName = "security.definition-non-literal-name";

    /// <summary>
    /// <c>AddSecurityRequirement</c> names a scheme with a value that is neither a literal nor a constant; the name is skipped.
    /// <see cref="ExtractionDiagnostic.SourceLocation"/>: the value. Subjects: the expression.
    /// </summary>
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

    /// <summary>
    /// A string-enum converter is registered with a naming policy (or Newtonsoft naming strategy) that
    /// cannot be read statically; its enum members are described by their names. Subjects: the
    /// converter type and the expression.
    /// </summary>
    public const string JsonOptionsUnknownConverterNamingPolicy = "json-options.unknown-converter-naming-policy";

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

    /// <summary>
    /// An XML <c>&lt;example&gt;</c> of a property or a type does not parse as a value of its schema
    /// (not a number for a number, not JSON for an object or an array, <c>null</c> for a schema that is
    /// not nullable): no example is written. Location: the property or the type's component.
    /// Subjects: <c>Type.Property</c> or the type, and the example text.
    /// </summary>
    public const string SchemaExampleNotParsable = "schema.example-not-parsable";

    /// <summary>
    /// A property or a type has several XML <c>&lt;example&gt;</c> elements: the first is used.
    /// Location: the property or the type's component. Subjects: <c>Type.Property</c> or the type.
    /// </summary>
    public const string SchemaExampleMultiple = "schema.example-multiple";

    /// <summary>
    /// The XML <c>example</c> of an action parameter (<c>&lt;param name="x" example="…"&gt;</c>) does not
    /// parse as a value of its schema: a path, query or header parameter gets no <c>example</c>, a body
    /// no media type <c>example</c>, a form field is left out of the form's example. Location: the
    /// parameter, or the request body (one per body, naming every form field that failed). Subjects:
    /// the parameter's name in the document (the C# name for a body) and the example text, a pair per
    /// form field.
    /// </summary>
    public const string ParameterExampleNotParsable = "parameter.example-not-parsable";

    /// <summary>
    /// A path parameter is declared <c>[SwaggerParameter(Required = false)]</c>: OpenAPI requires every
    /// path parameter, so it is written <c>required: true</c>. Location: the parameter. Subjects: its name.
    /// </summary>
    public const string ParameterPathRequiredKept = "parameter.path-required-kept";

    /// <summary>
    /// An <c>AddSecurityDefinition</c> declaration needs a value that cannot be resolved statically
    /// (<c>Type</c>, <c>In</c>, <c>Name</c>, <c>Scheme</c>, OAuth2 flows, their URLs or scopes, the OpenID
    /// Connect or OAuth2 metadata URL, <c>Deprecated</c>): the scheme is omitted, with the requirements
    /// that name it. Location: <c>#/components/securitySchemes/{name}</c>.
    /// <see cref="ExtractionDiagnostic.SourceLocation"/>: the declaration. Subjects: the scheme name.
    /// </summary>
    public const string SecuritySchemeNotStatic = "security.scheme-not-static";

    /// <summary>
    /// An <c>AddSecurityDefinition</c> declaration gives a URL as a literal or constant that is not a URI
    /// reference: the scheme is omitted, with the requirements that name it. Location:
    /// <c>#/components/securitySchemes/{name}</c>. Subjects: the scheme name and the text.
    /// </summary>
    public const string SecuritySchemeInvalidUri = "security.scheme-invalid-uri";

    /// <summary>
    /// For a 3.0/3.1 target, the <c>deviceAuthorization</c> flow of an OAuth2 scheme (OpenAPI 3.2) is
    /// written as <c>x-oai-deviceAuthorization</c>; one per scheme, covering the flow's URLs and scopes.
    /// Subjects: the scheme name.
    /// </summary>
    public const string SecurityDeviceAuthorizationMovedToExtension = "security.device-authorization-moved-to-extension";

    /// <summary>
    /// For a 3.0/3.1 target, <c>oauth2MetadataUrl</c> of a security scheme (OpenAPI 3.2) is written as
    /// <c>x-oai-oauth2-metadata-url</c>. Subjects: the scheme name.
    /// </summary>
    public const string SecurityOAuth2MetadataUrlMovedToExtension = "security.oauth2-metadata-url-moved-to-extension";

    /// <summary>
    /// For a 3.0/3.1 target, <c>deprecated</c> of a security scheme (OpenAPI 3.2) is written as
    /// <c>x-oai-deprecated</c>. Subjects: the scheme name.
    /// </summary>
    public const string SecurityDeprecatedMovedToExtension = "security.deprecated-moved-to-extension";

    /// <summary>
    /// The scopes listed for a scheme in <c>AddSecurityRequirement</c> cannot be resolved statically;
    /// they are written as an empty list. <see cref="ExtractionDiagnostic.SourceLocation"/>: the
    /// scopes. Subjects: the scopes expression.
    /// </summary>
    public const string SecurityRequirementNonLiteralScopes = "security.requirement-non-literal-scopes";

    /// <summary>
    /// For a 3.0 target, a <c>mutualTLS</c> security scheme (OpenAPI 3.1) is removed together with its
    /// name in every requirement: requirements naming other schemes too are simplified, requirements
    /// naming only it disappear, so the published auth contract changes. One per scheme. Location:
    /// <c>#/components/securitySchemes/{name}</c>. Subjects: the scheme name, then one entry per
    /// affected requirement (<c>"{where}: {A, B} → {B}"</c> or <c>"{where}: {A} → removed"</c>).
    /// </summary>
    public const string SecurityMutualTlsRemoved = "security.mutual-tls-removed";

    /// <summary>
    /// <c>[Authorize(Roles)]</c> roles that the document cannot carry: for a 3.0 target on the
    /// operation's non-OAuth schemes (3.0 requires empty values), in every version on its OAuth2 /
    /// OpenID Connect schemes (their values are scopes). One per operation and case. Location: the
    /// operation. Subjects: the schemes concerned, then the roles.
    /// </summary>
    public const string SecurityRolesNotWritten = "security.roles-not-written";

    /// <summary>
    /// <c>[Authorize(Roles)]</c> on an operation that has no security requirement at all (no explicit
    /// schemes, none in the document): the roles are not written and no scheme is invented. Location:
    /// the operation. Subjects: the roles.
    /// </summary>
    public const string SecurityRolesWithoutRequirement = "security.roles-without-requirement";

    /// <summary>For a 3.0 target, <c>info.summary</c> (OpenAPI 3.1) is omitted. Location: <c>#/info/summary</c>.</summary>
    public const string DocumentSummaryOmitted = "document.summary-omitted";

    /// <summary>For a 3.0 target, <c>jsonSchemaDialect</c> (OpenAPI 3.1) is omitted. Location: <c>#/jsonSchemaDialect</c>.</summary>
    public const string DocumentJsonSchemaDialectOmitted = "document.json-schema-dialect-omitted";

    /// <summary>
    /// For a 3.0 target, <c>license.identifier</c> (OpenAPI 3.1) is written as <c>x-oai-license-identifier</c>.
    /// Location: <c>#/info/license/x-oai-license-identifier</c>.
    /// </summary>
    public const string DocumentLicenseIdentifierMovedToExtension = "document.license-identifier-moved-to-extension";

    /// <summary>
    /// Before 3.2, <c>servers[].name</c> is written as <c>x-oai-name</c>; one per server. Location: the
    /// server's <c>x-oai-name</c>. Subjects: the name.
    /// </summary>
    public const string DocumentServerNameMovedToExtension = "document.server-name-moved-to-extension";

    /// <summary>Before 3.2, <c>$self</c> is written as <c>x-oai-$self</c>. Location: <c>#/x-oai-$self</c>.</summary>
    public const string DocumentSelfMovedToExtension = "document.self-moved-to-extension";

    /// <summary>
    /// Before 3.2, a tag's <c>summary</c>, <c>parent</c> or <c>kind</c> is written as <c>x-oas-summary</c>,
    /// <c>x-oas-parent</c>, <c>x-oas-kind</c>; one per tag and field (the feature names the field).
    /// Location: the extension in the tag. Subjects: the tag name.
    /// </summary>
    public const string DocumentTagFieldMovedToExtension = "document.tag-field-moved-to-extension";

    /// <summary>
    /// Document metadata in Program.cs is not an object creation the extractor reads (a variable, a
    /// call): the <c>OpenApiInfo</c> of <c>SwaggerDoc</c> / an <c>Info = …</c> assignment, its
    /// <c>Contact</c>, <c>License</c> or <c>ExternalDocs</c>, an <c>AddTag</c> argument or a tag's
    /// <c>ExternalDocs</c>. The fields it holds (title, description, version, summary, terms of service,
    /// contact, license, externalDocs, tag fields) are not taken from it. Also a value of a field that is
    /// read — the info <c>Title</c> / <c>Description</c> / <c>Version</c> / <c>Summary</c> /
    /// <c>TermsOfService</c>, the contact <c>Name</c> / <c>Email</c> / <c>Url</c>, the license <c>Name</c> /
    /// <c>Url</c> / <c>Identifier</c>, the external docs <c>Url</c> / <c>Description</c> of the info or of a
    /// tag — that is not a constant string. <see cref="ExtractionDiagnostic.SourceLocation"/>: the value.
    /// Subjects: the member or call, the expression, then the CLI flags that set the value instead
    /// (<c>--title</c>, <c>--description</c>, <c>--version</c>, <c>--summary</c>, <c>--contact-name</c>,
    /// <c>--contact-email</c>, <c>--contact-url</c>, <c>--license-name</c>, <c>--license-url</c>,
    /// <c>--license-identifier</c>, <c>--terms-of-service</c>), when there are any.
    /// </summary>
    public const string DocumentMetadataNotStatic = "document.metadata-not-static";

    /// <summary>
    /// A descriptive field of an <c>AddSecurityDefinition</c> declaration (<c>Description</c>,
    /// <c>BearerFormat</c>) cannot be resolved statically (a variable, a call, configuration): the
    /// scheme is written without it. Location: the field in <c>#/components/securitySchemes/{name}</c>.
    /// <see cref="ExtractionDiagnostic.SourceLocation"/>: the value in the source. Subjects: the scheme
    /// name, the field and the expression.
    /// </summary>
    public const string SecuritySchemeFieldNotStatic = "security.scheme-field-not-static";

    /// <summary>
    /// An <c>AddSecurityRequirement</c> call whose requirement is not an object creation the extractor
    /// reads (a variable, a call, a lambda returning one): the requirement is not written.
    /// <see cref="ExtractionDiagnostic.SourceLocation"/>: the call. Subjects: the expression.
    /// </summary>
    public const string SecurityRequirementNotStatic = "security.requirement-not-static";

    /// <summary>
    /// Program.cs registers a document or operation filter (<c>DocumentFilter&lt;T&gt;</c>,
    /// <c>OperationFilter&lt;T&gt;</c>, their <c>Add…FilterInstance</c> forms, or a document or operation
    /// transformer of <c>AddOpenApi</c>) and no <c>AddSecurityRequirement</c> is read: the filter runs at
    /// run time and may set the security requirements, which the document then lacks. One per
    /// registration. <see cref="ExtractionDiagnostic.SourceLocation"/>: the registration. Subjects: the
    /// registration method and the filter.
    /// </summary>
    public const string SecurityRequirementsMayComeFromFilter = "security.requirements-may-come-from-filter";

    /// <summary>A <c>[JsonConverter]</c> type is not in the registry of known converters; the schema is left unchanged. Subjects: the converter type.</summary>
    public const string SchemaUnknownJsonConverter = "schema.unknown-json-converter";
}
