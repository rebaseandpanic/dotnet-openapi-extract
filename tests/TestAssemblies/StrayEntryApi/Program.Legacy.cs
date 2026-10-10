using Microsoft.OpenApi;

// An older copy of Program.cs, excluded from compilation in StrayEntryApi.csproj.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Stray", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Stray-Key",
    });
});

var app = builder.Build();
app.MapControllers();
app.Run();
