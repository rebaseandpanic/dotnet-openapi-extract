using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Product catalog
/// </summary>
[ApiController]
[Route("api/v1/[controller]/[action]")]
[SwaggerTag("Product catalog operations")]
public class ProductsController : ControllerBase
{
    /// <summary>
    /// List all products
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ProductDto>), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "List products", OperationId = "ListProducts")]
    public ActionResult<List<ProductDto>> List()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Get product details
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Get product", OperationId = "GetProduct")]
    public ActionResult<ProductDto> Details([FromRoute] int id)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Absolute route — ignores controller prefix
    /// </summary>
    [HttpGet("/api/v1/catalog/featured")]
    [ProducesResponseType(typeof(List<ProductDto>), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Get featured products", OperationId = "GetFeaturedProducts")]
    public ActionResult<List<ProductDto>> Featured()
    {
        throw new NotImplementedException();
    }
}
