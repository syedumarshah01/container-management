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
    private readonly Dictionary<string, ViewModelBase> _navPages = new();
    private readonly Stack<ViewModelBase> _back = new();
    private static readonly TimeSpan LicenseCheckEvery = TimeSpan.FromHours(12);
    private bool _checkingLicense;
    private DispatcherTimer? _licenseTimer;
    private DateTime _lastLicenseCheckUtc = DateTime.MinValue;

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
            new NavItem("Sell", "newsale"),
            new NavItem("Sales", "sales"),
            new NavItem("Customers", "customers"),
            new NavItem("To collect", "market"),
            new NavItem("Item sales", "itemsales"),
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
    [ObservableProperty] private string brandName = AppInfo.ProductName;
    [ObservableProperty] private bool needsActivation;
    [ObservableProperty] private bool needsLicenseLock;
    [ObservableProperty] private string lockReason = "";
    [ObservableProperty] private string licenseKeyInput = "";
    [ObservableProperty] private string licenseHint = "This copy of ProBooks will take the shop name you stamp at install.";
    [ObservableProperty] private bool showSellerForm;
    [ObservableProperty] private string vendorPin = "";
    [ObservableProperty] private string issueBusinessName = "";
    [ObservableProperty] private string issuedKey = "";

    public void Start()
    {
        if (_license.IsActivated)
            EnsureLicenseWatch();
        if (NeedsActivation || NeedsLicenseLock || NeedsPin)
            return;
        BrandName = _license.BusinessName;
        if (SelectedNav is null)
            SelectedNav = NavItems[0];
        RefreshRoleStatus();
    }

    public void RequestLicenseCheck() => _ = CheckLicenseOnlineAsync(false);

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
    private async Task Activate()
    {
        if (!_license.TryActivate(LicenseKeyInput, out var error))
        {
            LicenseHint = error;
            return;
        }
        LicenseKeyInput = "";
        IssuedKey = "";
        await _license.RefreshRemoteAsync();
        FinishLicense();
    }

    [RelayCommand]
    private Task CheckLicense() => CheckLicenseOnlineAsync(true);

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
        if (_license.IsActivated)
            EnsureLicenseWatch();
        if (!NeedsPin && !NeedsLicenseLock)
            Start();
    }

    private void EnsureLicenseWatch()
    {
        if (_licenseTimer is not null)
            return;
        _licenseTimer = new DispatcherTimer { Interval = LicenseCheckEvery };
        _licenseTimer.Tick += (_, _) => _ = CheckLicenseOnlineAsync(false);
        _licenseTimer.Start();
        _ = CheckLicenseOnlineAsync(false);
    }

    private async Task CheckLicenseOnlineAsync(bool force)
    {
        if (!_license.IsActivated || _checkingLicense)
            return;
        if (!force && !LicenseCheckDue())
            return;

        _checkingLicense = true;
        _lastLicenseCheckUtc = DateTime.UtcNow;
        try
        {
            await _license.RefreshRemoteAsync();
            await Dispatcher.UIThread.InvokeAsync(ApplyLicenseState);
        }
        catch
        {
            /* keep last known pause state */
        }
        finally
        {
            _checkingLicense = false;
        }
    }

    private bool LicenseCheckDue()
    {
        var last = _lastLicenseCheckUtc != DateTime.MinValue
            ? _lastLicenseCheckUtc
            : _license.LastOnlineUtc ?? DateTime.MinValue;
        return last == DateTime.MinValue || DateTime.UtcNow - last >= LicenseCheckEvery;
    }

    private void ApplyLicenseState()
    {
        LockReason = _license.LockReason;
        if (_license.IsPaused)
        {
            NeedsLicenseLock = true;
            return;
        }

        if (!NeedsLicenseLock)
            return;

        NeedsLicenseLock = false;
        if (CurrentPage is null && !NeedsPin && !NeedsActivation)
            Start();
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (_suppressNav || value is null || NeedsPin || NeedsActivation || NeedsLicenseLock)
            return;
        ShowNav(value.Key);
    }

    public void Notify(string message, bool error = false)
    {
        Status = message;
        IsError = error;
    }

    public void Back()
    {
        if (_back.Count > 0)
        {
            Present(_back.Pop(), allowReload: false);
            return;
        }
        if (SelectedNav is not null)
            ShowNav(SelectedNav.Key);
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
    public void GoSettings() => Select("settings");

    public void OpenContainer(int id) =>
        Push(ActivatorUtilities.CreateInstance<ContainerDetailViewModel>(_services, id));

    public void OpenSale(int id)
    {
        var vm = ActivatorUtilities.CreateInstance<SaleDetailViewModel>(_services, id);
        if (CurrentPage is NewSaleViewModel)
            Present(vm, forceLoad: true);
        else
            Push(vm);
    }

    public void EditSale(int id)
    {
        var vm = ActivatorUtilities.CreateInstance<NewSaleViewModel>(_services);
        vm.EditingSaleId = id;
        Push(vm);
    }

    public void OpenCustomer(int id) =>
        Push(ActivatorUtilities.CreateInstance<CustomerDetailViewModel>(_services, id));

    private void Select(string key)
    {
        var item = NavItems.First(n => n.Key == key);
        if (ReferenceEquals(SelectedNav, item))
            ShowNav(key);
        else
            SelectedNav = item;
    }

    private void ShowNav(string key)
    {
        _back.Clear();
        Present(NavPage(key));
    }

    private void Push(ViewModelBase vm)
    {
        if (CurrentPage is not null)
            _back.Push(CurrentPage);
        Present(vm, forceLoad: true);
    }

    private ViewModelBase NavPage(string key)
    {
        if (_navPages.TryGetValue(key, out var page))
            return page;
        page = Create(key);
        _navPages[key] = page;
        return page;
    }

    private void Present(ViewModelBase vm, bool forceLoad = false, bool allowReload = true)
    {
        CurrentPage = vm;
        _ = LoadSafe(vm, forceLoad, allowReload);
    }

    private async Task LoadSafe(ViewModelBase vm, bool forceLoad, bool allowReload)
    {
        try
        {
            if (forceLoad || !vm.HasLoaded || (allowReload && vm.ReloadOnShow))
            {
                await vm.LoadAsync();
                vm.HasLoaded = true;
            }
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
        "settings" => _services.GetRequiredService<SettingsViewModel>(),
        _ => _services.GetRequiredService<DashboardViewModel>()
    };
}
