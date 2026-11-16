using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// The <c>Asp.Versioning</c> API explorer options the application's source sets:
/// <c>SubstituteApiVersionInUrl</c> and <c>SubstitutionFormat</c>.
/// </summary>
public sealed class ApiExplorerOptionsExtractionResult
{
    /// <summary>
    /// The value of <c>SubstituteApiVersionInUrl</c>: <see langword="null"/> when the code the tool reads
    /// does not set it, or sets it to a value that is not a constant (reported).
    /// </summary>
    public bool? SubstituteApiVersionInUrl { get; init; }

    /// <summary>
    /// The value of <c>SubstitutionFormat</c>: <see langword="null"/> when the code the tool reads does
    /// not set it, or sets it to a value that is not a constant (reported).
    /// </summary>
    public string? SubstitutionFormat { get; init; }

    /// <summary>
    /// Whether a setting was given a value that is not a constant: the tool does not know it, and the
    /// option that sets it explicitly should be used instead.
    /// </summary>
    public bool HasUnreadableValue { get; init; }

    /// <summary>Where <see cref="SubstituteApiVersionInUrl"/> is set (<c>file:line</c>), or <see langword="null"/>.</summary>
    public string? SubstituteApiVersionInUrlLocation { get; init; }

    internal static readonly ApiExplorerOptionsExtractionResult Empty = new();
}

/// <summary>
/// Reads the <c>Asp.Versioning</c> API explorer options from the source code: the entry point and the
/// methods of the project it calls, transitively — an extension method such as
/// <c>services.AddHttpSurface()</c> that calls <c>AddApiVersioning().AddApiExplorer(o =&gt; …)</c>
/// is read like the entry point itself.
/// </summary>
/// <remarks>
/// The settings are the assignments <c>o.SubstituteApiVersionInUrl = …</c> and
/// <c>o.SubstitutionFormat = …</c> in a lambda passed to <c>AddApiExplorer(…)</c> or to
/// <c>Configure&lt;ApiExplorerOptions&gt;(…)</c>. They are read in the order the code runs — the
/// statements of a method in source order, a called method at each of its calls — and the last
/// assignment wins, as at run time. Only code that runs where it is written is read: the body of a local
/// function, a method or a lambda is read where it is called, never because it is declared. A value must
/// be a constant (a literal or a <c>const</c>), and the assignment must run unconditionally; otherwise it
/// is reported with its <c>file:line</c> (<c>api-version.setting-not-static</c>,
/// <c>api-version.setting-conditional</c>) and the setting is left unknown from there. Not read: a class
/// registered as <c>IConfigureOptions&lt;ApiExplorerOptions&gt;</c>, a <c>Startup</c> class, a method the
/// code passes as a delegate, a lambda passed to anything but the options registration.
/// </remarks>
public static class ApiExplorerOptionsExtractor
{
    private const string SubstituteName = "SubstituteApiVersionInUrl";
    private const string FormatName = "SubstitutionFormat";
    private const string OptionsTypeName = "ApiExplorerOptions";

    /// <summary>
    /// Reads the API explorer options reachable from the entry point of <paramref name="context"/>;
    /// warnings go to <paramref name="onDiagnostic"/> (or to <c>Console.Error</c> when it is
    /// <see langword="null"/>).
    /// </summary>
    public static ApiExplorerOptionsExtractionResult Extract(
        SourceAnalysisContext context, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        if (!context.IsAvailable || context.EntryPointNode is null)
            return ApiExplorerOptionsExtractionResult.Empty;

        var reader = new Reader(context, onDiagnostic);
        reader.Visit(context.EntryPointNode);
        return reader.Result();
    }

    private sealed class Reader(SourceAnalysisContext context, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        private readonly CSharpCompilation _compilation = context.CompilationResult!.Compilation;
        private readonly HashSet<SyntaxNode> _callPath = [];
        private ILookup<string, SyntaxNode>? _methodsByName;

        private bool? _substitute;
        private string? _substituteLocation;
        private string? _format;
        private bool _unreadable;

        public ApiExplorerOptionsExtractionResult Result() => new()
        {
            SubstituteApiVersionInUrl         = _substitute,
            SubstitutionFormat                = _format,
            HasUnreadableValue                = _unreadable,
            SubstituteApiVersionInUrlLocation = _substituteLocation,
        };

        /// <summary>
        /// Reads the code <paramref name="scope"/> runs, in the order it runs it: the settings it assigns and,
        /// at each call of a method of the project, that method's body. <paramref name="conditional"/>: the
        /// scope runs only under a condition the tool does not evaluate.
        /// </summary>
        /// <remarks>
        /// The guard holds the bodies on the current call path only: a method called twice is read at each
        /// call, a recursive call is not read again.
        /// </remarks>
        public void Visit(SyntaxNode scope, bool conditional = false)
        {
            if (!_callPath.Add(scope))
                return;
            try
            {
                Walk(scope, conditional);
            }
            finally
            {
                _callPath.Remove(scope);
            }
        }

        /// <summary>
        /// Walks the children of <paramref name="node"/> in source order. Code that does not run where it is
        /// written is not read: the body of a local function, a lambda or an anonymous method (it runs when
        /// it is called), a type or namespace declaration. Code that runs only under a condition — a branch
        /// of <c>if</c>, <c>switch</c> or <c>?:</c>, the right operand of <c>&amp;&amp;</c>, <c>||</c> or
        /// <c>??</c>, a loop body, a <c>catch</c>, a conditional access — is read as conditional.
        /// </summary>
        private void Walk(SyntaxNode node, bool conditional)
        {
            switch (node)
            {
                case LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax
                    or BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or BaseNamespaceDeclarationSyntax:
                    return;

                case InvocationExpressionSyntax invocation when IsOptionsRegistration(invocation):
                    // The receiver runs first: AddApiVersioning(…).AddMvc() before AddApiExplorer(…).
                    Walk(invocation.Expression, conditional);
                    foreach (var argument in invocation.ArgumentList.Arguments)
                        ApplyRegistration(argument.Expression, conditional);
                    // A method of the project may carry the name (services.AddApiExplorer() of its own, called
                    // without an options lambda): its body runs. A call with an options lambda is the
                    // registration itself, not a wrapper.
                    if (!invocation.ArgumentList.Arguments.Any(a => a.Expression is AnonymousFunctionExpressionSyntax))
                    {
                        foreach (var body in CalledBodies(invocation))
                            Visit(body, conditional);
                    }
                    return;

                case InvocationExpressionSyntax invocation:
                    Walk(invocation.Expression, conditional);
                    Walk(invocation.ArgumentList, conditional);
                    foreach (var body in CalledBodies(invocation))
                        Visit(body, conditional);
                    return;

                case IfStatementSyntax ifStatement:
                    Walk(ifStatement.Condition, conditional);
                    Walk(ifStatement.Statement, conditional: true);
                    if (ifStatement.Else != null)
                        Walk(ifStatement.Else, conditional: true);
                    return;

                case ConditionalExpressionSyntax ternary:
                    Walk(ternary.Condition, conditional);
                    Walk(ternary.WhenTrue, conditional: true);
                    Walk(ternary.WhenFalse, conditional: true);
                    return;

                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression)
                                                       || binary.IsKind(SyntaxKind.LogicalOrExpression)
                                                       || binary.IsKind(SyntaxKind.CoalesceExpression):
                    Walk(binary.Left, conditional);
                    Walk(binary.Right, conditional: true);
                    return;

                case ConditionalAccessExpressionSyntax access:
                    Walk(access.Expression, conditional);
                    Walk(access.WhenNotNull, conditional: true);
                    return;

                case SwitchStatementSyntax switchStatement:
                    Walk(switchStatement.Expression, conditional);
                    foreach (var section in switchStatement.Sections)
                        Walk(section, conditional: true);
                    return;

                case SwitchExpressionSyntax switchExpression:
                    Walk(switchExpression.GoverningExpression, conditional);
                    foreach (var arm in switchExpression.Arms)
                        Walk(arm, conditional: true);
                    return;

                case WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or DoStatementSyntax
                    or CatchClauseSyntax:
                    foreach (var child in node.ChildNodes())
                        Walk(child, conditional: true);
                    return;

                default:
                    foreach (var child in node.ChildNodes())
                        Walk(child, conditional);
                    return;
            }
        }

        /// <summary>
        /// The settings a lambda passed to an options registration assigns, as the code of that lambda runs
        /// them (a branch inside it is conditional too).
        /// </summary>
        private void ApplyRegistration(ExpressionSyntax argument, bool conditional)
        {
            if (argument is not AnonymousFunctionExpressionSyntax lambda)
                return;

            var body = (SyntaxNode?)lambda.Block ?? lambda.ExpressionBody;
            if (body == null)
                return;

            foreach (var assignment in body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: SubstituteName or FormatName })
                    Apply(assignment, conditional || IsConditionalWithin(assignment, body));
            }
        }

        /// <summary>Whether <paramref name="node"/> runs only under a condition inside <paramref name="boundary"/>.</summary>
        private static bool IsConditionalWithin(SyntaxNode node, SyntaxNode boundary)
        {
            for (var child = node; child.Parent != null && child != boundary; child = child.Parent)
            {
                var conditionalChild = child.Parent switch
                {
                    IfStatementSyntax ifStatement           => child != ifStatement.Condition,
                    ElseClauseSyntax                        => true,
                    ConditionalExpressionSyntax ternary     => child != ternary.Condition,
                    BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression)
                                                       || binary.IsKind(SyntaxKind.LogicalOrExpression)
                                                       || binary.IsKind(SyntaxKind.CoalesceExpression) => child == binary.Right,
                    ConditionalAccessExpressionSyntax access => child == access.WhenNotNull,
                    SwitchSectionSyntax or SwitchExpressionArmSyntax or CatchClauseSyntax => true,
                    WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or DoStatementSyntax => true,
                    _ => false,
                };
                if (conditionalChild)
                    return true;
            }

            return false;
        }

        /// <summary><c>AddApiExplorer(…)</c> or <c>Configure&lt;ApiExplorerOptions&gt;(…)</c>.</summary>
        private static bool IsOptionsRegistration(InvocationExpressionSyntax invocation)
        {
            var name = invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name,
                SimpleNameSyntax simple             => simple,
                _                                   => null,
            };

            return name switch
            {
                IdentifierNameSyntax { Identifier.Text: "AddApiExplorer" } => true,
                GenericNameSyntax { Identifier.Text: "AddApiExplorer" or "Configure" } generic =>
                    generic.Identifier.Text == "AddApiExplorer"
                    || generic.TypeArgumentList.Arguments is [var argument] && LastIdentifier(argument) == OptionsTypeName,
                _ => false,
            };
        }

        private static string? LastIdentifier(TypeSyntax type) => type switch
        {
            IdentifierNameSyntax id           => id.Identifier.Text,
            QualifiedNameSyntax qualified     => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax aliased  => aliased.Name.Identifier.Text,
            _                                 => null,
        };

        private void Apply(AssignmentExpressionSyntax assignment, bool conditional)
        {
            var member = ((MemberAccessExpressionSyntax)assignment.Left).Name.Identifier.Text;
            var where = SourceLocations.Of(assignment, context);

            if (conditional)
            {
                // Whether it runs is decided at run time: the setting is unknown from here on.
                if (member == SubstituteName)
                {
                    _substitute = null;
                    _substituteLocation = null;
                }
                else
                {
                    _format = null;
                }

                Unknown(where, member, member == SubstituteName ? "--substitute-api-version-in-url" : "--api-version-format",
                    ExtractionDiagnosticCodes.ApiVersionSettingConditional, "is set under a condition the tool does not evaluate");
                return;
            }

            if (member == SubstituteName)
            {
                if (BoolConstant(assignment.Right) is { } value)
                {
                    _substitute = value;
                    _substituteLocation = where;
                }
                else
                {
                    _substitute = null;
                    _substituteLocation = null;
                    Unknown(where, SubstituteName, "--substitute-api-version-in-url", ExtractionDiagnosticCodes.ApiVersionSettingNotStatic, "is set to a value that is not a constant");
                }

                return;
            }

            if (InvocationMatcher.GetStringValue(assignment.Right, _compilation) is { } format)
            {
                _format = format;
            }
            else
            {
                _format = null;
                Unknown(where, FormatName, "--api-version-format", ExtractionDiagnosticCodes.ApiVersionSettingNotStatic, "is set to a value that is not a constant");
            }
        }

        private void Unknown(string where, string setting, string flag, string code, string what)
        {
            _unreadable = true;
            DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
            {
                Code           = code,
                Message        = $"{where}: ApiExplorerOptions.{setting} {what}; " +
                                 $"the tool does not run the code and leaves it unknown. Set it with {flag}.",
                SourceLocation = where,
                Feature        = "paths",
                Subjects       = [setting],
            });
        }

        private bool? BoolConstant(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax literal)
            {
                if (literal.IsKind(SyntaxKind.TrueLiteralExpression)) return true;
                if (literal.IsKind(SyntaxKind.FalseLiteralExpression)) return false;
            }

            try
            {
                var constant = _compilation.GetSemanticModel(expression.SyntaxTree).GetConstantValue(expression);
                return constant is { HasValue: true, Value: bool value } ? value : null;
            }
            catch (ArgumentException)
            {
                // The expression's tree is not part of the compilation.
                return null;
            }
        }

        /// <summary>
        /// The bodies of the methods of the project <paramref name="invocation"/> may call: the method the
        /// semantic model binds, else — the compilation has no framework references, so a call on a
        /// framework type often does not bind — the methods of that name whose form and parameters fit
        /// the call: an extension method for <c>x.M(…)</c>, a static method of the type named by
        /// <c>T.M(…)</c>, any method or local function for <c>M(…)</c>.
        /// </summary>
        private IEnumerable<SyntaxNode> CalledBodies(InvocationExpressionSyntax invocation)
        {
            var bound = BoundDeclarations(invocation);
            var declarations = bound.Count > 0 ? bound : MatchingDeclarations(invocation);
            foreach (var declaration in declarations)
            {
                SyntaxNode? body = declaration switch
                {
                    MethodDeclarationSyntax method        => (SyntaxNode?)method.Body ?? method.ExpressionBody,
                    LocalFunctionStatementSyntax function => (SyntaxNode?)function.Body ?? function.ExpressionBody,
                    _                                     => null,
                };
                if (body != null)
                    yield return body;
            }
        }

        private List<SyntaxNode> BoundDeclarations(InvocationExpressionSyntax invocation)
        {
            try
            {
                var info = _compilation.GetSemanticModel(invocation.SyntaxTree).GetSymbolInfo(invocation);
                var symbol = info.Symbol as IMethodSymbol;
                return symbol == null
                    ? []
                    : (symbol.ReducedFrom ?? symbol).DeclaringSyntaxReferences.Select(r => r.GetSyntax()).ToList();
            }
            catch (ArgumentException)
            {
                return [];
            }
        }

        private IEnumerable<SyntaxNode> MatchingDeclarations(InvocationExpressionSyntax invocation)
        {
            var name = InvocationMatcher.GetSimpleMethodName(invocation.Expression);
            if (name == null)
                return [];

            _methodsByName ??= context.CompilationResult!.SyntaxTrees
                .SelectMany(tree => tree.GetRoot().DescendantNodes())
                .Where(node => node is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
                .ToLookup(node => node switch
                {
                    MethodDeclarationSyntax method => method.Identifier.Text,
                    _                              => ((LocalFunctionStatementSyntax)node).Identifier.Text,
                }, StringComparer.Ordinal);

            var arguments = invocation.ArgumentList.Arguments.Count;
            return _methodsByName[name].Where(declaration => invocation.Expression switch
            {
                MemberAccessExpressionSyntax member =>
                    declaration is MethodDeclarationSyntax method
                    && (IsExtension(method) && Accepts(method.ParameterList, arguments + 1)
                        || IsStatic(method) && Receiver(member.Expression) == ContainingTypeName(method)
                           && Accepts(method.ParameterList, arguments)),
                _ => declaration switch
                {
                    MethodDeclarationSyntax method        => Accepts(method.ParameterList, arguments),
                    LocalFunctionStatementSyntax function => Accepts(function.ParameterList, arguments),
                    _                                     => false,
                },
            });
        }

        private static bool IsExtension(MethodDeclarationSyntax method) =>
            method.ParameterList.Parameters is [var first, ..] && first.Modifiers.Any(SyntaxKind.ThisKeyword);

        private static bool IsStatic(MethodDeclarationSyntax method) => method.Modifiers.Any(SyntaxKind.StaticKeyword);

        private static string? Receiver(ExpressionSyntax expression) => expression switch
        {
            IdentifierNameSyntax id            => id.Identifier.Text,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            _                                  => null,
        };

        private static string? ContainingTypeName(MethodDeclarationSyntax method) =>
            method.Parent is BaseTypeDeclarationSyntax type ? type.Identifier.Text : null;

        /// <summary>Whether a call with <paramref name="arguments"/> arguments fits <paramref name="parameters"/>.</summary>
        private static bool Accepts(ParameterListSyntax parameters, int arguments)
        {
            var list = parameters.Parameters;
            var hasParams = list.Any(p => p.Modifiers.Any(SyntaxKind.ParamsKeyword));
            var required = list.Count(p => p.Default == null && !p.Modifiers.Any(SyntaxKind.ParamsKeyword));
            return arguments >= required && (hasParams || arguments <= list.Count);
        }
    }
}
