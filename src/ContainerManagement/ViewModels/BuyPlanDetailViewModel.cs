using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// One China order sheet. Type the rows, press Add row as you go, and the boxes at the top
/// move with you: cost in yen, the same cost in rupees at your rate, what the lot brings if
/// everything sells, and the profit left after the one expense figure.
/// Nothing here moves stock — it is the paper you plan on.
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
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string supplier = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private decimal? yenRate = BuyPlanService.SuggestedRate;
    [ObservableProperty] private decimal? expensePkr;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string dirtyHint = "";

    [ObservableProperty] private string itemName = "";
    [ObservableProperty] private decimal? qty = 1;
    [ObservableProperty] private decimal? unitCostYen;
    [ObservableProperty] private decimal? unitWeightKg;
    [ObservableProperty] private decimal? salePricePkr;
    [ObservableProperty] private string lineNotes = "";
    [ObservableProperty] private BuyPlanLineRow? selected;
    [ObservableProperty] private string lineHint = "";

    [ObservableProperty] private string costYenText = "—";
    [ObservableProperty] private string costPkrText = "—";
    [ObservableProperty] private string expenseText = "—";
    [ObservableProperty] private string spendText = "—";
    [ObservableProperty] private string saleText = "—";
    [ObservableProperty] private string profitText = "—";
    [ObservableProperty] private string marginText = "—";
    [ObservableProperty] private string weightText = "—";
    [ObservableProperty] private string countText = "0 items";
    [ObservableProperty] private string rateText = "—";
    [ObservableProperty] private string profitHint = "";
    [ObservableProperty] private string profitFoot = "";
    [ObservableProperty] private string priceHint = "";
    [ObservableProperty] private bool hasPriceHint;
    [ObservableProperty] private bool profitGood = true;
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private string readOnlyHint = "";

    public override async Task LoadAsync()
    {
        IsOwner = _access.IsOwner;
        ReadOnlyHint = IsOwner ? "" : "Staff cannot change a buy plan — the owner's PIN is needed.";

        var plan = await _plans.GetAsync(_id);
        if (plan is null)
        {
            _shell.Notify("This plan is gone. It may have been deleted.", true);
            return;
        }

        _busy = true;
        LoadHeader(plan);
        _draft.Clear();
        _draft.AddRange(plan.Lines);
        RebuildGrid();
        Recalc();
        IsDirty = false;
        DirtyHint = "";
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
        if (_busy)
            return;
        MarkDirty();
        Recalc();
    }

    partial void OnTitleChanged(string value)
    {
        if (!_busy)
            MarkDirty();
    }

    partial void OnSupplierChanged(string value)
    {
        if (!_busy)
            MarkDirty();
    }

    partial void OnNotesChanged(string value)
    {
        if (!_busy)
            MarkDirty();
    }

    partial void OnSelectedItemChanged(BuyPlanLineRow? value)
    {
        if (_busy || value is null)
            return;
        _busy = true;
        ItemName = value.ItemName;
        Qty = value.Quantity;
        UnitCostYen = value.UnitCostYen;
        UnitWeightKg = value.UnitWeightKg;
        SalePricePkr = value.SalePricePkr;
        LineNotes = value.Notes ?? "";
        _busy = false;
        UpdateLineHint();
    }

    partial void OnItemNameChanged(string value) => UpdateLineHint();
    partial void OnQtyChanged(decimal? value) => UpdateLineHint();
    partial void OnUnitCostYenChanged(decimal? value) => UpdateLineHint();
    partial void OnUnitWeightKgChanged(decimal? value) => UpdateLineHint();
    partial void OnSalePricePkrChanged(decimal? value) => UpdateLineHint();

    [RelayCommand]
    private void AddLine()
    {
        if (!NeedOwner("change a buy plan"))
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
        _shell.Notify(row.ItemNameText + " put on the sheet. Press Save plan to keep it.");
    }

    [RelayCommand]
    private void UpdateLine()
    {
        if (!NeedOwner("change a buy plan"))
            return;

        if (Selected is null)
        {
            _shell.Notify("Pick a row in the table to change it.", true);
            return;
        }
        var at = _draft.IndexOf(Selected);
        if (at < 0)
        {
            _shell.Notify("That row is not on this plan any more.", true);
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
        _shell.Notify("Row updated. Press Save plan to keep it.");
    }

    [RelayCommand]
    private void RemoveLine()
    {
        if (!NeedOwner("change a buy plan"))
            return;

        if (Selected is null)
        {
            _shell.Notify("Pick a row in the table to remove it.", true);
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
    private void ClearForm()
    {
        _busy = true;
        ItemName = "";
        Qty = 1;
        UnitCostYen = null;
        UnitWeightKg = null;
        SalePricePkr = null;
        LineNotes = "";
        Selected = null;
        _busy = false;
        UpdateLineHint();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!NeedOwner("save a buy plan"))
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
                _shell.Notify("Left unsaved: " + ex.Message, true);
                return;
            }
        }
        _shell.Back();
    }

    private async Task WriteAsync()
    {
        if (!_access.IsOwner)
            throw new InvalidOperationException("Owner PIN needed to save a buy plan.");

        var rows = _draft
            .Where(r => !string.IsNullOrWhiteSpace(r.ItemName))
            .Select(r => r.ToInput())
            .ToList();

        await _plans.SaveAsync(_id, Title, Supplier, Notes, YenRate ?? 0, ExpensePkr ?? 0, rows);

        var saved = await _plans.GetAsync(_id);
        _busy = true;
        if (saved is not null)
        {
            LoadHeader(saved);
            _draft.Clear();
            _draft.AddRange(saved.Lines);
            RebuildGrid();
        }
        Recalc();
        IsDirty = false;
        DirtyHint = "";
        _busy = false;
        _shell.Notify("Plan saved.");
    }

    private void LoadHeader(BuyPlanRow plan)
    {
        Title = plan.Title;
        Supplier = plan.Supplier;
        Notes = plan.Notes;
        YenRate = plan.YenRate;
        ExpensePkr = plan.ExpensePkr;
        Subtitle = "Made " + plan.CreatedText + " · a plan only — stock and ledgers stay as they are";
    }

    private BuyPlanLineRow DraftRow() => new()
    {
        ItemName = ItemName.Trim(),
        Quantity = Qty ?? 0,
        UnitCostYen = UnitCostYen ?? 0,
        UnitWeightKg = UnitWeightKg ?? 0,
        SalePricePkr = SalePricePkr ?? 0,
        Notes = string.IsNullOrWhiteSpace(LineNotes) ? null : LineNotes.Trim(),
        YenRate = Rate
    };

    private static string? CheckRow(BuyPlanLineRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ItemName))
            return "Type the item name.";
        if (row.Quantity < 0)
            return "Quantity cannot be negative.";
        if (row.Quantity == 0)
            return "Type how many pieces.";
        if (row.UnitCostYen < 0)
            return "Yen cost cannot be negative.";
        if (row.SalePricePkr < 0)
            return "Sale price cannot be negative.";
        if (row.UnitWeightKg < 0)
            return "Weight cannot be negative.";
        return null;
    }

    /// <summary>
    /// The grid is read-only, so a row is refreshed by putting the same rows back in.
    /// Selection is kept by position so the form does not jump while you work down the sheet.
    /// </summary>
    private void RebuildGrid()
    {
        var at = Selected is null ? -1 : _draft.IndexOf(Selected);
        foreach (var row in _draft)
            row.YenRate = Rate;

        Lines.Clear();
        foreach (var row in _draft)
            Lines.Add(row);

        if (at >= 0 && at < Lines.Count)
            RestoreSelected(Lines[at]);
        else
            Selected = null;

        UpdateLineHint();
    }

    /// <summary>
    /// Puts the row back under the user's finger without overwriting what they are typing.
    /// </summary>
    private void RestoreSelected(BuyPlanLineRow row)
    {
        var wasBusy = _busy;
        _busy = true;
        Selected = row;
        _busy = wasBusy;
    }

    private void Recalc()
    {
        var t = BuyPlanTotal.Build(_draft, Rate, ExpensePkr ?? 0);

        CountText = t.ItemCountText;
        RateText = t.RateText + " per yen";
        CostYenText = t.CostYenText;
        CostPkrText = t.CostPkrText;
        ExpenseText = t.ExpenseText;
        SpendText = t.SpendText;
        SaleText = t.SaleText;
        ProfitText = t.ProfitText;
        MarginText = t.MarginText;
        WeightText = t.WeightText;
        ProfitGood = t.ProfitIsGood;
        ProfitHint = t.ItemCount == 0
            ? "Add rows and the profit works itself out."
            : $"All of it in for {t.SpendText}, all of it out for {t.SaleText}.";

        ProfitFoot = $"About {t.ProfitYenText} in yen at {t.RateText} · margin {t.MarginText}";

        var noPrice = _draft.Count(r => r.SalePricePkr <= 0.009m);
        HasPriceHint = noPrice > 0;
        PriceHint = noPrice == 0
            ? ""
            : noPrice + (noPrice == 1 ? " row has" : " rows have") + " no sale price yet — "
              + (noPrice == 1 ? "it counts" : "they count") + " as zero below.";
    }

    private void UpdateLineHint()
    {
        if (_busy)
            return;

        var row = DraftRow();
        // the quantity box starts at 1, so "nothing typed yet" is judged by the money fields
        var nothingTyped = string.IsNullOrWhiteSpace(row.ItemName) && row.UnitCostYen <= 0 && row.SalePricePkr <= 0;
        if (nothingTyped)
        {
            LineHint = "";
            return;
        }

        LineHint = $"This row: {row.CostYenText} = {row.CostPkrText} in ({row.CostPerPiecePkrText} a piece) · "
                   + $"{row.SaleTotalText} out if it all sells · {row.ProfitText} before expenses · "
                   + $"margin {row.MarginText} · {row.TotalWeightText} kg";
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
        DirtyHint = "Changed — press Save plan.";
    }

    private decimal Rate => YenRate is > 0 ? YenRate.Value : 1m;
}
