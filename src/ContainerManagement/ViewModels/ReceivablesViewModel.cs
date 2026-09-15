using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ReceivablesViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly LedgerService _ledger;
    private readonly ReportService _reports;
    private readonly IAppShell _shell;

    public ReceivablesViewModel(LedgerService ledger, ReportService reports, IAppShell shell, PrintService print)
    {
        _print = print;
        _ledger = ledger;
        _reports = reports;
        _shell = shell;
    }

    public ObservableCollection<ReceivableRow> Due { get; } = new();
    private List<ReceivableRow> _due = new();

    [ObservableProperty] private string query = "";

    partial void OnQueryChanged(string value) => ApplyFilter();

    /// <summary>What the list holds, narrowed by what was typed. The money in the cards above is the market's
    /// own and is read before this runs, so a search box can never move a total.</summary>
    private void ApplyFilter()
    {
        IEnumerable<ReceivableRow> src = _due;
        var q = Query?.Trim();
        if (!string.IsNullOrEmpty(q))
            src = _due.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || (r.Phone ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || r.Aging.Contains(q, StringComparison.OrdinalIgnoreCase));
        Due.Clear();
        foreach (var r in src)
            Due.Add(r);
    }
    [ObservableProperty] private ReceivableRow? selected;
    [ObservableProperty] private string outstanding = "—";

    /// <summary>The containers whose goods are out there, the one owing most at the head of the list. The
    /// figures are the containers' page's own rows, read as they are: this page asks who owes, and a shop
    /// waiting on one shipment's money wants its figure without going hunting for it.</summary>
    public ObservableCollection<ContainerProfitRow> Lots { get; } = new();

    /// <summary>The picker's own list: what <see cref="Lots"/> holds after the container search is applied. The
    /// page reads its figures off <see cref="Lots"/>, so narrowing a dropdown cannot narrow a total.</summary>
    public ObservableCollection<ContainerProfitRow> LotChoices { get; } = new();

    [ObservableProperty] private ContainerProfitRow? selectedLot;
    [ObservableProperty] private string lotQuery = "";
    [ObservableProperty] private string lotLabel = "";
    [ObservableProperty] private string lotFigure = "";
    [ObservableProperty] private string lotCollected = "";
    public bool HasLot => !string.IsNullOrEmpty(LotFigure);

    partial void OnLotQueryChanged(string value) => ApplyLotFilter();

    /// <summary>
    /// Which containers the dropdown offers. The one it is pointing at stays in the list and stays chosen even
    /// when it stops matching the words: a picker that drops your choice mid-typing has changed the figure on the
    /// page, not only the list it was picked from.
    /// </summary>
    private void ApplyLotFilter()
    {
        var keep = SelectedLot;
        var q = LotQuery?.Trim();
        LotChoices.Clear();
        foreach (var l in Lots)
        {
            if (string.IsNullOrEmpty(q)
                || l.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (l.ContainerNumber ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || l.Origin.Contains(q, StringComparison.OrdinalIgnoreCase))
                LotChoices.Add(l);
        }
        if (keep is not null && LotChoices.All(l => l.ContainerId != keep.ContainerId))
            LotChoices.Insert(0, keep);
        if (keep is not null && !ReferenceEquals(SelectedLot, keep))
            SelectedLot = keep;
    }
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
        _due = due;
        ApplyFilter();

        var lots = (await _reports.GetContainerProfitsAsync()).ToList();
        var keep = SelectedLot?.ContainerId ?? 0;
        Lots.Clear();
        Lots.Add(new ContainerProfitRow { ContainerId = 0, Title = "All containers" });
        foreach (var lot in lots.OrderByDescending(l => l.InMarket).ThenBy(l => l.Title))
            Lots.Add(lot);
        SelectedLot = Lots.FirstOrDefault(l => l.ContainerId == keep) ?? Lots[0];
        ApplyLotFilter();
    }

    partial void OnSelectedLotChanged(ContainerProfitRow? value)
    {
        if (value is null || value.ContainerId == 0)
        {
            LotLabel = "";
            LotFigure = "";
            LotCollected = "";
        }
        else
        {
            LotLabel = "On " + value.Title
                       + (string.IsNullOrWhiteSpace(value.ContainerNumber) ? "" : " · " + value.ContainerNumber);
            // The row's own texts, not a second formatting of the figures: the money on this page is the money on
            // the container's page, word for word, and what came in and what is still out are read from the same
            // place rather than one of them being worked out from the other.
            LotFigure = value.InMarketText;
            LotCollected = value.CollectedText;
        }
        OnPropertyChanged(nameof(HasLot));
    }

    [RelayCommand]
    private void Print()
    {
        var rows = Due.Select(r => new[]
        {
            r.Name, r.Phone ?? "", r.BalanceText, r.OldestDueText, r.Aging, r.LastPaymentText,
        }).Cast<IReadOnlyList<string>>().ToList();
        _print.PrintTable("to-collect.html", "To collect", null,
            new[] { "Customer", "Phone", "Owes", "Oldest due", "Age", "Last money in" },
            rows, new[] { "Total to chase", "", Outstanding, "", "", "" }, 2);
        _shell.Notify("Printed from the page you were on.");
    }

    [RelayCommand]
    private void Receive()
    {
        if (Selected is not null)
            _shell.OpenCustomer(Selected.CustomerId);
    }
}
