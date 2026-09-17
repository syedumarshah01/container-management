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

    /// <summary>
    /// The lot to bill from. Choose it and the item search below offers only what came in it, so a bill is one
    /// container's goods and its money is that container's money figure for figure, shared with nothing.
    /// Picking an item sets it by itself, and "All containers" is there for a customer buying across two lots
    /// in one visit: every line keeps the container it came from either way, so a container's collected and
    /// outstanding figures do not depend on how a bill happened to be typed.
    /// </summary>
    public ObservableCollection<ContainerChoice> ContainerChoices { get; } = new();
    public ObservableCollection<NewSaleLineInput> Lines { get; } = new();
    public IReadOnlyList<string> Methods { get; } = PaymentMethods.All;

    [ObservableProperty] private Customer? selectedCustomer;
    [ObservableProperty] private DateTimeOffset? saleDate = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? dueDate;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private ContainerChoice? containerChoice;

    /// <summary>The lot this bill belongs to, and the box being shut at it. See RefreshContainerLock.</summary>
    [ObservableProperty] private bool isContainerLocked;
    private int _lockedContainerId;
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
    [ObservableProperty] private string qtyError = "";
    public bool HasQtyError => !string.IsNullOrEmpty(QtyError);

    public override async Task LoadAsync()
    {
        var keepCustomerId = SelectedCustomer?.Id;
        var alreadyOpen = HasLoaded;
        _stock = await _inventory.GetSellableStockAsync();
        RebuildContainerChoices();
        await RefreshCustomersAsync(keepCustomerId);

        if (EditingSaleId is int sid && !alreadyOpen)
        {
            var sale = await _sales.GetSaleAsync(sid)
                ?? throw new InvalidOperationException("Sale not found.");
            Heading = $"Edit sale #{sale.InvoiceNo}";
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

    partial void OnContainerChoiceChanged(ContainerChoice? value)
    {
        // An item already picked from another lot cannot be added under this choice, so it is put back.
        if (value is { ContainerId: > 0 } && SelectedStock is { } pick && pick.ContainerId != value.ContainerId)
            SelectedStock = null;
        // And while the bill belongs to one lot, the box is held there: a reload or a programmatic change
        // that moved it is undone, because the lines the bill carries are why it is where it is.
        if (IsContainerLocked && value?.ContainerId != _lockedContainerId)
        {
            var own = ContainerChoices.FirstOrDefault(c => c.ContainerId == _lockedContainerId);
            if (own is not null && !ReferenceEquals(own, value))
                ContainerChoice = own;
        }
    }

    partial void OnSelectedStockChanged(StockOption? value)
    {
        if (value is null)
        {
            SelectedStockQty = "—";
            SelectedStockCost = "—";
            QtyError = "";
            return;
        }

        if (IsContainerLocked && value.ContainerId != _lockedContainerId)
        {
            // The list can no longer offer another container's items, so a pick from one is a row left over
            // from a search typed before the box shut. It goes back, and the lines the bill already holds
            // are named as the reason - a click in a list does not move where a bill's goods came from.
            // SelectedStock is cleared first because that reset writes over the message below.
            SelectedStock = null;
            var title = Lines.FirstOrDefault(l => l.ContainerId == _lockedContainerId)?.ContainerTitle;
            if (string.IsNullOrWhiteSpace(title))
                title = "one container";
            QtyError = $"This bill is {title}'s - remove its lines to sell from another container.";
            return;
        }

        // Picking an item decides which lot this bill is against, and the box above shows and holds it, so
        // the next pick comes from the same container without anyone having to remember to filter.
        if (ContainerChoice?.ContainerId != value.ContainerId)
            ContainerChoice = ContainerChoices.FirstOrDefault(c => c.ContainerId == value.ContainerId)
                              ?? ContainerChoice.All;
        SelectedStockQty = Money.Qty(value.Remaining) + " " + value.Unit;
        // The landed cost, not the goods price alone: it is the figure this line will be costed at, and a
        // picker that shows a smaller one teaches a shop to under-price a piece.
        SelectedStockCost = Money.Pkr(value.SellCost);
        PickQty = 1;
        PickPrice = value.LastSalePrice is > 0
            ? value.LastSalePrice
            : Money.Round(value.SellCost * 1.5m, 0);
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
            QtyError = "Only " + Money.Qty3(SelectedStock.Remaining) + " in stock.";
            return;
        }

        var already = Lines.Where(l => l.ContainerItemId == SelectedStock.ContainerItemId).Sum(l => l.Quantity);
        if (already + qty > SelectedStock.Remaining)
        {
            QtyError = "Only " + Money.Qty3(SelectedStock.Remaining - already) + " left after this bill.";
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
                _shell.Notify($"Sale #{sale.InvoiceNo} updated.");
            }
            else
            {
                sale = await _sales.CreateSaleAsync(
                    SelectedCustomer.Id, SaleDate?.DateTime ?? DateTime.Today,
                    Lines.ToList(), PaidNow ?? 0, Method, Notes, Discount ?? 0, due);
                _shell.Notify($"Sale #{sale.InvoiceNo} saved. Ledger updated.");
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

        // A bill that already has lines searches its own container and nothing else - the box being shut is
        // the same rule told to the picker, and the picker is where a wrong item could actually be clicked.
        var lotId = IsContainerLocked ? _lockedContainerId : ContainerChoice?.ContainerId ?? 0;
        var pool = lotId > 0 ? _stock.Where(s => s.ContainerId == lotId) : _stock;

        return pool
            .Where(s =>
                s.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (s.Sku ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .Cast<object>()
            .ToList();
    }

    /// <summary>The box beside the search, built from what is actually in stock and no longer than the lots
    /// that still have goods to sell. A choice made before is kept across a reload; a bill being edited takes
    /// its container from its own lines, when they all agree on one.</summary>
    private void RebuildContainerChoices()
    {
        var keep = ContainerChoice?.ContainerId ?? 0;
        ContainerChoices.Clear();
        ContainerChoices.Add(ContainerChoice.All);
        foreach (var lot in _stock
                     .GroupBy(o => o.ContainerId)
                     .Select(g => (Id: g.Key, Title: g.First().ContainerTitle))
                     .OrderBy(x => x.Title))
        {
            ContainerChoices.Add(new ContainerChoice { ContainerId = lot.Id, Title = lot.Title });
        }

        ContainerChoice = ContainerChoices.FirstOrDefault(c => c.ContainerId == keep) ?? ContainerChoices[0];
        RefreshContainerLock();
    }

    /// <summary>
    /// One bill, one container, from the moment its first line is on it: the box shows that lot and cannot be
    /// moved, the search under it offers only that container's items, and the printed invoice can name one
    /// container as where the goods went out from. Removing the last line of that container opens the box
    /// again, which is the only honest way to change a bill's container - the lines are what say where the
    /// goods came from, so neither half can be allowed to disagree with the other.
    ///
    /// A bill loaded for editing whose lines already span two containers is left at "All containers" and not
    /// locked. It is a fact from before this rule, and pinning it to either half would invent one; its
    /// invoice says nothing about a container, and each line keeps its own money where it belongs.
    /// </summary>
    private void RefreshContainerLock()
    {
        var lots = Lines.Select(l => l.ContainerId).Where(id => id > 0).Distinct().ToList();
        var one = Lines.Count > 0 && lots.Count == 1 ? lots[0] : 0;
        _lockedContainerId = one;
        IsContainerLocked = one > 0;
        if (one > 0 && ContainerChoice?.ContainerId != one)
            ContainerChoice = ContainerChoices.FirstOrDefault(c => c.ContainerId == one) ?? ContainerChoice;
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
        ContainerChoice = null;
        PickQty = 1;
        PickPrice = null;
        _stock = await _inventory.GetSellableStockAsync();
        RebuildContainerChoices();
        Recalc();
    }

    private decimal BillNet()
    {
        var gross = Lines.Sum(l => l.LineTotal);
        return Math.Max(0, gross - (Discount ?? 0));
    }

    private void Recalc()
    {
        RefreshContainerLock();
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
            QtyError = "Only " + Money.Qty3(SelectedStock.Remaining) + " in stock.";
            return;
        }
        var already = Lines.Where(l => l.ContainerItemId == SelectedStock.ContainerItemId).Sum(l => l.Quantity);
        if (already + qty > SelectedStock.Remaining)
        {
            QtyError = "Only " + Money.Qty3(SelectedStock.Remaining - already) + " left after this bill.";
            return;
        }
        QtyError = "";
    }
}
