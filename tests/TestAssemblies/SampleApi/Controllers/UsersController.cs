using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// User management operations
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[SwaggerTag("User management — CRUD operations for users")]
public class UsersController : ControllerBase
{
    /// <summary>
    /// Get all users
    /// </summary>
    /// <remarks>
    /// Returns a paginated list of users. Supports filtering by status.
    /// </remarks>
    /// <param name="status">Filter by user status</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Items per page (max 100)</param>
    /// <response code="200">List of users</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), StatusCodes.Status200OK)]
    [SwaggerOperation(
        Summary = "Get all users",
        Description = "Returns a paginated list of users. Supports filtering by status.",
        OperationId = "GetUsers",
        Tags = new[] { "Users" }
    )]
    public ActionResult<ApiResponse<List<UserDto>>> GetUsers(
        [FromQuery, SwaggerParameter("Filter by user status")] UserStatus? status,
        [FromQuery, SwaggerParameter("Page number (1-based)")] int page = 1,
        [FromQuery, SwaggerParameter("Items per page (max 100)")] int pageSize = 20)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    /// <remarks>
    /// Returns full user details including profile information.
    /// </remarks>
    /// <param name="id">User unique identifier (GUID)</param>
    /// <response code="200">User found</response>
    /// <response code="422">User not found</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [SwaggerOperation(
        Summary = "Get user by ID",
        Description = "Returns full user details including profile information",
        OperationId = "GetUser"
    )]
    public ActionResult<ApiResponse<UserDto>> GetUser(
        [FromRoute, SwaggerParameter("User unique identifier")] Guid id)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    /// <remarks>
    /// Creates a new user account. Email must be unique.
    /// Side effects: sends welcome email via notification service.
    /// </remarks>
    /// <param name="request">User creation data</param>
    /// <response code="201">User created</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="422">Email already exists</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [SwaggerOperation(
        Summary = "Create a new user",
        Description = "Creates a new user account. Email must be unique.",
        OperationId = "CreateUser"
    )]
    public ActionResult<ApiResponse<UserDto>> CreateUser(
        [FromBody] CreateUserRequest request)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Delete user
    /// </summary>
    /// <param name="id">User unique identifier</param>
    /// <response code="204">User deleted</response>
    /// <response code="422">User not found</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [SwaggerOperation(
        Summary = "Delete user",
        Description = "Permanently deletes user and all associated data",
        OperationId = "DeleteUser"
    )]
    public ActionResult DeleteUser(
        [FromRoute, SwaggerParameter("User unique identifier")] Guid id)
    {
        throw new NotImplementedException();
    }
}
