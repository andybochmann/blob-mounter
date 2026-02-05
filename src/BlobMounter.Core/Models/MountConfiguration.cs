namespace BlobMounter.Core.Models;

public sealed class MountConfiguration
{
    public required string AccountName { get; init; }
    public required string AccountKey { get; init; }
    public required string ContainerName { get; init; }
    public string? Subfolder { get; init; }
    public required char DriveLetter { get; init; }
    public bool ReadOnly { get; init; }
}
