using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniFileExplorer.Services;

namespace MiniFileExplorer.ViewModels;

public partial class FilePreviewViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;

    [ObservableProperty]
    private string _title = "Немає вибору";

    [ObservableProperty]
    private string _iconGlyph = "📁";

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isFolder;

    [ObservableProperty]
    private bool _isText;

    [ObservableProperty]
    private bool _isImage;

    [ObservableProperty]
    private bool _isBinary;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _textContent = string.Empty;

    [ObservableProperty]
    private Bitmap? _imagePreview;

    [ObservableProperty]
    private string _imageDimensions = string.Empty;

    [ObservableProperty]
    private string _fileSizeInfo = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _modifiedDateInfo = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    public FilePreviewViewModel(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService;
    }

    public void Reset()
    {
        Title = "Немає вибору";
        IconGlyph = "📁";
        IsEmpty = true;
        IsFolder = false;
        IsText = false;
        IsImage = false;
        IsBinary = false;
        IsLoading = false;
        TextContent = string.Empty;

        ImagePreview?.Dispose();
        ImagePreview = null;
        ImageDimensions = string.Empty;

        FileSizeInfo = string.Empty;
        FilePath = string.Empty;
        ModifiedDateInfo = string.Empty;
    }

    public async Task LoadPreviewAsync(FileItemViewModel? item, CancellationToken cancellationToken)
    {
        Reset();

        if (item == null || item.Name == "..")
            return;

        IsEmpty = false;
        Title = item.Name;
        IconGlyph = item.IconGlyph;
        FilePath = item.FullPath;
        FileSizeInfo = item.FormattedSize;
        ModifiedDateInfo = item.FormattedDate;

        if (item.IsDirectory)
        {
            IsFolder = true;
            return;
        }

        var ext = item.Extension?.ToLowerInvariant() ?? string.Empty;

        // Image preview
        if (IsImageExtension(ext))
        {
            await LoadImageAsync(item.FullPath, cancellationToken);
            return;
        }

        // Text / code preview
        if (IsTextExtension(ext))
        {
            await LoadTextAsync(item.FullPath, cancellationToken);
            return;
        }

        // Binary / executable / archive / other
        IsBinary = true;
    }

    private async Task LoadTextAsync(string fullPath, CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var sb = new StringBuilder();
            int lineCount = 0;
            const int maxLines = 100;

            await Task.Run(() =>
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                while (!reader.EndOfStream && lineCount < maxLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = reader.ReadLine();
                    if (line != null)
                    {
                        sb.AppendLine(line);
                        lineCount++;
                    }
                }

                if (!reader.EndOfStream)
                {
                    sb.AppendLine("\n--- [Показано перші 100 рядків] ---");
                }
            }, cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                TextContent = sb.ToString();
                IsText = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            TextContent = $"Не вдалося прочитати файл:\n{ex.Message}";
            IsText = true;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private async Task LoadImageAsync(string fullPath, CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bitmap = new Bitmap(fs);

                cancellationToken.ThrowIfCancellationRequested();
                ImagePreview?.Dispose();
                ImagePreview = bitmap;
                ImageDimensions = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} px";
            }, cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                IsImage = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch
        {
            // Fallback to binary card if image decoder fails
            IsBinary = true;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private static bool IsTextExtension(string ext)
    {
        return ext switch
        {
            ".txt" or ".md" or ".log" or ".rtf" or ".ini" or ".cfg" or ".conf" or
            ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".axaml" or ".xaml" or
            ".html" or ".css" or ".scss" or ".yaml" or ".yml" or ".sql" or ".sh" or ".bat" or
            ".cmd" or ".ps1" or ".gitignore" or ".editorconfig" => true,
            _ => false
        };
    }

    private static bool IsImageExtension(string ext)
    {
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".ico" or ".webp" => true,
            _ => false
        };
    }
}
