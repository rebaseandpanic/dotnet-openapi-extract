using Microsoft.OpenApi;

namespace SwaggerSetupApi;

/// <summary>The Swagger registration of the service, kept out of Program.cs.</summary>
public static class SwaggerSetup
{
    public static IServiceCollection AddApiSwagger(this IServiceCollection services) =>
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Setup API",
                Version = "v1",
                License = new OpenApiLicense { Name = "MIT" },
            });
            options.AddSecurityDefinition("Key", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Key",
            });
        });
}
