# Analyzer Scope: What We Support and What We Don't

## Supported (covers 100% of typical project use cases)

### Controllers and Routing

| Feature | Source | How We Extract |
|---------|--------|----------------|
| Controller discovery | `[ApiController]`, inheriting from `ControllerBase` | Reflection on DLL |
| Base route | `[Route("internal/api/v1/connections")]` on class | Attribute → string |
| HTTP methods and paths | `[HttpGet("path")]`, `[HttpPost]`, `[HttpPut]`, `[HttpDelete]`, `[HttpPatch]` | Attribute → method + template |
| Route composition | class `[Route]` + method `[HttpGet("sub")]` → full path | Template concatenation |
| Tokens `[controller]`, `[action]` | Replaced with class/method name by convention | String replacement |

### Parameters

| Feature | Source | How We Extract |
|---------|--------|----------------|
| Path parameters | `[FromRoute]` + parameter type | Attribute + type reflection |
| Query parameters | `[FromQuery]` + type | Attribute + type reflection |
| Body parameters | `[FromBody]` + DTO type | Attribute + recursive type traversal |
| Header parameters | `[FromHeader]` | Attribute + type reflection |
| Parameter descriptions | `[SwaggerParameter("description")]` | Attribute → string |
| Descriptions from XML | `<param name="id">description</param>` | XML file parsing |

### Responses

| Feature | Source | How We Extract |
|---------|--------|----------------|
| HTTP statuses + types | `[ProducesResponseType(typeof(ApiResponse<T>), 200)]` | Attribute → status + type |
| Multiple statuses | Multiple `[ProducesResponseType]` | All method attributes |
| Response descriptions from XML | `<response code="200">description</response>` | XML file parsing |

### Documentation (Swagger Annotations)

| Feature | Source | How We Extract |
|---------|--------|----------------|
| Operation Summary/Description | `[SwaggerOperation(Summary = "...", Description = "...")]` | Attribute → strings |
| OperationId | `[SwaggerOperation(OperationId = "...")]` | Attribute → string |
| Operation Tags | `[SwaggerOperation(Tags = new[] {"..."})]` | Attribute → array |
| Tag group description | `[SwaggerTag("description")]` on controller | Attribute → string |

### Documentation (XML Comments)

| Feature | Source | How We Extract |
|---------|--------|----------------|
| Method summary | `/// <summary>` → `<member name="M:Namespace.Class.Method">` | XML file |
| Remarks | `/// <remarks>` | XML file |
| Parameter descriptions | `/// <param name="x">` | XML file |
| Response descriptions | `/// <response code="200">` | XML file |
| DTO class summary | `/// <summary>` on class | XML file |
| DTO property summaries | `/// <summary>` on each property | XML file |

### Schemas (DTO → JSON Schema)

| Feature | Source | How We Extract |
|---------|--------|----------------|
| Primitive types | `string`, `int`, `long`, `bool`, `double`, `decimal` | Reflection → OpenAPI type |
| Guid, DateTime, DateTimeOffset | .NET types | Reflection → `format: uuid` / `date-time` |
| Nullable types | `int?`, `string?` | Reflection `Nullable<T>` / NRT |
| Enums | `enum Status { Active, Banned }` | Reflection → `enum: [...]` |
| Collections | `List<T>`, `IEnumerable<T>`, arrays | Reflection → `type: array, items: ...` |
| Generic wrappers | `ApiResponse<T>` with `Success`, `Data`, `Error` | Reflection generic type arguments |
| Nested objects | DTO with DTO properties | Recursive traversal |
| Inheritance | Base classes | Reflection `BaseType` |
| Validation attributes | `[Required]`, `[StringLength]`, `[Range]` | Attributes → schema constraints |

### Document Metadata

| Feature | Source | How We Extract |
|---------|--------|----------------|
| Title, Version | CLI parameters or config file | Command-line arguments |

---

## Not Supported

### Swashbuckle Runtime Filters

| Feature | Why Not Supported |
|---------|-------------------|
| `IOperationFilter` | Arbitrary C# code executed at runtime. Cannot be analyzed statically |
| `IDocumentFilter` | Same — modifies the entire document programmatically |
| `ISchemaFilter` | Same — modifies JSON Schema programmatically |

**Impact on typical projects: none** — custom filters are not commonly used in attribute-routing projects.

### Conventional Routing

| Feature | Why Not Supported |
|---------|-------------------|
| `MapControllerRoute("default", "{controller}/{action}/{id?}")` | Routes are defined in runtime code, not in attributes |

**Impact on typical projects: none** — standard use cases rely on attribute routing (`[Route]`, `[HttpGet]`).

### Minimal API Endpoints

| Feature | Why Not Supported |
|---------|-------------------|
| `app.MapGet("/api/items", handler)` | Endpoints are defined in Program.cs, not via controllers |
| Fluent metadata (`.WithName()`, `.Produces()`) | Runtime configuration |

**Impact on typical projects: none** — standard use cases rely on controllers.

### Dynamic Configuration

| Feature | Why Not Supported |
|---------|-------------------|
| Middleware that modifies routes (`UsePathBase`, URL rewriting) | Runtime behavior |
| Custom model binders | Runtime behavior |
| Auth scheme details (`AddAuthentication().AddJwtBearer(...)`) | Configured in DI |
| `[JsonDerivedType]` polymorphism | Requires runtime resolution |

**Impact on typical projects: minimal** — auth schemes won't appear in the spec automatically, but they can be specified via configuration.

---

## Summary

```
Coverage of typical projects:  ~100%
Coverage of an arbitrary ASP.NET Core project:  80-95%
  (depends on usage of filters, minimal API, conventional routing)
```

Everything used for documentation — attributes and XML comments — is available statically via reflection on the compiled DLL + XML file parsing.
