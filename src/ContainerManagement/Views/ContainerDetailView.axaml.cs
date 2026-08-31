using Avalonia;
using Avalonia.Controls;

namespace ContainerManagement.Views;

public partial class ContainerDetailView : UserControl
{
    public ContainerDetailView()
    {
        InitializeComponent();
        PageScrollBar.Attach(PageScroll);
        SizeChanged += (_, _) => PageScrollBar.Style(PageScroll);
    }
}
