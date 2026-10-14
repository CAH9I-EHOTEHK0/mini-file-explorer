using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniFileExplorer.ViewModels;

namespace MiniFileExplorer.Views;

public partial class FilePaneView : UserControl
{
    public FilePaneView()
    {
        InitializeComponent();

        AttachedToVisualTree += (s, e) =>
        {
            if (HeaderGrid?.ColumnDefinitions.Count >= 9)
            {
                var nameCol = HeaderGrid.ColumnDefinitions[1];
                var typeCol = HeaderGrid.ColumnDefinitions[3];
                var sizeCol = HeaderGrid.ColumnDefinitions[5];
                var dateCol = HeaderGrid.ColumnDefinitions[7];

                nameCol.PropertyChanged += (o, args) =>
                {
                    if (args.Property == ColumnDefinition.WidthProperty && DataContext is FilePaneViewModel vm)
                    {
                        var w = nameCol.Width;
                        if (Math.Abs(vm.ColNameWidth.Value - w.Value) > 0.5) vm.ColNameWidth = w;
                    }
                };

                typeCol.PropertyChanged += (o, args) =>
                {
                    if (args.Property == ColumnDefinition.WidthProperty && DataContext is FilePaneViewModel vm)
                    {
                        var w = typeCol.Width;
                        if (Math.Abs(vm.ColTypeWidth.Value - w.Value) > 0.5) vm.ColTypeWidth = w;
                    }
                };

                sizeCol.PropertyChanged += (o, args) =>
                {
                    if (args.Property == ColumnDefinition.WidthProperty && DataContext is FilePaneViewModel vm)
                    {
                        var w = sizeCol.Width;
                        if (Math.Abs(vm.ColSizeWidth.Value - w.Value) > 0.5) vm.ColSizeWidth = w;
                    }
                };

                dateCol.PropertyChanged += (o, args) =>
                {
                    if (args.Property == ColumnDefinition.WidthProperty && DataContext is FilePaneViewModel vm)
                    {
                        var w = dateCol.Width;
                        if (Math.Abs(vm.ColDateWidth.Value - w.Value) > 0.5) vm.ColDateWidth = w;
                    }
                };
            }
        };
    }

    private void OnAddressBarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is FilePaneViewModel vm)
            {
                vm.NavigateFromInputCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void OnItemListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FilePaneViewModel vm && vm.SelectedItem != null)
        {
            vm.OpenItemCommand.Execute(vm.SelectedItem);
        }
    }

    private void OnItemListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FilePaneViewModel vm)
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
        else if (e.Key == Key.F2)
        {
            if (vm.SelectedItem != null)
            {
                vm.RenameItemCommand.Execute(vm.SelectedItem);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            if (vm.SelectedItem != null)
            {
                if (VisualRoot is MainWindow mw && mw.DataContext is MainViewModel mainVm)
                {
                    mainVm.DeleteItemCommand.Execute(vm.SelectedItem);
                    e.Handled = true;
                }
            }
        }
    }

    private void OnPanePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ActivateThisPane();
    }

    private void OnListBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        ActivateThisPane();
    }

    private void ActivateThisPane()
    {
        if (DataContext is FilePaneViewModel vm && VisualRoot is MainWindow mw && mw.DataContext is MainViewModel mainVm)
        {
            mainVm.SetActivePane(vm);
        }
    }
}
