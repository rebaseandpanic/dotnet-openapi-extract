using System.CommandLine;
using DotNetOpenApiExtract.Core;
using Microsoft.OpenApi;

// ── Option declarations ───────────────────────────────────────────────────────

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
    Description = "API title (default: assembly file name without extension)",
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

var xmlOption = new Option<string?>("--xml")
{
    Description = "XML documentation file path (default: auto-detected next to the DLL)",
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
    Description = "Override the auto-detected source root directory (the folder containing .csproj). " +
                  "Use when the project layout does not follow the standard bin/ output convention.",
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

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var assembly     = parseResult.GetValue(assemblyOption)!;
    var output       = parseResult.GetValue(outputOption)!;
    var format       = parseResult.GetValue(formatOption)!;
    var title        = parseResult.GetValue(titleOption);
    var version      = parseResult.GetValue(versionOption)!;
    var description  = parseResult.GetValue(descriptionOption);
    var xml          = parseResult.GetValue(xmlOption);
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
    var servers           = parseResult.GetValue(serversOption);
    var pathBaseEmission  = parseResult.GetValue(pathBaseEmissionOption)!;

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
            return 1;
        }
    }
    // If not specified, resolvedNamingPolicy stays null → defaults to CamelCase in builder

    // ── Validate assembly path ────────────────────────────────────────────────
    if (!assembly.Exists)
    {
        Console.Error.WriteLine($"Error: Assembly not found: {assembly.FullName}");
        return 1;
    }

    // ── Validate format ───────────────────────────────────────────────────────
    if (!format.Equals("json", StringComparison.OrdinalIgnoreCase)
        && !format.Equals("yaml", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Error: Unknown format '{format}'. Use 'json' or 'yaml'.");
        return 1;
    }

    // ── Validate openapi version ──────────────────────────────────────────────
    if (openapiVer is not ("3.0" or "3.1" or "3.2"))
    {
        Console.Error.WriteLine(
            $"Error: Unknown OpenAPI version '{openapiVer}'. Use 3.0, 3.1, or 3.2.");
        return 1;
    }

    // ── Validate path-base-emission ───────────────────────────────────────────
    if (!pathBaseEmission.Equals("prefix", StringComparison.OrdinalIgnoreCase)
        && !pathBaseEmission.Equals("servers", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"Error: Unknown --path-base-emission value '{pathBaseEmission}'. Use 'prefix' or 'servers'.");
        return 1;
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
            XmlPath             = xml,
            Title               = title ?? Path.GetFileNameWithoutExtension(assembly.Name),
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
        };

        var document = OpenApiDocumentBuilder.Build(options);

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
        // Microsoft.OpenApi v3.5.0 extension methods:
        //   SerializeAsJsonAsync(Stream, OpenApiSpecVersion)
        //   SerializeAsYamlAsync(Stream, OpenApiSpecVersion)
        // Defined in Microsoft.OpenApi.OpenApiSerializableExtensions.
        await using var stream = File.Open(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (format.Equals("yaml", StringComparison.OrdinalIgnoreCase))
            await document.SerializeAsYamlAsync(stream, specVersion);
        else
            await document.SerializeAsJsonAsync(stream, specVersion);

        // ── Summary output ────────────────────────────────────────────────────
        Console.WriteLine($"OpenAPI specification written to {outputPath}");
        Console.WriteLine($"  Format:   {format.ToUpperInvariant()}");
        Console.WriteLine($"  Version:  OpenAPI {openapiVer}");
        Console.WriteLine($"  Paths:    {document.Paths?.Count ?? 0}");
        Console.WriteLine($"  Schemas:  {document.Components?.Schemas?.Count ?? 0}");

        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: File not found - {ex.FileName ?? ex.Message}");
        if (Environment.GetEnvironmentVariable("DOTNET_OPENAPI_VERBOSE") == "1")
            Console.Error.WriteLine(ex.ToString());
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (Environment.GetEnvironmentVariable("DOTNET_OPENAPI_VERBOSE") == "1")
            Console.Error.WriteLine(ex.ToString());
        return 1;
    }
});

return await rootCommand.Parse(args).InvokeAsync();
