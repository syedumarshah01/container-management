using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class CashViewModel : ViewModelBase
{
    private readonly CashService _cash;
    private readonly IAppShell _shell;

    public CashViewModel(CashService cash, IAppShell shell)
    {
        _cash = cash;
        _shell = shell;
    }

    public ObservableCollection<CashBookRow> Rows { get; } = new();
    public IReadOnlyList<string> Methods { get; } = PaymentMethods.All;
    public IReadOnlyList<string> Directions { get; } = ["In", "Out"];

    [ObservableProperty] private DateTimeOffset? day = DateTimeOffset.Now;
    [ObservableProperty] private string cashIn = "—";
    [ObservableProperty] private string bankIn = "—";
    [ObservableProperty] private string jazzIn = "—";
    [ObservableProperty] private string easyIn = "—";
    [ObservableProperty] private string outTotal = "—";
    [ObservableProperty] private string net = "—";
    [ObservableProperty] private decimal? moveAmount;
    [ObservableProperty] private string moveMethod = "Cash";
    [ObservableProperty] private string moveDirection = "Out";
    [ObservableProperty] private string moveNotes = "";

    public override Task LoadAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var (rows, inn, outing) = await _cash.GetDayAsync(Day?.DateTime ?? DateTime.Today);
        Rows.Clear();
        foreach (var r in rows)
            Rows.Add(r);
        CashIn = Money.Pkr(Get(inn, "Cash"));
        BankIn = Money.Pkr(Get(inn, "Bank Transfer"));
        JazzIn = Money.Pkr(Get(inn, "JazzCash"));
        EasyIn = Money.Pkr(Get(inn, "EasyPaisa"));
        var totalIn = inn.Values.Sum();
        OutTotal = Money.Pkr(outing);
        Net = Money.Pkr(totalIn - outing);
    }

    [RelayCommand]
    private async Task AddMoveAsync()
    {
        try
        {
            await _cash.AddMovementAsync(Day?.DateTime ?? DateTime.Today, MoveDirection, MoveMethod, MoveAmount ?? 0, MoveNotes);
            MoveAmount = 0;
            MoveNotes = "";
            await RefreshAsync();
            _shell.Notify("Cash book updated.");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    partial void OnDayChanged(DateTimeOffset? value) => _ = RefreshAsync();

    private static decimal Get(Dictionary<string, decimal> map, string key) =>
        map.TryGetValue(key, out var v) ? v : 0;
}
