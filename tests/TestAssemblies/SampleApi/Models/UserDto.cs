using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace SampleApi.Models;

/// <summary>
/// User data transfer object
/// </summary>
public class UserDto
{
    /// <summary>
    /// User unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// User email address
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public required string Email { get; set; }

    /// <summary>
    /// Display name
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public required string DisplayName { get; set; }

    /// <summary>
    /// User account status
    /// </summary>
    public UserStatus Status { get; set; }

    /// <summary>
    /// Account creation date (UTC)
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Profile information (null if not set)
    /// </summary>
    public UserProfile? Profile { get; set; }

    /// <summary>
    /// User tags
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Custom metadata key-value pairs
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// User profile details
/// </summary>
public class UserProfile
{
    /// <summary>
    /// First name
    /// </summary>
    [StringLength(50)]
    public string? FirstName { get; set; }

    /// <summary>
    /// Last name
    /// </summary>
    [StringLength(50)]
    public string? LastName { get; set; }

    /// <summary>
    /// Age in years
    /// </summary>
    [Range(0, 150)]
    public int? Age { get; set; }

    /// <summary>
    /// Profile picture URL
    /// </summary>
    public Uri? AvatarUrl { get; set; }
}

/// <summary>
/// User account status
/// </summary>
public enum UserStatus
{
    /// <summary>Active account</summary>
    Active,
    /// <summary>Suspended account</summary>
    Suspended,
    /// <summary>Banned account</summary>
    Banned,
    /// <summary>Deleted account</summary>
    Deleted
}
