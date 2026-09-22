using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniFileExplorer.Models;
using MiniFileExplorer.Services;

namespace MiniFileExplorer.ViewModels;

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

    [RelayCommand]
    public async Task NavigateToAsync(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        StatusMessage = "Завантаження...";

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
            StatusMessage = $"Помилка: Немає доступу до '{targetPath}'";
        }
        catch (DirectoryNotFoundException)
        {
            StatusMessage = $"Помилка: Каталог '{targetPath}' не знайдено";
        }
        catch (OperationCanceledException)
        {
            // Expected on fast navigation
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка: {ex.Message}";
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
    public Task RefreshAsync()
    {
        LoadDrives();
        return NavigateToAsync(CurrentPath);
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
                var psi = new ProcessStartInfo
                {
                    FileName = item.FullPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Не вдалося відкрити файл: {ex.Message}";
            }
        }
    }
}
