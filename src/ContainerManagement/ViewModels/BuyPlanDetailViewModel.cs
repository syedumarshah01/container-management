using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// One order sheet. Type a row, press Add row, and the tape at the top moves: yen cost, the same
/// money in rupees at your rate, the expense figure, what the lot brings if all of it sells, and
/// the profit left after both. Nothing here touches stock.
/// </summary>
public partial class BuyPlanDetailViewModel : ViewModelBase
{
    private readonly BuyPlanService _plans;
    private readonly AccessService _access;
    private readonly IAppShell _shell;
    private readonly int _id;
    private readonly List<BuyPlanLineRow> _draft = new();
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

    [ObservableProperty] private string title = "";
    [ObservableProperty] private decimal? yenRate = BuyPlanService.SuggestedRate;
    [ObservableProperty] private decimal? expensePkr;
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private bool isDirty;

    [ObservableProperty] private string itemName = "";
    [ObservableProperty] private decimal? qty = 1;
    [ObservableProperty] private decimal? unitCostYen;
    [ObservableProperty] private decimal? unitWeightKg;
    [ObservableProperty] private decimal? salePricePkr;
    [ObservableProperty] private BuyPlanLineRow? selected;
    [ObservableProperty] private bool hasSelection;

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
    }

    partial void OnExpensePkrChanged(decimal? value)
    {
        if (!_busy)
            MarkDirty();
    }

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

        await _plans.SaveAsync(_id, Title, YenRate ?? 0, ExpensePkr ?? 0, rows);

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
        ExpensePkr = plan.ExpensePkr;
        _draft.Clear();
        _draft.AddRange(plan.Lines);
        RebuildGrid();
        Recalc();
    }

    /// <summary>
    /// Keeps the stored weight on the same 3 decimal grid the page shows, so the number in the
    /// "kg each" column really does multiply out to "Total kg".
    /// </summary>
    private static decimal Round3(decimal value) => decimal.Round(value, 3, MidpointRounding.AwayFromZero);

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
        var t = BuyPlanTotal.Build(_draft, Rate, ExpensePkr ?? 0);

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
