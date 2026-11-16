using Asp.Versioning.ApiExplorer;
using VersionedApi.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddVersionedHttpSurface(builder.Configuration);

var app = builder.Build();
app.MapControllers();
app.Run();

// Declared, never called: it does not run, so it does not turn the substitution off.
static void DisableSubstitution(IServiceCollection services) =>
    services.Configure<ApiExplorerOptions>(options => options.SubstituteApiVersionInUrl = false);
