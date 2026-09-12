using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// One order sheet. Type a row, press Add row, and the tape at the top moves: yen cost, the same money in
/// rupees at your rate, the expense figure, what the lot brings if all of it sells, and the profit left after
/// both. The expenses are typed a bill at a time, as on a container - what it was for, how much, in yen or
/// rupees - and the sheet's expense figure is those rows added up. Nothing here touches stock.
/// </summary>
public partial class BuyPlanDetailViewModel : ViewModelBase
{
    private readonly BuyPlanService _plans;
    private readonly AccessService _access;
    private readonly IAppShell _shell;
    private readonly int _id;
    private readonly List<BuyPlanLineRow> _draft = new();
    private readonly List<BuyPlanExpenseRow> _expenseDraft = new();
    private bool _busy;

    public BuyPlanDetailViewModel(int id, BuyPlanService plans, AccessService access, IAppShell shell)
    {
        _id = id;
        _plans = plans;
        _access = access;
        _shell = shell;
    }

    public override bool FillsPage => true;

    public ObservableCollection<BuyPlanLineRow> Lines { get; } = new();

    /// <summary>The same two currencies the container page offers, from the same list.</summary>
    public IReadOnlyList<string> CurrencyOptions { get; } = Currencies.EntryLabels;
    public ObservableCollection<BuyPlanExpenseRow> Expenses { get; } = new();

    [ObservableProperty] private string title = "";
    [ObservableProperty] private decimal? yenRate = BuyPlanService.SuggestedRate;
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private bool isDirty;

    [ObservableProperty] private string itemName = "";
    [ObservableProperty] private decimal? qty = 1;
    [ObservableProperty] private decimal? unitCostYen;
    [ObservableProperty] private decimal? unitWeightKg;
    [ObservableProperty] private decimal? salePricePkr;
    [ObservableProperty] private BuyPlanLineRow? selected;
    [ObservableProperty] private bool hasSelection;

    // The expense form, typed a bill at a time. The rate box is the row's own rather than the sheet's header
    // box on purpose: the header rate is what the goods rows are priced at, and clicking an old bill should
    // not quietly re-price the whole sheet with it.
    [ObservableProperty] private string expenseDescription = "";
    [ObservableProperty] private decimal? expenseAmount;
    [ObservableProperty] private string expenseCurrency = Currencies.EntryLabels[0];
    [ObservableProperty] private decimal? expenseRate;
    [ObservableProperty] private bool expenseIsYen;
    [ObservableProperty] private bool showExpensePreview;
    [ObservableProperty] private string expensePreview = "";
    [ObservableProperty] private BuyPlanExpenseRow? selectedExpense;
    [ObservableProperty] private bool hasExpenseSelection;
    [ObservableProperty] private string expenseTape = "";
    [ObservableProperty] private bool showExpenseTape;

    [ObservableProperty] private string countText = "0 rows";
    [ObservableProperty] private string costYenText = "—";
    [ObservableProperty] private string costPkrText = "—";
    [ObservableProperty] private string expenseText = "—";
    [ObservableProperty] private string spendText = "—";
    [ObservableProperty] private string saleText = "—";
    [ObservableProperty] private string profitText = "—";
    [ObservableProperty] private string marginText = "—";
    [ObservableProperty] private string weightText = "—";
    [ObservableProperty] private bool profitGood = true;

    public override async Task LoadAsync()
    {
        IsOwner = _access.IsOwner;

        var plan = await _plans.GetAsync(_id);
        if (plan is null)
        {
            _shell.Notify("This sheet is gone. It may have been deleted.", true);
            return;
        }

        _busy = true;
        LoadFrom(plan);
        _busy = false;
    }

    partial void OnYenRateChanged(decimal? value)
    {
        if (_busy)
            return;
        MarkDirty();
        RebuildGrid();
        Recalc();
        // Rows being typed follow the rate the sheet was just set to; a row already added keeps the rupees it
        // was added with, because those rupees are the number the sheet has been totalling.
        UpdateExpensePreview();
    }

    partial void OnExpenseAmountChanged(decimal? value) => UpdateExpensePreview();

    partial void OnExpenseCurrencyChanged(string value)
    {
        ExpenseIsYen = Currencies.CodeOf(value) == "JPY";
        if (!_busy && !ExpenseIsYen)
            ExpenseRate = null;
        UpdateExpensePreview();
    }

    partial void OnExpenseRateChanged(decimal? value) => UpdateExpensePreview();

    partial void OnTitleChanged(string value)
    {
        if (!_busy)
            MarkDirty();
    }

    partial void OnSelectedChanged(BuyPlanLineRow? value)
    {
        HasSelection = value is not null && IsOwner;
        if (_busy || value is null)
            return;
        _busy = true;
        ItemName = value.ItemName;
        Qty = value.Quantity;
        UnitCostYen = value.UnitCostYen;
        UnitWeightKg = value.UnitWeightKg;
        SalePricePkr = value.SalePricePkr;
        _busy = false;
    }

    partial void OnSelectedExpenseChanged(BuyPlanExpenseRow? value)
    {
        HasExpenseSelection = value is not null && IsOwner;
        if (_busy || value is null)
            return;
        _busy = true;
        ExpenseDescription = value.Description;
        // The figure as it was written, and the rate it was written at: a yen bill is read back in yen, so
        // the row can be corrected without the book's rupee total being mistaken for what the shop typed.
        ExpenseCurrency = Currencies.Shown(value.Currency);
        ExpenseIsYen = value.Currency == "JPY";
        ExpenseAmount = value.AmountEntered;
        ExpenseRate = value.Currency == "JPY" ? value.RateUsed : null;
        _busy = false;
        UpdateExpensePreview();
    }

    /// <summary>
    /// What a yen bill comes to on this sheet, said while it is being typed and before any of it is kept.
    /// The multiplication is the shared one, with the same rate rule the save uses, so the rupees read here
    /// are the rupees the sheet will add - not a preview that drifts from the entry.
    /// </summary>
    private void UpdateExpensePreview()
    {
        if (!ExpenseIsYen || ExpenseAmount is not decimal amount || amount <= 0m)
        {
            ShowExpensePreview = false;
            ExpensePreview = "";
            return;
        }
        var converted = Currencies.InRupees(amount, Currencies.RateFor(Rate, ExpenseRate));
        ShowExpensePreview = true;
        ExpensePreview = converted is null
            ? Money.Yen(amount) + " has no rate to convert it at. Write Rs for 1 yen beside it, or choose "
              + "Rs if the bill was in rupees."
            : Money.Yen(amount) + " at " + Currencies.RateText(converted.Value.Rate) + " = "
              + Money.Pkr(converted.Value.Pkr) + ", and that is what the sheet adds to the goods.";
    }

    [RelayCommand]
    private void AddLine()
    {
        if (!NeedOwner("change an order sheet"))
            return;

        var row = DraftRow();
        var error = CheckRow(row);
        if (error is not null)
        {
            _shell.Notify(error, true);
            return;
        }

        _draft.Add(row);
        ClearForm();
        RebuildGrid();
        Recalc();
        MarkDirty();
    }

    [RelayCommand]
    private void UpdateLine()
    {
        if (!NeedOwner("change an order sheet"))
            return;
        if (Selected is null)
        {
            _shell.Notify("Pick a row first.", true);
            return;
        }
        var at = _draft.IndexOf(Selected);
        if (at < 0)
        {
            _shell.Notify("That row is not on this sheet any more.", true);
            return;
        }

        var row = DraftRow();
        var error = CheckRow(row);
        if (error is not null)
        {
            _shell.Notify(error, true);
            return;
        }

        row.Id = Selected.Id;
        _draft[at] = row;
        RebuildGrid();
        Recalc();
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveLine()
    {
        if (!NeedOwner("change an order sheet"))
            return;
        if (Selected is null)
        {
            _shell.Notify("Pick a row first.", true);
            return;
        }
        var at = _draft.IndexOf(Selected);
        if (at >= 0)
            _draft.RemoveAt(at);
        ClearForm();
        RebuildGrid();
        Recalc();
        MarkDirty();
    }

    [RelayCommand]
    private void AddExpense()
    {
        if (!NeedOwner("change an order sheet"))
            return;
        var (row, error) = ExpenseDraft();
        if (error is not null)
        {
            _shell.Notify(error, true);
            return;
        }
        _expenseDraft.Add(row!);
        ClearExpenseForm();
        RebuildExpenseGrid();
        Recalc();
        MarkDirty();
    }

    [RelayCommand]
    private void UpdateExpense()
    {
        if (!NeedOwner("change an order sheet"))
            return;
        if (SelectedExpense is null)
        {
            _shell.Notify("Pick an expense row first.", true);
            return;
        }
        var at = _expenseDraft.IndexOf(SelectedExpense);
        if (at < 0)
        {
            _shell.Notify("That row is not on this sheet any more.", true);
            return;
        }
        var (row, error) = ExpenseDraft();
        if (error is not null)
        {
            _shell.Notify(error, true);
            return;
        }
        row!.Id = SelectedExpense.Id;
        _expenseDraft[at] = row;
        RebuildExpenseGrid();
        Recalc();
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveExpense()
    {
        if (!NeedOwner("change an order sheet"))
            return;
        if (SelectedExpense is null)
        {
            _shell.Notify("Pick an expense row first.", true);
            return;
        }
        var at = _expenseDraft.IndexOf(SelectedExpense);
        if (at >= 0)
            _expenseDraft.RemoveAt(at);
        ClearExpenseForm();
        RebuildExpenseGrid();
        Recalc();
        MarkDirty();
    }

    /// <summary>
    /// The row as it is being typed, already in the money the sheet adds. A rupee figure is taken as written;
    /// a yen one is multiplied out here, by the shared rule and the rate on the row, and the yen figure and
    /// that rate travel with it - so the rupees read under the box are the rupees kept, and the bill can still
    /// be read in the currency it arrived in years from now. A yen figure with no rate to take it at is
    /// refused here rather than added and converted at whatever rate the sheet happens to hold tomorrow.
    /// </summary>
    private (BuyPlanExpenseRow? Row, string? Error) ExpenseDraft()
    {
        var amount = Money.Round(ExpenseAmount ?? 0m);
        var code = Currencies.CodeOf(ExpenseCurrency);
        if (amount <= 0m)
            return (null, "Type the amount the bill was for, above zero.");

        var row = new BuyPlanExpenseRow { Description = ExpenseDescription.Trim(), Currency = code };
        if (code != "JPY")
        {
            row.AmountPkr = amount;
            return (row, null);
        }

        var converted = Currencies.InRupees(amount, Currencies.RateFor(Rate, ExpenseRate));
        if (converted is null)
            return (null, Currencies.NoRateMessage(amount));
        row.AmountPkr = converted.Value.Pkr;
        row.AmountForeign = converted.Value.Foreign;
        row.RateUsed = converted.Value.Rate;
        return (row, null);
    }

    private void ClearExpenseForm()
    {
        _busy = true;
        ExpenseDescription = "";
        ExpenseAmount = null;
        ExpenseCurrency = Currencies.EntryLabels[0];
        ExpenseIsYen = false;
        ExpenseRate = null;
        SelectedExpense = null;
        _busy = false;
        ShowExpensePreview = false;
        ExpensePreview = "";
    }

    /// <summary>The expense grid is read-only too, so it is refreshed by putting the rows back. Selection is
    /// kept by position and the form is left alone, so typing survives an update.</summary>
    private void RebuildExpenseGrid()
    {
        var at = SelectedExpense is null ? -1 : _expenseDraft.IndexOf(SelectedExpense);
        Expenses.Clear();
        foreach (var row in _expenseDraft)
            Expenses.Add(row);
        if (at >= 0 && at < Expenses.Count)
            SelectedExpense = Expenses[at];
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!NeedOwner("save an order sheet"))
            return;
        try
        {
            await WriteAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        if (IsDirty)
        {
            try
            {
                await WriteAsync();
            }
            catch (Exception ex)
            {
                _shell.Notify(ex.Message, true);
                return;
            }
        }
        _shell.Back();
    }

    private async Task WriteAsync()
    {
        if (!_access.IsOwner)
            throw new InvalidOperationException("Owner PIN needed to save an order sheet.");

        var rows = _draft
            .Where(r => !string.IsNullOrWhiteSpace(r.ItemName))
            .Select(r => r.ToInput())
            .ToList();

        await _plans.SaveAsync(_id, Title, YenRate ?? 0, rows, _expenseDraft.Select(e => e.ToInput()).ToList());

        var saved = await _plans.GetAsync(_id);
        _busy = true;
        if (saved is not null)
            LoadFrom(saved);
        else
            Recalc();
        IsDirty = false;
        _busy = false;
        _shell.MarkChanged();
        _shell.Notify("Saved.");
    }

    private void LoadFrom(BuyPlanRow plan)
    {
        Title = plan.Title;
        YenRate = plan.YenRate;
        _draft.Clear();
        _draft.AddRange(plan.Lines);
        _expenseDraft.Clear();
        _expenseDraft.AddRange(plan.Expenses);
        RebuildGrid();
        RebuildExpenseGrid();
        Recalc();
        ClearExpenseForm();
    }

    /// <summary>
    /// Keeps the stored weight on the same 3 decimal grid the page shows, so the number in the
    /// "kg each" column really does multiply out to "Total kg".
    /// </summary>
    private static decimal Round3(decimal value) => Money.Round(value, 3);

    private BuyPlanLineRow DraftRow() => new()
    {
        ItemName = ItemName.Trim(),
        Quantity = Qty ?? 0,
        UnitCostYen = UnitCostYen ?? 0,
        UnitWeightKg = Round3(UnitWeightKg ?? 0),
        SalePricePkr = SalePricePkr ?? 0,
        YenRate = Rate
    };

    private static string? CheckRow(BuyPlanLineRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ItemName))
            return "Type the item name.";
        if (row.Quantity <= 0)
            return "Type how many pieces.";
        if (row.UnitCostYen < 0 || row.SalePricePkr < 0 || row.UnitWeightKg < 0)
            return "Those numbers cannot be negative.";
        return null;
    }

    private void ClearForm()
    {
        _busy = true;
        ItemName = "";
        Qty = 1;
        UnitCostYen = null;
        UnitWeightKg = null;
        SalePricePkr = null;
        Selected = null;
        _busy = false;
    }

    /// <summary>
    /// The grid is read-only, so rows are refreshed by putting them back in. Selection is kept by
    /// position, and the form is not touched, so typing survives an add.
    /// </summary>
    private void RebuildGrid()
    {
        var at = Selected is null ? -1 : _draft.IndexOf(Selected);
        var rate = Rate;
        foreach (var row in _draft)
            row.YenRate = rate;

        Lines.Clear();
        foreach (var row in _draft)
            Lines.Add(row);

        if (at >= 0 && at < Lines.Count)
            Selected = Lines[at];
    }

    private void Recalc()
    {
        // The sheet's expense figure is its rows added up, here while they are being typed and in the book
        // once they are saved - one rule, so the tape, the sheet and the list cannot show three totals.
        var t = BuyPlanTotal.Build(_draft, Rate, Money.Round(_expenseDraft.Sum(e => e.AmountPkr)));

        CountText = t.ItemCountText;
        CostYenText = t.CostYenText;
        CostPkrText = t.CostPkrText;
        ExpenseText = t.ExpenseText;
        SpendText = t.SpendText;
        SaleText = t.SaleText;
        ProfitText = t.ProfitText;
        MarginText = t.MarginText;
        WeightText = t.WeightText;
        ProfitGood = t.ProfitIsGood;
        ExpenseTape = ExpenseTapeFor(t);
        ShowExpenseTape = ExpenseTape.Length > 0;
    }

    /// <summary>The one line under the bills: what they add to the sheet, and how much of it was written in
    /// yen - because yen rows carry their own rate and are worth saying so out loud.</summary>
    private string ExpenseTapeFor(BuyPlanTotal t)
    {
        var yen = _expenseDraft.Count(e => e.Currency == "JPY");
        if (_expenseDraft.Count == 0)
            return "";
        var text = _expenseDraft.Count == 1
            ? "One bill, " + t.ExpenseText + ", added to the goods cost."
            : _expenseDraft.Count + " bills, " + t.ExpenseText + " in all, added to the goods cost.";
        return yen > 0
            ? text + " " + yen + (yen == 1 ? " is" : " are") + " in yen, at the rate written on the row."
            : text;
    }

    private bool NeedOwner(string action)
    {
        if (_access.IsOwner)
            return true;
        _shell.Notify("Owner PIN needed to " + action + ".", true);
        return false;
    }

    private void MarkDirty()
    {
        if (_busy)
            return;
        IsDirty = true;
    }

    private decimal Rate => YenRate is > 0 ? YenRate.Value : 1m;
}
