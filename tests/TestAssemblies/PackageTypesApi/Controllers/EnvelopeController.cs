using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using PackageTypesContracts;

namespace PackageTypesApi.Controllers;

/// <summary>An envelope of the application holding a project type and a package type.</summary>
public class Envelope
{
    /// <summary>An item of a library of the application.</summary>
    public ContractItem Item { get; set; } = new();

    /// <summary>Settings of a NuGet package type.</summary>
    public JsonMergeSettings Settings { get; set; } = new();

    public string Undocumented { get; set; } = "";
}

/// <summary>Returns the envelope.</summary>
[ApiController]
[Route("envelope")]
public class EnvelopeController : ControllerBase
{
    /// <summary>Gets the envelope.</summary>
    [HttpGet]
    public ActionResult<Envelope> Get() => new Envelope();
}
