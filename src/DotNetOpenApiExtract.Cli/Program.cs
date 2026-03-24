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

var camelCaseOption = new Option<bool>("--camel-case")
{
    Description = "Use camelCase property names in generated schemas",
    DefaultValueFactory = _ => true,
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
rootCommand.Options.Add(camelCaseOption);
rootCommand.Options.Add(enumAsStringOption);
rootCommand.Options.Add(openapiVersionOption);
rootCommand.Options.Add(excludePathsOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var assembly     = parseResult.GetValue(assemblyOption)!;
    var output       = parseResult.GetValue(outputOption)!;
    var format       = parseResult.GetValue(formatOption)!;
    var title        = parseResult.GetValue(titleOption);
    var version      = parseResult.GetValue(versionOption)!;
    var description  = parseResult.GetValue(descriptionOption);
    var xml          = parseResult.GetValue(xmlOption);
    var camelCase    = parseResult.GetValue(camelCaseOption);
    var enumAsStr    = parseResult.GetValue(enumAsStringOption);
    var openapiVer   = parseResult.GetValue(openapiVersionOption)!;
    var excludePaths = parseResult.GetValue(excludePathsOption);

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

    try
    {
        // ── Build document ────────────────────────────────────────────────────
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath           = assembly.FullName,
            XmlPath                = xml,
            Title                  = title ?? Path.GetFileNameWithoutExtension(assembly.Name),
            Version                = version,
            Description            = description,
            CamelCasePropertyNames = camelCase,
            EnumAsString           = enumAsStr,
            ExcludePathPrefixes    = excludePaths is { Length: > 0 } ? excludePaths : null,
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
