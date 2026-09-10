using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ContainersViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly InventoryService _inventory;
    private readonly IAppShell _shell;

    public ContainersViewModel(ReportService reports, InventoryService inventory, IAppShell shell)
    {
        _reports = reports;
        _inventory = inventory;
        _shell = shell;
    }

    public ObservableCollection<ContainerProfitRow> Rows { get; } = new();

    /// <summary>How the money handed over at creation went out - the same list the We owe page uses.</summary>
    public IReadOnlyList<string> PaidMethods { get; } = SupplierPayMethods.All;

    [ObservableProperty] private ContainerProfitRow? selected;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private string newNumber = "";
    [ObservableProperty] private string newOrigin = "China";
    // Deliberately no default: the arrival date is a fact about the shipment, not about the day the
    // entry happens to be typed in, so the form asks and waits rather than filling itself in.
    [ObservableProperty] private DateTimeOffset? newArrival;
    [ObservableProperty] private string newNotes = "";
    [ObservableProperty] private string newSupplier = "";
    [ObservableProperty] private decimal? newSupplierAmount;
    [ObservableProperty] private decimal? newPaid;
    [ObservableProperty] private string newPaidMethod = "Bank Transfer";
    [ObservableProperty] private decimal? newCartons;
    [ObservableProperty] private decimal? newCbm;
    [ObservableProperty] private decimal? newWeight;
    [ObservableProperty] private bool showAddForm;

    public override async Task LoadAsync()
    {
        var list = await _reports.GetContainerProfitsAsync();
        Rows.Clear();
        foreach (var r in list)
            Rows.Add(r);
    }

    [RelayCommand]
    private void Open()
    {
        if (Selected is not null)
            _shell.OpenContainer(Selected.ContainerId);
    }

    [RelayCommand]
    private void BeginAdd() => ShowAddForm = true;

    [RelayCommand]
    private void CancelAdd() => ShowAddForm = false;

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (NewArrival is null)
        {
            _shell.Notify("When did it arrive? Select the date.", true);
            return;
        }
        try
        {
            var c = await _inventory.CreateContainerAsync(
                NewTitle,
                NewNumber,
                NewOrigin,
                NewArrival?.DateTime,
                NewNotes,
                "PKR",
                1,
                null,
                NewCartons,
                NewCbm,
                NewWeight,
                NewSupplier,
                NewSupplierAmount ?? 0,
                NewPaid ?? 0,
                NewPaidMethod);
            var paid = NewPaid ?? 0;
            // The box is read as what is still owed, so the paid-now money never nets it down: this line
            // has to say the same thing the We owe page will say a second later.
            var owed = Money.Round(NewSupplierAmount ?? 0);
            var what = $"Container '{c.Title}' created.";
            if (paid > 0)
                what += owed > 0.009m
                    ? $" {Money.Pkr(paid)} paid by {NewPaidMethod}, {Money.Pkr(owed)} still owed."
                    : $" {Money.Pkr(paid)} paid by {NewPaidMethod}, supplier settled.";
            else if (owed > 0.009m)
                what += $" {Money.Pkr(owed)} owed on this container.";
            what += " Add items on the next screen.";
            _shell.Notify(what);
            NewTitle = "";
            NewNumber = "";
            NewArrival = null;
            NewNotes = "";
            NewSupplier = "";
            NewSupplierAmount = 0;
            NewPaid = null;
            NewCartons = null;
            NewCbm = null;
            NewWeight = null;
            ShowAddForm = false;
            await LoadAsync();
            _shell.OpenContainer(c.Id);
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }
}
