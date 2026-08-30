using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
using ContainerManagement.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ContainerManagement.ViewModels;

public partial class MainViewModel : ObservableObject, IAppShell
{
    private readonly IServiceProvider _services;
    private readonly AccessService _access;
    private readonly LicenseService _license;
    private bool _suppressNav;

    public MainViewModel(IServiceProvider services, AccessService access, LicenseService license)
    {
        _services = services;
        _access = access;
        _license = license;
        NavItems =
        [
            new NavItem("Home", "dash"),
            new NavItem("Containers", "containers"),
            new NavItem("Stock", "inventory"),
            new NavItem("Item sales", "itemsales"),
            new NavItem("Sell", "newsale"),
            new NavItem("Sales", "sales"),
            new NavItem("Customers", "customers"),
            new NavItem("To collect", "market"),
            new NavItem("Cash", "cash"),
            new NavItem("Profit", "profit"),
            new NavItem("Backup", "backup"),
            new NavItem("Settings", "settings")
        ];
        _access.UnlockOpen();
        BrandName = _license.BusinessName;
        NeedsActivation = !_license.IsActivated;
        NeedsLicenseLock = _license.IsActivated && _license.IsPaused;
        LockReason = _license.LockReason;
        NeedsPin = !NeedsActivation && !NeedsLicenseLock && _access.PinRequired && !_access.Unlocked;
    }

    public IReadOnlyList<NavItem> NavItems { get; }
    public bool IsOwner => _access.IsOwner;
    public string DatabasePath => DbPaths.DatabaseFile;

    [ObservableProperty] private NavItem? selectedNav;
    [ObservableProperty] private ViewModelBase? currentPage;
    [ObservableProperty] private string status = "Ready";
    [ObservableProperty] private bool isError;
    [ObservableProperty] private bool needsPin;
    [ObservableProperty] private string pinInput = "";
    [ObservableProperty] private string pinHint = "Enter PIN";
    [ObservableProperty] private string brandName = "CargoKhata";
    [ObservableProperty] private bool needsActivation;
    [ObservableProperty] private bool needsLicenseLock;
    [ObservableProperty] private string lockReason = "";
    [ObservableProperty] private string licenseKeyInput = "";
    [ObservableProperty] private string licenseHint = "Paste the license key for this shop.";
    [ObservableProperty] private bool showSellerForm;
    [ObservableProperty] private string vendorPin = "";
    [ObservableProperty] private string issueBusinessName = "";
    [ObservableProperty] private string issuedKey = "";

    public void Start()
    {
        if (NeedsActivation || NeedsLicenseLock || NeedsPin)
            return;
        BrandName = _license.BusinessName;
        SelectedNav = NavItems[0];
        RefreshRoleStatus();
        _ = CheckLicenseOnlineAsync();
    }

    [RelayCommand]
    private void Unlock()
    {
        if (_access.TryUnlock(PinInput))
        {
            PinInput = "";
            NeedsPin = false;
            Start();
        }
        else
        {
            PinHint = "Wrong PIN";
            PinInput = "";
        }
    }

    [RelayCommand]
    private void Activate()
    {
        if (_license.TryActivate(LicenseKeyInput, out var error))
        {
            LicenseKeyInput = "";
            IssuedKey = "";
            FinishLicense();
        }
        else
        {
            LicenseHint = error;
        }
    }

    [RelayCommand] private void ToggleSellerForm() => ShowSellerForm = !ShowSellerForm;

    [RelayCommand]
    private void IssueLicense()
    {
        if (_license.TryIssueAndActivate(VendorPin, IssueBusinessName, out var key, out var error))
        {
            IssuedKey = key;
            VendorPin = "";
            FinishLicense();
            Status = "Licensed for " + _license.BusinessName + ". Copy the key if you need a second PC.";
        }
        else
        {
            LicenseHint = error;
        }
    }

    private void FinishLicense()
    {
        BrandName = _license.BusinessName;
        NeedsActivation = false;
        NeedsLicenseLock = _license.IsPaused;
        LockReason = _license.LockReason;
        NeedsPin = _access.PinRequired && !_access.Unlocked;
        if (!NeedsPin && !NeedsLicenseLock)
            Start();
    }

    private async Task CheckLicenseOnlineAsync()
    {
        try
        {
            await _license.RefreshRemoteAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_license.IsPaused)
                {
                    LockReason = _license.LockReason;
                    NeedsLicenseLock = true;
                }
            });
        }
        catch
        {
            /* stay open if the status file cannot be reached */
        }
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (_suppressNav || value is null || NeedsPin || NeedsActivation || NeedsLicenseLock)
            return;
        Show(Create(value.Key));
    }

    public void Notify(string message, bool error = false)
    {
        Status = message;
        IsError = error;
    }

    public void GoDashboard() => Select("dash");
    public void GoContainers() => Select("containers");
    public void GoInventory() => Select("inventory");
    public void GoNewSale() => Select("newsale");
    public void GoSales() => Select("sales");
    public void GoCustomers() => Select("customers");
    public void GoReceivables() => Select("market");
    public void GoProfit() => Select("profit");
    public void GoBackup() => Select("backup");
    public void GoCash() => Select("cash");
    public void GoSettings() => Select("settings");

    public void OpenContainer(int id)
    {
        var vm = ActivatorUtilities.CreateInstance<ContainerDetailViewModel>(_services, id);
        ShowWithoutNav(vm);
    }

    public void OpenSale(int id)
    {
        var vm = ActivatorUtilities.CreateInstance<SaleDetailViewModel>(_services, id);
        ShowWithoutNav(vm);
    }

    public void EditSale(int id)
    {
        var vm = _services.GetRequiredService<NewSaleViewModel>();
        vm.EditingSaleId = id;
        ShowWithoutNav(vm);
    }

    public void OpenCustomer(int id)
    {
        var vm = ActivatorUtilities.CreateInstance<CustomerDetailViewModel>(_services, id);
        ShowWithoutNav(vm);
    }

    private void Select(string key)
    {
        var item = NavItems.First(n => n.Key == key);
        if (ReferenceEquals(SelectedNav, item))
            Show(Create(key));
        else
            SelectedNav = item;
    }

    private void ShowWithoutNav(ViewModelBase vm)
    {
        _suppressNav = true;
        Show(vm);
        _suppressNav = false;
    }

    private void Show(ViewModelBase vm)
    {
        CurrentPage = vm;
        _ = LoadSafe(vm);
    }

    private async Task LoadSafe(ViewModelBase vm)
    {
        try
        {
            await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            Notify(ex.Message, true);
        }
    }

    private void RefreshRoleStatus()
    {
        if (_access.IsStaff)
            Notify("Staff mode — costs, restore, and settings stay with the owner.");
    }

    private ViewModelBase Create(string key) => key switch
    {
        "dash" => _services.GetRequiredService<DashboardViewModel>(),
        "containers" => _services.GetRequiredService<ContainersViewModel>(),
        "inventory" => _services.GetRequiredService<InventoryViewModel>(),
        "itemsales" => _services.GetRequiredService<ItemSalesViewModel>(),
        "newsale" => _services.GetRequiredService<NewSaleViewModel>(),
        "sales" => _services.GetRequiredService<SalesViewModel>(),
        "customers" => _services.GetRequiredService<CustomersViewModel>(),
        "market" => _services.GetRequiredService<ReceivablesViewModel>(),
        "profit" => _services.GetRequiredService<ProfitViewModel>(),
        "backup" => _services.GetRequiredService<BackupViewModel>(),
        "cash" => _services.GetRequiredService<CashViewModel>(),
        "settings" => _services.GetRequiredService<SettingsViewModel>(),
        _ => _services.GetRequiredService<DashboardViewModel>()
    };
}
