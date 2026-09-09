using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class SaleDetailViewModel : ViewModelBase
{
    private readonly SalesService _sales;
    private readonly LedgerService _ledger;
    private readonly PrintService _print;
    private readonly AccessService _access;
    private readonly IAppShell _shell;
    private readonly int _id;
    private Sale? _sale;

    public SaleDetailViewModel(int id, SalesService sales, LedgerService ledger, PrintService print, AccessService access, IAppShell shell)
    {
        _id = id;
        _sales = sales;
        _ledger = ledger;
        _print = print;
        _access = access;
        _shell = shell;
    }

    [ObservableProperty] private string heading = "Sale";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string billTotal = "—";
    [ObservableProperty] private string received = "—";
    [ObservableProperty] private string onLedger = "—";
    [ObservableProperty] private string returnedText = "—";
    [ObservableProperty] private string discountText = "—";
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool canCancel;
    [ObservableProperty] private bool canReturn;
    [ObservableProperty] private bool isCancelled;

    /// <summary>
    /// Whether this return is being paid back in cash. It comes on by default when the bill has nothing
    /// left on the ledger - a returned item on a settled bill is money going out of the till, not a debt
    /// being reduced - and off while they still owe, where the return is absorbed as relief. Untick it to
    /// hold the money as their credit instead; the amount is never posted as cash in that case.
    /// </summary>
    [ObservableProperty] private bool refundInCash;

    public ObservableCollection<SaleLineRow> Lines { get; } = new();

    public override async Task LoadAsync()
    {
        _sale = await _sales.GetSaleAsync(_id);
        if (_sale is null)
        {
            _shell.Notify("Sale not found.", true);
            return;
        }

        var remaining = await _sales.RemainingOnInvoiceAsync(_id);
        var returnedAmount = _sale.Returns.Sum(r => r.Amount);
        Heading = $"Sale #{_sale.Id}";
        Subtitle = $"{_sale.Date:dd MMM yyyy} · {_sale.Customer.Name}" +
                   (_sale.DueDate is DateTime d ? $" · due {d:dd MMM yyyy}" : "") +
                   (string.IsNullOrWhiteSpace(_sale.Notes) ? "" : " · " + _sale.Notes);
        BillTotal = Money.Pkr(_sale.TotalAmount);
        DiscountText = _sale.DiscountAmount > 0 ? Money.Pkr(_sale.DiscountAmount) : "—";
        Received = Money.Pkr(_sale.PaidNow);
        ReturnedText = returnedAmount > 0 ? Money.Pkr(returnedAmount) : "—";
        OnLedger = Money.Pkr(remaining);
        IsCancelled = _sale.Status == SaleStatus.Cancelled;
        CanEdit = !IsCancelled && _sale.Date.Date == DateTime.Today && _sale.Returns.Count == 0;
        CanCancel = !IsCancelled && _sale.Returns.Count == 0 && (_access.IsOwner || _sale.Date.Date == DateTime.Today);

        Lines.Clear();
        foreach (var l in _sale.Lines)
        {
            var already = _sale.Returns.SelectMany(r => r.Lines).Where(x => x.SaleLineId == l.Id).Sum(x => x.Quantity);
            var max = Math.Max(0, l.Quantity - already);
            Lines.Add(new SaleLineRow
            {
                SaleLineId = l.Id,
                ContainerTitle = l.Container.Title,
                ProductName = l.Product.Name,
                QtyText = Money.Qty(l.Quantity),
                ReturnedQtyText = already > 0 ? Money.Qty(already) : "—",
                PriceText = Money.Pkr(l.UnitPrice),
                CostText = Money.Pkr(l.UnitCost),
                AmountText = Money.Pkr(l.LineTotal),
                ProfitText = Money.Pkr(l.LineProfit),
                MaxReturn = max,
                ReturnQty = null
            });
        }
        CanReturn = !IsCancelled && Lines.Any(l => l.CanReturnLine);
        // Anything received for this bill is money that can go back, so the tick comes on whenever
        // something was paid - not only when the bill was fully settled. A credit bill the customer has
        // not paid a rupee of stays off: there the return is relief from a debt, not cash.
        var stillOwed = await _sales.RemainingOnInvoiceAsync(_id);
        var received = Math.Max(0, _sale.TotalAmount - _sale.Returns.Sum(r => r.Amount) - stillOwed);
        RefundInCash = !IsCancelled && received > 0.009m;
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (_sale is null) return;
        var invoiceBalance = _sale.Status == SaleStatus.Cancelled
            ? 0
            : await _sales.RemainingOnInvoiceAsync(_id);
        var totalDue = await _ledger.GetBalanceAsync(_sale.CustomerId);
        var previous = totalDue - invoiceBalance;
        _print.OpenHtml(
            _print.InvoiceHtml(_sale, ShopSettings.Load(), previous, invoiceBalance, totalDue),
            $"invoice-{_sale.Id}.html");
    }

    [RelayCommand]
    private void Edit()
    {
        if (CanEdit)
            _shell.EditSale(_id);
    }

    [RelayCommand]
    private async Task ReturnItemsAsync()
    {
        try
        {
            var inputs = Lines
                .Where(l => (l.ReturnQty ?? 0) > 0)
                .Select(l => new SaleReturnInput { SaleLineId = l.SaleLineId, Quantity = l.ReturnQty ?? 0 })
                .ToList();
            var back = await _sales.ReturnItemsAsync(_id, inputs, RefundInCash);
            _shell.Notify(back > 0
                ? "Returned to the same container. " + Money.Pkr(back) + " came off their ledger and went back out of the till."
                : "Returned to the same container. Amount taken off their ledger.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        try
        {
            if (!_access.IsOwner && !CanCancel)
                _access.RequireOwner("cancel this sale");
            await _sales.CancelSaleAsync(_id);
            _shell.Notify("Sale cancelled. Stock returned to the same container.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand] private void Back() => _shell.Back();
}

public partial class SaleLineRow : ObservableObject
{
    public int SaleLineId { get; set; }
    public string ContainerTitle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string QtyText { get; set; } = "";
    public string ReturnedQtyText { get; set; } = "";
    public string PriceText { get; set; } = "";
    public string CostText { get; set; } = "";
    public string AmountText { get; set; } = "";
    public string ProfitText { get; set; } = "";
    public decimal MaxReturn { get; set; }
    public string LeftText => Money.Qty(MaxReturn);
    public bool CanReturnLine => MaxReturn > 0.0005m;

    [ObservableProperty] private decimal? returnQty;
}
