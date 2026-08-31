using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private void OnUnpaidDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as DashboardViewModel)?.OpenUnpaidCommand.Execute(null);
}
