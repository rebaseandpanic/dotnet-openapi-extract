# Wave 5 — Real-world Roslyn patterns coverage

Extend the extractors (T3 Security, T7 Response Headers, T12 Document Tags, T2+T9 JSON options) so they recognize **real production patterns** of code, not just canonical forms from Microsoft docs.

## Motivation

Running the extractor on a real DLL (VpnCoreApi, 100 endpoints, 232 schemas) showed a gap: the spec for paths/schemas/tags is extracted fine, but **security schemes, global requirements, response headers** remained empty — because the real Program.cs uses dialects that our Level-1 extractors don't cover.

This is **not a problem with the real code** — the code is correct and typical. The problem is that our extractors were written for the simplest forms from the documentation.

## Libraries and Licenses

**Everything is open-source, MIT, already in the project.** No commercial SAST tools (Parasoft dotTEST, JetBrains Qodana, SonarQube Enterprise) needed.

| Library | Status |
|------------|--------|
| `Microsoft.CodeAnalysis.CSharp` (Roslyn) | already integrated |
| `System.Reflection.MetadataLoadContext` | already integrated |
| `Microsoft.OpenApi` | already integrated |

For Level 3 additional `MetadataReference`s are needed — these are just DLLs from disk (`Microsoft.AspNetCore.App` shared framework), available locally. Free.

## Approach

Split the patterns into 4 levels. Implement Levels 1+2. Level 3 — discussed as a separate wave after evaluating 1+2. Level 4 — documented as known limitations.

## Level 1 — Syntactic polish (doing, Task 5.1)

Simple extensions to the current syntactic parsing.

### 5.1.1 FQN-prefixed type names

**Pattern:** `new Microsoft.OpenApi.OpenApiSecurityScheme { ... }` instead of `new OpenApiSecurityScheme { ... }`.

**Where it breaks:** `SecuritySchemeExtractor.TryExtractSchemeFromObjectCreation` matches `ObjectCreationExpression.Type` via `IdentifierNameSyntax`, doesn't handle `QualifiedNameSyntax`.

**Fix:** when matching the object-creation type — take the **last identifier** from any `QualifiedNameSyntax`/`AliasQualifiedNameSyntax`. A shared helper `GetLastIdentifierName(TypeSyntax)`.

Affected extractors: `SecuritySchemeExtractor`, `DocumentTagsExtractor`, possibly `JsonOptionsExtractor` (for `new JsonStringEnumConverter()`).

### 5.1.2 FQN-prefixed enum values

**Pattern:** `Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey` instead of `Type = SecuritySchemeType.ApiKey`.

**Where it breaks:** similarly, in parsing enum values inside an object initializer.

**Fix:** when matching `MemberAccessExpressionSyntax` for an enum value — walk the `Expression → Name` chain and take the last two identifiers (`SecuritySchemeType.ApiKey`), ignoring the namespace prefix.

### 5.1.3 Response.Headers indexer assignments

**Pattern:** `context.Response.Headers["X-Request-Id"] = value;` instead of `context.Response.Headers.Append("X-Request-Id", value);`.

**Where it breaks:** `ResponseHeaderExtractor` matches only `MemberAccessExpressionSyntax` with methods `Append/Add/TryAdd`. Doesn't handle `ElementAccessExpressionSyntax` (`Headers[...] = ...`).

**Fix:** add `ElementAccessExpressionSyntax` scanning where the receiver ends with `Response.Headers`. The header name is the first argument (`ElementAccessExpressionSyntax.ArgumentList.Arguments[0]`) if it's a literal.

### 5.1.4 Verbatim/interpolated const strings

**Pattern:** `UsePathBase(@"/api/v1")` (verbatim — already works), `UsePathBase($"/api/{Constants.Version}")` (interpolated — doesn't work).

**Where it breaks:** `InvocationMatcher.GetLiteralStringArgument` checks only `LiteralExpressionSyntax (StringLiteralExpression)`.

**Fix:** extend to `InterpolatedStringExpressionSyntax` where **all** `Contents` are `InterpolatedStringTextSyntax` (no runtime-dependent interpolations). Then the final string can be assembled from `Contents.Select(c => c.ToString())`.

Runtime interpolations (`$"{someVar}"`) — skip with a warning, that's Level 4.

### 5.1.5 Constants from static classes

**Pattern:** `Headers.Append(HeaderNames.RequestId, ...)` where `HeaderNames.RequestId` is `public const string RequestId = "X-Request-Id"`.

**Where it breaks:** doesn't match as a literal (it's `MemberAccessExpressionSyntax`).

**Fix:** via `SemanticModel.GetConstantValue(syntaxNode)` — Roslyn can resolve compile-time constants even without metadata references, **if the constant class itself is available in syntax trees**. For Program.cs, Constants usually live in the same project — SemanticModel resolves them.

**Limitation:** if Constants are in another assembly (external NuGet package) — Level 3 (metadata references) is needed. At Level 1 we cover only in-project constants.

### Level 1 Complexity

~1-2 days. Mostly — one general helper for QualifiedName unwrapping + small extensions in 3 extractors.

## Level 2 — Lambda-factory patterns (doing, Task 5.2)

### 5.2.1 Factory functions for security requirements

**Pattern:**
```csharp
c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
{
    { new OpenApiSecuritySchemeReference("ApiKey", doc, null), new List<string>() },
    { new OpenApiSecuritySchemeReference("ClientId", doc, null), new List<string>() }
});
```

**Where it breaks:** `SecuritySchemeExtractor.TryExtractRequirementSchemeNames` looks for `ObjectCreationExpressionSyntax` directly as an argument of `AddSecurityRequirement`, doesn't walk inside the lambda.

**Fix:** if the `AddSecurityRequirement` argument is a `LambdaExpressionSyntax`, find a nested `ObjectCreationExpressionSyntax` of type `OpenApiSecurityRequirement` inside `LambdaExpression.Body`, then the usual initializer parsing.

### 5.2.2 Complex options lambdas

**Pattern:** `AddSwaggerGen(c => { c.AddSecurityDefinition(...); c.AddSecurityRequirement(...); c.AddTag(...); })` — several invocations inside a single lambda.

**Where it breaks:** already partially works (we look for invocations in the lambda's scope), but may break on combinations with Level 1 fixes — verify the integration works.

### 5.2.3 Options-helper extension-factory

**Pattern:** `services.AddAuthentication().AddJwtBearer("SchemeName", options => { options.Authority = "..."; options.Audience = "..."; })`.

**Where it breaks:** already partially covered, but FQN-type resolution may require Level 1 improvements.

### Level 2 Complexity

~2-3 days. The main work is walking lambda bodies and correct state management for nested scopes.

## Level 3 — Semantic resolution with full references (NOT doing now, Task 5.3 separate)

**Patterns:**
- `services.AddVpnAuth()` — custom extension method, internally calls `AddAuthentication + AddJwtBearer`
- `SecurityConfig.Configure(services, options)` — helper class between files
- `policy: Constants.DefaultPolicy` where `Constants` is in a separate assembly

**What's required:**
1. Extend `SourceCompiler.BuildMinimalReferences()` — add all DLLs from the `Microsoft.AspNetCore.App` shared framework + references found in the csproj (via `dotnet msbuild /t:ResolveReferences`).
2. Use `SemanticModel.GetSymbolInfo()` to resolve types of extension methods, helper classes, external constants.
3. Recursively walk the resolved symbol (via `symbol.DeclaringSyntaxReferences`) — if the method is defined in our compilation, we can step inside and analyze it.

**Risks:**
- Latent bug W1 (`global::` prefix) — we fixed it recently, but others like it will appear.
- Compilation with full references becomes significantly slower (~5-10x).
- Cross-assembly resolution — if the method is in a DLL that's not in references, it still won't resolve.

**Cost:** 5-7 days. Requires a separate careful wave.

**Decision:** do it if, after Level 1+2, coverage on real projects is < 80% (metric below).

## Level 4 — Runtime-only (documented as known limitations)

**Patterns we will NEVER cover statically:**

| # | Pattern | Example | Why |
|---|---------|--------|--------|
| 1 | IConfiguration values | `Type = config["Auth:Scheme"]` | Runtime-dependent on appsettings.json / env |
| 2 | Conditional registration | `if (env.IsDevelopment()) services.AddX()` | Depends on environment |
| 3 | DI-factory | `services.AddScoped<ISchemeProvider>(sp => sp.GetRequiredService<X>())` | Runtime DI graph |
| 4 | Plugin discovery | `services.Scan(...).AddClasses(...)` | Assembly scanning at runtime |
| 5 | Runtime interpolation | `Headers.Append($"X-{variable}", ...)` | Value known at runtime |

**Action:** extend the `## Limitations` section in README — add an explicit list of these 5 patterns with examples.

## Success metrics

**Metric for evaluating Wave 5:** running the extractor on **at least 3 real production DLLs** with available sources.

| Project | Endpoints | Current coverage | Target coverage |
|--------|-----------|------------------|------------------|
| VpnCoreApi (available) | 100 | ~50% (paths/schemas/tags, but not security/servers/headers) | ≥90% |
| Project #2 (TBD) | TBD | TBD | ≥80% |
| Project #3 (TBD) | TBD | TBD | ≥80% |

**"Coverage"** = share of runtime-config features reflected in the emitted OpenAPI vs what should really be there (by manual inspection of Program.cs).

If after Level 1+2 coverage on 3 projects is ≥80% — Wave 5 is successful, Level 3 is not needed.
If less — start Level 3 as Wave 6.

## Execution Protocol

The same 5-step protocol as in Waves 0-4:

1. Implementation (subagent `csharp-pro`)
2. `zcoderev` review
3. Findings triage (main agent)
4. Fix subagent with a filtered list
5. Test quality audit subagent

## Wave 5 Tasks

### Task 5.1 — Level 1: Syntactic polish

Do all 5 Level 1 items with one subagent — they are interconnected and all touch the shared helper for QualifiedName unwrapping.

**Tests:**
- Unit tests for the shared helper `GetLastIdentifierName` / `UnwrapQualifiedName`
- Unit tests for the extended `GetLiteralStringArgument` (verbatim, interpolated const)
- Integration tests: temp Program.cs with FQN-types + object initializers → all 4 security scheme fields are extracted correctly
- Integration test: Response.Headers indexer → header ends up in the global headers list
- Integration test on VpnCoreApi.dll — how many features now work

### Task 5.2 — Level 2: Lambda-factory patterns

Do all 3 Level 2 patterns. Main focus — `AddSecurityRequirement(doc => ...)`.

**Tests:**
- Unit tests for walking lambda bodies with nested object creations
- Integration tests: `AddSecurityRequirement(doc => new OpenApiSecurityRequirement { ... })` → scheme names extracted correctly
- Re-run VpnCoreApi — verify that security schemes + global requirements are now in the spec

### Task 5.3 — Metric + decision on Level 3

After Task 5.1 + 5.2:
- Gather 2 additional real-world projects for testing (from public ASP.NET examples or our other projects).
- Run the extractor on all 3.
- Measure coverage manually — what % of runtime-config features are now in the spec.
- Make a decision: sufficient / go to Level 3.

### Task 5.4 — Level 4 documentation

Extend the `## Limitations` section of README.md with an explicit list of 5 runtime-only patterns with examples. Not a blocker, can be done in parallel with 5.1/5.2.

## Execution Order

```
5.1 (Level 1, parallel)  ┐
5.2 (Level 2, parallel)  ┤ → Combined review + audit
5.4 (README limitations) ┘
                         ↓
                       5.3 (metrics + decision)
                         ↓
                 if decided → Wave 6 (Level 3) as separate task doc
```

Tasks 5.1, 5.2, 5.4 are independent — can be done in parallel. 5.3 — after them.
