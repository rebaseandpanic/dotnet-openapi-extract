using DotNetOpenApiExtract.Core.Diagnostics;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>Kind of a pending loss record.</summary>
internal enum LossClass
{
    /// <summary>A loss caused by the target version: the field exists only in a later version.</summary>
    Degradation,

    /// <summary>A warning about the input itself (for example a body on GET in a 3.0 document).</summary>
    Source,
}

/// <summary>
/// What a pending loss is attached to. Reachability and the final location are computed from the
/// anchor against the finished document, so a record about something the build later drops (an
/// excluded path, an operation that lost a path+method conflict) is never delivered.
/// </summary>
internal abstract record LossAnchor
{
    private LossAnchor()
    {
    }

    /// <summary>The document root: always reachable, never excluded.</summary>
    public sealed record Document : LossAnchor
    {
        /// <summary>The single document anchor.</summary>
        public static Document Instance { get; } = new();

        private Document()
        {
        }
    }

    /// <summary>
    /// An operation object. It is reachable while the finished document still holds this exact
    /// object; its location is <c>METHOD /path</c> as written to the output.
    /// </summary>
    public sealed record Operation(OpenApiOperation Target) : LossAnchor;
}

/// <summary>
/// One warning collected during the build and delivered only after <see cref="DownlevelPass"/>
/// has filtered and ordered the whole set.
/// </summary>
internal sealed record PendingLoss
{
    public required LossClass Class { get; init; }

    public required string Code { get; init; }

    public required LossAnchor Anchor { get; init; }

    /// <summary>
    /// Message text after the location; the delivered message is
    /// <c>OpenAPI {version} target: {location}: {Message}</c>.
    /// </summary>
    public required string Message { get; init; }

    public string? Feature { get; init; }

    public DiagnosticAction? Action { get; init; }

    public OpenApiSpecVersion? RequiredVersion { get; init; }

    public string? ExtensionName { get; init; }

    public IReadOnlyList<string> Subjects { get; init; } = [];
}

/// <summary>
/// Collects the warnings of one build that depend on the target version or on the finished
/// document. Nothing recorded here is delivered until <see cref="DownlevelPass"/> runs.
/// </summary>
internal sealed class LossLedger
{
    private readonly List<PendingLoss> _entries = [];

    public LossLedger(OpenApiSpecVersion targetVersion)
    {
        TargetVersion = targetVersion;
    }

    /// <summary>The version the document is built for.</summary>
    public OpenApiSpecVersion TargetVersion { get; }

    /// <summary>The records in the order they were added.</summary>
    public IReadOnlyList<PendingLoss> Entries => _entries;

    public void Add(PendingLoss entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }
}
