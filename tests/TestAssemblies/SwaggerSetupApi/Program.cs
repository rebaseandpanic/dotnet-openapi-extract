using SwaggerSetupApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddApiSwagger();

var app = builder.Build();
app.MapControllers();
app.Run();
