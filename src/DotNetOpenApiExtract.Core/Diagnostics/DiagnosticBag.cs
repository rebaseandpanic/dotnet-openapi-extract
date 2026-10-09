namespace DotNetOpenApiExtract.Core.Diagnostics;

/// <summary>
/// Delivers diagnostics of one build (or one <see cref="Schema.SchemaGenerator"/> instance) to a
/// subscriber, or to <see cref="Console.Error"/> when there is none. A diagnostic repeated with the
/// same code, target version, feature, location and message is delivered once.
/// </summary>
internal sealed class DiagnosticBag
{
    private readonly Action<ExtractionDiagnostic>? _subscriber;
    private readonly HashSet<(string Code, Microsoft.OpenApi.OpenApiSpecVersion? TargetVersion, string? Feature, string? Location, string Message)> _delivered = [];

    public DiagnosticBag(Action<ExtractionDiagnostic>? subscriber)
    {
        _subscriber = subscriber;
    }

    /// <summary>Delivers <paramref name="diagnostic"/> unless an equal one was already delivered by this bag.</summary>
    public void Report(ExtractionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var key = (diagnostic.Code, diagnostic.TargetVersion, diagnostic.Feature, diagnostic.Location, diagnostic.Message);
        if (_delivered.Add(key))
            Deliver(_subscriber, diagnostic);
    }

    /// <summary>
    /// Delivers <paramref name="diagnostic"/> to <paramref name="subscriber"/>, or prints it to
    /// <see cref="Console.Error"/> when <paramref name="subscriber"/> is <see langword="null"/>.
    /// No deduplication: used by the public extractors when called on their own.
    /// </summary>
    public static void Deliver(Action<ExtractionDiagnostic>? subscriber, ExtractionDiagnostic diagnostic)
    {
        if (subscriber != null)
            subscriber(diagnostic);
        else
            Console.Error.WriteLine(diagnostic.ToStderrLine());
    }
}
