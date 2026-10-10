namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>
/// A <see cref="Uri"/> whose <see cref="ToString"/> is the text it was created from.
/// </summary>
/// <remarks>
/// Microsoft.OpenApi 3.10.2 writes <c>$self</c> (and its 3.0/3.1 extension <c>x-oai-$self</c>) with
/// <see cref="Uri.ToString"/>, which unescapes percent-encodings: <c>a%20b</c> would be written as
/// <c>a b</c>, which is not a URI reference. Other URI fields of the model keep the escapes.
/// </remarks>
internal sealed class VerbatimUri(string uriString, UriKind kind) : Uri(uriString, kind)
{
    public override string ToString() => OriginalString;
}
