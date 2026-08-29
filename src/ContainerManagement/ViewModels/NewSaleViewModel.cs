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
    private bool _syncingStock;

    public NewSaleViewModel(InventoryService inventory, SalesService sales, LedgerService ledger, IAppShell shell)
    {
        _inventory = inventory;
        _sales = sales;
        _ledger = ledger;
        _shell = shell;
        SearchGoods = PopulateSearchAsync;
    }

    public Func<string?, CancellationToken, Task<IEnumerable<object>>> SearchGoods { get; }

    public int? EditingSaleId { get; set; }

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<CargoContainer> Containers { get; } = new();
    public ObservableCollection<StockOption> StockInContainer { get; } = new();
    public ObservableCollection<NewSaleLineInput> Lines { get; } = new();
    public IReadOnlyList<string> Methods { get; } = PaymentMethods.All;

    [ObservableProperty] private Customer? selectedCustomer;
    [ObservableProperty] private DateTimeOffset? saleDate = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? dueDate;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private CargoContainer? selectedContainer;
    [ObservableProperty] private StockOption? selectedStock;
    [ObservableProperty] private string stockSearch = "";
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

    public override async Task LoadAsync()
    {
        var customers = await _ledger.ListCustomersAsync();
        Customers.Clear();
        foreach (var c in customers)
            Customers.Add(c);

        var containers = await _inventory.ContainersWithStockAsync();
        Containers.Clear();
        foreach (var c in containers)
            Containers.Add(c);

        _stock = await _inventory.GetSellableStockAsync();

        if (EditingSaleId is int sid)
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
        else
        {
            Heading = "Sell";
            SelectedCustomer = customers.FirstOrDefault(c => c.IsWalkIn) ?? customers.FirstOrDefault();
        }

        RefreshStock();
        Recalc();
    }

    partial void OnSelectedContainerChanged(CargoContainer? value)
    {
        if (_syncingStock)
        {
            RefreshStock();
            return;
        }
        SelectedStock = null;
        RefreshStock();
    }

    partial void OnSelectedStockChanged(StockOption? value)
    {
        if (value is null)
        {
            SelectedStockQty = "—";
            SelectedStockCost = "—";
            return;
        }

        if (SelectedContainer is null || SelectedContainer.Id != value.ContainerId)
        {
            _syncingStock = true;
            SelectedContainer = Containers.FirstOrDefault(c => c.Id == value.ContainerId);
            _syncingStock = false;
            RefreshStock();
        }

        SelectedStockQty = Money.Qty(value.Remaining) + " " + value.Unit;
        SelectedStockCost = Money.Pkr(value.UnitCost);
        PickQty = 1;
        PickPrice = value.LastSalePrice is > 0
            ? value.LastSalePrice
            : Math.Round(value.SellCost * 1.5m, 0);
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
        if (SelectedContainer is null)
        {
            _shell.Notify("Select which container these goods are being sold from.", true);
            return;
        }
        if (SelectedStock is null)
        {
            _shell.Notify("Select goods from that container.", true);
            return;
        }
        var qty = PickQty ?? 0;
        if (qty <= 0 || qty > SelectedStock.Remaining)
        {
            _shell.Notify($"Quantity must be between 0 and {Money.Qty(SelectedStock.Remaining)}.", true);
            return;
        }

        var already = Lines.Where(l => l.ContainerItemId == SelectedStock.ContainerItemId).Sum(l => l.Quantity);
        if (already + qty > SelectedStock.Remaining)
        {
            _shell.Notify("That would sell more than remaining in this container (including lines already on the bill).", true);
            return;
        }

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
        PickQty = 1;
        PickPrice = 0;
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

    private void RefreshStock()
    {
        StockInContainer.Clear();
        if (SelectedContainer is null) return;
        foreach (var s in _stock.Where(s => s.ContainerId == SelectedContainer.Id))
            StockInContainer.Add(s);
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

    partial void OnPaidNowChanged(decimal? value) => Recalc();
    partial void OnDiscountChanged(decimal? value) => Recalc();
}
