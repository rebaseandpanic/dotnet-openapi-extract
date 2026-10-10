using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.SourceAnalysis;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Warns when the assembly uses Swashbuckle or Microsoft.AspNetCore.OpenApi but its entry point holds no
/// configuration of them: the configuration then lives in another file — an extension method, a
/// <c>Startup</c> class, an <c>IConfigureOptions&lt;SwaggerGenOptions&gt;</c> — which the extractor does
/// not read, and the document misses it (code <see cref="ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint"/>).
/// </summary>
/// <remarks>
/// The warning must not fire on a project that simply has no configuration. The signals, all read from
/// the entry point:
/// <list type="bullet">
///   <item>A configuration call — <c>SwaggerDoc</c>, <c>AddSecurityDefinition</c>,
///   <c>AddSecurityRequirement</c>, <c>AddTag</c> — anywhere in it (also inside
///   <c>Configure&lt;SwaggerGenOptions&gt;(…)</c>), or <c>AddSwaggerGen</c> / <c>AddOpenApi</c> called
///   with arguments: the configuration is here. No warning.</item>
///   <item><c>AddSwaggerGen()</c> / <c>AddOpenApi()</c> called without arguments and nothing else: the
///   registration is here and it configures nothing. No warning.</item>
///   <item>The same bare registration next to an options class registered for them — a type argument
///   naming <c>SwaggerGenOptions</c> / <c>OpenApiOptions</c> (<c>AddTransient&lt;IConfigureOptions&lt;SwaggerGenOptions&gt;, X&gt;()</c>),
///   or <c>ConfigureOptions&lt;T&gt;()</c> with a <c>T</c> named after Swagger / OpenApi: the
///   configuration is in that class. Warning.</item>
///   <item>Neither <c>AddSwaggerGen</c> nor <c>AddOpenApi</c> in the entry point, while the assembly
///   references their package: they are called from another file. Warning.</item>
/// </list>
/// The package reference is the gate: an assembly that uses neither library (or only
/// <c>Swashbuckle.AspNetCore.Annotations</c>) is never warned. An assembly whose Swagger registration
/// lives in a shared library it references is not detected — it does not reference the package itself.
/// </remarks>
internal static class EntryPointConfigurationCheck
{
    private static readonly string[] Packages = ["Swashbuckle.AspNetCore.SwaggerGen", "Microsoft.AspNetCore.OpenApi"];

    private static readonly string[] Registrations = ["AddSwaggerGen", "AddOpenApi"];

    private static readonly string[] ConfigurationCalls = ["SwaggerDoc", "AddSecurityDefinition", "AddSecurityRequirement", "AddTag"];

    private static readonly string[] OptionsTypes = ["SwaggerGenOptions", "OpenApiOptions"];

    /// <summary>Reports to <paramref name="onDiagnostic"/> when the configuration is outside the entry point.</summary>
    public static void Check(SourceAnalysisContext context, Assembly assembly, Action<ExtractionDiagnostic> onDiagnostic)
    {
        if (context.EntryPointNode is not { } entryPoint)
            return;
        var packages = assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name != null && Packages.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (packages.Count == 0)
            return;

        var registrations = Registrations.SelectMany(name => InvocationMatcher.FindInvocations(context, name)).ToList();
        if (registrations.Any(r => r.ArgumentList.Arguments.Count > 0)
            || ConfigurationCalls.Any(name => InvocationMatcher.FindInvocations(context, name).Any()))
            return;
        if (registrations.Count > 0 && !RegistersOptionsClass(entryPoint))
            return;

        var where = SourceLocations.Of(entryPoint, context);
        DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
        {
            Code           = ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint,
            Message        = $"{where}: no Swagger / OpenAPI configuration found in the entry point ({string.Join(", ", packages)} is referenced). " +
                             "If it is in another file (an extension method, Startup, IConfigureOptions<SwaggerGenOptions>), the tool does not read it: " +
                             "the document lacks its title, description, license, security schemes and requirements. Set them with --title, --version, " +
                             "--description, --summary, --contact-name, --contact-email, --contact-url, --license-name, --license-url, " +
                             "--license-identifier and --terms-of-service.",
            SourceLocation = where,
            Subjects       = [.. packages],
        });
    }

    /// <summary>Whether the entry point registers an options class for SwaggerGen or AddOpenApi.</summary>
    private static bool RegistersOptionsClass(Microsoft.CodeAnalysis.SyntaxNode entryPoint) =>
        entryPoint.DescendantNodes().OfType<GenericNameSyntax>().Any(generic =>
            generic.TypeArgumentList.Arguments.Any(argument =>
                OptionsTypes.Any(type => argument.ToString().Contains(type, StringComparison.Ordinal))
                || (generic.Identifier.Text == "ConfigureOptions"
                    && (argument.ToString().Contains("Swagger", StringComparison.Ordinal) || argument.ToString().Contains("OpenApi", StringComparison.Ordinal)))));
}
