using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Contexts;

namespace ModernApi.Controllers;

/// <summary>Bodies serialized with the MVC JSON options.</summary>
[ApiController]
[Route("contexts")]
public class SerializationContextsController : ControllerBase
{
    /// <summary>Order summary returned by a controller.</summary>
    [HttpGet("mvc-order")]
    public ActionResult<OrderSummary> MvcOrder() => new OrderSummary { OrderNumber = "A-1", ItemCount = 1 };
}
