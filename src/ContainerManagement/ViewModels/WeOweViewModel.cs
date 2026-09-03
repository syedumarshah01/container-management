using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class WeOweViewModel : ViewModelBase
{
    private readonly CashBookService _cash;
    private readonly InventoryService _inventory;
    private readonly IAppShell _shell;
    private bool _ready;

    public WeOweViewModel(CashBookService cash, InventoryService inventory, IAppShell shell)
    {
        _cash = cash;
        _inventory = inventory;
        _shell = shell;
        _ready = true;
    }

    public ObservableCollection<WeOweRow> Rows { get; } = new();
    public ObservableCollection<PayContainerOption> Containers { get; } = new();

    [ObservableProperty] private string totalOwed = Money.Pkr(0);
    [ObservableProperty] private WeOweRow? selected;
    [ObservableProperty] private PayContainerOption? payContainer;
    [ObservableProperty] private DateTimeOffset? payDate = DateTimeOffset.Now;
    [ObservableProperty] private decimal? payAmount;
    [ObservableProperty] private string weOweThem = "—";

    public override async Task LoadAsync()
    {
        var keepPay = PayContainer?.Id ?? Selected?.ContainerId;
        var targets = await _cash.SupplierContainersAsync();

        Rows.Clear();
        Containers.Clear();
        foreach (var t in targets.OrderByDescending(x => x.Owed).ThenBy(x => x.SupplierName))
        {
            Rows.Add(new WeOweRow
            {
                ContainerId = t.Id,
                SupplierName = t.SupplierName,
                ContainerTitle = t.ContainerTitle,
                Owed = t.Owed
            });
            Containers.Add(new PayContainerOption
            {
                Id = t.Id,
                Label = t.Label,
                SupplierName = t.SupplierName,
                Owed = t.Owed
            });
        }

        TotalOwed = Money.Pkr(targets.Where(t => t.Owed > 0).Sum(t => t.Owed));
        PayContainer = Containers.FirstOrDefault(c => c.Id == keepPay) ?? Containers.FirstOrDefault();
        Selected = Rows.FirstOrDefault(r => r.ContainerId == PayContainer?.Id);
        ShowOwed();
    }

    partial void OnSelectedChanged(WeOweRow? value)
    {
        if (!_ready || value is null) return;
        PayContainer = Containers.FirstOrDefault(c => c.Id == value.ContainerId);
    }

    partial void OnPayContainerChanged(PayContainerOption? value)
    {
        if (_ready) ShowOwed();
    }

    private void ShowOwed()
    {
        if (PayContainer is null)
        {
            WeOweThem = "—";
            return;
        }
        var owed = PayContainer.Owed;
        if (owed > 0.009m)
            WeOweThem = Money.Pkr(owed);
        else if (owed < -0.009m)
            WeOweThem = "Paid extra " + Money.Pkr(-owed);
        else
            WeOweThem = Money.Pkr(0);
    }

    [RelayCommand]
    private async Task PaySupplierAsync()
    {
        if (PayContainer is null)
        {
            _shell.Notify("Pick a supplier.", true);
            return;
        }
        try
        {
            await _inventory.PaySupplierAsync(
                PayContainer.Id,
                PayDate?.DateTime ?? DateTime.Today,
                PayAmount ?? 0,
                "Bank Transfer",
                null);
            _shell.Notify("Supplier payment taken off cash.");
            PayAmount = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }
}

public class WeOweRow
{
    public int ContainerId { get; set; }
    public string SupplierName { get; set; } = "";
    public string ContainerTitle { get; set; } = "";
    public decimal Owed { get; set; }
    public string OwedText => Owed < -0.009m
        ? "Paid extra " + Money.Pkr(-Owed)
        : Money.Pkr(Owed);
}

public class PayContainerOption
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public decimal Owed { get; set; }
    public override string ToString() => Label;
}
