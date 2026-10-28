using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.Run();

/// <summary>A preference whose request uses the application's packaged JSON attributes.</summary>
public sealed class Preference
{
    /// <summary>The saved value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

/// <summary>Preferences with a framework validation error response.</summary>
[ApiController, Route("api/preferences")]
public sealed class PreferencesController : ControllerBase
{
    /// <summary>Replaces the preference.</summary>
    /// <param name="request">The value to save.</param>
    /// <response code="200">The saved preference.</response>
    /// <response code="400">Validation errors.</response>
    [HttpPut]
    [ProducesResponseType(typeof(Preference), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public ActionResult<Preference> Replace([FromBody] Preference request) => Ok(request);
}
