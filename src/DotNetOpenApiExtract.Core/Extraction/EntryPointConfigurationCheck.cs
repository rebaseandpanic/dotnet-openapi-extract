using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.SourceAnalysis;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Warns when the assembly uses Swashbuckle or Microsoft.AspNetCore.OpenApi but its entry point holds no
/// configuration of them: the configuration then lives outside the entry point — an extension method, a
/// <c>Startup</c> class, an <c>IConfigureOptions&lt;SwaggerGenOptions&gt;</c> — which the extractor does
/// not read, and the document misses it (code <see cref="ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint"/>).
/// </summary>
/// <remarks>
/// The warning must not fire on a project that simply has no configuration. The signals, all read from
/// the entry point:
/// <list type="bullet">
///   <item>A configuration call — <c>SwaggerDoc</c>, <c>AddSecurityDefinition</c>,
///   <c>AddSecurityRequirement</c>, <c>AddTag</c> — anywhere in it (also inside
///   <c>Configure&lt;SwaggerGenOptions&gt;(…)</c>): the configuration is here. No warning.</item>
///   <item><c>AddSwaggerGen</c> / <c>AddOpenApi</c> called without arguments, with a literal document
///   name, or with a lambda that configures the options itself (<c>c =&gt; c.IncludeXmlComments(…)</c>):
///   the registration is here and it configures nothing the document is built from. No warning.</item>
///   <item>The registration hands the options to code elsewhere — a method group
///   (<c>AddSwaggerGen(SwaggerSetup.Configure)</c>), a delegate variable, a lambda passing its options to
///   another method (<c>c =&gt; SwaggerSetup.Configure(c)</c>): the configuration is in that code.
///   Warning — unless that code is declared inside the entry point (a local function of Program.cs), which
///   the tool reads: it is followed, and warns only when it hands the options on outside.</item>
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

        if (ConfigurationCalls.Any(name => InvocationMatcher.FindInvocations(context, name).Any()))
            return;
        var registrations = Registrations.SelectMany(name => InvocationMatcher.FindInvocations(context, name)).ToList();
        var delegated = registrations
            .SelectMany(r => r.ArgumentList.Arguments.Select(a => (Registration: r, Target: DelegationTarget(a.Expression, context, entryPoint, depth: 0))))
            .FirstOrDefault(d => d.Target != null);
        var optionsClass = OptionsClassRegistration(entryPoint);
        if (registrations.Count > 0 && delegated.Target == null && optionsClass == null)
            return;

        const string Consequence =
            "the document lacks its title, description, license, security schemes and requirements. Set them with --title, --version, " +
            "--description, --summary, --contact-name, --contact-email, --contact-url, --license-name, --license-url, " +
            "--license-identifier and --terms-of-service.";
        var packageNames = string.Join(", ", packages);

        // The place: the call that hands the configuration elsewhere; without a registration, the first
        // call named after Swagger / OpenApi (an extension method such as AddApiSwagger()), else the entry point.
        SyntaxNode place;
        string message;
        if (delegated.Target != null)
        {
            place = delegated.Registration;
            message = $"the {InvocationMatcher.GetSimpleMethodName(delegated.Registration.Expression)} options are configured by {delegated.Target}, " +
                      $"declared outside the entry point, which the tool does not read: {Consequence}";
        }
        else if (optionsClass != null)
        {
            place = optionsClass;
            message = $"the Swagger / OpenAPI options are configured by the class registered with {optionsClass}, which the tool does not read: {Consequence}";
        }
        else
        {
            place = entryPoint.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(i => InvocationMatcher.GetSimpleMethodName(i.Expression) is { } name
                    && (name.Contains("Swagger", StringComparison.OrdinalIgnoreCase) || name.Contains("OpenApi", StringComparison.OrdinalIgnoreCase)))
                ?? entryPoint;
            message = $"no Swagger / OpenAPI configuration found in the entry point ({packageNames} is referenced). " +
                      $"If it is in another file (an extension method, Startup, IConfigureOptions<SwaggerGenOptions>), the tool does not read it: {Consequence}";
        }

        var where = SourceLocations.Of(place, context);
        DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
        {
            Code           = ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint,
            Message        = $"{where}: {message}",
            SourceLocation = where,
            Subjects       = [.. packages],
        });
    }

    /// <summary>
    /// The code outside the entry point a registration argument hands the configuration to, or
    /// <see langword="null"/>: a method group or a delegate variable (<c>AddSwaggerGen(SwaggerSetup.Configure)</c>),
    /// or a lambda that passes its options to another method (<c>c =&gt; SwaggerSetup.Configure(c)</c>). A
    /// literal (the document name of <c>AddOpenApi("v1")</c>) and a lambda configuring the options itself do not.
    /// </summary>
    /// <remarks>
    /// Where the target is declared is decided by its symbol, not assumed. A local function, a method or a
    /// delegate variable declared inside the entry point is code the tool reads (its <c>SwaggerDoc</c> and
    /// other configuration calls count as the entry point's), so it is followed in turn: it hands the
    /// configuration elsewhere only when it passes its options on to code outside the entry point. A target
    /// whose symbol cannot be resolved counts as outside.
    /// </remarks>
    private static string? DelegationTarget(ExpressionSyntax argument, SourceAnalysisContext context, SyntaxNode entryPoint, int depth)
    {
        // A chain of local functions in the entry point deeper than this is not followed: it counts as outside.
        const int MaxDepth = 8;

        argument = ObjectCreations.Unwrap(argument);
        if (argument is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            if (depth >= MaxDepth || DeclarationInEntryPoint(argument, context, entryPoint) is not { } declaration)
                return argument.ToString();
            return declaration switch
            {
                LocalFunctionStatementSyntax local => PassedOn(local.ParameterList.Parameters, (SyntaxNode?)local.Body ?? local.ExpressionBody, context, entryPoint, depth),
                MethodDeclarationSyntax method => PassedOn(method.ParameterList.Parameters, (SyntaxNode?)method.Body ?? method.ExpressionBody, context, entryPoint, depth),
                VariableDeclaratorSyntax variable when SingleValue(variable, context, entryPoint) is { } value
                    => DelegationTarget(value, context, entryPoint, depth + 1),
                _ => argument.ToString(),
            };
        }

        if (argument is not AnonymousFunctionExpressionSyntax lambda)
            return null;

        List<ParameterSyntax> parameters = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => [.. parenthesized.ParameterList.Parameters],
            AnonymousMethodExpressionSyntax method => method.ParameterList is { } list ? [.. list.Parameters] : [],
            _ => [],
        };
        return PassedOn(parameters, lambda.Body, context, entryPoint, depth);
    }

    /// <summary>
    /// The target outside the entry point of the first call in <paramref name="body"/> that passes one of
    /// <paramref name="parameters"/> on, or <see langword="null"/> when the body configures the options itself.
    /// </summary>
    private static string? PassedOn(
        IEnumerable<ParameterSyntax> parameters, SyntaxNode? body, SourceAnalysisContext context, SyntaxNode entryPoint, int depth)
    {
        if (body == null)
            return null;
        var names = parameters.Select(p => p.Identifier.Text).ToHashSet(StringComparer.Ordinal);
        return body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.ArgumentList.Arguments.Any(a => a.Expression is IdentifierNameSyntax id && names.Contains(id.Identifier.Text)))
            .Select(invocation => DelegationTarget(invocation.Expression, context, entryPoint, depth + 1))
            .FirstOrDefault(target => target != null);
    }

    /// <summary>
    /// The one value a delegate variable of the entry point can hold — its initializer — when that value is a
    /// lambda, an anonymous method or a method group and the variable is a local never written after its
    /// declaration; otherwise <see langword="null"/>, and the variable counts as handing the configuration
    /// outside. A reassignment (<c>=</c>, <c>+=</c>, <c>??=</c>…), a <c>ref</c> / <c>out</c> argument or a
    /// <c>ref</c> alias can give it a value the tool does not see; an initializer computed by a call or a
    /// conditional is not statically one target.
    /// </summary>
    private static ExpressionSyntax? SingleValue(VariableDeclaratorSyntax variable, SourceAnalysisContext context, SyntaxNode entryPoint)
    {
        if (variable.Initializer?.Value is not { } initializer
            || context.CompilationResult?.Compilation is not { } compilation)
            return null;
        var value = ObjectCreations.Unwrap(initializer);
        if (value is not (AnonymousFunctionExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return null;

        var model = compilation.GetSemanticModel(variable.SyntaxTree);
        if (model.GetDeclaredSymbol(variable) is not ILocalSymbol local)
            return null;

        var written = entryPoint.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(id => id.Identifier.Text == local.Name && IsWrite(id))
            .Any(id => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id).Symbol, local));
        return written ? null : value;
    }

    /// <summary>Whether <paramref name="name"/> stands where a value is stored into it or a reference to it is taken.</summary>
    private static bool IsWrite(IdentifierNameSyntax name) => name.Parent switch
    {
        AssignmentExpressionSyntax assignment => assignment.Left == name,
        ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
                                   && !argument.RefKindKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InKeyword),
        RefExpressionSyntax => true,
        PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax => true,
        _ => false,
    };

    /// <summary>
    /// The declaration of the method or variable <paramref name="name"/> refers to, when it is declared
    /// inside <paramref name="entryPoint"/>; otherwise <see langword="null"/>.
    /// </summary>
    private static SyntaxNode? DeclarationInEntryPoint(ExpressionSyntax name, SourceAnalysisContext context, SyntaxNode entryPoint)
    {
        if (context.CompilationResult?.Compilation is not { } compilation || !compilation.ContainsSyntaxTree(name.SyntaxTree))
            return null;
        var info = compilation.GetSemanticModel(name.SyntaxTree).GetSymbolInfo(name);
        var symbols = info.Symbol != null ? [info.Symbol] : info.CandidateSymbols;
        if (symbols.Length != 1)
            return null;
        var references = symbols[0].DeclaringSyntaxReferences;
        if (references.Length != 1 || references[0].SyntaxTree != entryPoint.SyntaxTree || !entryPoint.Span.Contains(references[0].Span))
            return null;
        return references[0].GetSyntax();
    }

    /// <summary>The call of the entry point that registers an options class for SwaggerGen or AddOpenApi, or <see langword="null"/>.</summary>
    private static InvocationExpressionSyntax? OptionsClassRegistration(SyntaxNode entryPoint) =>
        entryPoint.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(invocation =>
            invocation.Expression switch
            {
                MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => IsOptionsClass(generic),
                GenericNameSyntax generic => IsOptionsClass(generic),
                _ => false,
            });

    private static bool IsOptionsClass(GenericNameSyntax generic) =>
        generic.TypeArgumentList.Arguments.Any(argument =>
            OptionsTypes.Any(type => argument.ToString().Contains(type, StringComparison.Ordinal))
            || (generic.Identifier.Text == "ConfigureOptions"
                && (argument.ToString().Contains("Swagger", StringComparison.Ordinal) || argument.ToString().Contains("OpenApi", StringComparison.Ordinal))));
}
