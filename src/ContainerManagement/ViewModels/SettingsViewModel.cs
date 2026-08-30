using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
using ContainerManagement.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AccessService _access;
    private readonly LicenseService _license;
    private readonly BackupService _backups;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAppShell _shell;

    public SettingsViewModel(AccessService access, LicenseService license, BackupService backups, IDbContextFactory<AppDbContext> dbFactory, IAppShell shell)
    {
        _access = access;
        _license = license;
        _backups = backups;
        _dbFactory = dbFactory;
        _shell = shell;
    }

    [ObservableProperty] private string companyName = "";
    [ObservableProperty] private bool companyNameLocked;
    [ObservableProperty] private string licenseId = "";
    [ObservableProperty] private string licenseKey = "";
    [ObservableProperty] private string phone = "";
    [ObservableProperty] private string address = "";
    [ObservableProperty] private decimal? lowStock = 10;
    [ObservableProperty] private decimal? dueDays = 30;
    [ObservableProperty] private string ownerPin = "";
    [ObservableProperty] private string staffPin = "";
    [ObservableProperty] private string pinNote = "";
    [ObservableProperty] private bool confirmWipe;
    [ObservableProperty] private bool isOwner;

    public override Task LoadAsync()
    {
        IsOwner = _access.IsOwner;
        var s = ShopSettings.Load();
        CompanyNameLocked = _license.IsActivated;
        CompanyName = _license.IsActivated ? _license.BusinessName : s.CompanyName;
        LicenseId = string.IsNullOrWhiteSpace(_license.CustomerId) ? "Not activated" : _license.CustomerId;
        LicenseKey = _license.Key;
        Phone = s.Phone;
        Address = s.Address;
        LowStock = s.LowStockQty;
        DueDays = s.DefaultDueDays;
        PinNote = s.PinRequired
            ? "A PIN is set. Leave the boxes empty to keep it. Type a new PIN to change it."
            : "No PIN yet. Anyone at this PC has full access.";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Save()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to change settings.", true);
            return;
        }
        var s = ShopSettings.Load();
        s.CompanyName = _license.IsActivated ? _license.BusinessName : CompanyName.Trim();
        s.Phone = Phone.Trim();
        s.Address = Address.Trim();
        s.LowStockQty = LowStock ?? 10;
        s.DefaultDueDays = (int)Math.Max(0, DueDays ?? 30);
        if (!string.IsNullOrWhiteSpace(OwnerPin))
            s.OwnerPinHash = ShopSettings.HashPin(OwnerPin);
        if (!string.IsNullOrWhiteSpace(StaffPin))
            s.StaffPinHash = ShopSettings.HashPin(StaffPin);
        s.Save();
        _access.Reload();
        OwnerPin = "";
        StaffPin = "";
        _shell.Notify("Settings saved. Company name appears on printed invoices.");
    }

    [RelayCommand]
    private void ClearPins()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed.", true);
            return;
        }
        var s = ShopSettings.Load();
        s.OwnerPinHash = "";
        s.StaffPinHash = "";
        s.Save();
        _access.Reload();
        _shell.Notify("PINs cleared. Restart the app if you want the lock screen gone.");
    }

    [RelayCommand]
    private void WipeDemo()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to wipe data.", true);
            return;
        }
        if (!ConfirmWipe)
        {
            ConfirmWipe = true;
            _shell.Notify("Click Wipe again. A backup is taken first. Demo sales and customers will go. Walk-in stays.");
            return;
        }
        try
        {
            _backups.BackupNow("before-wipe");
            SqliteConnection.ClearAllPools();
            foreach (var extra in new[] { "", "-wal", "-shm" })
            {
                var path = DbPaths.DatabaseFile + extra;
                if (File.Exists(path))
                    File.Delete(path);
            }
            using (var db = _dbFactory.CreateDbContext())
            {
                db.Database.EnsureCreated();
                SchemaPatcher.Apply(DbPaths.ConnectionString);
                DbSeeder.SeedMinimal(db);
            }
            var s = ShopSettings.Load();
            s.DemoWiped = true;
            s.Save();
            ConfirmWipe = false;
            _shell.Notify("Demo data wiped. Add your own containers and customers.");
            _shell.GoDashboard();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }
}
