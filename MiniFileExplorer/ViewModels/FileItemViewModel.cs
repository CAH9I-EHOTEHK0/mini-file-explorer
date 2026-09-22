using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniFileExplorer.Models;
using MiniFileExplorer.Services;

namespace MiniFileExplorer.ViewModels;

public partial class FileItemViewModel : ViewModelBase
{
    private readonly FileSystemItem _item;
    private readonly IFileSystemService _fileSystemService;

    public FileItemViewModel(FileSystemItem item, IFileSystemService fileSystemService)
    {
        _item = item;
        _fileSystemService = fileSystemService;
    }

    public FileSystemItem Model => _item;

    public string Name => _item.Name;
    public string FullPath => _item.FullPath;
    public bool IsDirectory => _item.IsDirectory;
    public long? SizeBytes => _item.SizeBytes;
    public DateTime? ModifiedDate => _item.ModifiedDate;
    public string? Extension => _item.Extension;

    public string FormattedSize => IsDirectory 
        ? (CalculatedSizeBytes.HasValue ? _fileSystemService.FormatBytes(CalculatedSizeBytes.Value) : string.Empty)
        : _fileSystemService.FormatBytes(SizeBytes);

    public string FormattedDate => ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    public string TypeDescription => IsDirectory 
        ? (_item.Name == ".." ? "Батьківська папка" : "Папка") 
        : (string.IsNullOrWhiteSpace(Extension) ? "Файл" : $"Файл {Extension.ToUpperInvariant()}");

    public string IconGlyph => IsDirectory 
        ? (_item.Name == ".." ? "⬆" : "📁") 
        : GetFileIcon(Extension);

    [ObservableProperty]
    private long? _calculatedSizeBytes;

    partial void OnCalculatedSizeBytesChanged(long? value)
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
