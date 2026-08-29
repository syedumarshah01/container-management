using System.Globalization;
using Avalonia;
using Avalonia.Logging;

namespace ContainerManagement;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var culture = CultureInfo.CreateSpecificCulture("en-PK");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            Console.WriteLine("Launching desktop window…");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Press Enter to close.");
            try { Console.ReadLine(); } catch { /* ignore */ }
            Environment.Exit(1);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Warning);
}
