using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ContainersViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly ReportService _reports;
    private readonly InventoryService _inventory;
    private readonly IAppShell _shell;

    public ContainersViewModel(ReportService reports, InventoryService inventory, IAppShell shell, PrintService print)
    {
        _print = print;
        _reports = reports;
        _inventory = inventory;
        _shell = shell;
    }

    public ObservableCollection<ContainerProfitRow> Rows { get; } = new();

    /// <summary>How the money handed over at creation went out - the same list the We owe page uses.</summary>
    public IReadOnlyList<string> PaidMethods { get; } = SupplierPayMethods.All;

    [ObservableProperty] private ContainerProfitRow? selected;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private string newNumber = "";
    [ObservableProperty] private string newOrigin = "China";
    // Deliberately no default: the arrival date is a fact about the shipment, not about the day the
    // entry happens to be typed in, so the form asks and waits rather than filling itself in.
    [ObservableProperty] private DateTimeOffset? newArrival;
    [ObservableProperty] private string newNotes = "";
    [ObservableProperty] private string newSupplier = "";
    [ObservableProperty] private decimal? newSupplierAmount;
    [ObservableProperty] private decimal? newPaid;
    [ObservableProperty] private string newPaidMethod = "Bank Transfer";
    [ObservableProperty] private decimal? newCartons;
    [ObservableProperty] private decimal? newCbm;
    [ObservableProperty] private decimal? newWeight;
    [ObservableProperty] private bool showAddForm;

    private List<ContainerProfitRow> _all = new();

    [ObservableProperty] private string query = "";

    partial void OnQueryChanged(string value) => ApplyFilter();

    public override async Task LoadAsync()
    {
        _all = await _reports.GetContainerProfitsAsync();
        ApplyFilter();
    }

    /// <summary>
    /// The list as the shop asked to see it. A search narrows rows and touches no figure: what is left on each
    /// container is what the row carries, so the page cannot come to a different total than the container's own
    /// page does because somebody typed a letter and deleted another.
    /// </summary>
    private void ApplyFilter()
    {
        IEnumerable<ContainerProfitRow> src = _all;
        var q = Query?.Trim();
        if (!string.IsNullOrEmpty(q))
            src = _all.Where(r => r.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || r.ContainerNumber.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || (r.Origin ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || r.StatusText.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || r.ArrivalText.Contains(q, StringComparison.OrdinalIgnoreCase));
        Rows.Clear();
        foreach (var r in src)
            Rows.Add(r);
    }

    /// <summary>
    /// The list as the page shows it, on paper. Every cell is the text the row already carries, and there is
    /// no total line here because the page has none: a figure the screen never showed must not appear on the
    /// sheet, however obvious it looks.
    /// </summary>
    [RelayCommand]
    private void Print()
    {
        var rows = Rows.Select(r => new[]
        {
            r.Title, r.Origin, r.ArrivalText, r.StatusText, r.QtySoldText,
            r.RevenueText, r.CollectedText, r.InMarketText, r.RemainingValueText, r.ProfitText,
        }).Cast<IReadOnlyList<string>>().ToList();
        _print.PrintTable("containers.html", "Containers", null,
            new[] { "Container", "From", "Landed", "State", "Sold", "Sold for", "Collected", "In the market", "Stock value", "Profit" },
            rows, null, 4);
        _shell.Notify("Printed from the page you were on.");
    }

    [RelayCommand]
    private void Open()
    {
        if (Selected is not null)
            _shell.OpenContainer(Selected.ContainerId);
    }

    [RelayCommand]
    private void BeginAdd() => ShowAddForm = true;

    [RelayCommand]
    private void CancelAdd() => ShowAddForm = false;

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (NewArrival is null)
        {
            _shell.Notify("When did it arrive? Select the date.", true);
            return;
        }
        try
        {
            var c = await _inventory.CreateContainerAsync(
                NewTitle,
                NewNumber,
                NewOrigin,
                NewArrival?.DateTime,
                NewNotes,
                "PKR",
                1,
                null,
                NewCartons,
                NewCbm,
                NewWeight,
                NewSupplier,
                NewSupplierAmount ?? 0,
                NewPaid ?? 0,
                NewPaidMethod);
            var paid = NewPaid ?? 0;
            // The box is read as what is still owed, so the paid-now money never nets it down: this line
            // has to say the same thing the We owe page will say a second later.
            var owed = Money.Round(NewSupplierAmount ?? 0);
            var what = $"Container '{c.Title}' created.";
            if (paid > 0)
                what += owed > 0.009m
                    ? $" {Money.Pkr(paid)} paid by {NewPaidMethod}, {Money.Pkr(owed)} still owed."
                    : $" {Money.Pkr(paid)} paid by {NewPaidMethod}, supplier settled.";
            else if (owed > 0.009m)
                what += $" {Money.Pkr(owed)} owed on this container.";
            what += " Add items on the next screen.";
            _shell.Notify(what);
            NewTitle = "";
            NewNumber = "";
            NewArrival = null;
            NewNotes = "";
            NewSupplier = "";
            NewSupplierAmount = 0;
            NewPaid = null;
            NewCartons = null;
            NewCbm = null;
            NewWeight = null;
            ShowAddForm = false;
            await LoadAsync();
            _shell.OpenContainer(c.Id);
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }
}
