using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ReceivablesViewModel : ViewModelBase
{
    private readonly LedgerService _ledger;
    private readonly ReportService _reports;
    private readonly IAppShell _shell;

    public ReceivablesViewModel(LedgerService ledger, ReportService reports, IAppShell shell)
    {
        _ledger = ledger;
        _reports = reports;
        _shell = shell;
    }

    public ObservableCollection<ReceivableRow> Due { get; } = new();
    [ObservableProperty] private ReceivableRow? selected;
    [ObservableProperty] private string outstanding = "—";

    /// <summary>The containers whose goods are out there, the one owing most at the head of the list. The
    /// figures are the containers' page's own rows, read as they are: this page asks who owes, and a shop
    /// waiting on one shipment's money wants its figure without going hunting for it.</summary>
    public ObservableCollection<ContainerProfitRow> Lots { get; } = new();
    [ObservableProperty] private ContainerProfitRow? selectedLot;
    [ObservableProperty] private string lotLabel = "";
    [ObservableProperty] private string lotFigure = "";
    public bool HasLot => !string.IsNullOrEmpty(LotFigure);
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

        var lots = (await _reports.GetContainerProfitsAsync()).ToList();
        var keep = SelectedLot?.ContainerId ?? 0;
        Lots.Clear();
        Lots.Add(new ContainerProfitRow { ContainerId = 0, Title = "All containers" });
        foreach (var lot in lots.OrderByDescending(l => l.InMarket).ThenBy(l => l.Title))
            Lots.Add(lot);
        SelectedLot = Lots.FirstOrDefault(l => l.ContainerId == keep) ?? Lots[0];
    }

    partial void OnSelectedLotChanged(ContainerProfitRow? value)
    {
        if (value is null || value.ContainerId == 0)
        {
            LotLabel = "";
            LotFigure = "";
        }
        else
        {
            LotLabel = "In the market on " + value.Title
                       + (string.IsNullOrWhiteSpace(value.ContainerNumber) ? "" : " · " + value.ContainerNumber);
            // The row's own text, not a second formatting of the figure: the number on this page is the number
            // on the container's page, word for word.
            LotFigure = value.InMarketText;
        }
        OnPropertyChanged(nameof(HasLot));
    }

    [RelayCommand]
    private void Receive()
    {
        if (Selected is not null)
            _shell.OpenCustomer(Selected.CustomerId);
    }
}
