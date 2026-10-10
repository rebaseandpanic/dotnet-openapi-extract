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
| `--title <string>` | no | `SwaggerDoc` `Title`, then `[AssemblyTitle]`, `[AssemblyProduct]`, assembly name | API title in the info block |
| `--version <string>` | no | `SwaggerDoc` `Version`, then `v1` | API version in the info block |
| `--description <string>` | no | `SwaggerDoc` `Description`, then `[AssemblyDescription]` | API description |
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
| `--license-identifier <spdx>` | no | — | `info.license.identifier` (OpenAPI 3.1+; `x-oai-license-identifier` with a warning for 3.0); needs `--license-name`, excludes `--license-url` |
| `--summary <text>` | no | — | `info.summary` (OpenAPI 3.1+; omitted with a warning for 3.0); wins over `OpenApiInfo.Summary` in Program.cs |
| `--terms-of-service <url>` | no | — | `info.termsOfService` |
| `--server <url>` | no | — | Server URL in `servers[]` (repeatable) |
| `--server-name <name>` | no | — | Name of the k-th `--server` (`servers[].name`, OpenAPI 3.2; `x-oai-name` with a warning before); repeatable, none or one per `--server`, non-empty and unique |
| `--self-url <uri>` | no | — | `$self` (OpenAPI 3.2; `x-oai-$self` with a warning before); a URI reference without a fragment |
| `--json-schema-dialect <uri>` | no | — | `jsonSchemaDialect`: `https://spec.openapis.org/oas/3.1/dialect/base` for 3.1, `https://spec.openapis.org/oas/3.2/dialect/2025-09-17` for 3.2; for 3.0 either, omitted with a warning |

### Validation flags

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `--validate` | no | off | Enable validation (errors block CI via exit 1; `--help` prints the rule counts) |
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

Running `--validate` checks the extracted spec against the completeness rules; `openapi-extract --help` prints how many run as errors, how many as warnings and how many are off by default (the counts come from the rule registry).

Exit codes: `0` success, `1` validation errors, `2` any other error — a command line that does not parse (an unknown option, a missing value or required option) included.

| Severity | Exit code | When to use |
|----------|----------:|-------------|
| Error | 1 | OpenAPI-spec MUST violations, broken codegen |
| Warning | 0 | Industry best-practice (Spectral / Redocly consensus) |
| Off by default (error or warning when enabled) | 0 (disabled) | Opt-in via `--enable-rule`. Includes: `operation.has-required-response-codes`, `operation.operation-id-pascal-case`, `schema.additional-properties-explicit`, `response.content-type-json-default`, `spec.servers-defined`, `tag.description`, `component.no-unused`, `spec.no-eval-in-markdown`, `spec.no-script-tags-in-markdown` |

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

Rules run for the version of the document: the `--openapi-version` of the build, or the `openapi:` field of a standalone file. Some levels depend on it — `response.description` is an error for 3.0/3.1 and a warning for 3.2, where a response description is optional (`--strict` promotes it like any other warning). `response.schema-when-body` accepts `itemSchema` (3.2) and `x-oai-itemSchema` (3.0/3.1) as the schema of a streaming response, and a `text/event-stream` media type without a schema is not a violation.

Rules for the document structure of newer versions: `spec.license-identifier-or-url` (3.1+: a license has an identifier or a url, not both), `spec.paths-or-webhooks-or-components` (3.0: `paths` is required; 3.1/3.2: at least one of `paths`, `webhooks`, `components` — an empty object counts as present, a missing one does not), `tag.parent-defined` and `tag.no-parent-cycle` (3.2: a tag's `parent` names a declared tag, and parents never form a cycle), `spec.server-names-unique` (3.2: top-level server names are unique), `discriminator.default-mapping-when-optional` (3.2: a discriminator whose property an instance may lack — required-ness followed through `allOf`, `oneOf`, `anyOf` and `$ref` — has a `defaultMapping`; the tool's own polymorphism output always passes it). A rule for one version does not run for another.

`schema.property-constraints` checks the schema against the validation attributes the way the generator applies them: lengths by shape (string, array, dictionary) and at least as tight as the attribute, `[Range]` for every constructor with inclusive and exclusive bounds on their own keywords (on the numeric branch when number handling makes the property an `anyOf`). `component.no-unused` (off by default) counts references through `itemSchema`, `contentSchema`, `propertyNames`, compositions and discriminator mappings.

`schema.property-format` asks for the format the generator writes, by the same priority: `[SwaggerSchema(Format)]`, then `[EmailAddress]` / `[Url]` / `[Phone]`, then `[DataType]` (`DateTime` → `date-time`, `Date` → `date`, `Time` → `time`, `Duration` → `duration`, none on a `TimeSpan`, `EmailAddress` → `email`, `Password` → `password`, `Url` / `ImageUrl` → `uri`, `PhoneNumber` → `phone`, `Upload` → `binary`; other members give no format), then the CLR type (`Guid` → `uuid`, `DateTime` / `DateTimeOffset` → `date-time`, `DateOnly` → `date`, `TimeOnly` → `time`). A source without a format requires none.

`schema.array-items` (an array schema without `items`) is a code-generation policy, not an OpenAPI requirement: JSON Schema 2020-12, used by OpenAPI 3.1 and 3.2, allows an array without `items`. It stays an error by default because SDK generators turn such an array into a list of untyped values; skip it with `--skip-rule schema.array-items` when that is intended.

Standalone `validate` takes the version from the file's `openapi:` field and walks operations under `paths` and `webhooks`; a 3.1/3.2 file with only `components` or only `webhooks` is a valid document.

All severity/skip/enable flags apply to both modes. Run `dotnet openapi-extract --help` and `dotnet openapi-extract validate --help` for the full rule list with per-rule severities.

## What It Extracts

- Controllers (`[ApiController]`, `ControllerBase` inheritance)
- Routes (`[Route]`, `[HttpGet]`, `[HttpPost]`, etc., and `[AcceptVerbs]` with one or several methods and a named `Route`) with full template resolution; an action whose HTTP methods are not statically visible (an empty `[AcceptVerbs()]`, a custom `HttpMethodAttribute` subclass) is skipped with a warning
- Polymorphism (`[JsonPolymorphic]` / `[JsonDerivedType]`, or Swashbuckle `[SwaggerDiscriminator]` / `[SwaggerSubType]`) as unions: `oneOf` of per-type variants (with a `discriminator` for an abstract base or interface; plus a base branch for a concrete base), or `anyOf` when some derived types have no discriminator value
- Parameters (`[FromRoute]`, `[FromQuery]`, `[FromBody]`, `[FromHeader]`, `[FromForm]`) with `[ApiController]` inference
- `[JsonExtensionData]` (`IDictionary<string, object>`, `IDictionary<string, JsonElement>` and implementing types, `JsonObject`): not a property, the object gets `additionalProperties: {}`; shapes System.Text.Json rejects (other key/value types, two such properties, one bound to a constructor parameter, combined with `[JsonUnmappedMemberHandling(Disallow)]`) are extraction errors (exit 2)
- Dictionary keys (OpenAPI 3.1/3.2): `propertyNames` never narrower than what System.Text.Json writes and accepts — `Guid` keys `format: uuid`, integer keys a sign-and-digits `pattern`; string and enum keys unconstrained; a key converter unknown to the extractor leaves the keys unconstrained with a warning. `[MinLength]` / `[MaxLength]` on a dictionary give `minProperties` / `maxProperties`
- `[AllowedValues]` → `enum` (one string value: `const` for 3.1/3.2), `[DeniedValues]` → `not: {enum}`, typed by the schema; combined with the schema's own constraints (an enum type keeps its own `enum`, the allowed values are a second `allOf` element); values of another JSON type give a warning and no constraint
- `[JsonNumberHandling]` (property, then type, then the global option): the schema is the union of what System.Text.Json writes and reads — `anyOf` [number, numeric string] for the string flags, plus `enum: ["NaN", "Infinity", "-Infinity"]` for `float` / `double` / `Half`; ranges stay on the numeric branch, nullable covers the whole union
- Base64 data (`byte[]`, `[Base64String] string`): `format: byte` for OpenAPI 3.0, `contentEncoding: base64` without `format` for 3.1/3.2
- Responses (`[ProducesResponseType]` incl. `[ProducesResponseType<T>]`, `[SwaggerResponse]`, `[ProducesDefaultResponseType]`) on the action and on the controller (for one status code the action wins), with return type inference. `IAsyncEnumerable<T>` (also inside `Task<>` / `ValueTask<>` / `ActionResult<>`, or a type implementing it) is written by MVC as a JSON array and becomes `type: array, items: T`, with no component for the sequence type; a declared sequential media type (`application/jsonl`, `application/x-ndjson`, `application/json-seq`) gets `itemSchema: T` instead (OpenAPI 3.2; `x-oai-itemSchema` with a warning for 3.0/3.1), and `text/event-stream` gets no schema and a warning, since standard MVC cannot write server-sent events. `ServerSentEventsResult<T>` becomes `text/event-stream` with the event schema of OpenAPI 3.2 as `itemSchema` (`data` with `contentMediaType: application/json` and `contentSchema: T`; plain string data for `string` and `byte[]`). Typed results (`Ok<T>`, `Created<T>`, `NotFound`, `NoContent`, `ValidationProblem`, `Results<…>` by its variants, …) give one response per status with their body; explicit `[ProducesResponseType]` wins; a result whose status is not statically known gets a warning (its `Results<…>` siblings are still described; with no known response at all, a 200 without a schema stands in). File content (`FileResult` and derived types, `IFileHttpResult` results, `Stream`, `IFormFile`) is `type: string, format: binary` in every version, under the declared media type or `application/octet-stream`. `[SwaggerResponse(code, description, typeof(T), contentTypes…)]` media types are written as the response's content keys. The body type of a response comes from the type declared for its status code, then (for 200) `[Produces(typeof(T))]` / `[Produces<T>]`, then the return type; as in ASP.NET Core ApiExplorer, `[Produces(typeof(T))]` declares a 200 response and the return type is used only when nothing declares a response
- Schemas from DTO classes — primitives, nullable, collections, dictionaries, enums, generics, inheritance, self-referencing types
- String enums list the names on the wire, by the converter in force (property, then global, then type — System.Text.Json's order): System.Text.Json's `JsonStringEnumConverter` reads `[JsonStringEnumMemberName]`, Newtonsoft's `StringEnumConverter` reads `[EnumMember(Value)]`; defaults and allowed values use the same names. Path, query, header and form parameters, which model binding reads by member name, list the member names
- Enum extensions: `x-enum-varnames` (always, matches `enum[]` length), `x-enum-descriptions` (when any value is documented), markdown auto-glue `description` combining type summary + per-value bullet list
- Enum value description sources: XML `<summary>` (primary) with `[Description]` attribute fallback
- Validation attributes (`[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`, `[Length]`, etc.) on DTO properties and on action parameters (the same keywords and forms). `[Length(min, max)]` gives `minLength` / `maxLength` on strings, `minItems` / `maxItems` on collections and `minProperties` / `maxProperties` on dictionaries. `[Range]`: every overload, exclusive sides (`exclusiveMinimum: n` for 3.1/3.2, `minimum: n` + `exclusiveMinimum: true` for 3.0), string bounds of `Range(Type, string, string)` parsed in the invariant culture without losing decimal digits; only on numeric schemas
- JSON attributes (`[JsonPropertyName]`, `[JsonIgnore]`, `[JsonRequired]`)
- Swagger annotations (`[SwaggerOperation]`, `[SwaggerParameter]`, `[SwaggerTag]`, `[SwaggerSchema]`)
- `[SwaggerParameter(Required = …)]` wins over the inferred `required` (a path parameter stays required, with a warning); `[SwaggerTag(description, externalDocsUrl)]` gives the controller's tag `externalDocs.url`
- `format` of a property, one winner: `[SwaggerSchema(Format)]`, then `[EmailAddress]` / `[Url]` / `[Phone]`, then `[DataType]` (`DateTime` → `date-time`, `Date` → `date`, `Time` → `time`, `Duration` → `duration`, none on a `TimeSpan`, `EmailAddress` → `email`, `Password` → `password`, `Url` / `ImageUrl` → `uri`, `PhoneNumber` → `phone`, `Upload` → `binary`; other members give none), then the format of the type, which a source without a format leaves in place
- `readOnly` from `[SwaggerSchema(ReadOnly)]`, else `[ReadOnly]` (an explicit `ReadOnly = false` wins over `[ReadOnly(true)]`), `writeOnly` from `[SwaggerSchema(WriteOnly)]`, `title` from `[SwaggerSchema(Title)]`; a property both read-only and write-only is an extraction error (exit 2)
- XML documentation (`<summary>`, `<remarks>`, `<param>`, `<response>` on the action and on the controller)
- XML `<param name="x" example="…">` on action parameters, parsed by the parameter's schema: `parameter.example` for path, query and header parameters, the media type `example` of the request body for `[FromBody]`, and for `[FromForm]` fields one object keyed by their form field names; an example that does not parse is not written and gives a warning. XML `<param>` is found by the C# name also when `Name =` renames the parameter
- XML `<example>` on DTO properties (inherited ones and positional-record parameters via `<param name="X" example="…">` included) and on DTO types: parsed by the schema (numbers, booleans, objects and arrays as JSON, strings as they are, `null` only for a nullable schema) and written as `example` for OpenAPI 3.0, `examples: [v]` for 3.1/3.2; on the `allOf` wrapper of a reference property and on the numeric branch of a number read from strings. An example that does not parse is not written and gives a warning; of several `<example>` elements the first is used, with a warning
- Nullable reference types via NRT attribute analysis
- Description fallback chains: Swagger attrs > `[Description]` > XML docs; for properties XML `<summary>` > `[SwaggerSchema(Description)]` > `[Description]` > `[Display(Description)]`, as Swashbuckle with `EnableAnnotations()` and `IncludeXmlComments()` at run time
- `[Obsolete]` → `deprecated: true` on operations, schemas, enums
- API versioning (`[ApiVersion]`, `[MapToApiVersion]`, `[ApiVersionNeutral]`) as `x-api-version` extension
- Rate limiting (`[EnableRateLimiting]`, `[DisableRateLimiting]`) as `x-rate-limit-*` extensions
- Response caching (`[ResponseCache]`, `[OutputCache]`) as `Cache-Control` header description
- Well-known `[JsonConverter]` types (`JsonStringEnumConverter`, `IsoDateTimeConverter`, `UnixDateTimeConverter`, `StringEnumConverter`, etc.) mapped via built-in registry
- Per-endpoint security from `[Authorize]` / `[AllowAnonymous]` / `[Authorize(AuthenticationSchemes=...)]`; `[Authorize(Roles=...)]` roles in the requirement values for OpenAPI 3.1/3.2 (OR inside an attribute, AND across attributes)

From `Program.cs` via Roslyn (when sources are available):

Only the source files compiled into the assembly are read. Their list comes from the portable PDB
(`<assembly>.pdb` next to the DLL, or embedded), matched to the source root by path tail, so absolute
paths of another machine and `PathMap` paths (`/_/…`) work. A file under the source root that is not
in the assembly — `Program.Old.cs`, a file excluded with `<Compile Remove>`, a file added after the
build — is ignored. Without a PDB that matches the source root, every `.cs` file under it is read; if
several of them can be the entry point (top-level statements, or `Main` of the entry-point type), the
`Program.cs` nearest to the source root is read with the warning `source.entry-point-ambiguous`
naming the candidates, and when there is no such single file none is read.

- Security schemes (`AddSecurityDefinition`, `AddJwtBearer`, `AddSecurityRequirement`) — including lambda-factory form; OAuth2 flows (implicit, password, client credentials, authorization code, device authorization) with their URLs and scopes, the OpenID Connect URL, and the OpenAPI 3.2 `oauth2MetadataUrl` / `deprecated` (extensions with a warning before 3.2). A literal OAuth2 or OpenID Connect declaration without flows or URL is an extraction error; one built from variables is omitted with a warning; `mutualTLS` for 3.1/3.2 (removed for 3.0 with a warning naming the changed requirements)
- `UsePathBase("/prefix")` — prepended to paths or emitted as `servers[].url`
- `AddProblemDetails()` — auto-injects default 400 / 422 / 500 responses with RFC 7807 `ProblemDetails` schema
- JSON serializer options: `PropertyNamingPolicy`, `DictionaryKeyPolicy`, `DefaultIgnoreCondition`, `NumberHandling`, global `Converters.Add(...)` (with the naming policy of a string-enum converter). Read separately per serialization context, as ASP.NET Core applies them: controller bodies follow `AddControllers().AddJsonOptions(...)` only; `ConfigureHttpJsonOptions(...)` configures minimal APIs, typed `IResult` bodies and server-sent events data and never reaches controllers. When the two differ in naming policy, ignore condition, number handling or converters, a type used in both contexts gets a second component `{Id}Http` described by the HTTP options, and one warning names those types
- Global response headers from middleware (`app.Use(...)`, `UseMiddleware<T>`) — `Response.Headers.Append/Add/TryAdd` and indexer assignments
- Global `[Consumes]` / `[Produces]` from MVC filter registrations
- Request body media types from `[Consumes]` (action, then controller, then a global filter; default `application/json`); a form body uses its `[Consumes]` media type (e.g. `application/x-www-form-urlencoded`), default `multipart/form-data`
- Document-level tags with descriptions, `externalDocs` and the OpenAPI 3.2 `summary` / `parent` / `kind` from `c.AddTag(...)`; `info.title`, `description`, `version`, `summary`, `termsOfService`, the contact (`name`, `email`, `url`) and the license (`name`, `url`, `identifier`) from the `OpenApiInfo` of `SwaggerDoc(...)` / `AddOpenApi(...)`. Each field comes from the first source that sets it: the CLI flag (option), then `SwaggerDoc` / `AddOpenApi`, then the assembly attributes (`[AssemblyTitle]` / `[AssemblyProduct]`, `[AssemblyDescription]`, `[AssemblyCompany]` for the contact name), then the assembly file name (title only) or `v1` (version)
- FQN-prefixed types and enums (`new Microsoft.OpenApi.OpenApiSecurityScheme { Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey }`), and target-typed creations (`c.SwaggerDoc("v1", new() { License = new() { Name = "MIT", Url = new("…") } })`, `AddSecurityDefinition("x", new() { … })`, `Reference = new() { … }`, `[new("x", document)] = []`). Document metadata that is not an object creation (`c.SwaggerDoc("v1", info)`, `License = license`, `AddTag(tag)`) is reported with the warning `document.metadata-not-static`; a security definition built that way is omitted with `security.scheme-not-static`
- Strings from literals (plain, verbatim, raw `"""…"""`), literal concatenation, in-project `const string` members of any class, `nameof(...)` and interpolation of constants (`SemanticModel.GetConstantValue`)
- Both Swashbuckle API generations: `Microsoft.OpenApi.Models.*` with `Reference = new OpenApiReference { … }` (Swashbuckle ≤ 9) and `Microsoft.OpenApi.*` with `new OpenApiSecuritySchemeReference("x", document)` as a collection or index initializer key, in an expression or block lambda (Swashbuckle 10)

For every OpenAPI field the tool emits, could emit or never emits — its source in C#, its form in OpenAPI 3.0, 3.1 and 3.2, its warning and its validation rule — see the [OpenAPI Field Catalog](docs/specs/openapi-field-catalog.md).

## Limitations

| What | Why |
|------|-----|
| `IOperationFilter`, `IDocumentFilter`, `ISchemaFilter` | Arbitrary C# code executed at runtime — cannot be analyzed statically |
| Conventional routing (`MapControllerRoute`) | Routes defined in runtime code, not in attributes |
| Minimal API endpoints (`app.MapGet(...)`) | Endpoints defined in `Program.cs`, not via controllers |
| Unknown `[JsonConverter]` types | Arbitrary runtime code — falls back to default schema. Well-known converters are recognized via built-in registry |
| `[ModelBinder]` custom binding | Runtime behavior, not interpretable statically |
| Runtime Swashbuckle filters | Any filter that modifies the document at runtime is invisible to static analysis |
| Range of a number read from a string | With `[JsonNumberHandling]` / `NumberHandling` a number is `anyOf: [number, numeric string]`; `[Range]`, `[AllowedValues]` and the format constrain the numeric branch only. The string branch keeps the grammar System.Text.Json reads (`"+1"`, `"01"`, `"1.5e3"`), which a pattern cannot bound by value, so an out-of-range number sent as a string passes the schema and is rejected by the server |
| Dictionary keys (`propertyNames`, OpenAPI 3.1/3.2) | An approximation that is never narrower than System.Text.Json: integer keys are a digit pattern without the type's range (`"300"` passes for a `byte` key), unsigned keys also accept a leading `+` that System.Text.Json 10 rejects, and string and enum keys are not constrained. OpenAPI 3.0 has no `propertyNames` and leaves keys unconstrained |
| Serializing a library-built document into another OpenAPI version | The document is built for `OpenApiDocumentOptions.OpenApiVersion` (default 3.0) and is serialized only into that version; the CLI always builds, validates and serializes for one version |

### What the tool cannot see in Program.cs

`Program.cs` is read as source code and never run. Configuration that exists only when the
application runs is therefore not seen:

| Pattern | Example | Why |
|---------|---------|-----|
| `IConfiguration` and environment values | `Name = builder.Configuration["Auth:Header"]`, `Summary = Environment.GetEnvironmentVariable("API_SUMMARY")` | Value comes from `appsettings.json` / env vars at runtime |
| Values computed at startup | `Description = BuildDescription()`, `SwaggerDoc("v1", info)` with `info` built elsewhere | The code that computes them is not run |
| Configuration outside the entry point | `builder.Services.AddApiSwagger()` with `AddSwaggerGen(c => …)` in `SwaggerSetup.cs`, `Startup.ConfigureServices`, `IConfigureOptions<SwaggerGenOptions>` | Only the entry point (`Program.cs`) is read; the warning `document.configuration-not-in-entry-point` says so |
| Swashbuckle filters and `AddOpenApi` transformers | `c.OperationFilter<SecurityRequirementsFilter>()`, `o.AddDocumentTransformer<T>()` | Arbitrary code that edits the document at runtime |
| Conditional registration | `if (env.IsDevelopment()) services.AddX()` | Depends on runtime environment |
| DI-factory registration | `services.AddScoped<ISchemeProvider>(sp => sp.GetRequiredService<X>())` | Resolved from runtime DI graph |
| Assembly-scan plugin discovery | `services.Scan(...).AddClasses(...)` | Types discovered by runtime reflection |
| Runtime interpolation | `Headers.Append($"X-{variable}", ...)` | Variable value known only at runtime |

Values written as literals (plain, verbatim, raw `"""…"""`), concatenations of literals, `const`
members of any class of the project, `nameof(...)` and interpolations of constants are read. A value
the tool recognizes but cannot compute is never dropped silently: it gives a warning with its
`file:line` (`ExtractionDiagnostic.SourceLocation`) and, where one exists, the CLI flag that sets it:

| Warning code | When | Result |
|---|---|---|
| `document.metadata-not-static` | `SwaggerDoc` info, its `Title`, `Description`, `Version`, `Summary`, `TermsOfService`, `Contact` (`Name`, `Email`, `Url`), `License` (`Name`, `Url`, `Identifier`) or `ExternalDocs` is not a creation / literal / constant | the field is not taken from `SwaggerDoc` (the next source applies); the subjects end with the flags that set it (`--title`, `--description`, `--version`, `--summary`, `--terms-of-service`, `--contact-*`, `--license-*`) |
| `document.configuration-not-in-entry-point` | the assembly references `Swashbuckle.AspNetCore.SwaggerGen` or `Microsoft.AspNetCore.OpenApi`, and the entry point has no `SwaggerDoc` / `AddSecurityDefinition` / `AddSecurityRequirement` / `AddTag`, and calls `AddSwaggerGen` / `AddOpenApi` not at all, or without arguments next to an options class registered for it (`IConfigureOptions<SwaggerGenOptions>`, `ConfigureOptions<ConfigureSwaggerOptions>()`). A bare `AddSwaggerGen()` alone is a project without configuration and gives no warning | the configuration in the other file is not read; set the metadata with the flags |
| `security.scheme-not-static` | `Type`, `In`, `Name`, `Scheme` of a security scheme, or a value an OAuth2 / OpenID Connect scheme needs | the scheme is omitted, with the requirements that name it |
| `security.scheme-field-not-static` | `Description` or `BearerFormat` of a security scheme | the scheme is written without the field |
| `security.requirement-not-static` | `AddSecurityRequirement(requirement)` or a lambda returning something that is not an object creation | the requirement is not written |
| `security.requirements-may-come-from-filter` | a document / operation filter or transformer is registered and no `AddSecurityRequirement` is read | nothing changes; the filter may set requirements the document lacks |

A flag always wins over `SwaggerDoc` for its own field. Set document metadata the tool cannot see with `--title`, `--version`, `--description`, `--summary`, `--contact-name`,
`--contact-email`, `--contact-url`, `--license-name`, `--license-url`, `--license-identifier`,
`--terms-of-service` and `--server`. If your project relies heavily on runtime-resolved configuration,
consider a [Swashbuckle CLI tofile](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/README.md#swashbuckle-cli-tool-for-net-core)
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
