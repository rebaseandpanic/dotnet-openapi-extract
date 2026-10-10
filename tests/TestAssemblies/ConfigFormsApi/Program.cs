using ConfigFormsApi;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Config " + "forms",
        Version = $"{Names.Major}.0",
        Summary = "Summary " + "joined",
        Description = """
            A raw
            description
            """,
        Contact = new() { Name = nameof(Names.Team), Email = Names.ContactEmail, Url = new Uri("https://contact.example.com") },
        TermsOfService = new(Names.TermsUrl),
        License = new() { Name = $"{Names.Product} License", Url = new Uri(Names.LicenseUrl) },
    });

    options.AddSecurityDefinition("KeyHeader", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Name = Names.HeaderName,
        Description = """
            Key in a header
            """,
    });
    options.AddSecurityDefinition("KeyCookie", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Cookie,
        Name = Names.CookieName,
        Description = "Key in a " + "cookie",
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    options.AddSecurityDefinition(nameof(Names.Partner), new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Query,
        Name = $"{Names.Product}-partner",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("KeyHeader", document, null), new List<string>() },
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("KeyCookie", document)] = [],
    });
    options.AddSecurityRequirement(document =>
    {
        return new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference("Bearer", document, null), new List<string>() },
            { new OpenApiSecuritySchemeReference(nameof(Names.Partner), document, null), [] },
        };
    });
});

var app = builder.Build();
app.MapControllers();
app.Run();
