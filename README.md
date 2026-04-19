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

Standard OpenAPI generation tools (`swagger tofile`, `Microsoft.AspNetCore.OpenApi`) require the application to fully start — which means a real database, message queues, external APIs, background services, and all environment variables must be available. Without infrastructure, the app crashes and the spec is never generated.

DotNetOpenApiExtract solves this by reading metadata directly from the compiled DLL via `MetadataLoadContext`. It never executes any code from your assembly. All it needs is the build output directory.

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
| `--xml <path>` | no | auto-detect | Path to XML documentation file |
| `--source <path>` | no | — | Entry-point source file (usually auto-detected) |
| `--source-root <dir>` | no | auto-detect | Project root for Roslyn analysis of `Program.cs` |
| `--naming-policy <policy>` | no | `camelCase` | `camelCase`, `snake_case_lower`, `snake_case_upper`, `kebab-case-lower`, `kebab-case-upper`, `preserve` |
| `--enum-as-string` | no | `false` | Serialize enums as strings |
| `--path-base-emission <mode>` | no | `prefix` | How to emit `UsePathBase`: `prefix` (prepend to paths) or `servers` (add to `servers[]`) |
| `--openapi-version <3.0\|3.1\|3.2>` | no | `3.0` | OpenAPI specification version |
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
| `--require-response-code <method>:<code>` | no | — | Required response code for a method filter (repeatable). Activates `operation.has-required-response-codes` rule. Method: `GET`/`POST`/`PUT`/`PATCH`/`DELETE`/`HEAD`/`OPTIONS` or groups `mutating`/`safe`/`*` |
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
- Routes (`[Route]`, `[HttpGet]`, `[HttpPost]`, etc.) with full template resolution
- Parameters (`[FromRoute]`, `[FromQuery]`, `[FromBody]`, `[FromHeader]`, `[FromForm]`) with `[ApiController]` inference
- Responses (`[ProducesResponseType]`, `[SwaggerResponse]`) with return type inference
- Schemas from DTO classes — primitives, nullable, collections, dictionaries, enums, generics, inheritance, self-referencing types
- Validation attributes (`[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`, etc.)
- JSON attributes (`[JsonPropertyName]`, `[JsonIgnore]`, `[JsonRequired]`)
- Swagger annotations (`[SwaggerOperation]`, `[SwaggerParameter]`, `[SwaggerTag]`, `[SwaggerSchema]`)
- XML documentation (`<summary>`, `<remarks>`, `<param>`, `<response>`)
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
- JSON serializer options (`ConfigureHttpJsonOptions` / `AddJsonOptions`): `PropertyNamingPolicy`, `DictionaryKeyPolicy`, `DefaultIgnoreCondition`, `NumberHandling`, global `Converters.Add(...)`
- Global response headers from middleware (`app.Use(...)`, `UseMiddleware<T>`) — `Response.Headers.Append/Add/TryAdd` and indexer assignments
- Global `[Consumes]` / `[Produces]` from MVC filter registrations
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
| `Microsoft.OpenApi` | 3.5.0 | OpenAPI document model, JSON/YAML serialization, validation |
| `Microsoft.OpenApi.YamlReader` | 3.5.0 | YAML output support |
| `System.Reflection.MetadataLoadContext` | 10.0.5 | Load DLLs without executing code, read attributes and types |
| `Microsoft.CodeAnalysis.CSharp` | 4.11.0 | Roslyn parsing of `Program.cs` for runtime-configuration extraction |
| `System.CommandLine` | 2.0.5 | CLI argument parsing |

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
