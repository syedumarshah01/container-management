namespace ContainerManagement.Data;

public static class DbPaths
{
    public static string DirectoryPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "CargoKhata");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabaseFile => Path.Combine(DirectoryPath, "cargokhata.db");

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
