using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class ItemSalesView : UserControl
{
    public ItemSalesView() => InitializeComponent();

    private void OnCustomerDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as ItemSalesViewModel)?.OpenCustomerCommand.Execute(null);
}
