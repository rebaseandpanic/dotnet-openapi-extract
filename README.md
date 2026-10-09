# DotNetOpenApiExtract

[![NuGet](https://img.shields.io/nuget/v/DotNetOpenApiExtract)](https://www.nuget.org/packages/DotNetOpenApiExtract)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

CLI tool that extracts OpenAPI specifications from compiled .NET assemblies (DLL + XML docs) using static reflection — **no application startup required**.

## Installation

```bash
dotnet tool install -g DotNetOpenApiExtract
```

## Usage

```bash
dotnet openapi-extract --assembly bin/Debug/net9.0/MyApi.dll --output openapi.json
```

## Why

The standard ways to get an OpenAPI file out of an ASP.NET Core app run the app's own startup code. The Swashbuckle CLI (`swagger tofile`) loads your startup assembly and builds its host. [Build-time generation in `Microsoft.AspNetCore.OpenApi`](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi#generate-openapi-documents-at-build-time) invokes your entry point with a mock server. Neither serves requests, but every service registration and configuration read in `Program.cs` executes. If that code connects to a database or message broker, calls an external API, or fails fast on a missing required environment variable, document generation fails without that infrastructure. The usual fix is to guard startup code for the generator: a `GetDocument.Insider` entry-assembly check for Microsoft's generator, or a `SwaggerHostFactory` for the Swashbuckle CLI.

DotNetOpenApiExtract reads metadata straight from the compiled DLL via `MetadataLoadContext`. It never executes any code from your assembly, so it needs no guards in your startup code — only the build output directory.

This means you can generate OpenAPI specs:
- In CI/CD without any infrastructure
- On developer machines without Docker or databases running
- From any .NET version (6, 7, 8, 9, 10) assembly

## CLI Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `--assembly <path>` | yes | — | Path to the compiled DLL |
| `--output <path>` | no | `swagger.json` | Output file path |
| `--format <json\|yaml>` | no | `json` | Output format |
| `--title <string>` | no | assembly name | API title in the info block |
| `--version <string>` | no | `v1` | API version in the info block |
| `--description <string>` | no | — | API description |
| `--xml <path>` | no | auto-detect | Path to XML documentation file. Repeatable — each `--xml` adds one more source. Sources are merged with first-added winning on key collision. Framework/SDK ref-pack XMLs are also discovered automatically and added last (lowest priority). |
| `--source <path>` | no | — | Entry-point source file (usually auto-detected) |
| `--source-root <dir>` | no | auto-detect | Project root for Roslyn analysis of `Program.cs` |
| `--naming-policy <policy>` | no | `camelCase` | `camelCase`, `snake_case_lower`, `snake_case_upper`, `kebab-case-lower`, `kebab-case-upper`, `preserve`. Used only when `Program.cs` sets no JSON options (`AddJsonOptions` / `ConfigureHttpJsonOptions`) at all; otherwise each serialization context uses its own setting or the ASP.NET Core default (camelCase) |
| `--enum-as-string` | no | `false` | Serialize enums as strings |
| `--no-enum-auto-description` | no | off | Disable the auto-glue markdown description (type summary + per-value bullet list) on enum schemas. With this flag, `schema.description` on enums is not populated (unless set by a `JsonConverter` hint). `x-enum-descriptions` and `x-enum-varnames` still emit. |
| `--no-enum-varnames` | no | off | Disable the `x-enum-varnames` extension on enum schemas |
| `--path-base-emission <mode>` | no | `prefix` | How to emit `UsePathBase`: `prefix` (prepend to paths) or `servers` (add to `servers[]`) |
| `--openapi-version <3.0\|3.1\|3.2>` | no | `3.0` | OpenAPI version the document is built, validated and serialized for. Any other value is an error (exit 2) |
| `--exclude-path <prefix>` | no | — | Exclude paths by prefix (repeatable) |
| `--contact-name <string>` | no | — | `info.contact.name` |
| `--contact-email <string>` | no | — | `info.contact.email` |
| `--contact-url <url>` | no | — | `info.contact.url` |
| `--license-name <string>` | no | — | `info.license.name` |
| `--license-url <url>` | no | — | `info.license.url` |
| `--terms-of-service <url>` | no | — | `info.termsOfService` |
| `--server <url>` | no | — | Server URL in `servers[]` (repeatable) |

### Validation flags

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `--validate` | no | off | Enable validation (52 rules, errors block CI via exit 1) |
| `--skip-rule <id>` | no | — | Disable a rule (repeatable). Unknown IDs print warning to stderr |
| `--warn-rule <id>` | no | — | Demote error → warning (repeatable) |
| `--error-rule <id>` | no | — | Promote warning → error (repeatable) |
| `--enable-rule <id>` | no | — | Enable an off-by-default rule (repeatable) |
| `--strict` | no | `false` | Treat all warnings as errors (CI-strict mode) |
| `--min-description-length <N>` | no | `5` | Minimum length for description-rule checks (global default) |
| `--rule-min-length <id>:<N>` | no | — | Per-rule override for min-description-length (repeatable). Example: `--rule-min-length enum.value-description:3` |
| `--require-response-code <method>:<code>` | no | — | Required response code for a method filter (repeatable). Activates `operation.has-required-response-codes` rule. Method: `GET`/`POST`/`PUT`/`PATCH`/`DELETE`/`HEAD`/`OPTIONS`/`TRACE`/`QUERY` or groups `safe` (GET, HEAD, OPTIONS, TRACE, QUERY), `mutating` (any other method, including non-standard ones) and `*` |
| `--exclude-validation-path <prefix>` | no | — | Path prefixes skipped by `operation.has-error-response`, `operation.success-response`, `operation.has-required-response-codes` (repeatable) |
| `--validation-report <path>` | no | — | Write JSON report to file (else printed to stdout) |

## Validation

Running `--validate` checks the extracted spec against **52 completeness rules** — 27 errors + 16 warnings always-on + 9 warnings off-by-default.

| Severity | Count | Exit code | When to use |
|----------|------:|----------:|-------------|
| Error | 27 | 1 | OpenAPI-spec MUST violations, broken codegen |
| Warning | 16 | 0 | Industry best-practice (Spectral / Redocly consensus) |
| Warning (off-by-default) | 9 | 0 (disabled) | Opt-in via `--enable-rule`. Includes: `operation.has-required-response-codes`, `operation.operation-id-pascal-case`, `schema.additional-properties-explicit`, `response.content-type-json-default`, `spec.servers-defined`, `tag.description`, `component.no-unused`, `spec.no-eval-in-markdown`, `spec.no-script-tags-in-markdown` |

**Typical CI usage:**

```bash
# Block on errors, ignore warnings
dotnet openapi-extract --assembly bin/Debug/net9.0/MyApi.dll --validate

# Strict: block on any violation including warnings
dotnet openapi-extract --assembly bin/Debug/net9.0/MyApi.dll --validate --strict

# Skip a noisy rule
dotnet openapi-extract --assembly bin/Debug/net9.0/MyApi.dll --validate --skip-rule schema.required-consistency

# Enforce 422 on mutating endpoints (your org convention)
dotnet openapi-extract --assembly bin/Debug/net9.0/MyApi.dll --validate \
  --enable-rule operation.has-required-response-codes \
  --require-response-code mutating:422

# Different min-length per rule
dotnet openapi-extract --assembly bin/Debug/net9.0/MyApi.dll --validate \
  --min-description-length 10 \
  --rule-min-length enum.value-description:3 \
  --rule-min-length operation.description:30

# JSON report for tooling/agents
dotnet openapi-extract --assembly bin/Debug/net9.0/MyApi.dll --validate --validation-report report.json
```

**Standalone validation** — validate an existing spec file without extracting:

```bash
dotnet openapi-extract validate --spec openapi.json --validation-report report.json
```

All severity/skip/enable flags apply to both modes. Run `dotnet openapi-extract --help` and `dotnet openapi-extract validate --help` for the full rule list with per-rule severities.

## What It Extracts

- Controllers (`[ApiController]`, `ControllerBase` inheritance)
- Routes (`[Route]`, `[HttpGet]`, `[HttpPost]`, etc., and `[AcceptVerbs]` with one or several methods and a named `Route`) with full template resolution; an action whose HTTP methods are not statically visible (an empty `[AcceptVerbs()]`, a custom `HttpMethodAttribute` subclass) is skipped with a warning
- Polymorphism (`[JsonPolymorphic]` / `[JsonDerivedType]`, or Swashbuckle `[SwaggerDiscriminator]` / `[SwaggerSubType]`) as unions: `oneOf` of per-type variants (with a `discriminator` for an abstract base or interface; plus a base branch for a concrete base), or `anyOf` when some derived types have no discriminator value
- Parameters (`[FromRoute]`, `[FromQuery]`, `[FromBody]`, `[FromHeader]`, `[FromForm]`) with `[ApiController]` inference
- `[JsonExtensionData]` (`IDictionary<string, object>`, `IDictionary<string, JsonElement>` and implementing types, `JsonObject`): not a property, the object gets `additionalProperties: {}`; shapes System.Text.Json rejects (other key/value types, two such properties, one bound to a constructor parameter, combined with `[JsonUnmappedMemberHandling(Disallow)]`) are extraction errors (exit 2)
- Dictionary keys (OpenAPI 3.1/3.2): `propertyNames` never narrower than what System.Text.Json writes and accepts — `Guid` keys `format: uuid`, integer keys a sign-and-digits `pattern`; string and enum keys unconstrained; a key converter unknown to the extractor leaves the keys unconstrained with a warning. `[MinLength]` / `[MaxLength]` on a dictionary give `minProperties` / `maxProperties`
- `[AllowedValues]` → `enum` (one string value: `const` for 3.1/3.2), `[DeniedValues]` → `not: {enum}`, typed by the schema; combined with the schema's own constraints (an enum type keeps its own `enum`, the allowed values are a second `allOf` element); values of another JSON type give a warning and no constraint
- Base64 data (`byte[]`, `[Base64String] string`): `format: byte` for OpenAPI 3.0, `contentEncoding: base64` without `format` for 3.1/3.2
- Responses (`[ProducesResponseType]` incl. `[ProducesResponseType<T>]`, `[SwaggerResponse]`, `[ProducesDefaultResponseType]`) on the action and on the controller (for one status code the action wins), with return type inference. `IAsyncEnumerable<T>` (also inside `Task<>` / `ValueTask<>` / `ActionResult<>`, or a type implementing it) is written by MVC as a JSON array and becomes `type: array, items: T`, with no component for the sequence type; a declared sequential media type (`application/jsonl`, `application/x-ndjson`, `application/json-seq`) gets `itemSchema: T` instead (OpenAPI 3.2; `x-oai-itemSchema` with a warning for 3.0/3.1), and `text/event-stream` gets no schema and a warning, since standard MVC cannot write server-sent events. `ServerSentEventsResult<T>` becomes `text/event-stream` with the event schema of OpenAPI 3.2 as `itemSchema` (`data` with `contentMediaType: application/json` and `contentSchema: T`; plain string data for `string` and `byte[]`). Typed results (`Ok<T>`, `Created<T>`, `NotFound`, `NoContent`, `ValidationProblem`, `Results<…>` by its variants, …) give one response per status with their body; explicit `[ProducesResponseType]` wins; a result whose status is not statically known gets a warning (its `Results<…>` siblings are still described; with no known response at all, a 200 without a schema stands in). File content (`FileResult` and derived types, `IFileHttpResult` results, `Stream`, `IFormFile`) is `type: string, format: binary` in every version, under the declared media type or `application/octet-stream`. `[SwaggerResponse(code, description, typeof(T), contentTypes…)]` media types are written as the response's content keys. The body type of a response comes from the type declared for its status code, then (for 200) `[Produces(typeof(T))]` / `[Produces<T>]`, then the return type; as in ASP.NET Core ApiExplorer, `[Produces(typeof(T))]` declares a 200 response and the return type is used only when nothing declares a response
- Schemas from DTO classes — primitives, nullable, collections, dictionaries, enums, generics, inheritance, self-referencing types
- Enum extensions: `x-enum-varnames` (always, matches `enum[]` length), `x-enum-descriptions` (when any value is documented), markdown auto-glue `description` combining type summary + per-value bullet list
- Enum value description sources: XML `<summary>` (primary) with `[Description]` attribute fallback
- Validation attributes (`[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`, etc.). `[Range]`: every overload, exclusive sides (`exclusiveMinimum: n` for 3.1/3.2, `minimum: n` + `exclusiveMinimum: true` for 3.0), string bounds of `Range(Type, string, string)` parsed in the invariant culture without losing decimal digits; only on numeric schemas
- JSON attributes (`[JsonPropertyName]`, `[JsonIgnore]`, `[JsonRequired]`)
- Swagger annotations (`[SwaggerOperation]`, `[SwaggerParameter]`, `[SwaggerTag]`, `[SwaggerSchema]`)
- XML documentation (`<summary>`, `<remarks>`, `<param>`, `<response>` on the action and on the controller)
- Nullable reference types via NRT attribute analysis
- Description fallback chains: Swagger attrs > `[Description]` > XML docs
- `[Obsolete]` → `deprecated: true` on operations, schemas, enums
- API versioning (`[ApiVersion]`, `[MapToApiVersion]`, `[ApiVersionNeutral]`) as `x-api-version` extension
- Rate limiting (`[EnableRateLimiting]`, `[DisableRateLimiting]`) as `x-rate-limit-*` extensions
- Response caching (`[ResponseCache]`, `[OutputCache]`) as `Cache-Control` header description
- Well-known `[JsonConverter]` types (`JsonStringEnumConverter`, `IsoDateTimeConverter`, `UnixDateTimeConverter`, `StringEnumConverter`, etc.) mapped via built-in registry
- Per-endpoint security from `[Authorize]` / `[AllowAnonymous]` / `[Authorize(AuthenticationSchemes=...)]`

From `Program.cs` via Roslyn (when sources are available):

- Security schemes (`AddSecurityDefinition`, `AddJwtBearer`, `AddSecurityRequirement`) — including lambda-factory form
- `UsePathBase("/prefix")` — prepended to paths or emitted as `servers[].url`
- `AddProblemDetails()` — auto-injects default 400 / 422 / 500 responses with RFC 7807 `ProblemDetails` schema
- JSON serializer options: `PropertyNamingPolicy`, `DictionaryKeyPolicy`, `DefaultIgnoreCondition`, `NumberHandling`, global `Converters.Add(...)`. Read separately per serialization context, as ASP.NET Core applies them: controller bodies follow `AddControllers().AddJsonOptions(...)` only; `ConfigureHttpJsonOptions(...)` configures minimal APIs, typed `IResult` bodies and server-sent events data and never reaches controllers. When the two differ in naming policy, ignore condition, number handling or converters, a type used in both contexts gets a second component `{Id}Http` described by the HTTP options, and one warning names those types
- Global response headers from middleware (`app.Use(...)`, `UseMiddleware<T>`) — `Response.Headers.Append/Add/TryAdd` and indexer assignments
- Global `[Consumes]` / `[Produces]` from MVC filter registrations
- Request body media types from `[Consumes]` (action, then controller, then a global filter; default `application/json`); a form body uses its `[Consumes]` media type (e.g. `application/x-www-form-urlencoded`), default `multipart/form-data`
- Document-level tags with descriptions + `externalDocs` from `c.AddTag(...)`
- FQN-prefixed types and enums (`new Microsoft.OpenApi.OpenApiSecurityScheme { Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey }`)
- In-project `const string` values via `SemanticModel.GetConstantValue`

For the complete catalog of 650+ supported attributes and constructs, see [OpenAPI Attributes Catalog](docs/research/03-openapi-attributes-catalog.md).

## Limitations

| What | Why |
|------|-----|
| `IOperationFilter`, `IDocumentFilter`, `ISchemaFilter` | Arbitrary C# code executed at runtime — cannot be analyzed statically |
| Conventional routing (`MapControllerRoute`) | Routes defined in runtime code, not in attributes |
| Minimal API endpoints (`app.MapGet(...)`) | Endpoints defined in `Program.cs`, not via controllers |
| Unknown `[JsonConverter]` types | Arbitrary runtime code — falls back to default schema. Well-known converters are recognized via built-in registry |
| `[ModelBinder]` custom binding | Runtime behavior, not interpretable statically |
| Runtime Swashbuckle filters | Any filter that modifies the document at runtime is invisible to static analysis |
| Serializing a library-built document into another OpenAPI version | The document is built for `OpenApiDocumentOptions.OpenApiVersion` (default 3.0) and is serialized only into that version; the CLI always builds, validates and serializes for one version |

### Runtime-only Program.cs patterns

When analyzing `Program.cs` via Roslyn for security schemes, response headers, global options, etc.,
the tool recognizes common patterns but cannot resolve values known only at runtime.

| Pattern | Example | Why |
|---------|---------|-----|
| `IConfiguration` values | `Type = config["Auth:Scheme"]` | Value comes from `appsettings.json` / env vars at runtime |
| Conditional registration | `if (env.IsDevelopment()) services.AddX()` | Depends on runtime environment |
| DI-factory registration | `services.AddScoped<ISchemeProvider>(sp => sp.GetRequiredService<X>())` | Resolved from runtime DI graph |
| Assembly-scan plugin discovery | `services.Scan(...).AddClasses(...)` | Types discovered by runtime reflection |
| Runtime interpolation | `Headers.Append($"X-{variable}", ...)` | Variable value known only at runtime |

These patterns are skipped silently (or with a warning to stderr). If your project relies heavily
on runtime-resolved configuration, consider a [Swashbuckle CLI tofile](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/README.md#swashbuckle-cli-tool-for-net-core)
approach which executes the assembly partially instead of analyzing it statically.

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.OpenApi` | 3.10.2 | OpenAPI document model, JSON/YAML serialization, validation |
| `Microsoft.OpenApi.YamlReader` | 3.10.2 | YAML output support |
| `System.Reflection.MetadataLoadContext` | 10.0.12 | Load DLLs without executing code, read attributes and types |
| `Microsoft.CodeAnalysis.CSharp` | 5.9.0 | Roslyn parsing of `Program.cs` for runtime-configuration extraction (`LanguageVersion.Latest`, i.e. C# 14) |
| `System.CommandLine` | 2.0.12 | CLI argument parsing |

Does **not** depend on: ASP.NET Core, Swashbuckle, Entity Framework, or any infrastructure packages.

## Requirements

- .NET 10 SDK (to run the tool)
- The target assembly's build output directory with all reference DLLs (the tool needs them to resolve types)

## Building

```bash
dotnet build
dotnet test
```

## License

MIT
