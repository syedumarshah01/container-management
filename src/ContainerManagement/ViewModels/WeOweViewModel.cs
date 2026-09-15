using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// What the shop owes, in one place: the containers still due to suppliers in China, and the customers
/// whose own ledger is in their favour. The second group is not a different kind of debt - it is money
/// sitting in the till that belongs to someone - which is why it lives here rather than on the receivables
/// page, and why both are paid from this page the same way: record the money that actually left, and let
/// the amount owed fall by exactly that.
/// </summary>
public partial class WeOweViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly CashBookService _cash;
    private readonly InventoryService _inventory;
    private readonly LedgerService _ledger;
    private readonly IAppShell _shell;
    private bool _ready;

    public WeOweViewModel(
        CashBookService cash, InventoryService inventory, LedgerService ledger, IAppShell shell, PrintService print)
    {
        _print = print;
        _cash = cash;
        _inventory = inventory;
        _ledger = ledger;
        _shell = shell;
        _ready = true;
    }

    public ObservableCollection<WeOweRow> Rows { get; } = new();
    public ObservableCollection<PayContainerOption> Containers { get; } = new();
    public IReadOnlyList<string> Methods { get; } = SupplierPayMethods.All;
    public ObservableCollection<SupplierPaymentRow> Payments { get; } = new();
    private List<SupplierPaymentRow> _paid = new();

    public ObservableCollection<CustomerOwedRow> Customers { get; } = new();
    public ObservableCollection<CustomerOwedRow> PayOptions { get; } = new();
    public ObservableCollection<CustomerPayoutRow> Payouts { get; } = new();
    public IReadOnlyList<string> CustomerMethods { get; } = PaymentMethods.All;

    [ObservableProperty] private string totalOwed = Money.Pkr(0);
    [ObservableProperty] private string owedSplit = "";
    [ObservableProperty] private WeOweRow? selected;
    [ObservableProperty] private PayContainerOption? payContainer;
    [ObservableProperty] private DateTimeOffset? payDate = DateTimeOffset.Now;
    [ObservableProperty] private decimal? payAmount;
    [ObservableProperty] private string payNotes = "";
    [ObservableProperty] private string payMethod = "Bank Transfer";
    [ObservableProperty] private string weOweThem = "—";
    [ObservableProperty] private bool showPayments;

    [ObservableProperty] private CustomerOwedRow? selectedCustomer;
    [ObservableProperty] private CustomerOwedRow? payCustomer;
    [ObservableProperty] private DateTimeOffset? payCustomerDate = DateTimeOffset.Now;
    [ObservableProperty] private decimal? payCustomerAmount;
    [ObservableProperty] private string payCustomerNotes = "";
    [ObservableProperty] private string payCustomerMethod = "Cash";
    [ObservableProperty] private string customerOwedText = "—";
    [ObservableProperty] private bool showPayouts;

    public override async Task LoadAsync()
    {
        var keepPay = PayContainer?.Id ?? Selected?.ContainerId;
        var keepCustomer = PayCustomer?.CustomerId ?? SelectedCustomer?.CustomerId;
        var targets = await _cash.SupplierContainersAsync();
        var owed = await _ledger.GetCustomerOwedAsync();

        Rows.Clear();
        _targets = new List<PayContainerOption>();
        foreach (var t in targets.OrderByDescending(x => x.Owed).ThenBy(x => x.SupplierName))
        {
            Rows.Add(new WeOweRow
            {
                ContainerId = t.Id,
                SupplierName = t.SupplierName,
                ContainerTitle = t.ContainerTitle,
                Owed = t.Owed
            });
            _targets.Add(new PayContainerOption
            {
                Id = t.Id,
                Label = t.Label,
                SupplierName = t.SupplierName,
                Owed = t.Owed
            });
        }

        // The grid shows the ones money is due to; the dropdown also holds the ones already paid back, so
        // the payout that cleared them is still readable where it was made.
        _owed = owed;
        Customers.Clear();
        foreach (var r in owed)
        {
            if (r.Owed > 0.009m)
                Customers.Add(r);
        }
        ApplySupplierFilter();
        ApplyCustomerFilter();

        _paid = await _cash.SupplierPaymentsAsync();
        var supplierOwed = targets.Where(t => t.Owed > 0).Sum(t => t.Owed);
        var customerOwed = owed.Sum(r => r.Owed);
        TotalOwed = Money.Pkr(supplierOwed + customerOwed);
        OwedSplit = "Suppliers " + Money.Pkr(supplierOwed) + " · Customers " + Money.Pkr(customerOwed);
        PayContainer = _targets.FirstOrDefault(c => c.Id == keepPay) ?? _targets.FirstOrDefault();
        Selected = Rows.FirstOrDefault(r => r.ContainerId == PayContainer?.Id);
        PayCustomer = _owed.FirstOrDefault(c => c.CustomerId == keepCustomer) ?? _owed.FirstOrDefault();
        SelectedCustomer = Customers.FirstOrDefault(r => r.CustomerId == PayCustomer?.CustomerId);
        // The pickers are rebuilt once from what the forms ended up pointing at, so a search that was already
        // typed keeps holding the choice it was made over.
        ApplySupplierFilter();
        ApplyCustomerFilter();
        ShowOwed();
        await ShowOwedCustomerAsync();
    }

    // The two payment pickers, and what narrows them. The full lists stay behind so a reload can rebuild a
    // picker without losing the person or the container it was pointing at.
    [ObservableProperty] private string supplierQuery = "";
    [ObservableProperty] private string customerQuery = "";
    private List<PayContainerOption> _targets = new();
    private List<CustomerOwedRow> _owed = new();

    partial void OnSupplierQueryChanged(string value) => ApplySupplierFilter();

    partial void OnCustomerQueryChanged(string value) => ApplyCustomerFilter();

    /// <summary>
    /// The picker in a payment form, narrowed by what was typed. Whatever the form is already pointing at stays
    /// in the list, and stays selected, even when it stops matching the words: a picker that drops your choice
    /// mid-typing has changed the payment rather than the view of it, and a payment form whose target went blank
    /// while somebody was looking for a name is a form nobody should be sending money from.
    /// </summary>
    private void ApplySupplierFilter()
    {
        // A combo box lets go of a selection it can no longer see, and these two pickers are the form: if the
        // target went null while somebody typed a letter, the payment would be aimed at nobody. The choice is
        // put back after the list is rebuilt, whatever the box did in between.
        var keep = PayContainer;
        Containers.Clear();
        foreach (var o in Match(_targets, o => o.Label + " " + o.SupplierName, SupplierQuery))
            Containers.Add(o);
        if (keep is not null && Containers.All(o => o.Id != keep.Id))
            Containers.Insert(0, keep);
        if (keep is not null && PayContainer is null)
            PayContainer = keep;
    }

    private void ApplyCustomerFilter()
    {
        var keep = PayCustomer;
        PayOptions.Clear();
        foreach (var r in Match(_owed, r => r.Name + " " + (r.Phone ?? ""), CustomerQuery))
            PayOptions.Add(r);
        if (keep is not null && PayOptions.All(r => r.CustomerId != keep.CustomerId))
            PayOptions.Insert(0, keep);
        if (keep is not null && PayCustomer is null)
            PayCustomer = keep;
    }

    private static IEnumerable<T> Match<T>(List<T> rows, Func<T, string> asSaid, string query)
    {
        var q = query?.Trim();
        return string.IsNullOrEmpty(q)
            ? rows
            : rows.Where(r => asSaid(r).Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedChanged(WeOweRow? value)
    {
        if (!_ready || value is null) return;
        PayContainer = Containers.FirstOrDefault(c => c.Id == value.ContainerId);
    }

    partial void OnPayContainerChanged(PayContainerOption? value)
    {
        if (_ready) ShowOwed();
    }

    private void ShowOwed()
    {
        if (PayContainer is null)
        {
            WeOweThem = "—";
            Payments.Clear();
            ShowPayments = false;
            return;
        }
        Payments.Clear();
        foreach (var p in _paid.Where(p => p.ContainerId == PayContainer.Id))
            Payments.Add(p);
        ShowPayments = Payments.Count > 0;

        var owed = PayContainer.Owed;
        if (owed > 0.009m)
            WeOweThem = Money.Pkr(owed);
        else if (owed < -0.009m)
            WeOweThem = CashBookService.PaidExtraText(-owed);
        else
            WeOweThem = Money.Pkr(0);
    }

    partial void OnSelectedCustomerChanged(CustomerOwedRow? value)
    {
        if (!_ready || value is null) return;
        PayCustomer = PayOptions.FirstOrDefault(c => c.CustomerId == value.CustomerId);
    }

    partial void OnPayCustomerChanged(CustomerOwedRow? value)
    {
        if (_ready) _ = ShowOwedCustomerAsync();
    }

    /// <summary>
    /// The figure the pay box is working against, and the money already handed over to that customer. The
    /// amount owed is what their ledger says, in rupees, before the amount is typed - so the shop can see
    /// the ceiling it will be held to rather than discover it in a refusal.
    /// </summary>
    private async Task ShowOwedCustomerAsync()
    {
        if (PayCustomer is null)
        {
            CustomerOwedText = "—";
            Payouts.Clear();
            ShowPayouts = false;
            return;
        }

        CustomerOwedText = PayCustomer.Owed > 0.009m
            ? Money.Pkr(PayCustomer.Owed)
            : Money.Pkr(0);
        Payouts.Clear();
        foreach (var p in await _ledger.ListPayoutsAsync(PayCustomer.CustomerId))
            Payouts.Add(p);
        ShowPayouts = Payouts.Count > 0;
    }

    [RelayCommand]
    private void Print()
    {
        var owed = Rows.Select(r => new[] { r.ContainerTitle, r.SupplierName, r.OwedText })
            .Cast<IReadOnlyList<string>>().ToList();
        var paid = Payments.Select(p => new[] { p.DateText, p.Method, p.AmountText, p.NoteText })
            .Cast<IReadOnlyList<string>>().ToList();
        var back = Customers.Select(c => new[] { c.Name, c.OwedText, c.PaidOutText })
            .Cast<IReadOnlyList<string>>().ToList();
        var given = Payouts.Select(x => new[] { x.DateText, x.Method, x.AmountText, x.NoteText })
            .Cast<IReadOnlyList<string>>().ToList();
        _print.PrintTables("we-owe.html", "We owe", null, new[]
        {
            new PrintTable("Suppliers", new[] { "Container", "Supplier", "Owed" }, owed,
                new[] { "Total", "", TotalOwed }, 2),
            new PrintTable("Paid to suppliers", new[] { "Date", "How", "Amount", "Note" }, paid, null, 2),
            new PrintTable("Owed to customers", new[] { "Customer", "We owe them", "Paid out so far" }, back, null),
            new PrintTable("Handed to customers", new[] { "Date", "How", "Amount", "Note" }, given, null, 2),
        });
        _shell.Notify("Printed from the page you were on.");
    }

    [RelayCommand]
    private async Task PaySupplierAsync()
    {
        if (PayContainer is null)
        {
            _shell.Notify("Pick a supplier.", true);
            return;
        }
        try
        {
            await _inventory.PaySupplierAsync(
                PayContainer.Id,
                PayDate?.DateTime ?? DateTime.Today,
                PayAmount ?? 0,
                PayMethod,
                PayNotes);
            _shell.Notify("Supplier payment taken off cash.");
            PayAmount = null;
            PayNotes = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task PayCustomerAsync()
    {
        if (PayCustomer is null)
        {
            _shell.Notify("Pick a customer.", true);
            return;
        }
        try
        {
            await _ledger.PayCustomerAsync(
                PayCustomer.CustomerId,
                PayCustomerDate?.DateTime ?? DateTime.Today,
                PayCustomerAmount ?? 0,
                PayCustomerMethod,
                PayCustomerNotes);
            _shell.Notify("Paid to the customer. Their ledger and the cash in the Main ledger both moved "
                          + "by that figure.");
            PayCustomerAmount = null;
            PayCustomerNotes = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }
}

public class WeOweRow
{
    public int ContainerId { get; set; }
    public string SupplierName { get; set; } = "";
    public string ContainerTitle { get; set; } = "";
    public decimal Owed { get; set; }
    public string OwedText => Owed < -0.009m
        ? CashBookService.PaidExtraText(-Owed, terse: true)
        : Money.Pkr(Owed);
}

public class PayContainerOption
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public decimal Owed { get; set; }
    public override string ToString() => Label;
}
