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

    [ObservableProperty] private ContainerProfitRow? selected;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private string newNumber = "";
    [ObservableProperty] private string newOrigin = "China";
    [ObservableProperty] private DateTimeOffset? newArrival = DateTimeOffset.Now;
    [ObservableProperty] private string newNotes = "";
    [ObservableProperty] private string newSupplier = "";
    [ObservableProperty] private decimal? newSupplierAmount;
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
                NewSupplierAmount ?? 0);
            _shell.Notify($"Container '{c.Title}' created. Add items on the next screen.");
            NewTitle = "";
            NewNumber = "";
            NewNotes = "";
            NewSupplier = "";
            NewSupplierAmount = 0;
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
