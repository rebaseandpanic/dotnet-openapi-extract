# DotNetOpenApiExtract Library Design

## What It Is

A CLI tool that extracts an OpenAPI specification from a compiled DLL + XML documentation file without running the application.

## How It Runs

### Option 1: CLI tool (dotnet tool)

Installation:
```bash
# Globally
dotnet tool install -g DotNetOpenApiExtract

# Or as a project tool manifest
dotnet tool install DotNetOpenApiExtract
```

Usage:
```bash
dotnet openapi-extract \
  --assembly bin/Release/net10.0/MyApi.dll \
  --output swagger.json \
  --title "MyApi" \
  --version "v1"
```

### Option 2: MSBuild target (future, not implemented yet)

A planned `DotNetOpenApiExtract.Build` package with MSBuild .targets/.props for automatic generation at build time.

### In CI/CD

```yaml
- run: dotnet build
- run: dotnet openapi-extract --assembly bin/Release/net10.0/MyApi.dll --output swagger.json
```

No database, no application startup, no fake environment variables needed.

## Package Architecture

```
DotNetOpenApiExtract (NuGet)
├── DotNetOpenApiExtract.Core        — core: reflection, XML parsing, OpenApiDocument generation
├── DotNetOpenApiExtract.Cli          — dotnet tool, CLI wrapper over Core
└── DotNetOpenApiExtract.Build        — (future) MSBuild .targets/.props
```

- **DotNetOpenApiExtract.Core** — main library containing all logic
- **DotNetOpenApiExtract.Cli** — command-line argument parsing, Core invocation, file output

## Dependencies

| Package | Dependency | Version | Purpose |
|---------|-----------|---------|---------|
| Core | `Microsoft.OpenApi` | 3.5.0 | OpenAPI document model, JSON/YAML serialization, validation. Supports OpenAPI 3.0, 3.1, 3.2 |
| Core | `Microsoft.OpenApi.YamlReader` | 3.5.0 | YAML serialization (extracted from core in v3) |
| Core | `System.Reflection.MetadataLoadContext` | 9.0.14 | Loading DLLs without code execution, reading attributes and types via `GetCustomAttributesData()` |
| Core | `System.Xml.Linq` | — | XML comment parsing (built into .NET) |
| Cli | `System.CommandLine` | 2.0.5 | CLI argument parsing (stable) |
| Build | — | — | Only a .targets file that invokes Core |

Does not depend on: ASP.NET Core, Swashbuckle, Entity Framework, Npgsql, or any infrastructure.

### Key Features of Microsoft.OpenApi v3

- Interface-based models (`IOpenApiSchema`, `IOpenApiParameter`, etc.)
- `OpenApiString`/`OpenApiObject` replaced with `System.Text.Json.Nodes.JsonNode`
- `Schema.Type` is a flags enum: `JsonSchemaType.String | JsonSchemaType.Null`
- Async API: `SerializeAsJsonAsync()`, `OpenApiDocument.LoadAsync()`
- Validation: `LoadAsync()` returns `diagnostics` with errors/warnings
- AOT-compatible (reflection removed)

### Key Features of MetadataLoadContext

- Attributes can **only** be read via `GetCustomAttributesData()` (no instantiation)
- Type comparison **only** by `FullName` string (`typeof(X).IsAssignableFrom()` is not available)
- Requires all reference assemblies: DLLs from the output folder + runtime assemblies
- Attribute inheritance is not automatic — you must walk `BaseType` manually

## CLI Parameters

```
dotnet openapi-extract [options]

Required:
  --assembly <path>       Path to the DLL (after dotnet build)

Output:
  --output <path>         Output file path (default: swagger.json)
  --format <json|yaml>    Output format (default: json)

Document info:
  --title <string>        API title (default: assembly name)
  --version <string>      Document version (default: v1)
  --description <string>  API description

Behavior:
  --xml <path>            Path to XML file (default: next to DLL, same path with .xml extension)
  --camel-case            Property names in camelCase (default: true)
  --enum-as-string        Enums as strings (default: false)
  --openapi-version <3.0|3.1|3.2>  OpenAPI version (default: 3.0)
```

## Configuration File (optional)

For settings that cannot be expressed via attributes (security schemes, server URLs):

`openapi-extract.json` next to the project:
```json
{
  "securitySchemes": {
    "Bearer": {
      "type": "http",
      "scheme": "bearer",
      "bearerFormat": "JWT"
    }
  },
  "servers": [
    { "url": "https://api.example.com", "description": "Production" }
  ]
}
```

## Execution Flow (Core)

```
1. Load the DLL via MetadataLoadContext
2. Locate the XML file (next to the DLL or via the --xml parameter)
3. Scan the assembly:
   a. Find all classes with [ApiController] / inheriting ControllerBase
   b. Exclude [NonController], [ApiExplorerSettings(IgnoreApi = true)]
   c. For each controller:
      - Read [Route], [SwaggerTag], XML <summary>
      - For each public method with [Http*]:
        - Exclude [NonAction]
        - Build the full path (class Route + method Route)
        - Determine the HTTP method
        - Extract parameters (binding, types, descriptions)
        - Extract responses ([ProducesResponseType], [SwaggerResponse])
        - Extract metadata ([SwaggerOperation], XML)
4. Recursively build JSON Schema for all DTO types
   - Handle nullable, collections, dictionaries, enums, generics
   - Apply validation attributes
   - Insert XML comments
   - Deduplicate via $ref → components/schemas
5. Assemble the OpenApiDocument
6. Serialize to JSON/YAML via Microsoft.OpenApi
7. Write to file
```
