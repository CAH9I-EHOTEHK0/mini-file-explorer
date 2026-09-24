using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniFileExplorer.Services;

namespace MiniFileExplorer.ViewModels;

public enum HashVerificationState
{
    None,
    Match,
    Mismatch
}

public partial class FilePreviewViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IHashService _hashService;
    private CancellationTokenSource? _hashCts;

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

    // --- SHA-256 Hash Checker (Day 11) ---
    [ObservableProperty]
    private string _sha256Hash = string.Empty;

    [ObservableProperty]
    private bool _isComputingHash;

    [ObservableProperty]
    private double _hashProgress;

    [ObservableProperty]
    private string _expectedHash = string.Empty;

    [ObservableProperty]
    private HashVerificationState _hashVerification = HashVerificationState.None;

    [ObservableProperty]
    private string _hashStatusMessage = string.Empty;

    public bool CanComputeHash => !IsEmpty && !IsFolder && !string.IsNullOrWhiteSpace(FilePath);

    public FilePreviewViewModel(IFileSystemService fileSystemService, IHashService hashService)
    {
        _fileSystemService = fileSystemService;
        _hashService = hashService;
    }

    public void Reset()
    {
        _hashCts?.Cancel();
        _hashCts = null;

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

        Sha256Hash = string.Empty;
        IsComputingHash = false;
        HashProgress = 0;
        ExpectedHash = string.Empty;
        HashVerification = HashVerificationState.None;
        HashStatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanComputeHash));
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
        OnPropertyChanged(nameof(CanComputeHash));

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

    partial void OnExpectedHashChanged(string value)
    {
        VerifyHash();
    }

    private void VerifyHash()
    {
        var expected = ExpectedHash.Trim();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(Sha256Hash))
        {
            HashVerification = HashVerificationState.None;
            HashStatusMessage = string.Empty;
            return;
        }

        if (string.Equals(expected, Sha256Hash, StringComparison.OrdinalIgnoreCase))
        {
            HashVerification = HashVerificationState.Match;
            HashStatusMessage = "✅ Хеші збігаються!";
        }
        else
        {
            HashVerification = HashVerificationState.Mismatch;
            HashStatusMessage = "❌ Хеші НЕ збігаються!";
        }
    }

    [RelayCommand]
    public async Task ComputeSha256Async()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            return;

        _hashCts?.Cancel();
        _hashCts = new CancellationTokenSource();
        var ct = _hashCts.Token;

        IsComputingHash = true;
        HashProgress = 0;
        Sha256Hash = string.Empty;
        HashVerification = HashVerificationState.None;
        HashStatusMessage = "Обчислення SHA-256...";

        try
        {
            var progress = new Progress<double>(p => HashProgress = p);
            var hash = await _hashService.ComputeSha256Async(FilePath, progress, ct);
            if (!ct.IsCancellationRequested)
            {
                Sha256Hash = hash;
                HashStatusMessage = "Хеш успішно обчислено";
                VerifyHash();
            }
        }
        catch (OperationCanceledException)
        {
            HashStatusMessage = "Обчислення скасовано";
        }
        catch (Exception ex)
        {
            HashStatusMessage = $"Помилка: {ex.Message}";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsComputingHash = false;
            }
        }
    }

    [RelayCommand]
    public async Task CopySha256Async()
    {
        if (string.IsNullOrWhiteSpace(Sha256Hash)) return;

        try
        {
            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard
                : null;

            if (clipboard != null)
            {
                await clipboard.SetTextAsync(Sha256Hash);
                HashStatusMessage = "📋 Хеш скопійовано в буфер!";
            }
        }
        catch
        {
            // Ignore clipboard errors
        }
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
