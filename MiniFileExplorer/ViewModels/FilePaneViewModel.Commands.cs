using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MiniFileExplorer.ViewModels;

public partial class FilePaneViewModel
{
    public Action<FileItemViewModel?>? RequestDelete { get; set; }
    public Action? RequestCreateFolder { get; set; }
    public Action<FileItemViewModel?>? RequestRename { get; set; }
    public Func<Task>? RequestCopyBetweenPanes { get; set; }
    public Func<Task>? RequestMoveBetweenPanes { get; set; }
    public Action<FileItemViewModel?>? RequestShowProperties { get; set; }
    public Func<FileItemViewModel?, Task>? RequestCopyPath { get; set; }
    public Func<FileItemViewModel?, Task>? RequestComputeSha256 { get; set; }
    public Action<FileItemViewModel?>? RequestBookmark { get; set; }

    [RelayCommand]
    public void RenameItem(FileItemViewModel? item)
    {
        RequestRename?.Invoke(item ?? SelectedItem);
    }

    [RelayCommand]
    public void DeleteItem(FileItemViewModel? item)
    {
        RequestDelete?.Invoke(item ?? SelectedItem);
    }

    [RelayCommand]
    public void CreateFolder()
    {
        RequestCreateFolder?.Invoke();
    }

    [RelayCommand]
    public async Task CopyBetweenPanesAsync()
    {
        if (RequestCopyBetweenPanes != null)
        {
            await RequestCopyBetweenPanes.Invoke();
        }
    }

    [RelayCommand]
    public async Task MoveBetweenPanesAsync()
    {
        if (RequestMoveBetweenPanes != null)
        {
            await RequestMoveBetweenPanes.Invoke();
        }
    }

    [RelayCommand]
    public async Task CopyPathAsync(FileItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;
        if (RequestCopyPath != null)
        {
            await RequestCopyPath.Invoke(item);
        }
    }

    [RelayCommand]
    public void ShowProperties(FileItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item == null || item.Name == "..") return;
        RequestShowProperties?.Invoke(item);
    }

    [RelayCommand]
    public async Task ComputeSha256Async(FileItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item == null || item.IsDirectory || item.Name == "..") return;
        if (RequestComputeSha256 != null)
        {
            await RequestComputeSha256.Invoke(item);
        }
    }

    [RelayCommand]
    public void AddBookmark(FileItemViewModel? item)
    {
        RequestBookmark?.Invoke(item ?? SelectedItem);
    }
}