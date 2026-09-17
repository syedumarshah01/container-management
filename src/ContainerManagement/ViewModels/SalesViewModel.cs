using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class SalesViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly SalesService _sales;
    private readonly LedgerService _ledger;
    private readonly IAppShell _shell;
    private List<SaleListRow> _all = new();

    public SalesViewModel(SalesService sales, LedgerService ledger, IAppShell shell, PrintService print)
    {
        _print = print;
        _sales = sales;
        _ledger = ledger;
        _shell = shell;
    }

    public override bool FillsPage => true;

    public ObservableCollection<SaleListRow> Rows { get; } = new();
    public ObservableCollection<CustomerFilter> Customers { get; } = new();

    [ObservableProperty] private SaleListRow? selected;
    [ObservableProperty] private CustomerFilter? selectedCustomer;

    public override async Task LoadAsync()
    {
        var customers = await _ledger.ListCustomersAsync();
        Customers.Clear();
        Customers.Add(new CustomerFilter { Id = 0, Name = "All customers" });
        foreach (var c in customers)
            Customers.Add(new CustomerFilter { Id = c.Id, Name = c.Name });
        SelectedCustomer ??= Customers[0];

        var list = await _sales.ListSalesAsync();
        _all = new List<SaleListRow>();
        foreach (var s in list)
        {
            var left = s.Status == SaleStatus.Cancelled ? 0 : await _sales.RemainingOnInvoiceAsync(s.Id);
            _all.Add(new SaleListRow
            {
                Id = s.Id,
                DateText = s.Date.ToString("dd MMM yyyy"),
                CustomerName = s.Customer.Name,
                CustomerId = s.CustomerId,
                Containers = string.Join(", ", s.Lines.Select(l => l.Container.Title).Distinct()),
                TotalText = Money.Pkr(s.TotalAmount),
                PaidText = Money.Pkr(s.PaidNow),
                CreditText = s.Status == SaleStatus.Cancelled
                    ? "Cancelled"
                    : left > 0 ? Money.Pkr(left) + " on ledger" : "Settled"
            });
        }
        ApplyFilter();
    }

    [ObservableProperty] private string query = "";

    partial void OnSelectedCustomerChanged(CustomerFilter? value) => ApplyFilter();

    partial void OnQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<SaleListRow> src = _all;
        if (SelectedCustomer is { Id: > 0 })
            src = _all.Where(r => r.CustomerId == SelectedCustomer.Id);
        // A search narrows the list, it does not change what is on it: every line that comes back is the bill
        // the page would have shown anyway, and no figure is added up over the words typed in a box.
        var q = Query?.Trim();
        if (!string.IsNullOrEmpty(q))
            src = src.Where(r => r.CustomerName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || r.Containers.Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || r.Id.ToString().Contains(q));
        Rows.Clear();
        foreach (var r in src)
            Rows.Add(r);
    }

    [RelayCommand]
    private void Print()
    {
        var rows = Rows.Select(r => new[]
        {
            r.DateText, r.CustomerName, r.Containers, r.TotalText, r.PaidText, r.CreditText,
        }).Cast<IReadOnlyList<string>>().ToList();
        _print.PrintTable("sales.html", "Bills", null,
            new[] { "Date", "Customer", "Containers", "Bill", "Paid", "On credit" },
            rows, null, 3);
        _shell.Notify("Printed from the page you were on.");
    }

    [RelayCommand]
    private void Open()
    {
        if (Selected is not null)
            _shell.OpenSale(Selected.Id);
    }

    [RelayCommand] private void NewSale() => _shell.GoNewSale();
}

public class SaleListRow
{
    public int Id { get; set; }
    public string DateText { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public int CustomerId { get; set; }
    public string Containers { get; set; } = "";
    public string TotalText { get; set; } = "";
    public string PaidText { get; set; } = "";
    public string CreditText { get; set; } = "";
}

public class CustomerFilter
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}
