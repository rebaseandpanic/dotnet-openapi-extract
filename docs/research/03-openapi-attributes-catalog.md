# Complete Catalog: C# Attributes, Conventions, and Constructs Affecting OpenAPI Spec Generation

> Definitive requirements list for a static analyzer that generates OpenAPI specs from compiled DLLs + XML docs.
> For each item: attribute/construct name, OpenAPI field mapping, static readability, priority.

---

## 1. ASP.NET Core MVC / Web API Attributes

### 1.1 HTTP Method Attributes

All from `Microsoft.AspNetCore.Mvc` namespace. Each accepts an optional route template string.

| Attribute | OpenAPI Field | Static via Reflection | Priority |
|-----------|--------------|----------------------|----------|
| `[HttpGet("path")]` | `paths./path.get` | Yes — `Template` property + HTTP method | Critical |
| `[HttpPost("path")]` | `paths./path.post` | Yes | Critical |
| `[HttpPut("path")]` | `paths./path.put` | Yes | Critical |
| `[HttpDelete("path")]` | `paths./path.delete` | Yes | Critical |
| `[HttpPatch("path")]` | `paths./path.patch` | Yes | Critical |
| `[HttpHead("path")]` | `paths./path.head` | Yes | Nice-to-have |
| `[HttpOptions("path")]` | `paths./path.options` | Yes | Nice-to-have |
| `[AcceptVerbs("GET","POST")]` | Multiple method entries under the same path | Yes — `HttpMethods` property | Nice-to-have |

**Properties on each HTTP attribute:**
- `Template` (string) — route template, combined with controller `[Route]`
- `Name` (string) — used as `operationId` if set (via `IRouteTemplateProvider.Name`)
- `Order` (int) — route order, not in OpenAPI but affects matching

**Route template tokens to resolve:**
- `{paramName}` — path parameter
- `{paramName:constraint}` — path parameter with constraint (strip constraint for OpenAPI)
- `[controller]` — replace with controller class name minus "Controller" suffix
- `[action]` — replace with method name
- `[area]` — replace with `[Area]` attribute value

### 1.2 Routing and Controller Discovery Attributes

| Attribute | OpenAPI Effect | Static via Reflection | Priority |
|-----------|---------------|----------------------|----------|
| `[Route("api/[controller]")]` | Base path prefix for all operations in controller | Yes — `Template` property | Critical |
| `[Area("v1")]` | Contributes to `[area]` token in route | Yes — `RouteValue` property | Important |
| `[ApiController]` | Enables conventions: auto `[FromBody]` for complex types, auto `[FromQuery]` for simple types, auto 400 response, requires attribute routing | Yes — presence is detectable | Critical |
| `[NonAction]` | Excludes method from API (no OpenAPI entry) | Yes | Critical |
| `[NonController]` | Excludes class from API discovery | Yes | Critical |
| `[ApiExplorerSettings(IgnoreApi = true)]` | Excludes action or controller from OpenAPI document | Yes — `IgnoreApi` property | Critical |
| `[ApiExplorerSettings(GroupName = "v1")]` | Groups operations into specific OpenAPI document | Yes — `GroupName` property | Important |
| `[ActionName("CustomName")]` (NEW) | Overrides method name for `[action]` token resolution in route templates | Yes — `Name` property | Important |
| `[ExcludeFromDescription]` (NEW) | Excludes endpoint from OpenAPI document (Minimal API equivalent of `ApiExplorerSettings(IgnoreApi=true)`) | Yes — presence is detectable | Nice-to-have |

**Note on `[ActionName]`:** When a route template uses the `[action]` token (e.g., `[Route("api/[controller]/[action]")]`), the default replacement is the method name. If `[ActionName("CustomName")]` is present, the token resolves to `"CustomName"` instead.

### 1.3 Parameter Binding Attributes

All affect `in` field of OpenAPI parameter objects. For a controller with `[ApiController]`, binding source inference applies if no attribute is explicitly set:
- Simple types (string, int, Guid, etc.) with matching route param → `FromRoute`
- Simple types without route param → `FromQuery`
- Complex types → `FromBody`
- `IFormFile` / `IFormFileCollection` → `FromForm`
- `CancellationToken` → not in OpenAPI (service binding)

| Attribute | OpenAPI `in` / Field | Static via Reflection | Priority |
|-----------|---------------------|----------------------|----------|
| `[FromRoute(Name = "id")]` | `in: path` parameter | Yes — `Name` property | Critical |
| `[FromQuery(Name = "filter")]` | `in: query` parameter | Yes — `Name` property | Critical |
| `[FromBody]` | `requestBody` object | Yes | Critical |
| `[FromForm]` | `requestBody` with `multipart/form-data` or `application/x-www-form-urlencoded` | Yes | Important |
| `[FromHeader(Name = "X-Api-Key")]` | `in: header` parameter | Yes — `Name` property | Important |
| `[FromServices]` | **Not in OpenAPI** — service injection, skip | Yes — presence means exclude | Critical |
| `[AsParameters]` | Decompose type's properties into individual parameters (Minimal API primarily) | Yes | Nice-to-have |
| `[BindNever]` | Exclude from binding / not in OpenAPI | Yes | Important |
| `[ModelBinder]` | Custom binding — cannot fully interpret statically | Partially | Nice-to-have |

### 1.4 Response Metadata Attributes

| Attribute | OpenAPI Field | Static via Reflection | Priority |
|-----------|--------------|----------------------|----------|
| `[ProducesResponseType(typeof(MyDto), 200)]` | `responses.200.content.application/json.schema` | Yes — `Type`, `StatusCode` | Critical |
| `[ProducesResponseType(200)]` (no type) | `responses.200` (no schema) | Yes — `StatusCode` | Critical |
| `[ProducesResponseType<MyDto>(200)]` (.NET 7+) | Same as above, generic form | Yes — generic type arg | Critical |
| `[ProducesResponseType(typeof(MyDto), 200, "application/json")]` | Response with specific content type | Yes — `ContentTypes` | Important |
| `[Produces("application/json")]` | Default response content type for all actions / sets `content` keys | Yes — `ContentTypes` | Important |
| `[Produces(typeof(MyDto))]` | Default response type + content type | Yes — `Type`, `ContentTypes` | Important |
| `[Consumes("application/json")]` | `requestBody.content` keys (accepted content types) | Yes — `ContentTypes` | Important |
| `[ProducesDefaultResponseType]` | `responses.default` | Yes | Important |
| `[ProducesDefaultResponseType(typeof(ProblemDetails))]` | `responses.default.content.schema` | Yes — `Type` | Important |
| `[ProducesErrorResponseType(typeof(ProblemDetails))]` | Sets error response type for auto-generated 4xx/5xx responses | Yes — `Type` | Important |

**`Description` property (.NET 10+) (NEW):** In .NET 10, `ProducesResponseTypeAttribute`, `ProducesAttribute`, and `ProducesDefaultResponseTypeAttribute` all gained an optional `Description` property that maps directly to `responses.{code}.description`. This is the official way to set response descriptions without Swashbuckle `[SwaggerResponse]` or XML `<response>` comments:
```csharp
[ProducesResponseType<MyDto>(200, Description = "The requested resource")]
[Produces("application/json", Description = "Success response")]
[ProducesDefaultResponseType(typeof(ProblemDetails), Description = "Unexpected error")]
```
Source: [ProducesResponseType Description property (ASP.NET Core 10)](https://github.com/dotnet/aspnetcore/issues/55656)

**Note:** `[ApiController]` convention auto-adds `[ProducesResponseType(typeof(ValidationProblemDetails), 400)]` for actions with model-bound parameters. Implement this convention in static analyzer.

### 1.5 API Conventions

| Attribute | OpenAPI Effect | Static via Reflection | Priority |
|-----------|---------------|----------------------|----------|
| `[ApiConventionType(typeof(DefaultApiConventions))]` | Applies matching convention methods to all actions | Yes — `ConventionType` | Nice-to-have |
| `[ApiConventionMethod(typeof(DefaultApiConventions), nameof(DefaultApiConventions.Get))]` | Applies specific convention method to action (adds ProducesResponseType, etc.) | Yes | Nice-to-have |

`DefaultApiConventions` provides implicit response types:
- `Get` → 200, 404
- `Post` → 201, 400
- `Put` → 204, 400, 404
- `Delete` → 200, 400, 404

To support this statically, you need to read the convention type's methods and their `[ProducesResponseType]` attributes, then match by name/parameter patterns using `[ApiConventionNameMatch]` and `[ApiConventionTypeMatch]`.

**Note (from audit):** Keep `[ApiConventionType]` as **Phase 3**. The attribute reading infrastructure should be ready, but convention matching logic is complex and lower priority than direct attribute detection.

### 1.6 .NET 9+ Controller-Compatible Attributes (NEW)

These attributes were originally Minimal API only but **work on controllers in .NET 9+**. Priority has been elevated from Nice-to-have to Important/Critical based on audit findings.

| Attribute | OpenAPI Field | Static via Reflection | Priority |
|-----------|--------------|----------------------|----------|
| `[EndpointSummary("text")]` | `operation.summary` | Yes — `Summary` property | **Important** (upgraded from Nice-to-have; works on controllers in .NET 9+) |
| `[EndpointDescription("text")]` | `operation.description` | Yes — `Description` property | **Important** (upgraded from Nice-to-have; works on controllers in .NET 9+) |
| `[Tags("tag1","tag2")]` | `operation.tags` | Yes — `Tags` property | **Important** (upgraded from Nice-to-have; works on controllers in .NET 9+) |

**Note:** In .NET 9+, the built-in OpenAPI pipeline uses these attributes as primary metadata sources for both Minimal API and controller-based APIs. `[EndpointSummary]` and `[EndpointDescription]` are the official .NET-native alternatives to `[SwaggerOperation(Summary/Description)]`.

Source: [OpenAPI document generation in .NET 9](https://devblogs.microsoft.com/dotnet/dotnet9-openapi/)

### 1.7 Security Attributes (NEW)

Security schemes are defined at the document level via runtime configuration (see Section 7.6). However, the following attributes can be statically detected to annotate individual operations with security requirements.

| Attribute | OpenAPI Mapping | Static via Reflection | Priority |
|-----------|----------------|----------------------|----------|
| `[Authorize]` | `operation.security: [{ schemeName: [] }]` — apply security requirement from config-defined scheme | Yes — presence is detectable | **Important** |
| `[Authorize(Roles = "Admin")]` | `operation.security: [{ schemeName: ["Admin"] }]` — roles map to OAuth2 scopes | Yes — `Roles` property (comma-separated string) | **Important** |
| `[Authorize(AuthenticationSchemes = "Bearer")]` | Maps to specific security scheme name in `operation.security` | Yes — `AuthenticationSchemes` property | **Important** |
| `[AllowAnonymous]` | Remove `security` from this operation (overrides controller-level `[Authorize]`) | Yes — presence is detectable | **Important** |
| `[Authorize(Policy = "RequireAdmin")]` | Policy name only — actual scheme is runtime-configured | Yes — `Policy` property (informational) | Nice-to-have |

**Detection logic:**
1. If controller has `[Authorize]`, all actions inherit security requirement
2. If action has `[AllowAnonymous]`, security is explicitly removed for that operation
3. `AuthenticationSchemes` directly names the security scheme (e.g., `"Bearer"`)
4. `Roles` maps to scopes within the security requirement
5. The security scheme definition itself must come from config (CLI arg or config file)

Source: [Include OpenAPI metadata in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)

---

## 2. Swashbuckle.AspNetCore.Annotations Attributes

All require `options.EnableAnnotations()` in SwaggerGen setup. All are statically readable via reflection.

### 2.1 SwaggerOperationAttribute

**Applies to:** Action methods
**Namespace:** `Swashbuckle.AspNetCore.Annotations`

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `Summary` | `string` | `operation.summary` | Critical |
| `Description` | `string` | `operation.description` | Critical |
| `OperationId` | `string` | `operation.operationId` | Critical |
| `Tags` | `string[]` | `operation.tags` | Important |

### 2.2 SwaggerResponseAttribute

**Applies to:** Action methods, controller classes (inherited by all actions)
**Inherits from:** `ProducesResponseTypeAttribute`
**AllowMultiple:** Yes

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `StatusCode` | `int` (ctor) | `responses.{code}` key | Critical |
| `Description` | `string` (ctor) | `responses.{code}.description` | Critical |
| `Type` | `Type` (ctor) | `responses.{code}.content.schema` | Critical |
| `ContentTypes` | `string[]` (ctor/prop) | Content type keys under response | Important |

### 2.3 SwaggerParameterAttribute

**Applies to:** Method parameters, properties (for decomposed parameter types)

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `Description` | `string` | `parameter.description` | Critical |
| `Required` / `RequiredFlag` | `bool?` | `parameter.required` | Important |

### 2.4 SwaggerRequestBodyAttribute

**Applies to:** Parameters decorated with `[FromBody]`

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `Description` | `string` | `requestBody.description` | Critical |
| `Required` / `RequiredFlag` | `bool?` | `requestBody.required` | Important |

### 2.5 SwaggerTagAttribute

**Applies to:** Controller classes

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `Description` | `string` | `tags[].description` (document-level tag) | Important |
| `ExternalDocsUrl` | `string` | `tags[].externalDocs.url` | Nice-to-have |

### 2.6 SwaggerSchemaAttribute

**Applies to:** Classes, structs, properties, parameters, enums

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `Description` | `string` | `schema.description` | Critical |
| `Title` | `string` | `schema.title` | Nice-to-have |
| `Format` | `string` | `schema.format` | Important |
| `ReadOnly` / `ReadOnlyFlag` | `bool?` | `schema.readOnly` | Important |
| `WriteOnly` / `WriteOnlyFlag` | `bool?` | `schema.writeOnly` | Important |
| `Nullable` / `NullableFlag` | `bool?` | `schema.nullable` (OAS 3.0) / `type: [T, "null"]` (OAS 3.1) | Important |
| `Required` | `string[]` | `schema.required` array | Important |

**Note:** The `ReadOnly`, `WriteOnly`, `Nullable` property getters throw `InvalidOperationException` by design; the internal `*Flag` versions (nullable bool) are what Swashbuckle's filters actually read.

### 2.7 SwaggerDiscriminatorAttribute

**Applies to:** Base classes / interfaces for polymorphism

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `PropertyName` | `string` | `discriminator.propertyName` | Important |

### 2.8 SwaggerSubTypeAttribute

**Applies to:** Base classes / interfaces
**AllowMultiple:** Yes — one per derived type

| Property | Type | OpenAPI Field | Priority |
|----------|------|--------------|----------|
| `SubType` | `Type` | `discriminator.mapping` value → `$ref` to subtype schema | Important |
| `DiscriminatorValue` | `string` | `discriminator.mapping` key | Important |

**Together they generate:**
```json
{
  "discriminator": {
    "propertyName": "type",
    "mapping": {
      "circle": "#/components/schemas/Circle",
      "square": "#/components/schemas/Square"
    }
  },
  "oneOf": [
    { "$ref": "#/components/schemas/Circle" },
    { "$ref": "#/components/schemas/Square" }
  ]
}
```

### 2.9 SwaggerOperationFilterAttribute

**Applies to:** Action methods, controller classes
**OpenAPI effect:** Applies a custom `IOperationFilter` to specific operations

| Property | Type | Note |
|----------|------|------|
| `FilterType` | `Type` | **Cannot be executed statically** — arbitrary C# code |

**Static readability:** Detectable but **not executable**. Log a warning that this operation has a custom filter.

### 2.10 SwaggerSchemaFilterAttribute

**Applies to:** Classes, structs, enums
**Same limitation as above** — custom filter, not statically executable.

---

## 3. System.ComponentModel.DataAnnotations (Validation Attributes)

These are processed by Swashbuckle's `ApplyValidationAttributes` extension and by `SchemaGenerator` itself.

| Attribute | OpenAPI Schema Field | Static via Reflection | Priority |
|-----------|---------------------|----------------------|----------|
| `[Required]` | Adds property name to parent's `required` array; if `AllowEmptyStrings = false` on string → `minLength: 1` | Yes | Critical |
| `[StringLength(100, MinimumLength = 5)]` | `maxLength: 100`, `minLength: 5` | Yes | Critical |
| `[MaxLength(100)]` | `maxLength: 100` (strings) / `maxItems: 100` (arrays) | Yes | Critical |
| `[MinLength(5)]` | `minLength: 5` (strings) / `minItems: 5` (arrays) | Yes | Critical |
| `[Length(5, 100)]` (.NET 8+) | `minLength/minItems` + `maxLength/maxItems` | Yes | Important |
| `[Range(1, 100)]` | `minimum: 1`, `maximum: 100` | Yes — `Minimum`, `Maximum` | Critical |
| `[Range(typeof(double), "0.01", "999.99")]` | `minimum`, `maximum` (string-typed ranges) | Yes | Important |
| `[RegularExpression(@"^\d{4}$")]` | `pattern: "^\\d{4}$"` | Yes — `Pattern` | Important |
| `[EmailAddress]` | `format: "email"` (by convention, not natively) | Yes | Nice-to-have |
| `[Phone]` | `format: "phone"` (by convention) | Yes | Nice-to-have |
| `[Url]` | `format: "uri"` (by convention) | Yes | Nice-to-have |
| `[CreditCard]` | No standard OpenAPI mapping | Yes | Nice-to-have |
| `[Compare]` | No OpenAPI mapping (server-side only) | N/A | Skip |
| `[DataType(DataType.Date)]` | Can override `format` (e.g., `date`, `time`, `password`) | Yes — `DataType` enum | Nice-to-have |
| `[EnumDataType(typeof(MyEnum))]` | `enum: [...]` from enum type | Yes | Nice-to-have |
| `[AllowedValues("A","B","C")]` (.NET 8+) | `enum: ["A","B","C"]` | Yes | Important |
| `[DeniedValues("X")]` (.NET 8+) | No direct OpenAPI mapping | Yes | Skip |
| `[Base64String]` (.NET 8+) | `format: "byte"` (by convention) | Yes | Nice-to-have |

### 3.1 System.ComponentModel (Non-Validation) Attributes

| Attribute | OpenAPI Schema Field | Static via Reflection | Priority |
|-----------|---------------------|----------------------|----------|
| `[Description("text")]` | `schema.description` AND `parameter.description` — primary metadata source in .NET 9+ | Yes | **Critical** (elevated from Important; see note below) |
| `[DefaultValue(42)]` | `schema.default` | Yes — `Value` property | Important |
| `[DisplayName("Name")]` | No direct mapping; some generators use for `title` | Yes | Nice-to-have |
| `[Browsable(false)]` | No standard mapping; NSwag uses to hide properties | Yes | Skip |
| `[ReadOnly(true)]` | `schema.readOnly` (picked up by some generators) | Yes | Nice-to-have |
| `[Obsolete("reason")]` | `schema.deprecated: true` / `operation.deprecated: true` | Yes — `Message`, `IsError` | Important |

**Note on `[Description]` (.NET 9+) (NEW):** In .NET 9+ built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`), `[Description]` from `System.ComponentModel` is the **primary attribute** for setting descriptions on both parameters AND properties/types. It is no longer just a "fallback" — it is the official .NET-native replacement for `[SwaggerParameter(Description)]` and `[SwaggerSchema(Description)]`. The static analyzer must check `[Description]` on:
- **Method parameters** → `parameter.description`
- **Properties** → `schema.properties.{name}.description`
- **Types (classes, records, structs)** → `schema.description`

Source: [Include OpenAPI metadata in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)

---

## 4. System.Text.Json Serialization Attributes

These are processed by Swashbuckle's `JsonSerializerDataContractResolver`.

| Attribute | OpenAPI Effect | Static via Reflection | Priority |
|-----------|---------------|----------------------|----------|
| `[JsonPropertyName("user_name")]` | Schema property key = `"user_name"` instead of C# property name | Yes — `Name` property | Critical |
| `[JsonIgnore]` | Property excluded from schema entirely | Yes — `Condition` property (Always/Never/WhenWritingNull/WhenWritingDefault) | Critical |
| `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` | Still included in schema (only `Always` excludes) | Yes | Critical |
| `[JsonRequired]` | Property added to `required` array | Yes | Critical |
| `[JsonConstructor]` | Affects `readOnly` determination — properties deserialized via ctor params are NOT read-only | Yes | Important |
| `[JsonExtensionData]` | Property becomes `additionalProperties` in schema | Yes | Important |
| `[JsonConverter(typeof(...))]` | **Cannot determine schema statically** — arbitrary conversion logic | Detectable, not interpretable | Nice-to-have |
| `[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]` | No direct OpenAPI mapping | Yes | Skip |
| `[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]` | `discriminator.propertyName` | Yes — `TypeDiscriminatorPropertyName` | Important |
| `[JsonDerivedType(typeof(Circle), "circle")]` | `discriminator.mapping`, `oneOf` entries | Yes — `DerivedType`, `TypeDiscriminator` | Important |
| `[JsonStringEnumMemberName("custom_name")]` (.NET 9+) (NEW) | Overrides enum member string value in `enum: [...]` array | Yes — `Name` property on enum field | **Important** |
| `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` (.NET 8+) (NEW) | `additionalProperties: false` on schema | Yes — `Handling` property on type | **Important** |

**Note on `[JsonStringEnumMemberName]` (.NET 9+) (NEW):** When an enum is serialized as a string (via `JsonStringEnumConverter`), this attribute overrides the serialized name for individual enum members:
```csharp
public enum TaskStatus
{
    [JsonStringEnumMemberName("in_progress")] InProgress,
    [JsonStringEnumMemberName("not_started")] NotStarted
}
// → enum: ["in_progress", "not_started"]
```
Source: [What's new in System.Text.Json in .NET 9](https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-9/)

**Note on `[JsonUnmappedMemberHandling]` (.NET 8+) (NEW):** When `JsonUnmappedMemberHandling.Disallow` is set on a type, the JSON Schema should include `additionalProperties: false`. The built-in .NET OpenAPI generator does this.
Source: [JsonUnmappedMemberHandling and OpenAPI](https://github.com/dotnet/aspnetcore/issues/57981)

**Note on `JsonStringEnumConverter<TEnum>` generic form (NEW):** In addition to `[JsonConverter(typeof(JsonStringEnumConverter))]`, the generic form `[JsonConverter(typeof(JsonStringEnumConverter<MyEnum>))]` must also be detected. When applied to a property (rather than the enum type), it affects only that property's schema.

### 4.1 Newtonsoft.Json Attributes (if using `AddSwaggerGenNewtonsoftSupport()`)

| Attribute | OpenAPI Effect | Static via Reflection | Priority |
|-----------|---------------|----------------------|----------|
| `[JsonProperty("name", Required = Required.Always)]` | Property key rename + `required` | Yes | Important (if used) |
| `[JsonIgnore]` (Newtonsoft) | Property excluded from schema | Yes | Important (if used) |
| `[JsonConverter]` (Newtonsoft) | Not statically interpretable | Detectable only | Nice-to-have |

### 4.2 Global Serializer Settings (affect schema generation)

These are NOT attribute-based but affect how property names appear in schema:

| Setting | OpenAPI Effect | Statically Available? |
|---------|---------------|----------------------|
| `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase` | All property names → camelCase in schema | **No** — runtime config. Analyzer should default to camelCase (ASP.NET Core default) |
| `JsonSerializerOptions.DefaultIgnoreCondition` | Controls which properties are included | **No** — default to `Never` |
| `JsonSerializerOptions.Converters` | Custom converters affect schema | **No** — not detectable |

---

## 5. XML Documentation Comments

Swashbuckle processes XML docs via 5 filters: `XmlCommentsOperationFilter`, `XmlCommentsParameterFilter`, `XmlCommentsRequestBodyFilter`, `XmlCommentsSchemaFilter`, `XmlCommentsDocumentFilter`.

### 5.1 XML Tags Processed

| XML Tag | Context | OpenAPI Field | Priority |
|---------|---------|--------------|----------|
| `<summary>` | On action method | `operation.summary` | Critical |
| `<summary>` | On controller class | `tags[].description` (via `XmlCommentsDocumentFilter`) | Important |
| `<summary>` | On DTO class/struct | `schema.description` | Critical |
| `<summary>` | On DTO property | `schema.properties.{name}.description` | Critical |
| `<summary>` | On enum type | `schema.description` | Important |
| `<remarks>` | On action method | `operation.description` | Important |
| `<param name="x">desc</param>` | On action method | `parameters[name=x].description` or `requestBody.description` | Critical |
| `<param name="x" example="42">` | On action method | `parameters[name=x].example` / `requestBody.content.*.example` | Important |
| `<response code="200">desc</response>` | On action method | `responses.200.description` | Critical |
| `<response code="200">desc</response>` | On controller class | Inherited by all actions (merged) | Important |
| `<example>42</example>` | On DTO property | `schema.properties.{name}.example` | Important |
| `<example>42</example>` | On record constructor param | `schema.properties.{name}.example` | Important |
| `<inheritdoc />` | On method/class/property | Resolved by C# compiler into XML — if present in XML file, follow `cref` | Nice-to-have |

**Tags NOT processed by Swashbuckle** (included in XML but ignored):
- `<see cref="T:MyType"/>` — stripped to inner text during humanization
- `<seealso/>`
- `<code>` — Swashbuckle's `Humanize()` strips XML tags
- `<para>` — stripped to text
- `<c>` — stripped to text
- `<list>` — stripped
- `<typeparam>` — not mapped
- `<returns>` — **not mapped** (use `<response>` instead)
- `<value>` — not mapped
- `<exception>` — not mapped

### 5.2 XML Member Name Format (for lookup)

| Member Type | Format | Example |
|-------------|--------|---------|
| Type | `T:Namespace.ClassName` | `T:MyApp.Models.UserDto` |
| Method | `M:Namespace.Class.Method(ParamType1,ParamType2)` | `M:MyApp.Controllers.UsersController.GetById(System.Int32)` |
| Property | `P:Namespace.Class.PropertyName` | `P:MyApp.Models.UserDto.Email` |
| Field | `F:Namespace.Class.FieldName` | `F:MyApp.Models.Status.Active` |
| Enum member | `F:Namespace.EnumType.MemberName` | `F:MyApp.Models.Status.Active` |

**Generic methods:** `M:Namespace.Class.Get``1(``0)` — backtick notation for type parameters.

**Static readability:** The XML file is produced by the compiler alongside the DLL. Parse it as an `XDocument` and look up members by constructing the `name` attribute from reflection metadata.

---

## 6. C# Type System Features Affecting Schema Generation

### 6.1 Primitive Type Mapping

| C# Type | OpenAPI `type` | OpenAPI `format` | Priority |
|---------|---------------|-----------------|----------|
| `string` | `string` | — | Critical |
| `bool` | `boolean` | — | Critical |
| `byte` | `integer` | `int32` | Critical |
| `sbyte` | `integer` | `int32` | Critical |
| `short` / `Int16` | `integer` | `int32` | Critical |
| `ushort` / `UInt16` | `integer` | `int32` | Critical |
| `int` / `Int32` | `integer` | `int32` | Critical |
| `uint` / `UInt32` | `integer` | `int32` | Critical |
| `long` / `Int64` | `integer` | `int64` | Critical |
| `ulong` / `UInt64` | `integer` | `int64` | Critical |
| `float` / `Single` | `number` | `float` | Critical |
| `double` / `Double` | `number` | `double` | Critical |
| `decimal` / `Decimal` | `number` | `double` | Critical |
| `DateTime` | `string` | `date-time` | Critical |
| `DateTimeOffset` | `string` | `date-time` | Critical |
| `DateOnly` (.NET 6+) | `string` | `date` | Important |
| `TimeOnly` (.NET 6+) | `string` | `time` | Important |
| `TimeSpan` | `string` | `duration` | Important |
| `Guid` | `string` | `uuid` | Critical |
| `Uri` | `string` | `uri` | Important |
| `byte[]` | `string` | `byte` (base64) | Important |
| `char` | `string` | — | Nice-to-have |
| `object` | `{}` (any type) | — | Important |
| `dynamic` | `{}` (any type) | — | Nice-to-have |
| `Stream` / `IFormFile` | `string` | `binary` | Important |

### 6.2 Nullable Types

| Construct | OpenAPI Effect | Static Detection | Priority |
|-----------|---------------|-----------------|----------|
| `int?` / `Nullable<int>` | `nullable: true` (OAS 3.0) or `type: ["integer", "null"]` (OAS 3.1) | Yes — `Nullable.GetUnderlyingType()` | Critical |
| `string?` (NRT enabled) | `nullable: true` | Yes — `NullabilityInfoContext` (.NET 6+) or `NullableAttribute`/`NullableContextAttribute` on assembly | Critical |
| `MyDto?` (NRT enabled) | `nullable: true` on `$ref` (via `allOf` wrapper or `nullable` flag) | Yes — same NRT detection | Critical |
| Non-nullable ref type (NRT) | Added to `required` array (when `SupportNonNullableReferenceTypes` enabled) | Yes | Critical |

**NRT detection approach for static analyzer:**
1. Check assembly for `[NullableContext(byte)]` attribute — indicates NRT is enabled project-wide
2. Check each property for `[Nullable(byte)]` attribute — 0=oblivious, 1=not-null, 2=nullable
3. For generic types, `NullableAttribute` contains a `byte[]` — first element for the type itself, subsequent for generic args

**Byte-array tree-traversal algorithm (NEW from audit):**

The `byte[]` in `NullableAttribute` follows a **pre-order tree traversal** of the type's generic structure:
- `Dictionary<string, List<string?>>` → bytes: `[1, 1, 1, 2]` — root Dict(1), key:string(1), List(1), element:string?(2)
- `Task<ActionResult<MyDto?>>` → must unwrap both `Task<>` and `ActionResult<>` wrappers, then check NRT on the inner type
- Containing types (nested classes) also contribute bytes to the array

**MetadataLoadContext fallback rules:**
1. If property has no `NullableAttribute`, fall back to `NullableContextAttribute` on declaring type
2. If type has no `NullableContextAttribute`, fall back to assembly-level `NullableContextAttribute`
3. If nothing found, treat as oblivious (0) — assume nullable for reference types (conservative approach)
4. **Single-byte optimization:** When all bytes would be the same value, the compiler emits a single byte instead of an array. Implementation must handle both `byte` and `byte[]` forms

Source: [Roslyn Nullable Metadata spec](https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md)

### 6.3 Collections and Arrays

| C# Type | OpenAPI Schema | Priority |
|---------|---------------|----------|
| `T[]` | `{ type: "array", items: { <schema of T> } }` | Critical |
| `List<T>` | `{ type: "array", items: { <schema of T> } }` | Critical |
| `IEnumerable<T>` | `{ type: "array", items: { <schema of T> } }` | Critical |
| `ICollection<T>` | Same as above | Critical |
| `IList<T>` | Same | Critical |
| `IReadOnlyList<T>` | Same | Critical |
| `IReadOnlyCollection<T>` | Same | Critical |
| `ISet<T>` / `HashSet<T>` | `{ type: "array", items: { ... }, uniqueItems: true }` | Important |
| `IAsyncEnumerable<T>` | `{ type: "array", items: { ... } }` | Nice-to-have |

### 6.4 Dictionary Types

| C# Type | OpenAPI Schema | Priority |
|---------|---------------|----------|
| `Dictionary<string, T>` | `{ type: "object", additionalProperties: { <schema of T> } }` | Critical |
| `IDictionary<string, T>` | Same | Critical |
| `IReadOnlyDictionary<string, T>` | Same | Critical |
| `Dictionary<TKey, TValue>` (non-string key) | `{ type: "object", additionalProperties: { <schema of TValue> } }` — key type lost | Important |

### 6.5 Enum Types

| Scenario | OpenAPI Schema | Priority |
|----------|---------------|----------|
| `enum Status { Active, Inactive }` (default) | `{ type: "integer", format: "int32", enum: [0, 1] }` | Critical |
| Enum with `[JsonConverter(typeof(JsonStringEnumConverter))]` | `{ type: "string", enum: ["Active", "Inactive"] }` | Critical |
| Global `JsonStringEnumConverter` in options | All enums as strings | Critical |
| `[Flags]` enum | Same schema — `[Flags]` has no special OpenAPI representation | Important |
| Enum with `[EnumMember(Value = "active")]` | String value is `"active"` instead of `"Active"` | Important |
| Nullable enum `Status?` | Schema + `nullable: true` | Critical |

**For static analyzer — all 6 enum string detection methods (NEW, expanded from audit):**
1. `[JsonConverter(typeof(JsonStringEnumConverter))]` on **enum type** — all members serialized as strings
2. `[JsonConverter(typeof(JsonStringEnumConverter<MyEnum>))]` on **enum type** (NEW) — generic form, same effect
3. `[JsonConverter(typeof(JsonStringEnumConverter))]` on a **property** — affects only that property's enum schema
4. `[EnumMember(Value = "custom")]` on **enum field** — overrides individual member's string value (from `System.Runtime.Serialization`)
5. `[JsonStringEnumMemberName("custom")]` on **enum field** (.NET 9+) (NEW) — overrides individual member's string value (from `System.Text.Json`)
6. CLI flag `--enum-as-string` — global override, all enums treated as strings

**Detection priority:** Check method 6 first (global override), then method 1/2 on enum type, then method 3 on property, then methods 4/5 for individual member name overrides. If not found, default to integer representation.

Source: [What's new in System.Text.Json in .NET 9](https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-9/)

### 6.6 Records

| Feature | OpenAPI Effect | Static Detection | Priority |
|---------|---------------|-----------------|----------|
| `record UserDto(string Name, int Age)` | Same as class with properties — generates schema with `Name`, `Age` | Yes — `IsValueType` + constructor params | Critical |
| Record `init` properties | Same as regular properties in schema | Yes | Critical |
| `<param>` XML on record ctor | Maps to `schema.properties.{name}.description` | Yes — XML lookup by ctor member name | Important |
| `<example>` on record `<param>` | `schema.properties.{name}.example` | Yes | Important |

### 6.7 Required and Init-Only Properties (C# 11+)

| Feature | OpenAPI Effect | Static Detection | Priority |
|---------|---------------|-----------------|----------|
| `required string Name { get; set; }` | Added to `required` array | Yes — see detection strategy below (UPDATED) | Critical |
| `string Name { get; init; }` | **Not automatically read-only** in Swashbuckle — treated as read-write. `JsonConstructor` may affect this. | Yes — check for `IsExternalInit` modreq on setter (see below) | Important |

**`required` keyword detection — corrected from audit (UPDATED):**

The C# compiler does **NOT** emit a per-property attribute for the `required` keyword. The detection strategy is:

```
IF type has System.Runtime.CompilerServices.RequiredMemberAttribute
AND constructor does NOT have System.Runtime.CompilerServices.SetsRequiredMembersAttribute
THEN all properties matching C# `required` keyword → add to schema.required
```

**Challenge:** MetadataLoadContext cannot directly tell which specific properties have the `required` modifier. `System.Text.Json` infers this at runtime. For static analysis, use the following approach:
1. If property has `[JsonRequired]` → required (works regardless of `required` keyword)
2. If property has `[Required]` from DataAnnotations → required
3. The C# `required` keyword makes STJ treat the property as `JsonRequired` automatically at runtime, but this happens via runtime inference, not a per-property attribute

**Recommendation:** Document this limitation. Users should add `[JsonRequired]` or `[Required]` explicitly if they need the static analyzer to reliably detect required properties from the `required` keyword alone.

Source: [C# 11 Required Members specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-11.0/required-members), [Swashbuckle C# 11 required keyword support](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/2555)

**`init`-only property detection (NEW from audit):**

`init`-only is detected by checking if the property setter's return type has a required custom modifier of type `System.Runtime.CompilerServices.IsExternalInit`:
```csharp
var setMethod = property.SetMethod;
if (setMethod != null)
{
    var returnParam = setMethod.ReturnParameter;
    var modifiers = returnParam.GetRequiredCustomModifiers();
    bool isInitOnly = modifiers.Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
```
**Note:** `init`-only properties are NOT `readOnly` in OpenAPI — they can be written during creation but not updated. There is no direct OpenAPI mapping for "create-only" properties.

Source: [Detecting init-only properties with reflection](https://alistairevans.co.uk/2020/11/01/detecting-init-only-properties-with-reflection-in-c-9/)

### 6.8 Inheritance and Polymorphism

| Construct | OpenAPI Output | Static Detection | Priority |
|-----------|---------------|-----------------|----------|
| Class inheriting from base | `allOf: [{ $ref: base }, { properties: ... }]` (when `UseAllOfForInheritance` enabled) | Yes — `BaseType` | Important |
| Abstract base class | Typically `$ref` target only (not instantiable) | Yes — `IsAbstract` | Important |
| Interface as parameter type | Schema for the interface's declared properties | Yes | Important |
| `[JsonPolymorphic]` + `[JsonDerivedType]` | `oneOf` with `discriminator` (when `UseOneOfForPolymorphism` enabled) | Yes — attributes on base type | Important |
| `[SwaggerDiscriminator]` + `[SwaggerSubType]` | `oneOf` with `discriminator.mapping` | Yes — attributes on base type | Important |

### 6.9 Generic Types

| Construct | OpenAPI Handling | Priority |
|-----------|-----------------|----------|
| `ApiResponse<T>` where `T` = `UserDto` | Schema named `ApiResponseOfUserDto` (or similar) with `T` resolved | Critical |
| Multiple generic args `Pair<A,B>` | Fully resolved — `PairOfStringAndInt32` | Important |
| Open generics as parameters | Resolve at each usage site with actual type args | Critical |

**Schema naming:** Swashbuckle generates schema IDs from `Type.Name` + generic arg names. The static analyzer should replicate this: `{TypeName}Of{Arg1}And{Arg2}`.

### 6.10 Tuple Types

| Construct | OpenAPI Handling | Priority |
|-----------|-----------------|----------|
| `Tuple<string, int>` | `{ type: "object", properties: { Item1: string, Item2: integer } }` | Nice-to-have |
| `ValueTuple<string, int>` / `(string Name, int Age)` | Same — named elements may or may not be preserved | Nice-to-have |

### 6.11 Special Types

| Type | OpenAPI Handling | Priority |
|------|-----------------|----------|
| `IActionResult` / `ActionResult` (no type) | No response schema — must rely on `[ProducesResponseType]` | Critical |
| `ActionResult<T>` | Unwrap `T` as 200 response schema | Critical |
| `Task<T>` / `ValueTask<T>` | Unwrap to `T` | Critical |
| `IFormFile` | `requestBody` with `multipart/form-data`, property `type: string, format: binary` | Important |
| `IFormFileCollection` | Array of `IFormFile` | Important |
| `CancellationToken` | Skip — not an API parameter | Critical |
| `HttpContext`, `HttpRequest`, `HttpResponse` | Skip — framework types | Important |
| `ProblemDetails` | Standard schema per RFC 7807 | Important |
| `ValidationProblemDetails` | Standard schema extending ProblemDetails | Important |
| `FileResult` / `FileContentResult` / `FileStreamResult` | `type: string, format: binary` response | Important |
| `Results<T1, T2, ...>` (NEW) | Union return type — each `T` implements `IEndpointMetadataProvider` and contributes response metadata. Used in Minimal API but also works with controllers in .NET 9+ | **Important** |
| `Ok<T>`, `Created<T>`, `NotFound`, `BadRequest`, etc. (TypedResults) (NEW) | Concrete `IResult` implementations — generic arg determines response schema when returned directly | Important |
| `StatusCodeResult` (NEW) | No response body, just status code (e.g., 204 No Content) | Important |
| `CreatedAtActionResult` / `CreatedAtRouteResult` (NEW) | 201 response with `Location` header | Important |
| `ContentResult` (NEW) | `text/plain` response, `type: string` | Nice-to-have |
| `JsonResult` (NEW) | Schema is `object` (not typed — runtime value determines content) | Nice-to-have |
| `JsonElement` / `JsonNode` | `{}` (any type) | Nice-to-have |

---

## 7. OpenAPI Structural Constructs

### 7.1 `$ref` (Schema References)

**What generates `$ref`:**
- Any complex type (class, struct, record) used as a property, parameter, or response type → stored in `components/schemas/{SchemaId}` and referenced via `$ref`
- Reuse of the same type in multiple locations → single `$ref` to shared schema
- Schema ID default = type name (short, without namespace). For generics, includes type args.

**Static analyzer must:**
1. Build a schema repository (like `SchemaRepository`)
2. On first encounter of a type → generate full schema, store in `components/schemas`
3. On subsequent encounters → emit `$ref: "#/components/schemas/TypeName"`

### 7.2 `allOf`

**Generated when:**
- Inheritance + `UseAllOfForInheritance()` option → `allOf: [{$ref: Base}, {new properties}]`
- `UseAllOfToExtendReferenceSchemas()` option → wraps `$ref` in `allOf` to add `nullable`, `description`, etc. alongside a reference
- Nullable reference to a schema → `allOf: [{$ref: ...}]` + `nullable: true` at allOf level

### 7.3 `oneOf`

**Generated when:**
- `UseOneOfForPolymorphism()` + base type has known subtypes → `oneOf: [{$ref: Sub1}, {$ref: Sub2}]`
- `[JsonPolymorphic]` + `[JsonDerivedType]` attributes present
- `[SwaggerDiscriminator]` + `[SwaggerSubType]` attributes present

### 7.4 `anyOf`

**Rarely generated by Swashbuckle.** Not a common output. May appear with custom filters only.

### 7.5 `discriminator`

**Generated when:**
- `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` → `discriminator.propertyName: "type"`
- `[SwaggerDiscriminator("type")]` → same
- `[JsonDerivedType(typeof(Circle), "circle")]` → `discriminator.mapping.circle: "#/components/schemas/Circle"`
- `[SwaggerSubType(typeof(Circle), DiscriminatorValue = "circle")]` → same

### 7.6 Security Schemes

Security schemes are **entirely runtime-configured** in Swashbuckle:
```csharp
options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { ... });
options.AddSecurityRequirement(new OpenApiSecurityRequirement { ... });
```

**Static readability:** **No** — these are programmatic. The static analyzer should accept security scheme definitions via a config file or CLI arguments.

However, some attributes contribute:
| Attribute | Effect |
|-----------|--------|
| `[Authorize]` | Could infer "this operation requires auth" → `security: [{ Bearer: [] }]` |
| `[AllowAnonymous]` | Could infer "this operation has no security" |
| `[Authorize(Roles = "Admin")]` | Could infer scopes |
| `[Authorize(Policy = "RequireAdmin")]` | Policy name only — actual scheme is runtime |

**Recommendation for static analyzer (UPDATED from audit):** Accept security scheme definitions from config. Detect `[Authorize]` to apply security requirements to operations — this is now elevated to **Important** priority.

**`[Authorize]` detection details (NEW from audit):**

| Detection | OpenAPI Output |
|-----------|---------------|
| `[Authorize]` on controller | All actions in controller get `security: [{ schemeName: [] }]` (scheme from config) |
| `[Authorize]` on action | That specific action gets `security` requirement |
| `[Authorize(AuthenticationSchemes = "Bearer")]` | `security: [{ "Bearer": [] }]` — scheme name comes directly from attribute |
| `[Authorize(Roles = "Admin,Editor")]` | `security: [{ schemeName: ["Admin", "Editor"] }]` — roles map to scopes |
| `[AllowAnonymous]` on action | Overrides controller-level `[Authorize]` — emit `security: []` (empty array = no security) |
| `[Authorize(Policy = "RequireAdmin")]` | Policy name is informational only — actual scheme is runtime-configured |

**Inheritance logic:**
1. Check if controller class has `[Authorize]` → all actions inherit
2. Check if action method has `[Authorize]` → overrides/supplements controller-level
3. Check if action method has `[AllowAnonymous]` → removes security for that operation
4. `AuthenticationSchemes` property directly names the security scheme; if absent, use default from config
5. `Roles` property is a comma-separated string; split and use as scopes

Source: [Include OpenAPI metadata in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)

### 7.7 Tags

**Default behavior:** One tag per controller, tag name = controller name (minus "Controller" suffix).

| Source | OpenAPI `tags` field |
|--------|---------------------|
| Controller name (convention) | `operation.tags: ["Users"]` for `UsersController` |
| `[SwaggerOperation(Tags = new[] {"Users","Admin"})]` | Overrides default tag |
| `[SwaggerTag("User management")]` on controller | `document.tags[].description` |
| `<summary>` XML on controller class | `document.tags[].description` (via `XmlCommentsDocumentFilter`) |
| `[ApiExplorerSettings(GroupName = "v1")]` | Used for document grouping, NOT tags |

---

## 8. API Versioning Attributes

If using `Asp.Versioning.Mvc` / `Asp.Versioning.Http`:

| Attribute | OpenAPI Effect | Static via Reflection | Priority |
|-----------|---------------|----------------------|----------|
| `[ApiVersion("1.0")]` | Groups operations into versioned documents | Yes | Important |
| `[ApiVersion("2.0")]` | Multiple versions on one controller | Yes | Important |
| `[MapToApiVersion("2.0")]` | Specific action → specific version | Yes | Important |
| `[ApiVersionNeutral]` | Action appears in all versions | Yes | Important |

**Note:** Versioning typically affects which document an operation appears in, via `GroupName` integration with API Explorer. The static analyzer needs versioning config (URL segment, query parameter, or header) from settings.

---

## 9. Additional Attributes That May Affect OpenAPI

| Attribute | Namespace | OpenAPI Effect | Priority |
|-----------|-----------|---------------|----------|
| `[Obsolete]` | `System` | `deprecated: true` on operation or schema | Important |
| `[Display(Name = "User Name")]` | `System.ComponentModel.DataAnnotations` | May override property name display | Nice-to-have |
| `[DisplayFormat(DataFormatString = "yyyy-MM-dd")]` | `System.ComponentModel.DataAnnotations` | No direct mapping | Skip |
| `[ScaffoldColumn(false)]` | `System.ComponentModel.DataAnnotations` | No direct mapping | Skip |
| `[Key]` | `System.ComponentModel.DataAnnotations` | No direct mapping | Skip |
| `[EndpointSummary("text")]` | `Microsoft.AspNetCore.Http` (.NET 7+) | `operation.summary` — works on controllers in .NET 9+ | **Important** (upgraded) |
| `[EndpointDescription("text")]` | `Microsoft.AspNetCore.Http` (.NET 7+) | `operation.description` — works on controllers in .NET 9+ | **Important** (upgraded) |
| `[EndpointName("name")]` | `Microsoft.AspNetCore.Routing` | `operation.operationId` (Minimal API only) | Nice-to-have |
| `[Tags("tag1","tag2")]` | `Microsoft.AspNetCore.Http` (.NET 7+) | `operation.tags` — works on controllers in .NET 9+ | **Important** (upgraded) |
| `[Description("text")]` (NEW) | `System.ComponentModel` | `parameter.description`, `schema.description` — **primary metadata source in .NET 9+** | **Critical** |
| `[RequestSizeLimit(bytes)]` | `Microsoft.AspNetCore.Mvc` | No direct OpenAPI mapping | Skip |
| `[RequestFormLimits]` | `Microsoft.AspNetCore.Mvc` | No direct OpenAPI mapping | Skip |
| `[IgnoreAntiforgeryToken]` | `Microsoft.AspNetCore.Mvc` | No direct OpenAPI mapping | Skip |
| `[FormatFilter]` | `Microsoft.AspNetCore.Mvc` | No direct OpenAPI mapping | Skip |
| `[ServiceFilter]` / `[TypeFilter]` | `Microsoft.AspNetCore.Mvc` | No direct OpenAPI mapping | Skip |

---

## 10. Description Fallback Chains (NEW)

A key implementation detail — the priority order for resolving descriptions from multiple sources. The static analyzer should check these in order, using the first match found.

### 10.1 For Operations (methods):

| Priority | Source | OpenAPI Field |
|----------|--------|---------------|
| 1 | `[SwaggerOperation(Summary = "...")]` | `operation.summary` |
| 2 | `[EndpointSummary("...")]` | `operation.summary` |
| 3 | XML `<summary>` tag | `operation.summary` |
| 4 | `[SwaggerOperation(Description = "...")]` | `operation.description` |
| 5 | `[EndpointDescription("...")]` | `operation.description` |
| 6 | XML `<remarks>` tag | `operation.description` |

### 10.2 For Parameters:

| Priority | Source | OpenAPI Field |
|----------|--------|---------------|
| 1 | `[SwaggerParameter(Description = "...")]` | `parameter.description` |
| 2 | `[Description("...")]` (System.ComponentModel) | `parameter.description` |
| 3 | XML `<param name="x">` tag | `parameter.description` |

### 10.3 For Schema Properties:

| Priority | Source | OpenAPI Field |
|----------|--------|---------------|
| 1 | `[SwaggerSchema(Description = "...")]` | `schema.description` |
| 2 | `[Description("...")]` (System.ComponentModel) | `schema.description` |
| 3 | XML `<summary>` on property/type | `schema.description` |
| 4 | `[Display(Description = "...")]` | `schema.description` (lowest priority fallback) |

### 10.4 For Response Descriptions:

| Priority | Source | OpenAPI Field |
|----------|--------|---------------|
| 1 | `[SwaggerResponse(200, "description")]` | `responses.200.description` |
| 2 | `[ProducesResponseType(200, Description = "...")]` (.NET 10+) | `responses.200.description` |
| 3 | XML `<response code="200">description</response>` | `responses.200.description` |
| 4 | HTTP status code default text (e.g., "OK", "Not Found") | fallback |

Source: [Include OpenAPI metadata in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0), [OpenAPI document generation in .NET 9](https://devblogs.microsoft.com/dotnet/dotnet9-openapi/)

---

## 11. Implementation Checklist for Static Analyzer

### Phase 1 — Critical (Minimum Viable)

1. Discover controllers: classes with `[ApiController]` or inheriting `ControllerBase`
2. Exclude `[NonController]`, `[ApiExplorerSettings(IgnoreApi = true)]`
3. Discover actions: public methods with HTTP method attributes; exclude `[NonAction]`
4. Build paths: combine `[Route]` on class + HTTP attribute template; resolve `[controller]`, `[action]`, `[area]` tokens
5. Determine HTTP method from attribute type
6. Extract `operationId` from HTTP attribute `Name` property or `[SwaggerOperation(OperationId)]`
7. Determine parameter binding: `[FromRoute]`→path, `[FromQuery]`→query, `[FromHeader]`→header, `[FromBody]`→requestBody; apply `[ApiController]` inference rules for unannotated params
8. Build schemas: recurse through DTOs, handle primitives, collections, dictionaries, enums, generics, `$ref` deduplication
9. Handle nullable: `Nullable<T>` and NRT analysis
10. Apply `[Required]`, `[JsonRequired]`, C# `required` keyword → `required` array
11. Apply `[JsonPropertyName]` → property key rename; `[JsonIgnore]` → exclude
12. Apply `[ProducesResponseType]` / `[SwaggerResponse]` → response objects
13. Parse XML docs: `<summary>`, `<remarks>`, `<param>`, `<response>`, `<example>`
14. Unwrap `Task<T>`, `ActionResult<T>` return types
15. (NEW) Apply `[Description]` on parameters — primary metadata source in .NET 9+
16. (NEW) Apply `[EndpointSummary]` / `[EndpointDescription]` on controllers — works in .NET 9+
17. (NEW) Extract `ProducesResponseType.Description` property (.NET 10+) for response descriptions

### Phase 2 — Important

18. Apply validation attributes: `[StringLength]`, `[Range]`, `[RegularExpression]`, `[MinLength]`, `[MaxLength]`
19. Apply `[SwaggerOperation]` (Summary, Description, Tags)
20. Apply `[SwaggerParameter]`, `[SwaggerRequestBody]` descriptions
21. Apply `[SwaggerTag]` for document-level tag descriptions
22. Apply `[SwaggerSchema]` (Description, Format, ReadOnly, WriteOnly, Nullable, Required, Title)
23. Handle `[Produces]` / `[Consumes]` content types
24. Handle `[ProducesDefaultResponseType]`
25. Handle `[DefaultValue]` → `schema.default`
26. Handle `[Description]` from System.ComponentModel on types/properties
27. Handle `[Obsolete]` → `deprecated: true`
28. Handle polymorphism: `[JsonPolymorphic]`+`[JsonDerivedType]` or `[SwaggerDiscriminator]`+`[SwaggerSubType]`
29. Handle inheritance: `allOf` for base types
30. (NEW) Handle `[Authorize]` → security annotation with config-defined scheme (elevated from Phase 3)
31. (NEW) Handle `[Authorize(Roles = "...")]` → security scopes
32. (NEW) Handle `[AllowAnonymous]` → remove security from operation
33. (NEW) Handle `[JsonStringEnumMemberName]` (.NET 9+) → enum member string override
34. (NEW) Handle `[JsonUnmappedMemberHandling(Disallow)]` → `additionalProperties: false`
35. (NEW) Handle `[ActionName]` → affects `[action]` token resolution
36. (NEW) Handle `[Tags]` on controllers (.NET 9+)
37. (NEW) Handle `Results<T1, T2, ...>` return type unwrapping

### Phase 3 — Nice-to-Have

38. API versioning support
39. API conventions (`[ApiConventionType]`)
40. `[DataType]` → format overrides
41. Enum string conversion detection (including `JsonStringEnumConverter<TEnum>` generic form)
42. Tuple types
43. (UPDATED) `[ExcludeFromDescription]` detection
44. `<inheritdoc/>` resolution for unresolved cases in XML
45. `[SwaggerOperationFilter]` / `[SwaggerSchemaFilter]` detection with warnings
46. (NEW) `[Display(Description = "...")]` as description fallback
47. (NEW) `JsonStringEnumConverter<TEnum>` generic form detection on properties

---

## Sources

- [Swashbuckle.AspNetCore GitHub Repository](https://github.com/domaindrivendev/Swashbuckle.AspNetCore)
- [SwaggerSchemaAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerSchemaAttribute.cs)
- [SwaggerOperationAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerOperationAttribute.cs)
- [SwaggerParameterAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerParameterAttribute.cs)
- [SwaggerRequestBodyAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerRequestBodyAttribute.cs)
- [SwaggerResponseAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerResponseAttribute.cs)
- [SwaggerTagAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerTagAttribute.cs)
- [SwaggerSubTypeAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerSubTypeAttribute.cs)
- [SwaggerDiscriminatorAttribute source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Annotations/SwaggerDiscriminatorAttribute.cs)
- [SchemaGenerator source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.SwaggerGen/SchemaGenerator/SchemaGenerator.cs)
- [JsonSerializerDataContractResolver source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.SwaggerGen/SchemaGenerator/JsonSerializerDataContractResolver.cs)
- [XmlComments filters](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/tree/master/src/Swashbuckle.AspNetCore.SwaggerGen/XmlComments)
- [Annotations System - DeepWiki](https://deepwiki.com/domaindrivendev/Swashbuckle.AspNetCore/6.1-annotations-system)
- [ASP.NET Core Web API docs](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0)
- [ASP.NET Core OpenAPI metadata](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)
- [ASP.NET Core API conventions](https://learn.microsoft.com/en-us/aspnet/core/web-api/advanced/conventions?view=aspnetcore-10.0)
- [Swashbuckle Annotations NuGet](https://www.nuget.org/packages/Swashbuckle.AspNetCore.Annotations)
- [API Versioning Swashbuckle Integration](https://github.com/dotnet/aspnet-api-versioning/wiki/Swashbuckle-Integration)
- [Polymorphic serialization with STJ + OpenAPI](https://nikiforovall.blog/dotnet/aspnetcore/2024/04/06/openapi-polymorphism.html)
- [NRT support in Swashbuckle](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/1686)
- [What's new in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0) (NEW — from audit)
- [OpenAPI document generation in .NET 9](https://devblogs.microsoft.com/dotnet/dotnet9-openapi/) (NEW — from audit)
- [What's new in System.Text.Json in .NET 9](https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-9/) (NEW — from audit)
- [Roslyn Nullable Metadata spec](https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md) (NEW — from audit)
- [C# 11 Required Members specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-11.0/required-members) (NEW — from audit)
- [Detecting init-only properties with reflection](https://alistairevans.co.uk/2020/11/01/detecting-init-only-properties-with-reflection-in-c-9/) (NEW — from audit)
- [ProducesResponseType Description property (ASP.NET Core 10)](https://github.com/dotnet/aspnetcore/issues/55656) (NEW — from audit)
- [JsonUnmappedMemberHandling and OpenAPI](https://github.com/dotnet/aspnetcore/issues/57981) (NEW — from audit)
- [ExcludeFromDescription not working for controllers](https://github.com/dotnet/aspnetcore/issues/57425) (NEW — from audit)
- [Swashbuckle C# 11 required keyword support](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/2555) (NEW — from audit)
