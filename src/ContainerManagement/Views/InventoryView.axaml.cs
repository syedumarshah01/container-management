using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class InventoryView : UserControl
{
    public InventoryView() => InitializeComponent();

    private void OnLotDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as InventoryViewModel)?.OpenLotCommand.Execute(null);
}
