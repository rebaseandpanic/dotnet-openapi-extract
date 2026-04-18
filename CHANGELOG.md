# Changelog

All notable changes to this project.

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
