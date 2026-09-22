using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MiniFileExplorer.Models;

namespace MiniFileExplorer.Services;

public class FileSystemService : IFileSystemService
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB" };

    public IEnumerable<DriveItem> GetDrives()
    {
        var result = new List<DriveItem>();
        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                try
                {
                    if (drive.IsReady)
                    {
                        result.Add(new DriveItem
                        {
                            Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? $"Drive ({drive.Name.TrimEnd('\\')})" : drive.VolumeLabel,
                            RootDirectory = drive.RootDirectory.FullName,
                            TotalSize = drive.TotalSize,
                            AvailableFreeSpace = drive.AvailableFreeSpace,
                            DriveType = drive.DriveType.ToString()
                        });
                    }
                    else
                    {
                        result.Add(new DriveItem
                        {
                            Name = $"Drive ({drive.Name.TrimEnd('\\')})",
                            RootDirectory = drive.RootDirectory.FullName,
                            DriveType = drive.DriveType.ToString()
                        });
                    }
                }
                catch (Exception)
                {
                    // Ignore inaccessible or unready drives
                }
            }
        }
        catch (Exception)
        {
            // Ignore system-wide drive query exceptions
        }

        return result;
    }

    public Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<FileSystemItem>>(() =>
        {
            var items = new List<FileSystemItem>();

            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException($"Каталог не знайдено: {directoryPath}");
            }

            var dirInfo = new DirectoryInfo(directoryPath);

            // Add parent directory navigation if not at drive root
            var parent = dirInfo.Parent;
            if (parent != null)
            {
                items.Add(FileSystemItem.CreateParentDirectory(parent.FullName));
            }

            IEnumerable<FileSystemInfo> fileSystemInfos;
            try
            {
                fileSystemInfos = dirInfo.EnumerateFileSystemInfos();
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException($"Немає доступу до каталогу: {directoryPath}");
            }
            catch (Exception ex)
            {
                throw new IOException($"Помилка читання каталогу {directoryPath}: {ex.Message}", ex);
            }

            var directories = new List<FileSystemItem>();
            var files = new List<FileSystemItem>();

            foreach (var info in fileSystemInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (info is DirectoryInfo subDir)
                    {
                        directories.Add(new FileSystemItem
                        {
                            Name = subDir.Name,
                            FullPath = subDir.FullName,
                            IsDirectory = true,
                            SizeBytes = null,
                            ModifiedDate = SafeGetLastWriteTime(subDir),
                            Extension = null
                        });
                    }
                    else if (info is FileInfo file)
                    {
                        files.Add(new FileSystemItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false,
                            SizeBytes = SafeGetFileLength(file),
                            ModifiedDate = SafeGetLastWriteTime(file),
                            Extension = file.Extension.ToLowerInvariant()
                        });
                    }
                }
                catch (Exception)
                {
                    // Ignore inaccessible or disappeared items
                }
            }

            items.AddRange(directories.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase));
            items.AddRange(files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase));

            return items;
        }, cancellationToken);
    }

    public string FormatBytes(long? bytes)
    {
        if (bytes == null)
            return string.Empty;

        if (bytes == 0)
            return "0 B";

        long val = bytes.Value;
        int mag = (int)Math.Max(0, Math.Log(val, 1024));
        if (mag >= SizeSuffixes.Length)
            mag = SizeSuffixes.Length - 1;

        decimal adjustedSize = (decimal)val / (1L << (mag * 10));

        if (mag == 0)
            return $"{val} {SizeSuffixes[mag]}";

        return $"{adjustedSize:0.##} {SizeSuffixes[mag]}";
    }

    public bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public string? GetParentDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Directory.GetParent(path)?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? SafeGetLastWriteTime(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTime;
        }
        catch
        {
            return null;
        }
    }

    private static long? SafeGetFileLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch
        {
            return null;
        }
    }
}
