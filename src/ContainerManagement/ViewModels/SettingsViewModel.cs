using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    private readonly UpdateService _updates;
    private readonly IAppShell _shell;

    public override bool ReloadOnShow => false;

    public SettingsViewModel(AccessService access, LicenseService license, BackupService backups, IDbContextFactory<AppDbContext> dbFactory, UpdateService updates, IAppShell shell)
    {
        _access = access;
        _license = license;
        _backups = backups;
        _dbFactory = dbFactory;
        _updates = updates;
        _shell = shell;
    }

    [ObservableProperty] private string companyName = "";
    [ObservableProperty] private bool companyNameLocked;
    [ObservableProperty] private string licenseId = "";
    [ObservableProperty] private string licenseKey = "";
    [ObservableProperty] private string phone = "";
    [ObservableProperty] private string address = "";
    [ObservableProperty] private decimal? lowStock = 10;
    [ObservableProperty] private string whatsAppMessage = "";
    [ObservableProperty] private decimal? dueDays = 30;
    [ObservableProperty] private string ownerPin = "";
    [ObservableProperty] private string staffPin = "";
    [ObservableProperty] private string pinNote = "";
    [ObservableProperty] private bool confirmWipe;

    /// <summary>The Updates card. Checked, never automatic: the shop decides when its books change hands to a
    /// new build, and a program that updates itself in the background has no answer for the woman who finds it
    /// different on Monday.</summary>
    [ObservableProperty] private string updateLine = "Not checked yet.";
    [ObservableProperty] private string updateNotes = "";
    [ObservableProperty] private bool updateAvailable;
    [ObservableProperty] private bool updateBusy;
    private bool _confirmUpdate;
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private bool showWipeDemo = true;

    /// <summary>The words a typed message may borrow the book's figures with, straight off the list the
    /// sending code uses, so the settings page cannot name a token the message cannot fill.</summary>
    public string ShareTokens => PrintService.ShareTokenList;

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
        WhatsAppMessage = s.WhatsAppMessage;
        PinNote = s.PinRequired
            ? "A PIN is set. Leave the boxes empty to keep it. Type a new PIN to change it."
            : "No PIN yet. Anyone at this PC has full access.";
        ShowWipeDemo = !s.DemoWiped;
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
        // A message with a token the book cannot fill would go out to a customer reading "you owe {blance}",
        // so it is refused here rather than saved to be discovered in a chat.
        var unknown = PrintService.UnknownShareTokens(WhatsAppMessage);
        if (unknown.Count > 0)
        {
            _shell.Notify($"{string.Join(", ", unknown)} cannot be filled in from the book. It can use: {PrintService.ShareTokenList}", true);
            return;
        }
        var s = ShopSettings.Load();
        s.CompanyName = _license.IsActivated ? _license.BusinessName : CompanyName.Trim();
        s.Phone = Phone.Trim();
        s.Address = Address.Trim();
        s.LowStockQty = LowStock ?? 10;
        s.WhatsAppMessage = WhatsAppMessage.Trim();
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
    private async Task CheckUpdateAsync()
    {
        if (UpdateBusy)
            return;
        UpdateBusy = true;
        UpdateLine = "Asking the branch what is there...";
        UpdateAvailable = false;
        try
        {
            var s = await _updates.CheckAsync();
            UpdateNotes = s.Notes;
            // A clerk can be told that work is waiting - that is information for the owner, not a mistake - but
            // the button belongs to whoever can sign for it, and the page says so instead of showing nothing.
            UpdateLine = s.CanApply && !_access.IsOwner
                ? s.Message + " The owner has to sign in to start it."
                : s.Message;
            // The button appears when there is something to do, and not before - which is how the rest of this
            // page works. A failed check leaves nothing on the page but the reason.
            UpdateAvailable = s.CanApply && _access.IsOwner;
        }
        catch (Exception ex)
        {
            UpdateLine = ex.Message;
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    /// <summary>
    /// The update itself, which closes ProBooks so the new one can be built over it. It is pressed twice for the
    /// same reason the wipe is: what follows the second press happens while nobody is looking at the screen.
    /// </summary>
    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to update ProBooks.", true);
            return;
        }
        if (!_confirmUpdate)
        {
            _confirmUpdate = true;
            UpdateLine = "Press again to close ProBooks and build the new one. The books are backed up first.";
            return;
        }
        _confirmUpdate = false;
        UpdateBusy = true;
        try
        {
            var file = await _updates.ApplyAsync();
            UpdateLine = "Update started. If ProBooks has not come back in a few minutes, run "
                + file + " and read update.log.";
            // ProBooks closes itself: the build cannot write over the files that are running, and a window that
            // vanishes while a shop watches is at least an honest signal that something has begun.
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
                life.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateLine = ex.Message;
            _shell.Notify(ex.Message, true);
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    /// <summary>Notes are on the card only when there is something to read, so the page does not carry a blank box.</summary>
    public bool HasUpdateNotes => !string.IsNullOrWhiteSpace(UpdateNotes);

    partial void OnUpdateNotesChanged(string value) => OnPropertyChanged(nameof(HasUpdateNotes));

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
            ShowWipeDemo = false;
            _shell.Notify("Demo data wiped. Add your own containers and customers.");
            _shell.GoDashboard();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }
}
