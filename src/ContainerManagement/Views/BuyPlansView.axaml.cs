using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class BuyPlansView : UserControl
{
    public BuyPlansView() => InitializeComponent();

    private void OnDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as BuyPlansViewModel)?.OpenCommand.Execute(null);
}
