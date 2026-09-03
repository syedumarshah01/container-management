using ContainerManagement.Data;
using Microsoft.Data.Sqlite;

namespace ContainerManagement.Services;

public class BackupInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime TakenAt { get; set; }
    public string SizeText { get; set; } = "";
    public string WhenText { get; set; } = "";
}

public class BackupService
{
    public const int KeepCount = 30;

    private readonly GoogleDriveService _drive;

    public BackupService(GoogleDriveService drive)
    {
        _drive = drive;
    }

    public string LiveFile => DbPaths.DatabaseFile;
    public string BackupFolder => DbPaths.BackupDirectory;

    public void HardenDatabase()
    {
        using var con = new SqliteConnection(DbPaths.ConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            """;
        cmd.ExecuteNonQuery();
    }

    public void AutoBackupOnStart()
    {
        if (!File.Exists(LiveFile))
            return;

        var latest = ListBackups().FirstOrDefault();
        if (latest is null || DateTime.Now - latest.TakenAt >= TimeSpan.FromHours(12))
            BackupNow("auto");
        else
            _drive.RetryPending();
    }

    public BackupInfo BackupNow(string reason = "manual")
    {
        if (!File.Exists(LiveFile))
            throw new InvalidOperationException("There is no database to back up yet.");

        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        var dest = Path.Combine(BackupFolder, $"probooks-{reason}-{stamp}.db");
        CopySqlite(LiveFile, dest);
        _drive.UploadAfterBackup(dest);
        Prune();
        return ToInfo(new FileInfo(dest));
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        return BackupFiles(BackupFolder)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(ToInfo)
            .ToList();
    }

    public BackupInfo Restore(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new InvalidOperationException("That backup file is missing.");

        if (File.Exists(LiveFile))
            BackupNow("before-restore");

        SqliteConnection.ClearAllPools();
        CopySqlite(backupPath, LiveFile);
        HardenDatabase();
        return ToInfo(new FileInfo(backupPath));
    }

    public void OpenBackupFolder()
    {
        var folder = BackupFolder;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private static void CopySqlite(string sourceDb, string destDb)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destDb)!);
        using var source = new SqliteConnection($"Data Source={sourceDb};Mode=ReadOnly;Cache=Shared");
        source.Open();
        using var dest = new SqliteConnection($"Data Source={destDb};Mode=ReadWriteCreate");
        dest.Open();
        source.BackupDatabase(dest);
        using (var cmd = dest.CreateCommand())
        {
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
    }

    private void Prune()
    {
        var extras = BackupFiles(BackupFolder)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTime)
            .Skip(KeepCount)
            .ToList();
        foreach (var f in extras)
        {
            try { f.Delete(); } catch { /* ignore locked old copies */ }
        }
    }

    private static IEnumerable<string> BackupFiles(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "probooks-*.db")
                .Concat(Directory.GetFiles(folder, "cargokhata-*.db"))
            : [];

    private static BackupInfo ToInfo(FileInfo f) => new()
    {
        Path = f.FullName,
        Name = f.Name,
        TakenAt = f.LastWriteTime,
        SizeText = f.Length < 1024 * 1024
            ? $"{f.Length / 1024.0:0} KB"
            : $"{f.Length / (1024.0 * 1024):0.0} MB",
        WhenText = f.LastWriteTime.ToString("dd MMM yyyy  HH:mm")
    };
}
