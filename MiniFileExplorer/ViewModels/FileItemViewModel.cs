using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniFileExplorer.Models;
using MiniFileExplorer.Services;

namespace MiniFileExplorer.ViewModels;

public partial class FileItemViewModel : ViewModelBase
{
    private readonly FileSystemItem? _item;
    private readonly DriveItem? _drive;
    private readonly IFileSystemService _fileSystemService;

    public FileItemViewModel(FileSystemItem item, IFileSystemService fileSystemService)
    {
        _item = item;
        _fileSystemService = fileSystemService;
    }

    public FileItemViewModel(DriveItem drive, IFileSystemService fileSystemService)
    {
        _drive = drive;
        _fileSystemService = fileSystemService;
    }

    public FileSystemItem? Model => _item;
    public DriveItem? DriveModel => _drive;
    public bool IsDrive => _drive != null;

    public string Name => _drive != null ? _drive.DisplayText : (_item?.Name ?? string.Empty);
    public string FullPath => _drive != null ? _drive.RootDirectory : (_item?.FullPath ?? string.Empty);
    public bool IsDirectory => _drive != null || (_item?.IsDirectory ?? false);
    public long? SizeBytes => _drive != null ? _drive.TotalSize : (IsDirectory ? CalculatedSizeBytes : _item?.SizeBytes);
    public DateTime? ModifiedDate => _item?.ModifiedDate;
    public string? Extension => _item?.Extension;

    public string FormattedSize
    {
        get
        {
            if (_drive != null)
            {
                if (_drive.TotalSize <= 0) return string.Empty;
                return $"{_fileSystemService.FormatBytes(_drive.AvailableFreeSpace)} вільно з {_fileSystemService.FormatBytes(_drive.TotalSize)}";
            }

            if (_item?.Name == "..")
                return string.Empty;

            if (IsDirectory)
            {
                if (IsCalculatingSize)
                    return "обчислення...";

                return CalculatedSizeBytes.HasValue 
                    ? _fileSystemService.FormatBytes(CalculatedSizeBytes.Value) 
                    : string.Empty;
            }

            return _fileSystemService.FormatBytes(SizeBytes);
        }
    }

    public string FormattedDate => ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    public string TypeDescription => _drive != null
        ? $"Диск ({_drive.DriveType})"
        : (IsDirectory 
            ? (_item?.Name == ".." ? "Батьківська папка" : "Папка") 
            : (string.IsNullOrWhiteSpace(Extension) ? "Файл" : $"Файл {Extension.ToUpperInvariant()}"));

    public string IconGlyph => _drive != null
        ? (_drive.DriveType.Equals("Removable", StringComparison.OrdinalIgnoreCase) ? "🔌" : "💾")
        : (IsDirectory 
            ? (_item?.Name == ".." ? "⬆" : "📁") 
            : GetFileIcon(Extension));

    [ObservableProperty]
    private long? _calculatedSizeBytes;

    [ObservableProperty]
    private bool _isCalculatingSize;

    partial void OnCalculatedSizeBytesChanged(long? value)
    {
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(FormattedSize));
    }

    partial void OnIsCalculatingSizeChanged(bool value)
    {
        OnPropertyChanged(nameof(FormattedSize));
    }

    private static string GetFileIcon(string? extension)
    {
        return (extension?.ToLowerInvariant()) switch
        {
            ".txt" or ".md" or ".log" or ".rtf" => "📄",
            ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".axaml" or ".html" or ".css" => "📝",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" => "🖼",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".aac" => "🎵",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "🎬",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
            ".pdf" => "📕",
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "⚙",
            _ => "📄"
        };
    }
}
