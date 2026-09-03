using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class SalesView : UserControl
{
    public SalesView() => InitializeComponent();

    private void OnDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as SalesViewModel)?.OpenCommand.Execute(null);
}
