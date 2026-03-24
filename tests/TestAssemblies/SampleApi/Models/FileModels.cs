namespace SampleApi.Models;

/// <summary>
/// Result of a file upload operation
/// </summary>
public sealed class FileUploadResult
{
    /// <summary>Uploaded file ID</summary>
    public Guid FileId { get; set; }
    /// <summary>File name</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>File size in bytes</summary>
    public long SizeBytes { get; set; }
    /// <summary>Content type</summary>
    public string ContentType { get; set; } = string.Empty;
}

/// <summary>
/// File metadata
/// </summary>
public sealed class FileMetadata
{
    /// <summary>File ID</summary>
    public Guid Id { get; set; }
    /// <summary>Original file name</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>File size in bytes</summary>
    public long SizeBytes { get; set; }
    /// <summary>Upload date</summary>
    public DateTimeOffset UploadedAt { get; set; }
    /// <summary>File category</summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// File storage statistics
/// </summary>
public sealed class FileStats
{
    /// <summary>Total files count</summary>
    public int TotalFiles { get; set; }
    /// <summary>Total storage used in bytes</summary>
    public long TotalSizeBytes { get; set; }
}
