using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniFileExplorer.Models;
using MiniFileExplorer.Services;

namespace MiniFileExplorer.ViewModels;

public enum SortField
{
    Name,
    Type,
    Size,
    Date
}

public enum PaneViewMode
{
    Details,
    Grid
}

public partial class FilePaneViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _sizeCalcCts;
    private readonly List<FileItemViewModel> _allItems = new();
    private FileSystemWatcher? _watcher;
    private readonly System.Timers.Timer _watcherDebounceTimer = new(350) { AutoReset = false };
    private readonly object _watcherLock = new();

    public event Action<string, NotificationSeverity>? OnNotification;
    public event Action<FileItemViewModel?>? OnSelectionChanged;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _pathInput = string.Empty;

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> _items = new();

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _statusMessage = "Готово";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isCalculatingFolderSizes;

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private SortField _currentSortField = SortField.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private GridLength _colNameWidth = new(200, GridUnitType.Pixel);

    [ObservableProperty]
    private GridLength _colTypeWidth = new(85, GridUnitType.Pixel);

    [ObservableProperty]
    private GridLength _colSizeWidth = new(85, GridUnitType.Pixel);

    [ObservableProperty]
    private GridLength _colDateWidth = new(120, GridUnitType.Pixel);

    [ObservableProperty]
    private PaneViewMode _viewMode = PaneViewMode.Details;

    public bool IsDetailsView => ViewMode == PaneViewMode.Details;
    public bool IsGridView => ViewMode == PaneViewMode.Grid;

    public string ViewModeGlyph => ViewMode == PaneViewMode.Details ? "🔲" : "☰";
    public string ViewModeToolTip => ViewMode == PaneViewMode.Details ? "Перемкнути на вигляд 'Великі піктограми'" : "Перемкнути на вигляд 'Таблиця'";

    public string NameSortIndicator => CurrentSortField == SortField.Name ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string TypeSortIndicator => CurrentSortField == SortField.Type ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string SizeSortIndicator => CurrentSortField == SortField.Size ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string DateSortIndicator => CurrentSortField == SortField.Date ? (SortAscending ? " ▲" : " ▼") : string.Empty;

    public Bitmap? NameSortBitmap => CurrentSortField == SortField.Name ? GetSortChevronBitmap() : null;
    public Bitmap? TypeSortBitmap => CurrentSortField == SortField.Type ? GetSortChevronBitmap() : null;
    public Bitmap? SizeSortBitmap => CurrentSortField == SortField.Size ? GetSortChevronBitmap() : null;
    public Bitmap? DateSortBitmap => CurrentSortField == SortField.Date ? GetSortChevronBitmap() : null;

    private Bitmap? GetSortChevronBitmap() =>
        IconService.LoadBitmap(SortAscending ? "icons8-chevron-up-64.png" : "icons8-chevron-down-64.png");

    partial void OnViewModeChanged(PaneViewMode value)
    {
        OnPropertyChanged(nameof(IsDetailsView));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(ViewModeGlyph));
        OnPropertyChanged(nameof(ViewModeToolTip));
    }

    [RelayCommand]
    public void ToggleViewMode()
    {
        ViewMode = (ViewMode == PaneViewMode.Details) ? PaneViewMode.Grid : PaneViewMode.Details;
    }

    public FilePaneViewModel(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService;
        _watcherDebounceTimer.Elapsed += async (_, _) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Refresh silently without showing full-page loading blink
                await SilentRefreshAsync();
            });
        };
    }

    partial void OnSelectedItemChanged(FileItemViewModel? value)
    {
        OnSelectionChanged?.Invoke(value);
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
        OnPropertyChanged(nameof(NameSortBitmap));
        OnPropertyChanged(nameof(TypeSortBitmap));
        OnPropertyChanged(nameof(SizeSortBitmap));
        OnPropertyChanged(nameof(DateSortBitmap));
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
        StatusMessage = $"Обчислення розмірів (0/{directories.Count})...";

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
                StatusMessage = $"Обчислення розмірів ({completed}/{directories.Count})...";
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
            OnNotification?.Invoke("Шлях не може бути порожнім.", NotificationSeverity.Warning);
            return;
        }

        targetPath = targetPath.Trim('\"', '\'').Trim();

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        _sizeCalcCts?.Cancel();
        IsCalculatingFolderSizes = false;

        IsLoading = true;
        StatusMessage = "Завантаження...";

        if (string.Equals(targetPath, "thispc://", StringComparison.OrdinalIgnoreCase))
        {
            StopWatcher();
            CurrentPath = "thispc://";
            PathInput = "Цей ПК";
            CanGoUp = false;

            _allItems.Clear();
            var drives = _fileSystemService.GetDrives();
            foreach (var d in drives)
            {
                _allItems.Add(new FileItemViewModel(d, _fileSystemService));
            }

            ApplyFilterAndSort();
            IsLoading = false;
            StatusMessage = $"Дисків: {_allItems.Count}";
            return;
        }

        char[] invalidChars = Path.GetInvalidPathChars();
        if (targetPath.IndexOfAny(invalidChars) >= 0)
        {
            OnNotification?.Invoke($"Шлях містить неприпустимі символи: '{targetPath}'", NotificationSeverity.Error);
            StatusMessage = "Помилка введення шляху";
            PathInput = CurrentPath;
            IsLoading = false;
            return;
        }

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
            StartWatcher(targetPath);

            // Auto-calculate folder sizes in background
            _ = CalculateFolderSizesAsync();
        }
        catch (UnauthorizedAccessException)
        {
            OnNotification?.Invoke($"Доступ заборонено: у вас немає прав для перегляду '{targetPath}'", NotificationSeverity.Error);
            StatusMessage = "Помилка: відмовлено в доступі";
            PathInput = CurrentPath;
        }
        catch (DirectoryNotFoundException)
        {
            OnNotification?.Invoke($"Каталог не існує: '{targetPath}'", NotificationSeverity.Warning);
            StatusMessage = "Помилка: каталог не знайдено";
            PathInput = CurrentPath;
        }
        catch (PathTooLongException)
        {
            OnNotification?.Invoke("Занадто довгий шлях до каталогу (перевищено ліміт ОС).", NotificationSeverity.Error);
            StatusMessage = "Помилка: шлях занадто довгий";
            PathInput = CurrentPath;
        }
        catch (SecurityException)
        {
            OnNotification?.Invoke($"Помилка безпеки при доступі до '{targetPath}'", NotificationSeverity.Error);
            StatusMessage = "Помилка безпеки";
            PathInput = CurrentPath;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            OnNotification?.Invoke($"Непередбачена помилка: {ex.Message}", NotificationSeverity.Error);
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
        var input = PathInput.Trim();
        if (input.Equals("Цей ПК", StringComparison.OrdinalIgnoreCase) || input.Equals("thispc://", StringComparison.OrdinalIgnoreCase))
        {
            return NavigateToAsync("thispc://");
        }
        return NavigateToAsync(PathInput);
    }

    [RelayCommand]
    public Task GoUpAsync()
    {
        if (CurrentPath.Equals("thispc://", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var parent = _fileSystemService.GetParentDirectory(CurrentPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            return NavigateToAsync(parent);
        }
        else
        {
            // If already at drive root, going up takes you to "Цей ПК"
            return NavigateToAsync("thispc://");
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (CurrentPath.Equals("thispc://", StringComparison.OrdinalIgnoreCase))
        {
            await NavigateToAsync("thispc://");
            return;
        }

        if (!_fileSystemService.DirectoryExists(CurrentPath))
        {
            OnNotification?.Invoke("Поточний каталог більше не існує. Повертаємось до батьківської папки.", NotificationSeverity.Warning);
            var fallback = _fileSystemService.GetParentDirectory(CurrentPath);
            while (!string.IsNullOrWhiteSpace(fallback) && !_fileSystemService.DirectoryExists(fallback))
            {
                fallback = _fileSystemService.GetParentDirectory(fallback);
            }

            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = "thispc://";
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
                    OnNotification?.Invoke($"Файл не знайдено: '{item.Name}'", NotificationSeverity.Warning);
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
                OnNotification?.Invoke($"Не вдалося запустити файл '{item.Name}': {ex.Message}", NotificationSeverity.Error);
            }
        }
    }

    public async Task SilentRefreshAsync()
    {
        if (CurrentPath.Equals("thispc://", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_fileSystemService.DirectoryExists(CurrentPath))
        {
            // The directory was removed or renamed externally!
            StopWatcher();
            OnNotification?.Invoke("Каталог було змінено або видалено ззовні. Повертаємось до доступної папки.", NotificationSeverity.Warning);

            var fallback = _fileSystemService.GetParentDirectory(CurrentPath);
            while (!string.IsNullOrWhiteSpace(fallback) && !_fileSystemService.DirectoryExists(fallback))
            {
                fallback = _fileSystemService.GetParentDirectory(fallback);
            }

            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = "thispc://";
            }

            await NavigateToAsync(fallback);
            return;
        }

        try
        {
            var rawItems = await _fileSystemService.GetItemsAsync(CurrentPath);
            var selectedPath = SelectedItem?.FullPath;

            _allItems.Clear();
            foreach (var item in rawItems)
            {
                _allItems.Add(new FileItemViewModel(item, _fileSystemService));
            }

            ApplyFilterAndSort();

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                SelectedItem = Items.FirstOrDefault(x => x.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            }

            // Auto-calculate folder sizes in background
            _ = CalculateFolderSizesAsync();
        }
        catch
        {
            // Degrade silently if temporary locked
        }
    }

    private void StartWatcher(string path)
    {
        StopWatcher();

        if (string.IsNullOrWhiteSpace(path) || path.Equals("thispc://", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (!_fileSystemService.DirectoryExists(path))
                return;

            lock (_watcherLock)
            {
                _watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemChanged;
                _watcher.Changed += OnFileSystemChanged;
                _watcher.Error += OnWatcherError;
            }
        }
        catch
        {
            // Ignore watcher failure on network or restricted drives
        }
    }

    private void StopWatcher()
    {
        lock (_watcherLock)
        {
            _watcherDebounceTimer.Stop();
            if (_watcher != null)
            {
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnFileSystemChanged;
                    _watcher.Deleted -= OnFileSystemChanged;
                    _watcher.Renamed -= OnFileSystemChanged;
                    _watcher.Changed -= OnFileSystemChanged;
                    _watcher.Error -= OnWatcherError;
                    _watcher.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
                finally
                {
                    _watcher = null;
                }
            }
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce frequent file system notifications
        _watcherDebounceTimer.Stop();
        _watcherDebounceTimer.Start();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await SilentRefreshAsync();
        });
    }

    public void SetItemsDirectly(IEnumerable<FileItemViewModel> items, string status)
    {
        _allItems.Clear();
        _allItems.AddRange(items);
        Items.Clear();
        foreach (var item in _allItems)
        {
            Items.Add(item);
        }
        StatusMessage = status;
    }

    public void AddItemDirectly(FileItemViewModel item)
    {
        _allItems.Add(item);
        Items.Add(item);
    }

    public void ClearDirectly()
    {
        _allItems.Clear();
        Items.Clear();
    }

    public void ApplyFilterAndSort()
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
}
