using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// Every saved order sheet, with its totals. Creating one only needs a name — the rate and the
/// expense figure are typed on the sheet itself.
/// </summary>
public partial class BuyPlansViewModel : ViewModelBase
{
    private readonly BuyPlanService _plans;
    private readonly AccessService _access;
    private readonly IAppShell _shell;

    public BuyPlansViewModel(BuyPlanService plans, AccessService access, IAppShell shell)
    {
        _plans = plans;
        _access = access;
        _shell = shell;
    }

    public ObservableCollection<BuyPlanRow> Rows { get; } = new();

    [ObservableProperty] private BuyPlanRow? selected;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private bool showAddForm;
    [ObservableProperty] private bool hasSelection;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool confirmDelete;
    [ObservableProperty] private string deleteLabel = "Delete";
    [ObservableProperty] private bool isOwner;

    public override async Task LoadAsync()
    {
        IsOwner = _access.IsOwner;
        var keepId = Selected?.Id;
        var list = await _plans.ListAsync();

        Rows.Clear();
        foreach (var r in list)
            Rows.Add(r);
        Selected = keepId is int id ? Rows.FirstOrDefault(r => r.Id == id) : null;
    }

    partial void OnSelectedChanged(BuyPlanRow? value)
    {
        HasSelection = value is not null;
        CanEdit = HasSelection && IsOwner;
        ConfirmDelete = false;
    }

    /// <summary>
    /// Two taps instead of a tick box: the button arms itself, then deletes. Nothing can be
    /// removed by one stray click, and the row stays free of extra widgets.
    /// </summary>
    partial void OnConfirmDeleteChanged(bool value) => DeleteLabel = value ? "Tap again to delete" : "Delete";

    [RelayCommand]
    private void BeginAdd()
    {
        if (!NeedOwner("add an order sheet"))
            return;
        ShowAddForm = true;
    }

    [RelayCommand]
    private void CancelAdd() => ShowAddForm = false;

    [RelayCommand]
    private void Open()
    {
        if (Selected is not null)
            _shell.OpenBuyPlan(Selected.Id);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!NeedOwner("add an order sheet"))
            return;
        try
        {
            var plan = await _plans.CreateAsync(NewTitle);
            NewTitle = "";
            ShowAddForm = false;
            await LoadAsync();
            _shell.OpenBuyPlan(plan.Id);
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task DuplicateAsync()
    {
        if (!NeedOwner("copy an order sheet"))
            return;
        if (Selected is null)
        {
            _shell.Notify("Pick a sheet first.", true);
            return;
        }
        try
        {
            var copy = await _plans.DuplicateAsync(Selected.Id);
            await LoadAsync();
            Selected = Rows.FirstOrDefault(r => r.Id == copy.Id);
            _shell.Notify("Copied.");
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!NeedOwner("delete an order sheet"))
            return;
        if (Selected is null)
        {
            _shell.Notify("Pick a sheet first.", true);
            return;
        }
        if (!ConfirmDelete)
        {
            ConfirmDelete = true;
            _shell.Notify("This deletes the sheet and its rows. Tap Delete again to be sure.", true);
            return;
        }
        try
        {
            var gone = Selected.TitleText;
            await _plans.DeleteAsync(Selected.Id);
            ConfirmDelete = false;
            await LoadAsync();
            _shell.Notify(gone + " deleted.");
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    private bool NeedOwner(string action)
    {
        if (_access.IsOwner)
            return true;
        _shell.Notify("Owner PIN needed to " + action + ".", true);
        return false;
    }
}
