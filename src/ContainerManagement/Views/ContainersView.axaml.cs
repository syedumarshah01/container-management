using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class ContainersView : UserControl
{
    public ContainersView() => InitializeComponent();

    private void OnDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as ContainersViewModel)?.OpenCommand.Execute(null);
}
