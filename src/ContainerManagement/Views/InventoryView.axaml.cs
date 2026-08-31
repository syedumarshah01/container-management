using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
        PageScrollBar.Attach(PageScroll);
        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Height > 0)
                MainPane.Height = e.NewSize.Height;
            PageScrollBar.Style(PageScroll);
        };
    }

    private void OnLotDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as InventoryViewModel)?.OpenLotCommand.Execute(null);
}
