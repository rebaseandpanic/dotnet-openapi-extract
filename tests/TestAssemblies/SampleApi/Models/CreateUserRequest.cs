using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations;

namespace SampleApi.Models;

/// <summary>
/// Request to create a new user
/// </summary>
public class CreateUserRequest
{
    /// <summary>
    /// User email address (must be unique)
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
    /// Initial user status (default: Active)
    /// </summary>
    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>
    /// Optional profile information
    /// </summary>
    public UserProfile? Profile { get; set; }
}
