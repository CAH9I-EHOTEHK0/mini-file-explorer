using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniFileExplorer.ViewModels;

namespace MiniFileExplorer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.StartSearchCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (vm.IsModalOpen)
        {
            if (e.Key == Key.Escape)
            {
                vm.CancelModalCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter)
            {
                vm.ConfirmModalCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F2)
        {
            if (vm.ActivePane.SelectedItem != null && vm.RenameItemCommand.CanExecute(vm.ActivePane.SelectedItem))
            {
                vm.RenameItemCommand.Execute(vm.ActivePane.SelectedItem);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F7)
        {
            if (vm.CreateFolderCommand.CanExecute(null))
            {
                vm.CreateFolderCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Tab)
        {
            if (vm.IsDualPaneMode)
            {
                vm.SwapActivePaneCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F5)
        {
            if (vm.IsDualPaneMode && vm.CopyBetweenPanesCommand.CanExecute(null))
            {
                vm.CopyBetweenPanesCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F6)
        {
            if (vm.IsDualPaneMode && vm.MoveBetweenPanesCommand.CanExecute(null))
            {
                vm.MoveBetweenPanesCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
