# OpenAPI Field Catalog

Normative catalog of every OpenAPI field that `openapi-extract` emits, could emit, or deliberately never emits, for OpenAPI 3.0, 3.1 and 3.2.

## Purpose

This document is the single source of truth for one question: **which C# construct produces which OpenAPI field, in which OpenAPI version, and what happens to that field when the output targets an older version.** It covers:

- every fixed field of every object in OpenAPI 3.2.0, including fields that also exist in 3.0 and 3.1;
- fields that existed only in 3.0 (`nullable`, boolean `exclusiveMinimum`/`exclusiveMaximum`, `allowEmptyValue` on Header);
- the JSON Schema 2020-12 keywords available in the 3.1/3.2 Schema Object, and the 3.0 Schema Object subset;
- specification extensions (`x-*`) written by the tool or by the serialization library.

It replaces `docs/research/03-openapi-attributes-catalog.md`. That older file describes what an ideal extractor *could* read. This catalog describes what this extractor *does* read, checked against the code under `src/`.

## Context

The tool reads a compiled assembly through `MetadataLoadContext`, XML documentation files, and (optionally) the project's C# sources through Roslyn. It never runs user code. The document model is Microsoft.OpenApi 3.10.2.

The target version (`--openapi-version`, default `3.0`; in Core `OpenApiDocumentOptions.OpenApiVersion` and `SchemaOptions.OpenApiVersion`) is a build parameter: the document builder and the schema generator receive it, the CLI builds, validates and serializes for that one version, and an unsupported value is a configuration error. Version-dependent forms are being added field by field (rows marked "stage 1"); a field without one is built the same for every version and written by the library in the target format. Version-dependent today:

- nullable values are expressed as a `null` type, which the library writes as `nullable: true` in 3.0;
- the `spec.no-ref-siblings` rule runs only for 3.0;
- a request body on GET, HEAD or DELETE is reported with a warning for a 3.0 target (§10).

The library writes `openapi: 3.0.4`, `3.1.2` or `3.2.0` respectively.

In stage 1 the target version is a **build parameter**: the document and every schema are built for the target version, not rewritten after the build. The version contract covers every public Core surface — the document build options and methods, and direct schema generation through the public schema generator and its options — with the same default (3.0), the same configuration error for an unknown version, and the same diagnostics channel (for direct schema generation, diagnostics are deduplicated per generator instance). A build with validation where the build options and the validation context name **different** explicit versions is a configuration error; a version left unset in the validation context is taken from the build options. A document built for one version is serialized only into that version; serializing a Core document into another version is not supported.

When a field is set in the model but the target version has no such field, the library does one of three things:

- writes it as an extension (`x-oai-*`, `x-oas-*`, `x-jsonschema-*`);
- drops it silently;
- throws during serialization.

The "3.0 output" and "3.1 output" columns record which of these happens for each field. They were checked by serializing a document with every model property set through Microsoft.OpenApi 3.10.2, in all three versions.

## How to read the tables

Each section covers one OpenAPI object. Each row is one field path. Rows for a field with several C# sources, or several behaviours, are split where the behaviour differs.

### Columns

| Column | Meaning |
|---|---|
| Field | OpenAPI field path, e.g. `info.summary`, `schema.exclusiveMinimum`. |
| Since | First OpenAPI version with the field; `removed in X` where applicable. |
| Source in code | The user-code construct the extractor reads: attribute, XML doc tag, type shape, serializer option, `Program.cs` call via Roslyn, CLI flag. `—` if no source exists. |
| 3.0 / 3.1 / 3.2 output | `supported` rows: what the tool emits today (verified). `stage 1` rows (including `bug: …; stage 1`): the **target** output the stage must produce, per target version, including degradation. Current wrong behaviour of a `stage 1` row is recorded only in its Status cell. Other statuses: what is emitted today, or `never`. |
| Warning | Diagnostic emitted when the field is lost or degraded, or when the source cannot be mapped. `—` if none. |
| --validate rule | Existing rule id (from `src/DotNetOpenApiExtract.Core/Validation/Rules`), or a planned rule marked `planned:`. `—` if none. |
| Status | See legend below. |
| Tracking | `stage-1`, `stage-2`, `ref-siblings`, or `—`. |

### Status legend

| Status | Meaning |
|---|---|
| `supported` | Works today. Verified in code, and by running the CLI on a fixture where noted. |
| `stage 1` | In the current stage scope: standard .NET / ASP.NET Core / STJ / DataAnnotations / XML-doc / Roslyn sources the extractor does not read yet, or reads wrongly, plus CLI flags for document fields that have no code source. |
| `stage 2` | Needs the tool's own annotations. No standard source exists. |
| `separate` | Planned as a separate piece of work outside both stages. |
| `deferred: <reason>` | Consciously postponed; not part of any stage yet. |
| `not planned: <reason>` | Deliberately not produced. |
| `bug: <current behaviour>; stage 1` | The field, or its source, is read wrongly today; the text before the semicolon is the current (wrong) behaviour. The output columns of the row give the target behaviour. |

### Abbreviations used in cells

| Abbreviation | Meaning |
|---|---|
| `=` | Same as the 3.1 column. |
| `dropped (lib)` | Microsoft.OpenApi omits the field silently for this version. |
| `never` | The tool never generates the field. |
| `DW` | Stage-1 downlevel diagnostic (see "Extraction diagnostics contract" below). It goes to the public diagnostics channel in Core, and the CLI prints it to stderr. It never changes the exit code. One DW is emitted for the **topmost** lost or moved node; nodes inside that subtree get no DW of their own. |
| `MS.OpenApi 3.x` | A `Program.cs` initializer that compiles only against Microsoft.OpenApi 3.x (for example Microsoft.AspNetCore.OpenApi 11). It does not compile against Microsoft.OpenApi 2.x, which Swashbuckle 10 uses. |
| `extraction error` | A statically provable wrong user annotation. Core raises an extraction error naming the type and member; the CLI exits with code 2. |
| `configuration error` | Invalid or conflicting configuration (version, document metadata, dialect, `$self`, server names). Core raises a configuration exception; the CLI exits with code 2. |

### General rules that apply to every row

- **Default target version is 3.0.** An unknown version value (CLI flag or Core options) is a configuration error, never a silent 3.0. Fixing a defect may change 3.0 output. Such changes are released as fixes in a minor version, and the CHANGELOG lists them in a separate "changes in 3.0 output" section.
- **A field that exists only in a higher version, when the output targets a lower version,** is handled in exactly one of five ways, given by its row:
  - **lossless rewrite** into the lower-version equivalent (nullable, `const` → one-element `enum`, exclusive bounds) — no warning;
  - **moved into an extension** (`x-oai-*` / `x-oas-*`) the way the library writes it — with a DW;
  - **omitted** — with a DW;
  - **never generated** (`in: querystring`, `style: cookie`);
  - **form chosen by version** — the generator builds the target-version form directly and the higher-version field never enters the model, so there is no DW: `contentEncoding` versus `format: byte`, `propertyNames` in 3.0, role lists in 3.0. A warning is emitted only where the row says so (role lists in 3.0 do get one).
- **Special cases.** `mutualTLS` in 3.0 is omitted by the tool, together with the requirements that reference it, because the library throws; the DW names the scheme and the change to the auth contract. `$ref` siblings are separate work (`ref-siblings`); in this stage the `allOf` wrapper is kept in every version.
- **No field present in the built document is lost during serialization without a warning**, and serialization never throws for any target version.
- **Statically provable wrong annotations** are extraction errors (exit 2). Normative cases: invalid `[JsonExtensionData]` and its conflict with type-level `Disallow`; an invalid `[Range]`; a derived-type property with the discriminator's JSON name; a literal OAuth2/OIDC declaration without its required data; a final schema that is both `readOnly` and `writeOnly`. **Cases that cannot be proven statically** (custom converter, custom formatter, a value that is not a literal or constant) are warnings.
- **Wire contract reference**: what System.Text.Json (STJ) 10 really writes and accepts for the fixture types, not the current extractor output. **Form reference**: the text of the OpenAPI specification of the target version.
- **CLI flags override** values read from `Program.cs`, field by field.
- **`--strict`** applies only to `--validate` violations, never to extraction diagnostics.

---

## 1. OpenAPI Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `openapi` | 3.0 | CLI `--openapi-version 3.0\|3.1\|3.2` (default `3.0`; any other value → exit 2); Core `OpenApiDocumentOptions.OpenApiVersion` and `SchemaOptions.OpenApiVersion` (default 3.0; any other value → configuration error; the document is serialized only into the version it was built for) | `3.0.4` | `3.1.2` | `3.2.0` | — | — | supported | — |
| `$self` | 3.2 | CLI `--self-url <uri>`; the value must be a valid URI reference without a fragment, otherwise configuration error. Also available through Core options. | `x-oai-$self` | `x-oai-$self` | `$self` | DW (3.0, 3.1) | — | stage 1 | stage-1 |
| `info` | 3.0 | see §2 | written | = | = | — | `spec.info-title`, `spec.info-version`, `spec.info-description` | supported | — |
| `jsonSchemaDialect` | 3.1 | CLI `--json-schema-dialect <uri>`; only the dialect the generator and validator really support (the OAS base dialect of the target version; for a 3.0 target, where the field does not exist, the 3.1 and 3.2 base dialects are accepted and the field is omitted with a warning); any other value is a configuration error. Without the flag the field is not written (the specification default applies). | omitted | written | written | DW (3.0) | — | stage 1 | stage-1 |
| `servers` | 3.0 | see §5 | written | = | = | — | `spec.servers-defined` (off by default) | supported | — |
| `paths` | 3.0 (optional since 3.1) | controllers and actions; always present, possibly empty | always written | = | = | — | `spec.no-ref-siblings` (3.0), path rules in §8; planned (stage 1): `spec.paths-or-webhooks-or-components` (error; 3.0: `paths` required; 3.1/3.2: at least one of `paths`/`components`/`webhooks`; a missing object and an empty object are distinguished; standalone `validate` accepts documents with only `components` or only `webhooks`) | supported | — |
| `webhooks` | 3.1 | — (needs the tool's own annotation; no ASP.NET Core or Swashbuckle source) | dropped (lib) | written | written | DW | for documents not produced by the tool: operation rules and `operationId` uniqueness also run over `webhooks` operations | stage 2 | stage-2 |
| `components` | 3.0 | see §7 | written | = | = | — | `component.no-unused` (off by default) | supported | — |
| `security` | 3.0 | Roslyn `AddSecurityRequirement(...)`: one requirement object per call (OR across calls, AND within a call) | written | = | = | `Warning: security requirement references undeclared scheme '<n>' (document-level) — omitted; …` | `security.scheme-defined` | supported | — |
| `tags` | 3.0 | one tag per controller (§23) | written | = | = | — | `tag.no-duplicates`, `tag.description` (off by default) | supported | — |
| `externalDocs` | 3.0 | Roslyn `SwaggerDoc(...)` / `AddOpenApi(...)` containing `new OpenApiInfo { ExternalDocs = new OpenApiExternalDocs { Url, Description } }` (syntax match only; this initializer does not compile against Microsoft.OpenApi 2.x — CS0117 observed with Swashbuckle 10.3.0) | written | = | = | — | — | supported | — |
| `x-*` | 3.0 | see §32 | — | — | — | — | — | see §32 | — |

## 2. Info Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `info.title` | 3.0 | CLI `--title` → `[AssemblyTitle]` → `[AssemblyProduct]` → assembly file name. Roslyn `SwaggerDoc` `Title` is not read. | written | = | = | — | `spec.info-title` | supported | — |
| `info.summary` | 3.1 | CLI `--summary`; Roslyn `OpenApiInfo { Summary = … }` initializer inside `SwaggerDoc(...)` / `AddOpenApi(...)` (literals and constants only; with several `SwaggerDoc` calls the document is chosen as for `externalDocs`; property exists in Microsoft.OpenApi 2.x and 3.x); CLI wins | omitted | written | written | DW (3.0) | — | stage 1 | stage-1 |
| `info.description` | 3.0 | CLI `--description` → `[AssemblyDescription]` (MSBuild `<Description>`). Roslyn `SwaggerDoc` `Description` is not read. | written | = | = | — | `spec.info-description` | supported | — |
| `info.termsOfService` | 3.0 | CLI `--terms-of-service` (must be an absolute URI) | written | = | = | `Warning: --terms-of-service '<v>' is not a valid absolute URI and will be ignored.` | — | supported | — |
| `info.contact` | 3.0 | built when any `--contact-*` flag is given or `[AssemblyCompany]` resolves | written | = | = | — | — | supported | — |
| `info.license` | 3.0 | built only when `--license-name` is given | written | = | = | — | — | supported | — |
| `info.license` (merged sources) | 3.0 | CLI license flags and the Roslyn `OpenApiLicense` initializer (§4), merged field by field, CLI wins | written | = | = | — | `spec.license-identifier-or-url` (planned, §4) | stage 1 | stage-1 |
| `info.version` | 3.0 | CLI `--version` (default `v1`) | written | = | = | — | `spec.info-version` | supported | — |
| `info.x-*` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 3. Contact Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `contact.name` | 3.0 | CLI `--contact-name` → `[AssemblyCompany]` (MSBuild `<Company>`; the SDK defaults it to the assembly name) | written | = | = | — | — | supported | — |
| `contact.url` | 3.0 | CLI `--contact-url` (absolute URI) | written | = | = | `Warning: --contact-url '<v>' is not a valid absolute URI and will be ignored.` | — | supported | — |
| `contact.email` | 3.0 | CLI `--contact-email` (not validated) | written | = | = | — | — | supported | — |

## 4. License Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `license.name` | 3.0 | CLI `--license-name` | written | = | = | — | — | supported | — |
| `license.name` (merged sources) | 3.0 | CLI `--license-name` → Roslyn `new OpenApiLicense { Name = … }`. A license that has no `name` after the sources are merged (for example an identifier or URL without a name) is a configuration error; no name is synthesized and the license is never dropped silently. | written | = | = | — | — | stage 1 | stage-1 |
| `license.identifier` | 3.1 | CLI `--license-identifier`; Roslyn `new OpenApiLicense { Identifier = "MIT" }`; CLI wins. `identifier` and `url` are mutually exclusive: both set in the same source → configuration error; one set by the CLI and the other read from `Program.cs` → the CLI value wins and the other source's field is dropped without an error. | `x-oai-license-identifier` | `identifier` | `identifier` | DW (3.0) | planned: `spec.license-identifier-or-url` (3.1+, error: `identifier` and `url` are mutually exclusive; the library writes both without complaint) | stage 1 | stage-1 |
| `license.url` | 3.0 | CLI `--license-url` (absolute URI; ignored without `--license-name`) | written | = | = | `Warning: --license-url '<v>' is not a valid absolute URI and will be ignored.` | — | supported | — |
| `license.url` (merged sources) | 3.0 | CLI `--license-url` → Roslyn `new OpenApiLicense { Url = … }`; mutual exclusion with `identifier` as in the `license.identifier` row | written | = | = | — | planned: `spec.license-identifier-or-url` (3.1+) | stage 1 | stage-1 |

## 5. Server Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `servers[].url` | 3.0 | CLI `--server <url>` (repeatable, always read as a URL); `UsePathBase("/x")` with `--path-base-emission servers`. Roslyn `AddServer(...)` is not read. | written | = | = | — | `spec.servers-defined` (off by default) | supported | — |
| `servers[].description` | 3.0 | only the path-base entry: fixed text `Path base from UsePathBase()` | written | = | = | — | — | supported | — |
| `servers[].name` | 3.2 | a separate repeatable CLI flag for the server name (the old `--server` is always read as a whole URL, so URLs containing `=` keep working); also available through Core options. Binding is positional: the k-th name belongs to the k-th `--server` in flag order; either no names are given or exactly as many as `--server` flags. A different count, an empty name or a repeated name is a configuration error. The server added by the path-base emission mode never gets a name. | `x-oai-name` | `x-oai-name` | `name` | DW (3.0, 3.1) | planned: `servers[].name` unique (3.2, error) | stage 1 | stage-1 |
| `servers[].variables` | 3.0 | — | never | never | never | — | — | not planned: no code source; configuration only | — |
| `servers[].x-*` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 6. Server Variable Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `variables.*.enum` | 3.0 (non-empty since 3.1) | — | never | never | never | — | — | not planned: no code source | — |
| `variables.*.default` | 3.0 (must be in `enum` since 3.1) | — | never | never | never | — | — | not planned: no code source | — |
| `variables.*.description` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 7. Components Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `components.schemas` | 3.0 | every complex CLR type (class, struct, record, interface) reached from a parameter, body or response; ids follow the Swashbuckle convention (`UserDtoApiResponse`); an id already held by another type falls back to a longer name, then to that name with a numeric suffix `_2`, `_3`… in generation order; every type gets its own component. The fallback of a non-generic type (two types with one short name) is its full CLR name with `.`/`+` → `_` (`A.Summary` and `B.Summary` → `Summary` and `B_Summary`); the fallback of a closed generic type (two types with one candidate, such as `A.Page<Item>` and `B.Page<Item>`, both `ItemPage`) keeps the argument part and replaces the base name with the generic definition's full name, `.`/`+` → `_`, arity removed (`ItemB_Page`); `ProblemDetails` from `AddProblemDetails()` | written | = | = | — | `schema.description`, `component.no-unused` (off by default) | supported | — |
| `components.responses` | 3.0 | — | never | never | never | — | — | not planned: responses are inlined by design | — |
| `components.parameters` | 3.0 | — | never | never | never | — | — | not planned: parameters are inlined by design | — |
| `components.examples` | 3.0 | — | never | never | never | — | — | not planned: inlined; named examples are stage 2 (§19) | — |
| `components.requestBodies` | 3.0 | — | never | never | never | — | — | not planned: inlined by design | — |
| `components.headers` | 3.0 | — | never | never | never | — | — | not planned: inlined by design | — |
| `components.securitySchemes` | 3.0 | Roslyn `AddSecurityDefinition`, `AddJwtBearer` (§27) | written | = | = | `Warning: Duplicate security scheme '<n>' ignored (first registration wins).` | `security.scheme-description` | supported | — |
| `components.links` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `components.callbacks` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `components.pathItems` | 3.1 | — | dropped (lib); a `$ref` to it is left dangling | written | written | — | — | not planned: one-document generator; webhooks are inlined | — |
| `components.mediaTypes` | 3.2 | — | dropped (lib) | dropped (lib) | written | — | — | not planned: one-document generator | — |

## 8. Paths Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `paths./{path}` | 3.0 | class `[Route]` + `[Http*]` template; `[controller]`, `[action]` (incl. `[ActionName]`), `[area]` tokens; route constraints stripped; `UsePathBase` prefix (default `--path-base-emission prefix`); `--exclude-path`; exclusion via `[NonController]`, `[NonAction]`, `[ApiExplorerSettings(IgnoreApi = true)]`, `[ExcludeFromDescription]` (controller) | written | = | = | — | `path.params-match`, `path.no-empty-declaration`, `path.no-trailing-slash`, `path.no-query-string`, `path.no-identical` | supported | — |
| `paths./{path}` (two actions on the same path + method) | 3.0 | two actions mapped to one path and one method, through `[Http*]`, `[AcceptVerbs]` or both | one operation: the winner by the key — full name of the controller type, then the action method name, then the list of full parameter type names in order (for overloads), then attribute order in metadata; all comparisons ordinal; the winner does not depend on discovery order; the key is the same for `[Http*]` and `[AcceptVerbs]` | = | = | conflict warning `operation.path-method-conflict` naming every action involved (winner first); none for excluded paths | `operation.operation-id-unique` | supported | — |

## 9. Path Item Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `pathItem.$ref` | 3.0 | — | never | never | never | — | — | not planned: one-document generator | — |
| `pathItem.summary` | 3.0 | — | never | never | never | — | — | not planned: no code source (controller summary goes to the tag) | — |
| `pathItem.description` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `pathItem.get` | 3.0 | `[HttpGet]` | written | = | = | — | operation rules (§10) | supported | — |
| `pathItem.put` | 3.0 | `[HttpPut]` | written | = | = | — | operation rules | supported | — |
| `pathItem.post` | 3.0 | `[HttpPost]` | written | = | = | — | operation rules | supported | — |
| `pathItem.delete` | 3.0 | `[HttpDelete]` | written | = | = | — | operation rules | supported | — |
| `pathItem.options` | 3.0 | `[HttpOptions]` | written | = | = | — | operation rules | supported | — |
| `pathItem.head` | 3.0 | `[HttpHead]` | written | = | = | — | operation rules | supported | — |
| `pathItem.patch` | 3.0 | `[HttpPatch]` | written | = | = | — | operation rules | supported | — |
| `pathItem.trace` | 3.0 | `[AcceptVerbs("TRACE")]` (MVC has no `[HttpTrace]`) | `trace` | `trace` | `trace` | — | operation rules; `--require-response-code` counts TRACE as safe (with GET/HEAD/OPTIONS/QUERY) | supported | — |
| `pathItem.*` from `[AcceptVerbs("GET","POST", …)]` | 3.0 | `[AcceptVerbs]` (`Microsoft.AspNetCore.Mvc.AcceptVerbsAttribute`), `(string)` / `(params string[])` constructor, named `Route`; combined with `[Http*]` on the same action → union of methods without duplicates | one operation per method on the same path (methods with their own field); an explicit `operationId` shared by several operations of one action is kept and reported by validation, never renamed; without an explicit `operationId` no id is synthesized (as for single operations), so missing ids never conflict | = | = | — for methods with their own field; empty `[AcceptVerbs()]` → action not emitted, warning | operation rules, `operation.operation-id-unique` | supported | — |
| `pathItem.query` | 3.2 | `[AcceptVerbs("QUERY")]` with a named `Route` (`HttpMethods.Query` is a static field, not a constant, so an attribute can only take the literal) | `x-oai-additionalOperations.QUERY` (the operation inside keeps the 3.2 form) | `x-oai-additionalOperations.QUERY` | `query` | DW (3.0, 3.1): one per operation, covering the whole operation subtree; the operation is invisible to 3.0/3.1 tools | operation rules, `operationId` uniqueness, path exclusions and `--require-response-code` cover QUERY (safe group); violation pointers follow the real output, e.g. `x-oai-additionalOperations/QUERY` | supported | — |
| `pathItem.additionalOperations` | 3.2 | `[AcceptVerbs("LINK")]` etc.: any non-standard method; the key keeps the capitalization sent in the request. A method that has its own field never goes here. | `x-oai-additionalOperations.<M>` (3.2 form inside) | `x-oai-additionalOperations.<M>` | `additionalOperations.<M>` | DW (3.0, 3.1): one per operation | operation rules cover these operations, with pointers into `additionalOperations` / `x-oai-additionalOperations`; `--require-response-code` counts non-standard methods as mutating | supported | — |
| `pathItem.*` from a custom `HttpMethodAttribute` subclass | 3.0 | user `class HttpQueryAttribute : HttpMethodAttribute` (methods live in constructor IL, not in metadata) | action not emitted; the method is never guessed from the attribute name | = | = | `unknown HttpMethodAttribute subclass X; use [AcceptVerbs]` | — | stage 1 | stage-1 |
| `pathItem.servers` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `pathItem.parameters` | 3.0 | — | never (parameters are written per operation) | never | never | — | — | not planned: per-operation by design | — |
| `pathItem.x-*` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 10. Operation Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `operation.tags` | 3.0 | `[SwaggerOperation(Tags = …)]` → `[Tags(...)]` (action) → controller name. Tags from attributes are not added to top-level `tags[]`, so they dangle. | written | = | = | — | `operation.tags`, `operation.tag-defined` | supported | — |
| `operation.summary` | 3.0 | `[SwaggerOperation(Summary)]` → `[EndpointSummary]` → XML `<summary>` | written | = | = | — | `operation.summary`, markdown rules | supported | — |
| `operation.description` | 3.0 | `[SwaggerOperation(Description)]` → `[EndpointDescription]` → XML `<remarks>` | written | = | = | — | `operation.description`, `operation.deprecated-has-note`, `spec.no-eval-in-markdown`, `spec.no-script-tags-in-markdown` (off by default) | supported | — |
| `operation.externalDocs` | 3.0 | — | never | never | never | — | — | not planned: no standard source | — |
| `operation.operationId` | 3.0 | `[SwaggerOperation(OperationId)]` → `Name` of the attribute that produced the operation (`[Http*]` or `[AcceptVerbs]`); when one action declares the same method and route twice, the first given `Name` is used (two different names: the first, with a warning `discovery.conflicting-operation-names`); never synthesized | written | = | = | — | `operation.operation-id`, `operation.operation-id-unique`, `operation.operation-id-url-safe`, `operation.operation-id-pascal-case` (off by default) | supported | — |
| `operation.parameters` | 3.0 | §12 | written | = | = | — | `operation.parameters-unique` | supported | — |
| `operation.requestBody` | 3.0 | §13 | written | = | = | — | `operation.request-body-description` | supported | — |
| `operation.operationId` for a multi-method action | 3.0 | `[AcceptVerbs]` with several methods, without an explicit `operationId` | not synthesized (as for single operations today); an explicit `[SwaggerOperation(OperationId)]` / attribute `Name` is the same on every operation of the action and is reported by `operation.operation-id-unique` | = | = | — | `operation.operation-id-unique` | supported | — |
| `operation.requestBody` on GET/HEAD/DELETE | 3.0 (allowed since 3.1) | `[FromBody]` (or an inferred body) on such an action | written unchanged; 3.0 consumers must ignore it | written | written | 3.0 only: warning with method and final path ("3.0 consumers must ignore the body"), code `request-body.get-head-delete`; none for excluded paths | — | supported | — |
| `operation.responses` | 3.0 (optional since 3.1) | §16 | always written | = | = | — | `operation.has-error-response`, `operation.success-response`, `operation.has-required-response-codes` (off by default) | supported | — |
| `operation.callbacks` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `operation.deprecated` | 3.0 | `[Obsolete]` on the action or the controller | written | = | = | — | `operation.deprecated-has-note` | supported | — |
| `operation.security` | 3.0 | `[AllowAnonymous]` → `[]`; `[Authorize(AuthenticationSchemes = "A,B")]` → one requirement (AND); plain `[Authorize]` → inherits document-level | written | = | = | `Warning: security requirement references undeclared scheme '<n>' (<METHOD> <path>) — omitted; …` | `operation.security`, `security.scheme-defined` | supported | — |
| `operation.servers` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `operation.x-api-version` | — (extension) | `[ApiVersion]`, `[MapToApiVersion]`, `[ApiVersionNeutral]` (Asp.Versioning and legacy names) | written | = | = | — | — | supported | — |
| `operation.x-rate-limit-policy` / `x-rate-limit-disabled` | — (extension) | `[EnableRateLimiting]` / `[DisableRateLimiting]` | written | = | = | — | — | supported | — |

## 11. External Documentation Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `externalDocs.url` (document) | 3.0 | Roslyn `SwaggerDoc` / `AddOpenApi` → `OpenApiInfo.ExternalDocs.Url` (absolute URI) | written | = | = | — | — | supported | — |
| `externalDocs.description` (document) | 3.0 | same initializer, `Description` | written | = | = | — | — | supported | — |
| `tags[].externalDocs.url` | 3.0 | Roslyn `AddTag(new OpenApiTag { ExternalDocs = … })` (syntax match; `SwaggerGenOptions.AddTag` does not exist in Swashbuckle 10.3.0 — CS1061 observed) | written | = | = | — | — | supported | — |
| `tags[].externalDocs.url` from `[SwaggerTag(description, externalDocsUrl)]` | 3.0 | `SwaggerTagAttribute` constructor argument 1, an absolute URI (otherwise not written); wins over `AddTag(...)` externalDocs in `Program.cs`, which fills only a tag without one | written on the controller's tag | = | = | — | — | supported | — |
| `tags[].externalDocs.description` | 3.0 | Roslyn `AddTag` | written | = | = | — | — | supported | — |
| `operation.externalDocs`, `schema.externalDocs` | 3.0 | — | never | never | never | — | — | not planned: no standard source | — |

## 12. Parameter Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `parameter.name` | 3.0 | C# parameter name, or `Name` from `[FromRoute]` / `[FromQuery]` / `[FromHeader]` | written | = | = | — | `operation.parameters-unique` | supported | — |
| `parameter.in: path` | 3.0 | `[FromRoute]`, or a parameter whose name matches a route token | written | = | = | — | `path.params-match` | supported | — |
| `parameter.in: query` | 3.0 | `[FromQuery]`, or `[ApiController]` inference for simple types | written | = | = | — | — | supported | — |
| `parameter.in: query` from `[FromQuery] ComplexType` | 3.0 | `[FromQuery] Filter f` (MVC binds it per property) | one parameter `f` with `$ref` | = | = | — | — | separate (per-property expansion is outside both stages) | — |
| `parameter.in: header` | 3.0 | `[FromHeader]` | written | = | = | — | — | supported | — |
| `parameter.in: cookie` | 3.0 | — (MVC has no `[FromCookie]`) | never | never | never | — | — | not planned: no code source | — |
| `parameter.in: querystring` | 3.2 | — | never (library throws in 3.0/3.1) | never (library throws) | never | — | — | not planned: MVC never binds the whole query string as one value; must never be generated | — |
| `parameter.description` | 3.0 | `[SwaggerParameter("…")]` → `[Description]` → XML `<param name>` | written | = | = | — | `parameter.description` | supported | — |
| `parameter.description` for a renamed parameter | 3.0 | XML `<param name="a">` on `[FromHeader(Name = "X-A")] string a` / `[FromQuery(Name = …)]` | written: the XML description is found by the C# parameter name | = | = | — | `parameter.description` | supported | — |
| `parameter.required` | 3.0 | path → always true; `[Required]`; optional when there is a default value, `[DefaultValue]`, `Nullable<T>` or NRT `?` | written | = | = | — | `parameter.path-required-true`, `parameter.optional-has-default` | supported | — |
| `parameter.required` from `[SwaggerParameter(Required = …)]` | 3.0 | `SwaggerParameterAttribute.Required` on a path, query or header parameter | the explicit value wins over the inferred one; on a path parameter `required: true` is kept | = | = | `Required = false` on a path parameter → one warning at the parameter, subjects: its name (code `parameter.path-required-kept`) | `parameter.path-required-true` | supported | — |
| `parameter.deprecated` | 3.0 | — (`[Obsolete]` is not valid on parameters: CS0592) | never | never | never | — | — | not planned: no standard source | — |
| `parameter.allowEmptyValue` | 3.0 (deprecated) | — | never | never | never | — | — | not planned: deprecated, no source | — |
| `parameter.style` | 3.0 (`cookie` value since 3.2) | — (defaults apply: `form` for query, `simple` for path and header, which match MVC binding) | never written | = | = | — | — | supported (defaults); `style: cookie` not planned: library throws in 3.0/3.1, no source | — |
| `parameter.explode` | 3.0 | — (the default `true` for `form` matches MVC `ids=1&ids=2`) | never written | = | = | — | — | supported (default) | — |
| `parameter.allowReserved` | 3.0 (scope widened in 3.2) | — | never | never | never | — | — | not planned: no code source | — |
| `parameter.schema` | 3.0 | `SchemaGenerator` on the parameter type (§24) | written | = | = | — | `parameter.schema-type` | supported | — |
| `parameter.schema` constraints from parameter attributes | 3.0 | `[Range]`, `[StringLength]`, `[RegularExpression]`, `[Length]`, … on the action parameter | `[StringLength]`, `[MinLength]`, `[MaxLength]`, `[Length]`, `[RegularExpression]`, `[Range]`, `[AllowedValues]`, `[DeniedValues]` and the format attributes (`[EmailAddress]`, `[Url]`, `[Phone]`, `[DataType]`, by the format priority) applied exactly as to DTO properties (§24.3, §24.4, same per-version forms) to the schema of a path / query / header parameter, of each `[FromForm]` field, and of the request body for attributes on the `[FromBody]` parameter (a reference body is wrapped in `allOf`, the shared component is unchanged); enum members are listed by their member names for bound parameters and form fields, which model binding reads, and by the serializer's names for a body | = | = | as for properties, located at the parameter; a `[Range]` `RangeAttribute` rejects is an extraction error naming the controller type and `Method(parameter)` | `schema.property-constraints` covers properties only | supported | — |
| `parameter.schema.default` | 3.0 | `[DefaultValue(x)]` → C# default value (`int page = 1`; an enum default in the enum schema's form: name for a string enum, else number) | written | = | = | — | `parameter.optional-has-default` | supported | — |
| `parameter.example` | 3.0 | XML `<param name="x" example="2">` for a path / query / header parameter, found by the C# name also when `Name =` renames the parameter; written whether or not the parameter has a description. The value is parsed by the effective schema as for schema examples (§24.5): integer/number → number, boolean → boolean, `null` → JSON null (nullable schema only), object/array → JSON parse, string → string as is. | `example` | `example` | `example` | value that does not parse for a non-string schema, or `null` for a non-nullable schema → not written, one warning per parameter located at the parameter, subjects: its name and the value (code `parameter.example-not-parsable`) | — | supported | — |
| `parameter.examples` | 3.0 | — | never | never | never | — | — | stage 2 (named examples need the tool's own annotation) | stage-2 |
| `parameter.content` | 3.0 | — | never | never | never | — | — | not planned: no source; only needed for `querystring`/complex serialization | — |

## 13. Request Body Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `requestBody.description` | 3.0 | `[SwaggerRequestBody("…")]` / `[SwaggerRequestBody(Description = …)]` → `[SwaggerParameter]` → `[Description]` on the `[FromBody]` parameter | written | = | = | — | `operation.request-body-description` | supported | — |
| `requestBody.content` (body) | 3.0 | `[FromBody]`, or `[ApiController]` inference → the `application/json` key unless `[Consumes]` declares media types (next row); global `[Consumes]` from `AddControllers(o => o.Filters.Add(new ConsumesAttribute(...)))` replaces the key | written | = | = | — | — | supported | — |
| `requestBody.content` from action/controller `[Consumes]` | 3.0 | `[Consumes("application/xml")]` on the action or controller | media types of the body taken from `[Consumes]`: action wins over controller, controller wins over global | = | = | — | — | supported | — |
| `requestBody.content` (form) | 3.0 | `[FromForm]` parameters, `IFormFile`, `IFormFileCollection` → a synthetic object under `multipart/form-data`, one property per parameter under its form field name (`[FromForm(Name = …)]`, else the C# name); the required fields (parameter required logic of §12, `[SwaggerParameter(Required)]` included) are listed in its `required` | written | = | = | — | — | supported | — |
| `requestBody.content` form media type from `[Consumes]` | 3.0 | `[Consumes("application/x-www-form-urlencoded")]` etc. on the action or controller | form media type from `[Consumes]`; without `[Consumes]` `multipart/form-data` as today | = | = | — | — | supported | — |
| `requestBody.required` | 3.0 | body: parameter required logic (§12); form body: not set | written | = | = | — | — | supported | — |
| `requestBody.x-*` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 14. Media Type Object

The **effective response type** of a response is taken, in priority order, from: the type in `[ProducesResponseType]` / `[SwaggerResponse]` for that status code → the type in `[Produces(typeof(T))]` / `[Produces<T>]` (the 200 response, as ASP.NET Core ApiExplorer treats it) → the return type after `Task<>`, `ValueTask<>` and `ActionResult<>` are removed; at each level the action attribute wins over the controller attribute. Streaming is decided by the effective response type. A user type implementing `IAsyncEnumerable<T>` counts as `IAsyncEnumerable<T>`. The mapping is chosen **per media type**: when several media types are declared, the streaming form applies only to the sequential ones (`application/jsonl`, `application/x-ndjson`, `application/json-seq`, `text/event-stream`), and a JSON media type gets an array. Other responses of the action (errors etc.) are emitted as usual. Fake component schemas of BCL wrappers (`…IAsyncEnumerable`, `…ServerSentEventsResult`) never appear in any version.

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `mediaType.schema` (request/response) | 3.0 | body / response CLR type (§24); `[Produces]` without a type → media type without a schema | written | = | = | — | `response.schema-when-body` | supported | — |
| `mediaType.schema` type from `[Produces(typeof(T))]` / `[Produces<T>]` | 3.0 | `ProducesAttribute` type argument on the action or the controller (action wins); priority between `[ProducesResponseType]` / `[SwaggerResponse]` and the return type, for streaming and non-streaming responses alike; as in ApiExplorer it declares the 200 response (added next to other declared codes; a 200 declared without a type takes it) | body schema of `T` for the 200 response | = | = | — | `response.schema-when-body` | supported | — |
| `mediaType.schema` serialization context | 3.0 | MVC bodies → MVC JSON options (`AddJsonOptions`); SSE `contentSchema` and typed `IResult` bodies → HTTP JSON options (`ConfigureHttpJsonOptions`), as ASP.NET Core 10 applies them; a context Program.cs does not configure gets the ASP.NET Core default (camelCase); CLI `--naming-policy` applies only when Program.cs sets no JSON options in either context | each schema built from the options of its own context; a CLR type used in both contexts while the two option sets differ in something that affects the schema (naming policy, ignore condition, number handling, converters) → each context gets its own schema built from its own options (names of one context are never presented as the wire of the other) | = | = | one warning on the document (code `serialization-contexts.shared-types`) whose subjects are exactly those types | — | supported for MVC bodies, SSE `contentSchema` and typed `IResult` bodies: when the options differ, a type both contexts describe gets `{Id}Http` in the HTTP context (every role of a polymorphic base follows: `{D}As{Id}Http`, `{Id}HttpDefault`, and `mapping` / `defaultMapping` of the HTTP union lead only to HTTP targets); a type only the HTTP context describes keeps its id; a taken id falls back to the full CLR name with the same suffix | — |
| `mediaType.schema` for `IAsyncEnumerable<T>`, JSON media type (incl. inside `Task<>`/`ValueTask<>`/`ActionResult<>`, types implementing it, and the type declared by an attribute) | 3.0 | effective response type | `{type: array, items: T}`; nullable `T` → nullable items by the version's rules: `Nullable<T>`, and a reference element annotated `T?` in the action's return type (read from the compiler's nullable metadata through `Task<>` / `ValueTask<>` / `ActionResult<>`; `type: [T, "null"]` or `anyOf: [$ref, {type: null}]`, 3.0 `nullable: true`); an oblivious (`#nullable disable`) element stays non-nullable; the same applies to `itemSchema` of sequential media types | = | = | — | `schema.array-items` | supported for JSON media types; sequential media types: stage 1 | stage-1 |
| `mediaType.schema` for `IAsyncEnumerable<SseItem<T>>` returned from a controller | 3.0 | effective response type | JSON array of `SseItem<T>` objects as MVC writes them (same row as any `IAsyncEnumerable<T>`); SSE is never guessed from the type name | = | = | — | `schema.array-items` | supported | — |
| `mediaType.schema` for `ServerSentEventsResult<T>` | 3.0 | effective response type (.NET 10) | `text/event-stream` without `schema`; the event schema in `x-oai-itemSchema` (see the `itemSchema` row) | = | `text/event-stream` without `schema`; the event schema in `itemSchema` | DW (3.0, 3.1): see the `itemSchema` row | `response.schema-when-body` (planned: accepts `itemSchema` / `x-oai-itemSchema`) | supported (validation rule: stage 1) | stage-1 |
| `mediaType.schema` for typed `IResult` results | 3.0 | closed rule over framework results: `Results<…>` by its variants; types implementing `IStatusCodeHttpResult` with a known code and `IValueHttpResult<T>` (e.g. `Ok<T>`); status-only results (`IStatusCodeHttpResult` with a known code and no `IValueHttpResult<T>`: `NoContent`, `NotFound`, `UnauthorizedHttpResult`, …) | one response per status: with the body schema of `T` (HTTP JSON options), or without a schema for status-only results; explicit `[ProducesResponseType]` wins over inferred responses; file results → the binary row below; no fake components | = | = | only an `IResult` whose status is not statically known (untyped, user-defined, `JsonHttpResult<T>`, `StatusCodeHttpResult`, `ProblemHttpResult`, redirects, …) and with no declared response → that response is not described, warning (code `response.result-status-unknown`, one per operation, subjects: those results); the known variants of the same `Results<…>` are still described, and only when no response is known at all a 200 response without a schema stands in; status-only results get no warning | — | supported (the known statuses are the closed table of `Microsoft.AspNetCore.Http.HttpResults`: `Ok` 200, `Created`/`CreatedAtRoute` 201, `Accepted`/`AcceptedAtRoute` 202, `NoContent` 204, `BadRequest` and `ValidationProblem` (`application/problem+json`) 400, `UnauthorizedHttpResult` 401, `NotFound` 404, `Conflict` 409, `UnprocessableEntity` 422, `InternalServerError` 500; bodies declared by attributes on an `IResult` action are in the HTTP context too and follow the same streaming rules (`ServerSentEventsResult<T>` → event stream, `IAsyncEnumerable<T>` → array or `itemSchema` per media type); on an `IResult` action `[Produces(typeof(T))]` / `[Produces<T>]` gives the 200 body ahead of the result's value type, and a 200 declared with its own type keeps it; file results: see the next row) | — |
| `mediaType.schema` for file and stream types | 3.0 | all framework file types: MVC `FileResult` and derived types (`FileStreamResult`, `FileContentResult`, …), `IFileHttpResult` (`FileContentHttpResult`, `FileStreamHttpResult`, …), `Stream`, `IFormFile` | `{type: string, format: binary}` under the media type from `[Produces]` / `[Consumes]` (default for a response: `application/octet-stream`); `IFormFile` in a form → that form field; no fake components | = (no `contentMediaType` form) | = | — | — | supported (the response media type is the response's own declaration, else `[Produces]`, else `application/octet-stream`; a global `[Produces]` filter does not apply to file results; a typed file result is a 200 response) | — |
| `mediaType.itemSchema` (SSE) | 3.2 | `ServerSentEventsResult<T>` as effective response type | `x-oai-itemSchema` with the event object (the library writes nested 2020-12 keywords as `x-jsonschema-*`) | `x-oai-itemSchema` with the event object | `itemSchema`: `{type: object, required: [data], properties: {data: {type: string, contentMediaType: application/json, contentSchema: T}, event: {type: string}, id: {type: string}, retry: {type: integer, minimum: 0}}}`; for `T = string` and `T = byte[]`, `data` is a plain string **without** `contentMediaType` and **without** `contentSchema`; a `null` element is empty data (allowed by the string schema) | DW (3.0, 3.1): one per media type, covering `contentMediaType` / `contentSchema` inside | planned: `response.schema-when-body` accepts `itemSchema` / `x-oai-itemSchema`; `component.no-unused` follows `itemSchema` / `contentSchema` | supported (validation rules: stage 1) | stage-1 |
| `mediaType.itemSchema` (JSON Lines / NDJSON / json-seq) | 3.2 | `IAsyncEnumerable<T>` as effective response type + declared `application/jsonl` / `application/x-ndjson` / `application/json-seq` (`[Produces]`, `[ProducesResponseType]`, `[SwaggerResponse]`) | that media type without `schema`, `T` in `x-oai-itemSchema` | = | that media type with `itemSchema: T`, no `schema` | DW (3.0, 3.1): one per media type, at the media type; none inside an operation moved to `x-oai-additionalOperations`, whose DW covers it | as above | supported (validation rule: stage 1) | stage-1 |
| `mediaType.*` for `IAsyncEnumerable<T>` + declared `text/event-stream` | 3.2 | standard MVC has no SSE output formatter (406 at runtime without a custom one) | `text/event-stream` without `schema` | = | = | warning (every version, at the operation, also when the operation is moved): "standard MVC has no SSE formatter: use a custom formatter or `ServerSentEventsResult<T>`" | `response.schema-when-body`: a media type the generator deliberately emits without a schema is not a violation | supported (validation rule: stage 1) | stage-1 |
| `mediaType.*` for `[Produces("text/event-stream")]` without a body type | 3.0 | `[Produces]` | `content` without a schema | = | = | — | as above | supported | — |
| `mediaType.example` (`[FromBody]`) | 3.0 | XML `<param name="body" example="…">` on the `[FromBody]` parameter; parsed by the body's effective schema (as for `parameter.example`) | request `content.<type>.example` on every media type of the body, global `[Consumes]` included (not a Parameter Object) | = | = | value that does not parse for a non-string schema, or `null` for a body that is not nullable → not written, one warning located at the request body, subjects: the C# parameter name and the value (code `parameter.example-not-parsable`) | — | supported | — |
| `mediaType.example` (form body) | 3.0 | XML `<param name="x" example="…">` on one or several `[FromForm]` parameters that make up one form body | `content.<form type>.example`: an object whose keys are the wire names of the form fields (after renaming by attribute) and whose values are the parsed examples; fields without an example are left out; a complex `[FromForm]` parameter gives its example as an object, as for `[FromBody]`, under its field name (as the form schema has it) | = | = | a field example that does not parse → field left out; one warning per body located at the request body, subjects: a pair of field name and value for each such field (code `parameter.example-not-parsable`) | — | supported | — |
| `mediaType.examples` | 3.0 | — | never | never | never | — | — | stage 2 (named examples) | stage-2 |
| `mediaType.encoding` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `mediaType.prefixEncoding` | 3.2 | — | `x-oai-prefixEncoding` (lib) | `x-oai-prefixEncoding` | written | — | — | not planned: no typical ASP.NET Core source | — |
| `mediaType.itemEncoding` | 3.2 | — | `x-oai-itemEncoding` (lib) | `x-oai-itemEncoding` | written | — | — | not planned: no typical ASP.NET Core source | — |
| `content` key as `$ref` to `components.mediaTypes` | 3.2 | — | never | never | never | — | — | not planned: one-document generator | — |

## 15. Encoding Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `encoding.contentType` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `encoding.headers` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `encoding.style` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `encoding.explode` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `encoding.allowReserved` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `encoding.encoding` | 3.2 | — | never | never | never | — | — | not planned: no code source | — |
| `encoding.prefixEncoding` | 3.2 | — | never | never | never | — | — | not planned: no code source | — |
| `encoding.itemEncoding` | 3.2 | — | never | never | never | — | — | not planned: no code source | — |

## 16. Responses Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `responses.default` | 3.0 | `[ProducesDefaultResponseType]` / `[ProducesDefaultResponseType(typeof(T))]` | written | = | = | — | — | supported | — |
| `responses.{code}` | 3.0 | `[SwaggerResponse]` → `[ProducesResponseType]` (all overloads, incl. the generic form `[ProducesResponseType<T>]`) → `[ProducesDefaultResponseType]`; first wins per code. A 200 declared without a type gets the `[Produces(typeof(T))]` type, else the return type (§14). When nothing declares a response (action or controller, `[Produces(typeof(T))]` included): inferred from the return type (`void`/`Task` → 204; `IActionResult`/`ActionResult` → 200 without a body; otherwise 200 + body), as ASP.NET Core ApiExplorer does. `AddProblemDetails()` adds 400/422/500 `application/problem+json` where missing. Controller-level declarations: next row. | written | = | = | — | `operation.has-error-response`, `operation.success-response`, `operation.has-required-response-codes` (off by default; planned: QUERY and TRACE count as safe with GET/HEAD/OPTIONS, non-standard methods as modifying) | supported | — |
| `responses.{code}` from controller-level `[ProducesResponseType]` | 3.0 | `[ProducesResponseType]`, `[SwaggerResponse]` and `[ProducesDefaultResponseType]` on the controller class — a listed change to 3.0 output | responses of every operation of the controller, with the same fields as the action-level attribute; for the same status code the action wins over the controller field by field (a body type or description the action leaves open comes from the controller); as in ApiExplorer, a controller declaration means the return type no longer adds an inferred 200 | = | = | — | as above | supported | — |
| `responses.{code}` inferred from typed `IResult` | 3.0 | see §14 (typed `IResult` row) | one response per statically known status; explicit `[ProducesResponseType]` wins | = | = | see §14 | as above | supported | — |
| `responses.{1XX..5XX}` ranges | 3.0 | — | never | never | never | — | — | not planned: no standard source | — |
| `[ProducesErrorResponseType]` / `[ApiController]` automatic 400 | 3.0 | not read | — | — | — | — | — | not planned: outside this stage; error responses come only from `AddProblemDetails()` injection and explicit attributes | — |

## 17. Response Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `response.summary` | 3.2 | — (no standard source; `<response>`, `[SwaggerResponse]` and `ProducesResponseType.Description` all map to `description`) | `x-oai-summary` (lib) | `x-oai-summary` | `summary` | DW | — | stage 2 | stage-2 |
| `response.description` | 3.0 (optional since 3.2) | `[SwaggerResponse(code, "desc")]` → `[ProducesResponseType(Description = …)]` (.NET 10) → XML `<response code>` on the action → the same three sources on the controller (next row) → built-in status text; `[ProducesDefaultResponseType]` → `Response` | always written | = | = | — | `response.description` (planned: error for 3.0/3.1, warning for 3.2, where `description` is optional; `--warn-rule` / `--error-rule` / `--skip-rule` / `--strict` work as before) | supported | — |
| `response.description` from controller-level XML `<response>` | 3.0 | XML `<response code>` on the controller class — a listed change to 3.0 output | description of that status on every operation of the controller that has a response with that code; for the same status code every source on the action wins over the controller | = | = | — | as above | supported | — |
| `response.headers` | 3.0 | `[ResponseCache]` / `[OutputCache]` → `Cache-Control` on 2xx; Roslyn middleware `Response.Headers.Append/Add/TryAdd` / indexer → header on every response | written | = | = | — | — | supported | — |
| `response.content` | 3.0 | `[Produces]` (action → controller → global filter), content types in `[ProducesResponseType(typeof(T), code, "ct", …)]` and `[SwaggerResponse(..., contentTypes)]` (the response's own media types; a global `[Produces]` filter does not replace them); body type from the attribute or the return type | written | = | = | — | `response.schema-when-body`, `response.content-type-json-default` (off by default) | supported | — |
| `response.content` from `[SwaggerResponse(..., contentTypes)]` | 3.0 | `SwaggerResponseAttribute` content types | the declared content types are written | = | = | — | — | supported | — |
| `response.links` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 18. Callback Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `callbacks.{name}.{expression}` | 3.0 | — | never | never | never | — | — | not planned: no code source (webhooks are a different semantic, stage 2) | — |

## 19. Example Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `example.summary` | 3.0 | — | never | never | never | — | — | stage 2 | stage-2 |
| `example.description` | 3.0 | — | never | never | never | — | — | stage 2 | stage-2 |
| `example.value` | 3.0 (deprecated for non-JSON targets in 3.2) | — | never | never | never | — | deferred: `example.value-exclusive` (meaningful only after `dataValue`) | stage 2 (JSON examples: `value` in 3.0/3.1) | stage-2 |
| `example.dataValue` | 3.2 | — | `x-oai-dataValue` (lib) | `x-oai-dataValue` | `dataValue` | DW | deferred: `example.value-exclusive` | stage 2 | stage-2 |
| `example.serializedValue` | 3.2 | — | `x-oai-serializedValue` (lib) | `x-oai-serializedValue` | `serializedValue` | DW | deferred: `example.value-exclusive` | stage 2 | stage-2 |
| `example.externalValue` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 20. Link Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `link.operationRef` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `link.operationId` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `link.parameters` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `link.requestBody` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `link.description` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `link.server` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 21. Header Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `header.description` | 3.0 | fixed text: `Cache-Control: max-age=…` (from caching attributes) or `Response header '<n>' set by middleware.` | written | = | = | — | — | supported | — |
| `header.schema` | 3.0 | always `{type: string}` | written | = | = | — | — | supported | — |
| `header.required` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `header.deprecated` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `header.allowEmptyValue` | 3.0 (not part of Header in 3.2) | — | never | never | never | — | — | not planned: no source | — |
| `header.style` | 3.0 | — (default `simple`) | never | never | never | — | — | not planned: default suffices | — |
| `header.explode` | 3.0 | — | never | never | never | — | — | not planned: default suffices | — |
| `header.example` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `header.examples` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |
| `header.content` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 22. Reference Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `$ref` (schemas) | 3.0 | a repeated complex type → `#/components/schemas/<id>` | written | = | = | — | `spec.no-ref-siblings` (3.0 only) | supported | — |
| `$ref` (security scheme in requirements) | 3.0 | requirement keys | written | = | = | — | `security.scheme-defined` | supported | — |
| `$ref` (responses, parameters, examples, request bodies, headers, links, callbacks, path items, media types) | 3.0 | — | never | never | never | — | — | not planned: inline by design | — |
| `reference.summary` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: no non-schema references are built | — |
| `reference.description` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: no non-schema references are built | — |

## 23. Tag Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `tags[].name` | 3.0 | controller name without the `Controller` suffix. `AddTag` tags without a matching controller are not added. | written | = | = | — | `tag.no-duplicates`, `operation.tag-defined` | supported | — |
| `tags[].summary` | 3.2 | Roslyn `AddTag(new OpenApiTag { Summary })` (MS.OpenApi 3.x only) | `x-oas-summary` (lib) | `x-oas-summary` | `summary` | DW (3.0, 3.1) | — | stage 1 | stage-1 |
| `tags[].summary` from an own attribute | 3.2 | — (needs the tool's own annotation) | never | never | never | — | — | stage 2 | stage-2 |
| `tags[].description` | 3.0 | `[SwaggerTag("…")]` → XML `<summary>` on the controller → Roslyn `AddTag(... Description ...)` | written | = | = | — | `tag.description` (off by default) | supported | — |
| `tags[].externalDocs` | 3.0 | see §11 | written | = | = | — | — | supported | — |
| `tags[].parent` | 3.2 | Roslyn `AddTag(new OpenApiTag { Parent = … })` (MS.OpenApi 3.x only) | `x-oas-parent` (lib) | `x-oas-parent` | `parent` | DW (3.0, 3.1) | planned: `tag.parent-defined`, `tag.no-parent-cycle` (3.2, error) | stage 1 | stage-1 |
| `tags[].parent` from an own attribute | 3.2 | — (needs the tool's own annotation) | never | never | never | — | — | stage 2 | stage-2 |
| `tags[].kind` | 3.2 | Roslyn `AddTag(new OpenApiTag { Kind = … })` (MS.OpenApi 3.x only). Registry values: `nav`, `badge`, `audience`. | `x-oas-kind` (lib) | `x-oas-kind` | `kind` | DW (3.0, 3.1) | — | stage 1 | stage-1 |
| `tags[].kind` from an own attribute | 3.2 | — (needs the tool's own annotation) | never | never | never | — | — | stage 2 | stage-2 |

## 24. Schema Object

Schema rows are split by keyword group. "Since" for JSON Schema keywords is the OpenAPI version whose Schema Object accepts the keyword natively. In 3.0 the Schema Object is a subset of JSON Schema Wright draft 00 plus OAS extensions.

### 24.1 Core keywords

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `schema.$ref` | 3.0 | complex type reuse (§22); a reference with sibling attributes is wrapped in `allOf: [$ref]` (all versions) | written | = | = | — | `spec.no-ref-siblings` | supported | — |
| `schema.$ref` + siblings (`description`, `deprecated`, `default`, `readOnly`, `title`, `examples`) | 3.1 | XML `<summary>`, `<example>`, `[Description]`, `[Obsolete]`, `[DefaultValue]`, validation attributes on a property of reference type | `allOf: [$ref]` + siblings on the wrapper | `allOf: [$ref]` + siblings on the wrapper | `allOf: [$ref]` + siblings on the wrapper | — | `spec.no-ref-siblings` (3.0) | separate (siblings directly next to `$ref` in 3.1+ are tracked only under `ref-siblings`; the property-level siblings never change the shared component schema) | ref-siblings |
| `schema.$schema` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: no code source | — |
| `schema.$id` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: `components.schemas` covers reuse | — |
| `schema.$anchor` | 3.1 | — | `x-jsonschema-$anchor` (lib) | written | written | — | — | not planned: no code source | — |
| `schema.$dynamicRef` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: no code source | — |
| `schema.$dynamicAnchor` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: no code source | — |
| `schema.$vocabulary` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: no code source | — |
| `schema.$comment` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: no code source | — |
| `schema.$defs` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: `components.schemas` covers reuse | — |

### 24.2 Applicator keywords

Polymorphism (`[JsonPolymorphic]` with `TypeDiscriminatorPropertyName`, `IgnoreUnrecognizedTypeDiscriminators`, `UnknownDerivedTypeHandling`; `[JsonDerivedType(typeof(D)[, discriminator])]` with a string or integer discriminator; `[SwaggerDiscriminator]` + `[SwaggerSubType(typeof(D), DiscriminatorValue = …)]`) follows the STJ wire contract. The rows below describe the schema used where the base type is used polymorphically. A **variant** is the schema of an alternative that has a discriminator value (§24.7); an alternative without a value has no discriminator property. A **base branch** is the union alternative for a concrete base: it accepts the base object **without** the discriminator property and — only with `IgnoreUnrecognizedTypeDiscriminators = true` — an object whose discriminator value is **not** in the mapped set; it never accepts a mapped value, so `oneOf` stays mutually exclusive. With `IgnoreUnrecognizedTypeDiscriminators = false` no alternative accepts an unknown value (STJ rejects it). An abstract base or an interface has no base branch. The polymorphism configuration is not inherited: the union of a base lists exactly its declared derived types, and a derived type that is itself a polymorphic base gets its own union in its own polymorphic use. A custom `[JsonConverter]` on the base disables polymorphism and the existing unknown-converter rule applies. Today all of these attributes are ignored: the base is a plain object, inheritance is flattened, and derived types are absent (verified). The tool writes the base branch as the component `{Base}Default` (another id when a type already holds that name): the base's own properties plus, without `IgnoreUnrecognizedTypeDiscriminators`, `not: {required: [<property>]}`, and with it `properties: {<property>: {anyOf: [{type: string}, {type: integer, format: int32, minimum: -2147483648, maximum: 2147483647}], not: {enum: [<mapped values>]}}}` — System.Text.Json reads only a string or an Int32 integer as a discriminator (booleans, `null`, fractions, objects, arrays and larger integers are rejected), and a number never selects a variant declared with a string or the reverse (JSON Schema cannot tell `1.0` from `1`, which STJ rejects); in an `anyOf` union it carries no constraint. A variant is the component `{Derived}As{Base}`. A derived type without a value is its direct-use object (as STJ writes it under the base: no discriminator, even when the type is a polymorphic base of its own — then under the separate id `{Derived}Direct`); the base itself listed without a value adds no alternative, the base branch covers it.

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `schema.allOf` | 3.0 | only as the `$ref` wrapper (inheritance is flattened: base properties are copied, no `allOf` to a base schema) | written | = | = | — | — | supported | — |
| `schema.anyOf` | 3.0 | nullable reference → `anyOf: [$ref, {type: null}]` | 3.0 null branch as `{enum: [null], nullable: true}` (lib) | written | written | — | — | supported | — |
| `schema.anyOf` from `[JsonNumberHandling]` | 3.0 | see §24.3 (number handling) | see §24.3 | = | = | — | — | supported | — |
| `schema.oneOf` + `discriminator` (abstract base or interface, all derived types have a value) | 3.0 | abstract base or interface; every `[JsonDerivedType]` / `[SwaggerSubType]` has a discriminator value | `oneOf` of the variants + `discriminator {propertyName, mapping}`; no base branch, no `defaultMapping` | = | = | — | `component.no-unused` follows `oneOf` and `discriminator.mapping` | supported | — |
| `schema.anyOf` (some derived types have no discriminator value) | 3.0 | `[JsonDerivedType(typeof(D))]` without a discriminator, `[SwaggerSubType]` without `DiscriminatorValue` | `anyOf` of the alternatives (plus the direct-use base schema when the base is concrete; this row takes precedence over the concrete-base row), **no `discriminator` object**; variants keep their `const` / one-element `enum`; alternatives without a value have no discriminator property in `properties` or `required` | = | = | "variants without a discriminator are distinguishable only by structure" (code `polymorphism.anyof-without-discriminator`, located at the union component; none when the union is reachable only from excluded paths) | `component.no-unused` follows `anyOf` | supported | — |
| `schema.oneOf` (concrete base, declared among the derived types with a value or not) | 3.0 | concrete base; on the wire a base without a value is written without the discriminator; on read a missing discriminator gives the base | `oneOf` of the variants (including the base variant with its value when the base is declared with one) and the base branch; **no `discriminator` object** | = | `oneOf` as in 3.0/3.1 + `discriminator {propertyName, mapping, defaultMapping: <base branch>}` | 3.0, 3.1: one warning about the discriminator (code `polymorphism.discriminator-not-expressible`, located at the union component; a discriminator with an optional property cannot be expressed in these versions) | 3.2: `discriminator.default-mapping-when-optional` | supported | — |
| `schema.oneOf` versus `anyOf` (general rule) | 3.0 | any polymorphic base | `oneOf` only when the alternatives are mutually exclusive (each variant has its own `const`, and the base branch rejects every mapped value); otherwise `anyOf`. A `discriminator` object is written only when every alternative of the union has a discriminator value, or (3.2) the only alternative without a value is the base branch that `defaultMapping` points to | = | = | — | `discriminator.default-mapping-when-optional` (3.2) always passes on the tool's own output | supported | — |
| polymorphism from Swashbuckle attributes | 3.0 | `[SwaggerDiscriminator]` + `[SwaggerSubType]`: an alternative source of the same data. When STJ attributes are also present, the STJ attributes are the source of truth (they define the wire) | as the STJ rows above | = | = | STJ and Swashbuckle attributes disagree → warning | — | bug: ignored (verified); stage 1 | stage-1 |
| `schema.not` | 3.0 | `[DeniedValues(…)]` → `not: {enum: [...]}` with the schema's JSON type. The constraint is a sibling keyword of the same schema (applies together with the others, AND); the type's own `enum` is neither removed nor intersected. | `not: {enum: [...]}` | = | = | values that cannot be converted to the schema's JSON type → warning, constraint not written | — | supported | — |
| `schema.if` / `then` / `else` | 3.1 | — (conditional validation lives in executable code: `IValidatableObject`, FluentValidation) | `x-jsonschema-if/then/else` (lib) | written | written | — | — | not planned: would duplicate executable validation | — |
| `schema.dependentSchemas` | 3.1 | — | `x-jsonschema-dependentSchemas` (lib) | written | written | — | — | not planned: would duplicate executable validation | — |
| `schema.prefixItems` | 3.1 | — | — | — | — | — | — | not planned: absent from the Microsoft.OpenApi 3.10.2 model (the `UnrecognizedKeywords` workaround writes a literal `unrecognizedKeywords:` key); STJ writes `ValueTuple` as `{}` | — |
| `schema.items` | 3.0 | arrays, `List<T>` and other collection interfaces, sets, non-generic `IEnumerable` (`items: {}`) | written | = | = | — | `schema.array-items` (a code-generation policy: a missing `items` is allowed by JSON Schema 2020-12; documented as policy, not as a MUST) | supported | — |
| `schema.items` for `IAsyncEnumerable<T>` | 3.0 | see §14 | `items: T` | = | = | — | `schema.array-items` | bug: fake wrapper object; stage 1 | stage-1 |
| `schema.contains` | 3.1 | — | `x-jsonschema-contains` (lib) | written | written | — | — | not planned: no code source | — |
| `schema.properties` | 3.0 | public readable instance properties (incl. inherited), names by `[JsonPropertyName]` → naming policy (`--naming-policy` or Roslyn `PropertyNamingPolicy`; camelCase exactly as System.Text.Json's `JsonNamingPolicy.CamelCase`, a leading acronym lowered: `IOStatus` → `ioStatus`); `[JsonIgnore]` / `[JsonIgnore(Condition = Always)]` exclude | written | = | = | — | `schema.property-description`, `schema.no-required-undefined` | supported | — |
| `schema.properties` with `[JsonExtensionData]` | 3.0 | `[JsonExtensionData]` on a property of a shape STJ 10 accepts: `IDictionary<string, object>`, `IDictionary<string, JsonElement>` and their implementations, `JsonObject`. An extension property with `[JsonIgnore]` is not counted; one inherited from a base type is. | the property is absent from `properties` and `required`; the object gets `additionalProperties` that allows any JSON value | = | = | — | — | supported | — |
| `[JsonExtensionData]` invalid declarations | 3.0 | another value type, another key type, several extension properties in one type, an extension property bound to a constructor parameter, or a combination with `[JsonUnmappedMemberHandling(Disallow)]` on the same type | — | — | — | extraction error naming the type and the property | — | supported | — |
| `schema.patternProperties` | 3.1 | — | `x-jsonschema-patternProperties` (lib) | written | written | — | — | not planned: no code source | — |
| `schema.additionalProperties` (schema) | 3.0 | `Dictionary<K,V>`, `IDictionary`, `IReadOnlyDictionary`, `SortedDictionary`, `SortedList` and implementing types → schema of `V`; BCL object-like JSON types → `{}` | written | = | = | — | `schema.additional-properties-explicit` (off by default) | supported | — |
| `schema.additionalProperties: false` | 3.0 | `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` on the type | written | = | = | — | `schema.additional-properties-explicit` | supported | — |
| `schema.additionalProperties` with global `Disallow` and extension data | 3.0 | global unmapped-member handling `Disallow` (serializer options) on a type that has extension data | extension data wins: `additionalProperties` allows any JSON value (as the serializer behaves) | = | = | — | — | supported | — |
| `schema.propertyNames` for `Guid` keys | 3.1 | key type of `Dictionary<TKey,V>` / `IDictionary<TKey,V>` / `IReadOnlyDictionary<TKey,V>` (by the actual `IDictionary<TKey,TValue>` implementation) | not written (form chosen by version, as in 0.16) | `propertyNames: {type: string, format: uuid}` (STJ writes and accepts only the `D` format) | = | — (none in 3.0 either) | `component.no-unused` follows `propertyNames` | supported | — |
| `schema.propertyNames` for integer keys | 3.1 | signed integer key types; unsigned integer key types | not written (form chosen by version) | signed: `pattern` = optional `+` or `-` sign, then digits; unsigned: optional `+`, then digits (leading zeros allowed, as STJ accepts `+1` and `01`; for unsigned keys STJ 10 rejects `+`, so that pattern is wider than the contract); the type's range is not clamped by the pattern — the schema is wider than the contract, documented approximation | = | — | as above | supported | — |
| `schema.propertyNames` not written | 3.1 | `string` keys; enum keys (STJ accepts names case-insensitively and numbers, and writes numbers for undefined values); a key type with an unknown converter | not written | not written | not written | unknown key converter → warning | — | supported | — |
| `schema.unevaluatedItems` | 3.1 | — | — | — | — | — | — | not planned: absent from the library model; no source | — |
| `schema.unevaluatedProperties` | 3.1 | — (`Disallow` on a type composed with `allOf` would be a source, but schemas are flat) | `x-jsonschema-unevaluatedProperties` (lib) | written | written | — | — | not planned: revisit if polymorphism introduces `allOf` composition | — |

### 24.3 Validation keywords

**Number handling.** `[JsonNumberHandling]` on a property overrides the attribute on the type, which overrides the global serializer option. The schema is the **union of what STJ 10 writes and what it accepts**. A "numeric string" is a string whose pattern follows the grammar STJ accepts, never narrower: for integers an optional sign and digits (leading zeros allowed); for `float` / `double` / `decimal` / `Half` an optional sign, digits with an optional fractional part (a leading or trailing dot is allowed: `.5`, `1.`), leading zeros, an optional exponent (`1e2`, `1E+2`); no whitespace — except for `Half`, which STJ 10 parses with surrounding white space and group separators (`" 12"`, `"1,5"`, measured), so its numeric string allows them. For `float` / `double` / `Half`, **any** of `AllowReadingFromString`, `WriteAsString`, `AllowNamedFloatingPointLiterals` adds the named-literal branch `enum: ["NaN", "Infinity", "-Infinity"]` (STJ 10 reads them from a string with `AllowReadingFromString` and writes them as strings with `WriteAsString`); combined with a string flag that gives three branches. Range constraints (`[Range]`) and examples go only on the numeric branch; the string branch carries no range (the schema is wider than the server there — a documented limitation, no warning). Nullability is expressed by the version's rules on the whole `anyOf`; for numeric collections the rule applies to `items`.

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `schema.type` | 3.0 (array form since 3.1) | CLR type map: string/char → `string`; bool → `boolean`; integer types → `integer`; float/double/decimal → `number`; date/time/Guid/Uri → `string`; collections → `array`; dictionaries and complex types → `object`; `Nullable<T>` and NRT `?` add `null`; converter registry overrides | `null` → `nullable: true`; several non-null types → `anyOf` (lib) | `type: [T, "null"]` | = | — | `parameter.schema-type`, `schema.typed-enum` | supported | — |
| `schema.type` for `System.Object` | 3.0 | `object` / `dynamic` property | unconstrained schema `{}` | = | = | — | — | supported | — |
| number handling: none | 3.0 | no `[JsonNumberHandling]` and no global option | numeric schema | = | = | — | — | supported | — |
| number handling: `Strict` attribute over a global option | 3.0 | `[JsonNumberHandling(Strict)]` on a property or type while the global option sets string flags | numeric schema (the attribute wins) | = | = | — | — | supported | — |
| number handling: `AllowReadingFromString` | 3.0 | `[JsonNumberHandling]` on a property or type; global option | `anyOf: [numeric schema, numeric string]`; `float` / `double` / `Half`: plus the named-literal branch | = | = | — | `schema.property-constraints` (understands composite numeric schemas) | supported | — |
| number handling: `WriteAsString` | 3.0 | as above | `anyOf: [numeric schema, numeric string]` (writes a string, accepts only a number); `float` / `double` / `Half`: plus the named-literal branch | = | = | — | as above | supported | — |
| number handling: `WriteAsString` + `AllowReadingFromString` | 3.0 | as above | `anyOf: [numeric schema, numeric string]`; `float` / `double` / `Half`: plus the named-literal branch | = | = | — | as above | supported | — |
| number handling: `AllowNamedFloatingPointLiterals` | 3.0 | as above; only `float` / `double` / `Half` | `anyOf: [numeric schema, {enum: ["NaN", "Infinity", "-Infinity"]}]`; combined with a string flag: three branches | = | = | — | as above | supported | — |
| `schema.enum` (enum types) | 3.0 | enum fields: integer values, or names when `--enum-as-string`, `JsonStringEnumConverter` (type, property, global) or a registry converter applies | written | = | = | — | `schema.enum-filled`, `schema.no-duplicate-enum`, `schema.typed-enum` | supported | — |
| `schema.enum` wire names | 3.0 | string-serialized enums: the names on the wire **per the known converter in effect** (the converter on the property, otherwise on the type, otherwise the global one): STJ `JsonStringEnumConverter` reads `[JsonStringEnumMemberName]` (STJ ignores `[EnumMember]`); Newtonsoft `StringEnumConverter` reads `[EnumMember(Value)]`; only the attribute the converter reads applies — with both attributes present, the other one does not change the wire; without an attribute, the converter's naming policy, otherwise the member name. A naming policy comes only with a global registration (`[JsonConverter]` cannot pass one): `new JsonStringEnumConverter(JsonNamingPolicy.CamelCase | SnakeCaseLower | SnakeCaseUpper | KebabCaseLower | KebabCaseUpper)` (also `JsonStringEnumConverter<T>`), Newtonsoft `new StringEnumConverter(camelCaseText: true)` or a `NamingStrategy` (`CamelCaseNamingStrategy`, `SnakeCaseNamingStrategy`, `KebabCaseNamingStrategy`; constructor argument, `typeof`, or `NamingStrategy =` initializer); it applies to members the attribute does not rename (camelCase as System.Text.Json's `JsonNamingPolicy.CamelCase`). A policy expression that cannot be read statically leaves the member names, with a warning (code `json-options.unknown-converter-naming-policy`). `--enum-as-string` counts as System.Text.Json's converter; the community `JsonStringEnumMemberConverter` keeps the member names. The global converter in effect is the first registered one that writes enums as strings. A converter on a `Nullable<TEnum>` property converts the enum (both serializers lift it): string values in the version's nullable form. `default`, `[AllowedValues]` and `[DeniedValues]` of such a property use the same wire names. A path, query, header or form parameter is converted by model binding (the type converter), which reads member names, not JSON names: its `enum` lists the member names. Numeric mode is unchanged. | `enum` values are the wire names; `x-enum-varnames` and `x-enum-descriptions` stay aligned with the CLR members; the auto-description list shows the wire names | = | = | — | `schema.no-duplicate-enum`, `schema.typed-enum` | supported | — |
| `schema.enum` for `long`/`ulong`-backed enums | 3.0 | underlying values | values without loss; `type` / `format` the same as for a property of the same underlying type (existing form) | = | = | — | — | supported | — |
| `schema.enum` from `[AllowedValues(...)]` | 3.0 | `AllowedValuesAttribute`. The attribute's constraint and the schema's own constraints apply together (AND): where the keys differ, sibling keywords of the same schema; where they coincide (`[AllowedValues]` on an enum type that has its own `enum`), a composition of two schemas, with no precomputed intersection; the type's own `enum` is never removed On a number-handling union (`anyOf` [number, numeric string, …]) allowed values constrain the numeric branch only (the numeric-string branch keeps its grammar, since STJ also reads `"+1"`, `"01"`) and drop the NaN / Infinity branch; denied values exclude the numbers on the numeric branch and their written string forms on the numeric-string branch; values are checked against the numeric type. | `enum` of the values with the schema's JSON type; a single value → `enum: [v]` (lossless) | `enum`; a single value → `const` | = | values that cannot be converted to the schema's JSON type → warning, constraint not written | —| supported | — |
| `schema.const` | 3.1 | string discriminator value on a **variant** schema (an integer value is a one-element `enum` in every version: the library writes `const` only as a string) (never on the component used directly, §24.7); a single `[AllowedValues]` value. The SSE `event` literal from the `eventType` argument is deferred. | `enum: [v]` (lossless) | `const` | `const` | — (lossless) | — | supported (the SSE `event` literal: deferred) | — |
| `schema.multipleOf` | 3.0 | — | never | never | never | — | — | not planned: no standard source | — |
| `schema.maximum` / `schema.minimum` | 3.0 | `[Range(int,int)]`, `[Range(double,double)]` without exclusive flags, on properties and positional-record parameters | written | = | = | — | `schema.property-constraints` | supported | — |
| `schema.maximum` / `minimum` from `[Range(Type, string, string)]` | 3.0 | e.g. `[Range(typeof(decimal), "0.01", "999.99")]`; bounds parsed from the string arguments in the invariant culture (numeric operands: integer types, `decimal`, `double`, `float`, `Half`; a `float` / `Half` bound is printed as that type); decimal values keep their precision (never pass through binary floating point) | `minimum` / `maximum` with the exact values (numeric schemas only) | = | = | operand type that is not a numeric JSON type (e.g. `DateTime`) → no numeric constraint, warning | `schema.property-constraints` (planned: understands all `Range` overloads) | supported | — |
| `schema.exclusiveMaximum` / `exclusiveMinimum` | 3.0 boolean, number since 3.1 | `RangeAttribute.MinimumIsExclusive` / `MaximumIsExclusive` (.NET 8+), every `Range` overload; each bound inclusive or exclusive independently | `minimum: <n>` + `exclusiveMinimum: true` (same for maximum) | `exclusiveMinimum: <n>` without `minimum` (same for maximum) | = | — (lossless) | `schema.property-constraints` (planned: accepts exclusive bounds, nullable and composite numeric schemas, no false positives) | supported | — |
| invalid `[Range]` | 3.0 | only declarations that `RangeAttribute` (.NET 10) itself rejects: lower bound above upper bound; equal bounds with an exclusive side; an unparsable string with a numeric operand type; a NaN bound where the attribute throws | — | — | — | extraction error naming the type and the member | — | supported | — |
| `[Range]` bound not representable in JSON | 3.0 | a valid declaration with an infinite bound, or a NaN bound the attribute accepts | that bound not written; the other bound written as usual | = | = | warning | — | supported | — |
| `schema.maxLength` / `minLength` | 3.0 | `[StringLength(max, MinimumLength)]`, `[MaxLength]`, `[MinLength]` on non-array schemas | written | = | = | — | `schema.property-constraints` | supported | — |
| `[Length(min, max)]` | 3.0 | `LengthAttribute` (.NET 8+) on a property; a minimum of 0 writes no minimum; a declaration `LengthAttribute` rejects (negative minimum, maximum below minimum) writes nothing; next to `[MinLength]` / `[MaxLength]` / `[StringLength]` each side keeps the tighter bound (highest minimum, lowest maximum), contradicting bounds are written as they result; the same on action parameters | strings: `minLength` / `maxLength`; collections: `minItems` / `maxItems`; dictionaries: `minProperties` / `maxProperties` | = | = | — | planned: `schema.property-constraints` handles `[Length]` (the current rule does not) | supported | — |
| `schema.minLength: 1` from `[Required]` on strings | 3.0 | `[Required]` (`AllowEmptyStrings = false`) | not emitted | = | = | — | — | not planned: `[Required]` means presence, not length; an empty string is a valid response value | — |
| `schema.pattern` | 3.0 | `[RegularExpression]` | written | = | = | — | `schema.property-constraints` | supported | — |
| `schema.maxItems` / `minItems` | 3.0 | `[MaxLength]` / `[MinLength]` on array schemas | written | = | = | — | `schema.property-constraints` | supported | — |
| `schema.uniqueItems` | 3.0 | `HashSet<T>`, `ISet<T>`, `IReadOnlySet<T>`, `SortedSet<T>` | written | = | = | — | — | supported | — |
| `schema.maxContains` / `minContains` | 3.1 | — | `x-jsonschema-maxContains/minContains` (lib) | written | written | — | — | not planned: no code source | — |
| `schema.maxProperties` / `minProperties` | 3.0 | `[MinLength]` / `[MaxLength]` / `[Length]` on a dictionary | `minProperties` / `maxProperties` | = | = | — | `schema.property-constraints` | supported | — |
| `schema.required` | 3.0 | `[Required]`, `[JsonRequired]`, C# `required` (`RequiredMemberAttribute`), NRT non-nullable reference types; not when global `DefaultIgnoreCondition = WhenWritingNull` and nullable | written | = | = | — | `schema.required-consistency`, `schema.no-required-undefined` | supported | — |
| `schema.dependentRequired` | 3.1 | — | dropped (lib) | written | written | — | — | not planned: would duplicate executable validation | — |

### 24.4 Format

**Format priority** (one winner): `[SwaggerSchema(Format)]` → profile attribute (`[EmailAddress]`, `[Url]`, `[Phone]`) → `[DataType]` → format from the CLR type. The `schema.property-format` rule accepts exactly this table and priority and never requires a format the table does not promise.

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `format: int32` / `int64` | 3.0 | `byte`, `sbyte`, `short`, `ushort`, `int` → `int32`; `uint`, `long` → `int64` (`uint` values exceed the `int32` range); `ulong` → no `format` (its values exceed `int64`), also for enums backed by `ulong` | written | = | = | — | `schema.property-format` | supported | — |
| `format: float` / `double` | 3.0 | `float` → `float`; `double`, `decimal` → `double` | written | = | = | — | `schema.property-format` | supported | — |
| `format: date-time` / `date` / `time` / `duration` | 3.0 | `DateTime`, `DateTimeOffset` → `date-time`; `DateOnly` → `date`; `TimeOnly` → `time`; `TimeSpan` → `duration`; registry converters (`IsoDateTimeConverter`, `UnixDateTimeConverter`, …) | written | = | = | — | `schema.property-format` | supported | — |
| `format: uuid` / `uri` | 3.0 | `Guid` → `uuid`; `Uri` and `[Url]` → `uri` | written | = | = | — | `schema.property-format` | supported | — |
| `format: email` / `phone` | 3.0 | `[EmailAddress]`, `[Phone]`, by the format priority above (over `[DataType]` and the type's format) | written | = | = | — | — | supported | — |
| `format` priority | 3.0 | `[SwaggerSchema(Format)]`, profile attributes, `[DataType]`, CLR type together on one member | the single winner by the priority above; on a reference-typed property on the `allOf` wrapper; with number handling on the numeric branch, replacing the type's format there | = | = | — | planned: `schema.property-format` accepts the priority | supported | — |
| base64 data: `byte[]` | 3.0 (`format: byte` is not in the 3.1 format table) | `byte[]`; a known well-known converter on the property takes precedence (existing converter registry) | `type: string, format: byte` | `type: string, contentEncoding: base64`, no `format` | = | — (form chosen by version; `x-jsonschema-contentEncoding` never appears in 3.0) | `schema.property-format` | supported | — |
| base64 data: `[Base64String]` | 3.0 | `Base64StringAttribute` on `string`; nullable variants keep the version's nullable form | `type: string, format: byte` | `type: string, contentEncoding: base64`, no `format` | = | — (form chosen by version) | — | supported | — |
| `format: binary` | 3.0 | `IFormFile`, `Stream`, MVC `FileResult` and derived types, `IFileHttpResult` (see §14) | `type: string, format: binary` | = (never changed to `contentMediaType`) | = | — | — | supported | — |
| `format` from `[DataType(...)]` | 3.0 | `DataTypeAttribute` (the `[DataType("name")]` constructor gives no format): `DateTime` → `date-time`; `Date` → `date`; `Time` → `time`; `Duration` → `duration`; `EmailAddress` → `email`; `Password` → `password`; `Url`, `ImageUrl` → `uri`; `PhoneNumber` → `phone`; `Upload` → `binary`; `Custom`, `Currency`, `Text`, `Html`, `MultilineText`, `CreditCard`, `PostalCode` — no format. A member without a format (listed as "no format", or not named here) does not take part in the priority: the CLR-type format is kept; only a winner that sets a format suppresses the others. | `format` from the table | = | = | — | planned: `schema.property-format` accepts this table | supported | — |
| `format` from `[SwaggerSchema(Format = …)]` | 3.0 | `SwaggerSchemaAttribute.Format` on a property | written; wins over every derived format | = | = | — | planned: `schema.property-format` accepts it | supported | — |

### 24.5 Annotation keywords

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `schema.title` | 3.0 | `[SwaggerSchema(Title = …)]` on a property (on a reference-typed property: the `allOf` wrapper) | `title` | = | = | — | — | supported | — |
| `schema.description` (type) | 3.0 | `[SwaggerSchema(Description)]` → `[Description]` → XML `<summary>` (incl. open-generic and framework ref-pack XML); enums: auto markdown list (disable with `--no-enum-auto-description`) | written | = | = | — | `schema.description`, `enum.type-description` | supported | — |
| `schema.description` (property) | 3.0 | `[SwaggerSchema(Description)]` (named or constructor argument) → `[Description]` → `[Display(Description)]` → XML `<summary>` / record `<param>`; converter or BCL default text only when none of the attributes gives one | written | = | = | — | `schema.property-description` | supported | — |
| `schema.description` (property) from `[Display(Description = …)]` | 3.0 | `DisplayAttribute.Description`; priority below `[SwaggerSchema(Description)]` and `[Description]`, above XML `<summary>`; a `[Display]` with `ResourceType` names a resource key and is not used | `description` | = | = | — | `schema.property-description` | supported | — |
| `schema.default` | 3.0 | `[DefaultValue(x)]` on properties and parameters, one converter: numeric literals as numbers, `bool` as boolean, strings and other values as strings | written | = | = | — | `parameter.optional-has-default` (parameters) | supported | — |
| `schema.default` from `[DefaultValue(Type, string)]` | 3.0 | e.g. `[DefaultValue(typeof(decimal), "1.5")]`, on properties and parameters | the value converted to the type (invariant culture), with the schema's JSON type: numbers, `bool`, strings, `Guid`; `DateTime` / `DateTimeOffset` / `DateOnly` / `TimeOnly` / `TimeSpan` as System.Text.Json writes them; an enum as its number or, for an enum written as strings, its name; another type → warning, no `default` | = | = | value that does not convert → warning, no `default` | — | supported (code `schema.default-not-convertible`) | — |
| `schema.deprecated` | 3.0 | `[Obsolete]` on a property, DTO type or enum type | written | = | = | — | — | supported | — |
| `schema.readOnly` | 3.0 | effective value by priority `[SwaggerSchema(ReadOnly = …)]` → `[ReadOnly(…)]` on a property; an explicit `false` from the higher-priority source is honoured | `readOnly: true` when the effective value is true (on a reference-typed property: the `allOf` wrapper) | = | = | effective `readOnly` and `writeOnly` both true → extraction error (OAS forbids both) | — | supported | — |
| `schema.writeOnly` | 3.0 | effective value from `[SwaggerSchema(WriteOnly = …)]` on a property; an explicit `false` is honoured | `writeOnly: true` when the effective value is true | = | = | as above | — | supported | — |
| `schema.examples` / `example` (property) | 3.1 (`example` in 3.0) | XML `<example>` on a DTO property, including an inherited property and a positional-record parameter (`<param name="X" example="…">` on the record, since the compiler copies only the `<param>` text to the property); written whether or not `<summary>` is present. The text is data: white space inside a line is kept, only the XML formatting goes (each line trimmed at its ends, blank leading and trailing lines dropped, lines joined with a line feed). Parsed by the effective schema: integer/number → number, boolean → boolean, `null` → JSON null (nullable schema only), object/array → JSON parse, string → string as is. On a reference-type property the example goes on the `allOf` wrapper with the other siblings and never changes the shared component schema. With number handling, only on the numeric branch (a `null` example of a nullable union goes on the union). The text `null` is JSON null for a nullable schema and is not written for any other schema, a string one included; an integer must be integral; an object or an array schema takes only JSON of that kind; a schema without a type takes any JSON. | `example: v` | `examples: [v]` | = | value that does not parse for a non-string schema, or `null` for a non-nullable schema → not written, warning with element and value (code `schema.example-not-parsable`); several `<example>` elements → the first is used, warning (code `schema.example-multiple`) | — | supported | — |
| `schema.examples` / `example` (type) | 3.1 (`example` in 3.0) | XML `<example>` on a DTO type; parsed as above | on the component schema of the type — for a polymorphic base its union component; variants and the direct-use object of a base do not copy it: `example: v` | `examples: [v]` | = | as above | — | supported | — |

### 24.6 Content keywords

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `schema.contentEncoding` | 3.1 | `byte[]`, `[Base64String] string` → `base64` (§24.4) | never (form chosen by version: `format: byte`) | written | written | — | — | supported | — |
| `schema.contentMediaType` | 3.1 | SSE `data` → `application/json` (not for `T = string` / `byte[]`) | only inside `x-oai-itemSchema`, as `x-jsonschema-contentMediaType` (lib) | inside `x-oai-itemSchema` | inside `itemSchema` | covered by the `itemSchema` DW (3.0, 3.1) | — | stage 1 | stage-1 |
| `schema.contentSchema` | 3.1 | SSE `data` → schema of `T` (HTTP JSON options; not for `T = string` / `byte[]`) | only inside `x-oai-itemSchema`, as `x-jsonschema-contentSchema` (lib) | inside `x-oai-itemSchema` | inside `itemSchema` | covered by the `itemSchema` DW (3.0, 3.1) | planned: `component.no-unused` follows `contentSchema` | stage 1 | stage-1 |

### 24.7 OAS-specific Schema fields

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `schema.discriminator` | 3.0 | see §24.2 and §25 | written only under the conditions of §24.2 | = | = | — | — | supported | — |
| variant schema (alternative with a discriminator value) | 3.0 | `[JsonDerivedType(typeof(D), value)]` / `[SwaggerSubType(…, DiscriminatorValue = …)]` | a schema separate from the direct-use schema of the same type; the discriminator property is in `properties` and `required` with a one-element `enum` of the value, with the value's JSON type (string or integer; an integer value never becomes a string) | the same with `const` for a string value; an integer value stays a one-element `enum` (Microsoft.OpenApi 3.10.2 models `const` as a string and would write `"1"`) | = | — | — | supported | — |
| direct-use schema of a derived type; alternative without a value | 3.0 | the same type used directly (not polymorphically); a derived type declared without a discriminator value | no discriminator property — STJ does not write it there; the discriminator `const` / `enum` never goes on these schemas | = | = | — | — | supported | — |
| `schema.xml` | 3.0 | — (`[XmlAttribute]`, `[XmlElement]`, `[XmlArray]`, `[XmlText]`, `[XmlRoot]` exist but are not read) | never | never | never | — | — | deferred: the XML Object is not built at all; deferred together with `nodeType` | — |
| `schema.externalDocs` | 3.0 | — | never | never | never | — | — | not planned: no standard source | — |
| `schema.example` | 3.0 (deprecated since 3.1) | XML `<example>` (§24.5) | `example` (form chosen by version: the single example) | not written (`examples` instead) | = | — | — | supported | — |
| `schema.nullable` | 3.0, removed in 3.1 | `Nullable<T>`, NRT `?` (reference types without NRT data count as nullable) | `nullable: true` (lib, from a `null` type) | `type: [T, "null"]` | = | — | `schema.required-consistency` | supported | — |
| `schema.readOnly` / `writeOnly` | 3.0 | see §24.5 | see §24.5 | = | = | see §24.5 | — | supported | — |
| `schema.deprecated` | 3.0 | see §24.5 | — | — | — | — | — | supported | — |
| `schema.x-enum-varnames` | — (extension) | enum member names (CLR), parallel to `enum`; disable with `--no-enum-varnames` | written | = | = | — | — | supported | — |
| `schema.x-enum-varnames` with wire names | — (extension) | string-serialized enum whose `enum` values become wire names (§24.3), the converter on the type, the property or global | stays the CLR member names, same order and length as `enum`; `x-enum-descriptions` likewise | = | = | — | — | supported | — |
| `schema.x-enum-descriptions` | — (extension) | XML `<summary>` → `[Description]` per enum value | written | = | = | — | `enum.value-description` | supported | — |

## 25. Discriminator Object

The `discriminator` object is written only when every alternative of the union has a discriminator value, or (3.2) the only alternative without a value is the base branch that `defaultMapping` points to (§24.2). Output produced this way always passes `discriminator.default-mapping-when-optional`. A concrete base never has a `discriminator` object in 3.0/3.1, so `defaultMapping` is never degraded.

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `discriminator.propertyName` | 3.0 | `[JsonPolymorphic(TypeDiscriminatorPropertyName)]` (default `$type`); `[SwaggerDiscriminator(name)]`; STJ wins when both are present | written | = | = | STJ and Swashbuckle disagree → warning (code `polymorphism.source-disagreement`, located at the union component); a derived-type property with the same JSON name as the discriminator → extraction error (the serializer rejects such a contract) | — | supported | — |
| `discriminator.mapping` | 3.0 | `[JsonDerivedType(typeof(X), "x")]` / `[JsonDerivedType(typeof(X), 1)]`; `[SwaggerSubType(typeof(X), DiscriminatorValue = "x")]`. Keys are the string form of the discriminator value; an integer value keeps the JSON number type in the variant's `const` / `enum`. A missing discriminator value is never invented. | written | = | = | — | `component.no-unused` follows `discriminator.mapping` | supported | — |
| `discriminator.defaultMapping` | 3.2 | concrete base (declared among the derived types with a value or not), when the `discriminator` object is written (no derived types without a value); `UnknownDerivedTypeHandling` (serialization of an unknown CLR type) has no effect | not written (no `discriminator` object for a concrete base, §24.2) | not written (as 3.0) | `defaultMapping` → the base branch — not the union wrapper and not the base variant with `const`; abstract base / interface: never | 3.0, 3.1: none of its own (the single discriminator warning of §24.2 covers it) | `discriminator.default-mapping-when-optional` (planned: 3.2, error, on by default; required-ness evaluated through composition and references) | supported | — |
| `discriminator.x-*` | 3.0 | — | never | never | never | — | — | not planned: no code source | — |

## 26. XML Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `xml.name` | 3.0 | — (`[XmlElement("n")]`, `[XmlAttribute("n")]`, `[XmlRoot]`) | never | never | never | — | — | deferred: XML serialization support (the XML Object is not built at all) | — |
| `xml.namespace` | 3.0 | — (`Namespace` on `System.Xml.Serialization` attributes) | never | never | never | — | — | deferred: as above | — |
| `xml.prefix` | 3.0 | — | never | never | never | — | — | deferred: as above | — |
| `xml.attribute` | 3.0 (deprecated in 3.2) | — (`[XmlAttribute]`) | library writes it from `nodeType: attribute` | = | `nodeType` instead | — | — | deferred: as above | — |
| `xml.wrapped` | 3.0 (deprecated in 3.2) | — (`[XmlArray]`) | library writes it from `nodeType: element` on arrays | = | `nodeType` instead | — | — | deferred: as above | — |
| `xml.nodeType` | 3.2 | — (`[XmlAttribute]` → `attribute`, `[XmlText]` → `text`, `[XmlElement]` → `element`) | `attribute: true` / `wrapped: true`; `text`, `cdata`, `none` are dropped (lib) | = | `nodeType` | — | — | deferred: as above | — |

## 27. Security Scheme Object

OAuth2 and OpenID Connect declarations: missing required data in a fully known (literal) declaration is an **extraction error** (such a document cannot be serialized, and fails at run time too); a value that cannot be resolved statically (a variable, a call) → the scheme is omitted, with a warning. The CLI never crashes on these declarations.

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `securityScheme.type: apiKey` / `http` | 3.0 | Roslyn `AddSecurityDefinition(name, new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey \| Http, … })`; `AddJwtBearer(...)` → `http` / `bearer` / `JWT` (name `Bearer` unless given) | written | = | = | `Warning: AddSecurityDefinition call with non-literal name — skipped.` | `security.scheme-defined` | supported | — |
| `securityScheme.type: oauth2` | 3.0 | `Type = SecuritySchemeType.OAuth2` with `Flows = new OpenApiOAuthFlows { … }` | `oauth2` with its flows (§28, §29) | = | = | literal declaration without flows → extraction error; non-literal value → scheme omitted, warning | `security.scheme-defined` | bug: `Flows` is not parsed and the library throws `The 'flows' property is required for oauth2 security schemes.`, the CLI fails with exit 2 (verified); stage 1 | stage-1 |
| `securityScheme.type: openIdConnect` | 3.0 | `Type = SecuritySchemeType.OpenIdConnect` with `OpenIdConnectUrl = new Uri("…")` | `openIdConnect` with `openIdConnectUrl` | = | = | literal declaration without the URL → extraction error; non-literal value → scheme omitted, warning | `security.scheme-defined` | bug: `OpenIdConnectUrl` is not parsed and the library throws `The 'openIdConnectUrl' property is required for openIdConnect security schemes.`, the CLI fails with exit 2 (verified); stage 1 | stage-1 |
| `securityScheme.type: mutualTLS` | 3.1 | `Type = SecuritySchemeType.MutualTLS` in the security scheme initializer in `Program.cs` (exists in Microsoft.OpenApi 2.7.x and 3.x) | omitted (form chosen by the tool, the library would throw); the requirements referencing it are removed by the existing mechanism | `mutualTLS` in `components.securitySchemes` | `mutualTLS` | DW (3.0): names the scheme and the change to the auth contract (which requirements were simplified or removed); the CLI does not fail | `security.scheme-defined` | bug: the definition is dropped silently (verified); stage 1 | stage-1 |
| `securityScheme.type: mutualTLS` from `AddCertificate()` | 3.1 | `AddCertificate()` authentication registration | not a source | = | = | — | — | deferred: not proven to be a reliable source of `mutualTLS` | — |
| `securityScheme.description` | 3.0 | `Description = "…"` literal; `AddJwtBearer` → fixed text | written | = | = | — | `security.scheme-description` | supported | — |
| `securityScheme.name` | 3.0 | `Name = "…"` literal | written | = | = | — | — | supported | — |
| `securityScheme.in` | 3.0 | `In = ParameterLocation.Query \| Header \| Cookie` | written | = | = | — | — | supported | — |
| `securityScheme.scheme` | 3.0 | `Scheme = "…"` literal | written | = | = | — | — | supported | — |
| `securityScheme.bearerFormat` | 3.0 | `BearerFormat = "…"` literal | written | = | = | — | — | supported | — |
| `securityScheme.flows` | 3.0 | `Flows = new OpenApiOAuthFlows { … }` | written: `implicit`, `password`, `clientCredentials`, `authorizationCode` with all URLs and scopes (§28); `deviceAuthorization` per its row | = | = | see the oauth2 row | — | bug: not parsed (see the oauth2 row); stage 1 | stage-1 |
| `securityScheme.openIdConnectUrl` | 3.0 | `OpenIdConnectUrl = new Uri("…")` | written | = | = | see the openIdConnect row | — | bug: not parsed (see the openIdConnect row); stage 1 | stage-1 |
| `securityScheme.oauth2MetadataUrl` | 3.2 | Roslyn `OAuth2MetadataUrl = …` (MS.OpenApi 3.x only) | `x-oai-oauth2-metadata-url` (lib) | `x-oai-oauth2-metadata-url` | `oauth2MetadataUrl` | DW (3.0, 3.1) | — | stage 1 | stage-1 |
| `securityScheme.deprecated` | 3.2 | Roslyn `Deprecated = true` (MS.OpenApi 3.x only) | `x-oai-deprecated` (lib) | `x-oai-deprecated` | `deprecated` | DW (3.0, 3.1) | — | stage 1 | stage-1 |

## 28. OAuth Flows Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `flows.implicit` | 3.0 | `Flows = new OpenApiOAuthFlows { Implicit = … }` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |
| `flows.password` | 3.0 | `Password = …` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |
| `flows.clientCredentials` | 3.0 | `ClientCredentials = …` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |
| `flows.authorizationCode` | 3.0 | `AuthorizationCode = …` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |
| `flows.deviceAuthorization` | 3.2 | Roslyn `DeviceAuthorization = …` (MS.OpenApi 3.x only) | `x-oai-deviceAuthorization` (lib) | `x-oai-deviceAuthorization` | `deviceAuthorization` | DW (3.0, 3.1): one per scheme, covering the flow's URLs and scopes | — | stage 1 | stage-1 |

## 29. OAuth Flow Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `flow.authorizationUrl` | 3.0 | `AuthorizationUrl = new Uri(…)` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |
| `flow.deviceAuthorizationUrl` | 3.2 | `DeviceAuthorizationUrl` (MS.OpenApi 3.x only) | inside `x-oai-deviceAuthorization` (lib) | = | `deviceAuthorizationUrl` | covered by the `deviceAuthorization` DW | — | stage 1 | stage-1 |
| `flow.tokenUrl` | 3.0 | `TokenUrl` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |
| `flow.refreshUrl` | 3.0 | `RefreshUrl` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |
| `flow.scopes` | 3.0 | `Scopes = new Dictionary<string,string> { … }` | written | = | = | — | — | bug: flows not parsed; stage 1 | stage-1 |

## 30. Security Requirement Object

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `security[].{name}` | 3.0 | document: one object per `AddSecurityRequirement` call (scheme name from `new OpenApiSecuritySchemeReference("x")` or `new OpenApiReference { Id = "x", Type = ReferenceType.SecurityScheme }`, literal or in-project `const`); operation: `[Authorize(AuthenticationSchemes)]`; `[AllowAnonymous]` → `security: []`; undeclared names omitted | written | = | = | `Warning: AddSecurityRequirement call with non-literal scheme name — skipped.`; undeclared-scheme warning (§1) | `security.scheme-defined`, `operation.security` | supported | — |
| `security[].{name}: [scopes]` (OAuth2/OIDC) | 3.0 | scopes listed in `AddSecurityRequirement` (`{ ref, ["read"] }`) | the scopes from the requirement | = | = | — | — | bug: always `[]`, scopes dropped (verified); stage 1 | stage-1 |
| `security[].{name}: [roles]` (non-OAuth schemes) | 3.1 | `[Authorize(Roles = "A,B")]`: roles inside one attribute are OR; several attributes (including controller plus action) are AND. Roles apply to the operation's **effective schemes**: the attribute's explicit `AuthenticationSchemes`, otherwise the document requirements the operation inherits (the requirement is then copied onto the operation together with the roles). `[AllowAnonymous]` → no requirements, as today. | values empty for non-OAuth schemes (form chosen by version) | each roles alternative is a separate requirement object (array = OR), the list inside an object is all required roles (AND); alternatives are combined with the existing scheme alternatives and written in full, with no limit on their number; an empty role list is never published for a scheme that has roles; `oauth2` / `openIdConnect` values stay scopes, roles are never written there | = | 3.0: warning about the lost roles; all versions: roles while an effective scheme is `oauth2` / `openIdConnect` → warning naming those schemes and the lost roles; no effective requirement at all → warning, roles not written, no scheme invented | `operation.security` | stage 1 | stage-1 |
| `[Authorize(Policy = …)]` / `[Authorize("name")]` | 3.0 | authorization policy name | not written | = | = | — | — | not planned: outside this stage; OpenAPI has no field for a policy | — |
| `security[].{uri}` key | 3.2 | — | never | never | never | — | — | not planned: one-document generator | — |

## 31. Webhooks

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `webhooks.{name}` (Path Item) | 3.1 | — (no ASP.NET Core or Swashbuckle source; planned own annotation on a contract interface) | dropped (lib) | written | written | DW (names of dropped webhooks) | operation rules and `operationId` uniqueness run over `webhooks` (standalone validate needs this regardless of generation) | stage 2 | stage-2 |
| `webhooks.{name}.{method}` (Operation) | 3.1 | same pipeline as actions (XML, `[FromBody]`, `[FromHeader]`, `[ProducesResponseType]`, `[SwaggerOperation]`) | dropped (lib) | written | written | DW | as above | stage 2 | stage-2 |

## 32. Specification extensions (`x-*`)

| Field | Since | Source in code | 3.0 output | 3.1 output | 3.2 output | Warning | --validate rule | Status | Tracking |
|---|---|---|---|---|---|---|---|---|---|
| `x-enum-varnames` (schema) | 3.0 | enum member names (CLR) | written | = | = | — | — | supported | — |
| `x-enum-descriptions` (schema) | 3.0 | per-value docs | written | = | = | — | `enum.value-description`, `enum.type-description` | supported | — |
| `x-api-version` (operation) | 3.0 | API versioning attributes | written | = | = | — | — | supported | — |
| `x-rate-limit-policy` / `x-rate-limit-disabled` (operation) | 3.0 | rate-limiting attributes | written | = | = | — | — | supported | — |
| `x-oai-*` / `x-oas-*` (downlevel names) | 3.1 (prefixes reserved) | written only by the library when a higher-version field is serialized to a lower version | see the per-field rows | see the per-field rows | not used | one DW per topmost moved node (see the per-field rows) | not planned: a rule rejecting the reserved prefixes (the tool's own 3.0 output relies on them) | supported (library behaviour) | — |
| `x-jsonschema-*` (downlevel JSON Schema keywords) | 3.0 | written only by the library for 2020-12 keywords in 3.0 | in the tool's output only inside `x-oai-itemSchema` (§14, §24.6) | not used | not used | covered by the enclosing `itemSchema` DW | — | supported (library behaviour) | — |
| user-defined `x-*` on any object | 3.0 | — (only reachable through runtime filters, which are out of scope) | never | never | never | — | — | not planned: no static source | — |

---

## Degradation summary

The behaviour of Microsoft.OpenApi 3.10.2 below was confirmed by serializing a document with every model property set into 3.0, 3.1 and 3.2, and by the research runs. "Tool action" is the stage-1 contract: what the tool does about each behaviour. "One DW" always means one DW for the topmost lost or moved node; its subtree gets no DW of its own.

### Target 3.0

| Library behaviour | Fields | Tool action |
|---|---|---|
| Lossless rewrite | `type` with `null` → `nullable: true`; several types → `anyOf`; `const` → `enum: [v]`; numeric `exclusiveMinimum/Maximum` → bound + boolean; `examples: [v]` → `example: v` (the tool sets a single example) | Nothing. No DW. |
| Rewritten as `x-oai-*` | `info.license.identifier`, `servers[].name`, `$self`, `response.summary`, `mediaType.itemSchema` / `itemEncoding` / `prefixEncoding`, `example.dataValue` / `serializedValue`, `securityScheme.deprecated` / `oauth2MetadataUrl`, `flows.deviceAuthorization` (+ `deviceAuthorizationUrl` inside), QUERY and other methods → `x-oai-additionalOperations` (the operation body stays in 3.2 form) | Keep the library form. One DW per topmost node and place: one per operation moved into `x-oai-additionalOperations`; one per media type for `itemSchema` (covering `contentMediaType` / `contentSchema` inside); one per scheme for the `deviceAuthorization` flow (covering its URLs and scopes); one per field otherwise. |
| Rewritten as `x-oas-*` | `tags[].summary` / `parent` / `kind` | Keep. One DW per field and place. |
| Rewritten as `x-jsonschema-*` | `contentEncoding`, `contentMediaType`, `contentSchema`, `propertyNames`, `patternProperties`, `contains`, `minContains`, `maxContains`, `if`/`then`/`else`, `dependentSchemas`, `unevaluatedProperties`, `$anchor` | `contentEncoding` and `propertyNames` are never generated for 3.0 (form chosen by version: `format: byte` / no key constraint; no DW). `contentMediaType` / `contentSchema` appear only inside `x-oai-itemSchema` and are covered by its DW. The rest are never generated. |
| Dropped silently | `webhooks`, `components.pathItems` (a `$ref` to it stays dangling), `components.mediaTypes`, `info.summary`, `jsonSchemaDialect`, `discriminator.defaultMapping`, Reference `summary`/`description`, `$ref` siblings on `OpenApiSchemaReference`, `$schema`, `$id`, `$comment`, `$defs`, `$dynamicRef`, `$dynamicAnchor`, `$vocabulary`, `dependentRequired`, `xml.nodeType` `text`/`cdata`/`none` | One DW for each of `info.summary`, `jsonSchemaDialect`, `webhooks` (when generated). `defaultMapping` is never generated for 3.0 (a concrete base has no `discriminator` object there). `$ref` siblings: the tool keeps the `allOf` wrapper (separate work). The others are never generated. |
| Throws | `securityScheme.type: mutualTLS` (`OpenApiException`); `in: querystring` (`InvalidOperationException`); `style: cookie` (`OpenApiException`) | `mutualTLS`: the scheme and the requirements referencing it are omitted, with a DW that states the changed auth contract. `querystring` and `cookie` are never generated. |
| Written although invalid in 3.0 | role lists in requirements for non-OAuth schemes | Form chosen by version: the tool writes empty values in 3.0 itself, with a warning about the lost roles. |
| Not expressible in 3.0 | discriminator with an optional discriminator property (any concrete base) | `oneOf` of the variants and the base branch without a `discriminator` object, with one warning (§24.2). |
| Version-independent throws (all targets) | `oauth2` without `flows`; `openIdConnect` without `openIdConnectUrl` | Today the CLI fails with exit 2 (bug, see §27). Stage 1: `Flows` / `OpenIdConnectUrl` are parsed; a literal declaration without the required data is an extraction error; a value that cannot be resolved statically → the scheme is omitted, with a warning. |

### Target 3.1

| Library behaviour | Fields | Tool action |
|---|---|---|
| Rewritten as `x-oai-*` | same list as 3.0, except `info.license.identifier` (native in 3.1) | Keep. One DW per topmost node, as for 3.0. |
| Rewritten as `x-oas-*` | `tags[].summary` / `parent` / `kind` | Keep. One DW per field and place. (`defaultMapping` is never generated for 3.1, so `x-oas-default-mapping` never appears.) |
| Not expressible in 3.1 | discriminator with an optional discriminator property | As for 3.0: `oneOf` with the base branch, without a `discriminator` object, with one warning. |
| Dropped silently | `components.mediaTypes` | Never generated. |
| Throws | `in: querystring`, `style: cookie` | Never generated. |
| Quirk | if a schema has both `example` and `examples`, both are written | The tool only sets `examples`. |

### Target 3.2

No field is degraded. Everything in the model is written natively. `xml.attribute` and `xml.wrapped` are written as `nodeType`.

### Extraction diagnostics contract (stage 1)

- Diagnostics form a public, structured channel shared by every Core component, including direct schema generation; library callers read it without capturing `Console.Error`. The CLI prints it to stderr; without an explicit consumer the printing behaviour is kept. Existing warning texts are kept.
- They never affect the exit code. `--strict` affects only validation.
- A downlevel warning carries: a stable code; the target version; the field or feature; the place — a JSON pointer into the actual output (or method + path for operations); the action (omitted / moved into an extension with its name / semantics changed); the required version; a machine-readable list of the entities the warning names (actions, types, schemas, roles, requirements), so consumers rely on it rather than on the message text. In stderr it is one line with the existing `Warning:` prefix, for example: `Warning: OpenAPI 3.0 target: /paths/~1events/get/responses/200/content/text~1event-stream: itemSchema emitted as x-oai-itemSchema (requires 3.2).`
- **The unit of a warning is the topmost lost or moved node**; nodes inside its subtree get no warning of their own. Normative cases: an operation moved into `x-oai-additionalOperations` (one per operation); `itemSchema` moved into `x-oai-itemSchema` (one per media type, covering `contentMediaType` / `contentSchema` inside); the `deviceAuthorization` flow (one per scheme, covering its URL and scopes); a concrete-base discriminator that cannot be expressed in 3.0/3.1 (one per union).
- There is exactly one entry per (target version, field, place), in a deterministic order, deduplicated within one build (for direct schema generation: within one generator instance).
- There is no entry for excluded paths, for unreachable schemas, or for lossless rewrites.
- Extraction errors (provably wrong annotations) and configuration errors are not diagnostics: they stop the build (CLI exit code 2). Previously ignored provably wrong annotations and conflicting configuration therefore change the exit code from 0 to 2.
- Opt-in fail-on-loss is a possible later option, outside stage 1.

## Validation rules changed in stage 1

Rules run for the same version as the build (`--validate`, build with validation). Standalone `validate` of a foreign file takes the version from the document's `openapi:` field.

- **Response schema when a body exists** (all versions): `itemSchema` (3.2) and `x-oai-itemSchema` (3.0/3.1) count as a sufficient schema; media types the generator deliberately emits without a schema (SSE without a typed body) are not violations.
- **`response.description`**: error for 3.0/3.1, warning for 3.2 (where `description` is optional); level overrides work as before.
- **`schema.property-constraints`** (all versions): understands exclusive bounds, every `Range` overload, nullable and composite numeric schemas, with no false positives.
- **`schema.property-format`** (all versions): accepts exactly the `[DataType]` table and the format priority of §24.4; never requires a format the table does not promise.
- **`spec.license-identifier-or-url`** (3.1+, error, new).
- **`spec.paths-or-webhooks-or-components`** (error, new): 3.0 requires `paths`; 3.1/3.2 require at least one of the three; a missing object and an empty object are distinguished.
- **Tag parent** (3.2, error, new): `tag.parent-defined` and `tag.no-parent-cycle`.
- **Unique `servers[].name`** (3.2, error, new).
- **`discriminator.default-mapping-when-optional`** (3.2, error, on by default, new): required-ness is evaluated through composition and references.
- **Operation rules and `operationId` uniqueness** (all versions): run over operations with any method, including `query`, `additionalOperations` and `webhooks` operations, with pointers that follow the output of the validated version. Exceptions: `path.params-match` and `parameter.path-required-true` check path templates and run over `paths` only; `operation.security` needs build-time action bindings. Standalone `validate` of a 3.0/3.1 file sees no operations under `x-oai-additionalOperations`: the reader of Microsoft.OpenApi 3.10.2 does not load that extension back into operations.
- **`component.no-unused`** (all versions): references through `itemSchema`, `contentSchema`, `propertyNames`, `oneOf` / `anyOf` and `discriminator.mapping` count as use.
- **`schema.array-items`** (all versions): kept as a code-generation policy (JSON Schema 2020-12 allows a missing `items`); documented as policy, not as a MUST.
- Deferred: `example.value-exclusive`; checks for `in: querystring` / `style: cookie` in foreign documents.

## Not planned

| Field(s) | Reason |
|---|---|
| `parameter.in: querystring` | MVC never binds the whole query string as one value; `[FromQuery] T` binds per property. The library throws in 3.0/3.1. Must never be generated. |
| `parameter.style: cookie`, `parameter.in: cookie` | MVC has no `[FromCookie]`; cookies are read in method bodies (runtime). The `cookie` style makes the library throw in 3.0/3.1. |
| `mediaType.itemEncoding` / `prefixEncoding`, all Encoding Object fields | No typical ASP.NET Core scenario and no attributes. |
| `components.pathItems`, `components.mediaTypes`, `components.responses` / `parameters` / `examples` / `requestBodies` / `headers` / `links` / `callbacks`, non-schema `$ref`, Reference `summary`/`description` | Reuse across documents; this is a one-document generator that inlines by design. |
| `security[]` keys as URIs, widened `allowReserved` | Cross-document features; not needed for a single document. |
| `prefixItems`, `unevaluatedItems` | Absent from the Microsoft.OpenApi 3.10.2 model; the `UnrecognizedKeywords` workaround serializes a literal `unrecognizedKeywords` key (broken in every version). STJ writes no tuples (`ValueTuple` → `{}`). Revisit when the library supports them. |
| `if` / `then` / `else`, `dependentRequired`, `dependentSchemas` | Conditional validation lives in executable code (`IValidatableObject`, FluentValidation). An own attribute would duplicate it and drift from it. |
| `contains`, `minContains`, `maxContains`, `$defs`, `$id`, `$anchor`, `$dynamicRef`, `$dynamicAnchor`, `$vocabulary`, `$comment`, `$schema`, `patternProperties`, `multipleOf` | No natural C# source; `components.schemas` covers reuse. |
| `unevaluatedProperties` | Schemas are flat; revisit if polymorphism introduces `allOf` composition. |
| XML Object (`name`, `namespace`, `prefix`, `attribute`, `wrapped`, `nodeType`) | Deferred. Useful only for APIs that serialize XML; the XML Object is not built at all today. |
| `externalDocs` on operations and schemas, `servers` on path items and operations, path item `summary`/`description`/`parameters`, `servers[].variables` | No standard source. |
| `callbacks`, Link Object, `response.links` | No standard source. Webhooks (stage 2) are a different semantic. |
| `parameter.deprecated` | `[Obsolete]` cannot be applied to parameters (CS0592); there is no other standard source. |
| `parameter.allowEmptyValue`, `header.allowEmptyValue` | Deprecated; no source. |
| Header Object fields other than `description` and `schema` | No standard source for response-header metadata. |
| `example.externalValue` | No source. |
| `response` ranges (`2XX` …) | No standard source. |
| User-defined `x-*` | Only reachable through runtime filters (`IOperationFilter`, `IDocumentFilter`, `ISchemaFilter`), which static analysis cannot execute. |
| A validation rule against reserved `x-oai-` / `x-oas-` prefixes | The tool's own downlevel output uses them by design. |

## Deferred and outside this stage

| Item | Status |
|---|---|
| `$ref` with sibling keywords instead of the `allOf` wrapper in 3.1+ | separate work (`ref-siblings`); the `allOf` wrapper is kept in every version |
| Newtonsoft analogues of STJ attributes (`[JsonExtensionData]`, polymorphism) | separate work: the extractor has no notion of an "active serializer", and attributes of two serializers must not be mixed |
| `[FromQuery] ComplexType` as a set of parameters | separate work |
| `Title`, `Description`, `Contact`, `TermsOfService`, `AddServer` read from `Program.cs`; `[ProducesErrorResponseType]`; the automatic 400 of `[ApiController]`; `[Authorize(Policy)]` | not planned: outside this stage (document metadata is already covered by CLI flags and assembly attributes; OpenAPI has no field for a policy) |
| XML Object including `nodeType` | deferred: XML serialization support would start from scratch |
| SSE `event: {const}` from the `eventType` argument | deferred: the value is a literal in the method body |
| `AddCertificate()` as a source of `mutualTLS` | deferred |
| `example.value-exclusive` rule; checks for `in: querystring` / `style: cookie` in foreign documents | deferred |
| Opt-in non-zero exit code on downlevel losses | possible later option |
