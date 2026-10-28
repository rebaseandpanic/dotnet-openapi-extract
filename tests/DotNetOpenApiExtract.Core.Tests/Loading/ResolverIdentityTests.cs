using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Loading;

/// <summary>Application packages and the tool runtime may ship different builds with one assembly identity.</summary>
public sealed class ResolverIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ValidationProblemDetails_Response_Does_Not_Remove_The_Operation(bool nativeDependency)
    {
        using var directory = new TempDirectory();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(TestPaths.PackageJsonApiDll)!))
            File.Copy(file, Path.Combine(directory.Path, Path.GetFileName(file)));
        if (nativeDependency)
            File.WriteAllBytes(Path.Combine(directory.Path, "native-library.dll"), [0, 1, 2, 3]);
        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = Path.Combine(directory.Path, "PackageJsonApi.dll"),
        });

        document.Paths.Should().ContainKey("/api/preferences");
        var path = (OpenApiPathItem)document.Paths!["/api/preferences"];
        path.Operations.Should().ContainKey(HttpMethod.Put);
        var operation = path.Operations![HttpMethod.Put];
        operation.RequestBody.Should().NotBeNull();
        operation.Responses.Should().ContainKey("200").And.ContainKey("400");
        var response = operation.Responses!["400"];
        response.Content!.Values.Should().Contain(media => media.Schema is OpenApiSchemaReference);
        var validation = document.Components!.Schemas!["ValidationProblemDetails"];
        validation.Properties.Should().ContainKey("errors").And.ContainKey("title");
    }
}
