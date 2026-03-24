using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Order management
/// </summary>
[ApiController]
[Route("api/v1/orders")]
[SwaggerTag("Order operations")]
public class OrdersController : ControllerBase
{
    /// <summary>
    /// Get order by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Get order by ID", OperationId = "GetOrder")]
    public ActionResult<ApiResponse<OrderDto>> GetOrder([FromRoute] Guid id)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Get order items
    /// </summary>
    [HttpGet("{orderId:guid}/items/{itemId:int}")]
    [ProducesResponseType(typeof(ApiResponse<OrderItemDto>), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Get order item", OperationId = "GetOrderItem")]
    public ActionResult<ApiResponse<OrderItemDto>> GetOrderItem(
        [FromRoute] Guid orderId,
        [FromRoute] int itemId)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Search orders (custom action name)
    /// </summary>
    [HttpGet("find")]
    [ActionName("SearchOrders")]
    [ProducesResponseType(typeof(ApiResponse<List<OrderDto>>), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Search orders", OperationId = "SearchOrders")]
    public ActionResult<ApiResponse<List<OrderDto>>> Find(
        [FromQuery] string? query,
        [FromQuery] DateTimeOffset? fromDate)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// This is not an action
    /// </summary>
    [NonAction]
    public void HelperMethod() { }
}
