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
    private readonly IHashService _hashService;
    private readonly IBookmarkService _bookmarkService;
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
    private ObservableCollection<SidebarItemViewModel> _bookmarkItems = new();

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

    // --- Modal Confirmation / Input Overlay ---
    [ObservableProperty]
    private bool _isModalOpen;

    [ObservableProperty]
    private string _modalTitle = string.Empty;

    [ObservableProperty]
    private string _modalMessage = string.Empty;

    [ObservableProperty]
    private string _modalIcon = "⚠️";

    [ObservableProperty]
    private bool _isModalInputMode;

    [ObservableProperty]
    private string _modalInputValue = string.Empty;

    [ObservableProperty]
    private string _modalConfirmButtonText = "Підтвердити";

    [ObservableProperty]
    private bool _isModalDestructive;

    private Func<Task>? _modalConfirmAction;

    public string CurrentPath => ActivePane.CurrentPath;

    public MainViewModel() : this(new FileSystemService(), new SearchService(), new HashService(), new BookmarkService())
    {
    }

    public MainViewModel(IFileSystemService fileSystemService, ISearchService searchService, IHashService hashService, IBookmarkService bookmarkService)
    {
        _fileSystemService = fileSystemService;
        _searchService = searchService;
        _hashService = hashService;
        _bookmarkService = bookmarkService;
        _preview = new FilePreviewViewModel(_fileSystemService, _hashService);

        _leftPane = new FilePaneViewModel(_fileSystemService) { IsActive = true };
        _rightPane = new FilePaneViewModel(_fileSystemService) { IsActive = false };
        _activePane = _leftPane;

        WirePaneEvents(_leftPane);
        WirePaneEvents(_rightPane);

        InitSidebar();
        LoadDrives();
        _ = LoadBookmarksAsync();

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

        pane.RequestDelete = item => DeleteItemCommand.Execute(item);
        pane.RequestCreateFolder = () => CreateFolderCommand.Execute(null);
        pane.RequestCopyBetweenPanes = () => CopyBetweenPanesAsync();
        pane.RequestMoveBetweenPanes = () => MoveBetweenPanesAsync();
        pane.RequestShowProperties = item => ShowPropertiesCommand.Execute(item);
        pane.RequestComputeSha256 = item => ComputeSha256ForActiveItemAsync(item);
        pane.RequestBookmark = item => AddBookmarkForFolder(item);
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

        bool destExists = item.IsDirectory 
            ? _fileSystemService.DirectoryExists(destPath) 
            : File.Exists(destPath);

        if (destExists)
        {
            ShowConfirmationModal(
                title: "Перезаписати файл / папку?",
                message: $"'{item.Name}' вже існує в цільовій папці:\n{targetPane.CurrentPath}\n\nБажаєте перезаписати?",
                icon: "⚠️",
                confirmText: "Перезаписати",
                isDestructive: true,
                onConfirm: async () => await ExecuteCopyAsync(item, destPath, targetPane)
            );
            return;
        }

        await ExecuteCopyAsync(item, destPath, targetPane);
    }

    private async Task ExecuteCopyAsync(FileItemViewModel item, string destPath, FilePaneViewModel targetPane)
    {
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

        bool destExists = item.IsDirectory 
            ? _fileSystemService.DirectoryExists(destPath) 
            : File.Exists(destPath);

        if (destExists)
        {
            ShowConfirmationModal(
                title: "Перезаписати при переміщенні?",
                message: $"'{item.Name}' вже існує в цільовій папці:\n{targetPane.CurrentPath}\n\nБажаєте замінити і перемістити?",
                icon: "⚠️",
                confirmText: "Замінити",
                isDestructive: true,
                onConfirm: async () => await ExecuteMoveAsync(item, destPath, sourcePane, targetPane)
            );
            return;
        }

        await ExecuteMoveAsync(item, destPath, sourcePane, targetPane);
    }

    private async Task ExecuteMoveAsync(FileItemViewModel item, string destPath, FilePaneViewModel sourcePane, FilePaneViewModel targetPane)
    {
        try
        {
            if (item.IsDirectory)
            {
                if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
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
    public void DeleteItem(FileItemViewModel? item)
    {
        item ??= ActivePane.SelectedItem;
        if (item == null || item.Name == "..") return;

        string targetType = item.IsDirectory ? "папку" : "файл";
        string warningExtra = item.IsDirectory ? "\n⚠️ Усі файли та підпапки буде безповоротно видалено!" : "";

        ShowConfirmationModal(
            title: $"Видалити {targetType}?",
            message: $"Ви дійсно бажаєте видалити {targetType} '{item.Name}'?{warningExtra}",
            icon: "🗑",
            confirmText: "Видалити",
            isDestructive: true,
            onConfirm: async () =>
            {
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
        );
    }

    [RelayCommand]
    public void CreateFolder()
    {
        ModalTitle = "Створити нову папку";
        ModalMessage = $"Створення папки в:\n{ActivePane.CurrentPath}";
        ModalIcon = "📁";
        ModalConfirmButtonText = "Створити";
        IsModalDestructive = false;
        IsModalInputMode = true;
        ModalInputValue = "Нова папка";
        _modalConfirmAction = async () =>
        {
            var folderName = ModalInputValue.Trim();
            if (string.IsNullOrWhiteSpace(folderName)) return;

            string newPath = Path.Combine(ActivePane.CurrentPath, folderName);
            try
            {
                if (_fileSystemService.DirectoryExists(newPath))
                {
                    ShowBanner($"Папка з іменем '{folderName}' вже існує", NotificationSeverity.Warning);
                    return;
                }

                Directory.CreateDirectory(newPath);
                ShowBanner($"Створено папку: {folderName}", NotificationSeverity.Info);
                await ActivePane.RefreshAsync();
            }
            catch (Exception ex)
            {
                ShowBanner($"Помилка створення папки: {ex.Message}", NotificationSeverity.Error);
            }
        };
        IsModalOpen = true;
    }

    public void ShowConfirmationModal(string title, string message, string icon, string confirmText, bool isDestructive, Func<Task> onConfirm)
    {
        ModalTitle = title;
        ModalMessage = message;
        ModalIcon = icon;
        ModalConfirmButtonText = confirmText;
        IsModalDestructive = isDestructive;
        IsModalInputMode = false;
        ModalInputValue = string.Empty;
        _modalConfirmAction = onConfirm;
        IsModalOpen = true;
    }

    [RelayCommand]
    public async Task ConfirmModalAsync()
    {
        IsModalOpen = false;
        if (_modalConfirmAction != null)
        {
            await _modalConfirmAction.Invoke();
            _modalConfirmAction = null;
        }
    }

    [RelayCommand]
    public void CancelModal()
    {
        IsModalOpen = false;
        _modalConfirmAction = null;
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

    // --- Day 11: Bookmarks Management ---
    public async Task LoadBookmarksAsync()
    {
        try
        {
            var dtos = await _bookmarkService.LoadBookmarksAsync();
            BookmarkItems.Clear();
            foreach (var dto in dtos)
            {
                BookmarkItems.Add(new SidebarItemViewModel
                {
                    Name = dto.Name,
                    Path = dto.Path,
                    IconGlyph = dto.IconGlyph,
                    IsBookmark = true
                });
            }
        }
        catch (Exception ex)
        {
            ShowBanner($"Помилка завантаження закладок: {ex.Message}", NotificationSeverity.Warning);
        }
    }

    private async Task SaveBookmarksInternalAsync()
    {
        var dtos = BookmarkItems.Select(b => new BookmarkDto(b.Name, b.Path, b.IconGlyph)).ToList();
        await _bookmarkService.SaveBookmarksAsync(dtos);
    }

    [RelayCommand]
    public async Task AddCurrentFolderToBookmarksAsync()
    {
        var path = ActivePane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || path == "thispc://" || !_fileSystemService.DirectoryExists(path))
        {
            ShowBanner("Неможливо додати поточний шлях до закладок", NotificationSeverity.Warning);
            return;
        }

        var existing = BookmarkItems.FirstOrDefault(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            ShowBanner($"Папка вже є у закладках: {existing.Name}", NotificationSeverity.Info);
            return;
        }

        var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(dirName)) dirName = path;

        var newBookmark = new SidebarItemViewModel
        {
            Name = dirName,
            Path = path,
            IconGlyph = "⭐",
            IsBookmark = true
        };

        BookmarkItems.Add(newBookmark);
        await SaveBookmarksInternalAsync();
        ShowBanner($"Додано в закладки: {dirName}", NotificationSeverity.Info);
    }

    [RelayCommand]
    public async Task RemoveBookmarkAsync(SidebarItemViewModel? bookmark)
    {
        if (bookmark == null) return;
        BookmarkItems.Remove(bookmark);
        await SaveBookmarksInternalAsync();
        ShowBanner($"Видалено з закладок: {bookmark.Name}", NotificationSeverity.Info);
    }

    public void AddBookmarkForFolder(FileItemViewModel? item)
    {
        item ??= ActivePane.SelectedItem;
        if (item == null || !item.IsDirectory || item.Name == "..") return;

        var existing = BookmarkItems.FirstOrDefault(b => b.Path.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            ShowBanner($"Папка вже є у закладках: {existing.Name}", NotificationSeverity.Info);
            return;
        }

        var newBookmark = new SidebarItemViewModel
        {
            Name = item.Name,
            Path = item.FullPath,
            IconGlyph = "⭐",
            IsBookmark = true
        };

        BookmarkItems.Add(newBookmark);
        _ = SaveBookmarksInternalAsync();
        ShowBanner($"Додано в закладки: {item.Name}", NotificationSeverity.Info);
    }

    // --- Day 11: SHA-256 for active item ---
    public async Task ComputeSha256ForActiveItemAsync(FileItemViewModel? item)
    {
        item ??= ActivePane.SelectedItem;
        if (item == null || item.IsDirectory || item.Name == "..") return;

        if (!IsPreviewPaneVisible)
        {
            IsPreviewPaneVisible = true;
        }

        if (Preview.FilePath != item.FullPath)
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            await Preview.LoadPreviewAsync(item, _previewCts.Token);
        }

        await Preview.ComputeSha256Command.ExecuteAsync(null);
    }

    // --- Day 11: Open This PC ---
    [RelayCommand]
    public async Task OpenThisPcAsync()
    {
        LoadDrives();
        await ActivePane.NavigateToAsync("thispc://");
    }
}
