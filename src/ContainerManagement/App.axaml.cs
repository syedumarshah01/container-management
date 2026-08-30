using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ContainerManagement.Data;
using ContainerManagement.Services;
using ContainerManagement.ViewModels;
using ContainerManagement.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContainerManagement;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private bool _opened;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var backups = Services.GetRequiredService<BackupService>();
                var main = Services.GetRequiredService<MainViewModel>();
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new MainWindow { DataContext = main };
                desktop.MainWindow.Opened += (_, _) => _ = OnMainOpened(main, backups);
                desktop.Exit += (_, _) =>
                {
                    try { backups.BackupNow("close"); }
                    catch { /* never block exit */ }
                };
                if (OperatingSystem.IsWindows())
                    WatchForSecondLaunch(desktop);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("STARTUP FAILED");
            Console.Error.WriteLine(ex);
            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task OnMainOpened(MainViewModel main, BackupService backups)
    {
        if (_opened)
            return;
        _opened = true;

        try
        {
            await Task.Run(() => PrepareDatabase(backups));
            main.Start();
            _ = Task.Run(() =>
            {
                try { backups.AutoBackupOnStart(); }
                catch (Exception ex) { Console.WriteLine("Auto-backup skipped: " + ex.Message); }
            });
        }
        catch (Exception ex)
        {
            main.Notify(ex.Message, true);
        }
    }

    private static void PrepareDatabase(BackupService backups)
    {
        using var db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        SchemaPatcher.Apply(DbPaths.ConnectionString);
        if (ShopSettings.Load().DemoWiped)
            DbSeeder.SeedMinimal(db);
        else
            DbSeeder.Seed(db);
        backups.HardenDatabase();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(DbPaths.ConnectionString));

        services.AddSingleton<AccessService>();
        services.AddSingleton<LicenseService>();
        services.AddSingleton<GoogleDriveService>();
        services.AddSingleton<BackupService>();
        services.AddTransient<InventoryService>();
        services.AddTransient<SalesService>();
        services.AddTransient<LedgerService>();
        services.AddTransient<ReportService>();
        services.AddTransient<PrintService>();
        services.AddTransient<ExportService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IAppShell>(sp => sp.GetRequiredService<MainViewModel>());

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ContainersViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<ItemSalesViewModel>();
        services.AddTransient<NewSaleViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<ReceivablesViewModel>();
        services.AddTransient<ProfitViewModel>();
        services.AddTransient<BackupViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    [SupportedOSPlatform("windows")]
    private static void WatchForSecondLaunch(IClassicDesktopStyleApplicationLifetime desktop)
    {
        EventWaitHandle pulse;
        try
        {
            pulse = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ProBooks.Desktop.Show");
        }
        catch
        {
            return;
        }

        _ = Task.Run(() =>
        {
            while (true)
            {
                pulse.WaitOne();
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow is not { } window)
                        return;
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                });
            }
        });
    }
}
