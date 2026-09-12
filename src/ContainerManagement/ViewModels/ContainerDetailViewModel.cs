using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ContainerDetailViewModel : ViewModelBase
{
    private readonly InventoryService _inventory;
    private readonly ReportService _reports;
    private readonly AccessService _access;
    private readonly IAppShell _shell;
    private readonly int _id;
    private bool _loadingSelection;

    public ContainerDetailViewModel(int id, InventoryService inventory, ReportService reports, AccessService access, IAppShell shell)
    {
        _id = id;
        _inventory = inventory;
        _reports = reports;
        _access = access;
        _shell = shell;
    }

    [ObservableProperty] private string title = "Container";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string stockValue = "—";
    [ObservableProperty] private string stockSold = "—";

    [ObservableProperty] private string goodsName = "";
    [ObservableProperty] private string goodsSku = "";
    [ObservableProperty] private string goodsUnit = "pcs";
    [ObservableProperty] private decimal? goodsQty = 1;
    [ObservableProperty] private decimal? goodsInStock;
    [ObservableProperty] private decimal? goodsCost;

    /// <summary>What one piece weighs, in kilograms - the figure on the carton, and the same one the order
    /// sheet asks for. The container's expenses are shared over what the lot weighs in all, so an item left
    /// blank holds up the whole sharing: the page says so rather than loading its freight onto the others.</summary>
    [ObservableProperty] private decimal? goodsWeight;
    [ObservableProperty] private ContainerItemRow? selectedItem;

    [ObservableProperty] private string expenseCategory = "Sea Freight";
    [ObservableProperty] private string expenseCurrency = ExpenseCurrencies.All[0];
    [ObservableProperty] private decimal? expenseAmount;
    [ObservableProperty] private DateTimeOffset? expenseDate = DateTimeOffset.Now;
    [ObservableProperty] private string expenseNotes = "";
    [ObservableProperty] private ContainerExpense? selectedExpense;

    [ObservableProperty] private decimal? editWeight;

    /// <summary>Yen to rupees, for this shipment. Expenses written in yen are converted at it and the rate
    /// is kept on the line, so changing it here never re-values money already paid to a clearing agent.</summary>
    [ObservableProperty] private decimal? editYenRate;
    [ObservableProperty] private string expenseTape = "";
    [ObservableProperty] private string expensePreview = "";
    [ObservableProperty] private bool showExpensePreview;
    private decimal _yenRateOnFile;
    [ObservableProperty] private string editSupplier = "";
    [ObservableProperty] private decimal? editSupplierAmount;

    /// <summary>
    /// The paid box. It is not a container field but the total of that supplier's payments, so saving
    /// a different figure here edits the payments - see InventoryService.UpdateImportDetailsAsync.
    /// </summary>
    [ObservableProperty] private decimal? editPaidSoFar;
    [ObservableProperty] private DateTimeOffset? editArrival;
    [ObservableProperty] private string editBillHint = "";
    [ObservableProperty] private bool showBillHint;

    [ObservableProperty] private bool isClosed;
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private bool showImportEditor;
    [ObservableProperty] private bool showItemForm;

    public override bool FillsPage => true;

    public ObservableCollection<ContainerItemRow> Items { get; } = new();
    public ObservableCollection<ContainerExpense> Expenses { get; } = new();
    public IReadOnlyList<string> UnitOptions { get; } = Units.All;
    public IReadOnlyList<string> CategoryOptions { get; } = ExpenseCategories.All;
    public IReadOnlyList<string> CurrencyOptions { get; } = ExpenseCurrencies.All;

    public override async Task LoadAsync()
    {
        IsOwner = _access.IsOwner;
        var selectedId = SelectedItem?.Id;
        var selectedExpenseId = SelectedExpense?.Id;

        var c = await _inventory.GetContainerAsync(_id);
        if (c is null)
        {
            _shell.Notify("Container not found.", true);
            return;
        }

        Title = c.Title;
        Subtitle = $"{c.ContainerNumber ?? "No number"} · {c.Origin} · arrival "
            + (c.ArrivalDate is DateTime when ? when.ToString("dd MMM yyyy") : "not recorded");
        EditWeight = c.WeightKg;
        EditSupplier = c.Supplier?.Name ?? "";
        var paidNow = await _inventory.PaidSoFarAsync(_id);
        // The box asks what is still owed, because that is the figure the container form asked for and
        // the figure the We owe page shows. The stored number is the bill - what is owed plus everything
        // paid - so the two are exactly one subtraction apart, done here and nowhere else.
        var owedNow = Money.Round(c.SupplierAmount - paidNow);
        EditPaidSoFar = paidNow;
        EditSupplierAmount = owedNow > 0.009m ? owedNow : 0m;
        EditArrival = c.ArrivalDate;
        _yenRateOnFile = c.ExchangeRate;
        EditYenRate = c.ExchangeRate > 1 ? c.ExchangeRate : null;
        // The arithmetic of the sharing, in the shop's own words, straight off the service that writes the
        // costs - so the tape and the figures in the cost column cannot tell two different stories.
        ExpenseTape = (await _inventory.GetExpenseSplitAsync(_id)).Tape;
        ShowExpenseTape = !string.IsNullOrWhiteSpace(ExpenseTape);
        UpdateExpensePreview();
        // Money paid past the figure on a container can only be left over from the earlier rule: the pay
        // page refuses it now, and a box that cannot show a negative shows nothing owed. So the form says
        // what the clamp hides rather than pretending the two figures agree.
        ShowBillHint = owedNow < -0.009m;
        EditBillHint = ShowBillHint
            ? "This container has " + Money.Pkr(-owedNow) + " paid past the figure written on it, from "
              + "before this rule. Saving it as it stands counts that money towards the bill; moving the "
              + "extra to the next shipment is the tidier fix."
            : "";
        IsClosed = c.Status == ContainerStatus.Closed;

        _loadingSelection = true;
        Items.Clear();
        foreach (var i in c.Items)
        {
            Items.Add(new ContainerItemRow
            {
                Id = i.Id,
                Name = i.Product.Name,
                Sku = i.Product.Sku ?? "",
                Unit = i.Product.Unit,
                Purchased = i.QuantityReceived,
                InStock = i.QuantityRemaining,
                UnitCost = i.UnitCost,
                LandedCost = i.EffectiveCost,
                WeightKg = i.WeightKg,
                ForeignCost = i.UnitCost,
                Cartons = i.Cartons,
                PhotoPath = i.PhotoPath ?? i.Product.PhotoPath
            });
        }
        Expenses.Clear();
        foreach (var e in c.Expenses.OrderByDescending(x => x.Date))
            Expenses.Add(e);

        SelectedItem = selectedId is int sid ? Items.FirstOrDefault(i => i.Id == sid) : null;
        SelectedExpense = selectedExpenseId is int eid ? Expenses.FirstOrDefault(e => e.Id == eid) : null;
        _loadingSelection = false;

        var p = await _reports.GetContainerProfitAsync(_id);
        if (p is not null)
        {
            StockValue = Money.Pkr(p.RemainingValue);
            StockSold = p.SoldAmountText;
        }
    }

    partial void OnSelectedItemChanged(ContainerItemRow? value)
    {
        if (_loadingSelection || value is null) return;
        ShowItemForm = true;
        GoodsName = value.Name;
        GoodsSku = value.Sku;
        GoodsUnit = value.Unit;
        GoodsQty = value.Purchased;
        GoodsInStock = value.InStock;
        GoodsCost = value.ForeignCost;
        GoodsWeight = value.WeightKg;
    }

    [ObservableProperty] private bool showExpenseTape;

    partial void OnExpenseTapeChanged(string value) => ShowExpenseTape = !string.IsNullOrWhiteSpace(value);
    partial void OnExpenseAmountChanged(decimal? value) => UpdateExpensePreview();
    partial void OnExpenseCurrencyChanged(string value) => UpdateExpensePreview();
    partial void OnEditYenRateChanged(decimal? value) => UpdateExpensePreview();

    /// <summary>
    /// What the figure being typed comes to in rupees, shown before it is written. The conversion is the
    /// service's own, not a copy of it, so the rupee total on the screen is the one the book will keep.
    /// </summary>
    private void UpdateExpensePreview()
    {
        if (ExpenseCurrencies.CodeOf(ExpenseCurrency) != "JPY" || ExpenseAmount is not decimal amount || amount <= 0)
        {
            ShowExpensePreview = false;
            ExpensePreview = "";
            return;
        }
        // The box being emptied does not erase the rate the container holds, so the preview reads the same
        // figure the book would use.
        var converted = InventoryService.InRupees(amount, EditYenRate ?? _yenRateOnFile);
        if (converted is null)
        {
            ShowExpensePreview = true;
            ExpensePreview = "No yen rate on this container: write Rs for 1 yen in Import details, and "
                             + Money.Yen(amount) + " will convert.";
            return;
        }
        ShowExpensePreview = true;
        ExpensePreview = Money.Yen(amount) + " at " + converted.Value.Rate?.ToString("0.00####") + " = "
                         + Money.Pkr(converted.Value.Pkr)
                         + ", and that is what goes into the costs by weight.";
    }

    partial void OnGoodsInStockChanged(decimal? value)
    {
        if (_loadingSelection || SelectedItem is null || value is null) return;
        GoodsQty = value;
    }

    partial void OnSelectedExpenseChanged(ContainerExpense? value)
    {
        if (_loadingSelection || value is null) return;
        ExpenseCategory = value.Category;
        ExpenseCurrency = value.Currency == "JPY" ? ExpenseCurrencies.All[1] : ExpenseCurrencies.All[0];
        ExpenseAmount = value.Amount;
        ExpenseDate = new DateTimeOffset(value.Date);
        ExpenseNotes = value.Notes ?? "";
    }

    [RelayCommand]
    private async Task AddGoodsAsync()
    {
        try
        {
            await _inventory.AddGoodsAsync(_id, GoodsName, GoodsUnit, GoodsSku, GoodsQty ?? 0, GoodsCost ?? 0,
                null, null, null, Money.Round(GoodsWeight ?? 0m, 3) > 0 ? Money.Round(GoodsWeight ?? 0m, 3) : null, null);
            _shell.MarkChanged();
            _shell.Notify("Item added.");
            ClearGoodsForm();
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SaveGoodsAsync()
    {
        if (SelectedItem is null)
        {
            _shell.Notify("Select an item in the table to edit.", true);
            return;
        }
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to change cost or purchased qty.", true);
            return;
        }
        try
        {
            var stock = GoodsInStock ?? SelectedItem.InStock;
            // What was landed, not what is on the shelf now: the container's expenses are divided over the
            // pieces that came in, so a count must not quietly re-share the freight onto fewer pieces.
            var repriced = await _inventory.UpdateGoodsAsync(
                SelectedItem.Id, GoodsName, GoodsUnit, GoodsSku, SelectedItem.Purchased, stock, GoodsCost ?? 0,
                null, null, Money.Round(GoodsWeight ?? 0m, 3) > 0 ? Money.Round(GoodsWeight ?? 0m, 3) : null,
                SelectedItem.PhotoPath);
            _shell.MarkChanged();
            _shell.Notify(repriced == 0
                ? "Item updated."
                : $"Item updated. Cost also applied to {repriced} sold line{(repriced == 1 ? "" : "s")} - profit follows.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task AddExpenseAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to change expenses.", true); return; }
        try
        {
            await _inventory.AddExpenseAsync(_id, ExpenseDate?.DateTime ?? DateTime.Today, ExpenseCategory,
                ExpenseAmount ?? 0, ExpenseNotes, ExpenseCurrencies.CodeOf(ExpenseCurrency));
            _shell.MarkChanged();
            _shell.Notify("Expense added.");
            ExpenseAmount = 0;
            ExpenseNotes = "";
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SaveExpenseAsync()
    {
        if (SelectedExpense is null)
        {
            _shell.Notify("Select an expense in the table to edit.", true);
            return;
        }
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to change expenses.", true); return; }
        try
        {
            await _inventory.UpdateExpenseAsync(
                SelectedExpense.Id, ExpenseDate?.DateTime ?? DateTime.Today, ExpenseCategory, ExpenseAmount ?? 0,
                ExpenseNotes, ExpenseCurrencies.CodeOf(ExpenseCurrency));
            _shell.MarkChanged();
            _shell.Notify("Expense updated.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task RemoveExpenseAsync()
    {
        if (SelectedExpense is null) return;
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to change expenses.", true); return; }
        try
        {
            await _inventory.DeleteExpenseAsync(SelectedExpense.Id);
            _shell.MarkChanged();
            _shell.Notify("Expense removed.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task DeleteGoodsAsync()
    {
        if (SelectedItem is null)
        {
            _shell.Notify("Select an item in the table to delete.", true);
            return;
        }
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to delete an item.", true);
            return;
        }
        try
        {
            await _inventory.DeleteGoodsAsync(SelectedItem.Id);
            _shell.MarkChanged();
            _shell.Notify("Item deleted.");
            ClearGoodsForm();
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SaveDetailsAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to change container details.", true); return; }
        try
        {
            await _inventory.UpdateImportDetailsAsync(
                _id, EditSupplier, EditSupplierAmount ?? 0, EditPaidSoFar, EditWeight, EditArrival?.DateTime,
                EditYenRate);
            _shell.MarkChanged();
            _shell.Notify("Import details saved.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task ToggleCloseAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to close a container.", true); return; }
        try
        {
            await _inventory.SetStatusAsync(_id, IsClosed ? ContainerStatus.Open : ContainerStatus.Closed);
            _shell.MarkChanged();
            _shell.Notify(IsClosed ? "Container re-opened." : "Container closed.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand] private void ToggleImport() => ShowImportEditor = !ShowImportEditor;
    [RelayCommand] private void SellFromHere() => _shell.GoNewSale();
    [RelayCommand] private void Back() => _shell.Back();

    private void ClearGoodsForm()
    {
        GoodsName = "";
        GoodsSku = "";
        GoodsQty = 1;
        GoodsInStock = null;
        GoodsCost = 0;
        GoodsWeight = null;
        SelectedItem = null;
    }
}

public class ContainerItemRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Unit { get; set; } = "pcs";
    public decimal Purchased { get; set; }
    public decimal InStock { get; set; }
    public decimal UnitCost { get; set; }
    public decimal ForeignCost { get; set; }

    /// <summary>What the piece costs once the container's freight and customs have been shared out by
    /// weight. This is the figure the grid prints as the cost, because it is the figure the shop sells
    /// against - and the goods price is not lost: it stays on the item as what was paid to the supplier.</summary>
    public decimal LandedCost { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? Cartons { get; set; }
    public string? PhotoPath { get; set; }
    public string UnitCostText => Money.Pkr(LandedCost > 0 ? LandedCost : UnitCost);
    /// <summary>The freight part on its own, so the cost column can be read as goods plus this, and the
    /// addition checked against the tape under the expenses.</summary>
    public string FreightText => LandedCost - UnitCost > 0.005m ? Money.Pkr(Money.Round(LandedCost - UnitCost)) : "";
    /// <summary>The lot in all, which is what the expenses are divided by - shown next to the cost it
    /// bought, so the division can be checked on the screen with a calculator and two numbers.</summary>
    public string TotalWeightText => WeightKg is decimal w && w > 0 && Purchased > 0
        ? Money.Kg(Money.Round(w * Purchased, 3))
        : "no weight";
}
