using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Threading;
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
    private readonly ISearchService _searchService;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _previewCts;

    [ObservableProperty]
    private FilePaneViewModel _leftPane;

    [ObservableProperty]
    private FilePaneViewModel _rightPane;

    [ObservableProperty]
    private FilePaneViewModel _activePane;

    [ObservableProperty]
    private bool _isDualPaneMode;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchMode;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _searchStatusText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _quickAccessItems = new();

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _driveSidebarItems = new();

    [ObservableProperty]
    private FilePreviewViewModel _preview;

    [ObservableProperty]
    private bool _isPreviewPaneVisible = true;

    [ObservableProperty]
    private bool _hasBanner;

    [ObservableProperty]
    private string _bannerMessage = string.Empty;

    [ObservableProperty]
    private NotificationSeverity _bannerSeverity = NotificationSeverity.None;

    public string CurrentPath => ActivePane.CurrentPath;

    public MainViewModel() : this(new FileSystemService(), new SearchService())
    {
    }

    public MainViewModel(IFileSystemService fileSystemService, ISearchService searchService)
    {
        _fileSystemService = fileSystemService;
        _searchService = searchService;
        _preview = new FilePreviewViewModel(_fileSystemService);

        _leftPane = new FilePaneViewModel(_fileSystemService) { IsActive = true };
        _rightPane = new FilePaneViewModel(_fileSystemService) { IsActive = false };
        _activePane = _leftPane;

        WirePaneEvents(_leftPane);
        WirePaneEvents(_rightPane);

        InitSidebar();
        LoadDrives();

        var initialDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(initialDir) || !_fileSystemService.DirectoryExists(initialDir))
        {
            initialDir = Directory.GetCurrentDirectory();
        }

        _ = _leftPane.NavigateToAsync(initialDir);
        _ = _rightPane.NavigateToAsync(initialDir);
    }

    private void WirePaneEvents(FilePaneViewModel pane)
    {
        pane.OnNotification += (msg, sev) => ShowBanner(msg, sev);
        pane.OnSelectionChanged += item =>
        {
            if (pane == ActivePane && IsPreviewPaneVisible)
            {
                _previewCts?.Cancel();
                _previewCts = new CancellationTokenSource();
                _ = Preview.LoadPreviewAsync(item, _previewCts.Token);
            }
        };
    }

    public void SetActivePane(FilePaneViewModel pane)
    {
        if (ActivePane == pane) return;

        LeftPane.IsActive = (pane == LeftPane);
        RightPane.IsActive = (pane == RightPane);
        ActivePane = pane;

        OnPropertyChanged(nameof(CurrentPath));

        if (IsPreviewPaneVisible && pane.SelectedItem != null)
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            _ = Preview.LoadPreviewAsync(pane.SelectedItem, _previewCts.Token);
        }
    }

    [RelayCommand]
    public void ToggleDualPaneMode()
    {
        IsDualPaneMode = !IsDualPaneMode;
        if (IsDualPaneMode)
        {
            // Sync or keep independent
            if (string.IsNullOrWhiteSpace(RightPane.CurrentPath))
            {
                _ = RightPane.NavigateToAsync(LeftPane.CurrentPath);
            }
        }
        else
        {
            SetActivePane(LeftPane);
        }
    }

    [RelayCommand]
    public void SwapActivePane()
    {
        if (!IsDualPaneMode) return;
        SetActivePane(ActivePane == LeftPane ? RightPane : LeftPane);
    }

    [RelayCommand]
    public async Task CopyBetweenPanesAsync()
    {
        if (!IsDualPaneMode) return;

        var sourcePane = ActivePane;
        var targetPane = (ActivePane == LeftPane) ? RightPane : LeftPane;

        var item = sourcePane.SelectedItem;
        if (item == null || item.Name == "..") return;

        string destPath = Path.Combine(targetPane.CurrentPath, item.Name);

        try
        {
            if (item.IsDirectory)
            {
                CopyDirectoryRecursive(item.FullPath, destPath);
            }
            else
            {
                File.Copy(item.FullPath, destPath, overwrite: true);
            }

            ShowBanner($"Скопійовано '{item.Name}' у {targetPane.CurrentPath}", NotificationSeverity.Info);
            await targetPane.RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowBanner($"Помилка копіювання: {ex.Message}", NotificationSeverity.Error);
        }
    }

    [RelayCommand]
    public async Task MoveBetweenPanesAsync()
    {
        if (!IsDualPaneMode) return;

        var sourcePane = ActivePane;
        var targetPane = (ActivePane == LeftPane) ? RightPane : LeftPane;

        var item = sourcePane.SelectedItem;
        if (item == null || item.Name == "..") return;

        string destPath = Path.Combine(targetPane.CurrentPath, item.Name);

        try
        {
            if (item.IsDirectory)
            {
                Directory.Move(item.FullPath, destPath);
            }
            else
            {
                File.Move(item.FullPath, destPath, overwrite: true);
            }

            ShowBanner($"Переміщено '{item.Name}' у {targetPane.CurrentPath}", NotificationSeverity.Info);
            await sourcePane.RefreshAsync();
            await targetPane.RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowBanner($"Помилка переміщення: {ex.Message}", NotificationSeverity.Error);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }
        foreach (var sub in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(sub, Path.Combine(targetDir, Path.GetFileName(sub)));
        }
    }

    private void InitSidebar()
    {
        QuickAccessItems.Clear();

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var downloads = Path.Combine(userProfile, "Downloads");

        if (_fileSystemService.DirectoryExists(desktop))
            QuickAccessItems.Add(new SidebarItemViewModel { Name = "Робочий стіл", Path = desktop, IconGlyph = "🖥", IsDrive = false });

        if (_fileSystemService.DirectoryExists(downloads))
            QuickAccessItems.Add(new SidebarItemViewModel { Name = "Завантаження", Path = downloads, IconGlyph = "📥", IsDrive = false });

        if (_fileSystemService.DirectoryExists(documents))
            QuickAccessItems.Add(new SidebarItemViewModel { Name = "Документи", Path = documents, IconGlyph = "📄", IsDrive = false });

        if (_fileSystemService.DirectoryExists(pictures))
            QuickAccessItems.Add(new SidebarItemViewModel { Name = "Зображення", Path = pictures, IconGlyph = "🖼", IsDrive = false });
    }

    private void LoadDrives()
    {
        DriveSidebarItems.Clear();

        foreach (var drive in _fileSystemService.GetDrives())
        {
            string subtitle = drive.TotalSize > 0 
                ? $"{_fileSystemService.FormatBytes(drive.AvailableFreeSpace)} вільно з {_fileSystemService.FormatBytes(drive.TotalSize)}"
                : string.Empty;

            DriveSidebarItems.Add(new SidebarItemViewModel
            {
                Name = drive.Name,
                Path = drive.RootDirectory,
                IconGlyph = "💾",
                Subtitle = subtitle,
                IsDrive = true
            });
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
    public void TogglePreviewPane()
    {
        IsPreviewPaneVisible = !IsPreviewPaneVisible;
    }

    [RelayCommand]
    public async Task StartSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearchMode = true;
        IsSearching = true;
        SearchStatusText = $"Пошук '{SearchQuery}' у {ActivePane.CurrentPath}...";

        ActivePane.ClearDirectly();

        var progress = new Progress<SearchProgressReport>(r =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                SearchStatusText = $"Сканування: {r.ScannedFiles:N0} файлів, {r.ScannedFolders:N0} папок | Знайдено: {r.MatchesCount}";
            });
        });

        try
        {
            var buffer = new List<FileItemViewModel>();
            var lastFlush = DateTime.UtcNow;

            await Task.Run(async () =>
            {
                await foreach (var match in _searchService.SearchAsync(ActivePane.CurrentPath, SearchQuery, progress, token))
                {
                    token.ThrowIfCancellationRequested();

                    var vm = new FileItemViewModel(match, _fileSystemService);
                    lock (buffer)
                    {
                        buffer.Add(vm);
                    }

                    if (buffer.Count >= 30 || (DateTime.UtcNow - lastFlush).TotalMilliseconds > 150)
                    {
                        List<FileItemViewModel> toPost;
                        lock (buffer)
                        {
                            toPost = new List<FileItemViewModel>(buffer);
                            buffer.Clear();
                        }
                        lastFlush = DateTime.UtcNow;

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var item in toPost)
                            {
                                ActivePane.AddItemDirectly(item);
                            }
                        }, DispatcherPriority.Background);
                    }
                }

                if (buffer.Count > 0)
                {
                    List<FileItemViewModel> toPost;
                    lock (buffer)
                    {
                        toPost = new List<FileItemViewModel>(buffer);
                        buffer.Clear();
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var item in toPost)
                        {
                            ActivePane.AddItemDirectly(item);
                        }
                    }, DispatcherPriority.Background);
                }
            }, token);

            if (!token.IsCancellationRequested)
            {
                SearchStatusText = $"Пошук завершено. Знайдено елементів: {ActivePane.Items.Count}";
            }
        }
        catch (OperationCanceledException)
        {
            SearchStatusText = $"Пошук зупинено. Знайдено: {ActivePane.Items.Count}";
        }
        catch (Exception ex)
        {
            ShowBanner($"Помилка пошуку: {ex.Message}", NotificationSeverity.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public void CancelSearch()
    {
        _searchCts?.Cancel();
        IsSearching = false;
    }

    [RelayCommand]
    public async Task ExitSearchModeAsync()
    {
        CancelSearch();
        IsSearchMode = false;
        SearchQuery = string.Empty;
        await ActivePane.NavigateToAsync(ActivePane.CurrentPath);
    }

    [RelayCommand]
    public async Task OpenFileLocationAsync(FileItemViewModel? item)
    {
        item ??= ActivePane.SelectedItem;
        if (item == null) return;

        string? targetDir = item.IsDirectory ? item.FullPath : _fileSystemService.GetParentDirectory(item.FullPath);
        if (string.IsNullOrWhiteSpace(targetDir) || !_fileSystemService.DirectoryExists(targetDir))
            return;

        ExitSearchModeCommand.Execute(null);
        await ActivePane.NavigateToAsync(targetDir);

        var matchInFolder = ActivePane.Items.FirstOrDefault(x => x.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (matchInFolder != null)
        {
            ActivePane.SelectedItem = matchInFolder;
        }
    }

    [RelayCommand]
    public async Task NavigateToPathAsync(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ActivePane.NavigateToAsync(path);
        }
    }

    [RelayCommand]
    public async Task CopyPathAsync(FileItemViewModel? item)
    {
        item ??= ActivePane.SelectedItem;
        if (item == null) return;

        try
        {
            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard
                : null;

            if (clipboard != null)
            {
                await clipboard.SetTextAsync(item.FullPath);
                ShowBanner($"Шлях скопійовано: {item.FullPath}", NotificationSeverity.Info);
            }
        }
        catch (Exception ex)
        {
            ShowBanner($"Не вдалося скопіювати шлях: {ex.Message}", NotificationSeverity.Warning);
        }
    }

    [RelayCommand]
    public async Task DeleteItemAsync(FileItemViewModel? item)
    {
        item ??= ActivePane.SelectedItem;
        if (item == null || item.Name == "..") return;

        try
        {
            if (item.IsDirectory)
            {
                Directory.Delete(item.FullPath, true);
            }
            else
            {
                File.Delete(item.FullPath);
            }

            ShowBanner($"Видалено: {item.Name}", NotificationSeverity.Info);
            await ActivePane.RefreshAsync();
        }
        catch (UnauthorizedAccessException)
        {
            ShowBanner($"Не вдалося видалити '{item.Name}': відмовлено в доступі", NotificationSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowBanner($"Помилка видалення: {ex.Message}", NotificationSeverity.Error);
        }
    }

    [RelayCommand]
    public void ShowProperties(FileItemViewModel? item)
    {
        item ??= ActivePane.SelectedItem;
        if (item == null || item.Name == "..") return;

        string info = $"{item.Name} | Тип: {item.TypeDescription} | Розмір: {item.FormattedSize} | Змінено: {item.FormattedDate}";
        ShowBanner(info, NotificationSeverity.Info);
    }

    [RelayCommand]
    public Task OpenItemAsync(FileItemViewModel? item)
    {
        return ActivePane.OpenItemAsync(item);
    }
}
