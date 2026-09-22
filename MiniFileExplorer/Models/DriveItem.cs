namespace MiniFileExplorer.Models;

public class DriveItem
{
    public required string Name { get; init; }
    public required string RootDirectory { get; init; }
    public long TotalSize { get; init; }
    public long AvailableFreeSpace { get; init; }
    public string DriveType { get; init; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? RootDirectory : $"{Name} ({RootDirectory})";
}
