using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// A return is settled one of two ways, and both are on the card so the choice is in front of the
    /// shop before anything is written: the figure stays in the customer's ledger as credit, or it is paid
    /// out of the cashbook. The amounts on the two buttons come from the service's own arithmetic, run
    /// with writing switched off, so a button never promises a figure the book will not write.
    /// </summary>
    [ObservableProperty] private string returnLedgerText = "Adjust in their ledger";
    [ObservableProperty] private string returnCashText = "Pay from cashbook";
    [ObservableProperty] private bool canSettleReturn;

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
        foreach (var l in Lines)
            l.PropertyChanged += OnReturnQtyChanged;
        CanReturn = !IsCancelled && Lines.Any(l => l.CanReturnLine);
        _ = RefreshReturnPreviewAsync();
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
    private Task ReturnOnLedger() => SettleReturnAsync(inCash: false);

    [RelayCommand]
    private Task ReturnInCashbook() => SettleReturnAsync(inCash: true);

    private async Task SettleReturnAsync(bool inCash)
    {
        var inputs = ReturnInputs();
        if (inputs.Count == 0)
        {
            _shell.Notify("Type how many of each item came back.", true);
            return;
        }
        try
        {
            var back = await _sales.ReturnItemsAsync(_id, inputs, inCash);
            _shell.Notify(back > 0.009m
                ? "Returned to the same container. " + Money.Pkr(back) + " paid from the cashbook, and "
                  + "the rest of the amount sits in their ledger."
                : "Returned to the same container. The amount is adjusted in their ledger - nothing moved "
                  + "in the cashbook.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    /// <summary>
    /// The figures on the buttons, refreshed as the quantities are typed. The preview is the posting's own
    /// arithmetic with writing off, so what a button offers is what pressing it will do - including the
    /// rule that a return of everything left on a bill credits the whole remaining figure, which a second
    /// calculation beside the first would get wrong. Errors are swallowed here: a half-typed quantity is
    /// not a message to show, and the posting itself will say plainly what it refused.
    /// </summary>
    private async Task RefreshReturnPreviewAsync()
    {
        var inputs = ReturnInputs();
        if (inputs.Count == 0)
        {
            CanSettleReturn = false;
            ReturnLedgerText = "Adjust in their ledger";
            ReturnCashText = "Pay from cashbook";
            return;
        }
        try
        {
            var settle = await _sales.PreviewReturnAsync(_id, inputs);
            CanSettleReturn = true;
            ReturnLedgerText = "Adjust " + Money.Pkr(settle.Credit) + " in their ledger";
            ReturnCashText = settle.Cash > 0.009m
                ? "Pay " + Money.Pkr(settle.Cash) + " from cashbook"
                : "Pay from cashbook - Rs 0, nothing of theirs to give back";
        }
        catch
        {
            CanSettleReturn = false;
        }
    }

    private List<SaleReturnInput> ReturnInputs() => Lines
        .Where(l => (l.ReturnQty ?? 0) > 0)
        .Select(l => new SaleReturnInput { SaleLineId = l.SaleLineId, Quantity = l.ReturnQty ?? 0 })
        .ToList();

    private void OnReturnQtyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SaleLineRow.ReturnQty))
            _ = RefreshReturnPreviewAsync();
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
