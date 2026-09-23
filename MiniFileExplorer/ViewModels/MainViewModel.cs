using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
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

public enum SortField
{
    Name,
    Type,
    Size,
    Date
}

public partial class MainViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _sizeCalcCts;
    private CancellationTokenSource? _previewCts;
    private readonly List<FileItemViewModel> _allItems = new();

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _pathInput = string.Empty;

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _quickAccessItems = new();

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _driveSidebarItems = new();

    [ObservableProperty]
    private ObservableCollection<DriveItem> _drives = new();

    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> _items = new();

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private FilePreviewViewModel _preview;

    [ObservableProperty]
    private bool _isPreviewPaneVisible = true;

    [ObservableProperty]
    private string _statusMessage = "Готово";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isCalculatingFolderSizes;

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private bool _hasBanner;

    [ObservableProperty]
    private string _bannerMessage = string.Empty;

    [ObservableProperty]
    private NotificationSeverity _bannerSeverity = NotificationSeverity.None;

    [ObservableProperty]
    private SortField _currentSortField = SortField.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    public string NameSortIndicator => CurrentSortField == SortField.Name ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string TypeSortIndicator => CurrentSortField == SortField.Type ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string SizeSortIndicator => CurrentSortField == SortField.Size ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string DateSortIndicator => CurrentSortField == SortField.Date ? (SortAscending ? " ▲" : " ▼") : string.Empty;

    public MainViewModel() : this(new FileSystemService())
    {
    }

    public MainViewModel(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService;
        _preview = new FilePreviewViewModel(_fileSystemService);

        InitSidebar();
        LoadDrives();

        var initialDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(initialDir) || !_fileSystemService.DirectoryExists(initialDir))
        {
            initialDir = Directory.GetCurrentDirectory();
        }

        _ = NavigateToAsync(initialDir);
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

    partial void OnSelectedItemChanged(FileItemViewModel? value)
    {
        if (!IsPreviewPaneVisible)
            return;

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        _ = Preview.LoadPreviewAsync(value, _previewCts.Token);
    }

    partial void OnIsPreviewPaneVisibleChanged(bool value)
    {
        if (value && SelectedItem != null)
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            _ = Preview.LoadPreviewAsync(SelectedItem, _previewCts.Token);
        }
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilterAndSort();
    }

    partial void OnCurrentSortFieldChanged(SortField value)
    {
        NotifySortIndicators();
        ApplyFilterAndSort();
    }

    partial void OnSortAscendingChanged(bool value)
    {
        NotifySortIndicators();
        ApplyFilterAndSort();
    }

    private void NotifySortIndicators()
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(TypeSortIndicator));
        OnPropertyChanged(nameof(SizeSortIndicator));
        OnPropertyChanged(nameof(DateSortIndicator));
    }

    private void LoadDrives()
    {
        Drives.Clear();
        DriveSidebarItems.Clear();

        foreach (var drive in _fileSystemService.GetDrives())
        {
            Drives.Add(drive);

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
    public void ClearFilter()
    {
        FilterQuery = string.Empty;
    }

    [RelayCommand]
    public void ToggleSort(string fieldName)
    {
        if (Enum.TryParse<SortField>(fieldName, true, out var field))
        {
            if (CurrentSortField == field)
            {
                SortAscending = !SortAscending;
            }
            else
            {
                CurrentSortField = field;
                SortAscending = true;
            }
        }
    }

    [RelayCommand]
    public async Task CalculateFolderSizesAsync()
    {
        _sizeCalcCts?.Cancel();
        _sizeCalcCts = new CancellationTokenSource();
        var token = _sizeCalcCts.Token;

        var directories = _allItems.Where(x => x.IsDirectory && x.Name != "..").ToList();
        if (directories.Count == 0) return;

        IsCalculatingFolderSizes = true;
        StatusMessage = $"Обчислення розмірів папок (0/{directories.Count})...";

        try
        {
            int completed = 0;
            foreach (var dir in directories)
            {
                token.ThrowIfCancellationRequested();

                dir.IsCalculatingSize = true;
                try
                {
                    var size = await _fileSystemService.CalculateDirectorySizeBytesAsync(dir.FullPath, token);
                    dir.CalculatedSizeBytes = size;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    dir.CalculatedSizeBytes = null;
                }
                finally
                {
                    dir.IsCalculatingSize = false;
                }

                completed++;
                StatusMessage = $"Обчислення розмірів папок ({completed}/{directories.Count})...";
            }

            if (!token.IsCancellationRequested)
            {
                StatusMessage = "Розміри папок обчислено";
                if (CurrentSortField == SortField.Size)
                {
                    ApplyFilterAndSort();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsCalculatingFolderSizes = false;
            }
        }
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

        _sizeCalcCts?.Cancel();
        IsCalculatingFolderSizes = false;

        _previewCts?.Cancel();
        Preview.Reset();

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

            _allItems.Clear();
            foreach (var item in rawItems)
            {
                _allItems.Add(new FileItemViewModel(item, _fileSystemService));
            }

            ApplyFilterAndSort();
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
            // Expected on fast navigation
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
    public async Task CopyPathAsync(FileItemViewModel? item)
    {
        item ??= SelectedItem;
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
        item ??= SelectedItem;
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
            await RefreshAsync();
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
        item ??= SelectedItem;
        if (item == null || item.Name == "..") return;

        string info = $"{item.Name} | Тип: {item.TypeDescription} | Розмір: {item.FormattedSize} | Змінено: {item.FormattedDate}";
        ShowBanner(info, NotificationSeverity.Info);
    }

    private void ApplyFilterAndSort()
    {
        var parentItem = _allItems.FirstOrDefault(x => x.Name == "..");
        var query = FilterQuery.Trim();

        IEnumerable<FileItemViewModel> filtered = _allItems.Where(x => x.Name != "..");

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(x => 
                x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.Extension) && x.Extension.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        }

        var dirs = filtered.Where(x => x.IsDirectory);
        var files = filtered.Where(x => !x.IsDirectory);

        dirs = SortCollection(dirs);
        files = SortCollection(files);

        Items.Clear();

        if (parentItem != null && string.IsNullOrWhiteSpace(query))
        {
            Items.Add(parentItem);
        }

        foreach (var dir in dirs)
        {
            Items.Add(dir);
        }

        foreach (var file in files)
        {
            Items.Add(file);
        }

        var filterInfo = string.IsNullOrWhiteSpace(query) ? string.Empty : $" (знайдено: {Items.Count(x => x.Name != "..")})";
        StatusMessage = $"Елементів: {_allItems.Count(x => x.Name != "..")}{filterInfo}";
    }

    private IEnumerable<FileItemViewModel> SortCollection(IEnumerable<FileItemViewModel> source)
    {
        return CurrentSortField switch
        {
            SortField.Name => SortAscending
                ? source.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                : source.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase),

            SortField.Type => SortAscending
                ? source.OrderBy(x => x.Extension ?? string.Empty, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name)
                : source.OrderByDescending(x => x.Extension ?? string.Empty, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name),

            SortField.Size => SortAscending
                ? source.OrderBy(x => x.SizeBytes ?? 0).ThenBy(x => x.Name)
                : source.OrderByDescending(x => x.SizeBytes ?? 0).ThenBy(x => x.Name),

            SortField.Date => SortAscending
                ? source.OrderBy(x => x.ModifiedDate ?? DateTime.MinValue).ThenBy(x => x.Name)
                : source.OrderByDescending(x => x.ModifiedDate ?? DateTime.MinValue).ThenBy(x => x.Name),

            _ => source
        };
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
