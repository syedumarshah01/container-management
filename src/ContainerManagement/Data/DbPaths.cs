namespace ContainerManagement.Data;

public static class DbPaths
{
    public static string DirectoryPath
    {
        get
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var modern = Path.Combine(docs, AppInfo.ProductName);
            var legacy = Path.Combine(docs, AppInfo.LegacyProductName);
            var dir = Directory.Exists(legacy) && !Directory.Exists(modern) ? legacy : modern;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabaseFile
    {
        get
        {
            var modern = Path.Combine(DirectoryPath, "probooks.db");
            var legacy = Path.Combine(DirectoryPath, "cargokhata.db");
            if (File.Exists(legacy) && !File.Exists(modern))
                return legacy;
            return modern;
        }
    }

    public static string BackupDirectory
    {
        get
        {
            var dir = Path.Combine(DirectoryPath, "Backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string GoogleDriveSettingsFile => Path.Combine(DirectoryPath, "google-drive.json");

    public static string GoogleAuthDirectory
    {
        get
        {
            var dir = Path.Combine(DirectoryPath, "GoogleAuth");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string PhotosDirectory
    {
        get
        {
            var dir = Path.Combine(DirectoryPath, "Photos");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string PrintDirectory
    {
        get
        {
            var dir = Path.Combine(DirectoryPath, "Print");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConnectionString =>
        $"Data Source={DatabaseFile};Cache=Shared;Mode=ReadWriteCreate";
}
