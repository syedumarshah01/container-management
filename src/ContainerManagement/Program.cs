using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Logging;
using ContainerManagement.Data;
using ContainerManagement.Services;

namespace ContainerManagement;

sealed class Program
{
    private const int AttachParentProcess = -1;
    private static Mutex? _instance;

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (args.Length >= 2 && string.Equals(args[0], "--issue", StringComparison.OrdinalIgnoreCase))
            {
                AttachConsole(AttachParentProcess);
                var key = LicenseService.IssueKey(args[1]);
                Console.WriteLine(key);
                return;
            }

            if (!TryTakeInstance())
                return;

            var culture = CultureInfo.CreateSpecificCulture("en-PK");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            TryWriteCrash(ex);
            MessageBox(IntPtr.Zero, ex.Message, "ProBooks could not start", 0x00000010);
            Environment.Exit(1);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Warning);

    private static bool TryTakeInstance()
    {
        try
        {
            _instance = new Mutex(true, @"Local\ProBooks.Desktop", out var created);
            if (created)
                return true;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }

        try
        {
            EventWaitHandle.OpenExisting(@"Local\ProBooks.Desktop.Show").Set();
        }
        catch
        {
            /* first copy is still starting */
        }

        return false;
    }

    private static void TryWriteCrash(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(DbPaths.DirectoryPath, "startup-error.txt"), ex.ToString());
        }
        catch
        {
            /* ignore */
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
