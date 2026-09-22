using System;

namespace MiniFileExplorer.Models;

public class FileSystemItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public DateTime? ModifiedDate { get; init; }
    public string? Extension { get; init; }

    public static FileSystemItem CreateParentDirectory(string parentPath)
    {
        return new FileSystemItem
        {
            Name = "..",
            FullPath = parentPath,
            IsDirectory = true,
            SizeBytes = null,
            ModifiedDate = null,
            Extension = null
        };
    }
}
