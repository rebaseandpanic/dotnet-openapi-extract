using Asp.Versioning;
using Asp.Versioning.ApiExplorer;

namespace VersionedApi.Http;

/// <summary>API versioning with the version substituted into the URL.</summary>
public static class ApiVersioningSetup
{
    /// <summary>The substitution format: major and minor version.</summary>
    public const string SubstitutionFormat = "VV";

    /// <summary>Registers API versioning and its API explorer.</summary>
    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        // A value from configuration: not a constant, reported; the assignment below wins.
        services.Configure<ApiExplorerOptions>(options =>
            options.SubstituteApiVersionInUrl = configuration.GetValue<bool>("Versioning:Substitute"));

        // Assigned only under a condition: reported; the assignment below wins.
        if (configuration.GetValue<bool>("Versioning:MajorOnly"))
            services.Configure<ApiExplorerOptions>(options => options.SubstitutionFormat = "V");

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
                options.SubstitutionFormat = SubstitutionFormat;
            });
    }
}
