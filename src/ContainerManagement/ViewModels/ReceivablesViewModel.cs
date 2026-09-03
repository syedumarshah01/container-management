using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ReceivablesViewModel : ViewModelBase
{
    private readonly LedgerService _ledger;
    private readonly IAppShell _shell;

    public ReceivablesViewModel(LedgerService ledger, IAppShell shell)
    {
        _ledger = ledger;
        _shell = shell;
    }

    public ObservableCollection<ReceivableRow> Due { get; } = new();
    [ObservableProperty] private ReceivableRow? selected;
    [ObservableProperty] private string outstanding = "—";
    [ObservableProperty] private string advances = "—";
    [ObservableProperty] private string net = "—";

    public override async Task LoadAsync()
    {
        var rows = await _ledger.GetReceivablesAsync();
        var due = rows.Where(r => r.Balance > 0).ToList();
        var adv = rows.Where(r => r.Balance < 0).ToList();
        Outstanding = Money.Pkr(due.Sum(r => r.Balance));
        Advances = Money.Pkr(Math.Abs(adv.Sum(r => r.Balance)));
        Net = Money.Pkr(rows.Sum(r => r.Balance));
        Due.Clear();
        foreach (var r in due)
            Due.Add(r);
    }

    [RelayCommand]
    private void Receive()
    {
        if (Selected is not null)
            _shell.OpenCustomer(Selected.CustomerId);
    }
}
