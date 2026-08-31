using Avalonia.Controls;

namespace ContainerManagement.Views;

public partial class ContainerDetailView : UserControl
{
    public ContainerDetailView()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Height > 0)
                MainPane.Height = e.NewSize.Height;
        };
    }
}
