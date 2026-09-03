using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class ReceivablesView : UserControl
{
    public ReceivablesView() => InitializeComponent();

    private void OnDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as ReceivablesViewModel)?.ReceiveCommand.Execute(null);
}
