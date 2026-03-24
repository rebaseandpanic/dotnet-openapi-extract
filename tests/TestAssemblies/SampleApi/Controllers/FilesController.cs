using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// File management operations
/// </summary>
[ApiController]
[Route("api/v1/files")]
[SwaggerTag("File upload and download operations")]
public class FilesController : ControllerBase
{
    /// <summary>
    /// Upload a file
    /// </summary>
    /// <param name="file">The file to upload</param>
    /// <param name="category">File category</param>
    /// <param name="overwrite">Whether to overwrite existing file</param>
    /// <param name="ct">Cancellation token</param>
    /// <response code="201">File uploaded successfully</response>
    /// <response code="400">Invalid file</response>
    /// <response code="422">File too large</response>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileUploadResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [SwaggerOperation(Summary = "Upload a file", OperationId = "UploadFile")]
    public async Task<ActionResult<FileUploadResult>> Upload(
        IFormFile file,
        [FromQuery] string category = "general",
        [FromQuery] bool overwrite = false,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Download a file
    /// </summary>
    /// <param name="id">File unique identifier</param>
    /// <param name="apiKey">API key for authentication</param>
    /// <response code="200">File content</response>
    /// <response code="422">File not found</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [SwaggerOperation(Summary = "Download a file", OperationId = "DownloadFile")]
    public ActionResult Download(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Api-Key"), SwaggerParameter("API key for authentication")] string? apiKey)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Get file metadata
    /// </summary>
    /// <param name="id">File identifier</param>
    /// <response code="200">File metadata</response>
    [HttpGet("{id:guid}/metadata")]
    [ProducesResponseType(typeof(ApiResponse<FileMetadata>), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Get file metadata", OperationId = "GetFileMetadata")]
    public ActionResult<ApiResponse<FileMetadata>> GetMetadata([FromRoute] Guid id)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// No explicit ProducesResponseType — should infer from return type
    /// </summary>
    [HttpGet("stats")]
    [SwaggerOperation(Summary = "Get file statistics", OperationId = "GetFileStats")]
    public ActionResult<FileStats> GetStats()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Void return — should be 204
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(Summary = "Delete a file", OperationId = "DeleteFile")]
    public void DeleteFile([FromRoute] Guid id)
    {
        throw new NotImplementedException();
    }
}
