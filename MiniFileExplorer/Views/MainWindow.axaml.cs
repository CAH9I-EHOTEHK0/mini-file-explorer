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

    private void OnAddressBarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.NavigateFromInputCommand.Execute(null);
            }
            e.Handled = true;
        }
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

    private void OnItemListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedItem != null)
        {
            vm.OpenItemCommand.Execute(vm.SelectedItem);
        }
    }

    private void OnItemListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.Key == Key.Enter)
        {
            if (vm.SelectedItem != null)
            {
                vm.OpenItemCommand.Execute(vm.SelectedItem);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Back)
        {
            if (vm.CanGoUp)
            {
                vm.GoUpCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            if (vm.SelectedItem != null)
            {
                vm.DeleteItemCommand.Execute(vm.SelectedItem);
                e.Handled = true;
            }
        }
    }
}
