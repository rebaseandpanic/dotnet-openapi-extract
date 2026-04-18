using DotNetOpenApiExtract.Core.SourceAnalysis;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Detects whether <c>services.AddProblemDetails()</c> is registered in the
/// application's entry-point source code via Roslyn analysis.
/// </summary>
public static class ProblemDetailsDetector
{
    /// <summary>
    /// Returns <see langword="true"/> if <c>AddProblemDetails()</c> is called anywhere
    /// within the entry-point method reachable from <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// The source analysis context. When <see cref="SourceAnalysisContext.IsAvailable"/>
    /// is <see langword="false"/> or <see cref="SourceAnalysisContext.EntryPointNode"/>
    /// is <see langword="null"/>, this method returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if at least one invocation of <c>AddProblemDetails</c>
    /// is found in the entry-point scope; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Detection is syntactic — the presence of any method invocation named
    /// <c>AddProblemDetails</c> reachable from the entry point triggers injection.
    /// A user-defined method with the same name (not part of ASP.NET Core) would
    /// produce a false positive. In practice this is extremely rare.
    /// </remarks>
    public static bool IsRegistered(SourceAnalysisContext context)
    {
        if (!context.IsAvailable || context.EntryPointNode is null)
            return false;

        return InvocationMatcher
            .FindInvocations(context, "AddProblemDetails")
            .Any();
    }
}
