using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class ProfitView : UserControl
{
    public ProfitView() => InitializeComponent();

    private void OnDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as ProfitViewModel)?.OpenCommand.Execute(null);
}
