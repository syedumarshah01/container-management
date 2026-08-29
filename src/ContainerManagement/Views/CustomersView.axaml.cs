using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    private void OnDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as CustomersViewModel)?.OpenCommand.Execute(null);
}
