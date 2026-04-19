# Wave 6 — OpenAPI spec completeness validation

Add a validation subsystem that checks extracted OpenAPI specs for documentation completeness — useful for CI and code review.

## Motivation

After Waves 1-5 the extractor produces rich OpenAPI documents, but nothing enforces that developers actually fill in descriptions, tags, error responses, enum docs, etc. An agent running the extractor against a project needs machine-readable feedback like "operation `GET /users` is missing description — go fix `UsersController.GetUsers`".

## Design principles

- **CLI-driven, no config files.** Agents read `--help`, pass flags. No "create file / delete file" ceremony.
- **One severity.** Everything is `error` → exit 1. No warnings. If a rule is too strict for the project — `--skip-rule <id>`.
- **Two entry points:**
  - Inline: `--validate` on the existing extract command
  - Standalone: new `validate` subcommand that reads an existing `openapi.json` / `openapi.yaml`
- **Source location:** logical coordinates (class / method / property name) always, plus file+line when source-root is available.

## Tasks

### Task 6.1 — Enum value descriptions (prerequisite)

Prerequisite for rules #18 and #20 in the main task. Can run in parallel with 6.2 since the output format is agreed upfront.

**Goal:** extract XML doc `<summary>` from each enum value, emit them as `x-enum-descriptions: string[]` extension on the schema. Index matches `enum[]` array index.

**Contract** (fixed upfront so 6.2 can code against it):

```yaml
Status:
  type: string
  enum: [Active, Disabled, Pending]
  x-enum-descriptions:
    - "User can login and use all features."
    - "Account disabled by admin."
    - "Registration not yet confirmed."
```

Array length must match `enum.Length`. If a value has no XML doc → empty string `""` at that index.

**Files to touch:**
- `src/DotNetOpenApiExtract.Core/Documentation/XmlDocParser.cs` — parse `<summary>` for enum member IDs like `F:MyNamespace.Status.Active`
- `src/DotNetOpenApiExtract.Core/Documentation/DocumentationResolver.cs` — new method `ResolveEnumValueDescription(Type enumType, string memberName)`
- `src/DotNetOpenApiExtract.Core/Schema/SchemaGenerator.cs` — in `GenerateEnumSchema`, build `x-enum-descriptions` from doc resolver results, attach via `schema.Extensions["x-enum-descriptions"]`

**Tests:** 3-4 unit + 1 integration on SampleApi (add XML docs to an existing enum).

### Task 6.2 — Validation engine + 24 rules + two entry points

**All work goes in `src/DotNetOpenApiExtract.Core/Validation/` (new namespace).**

**Core abstractions:**
```csharp
public sealed record ValidationViolation(
    string RuleId,
    string JsonPointer,      // "#/paths/~1users/get"
    ViolationLocation? Location,
    string Message);

public sealed record ViolationLocation(
    string? ClassName,
    string? MethodName,
    string? PropertyName,
    string? File,
    int? Line);

public interface IValidationRule
{
    string Id { get; }
    IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context);
}

public sealed class ValidationContext
{
    public int MinDescriptionLength { get; init; } = 20;
    public IReadOnlyList<string> ExcludedPathPrefixes { get; init; } = ["/healthz", "/ready", "/metrics"];
    public IReadOnlyDictionary<string, (Type ClrType, MemberInfo? Member)> TypeBindings { get; init; }
      // Map schema-id / path+method → source ClrType + method/property — for ViolationLocation
}
```

**24 rules in `src/DotNetOpenApiExtract.Core/Validation/Rules/`:**

| Rule ID | Scope | What it checks |
|---------|-------|----------------|
| `operation.summary` | Operation | `operation.Summary` is non-empty |
| `operation.operation-id` | Operation | `operation.OperationId` is non-empty |
| `operation.description` | Operation | `operation.Description` is non-empty and >= `MinDescriptionLength` |
| `operation.tags` | Operation | `operation.Tags.Count >= 1` |
| `operation.has-error-response` | Operation | Has at least one 4xx or 5xx response; path not in `ExcludedPathPrefixes` |
| `operation.security` | Operation | If `[Authorize]` on action/controller AND no `[AllowAnonymous]` AND no global security → `operation.Security` must be non-null |
| `operation.deprecated-has-note` | Operation | If `operation.Deprecated == true` → `description` must mention "replacement" / "use instead" / "removed" (regex) |
| `parameter.description` | Parameter | `parameter.Description` non-empty |
| `parameter.schema-type` | Parameter | `parameter.Schema.Type` is not null/empty |
| `parameter.optional-has-default` | Parameter | If `Required == false` AND schema is nullable → `schema.Default` should be set (warn only for value types — skip for ref types) |
| `response.description` | Response | Non-empty |
| `response.schema-when-body` | Response | If `response.Content.Count > 0` → each media-type has `Schema` non-null |
| `schema.description` | Schema | `schema.Description` non-empty (only for `components.schemas` objects, not primitives/inline) |
| `schema.property-description` | Schema | Each property in object schema has non-empty `Description` |
| `schema.property-format` | Schema | For properties semantically expecting format (detected from CLR type or validation attrs): `Guid`→uuid, `DateTime`→date-time, `DateOnly`→date, `TimeOnly`→time, `[EmailAddress]`→email, `[Url]`→uri, `[DataType(DataType.Date)]`→date — schema must have matching `Format` |
| `schema.required-consistency` | Schema | For object schema: non-nullable properties should be in `required[]` array |
| `schema.property-constraints` | Schema | If property has `[StringLength]`/`[Range]`/`[RegularExpression]` attribute → corresponding `maxLength`/`minimum`/`maximum`/`pattern` present |
| `schema.enum-filled` | Schema | If property type is enum → `enum[]` array populated (not empty) |
| `enum.type-description` | Schema | Enum schema has `Description` non-empty and mentions all values (string contains each name, warn if not — but for now strict check) |
| `enum.value-description` | Schema | Enum schema has `x-enum-descriptions` extension, each entry non-empty (requires Task 6.1) |
| `security.scheme-defined` | Document | If any operation has `Security[]` → referenced schemes exist in `components.securitySchemes` |
| `security.scheme-description` | Document | Each scheme in `components.securitySchemes` has `Description` non-empty |
| `spec.info-title` | Document | `document.Info.Title` non-empty |
| `spec.info-description` | Document | `document.Info.Description` non-empty and >= `MinDescriptionLength` |

**CLI flags:**

```
--validate                              Turn on validation (all rules, all errors)
--skip-rule <rule-id>                   Disable a rule (repeatable)
--min-description-length <N>            Min chars for description rules (default: 20)
--exclude-validation-path <prefix>      Paths skipped for validation (repeatable, default: /healthz, /ready, /metrics)
--validation-report <path>              Write JSON report to file (else printed to stdout at end)
```

Plus new **subcommand** `validate`:
```
dotnet openapi-extract validate --spec openapi.json [--skip-rule ...] [--validation-report ...] [--min-description-length N] [--exclude-validation-path ...]
```

Reads the spec file (json or yaml), runs the same rules, same report format. Doesn't need `--assembly`.

**Help output:** the `--validate` flag description includes a compact list of rule IDs (can span multiple lines). Example:
```
  --validate     Enable OpenAPI spec validation. All 24 rules enabled as errors.
                 Rule IDs: operation.summary, operation.description, ..., spec.info-description.
                 Disable individual rules via --skip-rule <id>.
```

**Report JSON format:**

```json
{
  "spec": "swagger.json",
  "violations": [
    {
      "rule": "operation.description",
      "jsonPointer": "#/paths/~1api~1users/get",
      "location": {
        "className": "UsersController",
        "methodName": "GetUsers",
        "propertyName": null,
        "file": "src/Controllers/UsersController.cs",
        "line": 45
      },
      "message": "Operation description is missing or shorter than 20 characters (actual: 0)."
    }
  ],
  "summary": {
    "total": 1,
    "byRule": { "operation.description": 1 }
  }
}
```

If `--validation-report` is not set — JSON to stdout at end of normal output.

**Exit codes:**
- `0` — no violations
- `1` — violations found
- `2` — utility error (file not found, invalid config, parse error)

**Source location resolution (for inline `--validate` mode):**
- Extract phase already has `ControllerInfo.Type` (CLR Type) and `ActionInfo.Method` (MethodInfo). Thread them through into `ValidationContext.TypeBindings`.
- For `className`/`methodName` — always available from CLR metadata.
- For `file`/`line` — only when `sourceContext.IsAvailable`. Resolve:
  - Class → find `ClassDeclarationSyntax` in `compilation.SyntaxTrees` where `Identifier.Text == className` → `syntaxTree.FilePath` + node span start line.
  - Method → `syntaxTree.FilePath` + `MethodDeclarationSyntax` span start line.

**Source location resolution (for standalone `validate` mode):**
- No DLL, no Roslyn. Only `className`/`propertyName` available if we extract from schema IDs and `x-*` extensions. Leave `file`/`line` null.

**Tests:**
- One unit test per rule (~24 tests)
- Integration test: run `--validate` against SampleApi, expect specific violations (we know SampleApi is intentionally incomplete in places)
- Integration test: standalone `validate` against a crafted invalid openapi.json
- CLI tests: `--skip-rule`, `--min-description-length`, `--exclude-validation-path`, `--validation-report` file output
- Exit code tests: 0/1/2

## Execution protocol

Same 5-step as prior waves (see `docs/tasks/runtime-config-extraction.md`):
1. Implementation (2 parallel subagents for 6.1 + 6.2)
2. `zcoderev` combined on the diff
3. Finding triage (main agent)
4. Fix subagent with filtered list
5. Test quality audit subagent
