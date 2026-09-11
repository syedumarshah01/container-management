using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class CustomerDetailViewModel : ViewModelBase
{
    private readonly LedgerService _ledger;
    private readonly SalesService _sales;
    private readonly PrintService _print;
    private readonly AccessService _access;
    private readonly IAppShell _shell;
    private readonly int _id;

    public CustomerDetailViewModel(int id, LedgerService ledger, SalesService sales, PrintService print, AccessService access, IAppShell shell)
    {
        _id = id;
        _ledger = ledger;
        _sales = sales;
        _print = print;
        _access = access;
        _shell = shell;
    }

    [ObservableProperty] private string heading = "Customer";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string balanceText = "—";
    [ObservableProperty] private string balanceHint = "";
    [ObservableProperty] private string billedText = "—";
    [ObservableProperty] private string receivedText = "—";

    [ObservableProperty] private string editName = "";
    [ObservableProperty] private string editPhone = "";
    [ObservableProperty] private string editAddress = "";
    [ObservableProperty] private string editNotes = "";

    [ObservableProperty] private decimal? payAmount;
    [ObservableProperty] private DateTimeOffset? payDate = DateTimeOffset.Now;
    [ObservableProperty] private string payMethod = "Cash";
    [ObservableProperty] private string payNotes = "";
    [ObservableProperty] private UnpaidInvoice? selectedUnpaid;
    [ObservableProperty] private decimal? openingAmount;
    [ObservableProperty] private Payment? selectedPayment;
    [ObservableProperty] private bool isOwner;

    public IReadOnlyList<string> Methods { get; } = PaymentMethods.All;
    public ObservableCollection<LedgerRow> Ledger { get; } = new();
    public ObservableCollection<SaleListRow> Invoices { get; } = new();
    public ObservableCollection<UnpaidInvoice> Unpaid { get; } = new();
    public ObservableCollection<Payment> Payments { get; } = new();
    [ObservableProperty] private SaleListRow? selectedInvoice;

    /// <summary>The months a customer's receipts can be read for, and the figure for the one that is
    /// chosen. "Received" is the same word the year statement uses for the same measure - receipts dated in
    /// the month - so the two pages can be checked against each other. It is not the "Collected so far"
    /// figure at the top of this page, which is their whole ledger and counts the opening balance an advance
    /// left in the book before this software was keeping it.</summary>
    public ObservableCollection<CustomerMonth> Months { get; } = new();
    [ObservableProperty] private CustomerMonth? selectedMonth;
    [ObservableProperty] private string monthReceivedText = "—";

    // The month box drives the list under it, so picking a month has to reload; a reload must not do it
    // again while the page is being built, which is what this flag is for.
    private bool _buildingMonths;

    // And a click on the box can land while the page is still loading, so two reads are in the air: the
    // older one may answer last and put March's lines under a box reading April, which is worse than a
    // slow list. A stale read is thrown away instead of shown.
    private int _monthRead;

    partial void OnSelectedMonthChanged(CustomerMonth? value)
    {
        if (!_buildingMonths) _ = LoadMonthAsync();
    }

    private async Task LoadMonthAsync()
    {
        var read = ++_monthRead;
        var m = SelectedMonth ?? CustomerMonth.All;
        var (amount, _, rows) = await _ledger.GetReceiptsAsync(_id, m.Year, m.Month);
        if (read != _monthRead)
            return;
        Payments.Clear();
        foreach (var p in rows)
            Payments.Add(p);
        MonthReceivedText = Money.Pkr(amount);
    }

    public override async Task LoadAsync()
    {
        IsOwner = _access.IsOwner;
        var c = await _ledger.GetCustomerAsync(_id);
        if (c is null)
        {
            _shell.Notify("Customer not found.", true);
            return;
        }

        Heading = c.Name;
        Subtitle = $"{c.Phone ?? "No phone"}" +
                   (string.IsNullOrWhiteSpace(c.Address) ? "" : " · " + c.Address) +
                   (string.IsNullOrWhiteSpace(c.Notes) ? "" : " · " + c.Notes);

        EditName = c.Name;
        EditPhone = c.Phone ?? "";
        EditAddress = c.Address ?? "";
        EditNotes = c.Notes ?? "";

        // Newest entry on top, with each line still carrying the balance the book had reached at that
        // moment - the figures are worked out in time order first, so the column reads correctly from the
        // foot of the page upward. The printout underneath uses the same rows in time order, which is how
        // a statement is read on paper.
        var rows = (await _ledger.GetLedgerAsync(_id)).AsEnumerable().Reverse().ToList();
        Ledger.Clear();
        foreach (var r in rows)
            Ledger.Add(r);

        var bal = await _ledger.GetBalanceAsync(_id);
        BalanceText = Money.Pkr(bal);
        BalanceHint = bal > 0 ? "They owe you — money in the market" : bal < 0 ? "Advance / you owe them" : "Settled";
        // What we have charged them, less what came back. The lines that handed money over - a payout, or
        // the cash half of a return - are not billing, and counting their debits here made a settled
        // customer read as a larger account than the bills they were given.
        BilledText = Money.Pkr(rows.Where(l => !l.IsPaidOut).Sum(l => l.Debit)
                               - rows.Where(l => l.Type == LedgerType.Return).Sum(l => l.Credit));
        ReceivedText = Money.Pkr(rows.Where(l => l.Type != LedgerType.Return).Sum(l => l.Credit));
        OpeningAmount = rows.Where(r => r.Type == LedgerType.Opening).Sum(r => r.Debit - r.Credit);

        Unpaid.Clear();
        Unpaid.Add(new UnpaidInvoice { SaleId = 0, Label = "Not against a specific invoice", Remaining = 0 });
        foreach (var u in await _sales.UnpaidInvoicesAsync(_id))
            Unpaid.Add(u);
        SelectedUnpaid = Unpaid[0];

        // The months on offer are the months this customer's receipts span, with the current month always
        // among them. A month where nothing came in is the fact someone most wants to confirm, so it is not
        // left out; months nobody has traded in yet are, and the span follows the dates rather than the
        // clock, because a receipt can be dated ahead now that the date is set by hand.
        var every = await _ledger.GetReceiptsAsync(_id, null, null);
        var seen = every.Rows.Select(p => (p.Date.Year, p.Date.Month)).ToHashSet();
        seen.Add((DateTime.Today.Year, DateTime.Today.Month));
        var from = new DateTime(seen.Min().Item1, seen.Min().Item2, 1);
        var to = new DateTime(seen.Max().Item1, seen.Max().Item2, 1);
        var wanted = new List<CustomerMonth> { CustomerMonth.All };
        for (var d = from; d <= to; d = d.AddMonths(1))
            wanted.Add(CustomerMonth.Of(d.Year, d.Month));

        // The list is rebuilt only when the span it covers has actually moved. A list cleared and refilled
        // under the reader's hand can hand the box a cleared selection on the way through, and a figure that
        // jumps back to every month by itself is a figure nobody asked to change.
        var same = Months.Count == wanted.Count;
        if (same)
        {
            for (var i = 0; i < wanted.Count; i++)
                if (Months[i].Label != wanted[i].Label) { same = false; break; }
        }
        if (!same)
        {
            _buildingMonths = true;
            try
            {
                var keep = SelectedMonth;
                Months.Clear();
                foreach (var m in wanted)
                    Months.Add(m);
                SelectedMonth = keep is null
                    ? CustomerMonth.All
                    : Months.FirstOrDefault(o => o.Year == keep.Year && o.Month == keep.Month) ?? CustomerMonth.All;
            }
            finally { _buildingMonths = false; }
        }
        await LoadMonthAsync();

        var sales = await _sales.ListSalesAsync();
        Invoices.Clear();
        foreach (var s in sales.Where(x => x.CustomerId == _id))
        {
            var left = s.Status == SaleStatus.Cancelled ? 0 : await _sales.RemainingOnInvoiceAsync(s.Id);
            Invoices.Add(new SaleListRow
            {
                Id = s.Id,
                DateText = s.Date.ToString("dd MMM yyyy"),
                CustomerName = s.Customer.Name,
                CustomerId = s.CustomerId,
                Containers = string.Join(", ", s.Lines.Select(l => l.Container.Title).Distinct()),
                TotalText = Money.Pkr(s.TotalAmount),
                CreditText = s.Status == SaleStatus.Cancelled
                    ? "Cancelled"
                    : left > 0 ? Money.Pkr(left) : "Settled"
            });
        }
    }

    [RelayCommand]
    private async Task ReceiveAsync()
    {
        try
        {
            int? saleId = SelectedUnpaid is { SaleId: > 0 } u ? u.SaleId : null;
            var when = PayDate?.DateTime ?? DateTime.Today;
            await _ledger.ReceivePaymentAsync(_id, when, PayAmount ?? 0, PayMethod, PayNotes, saleId);
            _shell.Notify("Payment recorded. Ledger updated.");
            PayAmount = 0;
            PayNotes = "";
            await LoadAsync();
            // A receipt dated into another month than the box is showing disappears from the list the
            // reader is looking at, which looks exactly like a save that failed. The box follows the date
            // that was actually written.
            var landed = Months.FirstOrDefault(o => o.Year == when.Year && o.Month == when.Month);
            if (landed is not null) SelectedMonth = landed;
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task DeletePaymentAsync()
    {
        if (SelectedPayment is null)
        {
            _shell.Notify("Select a payment in the list first.", true);
            return;
        }
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to delete a payment.", true); return; }
        try
        {
            await _ledger.DeletePaymentAsync(SelectedPayment.Id);
            _shell.Notify("Payment removed from the ledger.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SaveOpeningAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to set opening balance.", true); return; }
        try
        {
            await _ledger.SetOpeningBalanceAsync(_id, OpeningAmount ?? 0);
            _shell.Notify("Opening balance saved.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SaveMetaAsync()
    {
        try
        {
            await _ledger.UpdateCustomerAsync(_id, EditName, EditPhone, EditAddress, EditNotes);
            _shell.Notify("Customer updated.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task PrintLedgerAsync()
    {
        var c = await _ledger.GetCustomerAsync(_id);
        if (c is null) return;
        var rows = await _ledger.GetLedgerAsync(_id);
        var bal = await _ledger.GetBalanceAsync(_id);
        _print.OpenHtml(_print.StatementHtml(c, rows, bal, ShopSettings.Load()), $"ledger-{_id}.html");
    }

    [RelayCommand]
    private async Task WhatsAppAsync()
    {
        try
        {
            var c = await _ledger.GetCustomerAsync(_id)
                ?? throw new InvalidOperationException("Customer not found.");
            var bal = await _ledger.GetBalanceAsync(_id);
            var shop = ShopSettings.Load().CompanyName;
            var text = bal > 0
                ? $"Assalamualaikum {c.Name}, {shop}: your balance is {Money.Pkr(bal)}. Please send when convenient."
                : $"Assalamualaikum {c.Name}, {shop}: your ledger is settled. Thank you.";
            PrintService.WhatsApp(c.Phone, text);
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void OpenInvoice()
    {
        if (SelectedInvoice is not null)
            _shell.OpenSale(SelectedInvoice.Id);
    }

    [RelayCommand] private void NewSale() => _shell.GoNewSale();
    [RelayCommand] private void Back() => _shell.Back();
}
