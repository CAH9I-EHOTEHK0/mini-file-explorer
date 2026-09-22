using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniFileExplorer.Models;
using MiniFileExplorer.Services;

namespace MiniFileExplorer.ViewModels;

public enum NotificationSeverity
{
    None,
    Info,
    Warning,
    Error
}

public partial class MainViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _pathInput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DriveItem> _drives = new();

    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> _items = new();

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _statusMessage = "Готово";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private bool _hasBanner;

    [ObservableProperty]
    private string _bannerMessage = string.Empty;

    [ObservableProperty]
    private NotificationSeverity _bannerSeverity = NotificationSeverity.None;

    public MainViewModel() : this(new FileSystemService())
    {
    }

    public MainViewModel(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService;
        LoadDrives();

        var initialDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(initialDir) || !_fileSystemService.DirectoryExists(initialDir))
        {
            initialDir = Directory.GetCurrentDirectory();
        }

        _ = NavigateToAsync(initialDir);
    }

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var drive in _fileSystemService.GetDrives())
        {
            Drives.Add(drive);
        }
    }

    public void ShowBanner(string message, NotificationSeverity severity)
    {
        BannerMessage = message;
        BannerSeverity = severity;
        HasBanner = true;
    }

    [RelayCommand]
    public void DismissBanner()
    {
        HasBanner = false;
        BannerMessage = string.Empty;
        BannerSeverity = NotificationSeverity.None;
    }

    [RelayCommand]
    public async Task NavigateToAsync(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            ShowBanner("Шлях не може бути порожнім.", NotificationSeverity.Warning);
            return;
        }

        targetPath = targetPath.Trim('\"', '\'').Trim();

        char[] invalidChars = Path.GetInvalidPathChars();
        if (targetPath.IndexOfAny(invalidChars) >= 0)
        {
            ShowBanner($"Шлях містить неприпустимі символи: '{targetPath}'", NotificationSeverity.Error);
            StatusMessage = "Помилка введення шляху";
            PathInput = CurrentPath;
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        StatusMessage = "Завантаження...";
        DismissBanner();

        try
        {
            var rawItems = await _fileSystemService.GetItemsAsync(targetPath, token);

            if (token.IsCancellationRequested)
                return;

            CurrentPath = targetPath;
            PathInput = targetPath;
            CanGoUp = _fileSystemService.GetParentDirectory(targetPath) != null;

            Items.Clear();
            foreach (var item in rawItems)
            {
                Items.Add(new FileItemViewModel(item, _fileSystemService));
            }

            StatusMessage = $"Елементів: {Items.Count}";
        }
        catch (UnauthorizedAccessException)
        {
            ShowBanner($"Доступ заборонено: у вас немає прав для перегляду '{targetPath}'", NotificationSeverity.Error);
            StatusMessage = "Помилка: відмовлено в доступі";
            PathInput = CurrentPath;
        }
        catch (DirectoryNotFoundException)
        {
            ShowBanner($"Каталог не існує: '{targetPath}'", NotificationSeverity.Warning);
            StatusMessage = "Помилка: каталог не знайдено";
            PathInput = CurrentPath;
        }
        catch (PathTooLongException)
        {
            ShowBanner("Занадто довгий шлях до каталогу (перевищено ліміт ОС).", NotificationSeverity.Error);
            StatusMessage = "Помилка: шлях занадто довгий";
            PathInput = CurrentPath;
        }
        catch (SecurityException)
        {
            ShowBanner($"Помилка безпеки при доступі до '{targetPath}'", NotificationSeverity.Error);
            StatusMessage = "Помилка безпеки";
            PathInput = CurrentPath;
        }
        catch (OperationCanceledException)
        {
            // Expected when user quickly navigates to another folder
        }
        catch (Exception ex)
        {
            ShowBanner($"Непередбачена помилка: {ex.Message}", NotificationSeverity.Error);
            StatusMessage = $"Помилка: {ex.Message}";
            PathInput = CurrentPath;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    public Task NavigateFromInputAsync()
    {
        return NavigateToAsync(PathInput);
    }

    [RelayCommand]
    public Task GoUpAsync()
    {
        var parent = _fileSystemService.GetParentDirectory(CurrentPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            return NavigateToAsync(parent);
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        LoadDrives();

        // If current directory was deleted externally, gracefully fallback to parent or user root
        if (!_fileSystemService.DirectoryExists(CurrentPath))
        {
            ShowBanner($"Поточний каталог більше не існує. Повертаємось до батьківської папки.", NotificationSeverity.Warning);
            var fallback = _fileSystemService.GetParentDirectory(CurrentPath);
            while (!string.IsNullOrWhiteSpace(fallback) && !_fileSystemService.DirectoryExists(fallback))
            {
                fallback = _fileSystemService.GetParentDirectory(fallback);
            }

            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            await NavigateToAsync(fallback);
            return;
        }

        await NavigateToAsync(CurrentPath);
    }

    [RelayCommand]
    public async Task OpenItemAsync(FileItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        if (item.IsDirectory)
        {
            await NavigateToAsync(item.FullPath);
        }
        else
        {
            try
            {
                if (!File.Exists(item.FullPath))
                {
                    ShowBanner($"Файл не знайдено: '{item.Name}'", NotificationSeverity.Warning);
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = item.FullPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowBanner($"Не вдалося запустити файл '{item.Name}': {ex.Message}", NotificationSeverity.Error);
            }
        }
    }
}
