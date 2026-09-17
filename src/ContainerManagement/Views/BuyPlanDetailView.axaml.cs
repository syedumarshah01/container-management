using Avalonia.Controls;

namespace ContainerManagement.Views;

public partial class BuyPlanDetailView : UserControl
{
    public BuyPlanDetailView()
    {
        InitializeComponent();
        PageScrollBar.Attach(PageScroll);
        SizeChanged += (_, _) => PageScrollBar.Style(PageScroll);
    }
}
