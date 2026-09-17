using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ContainerDetailViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly InventoryService _inventory;
    private readonly ReportService _reports;
    private readonly AccessService _access;
    private readonly IAppShell _shell;
    private readonly int _id;
    private bool _loadingSelection;

    public ContainerDetailViewModel(int id, InventoryService inventory, ReportService reports, AccessService access, IAppShell shell, PrintService print)
    {
        _print = print;
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
    [ObservableProperty] private string stockCollected = "—";
    [ObservableProperty] private string stockMarket = "—";

    [ObservableProperty] private string goodsName = "";
    [ObservableProperty] private string goodsSku = "";
    [ObservableProperty] private string goodsUnit = "pcs";
    [ObservableProperty] private decimal? goodsQty = 1;
    [ObservableProperty] private decimal? goodsInStock;
    [ObservableProperty] private decimal? goodsCost;

    /// <summary>Which currency the cost price is being typed in. The invoice from Japan says yen and the
    /// till says rupees, and this form now takes either - so the figure kept is the one that was written,
    /// next to the rupee figure the rest of the book works in.</summary>
    [ObservableProperty] private string goodsCurrency = Currencies.EntryLabels[0];
    [ObservableProperty] private bool goodsIsYen;
    [ObservableProperty] private string goodsCostPreview = "";
    [ObservableProperty] private bool showGoodsCostPreview;

    /// <summary>What one piece weighs, in kilograms - the figure on the carton, and the same one the order
    /// sheet asks for. The container's expenses are shared over what the lot weighs in all, so an item left
    /// blank holds up the whole sharing: the page says so rather than loading its freight onto the others.</summary>
    [ObservableProperty] private decimal? goodsWeight;
    [ObservableProperty] private ContainerItemRow? selectedItem;

    /// <summary>What the money was for, in the shop's own words. A dropdown of categories could not say
    /// "demurrage at the port", and a figure nobody can name is a figure nobody finds again; a blank is kept
    /// as "Other" by the book.</summary>
    [ObservableProperty] private string expenseCategory = "";
    [ObservableProperty] private string expenseCurrency = Currencies.EntryLabels[0];
    [ObservableProperty] private bool expenseIsYen;
    [ObservableProperty] private decimal? expenseAmount;
    [ObservableProperty] private DateTimeOffset? expenseDate = DateTimeOffset.Now;
    [ObservableProperty] private string expenseNotes = "";
    [ObservableProperty] private ContainerExpense? selectedExpense;

    [ObservableProperty] private decimal? editWeight;

    /// <summary>
    /// Yen to rupees, for this shipment - one figure, and the box for it is shown wherever a yen figure is
    /// being typed, because a rate read from a form above the fold is a rate that gets left at last month's
    /// value. Expenses and item costs are converted at it once, when they are saved, and the rate is kept on
    /// the line: changing it here afterwards never re-values money already paid to a clearing agent.
    /// </summary>
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

    // The send-back line. Its own boxes rather than the item form's, because the two acts move stock in
    // opposite directions and a box that had to be re-typed for either would be a box that gets used wrong.
    [ObservableProperty] private string backTarget = "";
    [ObservableProperty] private decimal? backQty;
    [ObservableProperty] private DateTimeOffset? backDate = DateTimeOffset.Now;
    [ObservableProperty] private string backReason = "";
    [ObservableProperty] private decimal? backTill;
    [ObservableProperty] private decimal? backBill;
    [ObservableProperty] private decimal? backFreight;
    [ObservableProperty] private string backNotes = "";
    [ObservableProperty] private string backOwed = "";
    [ObservableProperty] private bool showBackForm;
    [ObservableProperty] private SupplierReturnRow? selectedReturn;

    /// <summary>Two taps for the one action in this book that cannot be undone: the button arms itself,
    /// then deletes. A whole container is not taken out of the books by a stray click.</summary>
    [ObservableProperty] private bool confirmDelete;

    [ObservableProperty] private string deleteLabel = "Delete container";

    public override bool FillsPage => true;

    public ObservableCollection<ContainerItemRow> Items { get; } = new();
    public ObservableCollection<SupplierReturnRow> Returns { get; } = new();
    public ObservableCollection<ContainerExpense> Expenses { get; } = new();
    public IReadOnlyList<string> UnitOptions { get; } = Units.All;
    public IReadOnlyList<string> CurrencyOptions { get; } = Currencies.EntryLabels;

    public override async Task LoadAsync()
    {
        IsOwner = _access.IsOwner;
        var selectedId = SelectedItem?.Id;
        var selectedExpenseId = SelectedExpense?.Id;
        var selectedReturnId = SelectedReturn?.Id;

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
        // The rate as it stands, unless it is still the untouched default of 1 - which is not a rate but the
        // absence of one, and showing it in the box would have a shop save a figure it never chose.
        EditYenRate = Currencies.UsableRate(c.ExchangeRate) ? c.ExchangeRate : null;
        // The arithmetic of the sharing, in the shop's own words, straight off the service that writes the
        // costs - so the tape and the figures in the cost column cannot tell two different stories.
        ExpenseTape = (await _inventory.GetExpenseSplitAsync(_id)).Tape;
        ShowExpenseTape = !string.IsNullOrWhiteSpace(ExpenseTape);
        UpdateExpensePreview();
        UpdateGoodsPreview();
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
        // The figure the settlement against their bill has to stay inside of, said before it is typed rather
        // than refused after it. It is the same subtraction the pay-a-supplier list makes, from the same helper.
        BackOwed = owedNow > 0.009m ? Money.Pkr(owedNow) + " owed on this container" : "Nothing owed on this container";

        // Summed once here rather than counted on each row, so the figure in the goods table and the one in
        // the returns table below it are the same rows added the same way.
        var sentBack = c.SupplierReturns.GroupBy(r => r.ContainerItemId)
            .ToDictionary(g => g.Key, g => Money.Round(g.Sum(r => r.Quantity), 3));
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
                ForeignCost = i.ForeignCost,
                CostCurrency = i.CostCurrency,
                CostRate = i.CostRate,
                Cartons = i.Cartons,
                PhotoPath = i.PhotoPath ?? i.Product.PhotoPath,
                SentBack = sentBack.GetValueOrDefault(i.Id, 0m)
            });
        }
        Expenses.Clear();
        foreach (var e in c.Expenses.OrderByDescending(x => x.Date))
            Expenses.Add(e);

        Returns.Clear();
        foreach (var r in c.SupplierReturns.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
        {
            Returns.Add(new SupplierReturnRow
            {
                Id = r.Id,
                Date = r.Date,
                ItemName = r.Item.Product.Name,
                Quantity = r.Quantity,
                IntoTill = r.IntoTillPkr,
                AgainstBill = r.AgainstBillPkr,
                AgainstFreight = r.AgainstFreightPkr,
                Reason = r.Reason,
                Notes = r.Notes
            });
        }

        SelectedItem = selectedId is int sid ? Items.FirstOrDefault(i => i.Id == sid) : null;
        SelectedExpense = selectedExpenseId is int eid ? Expenses.FirstOrDefault(e => e.Id == eid) : null;
        SelectedReturn = selectedReturnId is int rid ? Returns.FirstOrDefault(x => x.Id == rid) : null;
        _loadingSelection = false;

        var p = await _reports.GetContainerProfitAsync(_id);
        if (p is not null)
        {
            StockValue = Money.Pkr(p.RemainingValue);
            StockSold = p.SoldAmountText;
            StockCollected = p.CollectedText;
            StockMarket = p.InMarketText;
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
        GoodsCost = value.CostEntered;
        GoodsCurrency = Currencies.Shown(value.CostCurrency);
        GoodsIsYen = value.CostCurrency == "JPY";
        GoodsWeight = value.WeightKg;
        BackTarget = value.Name + " — " + Money.Qty3(value.InStock) + " " + value.Unit + " here"
                     + (value.SentBack > 0.0004m ? ", " + Money.Qty3(value.SentBack) + " already sent back" : "");
        // The units are not filled in from what is here: the form says what is here, and what goes back is
        // the shop's own figure, typed - which is the only way a mis-typed zero shows itself.
        ShowBackForm = true;
        UpdateGoodsPreview();
    }

    [ObservableProperty] private bool showExpenseTape;

    partial void OnExpenseTapeChanged(string value) => ShowExpenseTape = !string.IsNullOrWhiteSpace(value);

    partial void OnConfirmDeleteChanged(bool value) => DeleteLabel = value ? "Tap again to delete" : "Delete container";
    partial void OnExpenseAmountChanged(decimal? value) => UpdateExpensePreview();

    partial void OnExpenseCurrencyChanged(string value)
    {
        ExpenseIsYen = Currencies.CodeOf(value) == "JPY";
        UpdateExpensePreview();
    }

    partial void OnGoodsCurrencyChanged(string value)
    {
        GoodsIsYen = Currencies.CodeOf(value) == "JPY";
        UpdateGoodsPreview();
    }

    partial void OnGoodsCostChanged(decimal? value) => UpdateGoodsPreview();

    // One rate box, wherever it is shown, and one answer under each of the two figures it converts: the
    // same number in both places it can be read, never two a shop has to square up.
    partial void OnEditYenRateChanged(decimal? value)
    {
        UpdateExpensePreview();
        UpdateGoodsPreview();
    }

    /// <summary>
    /// What the figure being typed comes to in rupees, shown before it is written. The conversion is the
    /// service's own, not a copy of it - the same method and the same rate rule - so the rupee total read on
    /// the screen is the one the book keeps, rather than a preview that drifts from the entry.
    /// </summary>
    private void UpdateExpensePreview()
    {
        if (Currencies.CodeOf(ExpenseCurrency) != "JPY" || ExpenseAmount is not decimal amount || amount <= 0)
        {
            ShowExpensePreview = false;
            ExpensePreview = "";
            return;
        }
        // An emptied box does not erase the rate the container holds, so the line reads the figure the book
        // would use rather than the figure the box happens to show.
        var converted = Currencies.InRupees(amount, Currencies.RateFor(_yenRateOnFile, EditYenRate));
        ShowExpensePreview = true;
        ExpensePreview = converted is null
            ? Money.Yen(amount) + " has no rate to convert it at. Write Rs for 1 yen here, or choose Rs if "
              + "the bill was in rupees."
            : Money.Yen(amount) + " at " + Currencies.RateText(converted.Value.Rate) + " = "
              + Money.Pkr(converted.Value.Pkr) + ", and that is what goes into the costs by weight.";
    }

    /// <summary>The same line for an item's cost price: what a piece will be carried at in rupees, before
    /// it is written - and it is that rupee figure, not the yen one, that the freight is then added to.</summary>
    private void UpdateGoodsPreview()
    {
        if (Currencies.CodeOf(GoodsCurrency) != "JPY" || GoodsCost is not decimal cost || cost <= 0)
        {
            ShowGoodsCostPreview = false;
            GoodsCostPreview = "";
            return;
        }
        var converted = Currencies.InRupees(cost, Currencies.RateFor(_yenRateOnFile, EditYenRate));
        ShowGoodsCostPreview = true;
        GoodsCostPreview = converted is null
            ? Money.Yen(cost) + " a piece has no rate to convert it at. Write Rs for 1 yen here, or choose "
              + "Rs if the invoice was in rupees."
            : Money.Yen(cost) + " at " + Currencies.RateText(converted.Value.Rate) + " = "
              + Money.Pkr(converted.Value.Pkr) + " a piece, before its share of the freight.";
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
        ExpenseCurrency = Currencies.Shown(value.Currency);
        ExpenseIsYen = value.Currency == "JPY";
        // A yen line carries the rate it was taken at, and the form reads it back: picking a line up and
        // pressing Save must put the same rupees down again, not today's value of a bill already paid. The
        // box then holds the sheet's rate for the next figure typed, which is what KeepRate is for.
        if (value.Currency == "JPY" && value.RateUsed is decimal rowRate && Currencies.UsableRate(rowRate))
            EditYenRate = rowRate;
        ExpenseAmount = value.Amount;
        ExpenseDate = new DateTimeOffset(value.Date);
        ExpenseNotes = value.Notes ?? "";
    }

    [RelayCommand]
    private void Print()
    {
        var goods = Items.Select(i => new[]
        {
            i.Name, i.Sku ?? "", i.Unit, i.UnitCostText, i.FreightText, i.TotalWeightText, i.CostNoteText,
        }).Cast<IReadOnlyList<string>>().ToList();
        var bills = Expenses.Select(e => new[]
        {
            e.Date.ToString("dd MMM yyyy"), e.Category, e.SourceText, e.Currency,
        }).Cast<IReadOnlyList<string>>().ToList();
        // The returns only earn a table when there are any to print, and the total line is labelled as a
        // total, so a sheet read on paper cannot be mistaken for a list of goods.
        var backs = Returns.Select(r => new[]
        {
            r.DateText, r.ItemName, r.QuantityText, r.IntoTillText, r.AgainstBillText, r.AgainstFreightText,
            r.Reason ?? ""
        }).Cast<IReadOnlyList<string>>().ToList();
        var tables = new List<PrintTable>
        {
            new("Goods",
                new[] { "Item", "Code", "Unit", "Cost each", "Freight each", "Weight", "Cost as written" }, goods, null, 3),
            new("Bills", new[] { "Date", "What it was for", "As it was written", "Money" }, bills, null, 2),
        };
        if (backs.Count > 0)
        {
            tables.Add(new PrintTable("Sent back to supplier",
                new[] { "Date", "Item", "Units", "Into the till", "Against their bill", "Against freight", "Why" },
                backs,
                new[]
                {
                    "All returns", "", Money.Qty3(Returns.Sum(r => r.Quantity)),
                    Money.Pkr(Money.Round(Returns.Sum(r => r.IntoTill))),
                    Money.Pkr(Money.Round(Returns.Sum(r => r.AgainstBill))),
                    Money.Pkr(Money.Round(Returns.Sum(r => r.AgainstFreight))), ""
                }, 2));
        }
        _print.PrintTables($"container-{_id}-paper.html", Title, Subtitle, tables);
        _shell.Notify("Printed from the page you were on.");
    }

    [RelayCommand]
    private async Task AddGoodsAsync()
    {
        try
        {
            await _inventory.AddGoodsAsync(_id, GoodsName, GoodsUnit, GoodsSku, GoodsQty ?? 0, GoodsCost ?? 0,
                null, null, null, Money.Round(GoodsWeight ?? 0m, 3) > 0 ? Money.Round(GoodsWeight ?? 0m, 3) : null,
                null, Currencies.CodeOf(GoodsCurrency), EditYenRate);
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
            // The Qty box is the landed count and Save has to hear it: a shop that wrote 1,000 and finds 700
            // in the packing corrects the item, and the freight each piece carries follows that number,
            // because the expenses are shared over the pieces that came in. What must not move it is the
            // other box - "In stock" is a shelf count, and letting a stock check re-share the freight onto
            // fewer pieces would re-price the whole lot from a miscount. The box is filled from the row
            // before it is edited, so a save that never touched Qty sends the same count back and the
            // freight lands where it already was.
            var repriced = await _inventory.UpdateGoodsAsync(
                SelectedItem.Id, GoodsName, GoodsUnit, GoodsSku, GoodsQty ?? SelectedItem.Purchased, stock,
                GoodsCost ?? 0, null, null,
                Money.Round(GoodsWeight ?? 0m, 3) > 0 ? Money.Round(GoodsWeight ?? 0m, 3) : null,
                SelectedItem.PhotoPath, Currencies.CodeOf(GoodsCurrency), EditYenRate);
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
                ExpenseAmount ?? 0, ExpenseNotes, Currencies.CodeOf(ExpenseCurrency), EditYenRate);
            _shell.MarkChanged();
            _shell.Notify("Expense added.");
            ClearExpenseForm();
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
                ExpenseNotes, Currencies.CodeOf(ExpenseCurrency), EditYenRate);
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
            // The row it was typed from is gone, so the boxes have nothing left to be true about.
            ClearExpenseForm();
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

    /// <summary>
    /// Hand goods back to the supplier, with whatever came back with them. Every figure on the form is typed
    /// and kept as typed: which of the three boxes the money went into is the shop's answer about where its
    /// money landed, and this page does not choose it.
    /// </summary>
    [RelayCommand]
    private async Task SendBackAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to send goods back.", true); return; }
        if (SelectedItem is null)
        {
            _shell.Notify("Select an item in the items table first, to say what goes back.", true);
            return;
        }
        if (BackQty is not decimal units || units <= 0m)
        {
            _shell.Notify("Say how many units are going back.", true);
            return;
        }
        try
        {
            var r = await _inventory.ReturnToSupplierAsync(SelectedItem.Id, BackDate?.DateTime ?? DateTime.Today,
                units, BackReason, BackTill ?? 0m, BackBill ?? 0m, BackFreight ?? 0m, BackNotes);
            _shell.MarkChanged();
            var back = Money.Round(r.IntoTillPkr + r.AgainstBillPkr + r.AgainstFreightPkr);
            _shell.Notify(back > 0.004m
                ? Money.Qty3(r.Quantity) + " sent back, " + Money.Pkr(back) + " came with them."
                : Money.Qty3(r.Quantity) + " sent back, nothing settled for them.");
            ClearBackForm();
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task RemoveReturnAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to remove a return.", true); return; }
        if (SelectedReturn is null)
        {
            _shell.Notify("Select the line in the returns table to remove it.", true);
            return;
        }
        try
        {
            await _inventory.RemoveReturnAsync(SelectedReturn.Id);
            _shell.MarkChanged();
            _shell.Notify("Return removed, and the units are back in stock.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    /// <summary>
    /// Take the container out of the book. The service refuses it on any lot that money or goods moved
    /// against, and says what is in the way, so this page never has to hold a second opinion about what is
    /// safe to delete.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to delete a container.", true); return; }
        if (!ConfirmDelete)
        {
            ConfirmDelete = true;
            return;
        }
        ConfirmDelete = false;
        try
        {
            await _inventory.DeleteContainerAsync(_id);
            _shell.MarkChanged();
            _shell.GoContainers();
            _shell.Notify("Container deleted.");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand] private void ToggleImport() => ShowImportEditor = !ShowImportEditor;
    [RelayCommand] private void SellFromHere() => _shell.GoNewSale();
    [RelayCommand] private void Back() => _shell.Back();

    /// <summary>
    /// The expense form as it stands before any of it is typed: nothing in the boxes, today on the date,
    /// rupees at the currency. Add and Remove come through here. Pressing Save on a row does not, because that
    /// row is still picked and its figures are still honest in the boxes above it - the same convention the
    /// goods form keeps, so the two halves of the page let go of a draft at the same moment.
    ///
    /// The rate box is left alone: it is the container's rate rather than part of this entry, and a run of
    /// yen bills is typed with one rate showing the whole time, which is also why the yen figures added
    /// before now keep the rate they were taken at rather than whatever the box is set to next.
    /// </summary>
    private void ClearExpenseForm()
    {
        ExpenseCategory = "";
        ExpenseAmount = null;
        ExpenseNotes = "";
        ExpenseCurrency = Currencies.EntryLabels[0];
        ExpenseIsYen = false;
        ShowExpensePreview = false;
        ExpensePreview = "";
        // The same value the box holds when the page first opens, so "reset" means back to that and not to
        // some second idea of today the file did not have before.
        ExpenseDate = DateTimeOffset.Now;
        SelectedExpense = null;
    }

    private void ClearBackForm()
    {
        BackTarget = "";
        BackQty = null;
        BackDate = DateTimeOffset.Now;
        BackReason = "";
        BackTill = null;
        BackBill = null;
        BackFreight = null;
        BackNotes = "";
        SelectedReturn = null;
    }

    private void ClearGoodsForm()
    {
        GoodsName = "";
        GoodsSku = "";
        GoodsQty = 1;
        GoodsInStock = null;
        GoodsCost = 0;
        GoodsWeight = null;
        GoodsCurrency = Currencies.EntryLabels[0];
        GoodsIsYen = false;
        ShowGoodsCostPreview = false;
        GoodsCostPreview = "";
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

    /// <summary>What has been handed back to the supplier off this line. It sits between what was landed and
    /// what is in stock so the two figures can be read as one story: what arrived, what went back, what is
    /// here. Blank when nothing went back, because a column of noughts is noise.</summary>
    public decimal SentBack { get; set; }

    public string SentBackText => SentBack > 0.0004m ? Money.Qty3(SentBack) : "";
    public decimal UnitCost { get; set; }
    public decimal ForeignCost { get; set; }

    /// <summary>What the piece costs once the container's freight and customs have been shared out by
    /// weight. This is the figure the grid prints as the cost, because it is the figure the shop sells
    /// against - and the goods price is not lost: it stays on the item as what was paid to the supplier.</summary>
    public decimal LandedCost { get; set; }
    public decimal? WeightKg { get; set; }

    /// <summary>The currency the cost price was typed in, and the rate it was taken at: shown under the
    /// rupee cost, so a line entered in yen reads as what it was without opening the form again.</summary>
    public string CostCurrency { get; set; } = "PKR";
    public decimal? CostRate { get; set; }
    public decimal CostEntered => CostCurrency == "JPY" ? ForeignCost : UnitCost;
    public string CostNoteText => CostCurrency == "JPY"
        ? Money.Yen(ForeignCost) + " at " + Currencies.RateText(CostRate)
        : "";
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

/// <summary>
/// One goods return as the container's page reads it. The three money figures are shown as they were settled,
/// side by side with the units they were settled on, because the pair is the point: a container whose goods
/// went back for nothing is a loss, and the row that says so has to be legible on its own.
/// </summary>
public class SupplierReturnRow
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string ItemName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal IntoTill { get; set; }
    public decimal AgainstBill { get; set; }
    public decimal AgainstFreight { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public string DateText => Date.ToString("dd MMM yyyy");
    public string QuantityText => Money.Qty3(Quantity);
    public string IntoTillText => IntoTill > 0.004m ? Money.Pkr(IntoTill) : "";
    public string AgainstBillText => AgainstBill > 0.004m ? Money.Pkr(AgainstBill) : "";
    public string AgainstFreightText => AgainstFreight > 0.004m ? Money.Pkr(AgainstFreight) : "";
}
