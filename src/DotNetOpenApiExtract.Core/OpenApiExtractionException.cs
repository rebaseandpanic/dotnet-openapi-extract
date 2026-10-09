namespace DotNetOpenApiExtract.Core;

/// <summary>
/// Thrown when an annotation in the analysed assembly is provably wrong — the serializer would
/// reject it at runtime — so no truthful document can be extracted. The message names the type
/// and the member; the CLI reports it with exit code 2. Problems that cannot be proven statically
/// are diagnostics instead.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so existing <c>catch</c> blocks keep working.
/// </remarks>
public sealed class OpenApiExtractionException : InvalidOperationException
{
    /// <summary>Initializes a new instance for an annotation on <paramref name="typeName"/>.</summary>
    /// <param name="message">What is wrong and how to fix it.</param>
    /// <param name="typeName">Full name of the type that carries the wrong annotation.</param>
    /// <param name="memberName">Name of the member that carries it, or <see langword="null"/> for a type-level annotation.</param>
    public OpenApiExtractionException(string message, string typeName, string? memberName = null)
        : base(message)
    {
        TypeName   = typeName;
        MemberName = memberName;
    }

    /// <summary>Full name of the type that carries the wrong annotation.</summary>
    public string TypeName { get; }

    /// <summary>Name of the member that carries it, or <see langword="null"/> for a type-level annotation.</summary>
    public string? MemberName { get; }
}
