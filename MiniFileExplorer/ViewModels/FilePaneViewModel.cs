using System;
using System.Collections.Generic;
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

public enum SortField
{
    Name,
    Type,
    Size,
    Date
}

public partial class FilePaneViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _sizeCalcCts;
    private readonly List<FileItemViewModel> _allItems = new();

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

    public string NameSortIndicator => CurrentSortField == SortField.Name ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string TypeSortIndicator => CurrentSortField == SortField.Type ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string SizeSortIndicator => CurrentSortField == SortField.Size ? (SortAscending ? " ▲" : " ▼") : string.Empty;
    public string DateSortIndicator => CurrentSortField == SortField.Date ? (SortAscending ? " ▲" : " ▼") : string.Empty;

    public FilePaneViewModel(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService;
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

        char[] invalidChars = Path.GetInvalidPathChars();
        if (targetPath.IndexOfAny(invalidChars) >= 0)
        {
            OnNotification?.Invoke($"Шлях містить неприпустимі символи: '{targetPath}'", NotificationSeverity.Error);
            StatusMessage = "Помилка введення шляху";
            PathInput = CurrentPath;
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        _sizeCalcCts?.Cancel();
        IsCalculatingFolderSizes = false;

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

            _allItems.Clear();
            foreach (var item in rawItems)
            {
                _allItems.Add(new FileItemViewModel(item, _fileSystemService));
            }

            ApplyFilterAndSort();
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
