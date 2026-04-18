# Extracting Runtime Configuration Into the OpenAPI Spec

Extend the extractor to pull configuration that currently cannot be obtained from DLL metadata.

Principle: **no changes in the target project's code**. The extractor reads what the developer has already written as ordinary .NET code.

Full catalog of runtime configuration and sources — see [docs/research/05-runtime-configuration-sources.md](../research/05-runtime-configuration-sources.md).

## Approach

Two extraction channels:

1. **DLL (already works)** — `MetadataLoadContext`, reads attributes and types.
2. **Sources via Roslyn (new)** — parse `Program.cs` and related files, resolve calls through `SemanticModel`.

The second channel is optional: if no sources are nearby — the runtime-config sections stay empty, everything else works as today.

## Source Auto-detection

Strategy — walk up from the DLL through the filesystem to the project folder with `.csproj`.

### Algorithm

1. **Walk up from DLL**: `bin/Debug/net9.0/MyApi.dll` → walk up to the first directory containing `*.csproj`. In a standard layout this is the project folder (`net9.0` → `Debug` → `bin` → project).

2. **One `.csproj` in the folder** — take it, done.

3. **Multiple `.csproj` in one folder** (rare) — match by DLL name: `MyApi.dll` ⇔ `MyApi.csproj`. Also check `<AssemblyName>` in csproj if it overrides the assembly name.

4. **Found csproj, but the name doesn't match any** — error, require explicit `--source-root`.

5. **No `.csproj` found** (custom build layout) — skip, runtime-config section stays empty. The user can set `--source-root` explicitly.

### Locating the Entry Point Inside source-root

Don't hardcode the `Program.cs` name — it's a convention, not a rule.

- From DLL metadata read `Assembly.EntryPoint` → get a `MethodInfo` with `DeclaringType`.
- In Roslyn compile all `.cs` in source-root into a `Compilation`, find the method through `SemanticModel` by full type name.
- Analyze the method body and everything it calls (extension methods, `builder.Services.AddX(...)`).

Works with both `Startup.cs` style and top-level statements (which under the hood compile into `Program.<Main>$`).

### Multi-project Solutions

Not a problem. The extractor always operates in the context of **a single DLL**, a single csproj. Other projects in the solution are invisible and unneeded.

### CLI Flags

- `--source <path>` — path to a specific file with the entry point (optional, usually not needed).
- `--source-root <dir>` — project root folder (optional, override auto-detection).

## Task Execution Protocol

Required steps, a task is not closed until all are completed:

1. **Implementation** — `csharp-pro` subagent writes code + unit/integration tests. Exit: `dotnet test` green, `dotnet build` without warnings.
2. **Code review** — run the `zcoderev` skill on the changed scope.
3. **Finding triage** — the main agent separates real bugs from reviewer glitches. Style-noise / "just in case add try/catch" / false positives on accepted patterns — discarded with a note why. Without triage the fix-subagent starts "fixing" normal code.
4. **Fix subagent** (`csharp-pro`) — receives **only the filtered fix list** with instructions "don't expand scope, don't add defensive checks without a reason". After — `dotnet test` must be green.
5. **Test-quality audit** — a separate subagent checks test quality (especially integration tests):
   - Are edge cases covered (null, empty, multiple instances, missing configuration)
   - No tautology (assert repeats the implementation)
   - Asserts on the final OpenAPI document, not on intermediate objects
   - Regressions — existing expectation tests are not broken

   If something is missing — fills in and runs to green.

## Tasks

13 tasks, grouped by priority. Each task is independent — can be done in any order, not necessarily in sequence.

### High priority

#### 1. Info block and servers (CLI flags)

Pure metadata, Roslyn not needed.

**New CLI flags:**
- `--contact-name`, `--contact-email`, `--contact-url`
- `--license-name`, `--license-url`
- `--terms-of-service`
- `--server <url>` (repeatable) for `servers[]`

#### 2. Global JSON naming policy

Replace the bool flag `--camel-case` with `--naming-policy <camelCase|snake_case_lower|snake_case_upper|kebab_case_lower|kebab_case_upper|preserve>`.

**Additionally via Roslyn:** extract the policy from `ConfigureHttpJsonOptions` / `AddJsonOptions` if configured in code. The CLI flag remains as override / fallback.

#### 3. Security schemes

**From Roslyn (scheme definitions and global requirement):**
- `AddSecurityDefinition("Name", new OpenApiSecurityScheme { ... })`
- `AddAuthentication().AddJwtBearer(...)`, `AddApiKeyInHeader(...)`, etc.
- `AddSecurityRequirement(...)` for global security

**From DLL attributes (per-endpoint override, already static):**
- `[Authorize]` → apply the default scheme
- `[AllowAnonymous]` → `security: []`
- `[Authorize(AuthenticationSchemes = "...")]` → specific scheme
- `[Authorize(Policy = "...")]` → if the scheme is tied to a policy

#### 4. `UsePathBase` / global route prefix

Roslyn match on `app.UsePathBase("/api/v1")` with a literal argument. Put the prefix either into `servers[].url`, or prepend it to all paths.

A known issue even in the native `AddOpenApi()` — `UsePathBase` does not make it into the spec without extra configuration.

#### 5. `AddProblemDetails()` → default error responses

Roslyn match on the `services.AddProblemDetails()` call. If present — auto-inject default responses (400, 422, 500) with content-type `application/problem+json` and schema `ProblemDetails` (RFC 7807) for actions where these statuses are not explicitly described via `[ProducesResponseType]`.

### Medium priority

#### 6. Custom `[JsonConverter]` override

Hardcoded registry of known converters with mapping to `{type, format}`:
- `JsonStringEnumConverter` (already partially covered via `--enum-as-string`)
- `JsonStringEnumMemberConverter`
- `IsoDateTimeConverter`
- etc.

For unknown ones — keep the default by C# type (possibly with a warning).

#### 7. Global response headers

Roslyn analysis of middleware:
- Scan middleware classes and `app.Use(...)` lambdas
- Match patterns `context.Response.Headers.Append("Name", ...)` / `.Add("Name", ...)` with literal names
- Apply as shared headers to responses of all endpoints

#### 8. Global `[Consumes]` / `[Produces]` via MVC filters

Roslyn match on `builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute(...)))`. Apply as default content-types to all endpoints where per-action `[Produces]`/`[Consumes]` don't override.

#### 9. Other JSON options (beyond naming policy)

From `ConfigureHttpJsonOptions` / `AddJsonOptions` via Roslyn:
- `DefaultIgnoreCondition` (`WhenWritingNull`) — affects `required` / `nullable`
- `NumberHandling` (`AllowReadingFromString`, `WriteAsString`) — number as string
- `DictionaryKeyPolicy` — casing of dictionary keys
- `Converters.Add(new XxxConverter())` — global converters, same registry as task 6

#### 10. API versioning (`Asp.Versioning`)

Attributes `[ApiVersion("1.0")]`, `[MapToApiVersion("2.0")]`, `[ApiVersionNeutral]` — static, **Roslyn not needed**.

Emission options (choose at implementation time):
- Separate specs per version (requires multi-output mode in the CLI)
- A single spec with a version parameter in path / query

### Low priority

#### 11. `[Obsolete]` → `deprecated: true`

Standard .NET attribute. Static. **First check** — maybe we already emit it. If not — a cheap win.

#### 12. Document-level tags and externalDocs

Roslyn match on `options.AddTag(new OpenApiTag { ... })` and root-level `ExternalDocs`. Currently tags come only from `[SwaggerTag]` on controllers, without description and external docs.

#### 13. Rate limiting and response caching

**Rate limiting:**
- `[EnableRateLimiting("policy")]` — attribute, static
- Policies (`PermitLimit`, `Window`) in `AddRateLimiter(...)` — Roslyn
- Emission via `x-ratelimit-policy` extension or description of `X-RateLimit-*` headers

**Response caching:**
- `[ResponseCache]` / `[OutputCache]` — attributes, static
- Emission via description of the `Cache-Control` header in the 200 response

## Discarded Options

Explicitly **not doing** (see research for rationale):

- `IOperationFilter` / `ISchemaFilter` / `IDocumentFilter` — arbitrary C# code
- `CustomSchemaIds(type => ...)` — delegate; at most a CLI flag with two strategies
- `InvalidModelStateResponseFactory` — delegate
- FluentValidation — a separate discussion, out of scope for runtime configuration
- `MapType<T>(() => new OpenApiSchema { ... })` — covered by the hardcoded registry from task 6
- Assembly-level attributes like `[assembly: OpenApiSecurityScheme]` — **invasive**, contradicts the tool's philosophy
