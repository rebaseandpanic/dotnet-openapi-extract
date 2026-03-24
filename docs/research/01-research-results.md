# Research: Generating OpenAPI Without Running the Application

## Problem

`swagger tofile` (Swashbuckle.AspNetCore.Cli) requires a full ASP.NET Core host startup to generate an OpenAPI specification. This means a real database, Quartz, CAP, background services must be available. Without infrastructure, the application crashes and the spec cannot be generated.

## How `swagger tofile` Works Internally

### Execution Flow

1. The CLI launches a child process via `dotnet exec` with the target DLL's `.deps.json` and `.runtimeconfig.json`
2. Loads the assembly via `AssemblyLoadContext.Default.LoadFromAssemblyPath()`
3. Tries **4 strategies** to obtain an `IServiceProvider` (in priority order):

| # | Strategy | What It Does |
|---|----------|--------------|
| 1 | `SwaggerHostFactory.CreateHost()` | Looks for a `SwaggerHostFactory` class in the DLL. Full control by the developer |
| 2 | `SwaggerWebHostFactory.CreateWebHost()` | Same for legacy `IWebHost` |
| 3 | `Host.CreateDefaultBuilder().UseStartup(assemblyName)` | Classic Startup class |
| 4 | `HostFactoryResolver` + DiagnosticListener | For top-level statements — **runs Main()** |

4. Resolves `ISwaggerProvider` from DI, calls `GetSwagger()`, and serializes to JSON

### What the CLI Already Does to Lighten the Load (Strategy 4)

- Replaces the server with `NoopServer` (does not listen on a port)
- Removes `IHostLifetime`
- **Removes all `IHostedService`** except `GenericWebHostService`
- However: **the entire `Program.cs` still executes** — all `builder.Services.AddXxx()` calls, database connections, etc.

### Why the Spec Cannot Be Generated Without Running the Host

`SwaggerGenerator` depends on `IApiDescriptionGroupCollectionProvider`, which is populated by the routing/MVC infrastructure during host startup. API metadata does not exist as static information in the DLL — it is discovered dynamically when the middleware pipeline is built.

## Solution Options (Runtime)

### Option 1: `SwaggerHostFactory`

A class in the DLL with a `CreateHost()` method — the CLI calls it first, `Program.cs` is not executed. Requires duplicating controller registration and SwaggerGen configuration.

### Option 2: Guard in Program.cs

`OPENAPI_GENERATION=true` — skip all infrastructure (database, Quartz, CAP). Program.cs still runs, but without heavy dependencies.

### Option 3: Static Analyzer (Chosen Approach)

Do not run the application at all. After `dotnet build`, take the DLL + XML and use reflection/Roslyn to assemble the OpenAPI spec from attributes and XML comments.

## Static Approach Research

### No Existing Solutions

- **Nobody has built** a Roslyn source generator that produces OpenAPI from controllers (all existing ones work in the opposite direction: OpenAPI to C# code)
- **Microsoft is not heading in this direction** — their position: runtime information is necessary for an accurate spec
- `Microsoft.Extensions.ApiDescription.Server` also runs the host (with NoopServer)
- NSwag — in v14 removed the reflection-only approach, now also requires running the host

### Microsoft's Position

- Microsoft acknowledges the problem: issue [#58353](https://github.com/dotnet/aspnetcore/issues/58353) — "Enable easy stubbing of unavailable startup dependencies"
- But they are solving it not through static analysis, rather by improving build-time generation (still with host startup)
- XML doc source generator in .NET 10 — the first step toward static analysis, but only for XML comments, not the full spec

### Why a Static Analyzer Is Realistic for Typical ASP.NET Core Projects Using Attribute Routing

The API documentation is **100% declarative**:
- XML comments (`/// <summary>`)
- Swashbuckle attributes (`[SwaggerOperation]`, `[SwaggerParameter]`, `[SwaggerTag]`)
- ASP.NET Core attributes (`[HttpGet]`, `[Route]`, `[ProducesResponseType]`, `[FromBody]`)
- DTO classes with XML and types
- **No custom filters** (`IOperationFilter`, `IDocumentFilter`, `ISchemaFilter`)
- Minimal SwaggerGen configuration: `SwaggerDoc`, `EnableAnnotations`, `IncludeXmlComments`

### Two Implementation Approaches

| Approach | When It Works | Complexity | Dependencies |
|----------|--------------|------------|--------------|
| **Roslyn source generator** | At compilation (before DLL) | High — NuGet restrictions, incremental caching | Roslyn API only |
| **MSBuild task + reflection** | After `dotnet build` (on DLL + XML) | Medium — ordinary .NET code | `Microsoft.OpenApi`, reflection |

**Recommendation:** MSBuild task + reflection — significantly simpler, no source generator limitations, can use `Microsoft.OpenApi` for serialization.

## Sources

- [Swashbuckle CLI source — Program.cs](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Cli/Program.cs)
- [Swashbuckle CLI source — HostingApplication.cs](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/src/Swashbuckle.AspNetCore.Cli/HostingApplication.cs)
- [Microsoft: Generate OpenAPI documents](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi)
- [Issue #58353 — Improve build-time OpenAPI document generation](https://github.com/dotnet/aspnetcore/issues/58353)
- [Swashbuckle Issue #3082 — CLI + top-level statements](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/3082)
- [Blog: From XML doc comments to OpenAPI specs](https://blog.safia.rocks/2025/10/13/openapi-xml-generator/)
