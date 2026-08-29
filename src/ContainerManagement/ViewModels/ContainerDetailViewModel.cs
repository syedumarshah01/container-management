using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
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
    [ObservableProperty] private string supplierOwed = "—";

    [ObservableProperty] private string goodsName = "";
    [ObservableProperty] private string goodsSku = "";
    [ObservableProperty] private string goodsUnit = "pcs";
    [ObservableProperty] private decimal? goodsQty = 1;
    [ObservableProperty] private decimal? goodsInStock;
    [ObservableProperty] private decimal? goodsCost;
    [ObservableProperty] private decimal? goodsCartons;
    [ObservableProperty] private decimal? countIs;
    [ObservableProperty] private string adjustReason = "";
    [ObservableProperty] private ContainerItemRow? selectedItem;

    [ObservableProperty] private string expenseCategory = "Sea Freight";
    [ObservableProperty] private decimal? expenseAmount;
    [ObservableProperty] private DateTimeOffset? expenseDate = DateTimeOffset.Now;
    [ObservableProperty] private string expenseNotes = "";
    [ObservableProperty] private ContainerExpense? selectedExpense;

    [ObservableProperty] private decimal? editCartons;
    [ObservableProperty] private decimal? editCbm;
    [ObservableProperty] private decimal? editWeight;
    [ObservableProperty] private string editSupplier = "";
    [ObservableProperty] private decimal? editSupplierAmount;
    [ObservableProperty] private bool isClosed;
    [ObservableProperty] private bool isOwner;

    [ObservableProperty] private decimal? supplierPay;
    [ObservableProperty] private string supplierPayMethod = "TT";

    public ObservableCollection<ContainerItemRow> Items { get; } = new();
    public ObservableCollection<ContainerExpense> Expenses { get; } = new();
    public IReadOnlyList<string> UnitOptions { get; } = Units.All;
    public IReadOnlyList<string> CategoryOptions { get; } = ExpenseCategories.All;
    public IReadOnlyList<string> SupplierMethods { get; } = SupplierPayMethods.All;

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
        Subtitle = $"{c.ContainerNumber ?? "No number"} · {c.Origin} · arrival {c.ArrivalDate:dd MMM yyyy}";
        EditCartons = c.Cartons;
        EditCbm = c.Cbm;
        EditWeight = c.WeightKg;
        EditSupplier = c.Supplier?.Name ?? "";
        EditSupplierAmount = c.SupplierAmount;
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
        SupplierOwed = Money.Pkr(await _inventory.SupplierBalanceAsync(_id));
    }

    partial void OnSelectedItemChanged(ContainerItemRow? value)
    {
        if (_loadingSelection || value is null) return;
        GoodsName = value.Name;
        GoodsSku = value.Sku;
        GoodsUnit = value.Unit;
        GoodsQty = value.Purchased;
        GoodsInStock = value.InStock;
        GoodsCost = value.ForeignCost;
        GoodsCartons = value.Cartons;
        CountIs = value.InStock;
    }

    partial void OnSelectedExpenseChanged(ContainerExpense? value)
    {
        if (_loadingSelection || value is null) return;
        ExpenseCategory = value.Category;
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
                null, GoodsCartons, null, null, null);
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
            var remaining = GoodsInStock ?? SelectedItem.InStock;
            await _inventory.UpdateGoodsAsync(
                SelectedItem.Id, GoodsName, GoodsUnit, GoodsSku, GoodsQty ?? 0, remaining, GoodsCost ?? 0,
                GoodsCartons, null, null, SelectedItem.PhotoPath);
            _shell.Notify("Item updated.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task AdjustAsync()
    {
        if (SelectedItem is null)
        {
            _shell.Notify("Select an item, then type the physical count.", true);
            return;
        }
        try
        {
            await _inventory.AdjustStockAsync(SelectedItem.Id, CountIs ?? 0, AdjustReason);
            _shell.Notify("Stock count saved.");
            AdjustReason = "";
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task PickPhotoAsync()
    {
        if (SelectedItem is null)
        {
            _shell.Notify("Select an item first.", true);
            return;
        }
        try
        {
            var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (window is null) return;
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Photo of this item",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });
            if (files.Count == 0) return;
            var src = files[0].TryGetLocalPath();
            if (src is null) return;
            var dest = Path.Combine(DbPaths.PhotosDirectory, $"item-{SelectedItem.Id}{Path.GetExtension(src)}");
            File.Copy(src, dest, overwrite: true);
            await _inventory.UpdateGoodsAsync(
                SelectedItem.Id, GoodsName, GoodsUnit, GoodsSku, GoodsQty ?? SelectedItem.Purchased,
                GoodsInStock ?? SelectedItem.InStock, GoodsCost ?? SelectedItem.ForeignCost,
                GoodsCartons, null, null, dest);
            _shell.Notify("Photo saved.");
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
            await _inventory.AddExpenseAsync(_id, ExpenseDate?.DateTime ?? DateTime.Today, ExpenseCategory, ExpenseAmount ?? 0, ExpenseNotes);
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
                SelectedExpense.Id, ExpenseDate?.DateTime ?? DateTime.Today, ExpenseCategory, ExpenseAmount ?? 0, ExpenseNotes);
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
            _shell.Notify("Expense removed.");
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
                _id, EditSupplier, EditSupplierAmount ?? 0, EditCartons, EditCbm, EditWeight);
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
            _shell.Notify(IsClosed ? "Container re-opened." : "Container closed.");
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task PaySupplierAsync()
    {
        if (!_access.IsOwner) { _shell.Notify("Owner PIN needed to record supplier payment.", true); return; }
        try
        {
            await _inventory.UpdateImportDetailsAsync(
                _id, EditSupplier, EditSupplierAmount ?? 0, EditCartons, EditCbm, EditWeight);
            await _inventory.PaySupplierAsync(_id, DateTime.Today, SupplierPay ?? 0, SupplierPayMethod, null);
            _shell.Notify("Supplier payment saved.");
            SupplierPay = 0;
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand] private void SellFromHere() => _shell.GoNewSale();
    [RelayCommand] private void Back() => _shell.GoContainers();

    private void ClearGoodsForm()
    {
        GoodsName = "";
        GoodsSku = "";
        GoodsQty = 1;
        GoodsInStock = null;
        GoodsCost = 0;
        GoodsCartons = null;
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
    public decimal? Cartons { get; set; }
    public string? PhotoPath { get; set; }
    public string UnitCostText => Money.Pkr(UnitCost);
}
