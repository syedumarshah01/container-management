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

    /// <summary>
    /// The details box is shut by default - it is a form for once a year, not part of reading a customer - and
    /// it opens itself when a send fails for want of a number, because a complaint pointing at a closed panel
    /// is a dead end. It is never closed by the book: once it is open the shop is in it.
    /// </summary>
    [ObservableProperty] private bool showDetails;

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

        // The advance row leads the list and is never picked for you: money that lands against no bill because
        // the box happened to open on that line is money the shop decided nothing about, and it then belongs to
        // no container's money either. A pick made before a reload is kept only while that bill is still owed
        // on, so taking half of what was left leaves the same bill in the box and settling it leaves nothing.
        var keepInvoice = SelectedUnpaid is { SaleId: > 0 } picked ? picked.SaleId : 0;
        Unpaid.Clear();
        Unpaid.Add(new UnpaidInvoice { SaleId = 0, Label = "No bill - take it as an advance", Remaining = 0 });
        foreach (var u in await _sales.UnpaidInvoicesAsync(_id))
            Unpaid.Add(u);
        SelectedUnpaid = keepInvoice > 0 ? Unpaid.FirstOrDefault(u => u.SaleId == keepInvoice) : null;

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
        // The pick is required rather than defaulted, and being required is the whole point: an amount typed
        // with nothing chosen underneath it used to be booked as an advance by whoever last edited this list.
        // Taking money as an advance is still allowed, but it has to be the thing that was chosen.
        if (SelectedUnpaid is null)
        {
            _shell.Notify("Say what this money is against: pick the invoice it settles, or the line that takes "
                          + "it as an advance.", true);
            return;
        }
        try
        {
            // Read through the row's own word for it, so the meaning is in the code as well as in the label:
            // a bill number, or nothing to apply it to because this money is an advance.
            int? saleId = SelectedUnpaid.IsAdvance ? null : SelectedUnpaid.SaleId;
            var when = PayDate?.DateTime ?? DateTime.Today;
            await _ledger.ReceivePaymentAsync(_id, when, PayAmount ?? 0, PayMethod, PayNotes, saleId);
            _shell.Notify("Payment recorded. Ledger updated.");
            PayAmount = null;
            PayNotes = "";
            // Let go of the bill too: the next receipt is another decision, and a box left showing the last
            // one is how a receipt gets applied to a bill that had nothing left on it.
            SelectedUnpaid = null;
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

    /// <summary>
    /// Sends the ledger, not a note about it: the message is the customer's own lines and their balance, so a
    /// reply can quote a line from it. The launch happens off the page's thread, because handing a link to
    /// another program can sit there a moment, and a button that freezes the window reads as a broken button.
    /// </summary>
    [RelayCommand]
    private async Task WhatsAppAsync()
    {
        string? text = null;
        try
        {
            var c = await _ledger.GetCustomerAsync(_id)
                ?? throw new InvalidOperationException("Customer not found.");
            if (string.IsNullOrWhiteSpace(c.Phone))
                ShowDetails = true;
            var rows = await _ledger.GetLedgerAsync(_id);
            if (rows.Count == 0)
                throw new InvalidOperationException("This customer's ledger is empty - there is nothing to send.");
            var bal = await _ledger.GetBalanceAsync(_id);
            var shop = ShopSettings.Load();
            // The ledger, as words, in the chat: nothing is written to a folder, nothing is launched beyond the
            // link itself, and nothing is left for the shop to drag anywhere. The lines are the customer's own,
            // in the order the money moved, and the Print button next door is still the way to put the same
            // ledger on paper.
            var message = PrintService.ShareText(shop.CompanyName, c.Name, rows, bal,
                PrintService.ShareUrlBudget, shop.WhatsAppMessage);
            text = message;
            var dialed = await Task.Run(() => PrintService.WhatsApp(c.Phone, message));
            _shell.Notify($"Chat opened with {dialed}.");
        }
        catch (Exception ex)
        {
            // The message is worth more to the shop than the reason, so it goes to the clipboard: pasting it
            // into a chat is a send, while retyping a ledger is not.
            if (text is null)
                _shell.Notify(ex.Message, true);
            else
            {
                await _shell.CopyTextAsync(text);
                _shell.Notify(ex.Message + " The message has been copied to the clipboard - paste it into WhatsApp.", true);
            }
        }
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
