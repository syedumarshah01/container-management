using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class InventoryViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly ReportService _reports;
    private readonly IAppShell _shell;
    private List<InventoryRow> _all = new();

    public InventoryViewModel(ReportService reports, IAppShell shell, PrintService print)
    {
        _print = print;
        _reports = reports;
        _shell = shell;
    }

    public override bool FillsPage => true;

    public ObservableCollection<InventoryRow> Rows { get; } = new();
    public ObservableCollection<InventoryLot> SelectedLots { get; } = new();

    [ObservableProperty] private string query = "";
    [ObservableProperty] private string totalValue = "—";
    [ObservableProperty] private string lowHint = "";
    [ObservableProperty] private InventoryRow? selected;
    [ObservableProperty] private InventoryLot? selectedLot;
    [ObservableProperty] private string lotsHeading = "Select an item to see which containers hold it.";
    [ObservableProperty] private bool hasSelectedLots;

    /// <summary>Whether the shelf a closed container holds is listed too. Off by default, because the working
    /// shelf is what this page is for - and it changes which lines are listed and nothing else: the total above,
    /// the figures on every row and the lines on the printed sheet are the book's, read before the view is
    /// chosen, so putting a closed container out of sight cannot make the stock worth less.</summary>
    [ObservableProperty] private bool showClosed;

    [ObservableProperty] private string closedLabel = "Closed lots";
    [ObservableProperty] private bool showClosedButton;

    partial void OnShowClosedChanged(bool value) => ApplyFilter();

    /// <summary>The card under the figure holds one line, and only when it has something to say: "nothing is
    /// low" is not a fact about anybody's shelf. What the view leaves out is counted in the button for it.</summary>
    public bool ShowLowHint => LowHint.Length > 0;

    partial void OnLowHintChanged(string value) => OnPropertyChanged(nameof(ShowLowHint));

    public override async Task LoadAsync()
    {
        var keepId = Selected?.ProductId;
        _all = await _reports.GetGrandInventoryAsync();
        ApplyFilter();
        if (keepId is int id)
            Selected = Rows.FirstOrDefault(r => r.ProductId == id);
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(InventoryRow? value)
    {
        SelectedLots.Clear();
        if (value is null)
        {
            HasSelectedLots = false;
            LotsHeading = "Select an item to see which containers hold it.";
            return;
        }

        foreach (var lot in value.Lots.OrderBy(l => l.ContainerTitle))
            SelectedLots.Add(lot);
        HasSelectedLots = SelectedLots.Count > 0;
        // The row's own figures still count the lots that are not listed, so the heading says so: an item that
        // reads 48 in stock while listing 40 is honest only if it names the other 8.
        var hidden = value.HiddenLots > 0
            ? "  " + value.HiddenLots + " more in "
              + (value.HiddenLots == 1 ? "a closed container" : "closed containers") + ", not listed."
            : "";
        LotsHeading = SelectedLots.Count == 1
            ? value.ProductName + " is in 1 container." + hidden
            : value.ProductName + " is in " + SelectedLots.Count + " containers." + hidden;
    }

    [RelayCommand]
    private void Print()
    {
        var rows = Rows.Select(r => new[] { r.ProductName, r.SkuText, r.Unit, r.InStockText, r.ValueText, r.LotsText })
            .Cast<IReadOnlyList<string>>().ToList();
        // The sheet lists the rows on screen and its total is the sum of the lines it carries, so the figures on
        // paper add back up to themselves. What the view leaves out is named at the top rather than dropped
        // without a word, and the card above the list keeps reading the whole book either way.
        var aside = StockListRules.Aside(_all);
        var stamp = aside.Items == 0 ? null
            : ShowClosed
                ? "closed containers included - " + Money.Pkr(aside.Value) + " of it"
                : aside.Items + " items in closed containers are not listed - " + Money.Pkr(aside.Value);
        _print.PrintTable("stock.html", "Stock on the shelf", stamp,
            new[] { "Item", "Code", "Unit", "In stock", "Worth", "Lots" },
            rows, new[]
            {
                "Total", "", "",
                Money.Qty(Money.Round(Rows.Sum(r => r.TotalRemaining))),
                Money.Pkr(Money.Round(Rows.Sum(r => r.TotalValue))), "",
            }, 3);
        _shell.Notify("Printed from the page you were on.");
    }

    [RelayCommand]
    private void ToggleClosed() => ShowClosed = !ShowClosed;

    [RelayCommand]
    private void OpenLot()
    {
        if (SelectedLot is not null)
            _shell.OpenContainer(SelectedLot.ContainerId);
    }

    private void ApplyFilter()
    {
        // View first, then search, and the search only narrows what the view left: the rule the Containers page
        // holds, so typing a closed container's name cannot reach stock this page is not showing.
        IEnumerable<InventoryRow> src = StockListRules.Shown(_all, ShowClosed);
        var q = Query?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            src = src.Where(r =>
                r.ProductName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (r.Sku ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Lots.Any(l => l.ContainerTitle.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        var list = src.ToList();
        var keepId = Selected?.ProductId;
        Rows.Clear();
        foreach (var r in list)
            Rows.Add(r);

        // The card reads the book, not the filtered list: a figure on a card named Total stock value that moved
        // when somebody typed a letter was answering a different question from the one it looks like. When the
        // list is narrowed, the card says how much of it is listed, which is all a search may honestly do.
        // One figure on the card, and only the book's own. The counts of items, lots and units this page used to
        // carry were shown nowhere and read by nothing, which on a money page is not spare code but a number that
        // will eventually disagree with the shelf it stopped describing.
        TotalValue = Money.Pkr(Money.Round(_all.Sum(r => r.TotalValue)));
        var low = _all.Count(r => r.IsLow);
        LowHint = low == 0 ? "" : low + " items are at or below the low-stock level (Settings).";

        // The count of what is aside belongs to the control that reveals it, where reading it is also doing
        // something about it - not to a sentence under the money, which would itself change as the button went.

        var aside = StockListRules.Aside(_all);
        ShowClosedButton = aside.Items > 0;
        ClosedLabel = ShowClosed ? "Open lots only" : $"Closed lots ({aside.Items})";

        Selected = keepId is int id ? Rows.FirstOrDefault(r => r.ProductId == id) : null;
    }
}
