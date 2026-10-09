using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace ModernApi.Controllers;

/// <summary>Report rows written as XML or CSV.</summary>
public class ReportRow
{
    /// <summary>Row label.</summary>
    public required string Label { get; set; }
}

/// <summary>File and stream responses, and response media types declared by [SwaggerResponse].</summary>
[ApiController]
[Route("binary")]
public class BinaryContentController : ControllerBase
{
    /// <summary>A file result declared by an attribute.</summary>
    [HttpGet("file-result")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public IActionResult DeclaredFile() => File([1], "application/octet-stream");

    /// <summary>A file stream result returned directly.</summary>
    [HttpGet("file-stream")]
    public FileStreamResult FileStream() => File(new MemoryStream([1]), "application/octet-stream");

    /// <summary>A file content result with a declared media type.</summary>
    [HttpGet("file-content")]
    [Produces("application/pdf")]
    public FileContentResult PdfContent() => File([1], "application/pdf");

    /// <summary>A file content typed result.</summary>
    [HttpGet("http-file-content")]
    public FileContentHttpResult HttpFileContent() => TypedResults.File([1]);

    /// <summary>A file stream typed result.</summary>
    [HttpGet("http-file-stream")]
    public FileStreamHttpResult HttpFileStream() => TypedResults.File(new MemoryStream([1]));

    /// <summary>A raw stream.</summary>
    [HttpGet("stream")]
    public Stream RawStream() => new MemoryStream([1]);

    /// <summary>Uploads a file from a form.</summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public IActionResult Upload(IFormFile document) => Ok();

    /// <summary>A report in the media types [SwaggerResponse] declares.</summary>
    [HttpGet("report")]
    [SwaggerResponse(StatusCodes.Status200OK, "Report rows", typeof(ReportRow), "application/xml", "text/csv")]
    public IActionResult Report() => Ok();
}
