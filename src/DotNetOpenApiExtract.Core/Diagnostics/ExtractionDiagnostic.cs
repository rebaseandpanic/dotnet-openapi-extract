using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Diagnostics;

/// <summary>What happened to the field a downlevel diagnostic is about.</summary>
public enum DiagnosticAction
{
    /// <summary>The field is not written to the document.</summary>
    Omitted,

    /// <summary>The field is written under an extension (<see cref="ExtractionDiagnostic.ExtensionName"/>).</summary>
    MovedToExtension,

    /// <summary>The field is written, but consumers of the target version read it differently.</summary>
    SemanticsChanged,
}

/// <summary>
/// One warning produced while a document or a schema is extracted. Diagnostics never stop a
/// build and never change the CLI exit code; errors are exceptions
/// (<see cref="OpenApiConfigurationException"/>, <see cref="OpenApiExtractionException"/>).
/// </summary>
/// <remarks>
/// Subscribe through <see cref="OpenApiDocumentOptions.OnDiagnostic"/> or
/// <see cref="Schema.SchemaOptions.OnDiagnostic"/>. Without a subscriber each diagnostic is printed
/// to <see cref="Console.Error"/> as a single line, as before. Rely on <see cref="Code"/>,
/// <see cref="Location"/> and <see cref="Subjects"/>; <see cref="Message"/> is for people.
/// </remarks>
public sealed record ExtractionDiagnostic
{
    /// <summary>Stable identifier of the kind of diagnostic; see <see cref="ExtractionDiagnosticCodes"/>.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable text: the line printed after <c>Warning: </c> when nobody subscribes.</summary>
    public required string Message { get; init; }

    /// <summary>The OpenAPI version the build targets, for diagnostics that depend on it; otherwise <see langword="null"/>.</summary>
    public OpenApiSpecVersion? TargetVersion { get; init; }

    /// <summary>The field or feature the diagnostic is about, or <see langword="null"/>.</summary>
    public string? Feature { get; init; }

    /// <summary>
    /// Where in the output the diagnostic applies: a JSON pointer into the actual output
    /// (for example <c>#/info/license/url</c>), or <c>METHOD /path</c> for an operation.
    /// <see langword="null"/> when the diagnostic has no place in the document.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Where in the source code the diagnostic comes from, as <c>file:line</c> (the file relative to the
    /// source root, the line 1-based): the value or call in Program.cs the extractor could not read.
    /// <see langword="null"/> when the diagnostic does not come from the source code.
    /// </summary>
    public string? SourceLocation { get; init; }

    /// <summary>What happened to the field, for downlevel diagnostics; otherwise <see langword="null"/>.</summary>
    public DiagnosticAction? Action { get; init; }

    /// <summary>The extension the field was moved into when <see cref="Action"/> is <see cref="DiagnosticAction.MovedToExtension"/>.</summary>
    public string? ExtensionName { get; init; }

    /// <summary>The lowest OpenAPI version that has the field, for downlevel diagnostics; otherwise <see langword="null"/>.</summary>
    public OpenApiSpecVersion? RequiredVersion { get; init; }

    /// <summary>
    /// The entities the diagnostic names (schemes, settings, types, headers…), in a
    /// machine-readable form. Empty when the diagnostic names none.
    /// </summary>
    public IReadOnlyList<string> Subjects { get; init; } = [];

    /// <summary>
    /// The exact line printed to stderr without a subscriber, when it is not
    /// <c>Warning: </c> + <see cref="Message"/> (kept for warnings whose text predates the channel).
    /// </summary>
    internal string? StderrLine { get; init; }

    internal string ToStderrLine() => StderrLine ?? "Warning: " + Message;
}
