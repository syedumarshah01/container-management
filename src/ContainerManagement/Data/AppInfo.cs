namespace ContainerManagement.Data;

public static class AppInfo
{
    public const string ProductName = "ProBooks";
    public const string LegacyProductName = "CargoKhata";

    /// <summary>
    /// The version this build carries, read from the assembly the shop is running rather than typed into a
    /// label, so the number in the Updates page is the number the exe's own properties say. Three parts,
    /// because a fourth is noise nobody reads.
    /// </summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
}
