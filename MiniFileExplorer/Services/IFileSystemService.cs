using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniFileExplorer.Models;

namespace MiniFileExplorer.Services;

public interface IFileSystemService
{
    IEnumerable<DriveItem> GetDrives();
    Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(string directoryPath, CancellationToken cancellationToken = default);
    string FormatBytes(long? bytes);
    bool DirectoryExists(string path);
    string? GetParentDirectory(string path);
}
