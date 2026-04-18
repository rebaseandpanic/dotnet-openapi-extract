using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Controller that supports API versions 1.0 and 2.0.
/// GetAll is available in both versions.
/// GetV2Only is mapped exclusively to version 2.0.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/items")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class VersionedController : ControllerBase
{
    /// <summary>Returns all items (available in versions 1.0 and 2.0).</summary>
    [HttpGet("all")]
    public IActionResult GetAll() => Ok();

    /// <summary>Returns a single item by ID (version 2.0 only).</summary>
    [HttpGet("{id:int}")]
    [MapToApiVersion("2.0")]
    public IActionResult GetV2Only(int id) => Ok();
}

/// <summary>
/// Version-neutral controller — responds to all API versions.
/// </summary>
[ApiController]
[Route("api/status")]
[ApiVersionNeutral]
public class StatusController : ControllerBase
{
    /// <summary>Returns the service status.</summary>
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller where only the controller carries [ApiVersion("1.0")]
/// and an action additionally carries [ApiVersion("2.0")] — used to
/// test union merging.
/// </summary>
[ApiController]
[Route("api/union")]
[ApiVersion("1.0")]
public class VersioningUnionController : ControllerBase
{
    /// <summary>Available in both 1.0 (from controller) and 2.0 (from action).</summary>
    [HttpGet]
    [ApiVersion("2.0")]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller that has the same version on both the controller and the action
/// — used to verify deduplication.
/// </summary>
[ApiController]
[Route("api/dedup")]
[ApiVersion("1.0")]
public class VersioningDedupController : ControllerBase
{
    /// <summary>Version 1.0 declared on both controller and action — should appear once.</summary>
    [HttpGet]
    [ApiVersion("1.0")]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller whose action carries [ApiVersionNeutral] while the controller is not neutral.
/// </summary>
[ApiController]
[Route("api/actionneutral")]
[ApiVersion("1.0")]
public class VersioningActionNeutralController : ControllerBase
{
    /// <summary>This specific action is neutral regardless of the controller version.</summary>
    [HttpGet]
    [ApiVersionNeutral]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller using the int-pair constructor overload [ApiVersion(1, 0)].
/// </summary>
[ApiController]
[Route("api/intversion")]
[ApiVersion(1, 0)]
public class VersioningIntConstructorController : ControllerBase
{
    /// <summary>Returns data for version 1.0 declared via int constructor.</summary>
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller using the int-major-minor-status constructor overload [ApiVersion(major, minor, status)].
/// </summary>
[ApiController]
[Route("api/preview/items")]
[ApiVersion(1, 0, "beta")]
[ApiVersion(2, 0, "rc1")]
public class VersioningStatusSuffixController : ControllerBase
{
    /// <summary>Returns preview items with status-suffix version annotations.</summary>
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller using the double-status constructor overload [ApiVersion(double, status)].
/// </summary>
[ApiController]
[Route("api/preview2/items")]
[ApiVersion(1.0, "alpha")]
public class VersioningDoubleStatusSuffixController : ControllerBase
{
    /// <summary>Returns preview2 items with double-status version annotation.</summary>
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller using the bare-double constructor overload [ApiVersion(double)].
/// Uses 1.5 (not 1.0) so the result is unambiguous vs the int-pair "1.0" path.
/// </summary>
[ApiController]
[Route("api/doubleversion")]
[ApiVersion(1.5)]
public class VersioningBareDoubleController : ControllerBase
{
    /// <summary>Returns data for version 1.5 declared via bare double constructor.</summary>
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>
/// Controller that is [ApiVersionNeutral] at class level while an action also carries [ApiVersion("1.0")].
/// Neutral must win: GetSupportedVersions must return empty, IsVersionNeutral must return true.
/// </summary>
[ApiController]
[Route("api/neutralwithversion")]
[ApiVersionNeutral]
public class VersioningNeutralOverridesVersionController : ControllerBase
{
    /// <summary>Action with redundant [ApiVersion] — neutral on the controller takes priority.</summary>
    [HttpGet]
    [ApiVersion("1.0")]
    public IActionResult Get() => Ok();
}
