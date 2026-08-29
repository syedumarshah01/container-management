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
    private readonly PrintService _print;
    private readonly AccessService _access;
    private readonly IAppShell _shell;
    private readonly int _id;
    private Sale? _sale;

    public SaleDetailViewModel(int id, SalesService sales, PrintService print, AccessService access, IAppShell shell)
    {
        _id = id;
        _sales = sales;
        _print = print;
        _access = access;
        _shell = shell;
    }

    [ObservableProperty] private string heading = "Sale";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string billTotal = "—";
    [ObservableProperty] private string received = "—";
    [ObservableProperty] private string onLedger = "—";
    [ObservableProperty] private string discountText = "—";
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool canCancel;
    [ObservableProperty] private bool isCancelled;

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
        Heading = $"Sale #{_sale.Id}";
        Subtitle = $"{_sale.Date:dd MMM yyyy} · {_sale.Customer.Name}" +
                   (_sale.DueDate is DateTime d ? $" · due {d:dd MMM yyyy}" : "") +
                   (string.IsNullOrWhiteSpace(_sale.Notes) ? "" : " · " + _sale.Notes);
        BillTotal = Money.Pkr(_sale.TotalAmount);
        DiscountText = _sale.DiscountAmount > 0 ? Money.Pkr(_sale.DiscountAmount) : "—";
        Received = Money.Pkr(_sale.PaidNow);
        OnLedger = Money.Pkr(remaining);
        IsCancelled = _sale.Status == SaleStatus.Cancelled;
        CanEdit = !IsCancelled && _sale.Date.Date == DateTime.Today;
        CanCancel = !IsCancelled && (_access.IsOwner || _sale.Date.Date == DateTime.Today);

        Lines.Clear();
        foreach (var l in _sale.Lines)
        {
            Lines.Add(new SaleLineRow
            {
                ContainerTitle = l.Container.Title,
                ProductName = l.Product.Name,
                QtyText = Money.Qty(l.Quantity),
                PriceText = Money.Pkr(l.UnitPrice),
                CostText = Money.Pkr(l.UnitCost),
                AmountText = Money.Pkr(l.LineTotal),
                ProfitText = Money.Pkr(l.LineProfit)
            });
        }
    }

    [RelayCommand]
    private void Print()
    {
        if (_sale is null) return;
        _print.OpenHtml(_print.InvoiceHtml(_sale, ShopSettings.Load()), $"invoice-{_sale.Id}.html");
    }

    [RelayCommand]
    private void Edit()
    {
        if (CanEdit)
            _shell.EditSale(_id);
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

    [RelayCommand] private void Back() => _shell.GoSales();
}

public class SaleLineRow
{
    public string ContainerTitle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string QtyText { get; set; } = "";
    public string PriceText { get; set; } = "";
    public string CostText { get; set; } = "";
    public string AmountText { get; set; } = "";
    public string ProfitText { get; set; } = "";
}
