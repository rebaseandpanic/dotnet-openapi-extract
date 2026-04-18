# Research: Which OpenAPI Configuration Is Set at Runtime

**Context.** The extractor works statically via `MetadataLoadContext` — reading only attributes and types from the compiled DLL. Part of the information needed for a full OpenAPI spec is set imperatively by developers in `Program.cs` through `builder.Services.Add*(...)` / `app.Use*(...)`. This configuration is not reflected in metadata.

**Research goal.** Collect the full list of ASP.NET Core runtime configuration that influences the OpenAPI spec. For each item — where to obtain it (DLL attribute / Roslyn from Program.cs / not possible statically), and what it gives us in the spec.

## How Existing Tools Work

An important context fact for understanding our limitations.

### `dotnet swagger tofile` (Swashbuckle CLI)

Formally "build-time", but internally loads the target assembly's `runtimeconfig.json` via `dotnet exec`, starts the DI container and pulls OpenAPI from the registered `ISwaggerProvider`. Not static analysis — **partial application startup**. That's why it requires `IApiDescriptionGroupCollectionProvider` in DI, and issues arise if ApiExplorer is not registered.

Sources:
- [Swashbuckle CLI docs](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/docs/configure-and-customize-cli.md)
- [Swashbuckle CLI Program.cs](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Cli/Program.cs)
- [Swashbuckle tofile with .NET 8 (Alex Sikilinda)](https://sikilinda.com/posts/dotnet-swagger-tofile-dotnet-8/)

### NSwag.MSBuild

Same thing — post-build target via `$(NSwagExe)`, internally `aspNetCoreToOpenApi` loads the assembly and pulls ApiExplorer. Known issues with missing ApiExplorer in DI.

Sources:
- [NSwag.MSBuild wiki](https://github.com/RicoSuter/NSwag/wiki/NSwag.MSBuild)
- [ApiExplorer registration issue (NSwag#3737)](https://github.com/RicoSuter/NSwag/issues/3737)

### Conclusion

Purely static OpenAPI extraction from a .NET assembly in the ecosystem **does not exist**. All existing tools cheat by loading and partially starting the assembly. Our USP is to stay truly static, without startup. For runtime configuration this means: either parse `Program.cs` via Roslyn (as source text), or don't support it at all.

## Runtime Configuration Catalog

Order — from the most important for the baseline spec to advanced features.

### 1. Security schemes

**What it is.** OpenAPI `components.securitySchemes` + `security` (global) + per-endpoint `security` overrides.

**How it's set in ASP.NET Core:**
```csharp
builder.Services.AddAuthentication().AddJwtBearer(...);
options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { ... });
options.AddSecurityRequirement(new OpenApiSecurityRequirement { ... });
```

**Where to obtain:**
- Scheme definitions and global requirement — **Roslyn from Program.cs** (`AddSecurityDefinition` / `AddJwtBearer` / `AddSecurityRequirement` calls)
- Per-endpoint override — **DLL attributes**: `[Authorize]`, `[AllowAnonymous]`, `[Authorize(AuthenticationSchemes="...")]`

Sources:
- [OAuth2 and Security (Swashbuckle DeepWiki)](https://deepwiki.com/domaindrivendev/Swashbuckle.AspNetCore/6.5-oauth2-and-security)
- [Securing a .NET API (JWT, API Key)](https://ziedrebhi.medium.com/securing-a-net-api-c-api-key-basic-authentication-and-jwt-369181eee672)

### 2. JSON serializer options

**What it is.** Serialization settings that affect schema shape and field casing.

**How it's set:**
```csharp
builder.Services.ConfigureHttpJsonOptions(o => {
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

Or via MVC:
```csharp
builder.Services.AddControllers().AddJsonOptions(o => { /* same */ });
```

`PropertyNamingPolicy` options: `CamelCase`, `SnakeCaseLower`, `SnakeCaseUpper`, `KebabCaseLower`, `KebabCaseUpper`, `null` (preserve).

**Where to obtain.** Only Roslyn from `Program.cs`. Part of it can be duplicated via CLI flags as an override.

Sources:
- [JSON Serialization options (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/configure-options)
- [System.Text.Json support in Swashbuckle](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/docs/configure-and-customize-swaggergen.md)

### 3. Custom `[JsonConverter]` type override

**What it is.** Attribute `[JsonConverter(typeof(X))]` or global `options.Converters.Add(new X())` changes the JSON type of a property/type. The actual converter behavior is arbitrary C# code.

**How it's set:**
```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Status { Active, Disabled }

// or globally
options.Converters.Add(new JsonStringEnumConverter());
```

**Where to obtain.**
- Attribute — static (DLL).
- Converter behavior — **hardcoded registry** of known ones: `JsonStringEnumConverter` → `string` enum, `JsonStringEnumMemberConverter`, `IsoDateTimeConverter` → `string` format `date-time`, etc.
- For unknown ones — default by C# type + warning.

Sources:
- [Custom Data Types in ASP.NET Core Web APIs (Magnus Montin)](https://blog.magnusmontin.net/2020/04/03/custom-data-types-in-asp-net-core-web-apis/)
- [Swashbuckle MapType issue #309](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/309)

### 4. Info block and servers

**What it is.** `info.title`, `info.version`, `info.description`, `info.contact`, `info.license`, `info.termsOfService`, `servers[]`.

**How it's set:**
```csharp
options.SwaggerDoc("v1", new OpenApiInfo {
    Title = "Users API",
    Contact = new OpenApiContact { Name = "...", Email = "..." },
    License = new OpenApiLicense { Name = "MIT", Url = new Uri("...") },
    TermsOfService = new Uri("...")
});
options.AddServer(new OpenApiServer { Url = "https://api.example.com" });
```

**Where to obtain.** Pure metadata → **CLI flags**. Optionally — Roslyn for the `SwaggerDoc(...)` call.

Sources:
- [Swashbuckle SwaggerGen Configuration](https://deepwiki.com/domaindrivendev/Swashbuckle.AspNetCore/3.1-swaggergen-configuration)

### 5. Global response headers

**What it is.** Headers added by middleware to all responses: `X-Request-Id`, `X-RateLimit-*`, `X-Correlation-Id`.

**How it's set:**
```csharp
app.Use(async (context, next) => {
    context.Response.Headers.Append("X-Request-Id", Guid.NewGuid().ToString());
    await next();
});
```

**Where to obtain.** Roslyn — scan middleware classes and `app.Use(...)` lambdas for `context.Response.Headers.Append/Add("Name", ...)` patterns with literal names.

Sources:
- [Exploring Communication of Rate Limits in ASP.NET Core (Tomasz Pęczek)](https://www.tpeczek.com/2022/07/exploring-communication-of-rate-limits.html)

### 6. Global `[Consumes]` / `[Produces]` via MVC filters

**What it is.** Default content types for all service endpoints.

**How it's set:**
```csharp
builder.Services.AddControllers(o => {
    o.Filters.Add(new ProducesAttribute("application/json"));
    o.Filters.Add(new ConsumesAttribute("application/json"));
});
```

**Where to obtain.** Roslyn — match `MvcOptions.Filters.Add(new ProducesAttribute(...))`. Per-action attributes are already static.

Sources:
- [Global ConsumesAttribute filter issue (Swashbuckle#1130)](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/1130)
- [Content-Type discussion Swashbuckle#1691](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/1691)

### 7. `UsePathBase` / global route prefix

**What it is.** A prefix for all service paths: `/api/v1` → instead of `/users` the spec should have `/api/v1/users` (or the prefix goes into `servers[].url`).

**How it's set:**
```csharp
app.UsePathBase("/api/v1");
```

**Where to obtain.** Roslyn — match `app.UsePathBase(string)` with a literal argument.

Known issue: even the native `AddOpenApi()` in ASP.NET Core doesn't handle this.

Sources:
- [UsePathBase not in OpenApi (aspnetcore#61486)](https://github.com/dotnet/aspnetcore/issues/61486)
- [Understanding PathBase (Andrew Lock)](https://andrewlock.net/understanding-pathbase-in-aspnetcore/)

### 8. `AddProblemDetails()` → default error responses

**What it is.** RFC 7807. If `services.AddProblemDetails()` is registered in Program.cs, all 4xx/5xx responses without explicit documentation should return `application/problem+json` with the standard `ProblemDetails` schema.

**How it's set:**
```csharp
builder.Services.AddProblemDetails();
```

**Where to obtain.** Roslyn — match the `AddProblemDetails()` call. If present — auto-inject default responses (400, 422, 500) with `ProblemDetails` schema.

Sources:
- [Handle errors in ASP.NET Core APIs (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0)
- [ProblemDetails Error Handling RFC 7807 (dapiq)](https://dapiq.com/insights/problemdetails-error-handling-rfc-7807-aspnet)

### 9. API versioning (`Asp.Versioning`)

**What it is.** Attributes `[ApiVersion("1.0")]`, `[MapToApiVersion("2.0")]`, `[ApiVersionNeutral]` — define endpoint versions.

**Where to obtain.** **DLL attributes**, static. Roslyn not needed. Task — emit either separate specs per version, or merge with a version parameter in path.

Sources:
- [Asp.Versioning Swashbuckle integration](https://github.com/dotnet/aspnet-api-versioning/wiki/Swashbuckle-Integration)

### 10. `[Obsolete]` → `deprecated: true`

**What it is.** Standard .NET attribute `[Obsolete]` on an action → OpenAPI `operation.deprecated: true`.

**Where to obtain.** Static. Check whether the current implementation emits it — if not, a cheap win.

### 11. Document-level tags and externalDocs

**What it is.** Tags with descriptions at the document level (not just names from `[SwaggerTag]`), and root-level `externalDocs`.

**How it's set:**
```csharp
options.AddTag(new OpenApiTag {
    Name = "Users",
    Description = "User management endpoints",
    ExternalDocs = new OpenApiExternalDocs { Url = new Uri("...") }
});
```

**Where to obtain.** Roslyn for literal values.

Sources:
- [OpenAPI Specification 3.1.0](https://swagger.io/specification/)

### 12. Rate limiting (`AddRateLimiter`, `[EnableRateLimiting]`)

**What it is.** ASP.NET Core 7+ built-in rate limiter. The `[EnableRateLimiting("policy")]` attribute on an action + policies in `AddRateLimiter(...)`.

**Where to obtain.**
- Attributes — static.
- Policies (`PermitLimit`, `Window`) — Roslyn.
- OpenAPI doesn't natively describe rate limits → we emit via `x-ratelimit-policy` extension or by describing `X-RateLimit-Limit`/`X-RateLimit-Remaining`/`X-RateLimit-Reset` headers.

Sources:
- [Rate limiting middleware (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [RateLimit HTTP headers draft (tpeczek)](https://www.tpeczek.com/2022/07/exploring-communication-of-rate-limits.html)

### 13. Response caching (`[ResponseCache]` / `[OutputCache]`)

**What it is.** Attributes that define response caching. Can be reflected in the spec as a `Cache-Control` header in the 200 response description.

**Where to obtain.** Static (attributes).

## What We Discard

### `IOperationFilter` / `ISchemaFilter` / `IDocumentFilter`

Arbitrary C# code, executed at runtime by Swashbuckle. Cannot be statically interpreted by definition. Already documented in README as a limitation.

### `CustomSchemaIds(type => ...)`

Takes `Func<Type, string>` — an arbitrary lambda. At most — a CLI flag `--schema-id-policy <full|short>` with two predefined strategies.

### `InvalidModelStateResponseFactory`

Delegate that builds a custom response for an invalid ModelState. Not readable.

### FluentValidation

Validation rules live in separate `AbstractValidator<T>` classes, not as attributes on the DTO. Matching DTO → validator is possible via Roslyn by inferring the type argument, but that's **a separate discussion** — beyond the scope of "simple" runtime configuration.

### `MapType<T>(() => new OpenApiSchema { ... })`

Same as custom `JsonConverter` — arbitrary C# code in a lambda. Roslyn can theoretically read the literal object initializer, but too narrow a case — covered by the hardcoded registry from item 3.

## Summary

| # | What | Extraction method | Priority |
|---|-----|-------------------|-----------|
| 1 | Security schemes | Roslyn + DLL attributes | high |
| 2 | JSON serializer options | Roslyn + CLI flags | high |
| 3 | Custom `[JsonConverter]` | Hardcoded registry | medium |
| 4 | Info + servers | CLI flags | high |
| 5 | Global response headers | Roslyn middleware | medium |
| 6 | Global `[Consumes]`/`[Produces]` | Roslyn MvcOptions | medium |
| 7 | `UsePathBase` | Roslyn | high |
| 8 | `AddProblemDetails` default responses | Roslyn | high |
| 9 | API versioning | DLL attributes | medium |
| 10 | `[Obsolete]` → deprecated | DLL attributes | low (check) |
| 11 | Document-level tags + externalDocs | Roslyn | low |
| 12 | Rate limiting | Roslyn + DLL attributes | low |
| 13 | Response caching | DLL attributes | low |

All items are implemented non-invasively — the target project's developer changes nothing in their code.
