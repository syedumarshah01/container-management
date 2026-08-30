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

        var rows = await _ledger.GetLedgerAsync(_id);
        Ledger.Clear();
        foreach (var r in rows)
            Ledger.Add(r);

        var bal = await _ledger.GetBalanceAsync(_id);
        BalanceText = Money.Pkr(bal);
        BalanceHint = bal > 0 ? "They owe you — money in the market" : bal < 0 ? "Advance / you owe them" : "Settled";
        BilledText = Money.Pkr(rows.Sum(l => l.Debit));
        ReceivedText = Money.Pkr(rows.Where(l => l.Type != LedgerType.Return).Sum(l => l.Credit));
        OpeningAmount = rows.Where(r => r.Type == LedgerType.Opening).Sum(r => r.Debit - r.Credit);

        Unpaid.Clear();
        Unpaid.Add(new UnpaidInvoice { SaleId = 0, Label = "Not against a specific invoice", Remaining = 0 });
        foreach (var u in await _sales.UnpaidInvoicesAsync(_id))
            Unpaid.Add(u);
        SelectedUnpaid = Unpaid[0];

        Payments.Clear();
        foreach (var p in await _ledger.ListPaymentsAsync(_id))
            Payments.Add(p);

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
            await _ledger.ReceivePaymentAsync(_id, PayDate?.DateTime ?? DateTime.Today, PayAmount ?? 0, PayMethod, PayNotes, saleId);
            _shell.Notify("Payment recorded. Ledger updated.");
            PayAmount = 0;
            PayNotes = "";
            await LoadAsync();
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
            _shell.Notify("Payment removed from the khata.");
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
    private async Task PrintKhataAsync()
    {
        var c = await _ledger.GetCustomerAsync(_id);
        if (c is null) return;
        var rows = await _ledger.GetLedgerAsync(_id);
        var bal = await _ledger.GetBalanceAsync(_id);
        _print.OpenHtml(_print.StatementHtml(c, rows, bal, ShopSettings.Load()), $"khata-{_id}.html");
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
                : $"Assalamualaikum {c.Name}, {shop}: your khata is settled. Thank you.";
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
