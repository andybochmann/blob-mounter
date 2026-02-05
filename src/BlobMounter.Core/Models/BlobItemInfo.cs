namespace BlobMounter.Core.Models;

public sealed class BlobItemInfo
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public bool IsDirectory { get; init; }
    public string? ETag { get; init; }
}
