# Comprehensive Audit: OpenAPI Attribute Catalog Gaps

> Audit date: 2026-03-24
> Scope: What can be statically extracted from compiled .NET DLLs + XML docs for OpenAPI spec generation
> Baseline: `03-openapi-attributes-catalog.md`

---

## Executive Summary

The existing catalog (03-openapi-attributes-catalog.md) is **solid** — it covers approximately 90% of what matters. This audit identifies **23 missing items**, **11 incomplete items**, and **8 new .NET 9/10 features** that should be considered. The most impactful gaps are:

1. `[EndpointSummary]` / `[EndpointDescription]` now work on controllers (not just Minimal API) in .NET 9+
2. `[Description]` (System.ComponentModel) is now a first-class OpenAPI metadata provider in .NET 9+ built-in OpenAPI
3. `ProducesResponseType` gained a `Description` property in .NET 10
4. `[JsonStringEnumMemberName]` (.NET 9) affects enum schema values
5. `[JsonUnmappedMemberHandling(Disallow)]` should map to `additionalProperties: false`
6. NRT byte-array encoding for nested generics needs deeper treatment
7. `[Authorize]` -> security mapping is more feasible than noted

---

## 1. MISSING Items

### 1.1 ASP.NET Core MVC / Routing Attributes

| Item | OpenAPI Mapping | Static? | Priority | Recommendation |
|------|----------------|---------|----------|----------------|
| `[ActionName("CustomName")]` | Affects `[action]` token resolution in route templates | Yes | Important | **Add** — if `[action]` token is used, `ActionName` overrides the default method name |
| `[EndpointSummary("text")]` on controllers | `operation.summary` | Yes | **Critical** | **Add** — as of .NET 9+, this works on controllers too, not just Minimal API. Our catalog lists it as "Nice-to-have (Minimal API only)" but it is now relevant for controller-based APIs |
| `[EndpointDescription("text")]` on controllers | `operation.description` | Yes | **Critical** | **Add** — same reasoning as EndpointSummary |
| `[ExcludeFromDescription]` | Excludes endpoint from OpenAPI | Yes | Important | **Add** — equivalent to `ApiExplorerSettings(IgnoreApi=true)` for Minimal APIs. Worth detecting for completeness |
| `[Tags("tag1","tag2")]` on controllers | `operation.tags` | Yes | Important | **Add** — works on controller actions in .NET 9+, alternative to `[SwaggerOperation(Tags=...)]` |
| `[EnableCors]` / `[DisableCors]` | No direct OpenAPI field | Yes | Skip | **Skip** — CORS has no OpenAPI representation |
| `[RequestFormLimits]` | No direct OpenAPI mapping | Yes | Skip | **Skip** — already correctly marked as Skip in catalog |
| `[DisableRequestSizeLimit]` | No direct OpenAPI mapping | Yes | Skip | **Skip** |
| `[BindProperty]` / `[BindProperties]` | Affects which properties are bound from form/query | Yes | Nice-to-have | **Skip for now** — MVC Pages specific, not typical for API controllers |

### 1.2 Security Attributes

| Item | OpenAPI Mapping | Static? | Priority | Recommendation |
|------|----------------|---------|----------|----------------|
| `[Authorize]` → `security` | `operation.security: [{ schemeName: [] }]` | Yes | **Important** | **Add with config** — detect `[Authorize]` and apply security requirement from config-defined scheme. The catalog mentions this but it should be elevated from "Nice-to-have" to "Important" |
| `[Authorize(Roles = "Admin")]` | `operation.security: [{ schemeName: ["Admin"] }]` (as scopes) | Yes | Important | **Add** — Roles can map to OAuth2 scopes |
| `[Authorize(AuthenticationSchemes = "Bearer")]` | Maps to specific security scheme name | Yes | Important | **Add** — `AuthenticationSchemes` property directly names the scheme |
| `[AllowAnonymous]` | Override — remove security from this operation | Yes | Important | **Add** — if controller has `[Authorize]`, individual actions with `[AllowAnonymous]` should not have `security` |

### 1.3 System.Text.Json Attributes

| Item | OpenAPI Mapping | Static? | Priority | Recommendation |
|------|----------------|---------|----------|----------------|
| `[JsonStringEnumMemberName("custom_name")]` (.NET 9+) | Overrides enum member string value in `enum: [...]` | Yes | **Important** | **Add** — new in .NET 9. When enum is serialized as string, this attribute overrides the serialized name. E.g., `[JsonStringEnumMemberName("in_progress")] InProgress` → enum value is `"in_progress"` |
| `[JsonUnmappedMemberHandling(Disallow)]` (.NET 8+) | `additionalProperties: false` | Yes | Important | **Add** — when applied to a type, the JSON Schema should include `additionalProperties: false`. The built-in .NET OpenAPI generator does this (with known bugs). We should too |
| `[JsonObjectCreationHandling]` (.NET 8+) | No direct OpenAPI mapping | Yes | Skip | **Skip** — affects deserialization behavior, not schema shape |
| `[JsonSourceGenerationOptions]` | No direct OpenAPI mapping (compile-time serialization config) | No | Skip | **Skip** — source generator metadata, not available via reflection on the target DLL |

### 1.4 System.ComponentModel Attributes — Elevated Importance

| Item | OpenAPI Mapping | Static? | Priority | Recommendation |
|------|----------------|---------|----------|----------------|
| `[Description("text")]` on parameters | `parameter.description` | Yes | **Critical** | **Elevate** — in .NET 9+ built-in OpenAPI, `[Description]` is the PRIMARY attribute for parameter descriptions (replacing the Swashbuckle-only `[SwaggerParameter]`). Our catalog lists it as "Important" for schema.description but does not mention parameter usage |
| `[Description("text")]` on types/properties | `schema.description` (fallback if no XML doc) | Yes | **Critical** | **Elevate** — .NET 9+ built-in OpenAPI uses `[Description]` as a primary source for schema descriptions, not just a "fallback" |

### 1.5 System.ComponentModel.DataAnnotations — Missing

| Item | OpenAPI Mapping | Static? | Priority | Recommendation |
|------|----------------|---------|----------|----------------|
| `[Display(Name = "User Name", Description = "...")]` | `Description` property → `schema.description`; `Name` property → informational only | Yes | Nice-to-have | **Consider** — `Display.Description` can serve as another fallback for schema descriptions. Low priority since `[Description]` and XML docs are more standard |
| `[FileExtensions(Extensions = ".jpg,.png")]` | No standard OpenAPI mapping | Yes | Skip | **Skip** |
| `[Compare("OtherProperty")]` | No OpenAPI mapping (server-side validation only) | N/A | Skip | Already correctly Skip in catalog |

### 1.6 API Versioning

| Item | OpenAPI Mapping | Static? | Priority | Recommendation |
|------|----------------|---------|----------|----------------|
| `[ApiVersion("1.0")]` with `[MapToApiVersion]` | Determines which document an operation belongs to | Yes | Important | Already in catalog as "Important" but listed as "Future (not now)" in implementation plan. **Recommend keeping as Phase 3** but ensure the attribute reading infrastructure is ready |

---

## 2. INCOMPLETE Items

### 2.1 ProducesResponseType — Missing `Description` Property (.NET 10)

**Current catalog:** Lists `Type`, `StatusCode`, `ContentTypes` properties.

**Gap:** In .NET 10, `ProducesResponseTypeAttribute`, `ProducesAttribute`, and `ProducesDefaultResponseTypeAttribute` all gained an optional `Description` property that maps directly to `responses.{code}.description`.

```csharp
[ProducesResponseType<MyDto>(200, Description = "The requested resource")]
```

**Priority:** Critical
**Recommendation:** Add `Description` property extraction for all three attributes. This is the official way to set response descriptions without Swashbuckle `[SwaggerResponse]` or XML `<response>` comments in .NET 10+.

### 2.2 NRT Detection — Incomplete for Nested Generics

**Current catalog:** Correctly describes `NullableAttribute` with `byte[]` for generic types but lacks depth.

**Gap:** The byte-array encoding follows a pre-order tree traversal:
- `Dictionary<string, List<string?>>` → bytes: `[1, 1, 1, 2]` — root(1), key:string(1), List(1), element:string?(2)
- `Task<ActionResult<MyDto?>>` → must unwrap both wrappers, then check NRT on the inner type
- Containing types (nested classes) also contribute bytes

**Rules for MetadataLoadContext:**
1. If property has no `NullableAttribute`, fall back to `NullableContextAttribute` on declaring type
2. If type has no `NullableContextAttribute`, fall back to assembly-level `NullableContextAttribute`
3. If nothing found, treat as oblivious (0) — assume nullable for reference types (conservative)
4. Single-byte optimization: when all bytes are the same, compiler emits single byte instead of array

**Priority:** Critical
**Recommendation:** Document the full tree-traversal algorithm. Implementation must handle the byte-index tracking across generic type argument traversal.

### 2.3 Enum Handling — Missing String Conversion Detection Methods

**Current catalog:** Mentions `[JsonConverter(typeof(JsonStringEnumConverter))]` on enum type.

**Gap:** Multiple ways to enable string enum serialization:
1. `[JsonConverter(typeof(JsonStringEnumConverter))]` on enum type — **covered**
2. `[JsonConverter(typeof(JsonStringEnumConverter<MyEnum>))]` — generic form, **NOT covered**
3. `[JsonConverter(typeof(JsonStringEnumConverter))]` on a property — affects only that property
4. `[EnumMember(Value = "custom")]` — **covered** (from System.Runtime.Serialization)
5. `[JsonStringEnumMemberName("custom")]` (.NET 9+) — **NOT covered**
6. CLI flag `--enum-as-string` — **covered**

**Priority:** Important
**Recommendation:** Add detection of `JsonStringEnumConverter<TEnum>` and `[JsonStringEnumMemberName]`.

### 2.4 Return Type Unwrapping — Missing Types

**Current catalog:** Lists `Task<T>`, `ValueTask<T>`, `ActionResult<T>`, `IActionResult`, `FileResult`, etc.

**Gap:** Missing:
- `Results<T1, T2, ...>` — union return type from `Microsoft.AspNetCore.Http`. Used in Minimal API but also works with controllers in .NET 9+. Each `T` implements `IEndpointMetadataProvider` and contributes response metadata
- `Ok<T>`, `NotFound`, `Created<T>`, `BadRequest`, etc. (TypedResults) — these are the concrete `IResult` implementations. When returned directly (not via `Results<>` union), the return type's generic arg determines response schema
- `ObjectResult` — already covered implicitly but should be explicit
- `JsonResult` — schema is `object` (not typed)
- `ContentResult` — `text/plain` response, `type: string`
- `StatusCodeResult` — no body, just status code
- `CreatedAtActionResult`, `CreatedAtRouteResult` — 201 with Location header

**Priority:** Important (for Results<>), Nice-to-have (for others)
**Recommendation:** Add `Results<T1,...>` unwrapping. The TypedResults concrete types are only relevant if users return them directly without `[ProducesResponseType]`.

### 2.5 `[JsonIgnore]` — Condition Property Not Fully Documented

**Current catalog:** Mentions `Condition` property and states "only `Always` excludes."

**Gap:** The `Condition` values and their schema effects:
- `Always` → exclude property from schema entirely
- `WhenWritingNull` → property stays in schema; consider marking as nullable
- `WhenWritingDefault` → property stays in schema
- `Never` → always include (override any global setting)

Also: `[JsonIgnore]` on a property with `Condition = WhenWritingNull` could inform nullable inference.

**Priority:** Nice-to-have (behavior already correctly described, just needs more detail)

### 2.6 `required` Keyword Detection — Needs MetadataLoadContext Specifics

**Current catalog:** Correctly identifies `RequiredMemberAttribute` on type and per-property detection.

**Gap:** Specific detection via MetadataLoadContext:
1. Check type for `System.Runtime.CompilerServices.RequiredMemberAttribute` (indicates type has required members)
2. For each property, check for `System.Runtime.CompilerServices.RequiredMemberAttribute` on the property itself — **this is wrong**. The property does NOT get `RequiredMemberAttribute`. Instead:
   - In .NET 7+, use `PropertyInfo.GetRequiredCustomModifiers()` to check for `IsExternalInit`-like modifiers — but this is for init-only
   - For `required`, the compiler emits metadata that `System.Text.Json` reads via `JsonPropertyInfo.IsRequired` at runtime
   - Via MetadataLoadContext: check if the property has `[JsonRequired]` OR if it appears in the type's constructor as a required parameter with `SetsRequiredMembersAttribute` absent

**Correct detection strategy:**
```
IF type has RequiredMemberAttribute
AND constructor does NOT have SetsRequiredMembersAttribute
THEN all properties matching C# `required` keyword → add to schema.required
```
The challenge: MetadataLoadContext cannot directly tell which properties have the `required` modifier. The compiler does NOT emit a per-property attribute for `required`. Instead, `System.Text.Json` in .NET 7+ infers it at runtime. For static analysis, the safest approach is:
- If property has `[JsonRequired]` → required (works regardless)
- If property has `[Required]` from DataAnnotations → required
- The C# `required` keyword makes STJ treat the property as JsonRequired automatically, but this happens at runtime

**Priority:** Important
**Recommendation:** Document this limitation. Recommend users add `[JsonRequired]` or `[Required]` explicitly if they need the static analyzer to detect required properties from the `required` keyword alone.

### 2.7 `init`-Only Properties — Detection Method

**Current catalog:** Mentions `IsInitOnly` on setter but doesn't explain detection.

**Gap:** `init`-only is detected by checking if the property setter's return type has a required custom modifier of type `System.Runtime.CompilerServices.IsExternalInit`:

```csharp
var setMethod = property.SetMethod;
if (setMethod != null)
{
    var returnParam = setMethod.ReturnParameter;
    var modifiers = returnParam.GetRequiredCustomModifiers();
    bool isInitOnly = modifiers.Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
```

**Priority:** Important
**Recommendation:** Add this detection method to the catalog. `init`-only properties are NOT `readOnly` in OpenAPI — they can be written during creation but not updated. There is no direct OpenAPI mapping for "create-only" properties.

### 2.8 XML Documentation — `<inheritdoc>` Resolution

**Current catalog:** Says "if present in XML file, follow `cref`" and marks as Nice-to-have.

**Gap:** The C# compiler (since .NET 5) resolves `<inheritdoc>` at compile time and writes the resolved content into the XML file. So in most cases, the XML file already contains the resolved documentation. However:
- If the XML file was produced by an older compiler, `<inheritdoc/>` may be unresolved
- If `<inheritdoc cref="SomeType.SomeMethod"/>` is used, it may or may not be resolved
- Our static analyzer should: (1) first check if content is already resolved (no `<inheritdoc>` tag present), (2) if `<inheritdoc>` is present, attempt to resolve by walking base types/interfaces

**Priority:** Nice-to-have
**Recommendation:** Keep as Phase 3. In practice, modern compilers resolve this.

### 2.9 XML `<exception>` Tag

**Current catalog:** Listed as "not mapped."

**Gap:** `<exception cref="T:System.ArgumentException">...</exception>` could theoretically map to error responses. However:
- Swashbuckle does not process this
- The mapping is ambiguous (what HTTP status code does `ArgumentException` map to?)
- Would require a configurable exception-to-status mapping

**Priority:** Skip
**Recommendation:** Skip — too ambiguous without configuration. `<response>` tag is the correct way to document responses.

### 2.10 `[Consumes]` — Missing Multiple Content Type Handling

**Current catalog:** Lists `[Consumes("application/json")]` correctly.

**Gap:** `[Consumes]` with multiple content types generates multiple `requestBody.content` entries:
```csharp
[Consumes("application/json", "application/xml")]
```
Generates:
```json
"requestBody": {
  "content": {
    "application/json": { "schema": { ... } },
    "application/xml": { "schema": { ... } }
  }
}
```
Also: `[Consumes("multipart/form-data")]` should trigger `IFormFile` property schema generation.

**Priority:** Important
**Recommendation:** Ensure multiple content type handling is explicit in implementation.

### 2.11 `IFormFile` / `IFormFileCollection` — Parameter vs Property Detection

**Current catalog:** Lists `IFormFile` → `requestBody` with `multipart/form-data`.

**Gap:** Multiple scenarios:
1. `IFormFile` as a method parameter → entire body is file upload
2. `IFormFile` as a property in a model class → multipart form with file field
3. `IFormFileCollection` → array of files
4. `[FromForm] MyModel model` where `MyModel` has `IFormFile` properties → multipart with mixed fields
5. `[FromForm] string name, IFormFile file` → multiple parameters combined into multipart body

**Priority:** Important
**Recommendation:** Document all scenarios. The schema for `IFormFile` is `{ type: "string", format: "binary" }`.

---

## 3. NEW in .NET 9/10

### 3.1 .NET 9 Additions

| Feature | OpenAPI Impact | Priority | Recommendation |
|---------|---------------|----------|----------------|
| Built-in OpenAPI via `Microsoft.AspNetCore.OpenApi` | Replaces Swashbuckle for many projects. Uses `System.Text.Json` JSON Schema exporter internally. Our tool remains relevant as it avoids runtime dependency | N/A (context) | Note in docs |
| `[Description]` as primary metadata source | Now the official way to add descriptions to parameters and properties for OpenAPI in .NET 9+ (not just Swashbuckle fallback) | **Critical** | **Elevate priority** |
| `[JsonStringEnumMemberName]` attribute | Custom enum member serialization names | Important | **Add** |
| OpenAPI 3.1 as option (not default in .NET 9) | Nullable encoding changes: `type: ["string", "null"]` instead of `nullable: true` | Important | Already in catalog |

### 3.2 .NET 10 Additions

| Feature | OpenAPI Impact | Priority | Recommendation |
|---------|---------------|----------|----------------|
| OpenAPI 3.1 as DEFAULT | All documents default to 3.1. Must handle nullable encoding differently for 3.0 vs 3.1 | **Critical** | Ensure `--openapi-version` flag switches encoding strategy |
| `ProducesResponseType.Description` property | `responses.{code}.description` | **Critical** | **Add** |
| `Produces.Description` property | `responses.{code}.description` | **Critical** | **Add** |
| `ProducesDefaultResponseType.Description` | `responses.default.description` | **Critical** | **Add** |
| OpenAPI in YAML format | Serve as `.yaml` | Already planned | Already in CLI design |
| `$ref` with sibling properties in 3.1 | Description alongside `$ref` now valid | Important | Update `allOf` wrapping logic — in 3.1, no need to wrap `$ref` in `allOf` just to add `description` |
| Form data enum schema fix | Enum params in form data use actual enum type | Nice-to-have | Follow same enum logic |
| JSON Schema draft 2020-12 compliance | `type` as array, `$ref` siblings, etc. | Important | Handled by Microsoft.OpenApi v3 model |

### 3.3 .NET 10 — Microsoft.OpenApi v2.0 Breaking Changes

Our tool uses Microsoft.OpenApi v3.5.0 which is newer. Key changes that affect us:
- `OpenApiSchema` properties are now via interfaces (`IOpenApiSchema`)
- `Nullable` property removed from schema — use `JsonSchemaType.Null` flag in `Type`
- `OpenApiAny` → `JsonNode`
- These are already accounted for in our `04-library-design.md`

---

## 4. Items Confirmed as Correctly Excluded

These items were reviewed and are correctly marked as Skip or out of scope:

| Item | Reason for Exclusion |
|------|---------------------|
| `IOperationFilter`, `IDocumentFilter`, `ISchemaFilter` | Runtime code, cannot execute statically |
| `[ModelBinder]` | Custom binding logic, not interpretable |
| `[JsonConverter(typeof(...))]` | Arbitrary conversion, not interpretable (but detectable — log warning) |
| Minimal API fluent methods (`.WithName()`, `.Produces()`) | Runtime code in `Program.cs` |
| Conventional routing | Runtime code |
| `[RequestFormLimits]`, `[DisableRequestSizeLimit]` | No OpenAPI mapping |
| `[FormatFilter]`, `[ServiceFilter]`, `[TypeFilter]` | No OpenAPI mapping |
| `[ScaffoldColumn]`, `[Key]` | ORM/UI attributes, no OpenAPI mapping |
| `[DisplayFormat]` | No standard OpenAPI mapping |
| `[Compare]` | Server-side validation only |
| `[DeniedValues]` | No OpenAPI mapping (opposite of enum constraint) |
| `[Browsable(false)]` | Non-standard, NSwag-specific |
| `[JsonObjectCreationHandling]` | Deserialization behavior, no schema effect |
| `[JsonSourceGenerationOptions]` | Source generator config, not in target DLL |
| `[JsonNumberHandling]` | No OpenAPI mapping |

---

## 5. Revised Priority Matrix

### Phase 1 — Critical (Must Have for MVP)

Everything currently in Phase 1 of the catalog, PLUS:
1. **`[Description]` on parameters** — now the standard way in .NET 9+
2. **`[EndpointSummary]` / `[EndpointDescription]` on controllers** — works in .NET 9+
3. **`ProducesResponseType.Description` property** — .NET 10+

### Phase 2 — Important (Should Have)

Everything currently in Phase 2, PLUS:
4. **`[Authorize]` → security annotation** (with config-defined scheme) — elevate from Nice-to-have
5. **`[Authorize(Roles = "...")]`** → security scopes
6. **`[AllowAnonymous]`** → remove security
7. **`[JsonStringEnumMemberName]`** (.NET 9+)
8. **`[JsonUnmappedMemberHandling(Disallow)]`** → `additionalProperties: false`
9. **`[ActionName]`** → affects `[action]` token resolution
10. **`[Tags]`** on controllers (.NET 9+)
11. **`Results<T1,...>` return type unwrapping**

### Phase 3 — Nice to Have

12. `[ExcludeFromDescription]` detection
13. `<inheritdoc/>` resolution for unresolved cases
14. `[Display(Description = "...")]` as fallback
15. `JsonStringEnumConverter<TEnum>` generic form detection
16. `[DataType]` → format mapping
17. Tuple types

---

## 6. Complete Attribute Extraction Priority Checklist

For reference, here is the complete list organized by detection complexity:

### Trivial (read single attribute property)
- `[HttpGet]`, `[HttpPost]`, etc. — Template, Name
- `[Route]` — Template
- `[ApiController]`, `[NonAction]`, `[NonController]`
- `[FromRoute]`, `[FromQuery]`, `[FromBody]`, `[FromHeader]`, `[FromForm]`, `[FromServices]`, `[BindNever]`
- `[Produces]`, `[Consumes]` — ContentTypes
- `[ProducesResponseType]` — Type, StatusCode, ContentTypes, **Description** (.NET 10)
- `[ProducesDefaultResponseType]` — Type, **Description** (.NET 10)
- `[SwaggerOperation]` — Summary, Description, OperationId, Tags
- `[SwaggerResponse]` — StatusCode, Description, Type, ContentTypes
- `[SwaggerParameter]` — Description, Required
- `[SwaggerRequestBody]` — Description, Required
- `[SwaggerTag]` — Description, ExternalDocsUrl
- `[SwaggerSchema]` — Description, Title, Format, ReadOnly, WriteOnly, Nullable, Required
- `[SwaggerDiscriminator]` — PropertyName
- `[SwaggerSubType]` — SubType, DiscriminatorValue
- `[ApiExplorerSettings]` — IgnoreApi, GroupName
- `[EndpointSummary]`, `[EndpointDescription]`, `[Tags]`, `[EndpointName]`
- `[ExcludeFromDescription]`
- `[Area]` — RouteValue
- `[Obsolete]` — deprecated
- `[Description]` — text
- `[DefaultValue]` — value
- `[ReadOnly]` — flag
- `[Required]` — AllowEmptyStrings
- `[StringLength]` — MaximumLength, MinimumLength
- `[MaxLength]`, `[MinLength]`
- `[Length]` — MinimumLength, MaximumLength
- `[Range]` — Minimum, Maximum
- `[RegularExpression]` — Pattern
- `[EmailAddress]`, `[Phone]`, `[Url]`, `[CreditCard]`
- `[AllowedValues]` — Values
- `[Base64String]`
- `[DataType]` — DataType enum
- `[EnumDataType]` — EnumType
- `[JsonPropertyName]` — Name
- `[JsonIgnore]` — Condition
- `[JsonRequired]`
- `[JsonExtensionData]`
- `[JsonPolymorphic]` — TypeDiscriminatorPropertyName
- `[JsonDerivedType]` — DerivedType, TypeDiscriminator
- `[JsonStringEnumMemberName]` — Name (.NET 9+)
- `[JsonUnmappedMemberHandling]` — Handling (.NET 8+)
- `[JsonConstructor]`
- `[EnumMember]` — Value
- `[Authorize]` — Roles, AuthenticationSchemes, Policy
- `[AllowAnonymous]`
- `[ActionName]` — Name
- `[ApiVersion]`, `[MapToApiVersion]`, `[ApiVersionNeutral]`

### Moderate (type system analysis)
- Nullable<T> unwrapping
- Collection/Dictionary interface detection (IEnumerable<T>, IDictionary<K,V>, etc.)
- Generic type argument resolution
- Inheritance chain walking (BaseType)
- Return type unwrapping (Task<T>, ActionResult<T>, Results<T1,T2,...>)
- Record positional parameter detection
- IFormFile / IFormFileCollection detection

### Complex (multi-step analysis)
- NRT detection via NullableAttribute/NullableContextAttribute byte-array traversal
- `required` keyword detection (RequiredMemberAttribute + SetsRequiredMembersAttribute)
- `init`-only detection (IsExternalInit modreq)
- Route template composition with token replacement
- `[ApiController]` binding inference (parameter source from type + route matching)
- API Conventions matching (DefaultApiConventions)
- Schema $ref deduplication and cycle detection

---

## 7. Swashbuckle Annotations — Completeness Verification

The Swashbuckle.AspNetCore.Annotations package contains **exactly 10 attribute classes**:

1. `SwaggerOperationAttribute` — **Covered**
2. `SwaggerResponseAttribute` — **Covered**
3. `SwaggerParameterAttribute` — **Covered**
4. `SwaggerRequestBodyAttribute` — **Covered**
5. `SwaggerTagAttribute` — **Covered**
6. `SwaggerSchemaAttribute` — **Covered**
7. `SwaggerDiscriminatorAttribute` — **Covered**
8. `SwaggerSubTypeAttribute` — **Covered**
9. `SwaggerOperationFilterAttribute` — **Covered** (with "cannot execute" note)
10. `SwaggerSchemaFilterAttribute` — **Covered** (with "cannot execute" note)

**There is NO `SwaggerIgnore` or `SwaggerExclude` attribute in the official package.** These are custom attributes created by users with custom schema filters. Our catalog is complete for Swashbuckle annotations.

---

## 8. C# Language Feature Coverage

| Feature | In Catalog? | Gap |
|---------|-------------|-----|
| Records (positional params) | Yes | Adequate |
| `required` keyword (C# 11) | Yes | Detection method needs correction (see 2.6) |
| `init`-only properties (C# 9) | Yes | Detection method needs detail (see 2.7) |
| Primary constructors (C# 12) | Not explicitly | **No gap** — primary constructor parameters in non-record classes do NOT create properties automatically. They are just constructor parameters captured as fields. No schema impact unless explicitly assigned to properties |
| File-scoped types (`file class Foo`) | Not mentioned | **Add note:** file-scoped types are internal and cannot appear as public API parameters/responses. They will naturally be excluded since we only scan public types. No action needed |
| Tuple return types | Yes | Adequate (Nice-to-have) |
| `global using` | N/A | No impact on reflection |
| Collection expressions (C# 12) | N/A | Syntax sugar, no reflection impact |
| Extension types (C# 14 preview) | N/A | Not yet stable |

---

## 9. Description Fallback Chain

A key implementation detail worth documenting — the priority order for resolving descriptions:

### For Operations (methods):
1. `[SwaggerOperation(Summary = "...")]` → `operation.summary`
2. `[EndpointSummary("...")]` → `operation.summary`
3. XML `<summary>` → `operation.summary`
4. `[SwaggerOperation(Description = "...")]` → `operation.description`
5. `[EndpointDescription("...")]` → `operation.description`
6. XML `<remarks>` → `operation.description`

### For Parameters:
1. `[SwaggerParameter(Description = "...")]` → `parameter.description`
2. `[Description("...")]` (System.ComponentModel) → `parameter.description`
3. XML `<param name="x">` → `parameter.description`

### For Schema Properties:
1. `[SwaggerSchema(Description = "...")]` → `schema.description`
2. `[Description("...")]` (System.ComponentModel) → `schema.description`
3. XML `<summary>` on property → `schema.description`
4. `[Display(Description = "...")]` → `schema.description` (lowest priority fallback)

### For Response Descriptions:
1. `[SwaggerResponse(200, "description")]` → `responses.200.description`
2. `[ProducesResponseType(200, Description = "...")]` (.NET 10) → `responses.200.description`
3. XML `<response code="200">description</response>` → `responses.200.description`
4. HTTP status code default text (e.g., "OK", "Not Found") → fallback

---

## Sources

- [What's new in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0)
- [Include OpenAPI metadata in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)
- [OpenAPI document generation in .NET 9](https://devblogs.microsoft.com/dotnet/dotnet9-openapi/)
- [What's new in System.Text.Json in .NET 9](https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-9/)
- [Swashbuckle.AspNetCore Annotations source](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/tree/master/src/Swashbuckle.AspNetCore.Annotations)
- [SwaggerIgnore feature request (not implemented)](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/2085)
- [Roslyn Nullable Metadata spec](https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md)
- [C# 11 Required Members specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-11.0/required-members)
- [Detecting init-only properties with reflection](https://alistairevans.co.uk/2020/11/01/detecting-init-only-properties-with-reflection-in-c-9/)
- [ProducesResponseType Description property (ASP.NET Core 10)](https://github.com/dotnet/aspnetcore/issues/55656)
- [JsonUnmappedMemberHandling and OpenAPI](https://github.com/dotnet/aspnetcore/issues/57981)
- [ASP.NET Core 10 OpenAPI enhancements](https://medium.com/@sidharth.cp34/openapi-swagger-enhancements-in-asp-net-core-10-the-complete-2025-guide-2fa6da93a7fb)
- [ExcludeFromDescription not working for controllers](https://github.com/dotnet/aspnetcore/issues/57425)
- [Swashbuckle C# 11 required keyword support](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/2555)
