using Asp.Versioning.ApiExplorer;

namespace VersionedApi.Http;

/// <summary>Registers the HTTP surface of the application.</summary>
public static class VersionedHttpSurface
{
    /// <summary>Adds controllers and API versioning.</summary>
    public static IServiceCollection AddVersionedHttpSurface(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();
        ApiVersioningSetup.Add(services, configuration);

        // Turned off, then on again by the same method called a second time — through a method of the
        // project named like the registration: the last assignment wins.
        services.Configure<ApiExplorerOptions>(options => options.SubstituteApiVersionInUrl = false);
        services.AddApiExplorer(configuration);
        return services;
    }

    /// <summary>A method of the project named like the Asp.Versioning registration, called without an options lambda.</summary>
    public static IServiceCollection AddApiExplorer(this IServiceCollection services, IConfiguration configuration)
    {
        ApiVersioningSetup.Add(services, configuration);
        return services;
    }
}
