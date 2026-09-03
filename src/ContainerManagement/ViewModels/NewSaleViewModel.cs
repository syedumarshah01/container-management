using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class NewSaleViewModel : ViewModelBase
{
    private readonly InventoryService _inventory;
    private readonly SalesService _sales;
    private readonly LedgerService _ledger;
    private readonly IAppShell _shell;
    private List<StockOption> _stock = new();

    public NewSaleViewModel(InventoryService inventory, SalesService sales, LedgerService ledger, IAppShell shell)
    {
        _inventory = inventory;
        _sales = sales;
        _ledger = ledger;
        _shell = shell;
        SearchGoods = PopulateSearchAsync;
    }

    public Func<string?, CancellationToken, Task<IEnumerable<object>>> SearchGoods { get; }

    public override bool ReloadOnShow => true;

    public int? EditingSaleId { get; set; }

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<NewSaleLineInput> Lines { get; } = new();
    public IReadOnlyList<string> Methods { get; } = PaymentMethods.All;

    [ObservableProperty] private Customer? selectedCustomer;
    [ObservableProperty] private DateTimeOffset? saleDate = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? dueDate;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private StockOption? selectedStock;
    [ObservableProperty] private string stockSearch = "";
    [ObservableProperty] private string selectedStockContainer = "—";
    [ObservableProperty] private string selectedStockQty = "—";
    [ObservableProperty] private string selectedStockCost = "—";
    [ObservableProperty] private decimal? pickQty = 1;
    [ObservableProperty] private decimal? pickPrice;
    [ObservableProperty] private decimal? paidNow;
    [ObservableProperty] private decimal? discount;
    [ObservableProperty] private string method = "Cash";
    [ObservableProperty] private string billTotal = Money.Pkr(0);
    [ObservableProperty] private string ledgerHint = "";
    [ObservableProperty] private string heading = "Sell";
    [ObservableProperty] private NewSaleLineInput? selectedLine;
    [ObservableProperty] private bool fullCashDefault;
    [ObservableProperty] private string qtyError = "";
    public bool HasQtyError => !string.IsNullOrEmpty(QtyError);

    public override async Task LoadAsync()
    {
        var keepCustomerId = SelectedCustomer?.Id;
        var alreadyOpen = HasLoaded;
        _stock = await _inventory.GetSellableStockAsync();
        await RefreshCustomersAsync(keepCustomerId);

        if (EditingSaleId is int sid && !alreadyOpen)
        {
            var sale = await _sales.GetSaleAsync(sid)
                ?? throw new InvalidOperationException("Sale not found.");
            Heading = $"Edit sale #{sale.Id}";
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == sale.CustomerId);
            SaleDate = new DateTimeOffset(sale.Date);
            DueDate = sale.DueDate is DateTime d ? new DateTimeOffset(d) : null;
            Notes = sale.Notes ?? "";
            Discount = sale.DiscountAmount;
            PaidNow = sale.PaidNow;
            Lines.Clear();
            foreach (var l in sale.Lines)
            {
                Lines.Add(new NewSaleLineInput
                {
                    ContainerId = l.ContainerId,
                    ContainerItemId = l.ContainerItemId,
                    ProductId = l.ProductId,
                    ProductName = l.Product.Name,
                    ContainerTitle = l.Container.Title,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    UnitCost = l.UnitCost,
                    Remaining = l.Quantity
                });
            }
        }
        else if (EditingSaleId is null && !alreadyOpen)
        {
            Heading = "Sell";
            SelectedCustomer = Customers.FirstOrDefault(c => c.IsWalkIn) ?? Customers.FirstOrDefault();
        }

        Recalc();
    }

    private async Task RefreshCustomersAsync(int? keepId)
    {
        var customers = await _ledger.ListCustomersAsync();
        Customers.Clear();
        foreach (var c in customers)
            Customers.Add(c);
        SelectedCustomer = keepId is int id
            ? Customers.FirstOrDefault(c => c.Id == id)
            : SelectedCustomer;
    }

    partial void OnSelectedStockChanged(StockOption? value)
    {
        if (value is null)
        {
            SelectedStockContainer = "—";
            SelectedStockQty = "—";
            SelectedStockCost = "—";
            QtyError = "";
            return;
        }

        SelectedStockContainer = value.ContainerTitle;
        SelectedStockQty = Money.Qty(value.Remaining) + " " + value.Unit;
        SelectedStockCost = Money.Pkr(value.UnitCost);
        PickQty = 1;
        PickPrice = value.LastSalePrice is > 0
            ? value.LastSalePrice
            : Math.Round(value.SellCost * 1.5m, 0);
        QtyError = "";
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (EditingSaleId is not null || value is null) return;
        FullCashDefault = value.IsWalkIn;
        Recalc();
    }

    [RelayCommand]
    private void AddLine()
    {
        if (SelectedStock is null)
        {
            QtyError = "Search and pick an item.";
            return;
        }
        var qty = PickQty ?? 0;
        if (qty <= 0)
        {
            QtyError = "Enter a quantity greater than zero.";
            return;
        }
        if (qty > SelectedStock.Remaining)
        {
            QtyError = "Only " + Money.Qty(SelectedStock.Remaining) + " in stock.";
            return;
        }

        var already = Lines.Where(l => l.ContainerItemId == SelectedStock.ContainerItemId).Sum(l => l.Quantity);
        if (already + qty > SelectedStock.Remaining)
        {
            QtyError = "Only " + Money.Qty(SelectedStock.Remaining - already) + " left after this bill.";
            return;
        }

        QtyError = "";
        Lines.Add(new NewSaleLineInput
        {
            ContainerId = SelectedStock.ContainerId,
            ContainerItemId = SelectedStock.ContainerItemId,
            ProductId = SelectedStock.ProductId,
            ProductName = SelectedStock.ProductName,
            ContainerTitle = SelectedStock.ContainerTitle,
            Unit = SelectedStock.Unit,
            Quantity = qty,
            UnitPrice = PickPrice ?? 0,
            UnitCost = SelectedStock.SellCost,
            Remaining = SelectedStock.Remaining
        });
        SelectedStock = null;
        StockSearch = "";
        PickQty = 1;
        PickPrice = 0;
        QtyError = "";
        if (FullCashDefault && EditingSaleId is null)
            PaidNow = BillNet();
        Recalc();
    }

    [RelayCommand]
    private void RemoveLine()
    {
        if (SelectedLine is null)
        {
            if (Lines.Count > 0)
                Lines.RemoveAt(Lines.Count - 1);
        }
        else
            Lines.Remove(SelectedLine);
        if (FullCashDefault && EditingSaleId is null)
            PaidNow = BillNet();
        Recalc();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            if (SelectedCustomer is null)
                throw new InvalidOperationException("Select a customer.");

            var due = DueDate?.DateTime;
            if ((PaidNow ?? 0) < BillNet() && due is null)
            {
                var days = Math.Max(0, ShopSettings.Load().DefaultDueDays);
                due = (SaleDate?.DateTime ?? DateTime.Today).Date.AddDays(days);
            }

            Sale sale;
            if (EditingSaleId is int sid)
            {
                sale = await _sales.UpdateSaleAsync(
                    sid, SelectedCustomer.Id, SaleDate?.DateTime ?? DateTime.Today,
                    Lines.ToList(), PaidNow ?? 0, Method, Notes, Discount ?? 0, due);
                _shell.Notify($"Sale #{sale.Id} updated.");
            }
            else
            {
                sale = await _sales.CreateSaleAsync(
                    SelectedCustomer.Id, SaleDate?.DateTime ?? DateTime.Today,
                    Lines.ToList(), PaidNow ?? 0, Method, Notes, Discount ?? 0, due);
                _shell.Notify($"Sale #{sale.Id} saved. Ledger updated.");
                await ResetDraftAsync();
            }
            _shell.OpenSale(sale.Id);
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    private async Task<IEnumerable<object>> PopulateSearchAsync(string? search, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        if (string.IsNullOrWhiteSpace(search))
            return Array.Empty<object>();

        return _stock
            .Where(s =>
                s.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (s.Sku ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .Cast<object>()
            .ToList();
    }

    private async Task ResetDraftAsync()
    {
        EditingSaleId = null;
        Heading = "Sell";
        Lines.Clear();
        Notes = "";
        Discount = null;
        PaidNow = null;
        DueDate = null;
        SelectedStock = null;
        StockSearch = "";
        PickQty = 1;
        PickPrice = null;
        _stock = await _inventory.GetSellableStockAsync();
        Recalc();
    }

    private decimal BillNet()
    {
        var gross = Lines.Sum(l => l.LineTotal);
        return Math.Max(0, gross - (Discount ?? 0));
    }

    private void Recalc()
    {
        var net = BillNet();
        BillTotal = Money.Pkr(net);
        var paid = PaidNow ?? 0;
        LedgerHint = $"Received now {Money.Pkr(paid)} · going to ledger {Money.Pkr(Math.Max(0, net - paid))}";
    }

    partial void OnPickQtyChanged(decimal? value) => CheckQty();
    partial void OnPaidNowChanged(decimal? value) => Recalc();
    partial void OnDiscountChanged(decimal? value) => Recalc();
    partial void OnQtyErrorChanged(string value) => OnPropertyChanged(nameof(HasQtyError));

    private void CheckQty()
    {
        if (SelectedStock is null)
            return;
        var qty = PickQty ?? 0;
        if (qty <= 0)
            return;
        if (qty > SelectedStock.Remaining)
        {
            QtyError = "Only " + Money.Qty(SelectedStock.Remaining) + " in stock.";
            return;
        }
        var already = Lines.Where(l => l.ContainerItemId == SelectedStock.ContainerItemId).Sum(l => l.Quantity);
        if (already + qty > SelectedStock.Remaining)
        {
            QtyError = "Only " + Money.Qty(SelectedStock.Remaining - already) + " left after this bill.";
            return;
        }
        QtyError = "";
    }
}
