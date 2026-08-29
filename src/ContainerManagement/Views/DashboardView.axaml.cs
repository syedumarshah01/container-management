using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private void OnReceivableDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as DashboardViewModel)?.OpenReceivableCommand.Execute(null);

    private void OnSaleDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as DashboardViewModel)?.OpenSaleCommand.Execute(null);

    private void OnContainerDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as DashboardViewModel)?.OpenContainerCommand.Execute(null);
}
