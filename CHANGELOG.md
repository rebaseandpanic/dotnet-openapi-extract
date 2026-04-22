# Changelog

All notable changes to this project.

## [0.8.0] - 2026-04-22

- [BUGFIX] `[SwaggerRequestBody]` on `[FromBody]` parameters is now respected — both the positional `[SwaggerRequestBody("...")]` and named-argument `[SwaggerRequestBody(Description = "...")]` forms populate `operation.requestBody.description`. Previously only `[SwaggerParameter]` and `[Description]` were read, so the canonical Swashbuckle attribute for body descriptions was silently ignored.
- [BUGFIX] Property descriptions declared on an open generic type (e.g. `<summary>` on `ApiResponse<T>.Success`) now propagate to every closed specialization schema (`UserDtoApiResponse`, `UserDtoListApiResponse`, etc.). C# emits XML doc entries under the open-generic key (`P:Namespace.ApiResponse\`1.Success`); the extractor now falls back to `GetGenericTypeDefinition().FullName` when a closed-type lookup misses. Applies consistently to type, property, and field documentation resolution.
- [BUGFIX] Method-parameter defaults now surface as `schema.default` in the OpenAPI spec. Both `[DefaultValue(1)]` and inline C# defaults (`int page = 1`) are written, with attribute taking precedence when both are present. Switch covers `bool`, `string`, and every BCL numeric type (`int`, `long`, `float`, `double`, `decimal`, `uint`, `short`, `ushort`, `ulong`, `sbyte`, `byte`). `OpenApiSchemaReference` values are guarded and skipped silently.
- [ARCHITECTURE] A parameter decorated with `[DefaultValue]` (even without an inline `= value`) is now inferred as `required: false`. This removes the previous contradiction of emitting `required: true` alongside `default: N`, matching OpenAPI semantics that consumers may omit any parameter carrying a default.

## [0.7.0] - 2026-04-20

- [FEATURE] Auto-glue markdown description on enum schemas — when per-value XML `<summary>` docs are present, the extractor now composes a human-readable markdown bullet list (`* \`0\` — Draft: Order created but not submitted`) and writes it to `schema.description`. Works in viewers that ignore the `x-enum-descriptions` extension (e.g. older Swagger UI). When a type-level `<summary>` is also present it is used as the intro paragraph; otherwise the bullet list stands alone. Partially-documented enums skip bullets for undocumented values. Respects `JsonConverter` hints — if a converter already set a description, auto-glue is skipped.
- [FEATURE] New `x-enum-varnames` extension on every enum schema, emitting the C# member names parallel to the `enum[]` array. Critical for SDK generators (openapi-generator, NSwag) to produce typed constants (`OrderStatus.Draft`) instead of magic numbers. Emitted unconditionally when at least one field exists — independent of whether per-value docs are present.
- [FEATURE] `[Description("...")]` attribute fallback for enum value documentation. `DocumentationResolver.ResolveEnumValueDescription` now checks `System.ComponentModel.DescriptionAttribute` on the enum field when XML `<summary>` is absent, matching the fallback chain already documented for type-level descriptions. Both auto-glue and `x-enum-descriptions` benefit from the fallback.
- [FEATURE] New CLI flag `--no-enum-auto-description` — disables the markdown auto-glue. `x-enum-descriptions` and `x-enum-varnames` still emit; `schema.description` is left unpopulated (unless set by a `JsonConverter` hint). For teams that rely on extension-aware viewers and prefer the raw extension data without markdown duplication.
- [FEATURE] New CLI flag `--no-enum-varnames` — disables the `x-enum-varnames` extension. Use when a downstream tool mis-parses the extension or when a stricter OAS subset is required.

## [0.6.0] - 2026-04-19

- [FEATURE] C# 11+ `required` modifier is now recognized as a required-property signal. Properties declared with `public required T Prop { get; set; }` emit `RequiredMemberAttribute` at compile time; the extractor reads this attribute alongside `[Required]` and `[JsonRequired]` and adds the property to the schema's `required[]` array. Applies to reference and value types alike — the `required` modifier explicitly signals intent, so unlike nullable-reference-type inference this is unconditional.

## [0.5.0] - 2026-04-19

- [FEATURE] BCL JSON container types (`JsonElement`, `JsonNode`, `JsonDocument`, `JsonObject`, `JsonArray`, `JsonValue`, `JToken`, `JObject`, `JArray`, `JValue`, `JRaw`, `ExpandoObject`) now emit as **inline** schemas with the correct shape instead of registering useless empty named schemas in `components/schemas`. Scalar/document types → `{}` (any JSON), object-like → `{type: object, additionalProperties: {}}`, array-like → `{type: array, items: {}}`. Each gets a default description ("Arbitrary JSON value/object/array") which can be overridden by a property-level `[Description]`. New public `BclJsonTypeRegistry` follows the same extensibility shape as `JsonConverterRegistry`.
- [BUGFIX] `MakeNullable` on a truly-any schema (no `Type`) no longer invents `JsonSchemaType.String` — leaves the type unset so the `{}` continues to accept any JSON value including null.
- [ARCHITECTURE] **BREAKING:** Property-level `[Description]` now unconditionally overrides any default description set by a converter hint or the BCL registry. Previously a converter-hint description (e.g. `UnixDateTimeConverter` → "Unix timestamp (seconds)") would win over an explicit `[Description]` on the property.

## [0.4.0] - 2026-04-19

- [FEATURE] Configurable required response codes per HTTP method — new rule `operation.has-required-response-codes` (off by default) + `--require-response-code <method>:<code>` CLI flag (repeatable). Method filters: `GET`/`POST`/`PUT`/`PATCH`/`DELETE`/`HEAD`/`OPTIONS`, groups `mutating` (POST/PUT/PATCH/DELETE) and `safe` (GET/HEAD/OPTIONS), or `*` for any. Enforces org conventions like "all mutating endpoints must declare 422 for business errors" via `--require-response-code mutating:422`.
- [FEATURE] New rule `operation.request-body-description` (Warning, on by default) — every operation with a request body must describe it. Respects per-rule min-description-length.
- [FEATURE] New rule `operation.operation-id-pascal-case` (off by default) — enforces PascalCase for operation IDs (`GetUser`, not `getUser` or `get_user`). Complements existing `operation.operation-id-url-safe`.
- [FEATURE] New rule `schema.additional-properties-explicit` (off by default) — for teams wanting strict API contracts, flags named component schemas where `additionalProperties` is not set explicitly. Skips schemas inside `allOf`/`anyOf`/`oneOf` composition.
- [FEATURE] New rule `response.content-type-json-default` (off by default) — for JSON-only APIs, flags responses that don't list `application/json` in content-types.
- [FEATURE] Per-rule minimum description length override — new `--rule-min-length <rule-id>:<N>` CLI flag (repeatable). Lets teams require substantive operation descriptions while accepting terse enum value descriptions: `--min-description-length 10 --rule-min-length enum.value-description:3 --rule-min-length operation.description:30`.
- [FEATURE] Enhanced `enum.value-description` rule — now respects `--min-description-length` and per-rule override (was: only checked for empty string). Empty values continue to produce the existing "missing description" message; too-short non-empty values produce a new "shorter than N characters" message.
- 52 validation rules total (up from 47): 27 errors, 16 always-on warnings, 9 off-by-default warnings. The 4 newly added off-by-default rules (R48, R50, R51, R52) target org-convention and strict-contract scenarios.

## [0.3.0] - 2026-04-19

- [FEATURE] OpenAPI spec validation engine with **47 completeness rules** — operation summary/id/description/tags, parameter/response/schema consistency, security, enum descriptions, path templating, response status coverage, and more. Run with `--validate` to check extracted specs. Errors (26 rules by default) block CI via exit 1; warnings (16 rules) are reported non-blocking; 5 rules are off-by-default and require explicit `--enable-rule`.
- [FEATURE] Standalone `validate` subcommand — lint pre-existing OpenAPI JSON/YAML files without extracting from a DLL. `dotnet openapi-extract validate --spec openapi.json`. Supports all validation flags.
- [FEATURE] Two-level severity system (Error / Warning) with per-rule overrides — `--strict` (promote all warnings to errors for CI), `--warn-rule <id>` (demote error → warning), `--error-rule <id>` (promote warning → error), `--skip-rule <id>` (disable), `--enable-rule <id>` (turn on off-by-default rule).
- [FEATURE] JSON validation report (`--validation-report <path>`) with structured violations — each violation carries severity, rule ID, JSON pointer, and source location (class / method / property name plus Roslyn-resolved file path and line number when source-root is available), for agent-driven auto-fix.
- [FEATURE] Enum value XML documentation extraction — `<summary>` on each enum value is emitted as `x-enum-descriptions` extension on the schema (array aligned with `enum` values).
- [FEATURE] OpenAPI version awareness — `spec.no-ref-siblings` rule skips on OAS 3.1/3.2 where JSON Schema Draft 2020-12 allows `$ref` siblings; `--openapi-version` flag propagates through validation.
- [UX] CLI warns to stderr when `--skip-rule` / `--warn-rule` / `--error-rule` / `--enable-rule` receives an unknown rule ID (previously silent, allowing typos to go unnoticed).
- [UX] CLI warns to stderr when explicitly-provided `--xml` file does not exist (previously silent degradation).
- [UX] `--help` output for `--validate` and `validate` subcommand lists all 47 rule IDs with their default severity — self-documenting for agents.
- [BUGFIX] Property-level violation locations now correctly point to the property line (not the enclosing class line) when the OpenAPI schema uses camelCase keys and C# uses PascalCase — case-insensitive fallback in `ViolationLocationResolver`.
- [ARCHITECTURE] **BREAKING:** `--min-description-length` default lowered from 20 to 5. CI pipelines relying on the old value should pass `--min-description-length 20` explicitly.
- [ARCHITECTURE] **BREAKING:** `--exclude-validation-path` no longer has hardcoded `/healthz`, `/ready`, `/metrics` defaults — pass them explicitly if desired. Enables use by projects with different ops-path conventions.
- [ARCHITECTURE] **BREAKING:** Validation exit code logic now based on Error-severity violations only. Pure warnings exit 0. Use `--strict` to restore old "fail on any violation" behavior.

## [0.2.0] - 2026-04-18

- [FEATURE] Extract security schemes from `Program.cs` via Roslyn — `AddSecurityDefinition`, `AddJwtBearer`, `AddSecurityRequirement` (including lambda-factory form). Per-endpoint security from `[Authorize]` / `[AllowAnonymous]` attributes.
- [FEATURE] Extract API versioning from `[ApiVersion]` / `[MapToApiVersion]` / `[ApiVersionNeutral]` attributes as `x-api-version` extension.
- [FEATURE] Extract `[Obsolete]` → `deprecated: true` on operations, schemas, and enums (class-level propagates to all actions).
- [FEATURE] Extract `UsePathBase("/prefix")` from `Program.cs` — prepended to paths or emitted as `servers[].url` via new `--path-base-emission` flag.
- [FEATURE] Extract `AddProblemDetails()` — auto-injects default 400 / 422 / 500 responses with RFC 7807 `ProblemDetails` schema where not explicitly documented.
- [FEATURE] Extract JSON serializer options from `ConfigureHttpJsonOptions` / `AddJsonOptions` — `PropertyNamingPolicy`, `DictionaryKeyPolicy`, `DefaultIgnoreCondition`, `NumberHandling`, global `Converters.Add(...)`.
- [FEATURE] Built-in `JsonConverter` registry for well-known converters — `JsonStringEnumConverter`, `IsoDateTimeConverter`, `UnixDateTimeConverter`, `StringEnumConverter`, `JsonStringEnumMemberConverter`, `JavaScriptDateTimeConverter` mapped to appropriate OpenAPI schemas.
- [FEATURE] Extract global response headers from middleware (`app.Use`, `UseMiddleware<T>`) — `Response.Headers.Append/Add/TryAdd` and indexer assignments with literal/const names.
- [FEATURE] Extract global `[Consumes]` / `[Produces]` from MVC filter registrations (`AddControllers(o => o.Filters.Add(...))`).
- [FEATURE] Extract document-level tags with descriptions + `externalDocs` from `c.AddTag(...)` and `c.SwaggerDoc(...)`.
- [FEATURE] Extract rate limiting (`[EnableRateLimiting]` / `[DisableRateLimiting]`) as `x-rate-limit-*` extensions; response caching (`[ResponseCache]` / `[OutputCache]`) as `Cache-Control` header description.
- [FEATURE] Info block and `servers[]` via new CLI flags — `--contact-name`, `--contact-email`, `--contact-url`, `--license-name`, `--license-url`, `--terms-of-service`, `--server` (repeatable).
- [ARCHITECTURE] Roslyn-based source analysis foundation — auto-detect source root from DLL path, compile C# sources, resolve entry point, match invocations and literal/constant arguments. New `--source` and `--source-root` CLI flags.
- [ARCHITECTURE] Support real-world production `Program.cs` dialects — FQN-prefixed types (`Microsoft.OpenApi.OpenApiSecurityScheme`), FQN enum values, interpolated/verbatim literal strings, in-project `const string` values via `SemanticModel.GetConstantValue`.
- [ARCHITECTURE] **BREAKING:** Replace boolean `--camel-case` flag with `--naming-policy <camelCase|snake_case_lower|snake_case_upper|kebab-case-lower|kebab-case-upper|preserve>`. Old flag removed.
- [PERFORMANCE] Pre-fetch `GetCustomAttributesData()` once per action and pass to all extractors — reduces metadata parse calls from ~14 to 2 per action.
- [PERFORMANCE] Shared `InvocationsByName` lookup in `SourceAnalysisContext` — 12+ independent tree walks reduced to a single traversal with O(1) lookup per extractor.
- [PERFORMANCE] Cache `NullableContextAttribute` resolution per type in `SchemaGenerator`; replace `ToString()` + `.Contains()` checks with structural `TypeSyntaxHelper.GetUnqualifiedTypeName` calls.
- Document runtime-only `Program.cs` patterns (IConfiguration values, conditional registration, DI factories, runtime interpolation) as known limitations in README.

## [0.1.0] - 2026-03-24

- Initial release: static OpenAPI spec extractor for .NET assemblies via `MetadataLoadContext`.
