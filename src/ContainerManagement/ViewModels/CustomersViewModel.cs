using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class CustomersViewModel : ViewModelBase
{
    private readonly LedgerService _ledger;
    private readonly IAppShell _shell;
    private List<ReceivableRow> _all = new();

    public CustomersViewModel(LedgerService ledger, IAppShell shell)
    {
        _ledger = ledger;
        _shell = shell;
    }

    public ObservableCollection<ReceivableRow> Rows { get; } = new();

    [ObservableProperty] private ReceivableRow? selected;
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string newName = "";
    [ObservableProperty] private string newPhone = "";
    [ObservableProperty] private string newAddress = "";
    [ObservableProperty] private string newNotes = "";

    public override async Task LoadAsync()
    {
        _all = await _ledger.GetReceivablesAsync();
        ApplyFilter();
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Open()
    {
        if (Selected is not null)
            _shell.OpenCustomer(Selected.CustomerId);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        try
        {
            var c = await _ledger.CreateCustomerAsync(NewName, NewPhone, NewAddress, NewNotes);
            _shell.Notify($"Customer '{c.Name}' saved.");
            NewName = NewPhone = NewAddress = NewNotes = "";
            await LoadAsync();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    private void ApplyFilter()
    {
        IEnumerable<ReceivableRow> src = _all;
        if (!string.IsNullOrWhiteSpace(Query))
        {
            src = _all.Where(r =>
                r.Name.Contains(Query, StringComparison.OrdinalIgnoreCase)
                || (r.Phone ?? "").Contains(Query));
        }
        Rows.Clear();
        foreach (var r in src)
            Rows.Add(r);
    }
}
