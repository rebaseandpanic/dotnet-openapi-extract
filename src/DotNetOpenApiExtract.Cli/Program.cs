using System.CommandLine;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

// ── Validation option helpers ─────────────────────────────────────────────────

// Build the rule ID list with default severities for --help text.
var allRuleIds = string.Join(", ", CoreValidator.AllRules.Select(r =>
{
    var sev = r.DefaultSeverity == ValidationSeverity.Error ? "error" : "warning";
    var offTag = CoreValidator.DefaultOffRuleIds.Contains(r.Id) ? ", off" : "";
    return $"{r.Id} ({sev}{offTag})";
}));

// ── Shared validation options (used by both root command and validate subcommand) ─

var validateOption = new Option<bool>("--validate")
{
    Description = $"Enable OpenAPI spec validation. Runs {CoreValidator.AllRuleIds.Count} rules — " +
                  $"{CoreValidator.AllRules.Count(r => r.DefaultSeverity == ValidationSeverity.Error)} as errors, " +
                  $"{CoreValidator.AllRules.Count(r => r.DefaultSeverity == ValidationSeverity.Warning && !CoreValidator.DefaultOffRuleIds.Contains(r.Id))} as warnings, " +
                  $"{CoreValidator.DefaultOffRuleIds.Count} as warnings (off by default). " +
                  "Error severity blocks CI (exit 1). Warnings are reported but do not fail by default. " +
                  "Use --strict to treat all warnings as errors. " +
                  "Off-by-default rules can be enabled with --enable-rule <id>. " +
                  $"Rule IDs: {allRuleIds}. " +
                  "Disable individual rules via --skip-rule <id>.",
};

var skipRuleOption = new Option<string[]>("--skip-rule")
{
    Description = "Disable a specific validation rule by ID (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var minDescLengthOption = new Option<int>("--min-description-length")
{
    Description = "Minimum characters required for description fields in validation rules (default: 5)",
    DefaultValueFactory = _ => 5,
};

var excludeValidationPathOption = new Option<string[]>("--exclude-validation-path")
{
    Description = "Path prefixes skipped by rules that require error responses (operation.has-error-response) " +
                  "and success responses (operation.success-response). Other rules are not affected. " +
                  "Repeatable. Default: none.",
    AllowMultipleArgumentsPerToken = false,
};

var strictOption = new Option<bool>("--strict")
{
    Description = "Treat all Warning-severity violations as errors (exit 1 on any violation). Equivalent to promoting all warnings to errors.",
};

var warnRuleOption = new Option<string[]>("--warn-rule")
{
    Description = "Demote a specific rule from error to warning severity (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var errorRuleOption = new Option<string[]>("--error-rule")
{
    Description = "Promote a specific rule from warning to error severity (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var enableRuleOption = new Option<string[]>("--enable-rule")
{
    Description = "Enable a rule that is off by default (can be specified multiple times). " +
                  $"Off-by-default rule IDs: {string.Join(", ", CoreValidator.DefaultOffRuleIds)}",
    AllowMultipleArgumentsPerToken = false,
};

var validationReportOption = new Option<string?>("--validation-report")
{
    Description = "Write JSON validation report to this file path (if omitted, report is printed to stdout)",
};

var requireResponseCodeOption = new Option<string[]>("--require-response-code")
{
    Description = "Required response codes for specific HTTP methods. Format: METHOD:CODE. Method can be: " +
                  "GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS, mutating (POST/PUT/PATCH/DELETE), " +
                  "safe (GET/HEAD/OPTIONS), or * for any. Repeatable. Activates " +
                  "operation.has-required-response-codes rule (requires --enable-rule). " +
                  "Example: --require-response-code mutating:422 --require-response-code *:401",
    AllowMultipleArgumentsPerToken = false,
};

var ruleMinLengthOption = new Option<string[]>("--rule-min-length")
{
    Description = "Override --min-description-length for a specific rule. Format: RULE-ID:N. Repeatable. " +
                  "Applies to rules that check description length. " +
                  "Example: --rule-min-length enum.value-description:5 --rule-min-length operation.description:30",
    AllowMultipleArgumentsPerToken = false,
};

// ── Standard extraction options ────────────────────────────────────────────────

var assemblyOption = new Option<FileInfo>("--assembly")
{
    Description = "Path to the .NET assembly DLL (produced by dotnet build)",
    Required = true,
};

var outputOption = new Option<string>("--output")
{
    Description = "Output file path",
    DefaultValueFactory = _ => "swagger.json",
};

var formatOption = new Option<string>("--format")
{
    Description = "Output format: json or yaml",
    DefaultValueFactory = _ => "json",
};

var titleOption = new Option<string?>("--title")
{
    Description = "API title. Defaults to [AssemblyTitle], then [AssemblyProduct], then DLL file name",
};

var versionOption = new Option<string>("--version")
{
    Description = "Document version",
    DefaultValueFactory = _ => "v1",
};

var descriptionOption = new Option<string?>("--description")
{
    Description = "API description written to the info block",
};

var xmlOption = new Option<string[]>("--xml")
{
    Description = "XML documentation file path. Can be specified multiple times — each path adds one more source. " +
                  "Sources are merged with first-added winning on key collision, so earlier --xml paths take priority. " +
                  "Project XML is auto-detected next to the DLL and added after explicit paths. " +
                  "Framework/SDK ref-pack XMLs are discovered automatically and added last. " +
                  "If an explicitly-provided file does not exist, a warning is printed to stderr and that entry is skipped.",
    AllowMultipleArgumentsPerToken = false,
};

var namingPolicyOption = new Option<string?>("--naming-policy")
{
    Description = "JSON property naming policy: preserve, camelCase (default), snake_case_lower, snake_case_upper, kebab-case-lower, kebab-case-upper",
};

var enumAsStringOption = new Option<bool>("--enum-as-string")
{
    Description = "Serialize enum values as strings instead of integers",
    DefaultValueFactory = _ => false,
};

var openapiVersionOption = new Option<string>("--openapi-version")
{
    Description = "OpenAPI specification version: 3.0, 3.1, or 3.2",
    DefaultValueFactory = _ => "3.0",
};

var excludePathsOption = new Option<string[]>("--exclude-path")
{
    Description = "Path prefix to exclude from the generated spec (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var sourceOption = new Option<string?>("--source")
{
    Description = "Path to a specific source file (e.g. entry point). Reserved for future use.",
};

var sourceRootOption = new Option<string?>("--source-root")
{
    Description = "Project root folder for Roslyn source analysis. When not specified, auto-detected " +
                  "by walking up from the DLL path to the first directory containing a .csproj. " +
                  "Pass explicitly only when sources and build artifacts are in separate directory trees.",
};

var contactNameOption = new Option<string?>("--contact-name")
{
    Description = "Name of the contact person or organisation (written to info.contact.name)",
};

var contactEmailOption = new Option<string?>("--contact-email")
{
    Description = "Email address of the API contact (written to info.contact.email)",
};

var contactUrlOption = new Option<string?>("--contact-url")
{
    Description = "URL of the contact information page — must be an absolute URI (info.contact.url)",
};

var licenseNameOption = new Option<string?>("--license-name")
{
    Description = "SPDX license name, e.g. MIT or Apache-2.0 (written to info.license.name)",
};

var licenseUrlOption = new Option<string?>("--license-url")
{
    Description = "URL pointing to the full license text — must be an absolute URI (info.license.url)",
};

var termsOfServiceOption = new Option<string?>("--terms-of-service")
{
    Description = "URL to the Terms of Service for the API — must be an absolute URI (info.termsOfService)",
};

var serversOption = new Option<string[]>("--server")
{
    Description = "Server base URL to include in the servers array (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var pathBaseEmissionOption = new Option<string>("--path-base-emission")
{
    Description = "How to emit the path base detected via app.UsePathBase(): " +
                  "'prefix' (default) prepends to every path, " +
                  "'servers' adds a relative servers[] entry",
    DefaultValueFactory = _ => "prefix",
};

var noEnumAutoDescriptionOption = new Option<bool>("--no-enum-auto-description")
{
    Description = "Disable automatic markdown description on enum schemas. " +
                  "When set, schema.description gets only the type-level summary (old behavior). " +
                  "x-enum-descriptions is still emitted when values are documented.",
    DefaultValueFactory = _ => false,
};

var noEnumVarnamesOption = new Option<bool>("--no-enum-varnames")
{
    Description = "Disable the x-enum-varnames extension on enum schemas.",
    DefaultValueFactory = _ => false,
};

// ── Root command ──────────────────────────────────────────────────────────────

var rootCommand = new RootCommand(
    "Extract an OpenAPI specification from a compiled .NET assembly without running the application");

// Remove the built-in --version option so our --version (document version) option is unambiguous.
// The framework's VersionOption occupies the same name and short-circuits normal command handling.
var builtInVersionOption = rootCommand.Options.OfType<VersionOption>().FirstOrDefault();
if (builtInVersionOption != null)
    rootCommand.Options.Remove(builtInVersionOption);

rootCommand.Options.Add(assemblyOption);
rootCommand.Options.Add(outputOption);
rootCommand.Options.Add(formatOption);
rootCommand.Options.Add(titleOption);
rootCommand.Options.Add(versionOption);
rootCommand.Options.Add(descriptionOption);
rootCommand.Options.Add(xmlOption);
rootCommand.Options.Add(namingPolicyOption);
rootCommand.Options.Add(enumAsStringOption);
rootCommand.Options.Add(openapiVersionOption);
rootCommand.Options.Add(excludePathsOption);
rootCommand.Options.Add(sourceOption);
rootCommand.Options.Add(sourceRootOption);
rootCommand.Options.Add(contactNameOption);
rootCommand.Options.Add(contactEmailOption);
rootCommand.Options.Add(contactUrlOption);
rootCommand.Options.Add(licenseNameOption);
rootCommand.Options.Add(licenseUrlOption);
rootCommand.Options.Add(termsOfServiceOption);
rootCommand.Options.Add(serversOption);
rootCommand.Options.Add(pathBaseEmissionOption);
rootCommand.Options.Add(noEnumAutoDescriptionOption);
rootCommand.Options.Add(noEnumVarnamesOption);
// Validation options
rootCommand.Options.Add(validateOption);
rootCommand.Options.Add(skipRuleOption);
rootCommand.Options.Add(minDescLengthOption);
rootCommand.Options.Add(excludeValidationPathOption);
rootCommand.Options.Add(validationReportOption);
rootCommand.Options.Add(strictOption);
rootCommand.Options.Add(warnRuleOption);
rootCommand.Options.Add(errorRuleOption);
rootCommand.Options.Add(enableRuleOption);
rootCommand.Options.Add(requireResponseCodeOption);
rootCommand.Options.Add(ruleMinLengthOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var assembly     = parseResult.GetValue(assemblyOption)!;
    var output       = parseResult.GetValue(outputOption)!;
    var format       = parseResult.GetValue(formatOption)!;
    var title        = parseResult.GetValue(titleOption);
    var version      = parseResult.GetValue(versionOption)!;
    var description  = parseResult.GetValue(descriptionOption);
    var xmlPaths     = parseResult.GetValue(xmlOption);
    var namingPolicy = parseResult.GetValue(namingPolicyOption);
    var enumAsStr    = parseResult.GetValue(enumAsStringOption);
    var openapiVer   = parseResult.GetValue(openapiVersionOption)!;
    var excludePaths = parseResult.GetValue(excludePathsOption);
    var source       = parseResult.GetValue(sourceOption);
    var sourceRoot   = parseResult.GetValue(sourceRootOption);
    var contactName  = parseResult.GetValue(contactNameOption);
    var contactEmail = parseResult.GetValue(contactEmailOption);
    var contactUrl   = parseResult.GetValue(contactUrlOption);
    var licenseName  = parseResult.GetValue(licenseNameOption);
    var licenseUrl   = parseResult.GetValue(licenseUrlOption);
    var termsOfSvc   = parseResult.GetValue(termsOfServiceOption);
    var servers                 = parseResult.GetValue(serversOption);
    var pathBaseEmission        = parseResult.GetValue(pathBaseEmissionOption)!;
    var noEnumAutoDescription   = parseResult.GetValue(noEnumAutoDescriptionOption);
    var noEnumVarnames          = parseResult.GetValue(noEnumVarnamesOption);
    var doValidate              = parseResult.GetValue(validateOption);
    var skipRules         = parseResult.GetValue(skipRuleOption);
    var minDescLen        = parseResult.GetValue(minDescLengthOption);
    var excludeValPaths   = parseResult.GetValue(excludeValidationPathOption);
    var validationReport  = parseResult.GetValue(validationReportOption);
    var isStrict          = parseResult.GetValue(strictOption);
    var warnRules         = parseResult.GetValue(warnRuleOption);
    var errorRules        = parseResult.GetValue(errorRuleOption);
    var enableRules       = parseResult.GetValue(enableRuleOption);
    var requireRespCodes  = parseResult.GetValue(requireResponseCodeOption);
    var ruleMinLengths    = parseResult.GetValue(ruleMinLengthOption);

    // ── Warn if any explicitly provided --xml path does not exist ───────────
    List<string>? validXmlPaths = null;
    if (xmlPaths is { Length: > 0 })
    {
        validXmlPaths = new List<string>(xmlPaths.Length);
        foreach (var xmlPath in xmlPaths)
        {
            if (!File.Exists(xmlPath))
            {
                Console.Error.WriteLine(
                    $"Warning: --xml '{xmlPath}' does not exist. Skipping this XML source.");
            }
            else
            {
                validXmlPaths.Add(xmlPath);
            }
        }
    }

    // ── Warn on unrecognized rule IDs ─────────────────────────────────────────
    WarnUnknownRuleIds(skipRules,   "--skip-rule");
    WarnUnknownRuleIds(warnRules,   "--warn-rule");
    WarnUnknownRuleIds(errorRules,  "--error-rule");
    WarnUnknownRuleIds(enableRules, "--enable-rule");

    // ── Parse --require-response-code entries ─────────────────────────────────
    var parsedRequiredCodes = ParseRequireResponseCodes(requireRespCodes);

    // ── Parse --rule-min-length entries ───────────────────────────────────────
    var parsedRuleMinLengths = ParseRuleMinLengths(ruleMinLengths);

    // ── Resolve naming policy ─────────────────────────────────────────────────
    JsonNamingPolicy? resolvedNamingPolicy = null;

    if (!string.IsNullOrEmpty(namingPolicy))
    {
        resolvedNamingPolicy = namingPolicy!.ToLowerInvariant() switch
        {
            "preserve"         => JsonNamingPolicy.Preserve,
            "camelcase"        => JsonNamingPolicy.CamelCase,
            "snake_case_lower" => JsonNamingPolicy.SnakeCaseLower,
            "snake_case_upper" => JsonNamingPolicy.SnakeCaseUpper,
            "kebab-case-lower" => JsonNamingPolicy.KebabCaseLower,
            "kebab-case-upper" => JsonNamingPolicy.KebabCaseUpper,
            _                  => (JsonNamingPolicy?)null,
        };

        if (resolvedNamingPolicy == null)
        {
            Console.Error.WriteLine(
                $"Error: Unknown --naming-policy '{namingPolicy}'. " +
                "Use: preserve, camelCase, snake_case_lower, snake_case_upper, kebab-case-lower, kebab-case-upper");
            return 2;
        }
    }
    // If not specified, resolvedNamingPolicy stays null → defaults to CamelCase in builder

    // ── Validate assembly path ────────────────────────────────────────────────
    if (!assembly.Exists)
    {
        Console.Error.WriteLine($"Error: Assembly not found: {assembly.FullName}");
        return 2;
    }

    // ── Validate format ───────────────────────────────────────────────────────
    if (!format.Equals("json", StringComparison.OrdinalIgnoreCase)
        && !format.Equals("yaml", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Error: Unknown format '{format}'. Use 'json' or 'yaml'.");
        return 2;
    }

    // ── Validate openapi version ──────────────────────────────────────────────
    if (openapiVer is not ("3.0" or "3.1" or "3.2"))
    {
        Console.Error.WriteLine(
            $"Error: Unknown OpenAPI version '{openapiVer}'. Use 3.0, 3.1, or 3.2.");
        return 2;
    }

    // ── Validate path-base-emission ───────────────────────────────────────────
    if (!pathBaseEmission.Equals("prefix", StringComparison.OrdinalIgnoreCase)
        && !pathBaseEmission.Equals("servers", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"Error: Unknown --path-base-emission value '{pathBaseEmission}'. Use 'prefix' or 'servers'.");
        return 2;
    }

    var pathBaseEmissionMode = pathBaseEmission.Equals("servers", StringComparison.OrdinalIgnoreCase)
        ? PathBaseEmission.ServersEntry
        : PathBaseEmission.PathPrefix;

    try
    {
        // ── Build document ────────────────────────────────────────────────────
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath        = assembly.FullName,
            XmlPaths            = validXmlPaths is { Count: > 0 } ? validXmlPaths : null,
            Title               = title,
            Version             = version,
            Description         = description,
            NamingPolicy        = resolvedNamingPolicy,
            EnumAsString        = enumAsStr,
            ExcludePathPrefixes = excludePaths is { Length: > 0 } ? excludePaths : null,
            SourcePath          = source,
            SourceRoot          = sourceRoot,
            ContactName         = contactName,
            ContactEmail        = contactEmail,
            ContactUrl          = contactUrl,
            LicenseName         = licenseName,
            LicenseUrl          = licenseUrl,
            TermsOfService      = termsOfSvc,
            Servers             = servers is { Length: > 0 } ? servers : null,
            PathBaseEmission    = pathBaseEmissionMode,
            EnumAutoDescription = !noEnumAutoDescription,
            EnumVarnames        = !noEnumVarnames,
        };

        OpenApiDocument document;
        ValidationResult? validationResult = null;

        if (doValidate)
        {
            // Build effective excluded path prefixes for validation (no hardcoded defaults)
            IReadOnlyList<string> excludedValPaths = excludeValPaths is { Length: > 0 }
                ? (IReadOnlyList<string>)excludeValPaths
                : [];

            var severityOverrides = BuildSeverityOverrides(isStrict, warnRules, errorRules);

            // Map --openapi-version string to enum for version-conditional rules (e.g. spec.no-ref-siblings)
            var specVersionForValidation = openapiVer switch
            {
                "3.1" => (OpenApiSpecVersion?)OpenApiSpecVersion.OpenApi3_1,
                "3.2" => (OpenApiSpecVersion?)OpenApiSpecVersion.OpenApi3_2,
                _     => (OpenApiSpecVersion?)OpenApiSpecVersion.OpenApi3_0,
            };

            var validationContext = new ValidationContext
            {
                MinDescriptionLength = minDescLen,
                ExcludedPathPrefixes = excludedValPaths,
                SkippedRuleIds = skipRules is { Length: > 0 }
                    ? new HashSet<string>(skipRules, StringComparer.Ordinal)
                    : (IReadOnlySet<string>)new HashSet<string>(),
                EnabledRuleIds = enableRules is { Length: > 0 }
                    ? new HashSet<string>(enableRules, StringComparer.Ordinal)
                    : (IReadOnlySet<string>)new HashSet<string>(),
                SeverityOverrides = severityOverrides,
                OpenApiSpecVersion = specVersionForValidation,
                RequiredResponseCodes = parsedRequiredCodes.Count > 0 ? parsedRequiredCodes : null,
                MinDescriptionLengthPerRule = parsedRuleMinLengths.Count > 0 ? parsedRuleMinLengths : null,
            };

            document = OpenApiDocumentBuilder.BuildWithValidation(options, validationContext, out var result);
            validationResult = result;
        }
        else
        {
            document = OpenApiDocumentBuilder.Build(options);
        }

        // ── Choose spec version ───────────────────────────────────────────────
        var specVersion = openapiVer switch
        {
            "3.1" => OpenApiSpecVersion.OpenApi3_1,
            "3.2" => OpenApiSpecVersion.OpenApi3_2,
            _     => OpenApiSpecVersion.OpenApi3_0,
        };

        // ── Ensure output directory exists ────────────────────────────────────
        var outputPath = Path.GetFullPath(output);
        var outputDir  = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // ── Serialize ─────────────────────────────────────────────────────────
        await using var stream = File.Open(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (format.Equals("yaml", StringComparison.OrdinalIgnoreCase))
            await document.SerializeAsYamlAsync(stream, specVersion);
        else
            await document.SerializeAsJsonAsync(stream, specVersion);

        // ── Summary output ────────────────────────────────────────────────────
        // When validation is active, route extraction summary to stderr so stdout
        // can carry the JSON report without mixing human and machine output.
        var summaryWriter = doValidate ? Console.Error : Console.Out;
        summaryWriter.WriteLine($"OpenAPI specification written to {outputPath}");
        summaryWriter.WriteLine($"  Format:   {format.ToUpperInvariant()}");
        summaryWriter.WriteLine($"  Version:  OpenAPI {openapiVer}");
        summaryWriter.WriteLine($"  Paths:    {document.Paths?.Count ?? 0}");
        summaryWriter.WriteLine($"  Schemas:  {document.Components?.Schemas?.Count ?? 0}");

        // ── Validation report ─────────────────────────────────────────────────
        if (validationResult != null)
        {
            var specPath = outputPath;
            var reportJson = ValidationReportWriter.ToJson(validationResult, specPath);

            if (!string.IsNullOrEmpty(validationReport))
            {
                var reportDir = Path.GetDirectoryName(Path.GetFullPath(validationReport));
                if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
                    Directory.CreateDirectory(reportDir);

                await File.WriteAllTextAsync(validationReport, reportJson, cancellationToken);
                Console.Error.WriteLine($"Validation: {validationResult.Count} violation(s) " +
                    $"({validationResult.ErrorCount} error(s), {validationResult.WarningCount} warning(s)). " +
                    $"Report written to {validationReport}");
            }
            else
            {
                // Print JSON to stdout
                Console.WriteLine(reportJson);
            }

            // Exit 1 only on Error-severity violations. Warnings are reported but don't fail.
            if (validationResult.ErrorCount > 0)
                return 1;
        }

        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: File not found - {ex.FileName ?? ex.Message}");
        if (Environment.GetEnvironmentVariable("DOTNET_OPENAPI_VERBOSE") == "1")
            Console.Error.WriteLine(ex.ToString());
        return 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (Environment.GetEnvironmentVariable("DOTNET_OPENAPI_VERBOSE") == "1")
            Console.Error.WriteLine(ex.ToString());
        return 2;
    }
});

// ── 'validate' subcommand ─────────────────────────────────────────────────────

var specOption = new Option<FileInfo>("--spec")
{
    Description = "Path to the OpenAPI spec file (JSON or YAML)",
    Required = true,
};

// Reuse the validation options for the subcommand (same declarations)
var validateSubSkipRuleOption = new Option<string[]>("--skip-rule")
{
    Description = "Disable a specific validation rule by ID (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var validateSubMinDescOption = new Option<int>("--min-description-length")
{
    Description = "Minimum characters required for description fields (default: 5)",
    DefaultValueFactory = _ => 5,
};

var validateSubExcludePathOption = new Option<string[]>("--exclude-validation-path")
{
    Description = "Path prefixes skipped by rules that require error responses (operation.has-error-response) " +
                  "and success responses (operation.success-response). Other rules are not affected. " +
                  "Repeatable. Default: none.",
    AllowMultipleArgumentsPerToken = false,
};

var validateSubStrictOption = new Option<bool>("--strict")
{
    Description = "Treat all Warning-severity violations as errors (exit 1 on any violation)",
};

var validateSubWarnRuleOption = new Option<string[]>("--warn-rule")
{
    Description = "Demote a specific rule from error to warning severity (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var validateSubErrorRuleOption = new Option<string[]>("--error-rule")
{
    Description = "Promote a specific rule from warning to error severity (can be specified multiple times)",
    AllowMultipleArgumentsPerToken = false,
};

var validateSubEnableRuleOption = new Option<string[]>("--enable-rule")
{
    Description = "Enable a rule that is off by default (can be specified multiple times). " +
                  $"Off-by-default rule IDs: {string.Join(", ", CoreValidator.DefaultOffRuleIds)}",
    AllowMultipleArgumentsPerToken = false,
};

var validateSubReportOption = new Option<string?>("--validation-report")
{
    Description = "Write JSON validation report to this file path (if omitted, report is printed to stdout)",
};

var validateSubRequireResponseCodeOption = new Option<string[]>("--require-response-code")
{
    Description = "Required response codes for specific HTTP methods. Format: METHOD:CODE. Method can be: " +
                  "GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS, mutating (POST/PUT/PATCH/DELETE), " +
                  "safe (GET/HEAD/OPTIONS), or * for any. Repeatable. Activates " +
                  "operation.has-required-response-codes rule (requires --enable-rule). " +
                  "Example: --require-response-code mutating:422 --require-response-code *:401",
    AllowMultipleArgumentsPerToken = false,
};

var validateSubRuleMinLengthOption = new Option<string[]>("--rule-min-length")
{
    Description = "Override --min-description-length for a specific rule. Format: RULE-ID:N. Repeatable. " +
                  "Applies to rules that check description length. " +
                  "Example: --rule-min-length enum.value-description:5 --rule-min-length operation.description:30",
    AllowMultipleArgumentsPerToken = false,
};

var validateCommand = new Command("validate",
    $"Validate an existing OpenAPI spec file against {CoreValidator.AllRuleIds.Count} completeness rules. " +
    $"Off-by-default rules (require --enable-rule): {string.Join(", ", CoreValidator.DefaultOffRuleIds)}. " +
    $"Rule IDs: {allRuleIds}.");

validateCommand.Options.Add(specOption);
validateCommand.Options.Add(validateSubSkipRuleOption);
validateCommand.Options.Add(validateSubMinDescOption);
validateCommand.Options.Add(validateSubExcludePathOption);
validateCommand.Options.Add(validateSubReportOption);
validateCommand.Options.Add(validateSubStrictOption);
validateCommand.Options.Add(validateSubWarnRuleOption);
validateCommand.Options.Add(validateSubErrorRuleOption);
validateCommand.Options.Add(validateSubEnableRuleOption);
validateCommand.Options.Add(validateSubRequireResponseCodeOption);
validateCommand.Options.Add(validateSubRuleMinLengthOption);

validateCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var specFile   = parseResult.GetValue(specOption)!;
    var skipRules  = parseResult.GetValue(validateSubSkipRuleOption);
    var minDescLen = parseResult.GetValue(validateSubMinDescOption);
    var excludeValPaths   = parseResult.GetValue(validateSubExcludePathOption);
    var validationReport  = parseResult.GetValue(validateSubReportOption);
    var isStrict          = parseResult.GetValue(validateSubStrictOption);
    var warnRules         = parseResult.GetValue(validateSubWarnRuleOption);
    var errorRules        = parseResult.GetValue(validateSubErrorRuleOption);
    var enableRules       = parseResult.GetValue(validateSubEnableRuleOption);
    var requireRespCodes  = parseResult.GetValue(validateSubRequireResponseCodeOption);
    var ruleMinLengths    = parseResult.GetValue(validateSubRuleMinLengthOption);

    // ── Warn on unrecognized rule IDs ─────────────────────────────────────────
    WarnUnknownRuleIds(skipRules,   "--skip-rule");
    WarnUnknownRuleIds(warnRules,   "--warn-rule");
    WarnUnknownRuleIds(errorRules,  "--error-rule");
    WarnUnknownRuleIds(enableRules, "--enable-rule");

    // ── Parse --require-response-code entries ─────────────────────────────────
    var parsedRequiredCodes = ParseRequireResponseCodes(requireRespCodes);

    // ── Parse --rule-min-length entries ───────────────────────────────────────
    var parsedRuleMinLengths = ParseRuleMinLengths(ruleMinLengths);

    if (!specFile.Exists)
    {
        Console.Error.WriteLine($"Error: Spec file not found: {specFile.FullName}");
        return 2;
    }

    // ── Parse the spec file ───────────────────────────────────────────────────
    OpenApiDocument document;
    OpenApiSpecVersion? loadedSpecVersion = null;
    try
    {
        var readSettings = new OpenApiReaderSettings();
        readSettings.TryAddReader("yaml", new OpenApiYamlReader());
        var readResult = await OpenApiDocument.LoadAsync(specFile.FullName, readSettings, cancellationToken);

        if (readResult.Document == null)
        {
            Console.Error.WriteLine($"Error: Failed to parse OpenAPI spec: {specFile.FullName}");
            if (readResult.Diagnostic?.Errors != null)
            {
                foreach (var err in readResult.Diagnostic.Errors)
                    Console.Error.WriteLine($"  {err.Message}");
            }
            return 2;
        }

        document = readResult.Document;
        // Capture the spec version from the diagnostic for version-conditional rules
        // (e.g. spec.no-ref-siblings skips on 3.1/3.2 where $ref siblings are legal).
        loadedSpecVersion = readResult.Diagnostic?.SpecificationVersion;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: Failed to read spec file: {ex.Message}");
        if (Environment.GetEnvironmentVariable("DOTNET_OPENAPI_VERBOSE") == "1")
            Console.Error.WriteLine(ex.ToString());
        return 2;
    }

    // ── Build validation context (standalone — no CLR bindings) ──────────────
    IReadOnlyList<string> excludedValPaths = excludeValPaths is { Length: > 0 }
        ? (IReadOnlyList<string>)excludeValPaths
        : [];

    var severityOverrides = BuildSeverityOverrides(isStrict, warnRules, errorRules);

    var validationContext = new ValidationContext
    {
        MinDescriptionLength = minDescLen,
        ExcludedPathPrefixes = excludedValPaths,
        SkippedRuleIds = skipRules is { Length: > 0 }
            ? new HashSet<string>(skipRules, StringComparer.Ordinal)
            : (IReadOnlySet<string>)new HashSet<string>(),
        EnabledRuleIds = enableRules is { Length: > 0 }
            ? new HashSet<string>(enableRules, StringComparer.Ordinal)
            : (IReadOnlySet<string>)new HashSet<string>(),
        SeverityOverrides = severityOverrides,
        // No CLR bindings in standalone mode
        ActionByOperationKey = null,
        TypeBySchemaId       = null,
        SourceContext        = null,
        OpenApiSpecVersion   = loadedSpecVersion,
        RequiredResponseCodes = parsedRequiredCodes.Count > 0 ? parsedRequiredCodes : null,
        MinDescriptionLengthPerRule = parsedRuleMinLengths.Count > 0 ? parsedRuleMinLengths : null,
    };

    var result = CoreValidator.Validate(document, validationContext);

    // ── Output report ─────────────────────────────────────────────────────────
    var reportJson = ValidationReportWriter.ToJson(result, specFile.FullName);

    if (!string.IsNullOrEmpty(validationReport))
    {
        var reportDir = Path.GetDirectoryName(Path.GetFullPath(validationReport));
        if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
            Directory.CreateDirectory(reportDir);

        await File.WriteAllTextAsync(validationReport, reportJson, cancellationToken);
        Console.Error.WriteLine($"Validation: {result.Count} violation(s) " +
            $"({result.ErrorCount} error(s), {result.WarningCount} warning(s)). " +
            $"Report written to {validationReport}");
    }
    else
    {
        Console.WriteLine(reportJson);
    }

    // Exit 1 only on Error-severity violations. Warnings are reported but don't fail.
    return result.ErrorCount > 0 ? 1 : 0;
});

rootCommand.Subcommands.Add(validateCommand);

return await rootCommand.Parse(args).InvokeAsync();

// ── Helper: build severity overrides dictionary ────────────────────────────────
/// <summary>
/// Builds a severity overrides dictionary from CLI flags.
/// Priority: explicit --warn-rule / --error-rule wins over --strict.
/// --strict promotes remaining warnings (not explicitly overridden) to errors.
/// </summary>
static IReadOnlyDictionary<string, ValidationSeverity>? BuildSeverityOverrides(
    bool strict,
    string[]? warnRules,
    string[]? errorRules)
{
    // Collect explicit per-rule overrides
    var explicit_ = new Dictionary<string, ValidationSeverity>(StringComparer.Ordinal);

    if (warnRules is { Length: > 0 })
        foreach (var id in warnRules)
            explicit_[id] = ValidationSeverity.Warning;

    if (errorRules is { Length: > 0 })
        foreach (var id in errorRules)
            explicit_[id] = ValidationSeverity.Error;

    if (!strict && explicit_.Count == 0)
        return null;

    var overrides = new Dictionary<string, ValidationSeverity>(explicit_, StringComparer.Ordinal);

    if (strict)
    {
        // Promote every warning rule to error, unless it has an explicit override
        foreach (var rule in CoreValidator.AllRules)
        {
            if (rule.DefaultSeverity == ValidationSeverity.Warning && !explicit_.ContainsKey(rule.Id))
                overrides[rule.Id] = ValidationSeverity.Error;
        }
    }

    return overrides.Count > 0 ? overrides : null;
}

// ── Helper: warn about unrecognized rule IDs ───────────────────────────────────
/// <summary>
/// Writes a warning to stderr for each rule ID in <paramref name="ruleIds"/> that is not a
/// recognized built-in rule. Helps catch typos in --skip-rule, --warn-rule, etc.
/// </summary>
static void WarnUnknownRuleIds(string[]? ruleIds, string flagName)
{
    if (ruleIds is not { Length: > 0 }) return;

    var unknown = CoreValidator.FindUnknownRuleIds(ruleIds);
    foreach (var id in unknown)
        Console.Error.WriteLine($"Warning: {flagName} '{id}' is not a valid rule ID.");
}

// ── Helper: parse --require-response-code entries ─────────────────────────────
/// <summary>
/// Parses <c>METHOD:CODE</c> entries from the <c>--require-response-code</c> flag.
/// Writes warnings to stderr for invalid entries and skips them.
/// </summary>
static List<(string MethodFilter, int Code)> ParseRequireResponseCodes(string[]? entries)
{
    var result = new List<(string MethodFilter, int Code)>();
    if (entries is not { Length: > 0 }) return result;

    foreach (var entry in entries)
    {
        var colonIndex = entry.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex < 0)
        {
            Console.Error.WriteLine(
                $"Warning: --require-response-code '{entry}' is invalid (expected METHOD:CODE format). Skipping.");
            continue;
        }

        var methodPart = entry[..colonIndex].Trim();
        var codePart   = entry[(colonIndex + 1)..].Trim();

        // Validate method filter
        if (!DotNetOpenApiExtract.Core.Validation.Rules.OperationHasRequiredResponseCodesRule.IsValidMethodFilter(methodPart))
        {
            Console.Error.WriteLine(
                $"Warning: --require-response-code '{entry}' has unknown method filter '{methodPart}'. " +
                "Valid values: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS, mutating, safe, *. Skipping.");
            continue;
        }

        // Validate status code
        if (!int.TryParse(codePart, out var code) || code < 100 || code > 599)
        {
            Console.Error.WriteLine(
                $"Warning: --require-response-code '{entry}' has invalid status code '{codePart}' " +
                "(expected integer 100-599). Skipping.");
            continue;
        }

        result.Add((methodPart, code));
    }

    return result;
}

// ── Helper: parse --rule-min-length entries ───────────────────────────────────
/// <summary>
/// Parses <c>RULE-ID:N</c> entries from the <c>--rule-min-length</c> flag.
/// Writes warnings to stderr for invalid entries and skips them.
/// </summary>
static Dictionary<string, int> ParseRuleMinLengths(string[]? entries)
{
    var result = new Dictionary<string, int>(StringComparer.Ordinal);
    if (entries is not { Length: > 0 }) return result;

    foreach (var entry in entries)
    {
        var colonIndex = entry.LastIndexOf(':');
        if (colonIndex < 0)
        {
            Console.Error.WriteLine(
                $"Warning: --rule-min-length '{entry}' is invalid (expected RULE-ID:N format). Skipping.");
            continue;
        }

        var ruleId = entry[..colonIndex].Trim();
        var nPart  = entry[(colonIndex + 1)..].Trim();

        // Validate rule ID
        var unknownIds = CoreValidator.FindUnknownRuleIds(new[] { ruleId });
        if (unknownIds.Count > 0)
        {
            Console.Error.WriteLine(
                $"Warning: --rule-min-length '{entry}' references unknown rule ID '{ruleId}'. Skipping.");
            continue;
        }

        // Validate N (non-negative integer)
        if (!int.TryParse(nPart, out var n) || n < 0)
        {
            Console.Error.WriteLine(
                $"Warning: --rule-min-length '{entry}' has invalid length '{nPart}' " +
                "(expected non-negative integer). Skipping.");
            continue;
        }

        result[ruleId] = n;
    }

    return result;
}
