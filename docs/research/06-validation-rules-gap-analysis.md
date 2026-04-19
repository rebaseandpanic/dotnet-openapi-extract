# Validation Rules Gap Analysis — 2026-04-18

## Current state

**24 rules** across 6 scopes. Default severities are listed below.

**Critical implementation note:** The codebase does not yet have a per-rule severity field. `ValidationViolation` carries `RuleId, JsonPointer, Location, Message` only — no severity. The exit code logic in `Program.cs` is binary: any violations → exit 1. The "2-level severity (error/warning)" in the task description is an aspirational design, not a shipped feature. Wave 7 must introduce severity as a first-class field before the severity model question becomes real. This note directly shapes Goal 3 below.

### Current 24 rules and proposed default severities

| # | Rule ID | What it checks | Proposed severity |
|---|---------|----------------|-------------------|
| 1 | `operation.summary` | Non-empty summary | warning |
| 2 | `operation.operation-id` | Non-empty operationId | warning |
| 3 | `operation.description` | Non-empty description (min length configurable) | warning |
| 4 | `operation.tags` | At least one tag | warning |
| 5 | `operation.has-error-response` | At least one 4xx/5xx response declared | warning |
| 6 | `operation.security` | If `[Authorize]` present, security declared | error |
| 7 | `operation.deprecated-has-note` | If `deprecated:true`, description mentions replacement | warning |
| 8 | `parameter.description` | Non-empty parameter description | warning |
| 9 | `parameter.schema-type` | `schema.Type` is set | warning |
| 10 | `parameter.optional-has-default` | Optional value-type params have `default` | warning |
| 11 | `response.description` | Non-empty response description | error |
| 12 | `response.schema-when-body` | Content entries have `schema` | error |
| 13 | `schema.description` | Named component schemas have description | warning |
| 14 | `schema.property-description` | Each property has description | warning |
| 15 | `schema.property-format` | CLR-typed properties have matching `format` | warning |
| 16 | `schema.required-consistency` | Non-nullable properties in `required[]` | warning |
| 17 | `schema.property-constraints` | `[StringLength]`/`[Range]` reflected in schema constraints | warning |
| 18 | `schema.enum-filled` | Enum schemas have populated `enum` array | error |
| 19 | `enum.type-description` | Enum schema description mentions all values | warning |
| 20 | `enum.value-description` | `x-enum-descriptions` entries are non-empty | warning |
| 21 | `security.scheme-defined` | Referenced schemes exist in `components.securitySchemes` | error |
| 22 | `security.scheme-description` | Each security scheme has description | warning |
| 23 | `spec.info-title` | `info.title` non-empty | error |
| 24 | `spec.info-description` | `info.description` non-empty | warning |

---

## Gap analysis vs OpenAPI specification

Sources: OAS 3.0.3 (https://spec.openapis.org/oas/v3.0.3), OAS 3.1.0 (https://spec.openapis.org/oas/v3.1.0), OAS 3.2.0 (https://spec.openapis.org/oas/v3.2.0).

### Rules we should add based on spec — MUST-level violations

**spec.info-version** (new rule)
- The spec requires `info.version` in both 3.0 and 3.1: "REQUIRED. The version of the OpenAPI document." Our tool writes the version from the `--version` CLI argument (default `v1`), but `validate` subcommand operating on an existing spec has no guarantee. Add a check that `info.version` is present and non-empty.
- Severity: **error** (required field per spec).

**path.params-match** (new rule)
- OAS 3.0.3 states path template variables (`{id}`) must be defined as parameters with `in: path`. Spectral marks this severity 0 (error). Missing path parameters make the spec structurally invalid and break code generators.
- Severity: **error**.

**path.no-empty-declaration** (new rule)
- Path parameter declarations must not be empty (`/foo/{}` is invalid per spec). Spectral `path-declarations-must-exist`, severity recommended.
- Severity: **error**.

**schema.array-items** (new rule)
- Spectral `array-items` is severity 0 (error): schemas with `type: array` require a sibling `items` field. Without `items` the schema is valid per JSON Schema but ambiguous; all major code generators and SDK tools break on arrayless items. The OpenAPI 3.1 spec inherits this from JSON Schema Draft 2020-12.
- Severity: **error**.

**operation.responses-not-empty** (new rule)
- OAS 3.0.3: "The Responses Object MUST contain at least one response code." Our current rule `operation.has-error-response` only checks for 4xx/5xx; an operation with solely a `default` response and no success response also violates this principle. Separate check: `responses` object must exist and be non-empty.
- Severity: **error**.

**operation.success-response** (new rule)
- Spectral `operation-success-response`, recommended, checks at least one 2xx or 3xx response exists. Complementary to our existing `operation.has-error-response`. Operations that declare only error responses with no success path confuse clients and code generators.
- Severity: **warning** (not a hard spec MUST, but strongly recommended across all linters).

### Spec-structural checks (OAS 3.2-specific)

**spec.paths-or-webhooks-or-components** (new rule)
- OAS 3.2 changed the top-level required field: "At least one of the `components`, `paths`, or `webhooks` fields MUST be present." OAS 3.0/3.1 required `paths` unconditionally. When targeting `--openapi-version 3.2`, validate this relaxed constraint; for 3.0/3.1, validate that `paths` is non-null (even if empty is technically allowed "due to ACL constraints").
- Severity: **error** for 3.0/3.1 when `paths` is null/missing, **warning** for 3.2 (spec permits all-empty).

**parameter.path-required-true** (new rule)
- OAS spec (all versions): "If the parameter location is 'path', this property [required] is REQUIRED and its value MUST be true." A path parameter with `required: false` is a spec violation.
- Severity: **error**.

### Rules we should reconsider (existing 24)

**`operation.deprecated-has-note`** — current implementation uses English keyword regex (`replacement|use instead|removed`). This is locale-specific and will produce false positives/negatives on non-English descriptions. It also requires description to exist — which `operation.description` already checks. Consider loosening to simply requiring that deprecated operations have a non-empty description of minimum N characters (already checked by `operation.description`), and remove the keyword pattern check. If kept, lower to **info** level or document clearly as opinionated.

**`parameter.optional-has-default`** — no linter (Spectral, Redocly) checks this by default. It is an opinionated best-practice rule for value types. Fits **info** level if that level exists, or keep as **warning** but mark as easily suppressible.

**`schema.required-consistency`** — the current implementation flags ALL non-nullable value-type properties not in `required[]`. This is stricter than Redocly's `no-required-schema-properties-undefined` (which only flags properties listed in `required` but not defined in `properties`). Our direction is the logical inverse. This is an opinionated consistency check. The logic is sound for codegen consumers but will produce noise on third-party specs. Keep as **warning** but document the scope clearly.

**`enum.type-description` and `enum.value-description`** — use of `x-enum-descriptions` is a non-standard extension (Swashbuckle convention). Valid for our internal toolchain but should be flagged clearly as an extension-dependent rule. In standalone `validate` mode against a third-party spec, these rules will never fire usefully. Consider making both **info** or **off-by-default** when per-rule severity is introduced.

---

## Gap analysis vs industry linters

### Spectral OAS ruleset — rules we don't have

Spectral has 56 rules total; OAS3-relevant rules we don't cover:

| Spectral rule | Spectral severity | Priority for us |
|---|---|---|
| `operation-success-response` | default (error) | **High** — add |
| `operation-operationId-unique` | 0 (error) | **High** — add |
| `operation-operationId-valid-in-url` | default | **Medium** — add |
| `operation-tag-defined` | default | **Medium** — add (tags must exist in top-level `tags` array) |
| `path-params` | 0 (error) | **High** — add as `path.params-match` |
| `path-declarations-must-exist` | default | **High** — add |
| `path-keys-no-trailing-slash` | default | **Medium** — add |
| `path-not-include-query` | default | **Medium** — add |
| `array-items` | 0 (error) | **High** — add |
| `typed-enum` | default | **Medium** — add as `schema.typed-enum` |
| `duplicated-entry-in-enum` | warn | **Medium** — add |
| `no-$ref-siblings` | 0 (error) | **High** for 3.0; OAS 3.1 removes this restriction (JSON Schema `$ref` with siblings is valid in 3.1+). Added as rule A0, version-conditional. |
| `oas3-api-servers` | default | **Low** — add as `spec.servers-defined` (warning, not error) |
| `oas3-examples-value-or-externalValue` | default | **Low** — opt-in |
| `oas3-unused-component` | default | **Low** — info-level, add as opt-in |
| `no-eval-in-markdown` | default | **Low** — security hygiene, easy to add |
| `no-script-tags-in-markdown` | default | **Low** — security hygiene |
| `openapi-tags-uniqueness` | error | **Medium** — add |
| `info-contact` | default | **Skip** — opinionated, org-specific |
| `info-license` / `license-url` | default | **Skip** — opinionated |
| `tag-description` | default | **Low** — add |
| `openapi-tags-alphabetical` | default | **Skip** — pure style |
| `operation-singular-tag` | default | **Skip** — opinionated, Redocly also off-by-default |
| `oas3-server-not-example.com` | default | **Skip** — dev concern, not a completeness rule |
| `oas3-callbacks-in-callbacks` | default | **Skip** — .NET controller extraction does not produce callbacks; relevant only in standalone `validate` against third-party specs; low value for our user base |
| `oas3_1-servers-in-webhook` | default | **Skip** — webhooks are not produced by the extractor; add only if webhook support is added to the extraction engine |
| `oas3_1-callbacks-in-webhook` | default | **Skip** — same reasoning as above |

### Redocly default ruleset — rules we don't have

| Redocly rule | Default severity | Priority for us |
|---|---|---|
| `no-unresolved-refs` | error | **Critical** — add as `spec.no-unresolved-refs` (hard to implement statically but important) |
| `no-unused-components` | warn | **Low** — add as info/opt-in |
| `operation-2xx-response` | warn | **High** — same as Spectral `operation-success-response`, add |
| `operation-4xx-response` | warn | We have `operation.has-error-response` (covers 4xx + 5xx) |
| `operation-operationId-unique` | error | **High** — add |
| `operation-operationId-url-safe` | error | **Medium** — add |
| `operation-parameters-unique` | error | **High** — add |
| `path-declaration-must-exist` | error | **High** — add |
| `path-parameters-defined` | error | **High** — same as `path.params-match`, add |
| `path-not-include-query` | error | **Medium** — add |
| `no-path-trailing-slash` | error | **Medium** — add |
| `no-ambiguous-paths` | error | **Low** — hard to check statically |
| `no-identical-paths` | error | **Medium** — add |
| `no-enum-type-mismatch` | error | **Medium** — add |
| `no-required-schema-properties-undefined` | warn | **Medium** — add |
| `no-invalid-schema-examples` | warn | **Medium** — add (verify examples match schema types) |
| `no-invalid-parameter-examples` | warn | **Medium** — add |
| `component-name-unique` | error | **Low** — add |
| `no-empty-servers` | error | **Low** — add as warning |
| `no-server-trailing-slash` | error | **Low** — add |
| `no-duplicated-tag-names` | error | **Medium** — add |
| `operation-tag-defined` | warn | **Medium** — add |
| `required-string-property-missing-min-length` | warn | **Low** — opinionated, skip or make opt-in |
| `scalar-property-missing-example` | off | **Skip** — too noisy, off by default even in Redocly |
| `array-parameter-serialization` | off | **Skip** — off by default even in Redocly |
| `paths-kebab-case`, `no-http-verbs-in-paths`, `path-segment-plural` | off/warn | **Skip** — opinionated naming conventions |
| `boolean-parameter-prefixes` | off | **Skip** — opinionated |
| `operation-4xx-problem-details-rfc7807` | off | **Skip** — opinionated org convention |
| `response-contains-header`, `response-contains-property` | off | **Skip** — opinionated org convention |

### Rules cited by 3+ linters — strongest universal signal

The following rules appear in Spectral, Redocly, and are required or strongly recommended by the OAS spec itself. These are the highest-confidence additions:

1. **operation.success-response** — Spectral + Redocly + industry consensus
2. **operation.operation-id-unique** — Spectral (error) + Redocly (error) + spec (operationId must be unique)
3. **operation.operation-id-url-safe** — Spectral + Redocly + APIStylebook guidelines
4. **path.params-match** — Spectral (error) + Redocly (error) + OAS spec MUST
5. **path.no-trailing-slash** — Spectral + Redocly (both mark as error/recommended)
6. **path.no-query-string** — Spectral + Redocly (both mark as error/recommended)
7. **schema.array-items** — Spectral (error) + implicit in OAS 3.1 JSON Schema inheritance
8. **schema.typed-enum** — Spectral + Redocly (`no-enum-type-mismatch`)
9. **spec.info-version** — OAS 3.0 REQUIRED + OAS 3.1 REQUIRED
10. **tag.no-duplicates** — Spectral (error) + Redocly (error)
11. **operation.parameters-unique** — Redocly (error), Spectral `operation-parameters` (recommended)

---

## Severity model recommendation

### Current state: no per-rule severity

As noted above, `ValidationViolation` has no severity field today. All violations cause exit code 1. The question is not "expand from 2 to 3 levels" but "introduce severity at all, and at what granularity."

### Recommendation: adopt 2 levels (error / warning)

**Adopt error and warning. Skip info/hint.**

Reasoning:

1. **Redocly uses error/warn/off** and is considered the most production-ready of the major linters. The `off` state maps cleanly to our existing `SkippedRuleIds` mechanism — no third severity level needed.

2. **Spectral's 4 levels (error/warn/info/hint) cause user confusion.** In practice, users configure everything as warn or error; info and hint see almost no real-world usage. The Spectral community frequently asks "what is the difference between info and hint."

3. **Our rules are completeness-focused, not style-focused.** The only category that arguably benefits from a third level is "opinionated/org-specific" rules — but the right answer for those is to make them **off by default** (via `SkippedRuleIds`), not to add a third severity. The user enables them explicitly via rule configuration when they want them.

4. **Implementation simplicity.** Adding `Severity` (enum with two values) to `ValidationViolation` and `IValidationRule.DefaultSeverity` is a small change. Adding 3 levels requires CLI flags for each level's exit-code behavior and creates ambiguity in CI pipelines ("do warnings fail the build?").

**Concrete change for Wave 7:**
- Add `ValidationSeverity { Error, Warning }` enum.
- Add `Severity` to `ValidationViolation`.
- Add `DefaultSeverity` to `IValidationRule`.
- Exit code logic: exit 1 if any **error**-level violations exist. Warnings are reported but don't fail by default.
- Add `--strict` flag: treat all warnings as errors (exit 1 on any violation).
- Existing `--skip-rule` covers the "off" case.

This gives users three effective levels: off (skipped), warn (reported, no failure), error (fails build).

---

## Proposed additions

Rules are ordered by priority. Justification column references specific linter/spec sources verified above.

### Group A — Spec-MUST violations (implement first, all error-severity)

**A0. `spec.no-ref-siblings`**
- Checks: in OpenAPI 3.0 documents, no sibling properties exist alongside a `$ref`. (In OAS 3.1, `$ref` with siblings is valid per JSON Schema Draft 2020-12, so this rule applies only when `--openapi-version 3.0`.)
- Scope: spec-level.
- Severity: error (3.0 only — off for 3.1/3.2).
- Justification: Spectral `no-$ref-siblings` severity 0. In OAS 3.0, `$ref` semantics replace the containing object entirely; sibling properties are silently ignored by parsers, causing data loss.
- Difficulty: medium (full document traversal; needs version-conditional logic).

**A1. `spec.info-version`**
- Checks: `info.version` is present and non-empty.
- Scope: spec-level.
- Severity: error.
- Justification: OAS 3.0.3 and 3.1.0 REQUIRED field. Our tool writes it from `--version` but `validate` subcommand on third-party specs has no guarantee.
- Difficulty: easy.

**A2. `operation.operation-id-unique`**
- Checks: all `operationId` values across the entire document are unique.
- Scope: spec-level (cross-operation).
- Severity: error.
- Justification: Spectral severity 0, Redocly error. Non-unique operationIds break all SDK generators (method name collisions) and OpenAPI tooling that indexes by operationId.
- Difficulty: easy.

**A3. `path.params-match`**
- Checks: every `{variable}` in a path template has a corresponding parameter with `in: path` declared on the operation or path item; and every `in: path` parameter corresponds to a template variable.
- Scope: path/operation-level.
- Severity: error.
- Justification: Spectral `path-params` severity 0, Redocly `path-parameters-defined` error. OAS 3.0.3 constraint. Our extractor tries to wire these correctly, but standalone `validate` mode needs this check.
- Difficulty: medium (bidirectional check: template→params and params→template).

**A4. `path.no-empty-declaration`**
- Checks: no path template contains an empty variable (`/foo/{}`).
- Scope: path-level.
- Severity: error.
- Justification: Spectral `path-declarations-must-exist` recommended. Structurally invalid per OAS.
- Difficulty: easy.

**A5. `parameter.path-required-true`**
- Checks: parameters with `in: path` have `required: true`.
- Scope: parameter-level.
- Severity: error.
- Justification: OAS 3.0.3 MUST: "if in is 'path', this property is REQUIRED and its value MUST be true." Our extractor sets this correctly, but standalone `validate` needs the check.
- Difficulty: easy.

**A6. `schema.array-items`**
- Checks: schemas with `type: array` have a sibling `items` field.
- Scope: schema-level.
- Severity: error.
- Justification: Spectral `array-items` severity 0. Without `items`, code generators produce `List<object>` at best, and many tools reject the schema outright.
- Difficulty: easy (check `schema.Type == JsonSchemaType.Array && schema.Items == null`).

**A7. `operation.parameters-unique`**
- Checks: no operation has two parameters with the same `name` + `in` combination.
- Scope: operation-level.
- Severity: error.
- Justification: OAS 3.0.3: parameter uniqueness is required. Redocly `operation-parameters-unique` error. Spectral `operation-parameters` recommended.
- Difficulty: easy.

### Group B — Structural completeness (implement second, warning-severity)

**B1. `operation.success-response`**
- Checks: each operation declares at least one 2xx or 3xx response.
- Scope: operation-level.
- Severity: warning.
- Justification: Spectral `operation-success-response` recommended. Redocly `operation-2xx-response` warn. Operations with only error responses are incomplete from a consumer perspective.
- Difficulty: easy (complement of our existing `operation.has-error-response`).

**B2. `operation.operation-id-url-safe`**
- Checks: `operationId` contains only URL-safe characters (alphanumeric, `-`, `_`).
- Scope: operation-level.
- Severity: warning.
- Justification: Spectral `operation-operationId-valid-in-url` recommended. Redocly `operation-operationId-url-safe` error. OperationIds with spaces or special characters break SDK method name generation in most languages.
- Difficulty: easy (regex `^[a-zA-Z0-9_-]+$`).

**B3. `path.no-trailing-slash`**
- Checks: path keys do not end with `/` (except the root path `/`).
- Scope: path-level.
- Severity: warning.
- Justification: Spectral `path-keys-no-trailing-slash` recommended. Redocly `no-path-trailing-slash` error. Causes duplicate-URL ambiguity and breaks some HTTP clients.
- Difficulty: easy.

**B4. `path.no-query-string`**
- Checks: path keys do not contain `?`.
- Scope: path-level.
- Severity: warning.
- Justification: Spectral `path-not-include-query` recommended. Redocly `path-not-include-query` error. Query parameters in paths break routing in all major frameworks.
- Difficulty: easy.

**B5. `path.no-identical`**
- Checks: no two paths in the document are identical (same string).
- Scope: spec-level.
- Severity: warning.
- Justification: Redocly `no-identical-paths` error. Duplicate paths produce ambiguous routing.
- Difficulty: easy (set membership).

**B6. `tag.no-duplicates`**
- Checks: the top-level `tags` array has no duplicate tag names.
- Scope: spec-level.
- Severity: warning.
- Justification: Spectral `openapi-tags-uniqueness` error, Redocly `no-duplicated-tag-names` error.
- Difficulty: easy.

**B7. `operation.tag-defined`**
- Checks: each tag used on an operation exists in the top-level `tags` array.
- Scope: operation-level.
- Severity: warning.
- Justification: Spectral `operation-tag-defined` recommended, Redocly `operation-tag-defined` warn. Undefined tags produce broken navigation in most API portals (Redoc, SwaggerUI).
- Difficulty: easy.

**B8. `schema.typed-enum`**
- Checks: enum values in a schema match the declared `type` (e.g., string enum must not contain integers).
- Scope: schema-level.
- Severity: warning.
- Justification: Spectral `typed-enum` recommended. Redocly `no-enum-type-mismatch` error. Type-mismatched enums break client deserialization.
- Difficulty: medium (type-check each enum value against `schema.Type`).

**B9. `schema.no-duplicate-enum`**
- Checks: enum arrays contain no duplicate values.
- Scope: schema-level.
- Severity: warning.
- Justification: Spectral `duplicated-entry-in-enum` warn. Duplicate enum values confuse code generators and produce duplicate case labels.
- Difficulty: easy.

**B10. `schema.no-required-undefined`**
- Checks: properties listed in `required[]` are defined in `properties`.
- Scope: schema-level.
- Severity: warning.
- Justification: Redocly `no-required-schema-properties-undefined` warn. Note: this is the complement to our existing `schema.required-consistency` (which checks that defined properties are in `required[]`). Both are needed.
- Difficulty: easy.

### Group C — Developer experience (implement last, warning-severity, off by default)

**C1. `spec.servers-defined`**
- Checks: `servers` array is present and non-empty.
- Scope: spec-level.
- Severity: warning (off by default — many internal specs legitimately omit servers).
- Justification: Spectral `oas3-api-servers` recommended. Redocly `no-empty-servers` error. Missing servers causes API explorer tools to use relative URLs, which often break.
- Difficulty: easy.

**C2. `tag.description`**
- Checks: each entry in the top-level `tags` array has a non-empty `description`.
- Scope: spec-level.
- Severity: warning (off by default).
- Justification: Spectral `tag-description` recommended. Redocly `tag-description` warn. Undescribed tags produce poor navigation in Redoc and SwaggerUI.
- Difficulty: easy.

**C3. `component.no-unused`**
- Checks: schemas and other components in `components` are referenced at least once.
- Scope: spec-level.
- Severity: warning (off by default — legitimate in incremental publishing workflows).
- Justification: Spectral `oas3-unused-component` recommended, Redocly `no-unused-components` warn. Unused components bloat generated SDKs.
- Difficulty: medium (requires full $ref traversal of the document).

**C4. `spec.no-eval-in-markdown`**
- Checks: no `description` field contains `eval(`.
- Scope: spec-level.
- Severity: warning.
- Justification: Spectral `no-eval-in-markdown` recommended. Security hygiene for portals that render markdown descriptions.
- Difficulty: easy.

**C5. `spec.no-script-tags-in-markdown`**
- Checks: no `description` field contains `<script>`.
- Scope: spec-level.
- Severity: warning.
- Justification: Spectral `no-script-tags-in-markdown` recommended. Same security rationale as C4.
- Difficulty: easy.

---

## Proposed re-categorizations of existing 24 rules

When per-rule severity is introduced in Wave 7, the following 10 of the 24 existing rules should have their severity set explicitly and differ from a plain "all warnings" default. The remaining 14 rules default cleanly to **warning** without further discussion needed.

| Rule ID | Current (implicit) | Proposed | Reason |
|---|---|---|---|
| `response.description` | all equal | **error** | OAS spec REQUIRED field. Redocly and Spectral both treat missing response description as an error. |
| `response.schema-when-body` | all equal | **error** | Content without schema is structurally broken — code generators have nothing to work with. |
| `schema.enum-filled` | all equal | **error** | Enum type without `enum` values makes the schema meaningless for validation and codegen. |
| `security.scheme-defined` | all equal | **error** | References to undefined security schemes are structural errors (equivalent to unresolved $refs). |
| `spec.info-title` | all equal | **error** | OAS REQUIRED field. |
| `operation.security` | all equal | **error** | Authorization declared but not reflected in spec — security contract is broken for consumers. |
| `operation.deprecated-has-note` | all equal | **warning** (reconsider as info) | English keyword regex is too fragile and opinionated. See "rules to reconsider" section. |
| `parameter.optional-has-default` | all equal | **warning** (reconsider as info or off-by-default) | Not checked by any major linter. Useful but opinionated. |
| `enum.value-description` | all equal | **warning** (off by default) | `x-enum-descriptions` is a Swashbuckle-specific extension, not a standard. Third-party specs will not have it. |
| `enum.type-description` | all equal | **warning** (off by default) | Same reasoning as above — Swashbuckle convention, not a standard. |

---

## Summary

- Current: 24 rules, no per-rule severity field (all violations treated equally, exit 1 on any).
- Proposed additions: **23 new rules** (8 error-level group A including A0, 10 warning-level group B, 5 warning/off group C).
- Proposed re-severities for existing rules: 10 rules should have explicit severity when the field is introduced.
- New total: **47 rules**.
- Severity model: **2 levels — error and warning** (no info/hint). "Off" (disabled) is the third effective state, handled by the existing `--skip-rule` / `SkippedRuleIds` mechanism. Rules in Group C should be off by default.

### Implementation priority order for Wave 7

1. Introduce `ValidationSeverity` enum and `Severity` field on `ValidationViolation` and `IValidationRule`.
2. Assign severities to all 24 existing rules (see table above).
3. Implement Group A rules (A0–A7, all errors). These have the most correctness impact.
4. Implement Group B rules (B1–B10, warnings). Standard completeness rules from Spectral + Redocly overlap.
5. Implement Group C rules (C1–C5, off by default). Nice-to-have, avoid noise for existing users.
6. Revisit `operation.deprecated-has-note` keyword regex — consider loosening or demoting.
