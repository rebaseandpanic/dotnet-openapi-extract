# DotNetOpenApiExtract

CLI tool that extracts OpenAPI specifications from compiled .NET assemblies (DLL + XML docs) using static reflection — **no application startup required**.

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
| `--camel-case` | no | `true` | Use camelCase for property names |
| `--enum-as-string` | no | `false` | Serialize enums as strings |
| `--openapi-version <3.0\|3.1\|3.2>` | no | `3.0` | OpenAPI specification version |
| `--exclude-path <prefix>` | no | — | Exclude paths by prefix (repeatable) |

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

For the complete catalog of 650+ supported attributes and constructs, see [OpenAPI Attributes Catalog](docs/research/03-openapi-attributes-catalog.md).

## Limitations

| What | Why |
|------|-----|
| `IOperationFilter`, `IDocumentFilter`, `ISchemaFilter` | Arbitrary C# code executed at runtime — cannot be analyzed statically |
| Conventional routing (`MapControllerRoute`) | Routes defined in runtime code, not in attributes |
| Minimal API endpoints (`app.MapGet(...)`) | Endpoints defined in `Program.cs`, not via controllers |
| Custom `[JsonConverter]` logic | Converter behavior is arbitrary runtime code |
| `[ModelBinder]` custom binding | Runtime behavior, not interpretable statically |
| Security schemes (`AddSecurityDefinition`) | Configured programmatically in DI — use `--exclude-path` or config file |
| Runtime Swashbuckle filters | Any filter that modifies the document at runtime is invisible to static analysis |

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.OpenApi` | 3.5.0 | OpenAPI document model, JSON/YAML serialization, validation |
| `Microsoft.OpenApi.YamlReader` | 3.5.0 | YAML output support |
| `System.Reflection.MetadataLoadContext` | 10.0.5 | Load DLLs without executing code, read attributes and types |
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
