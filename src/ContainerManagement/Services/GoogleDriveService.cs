using System.Globalization;
using System.Text.Json;
using ContainerManagement.Data;
using ContainerManagement.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace ContainerManagement.Services;

public class GoogleDriveSettings
{
    public string Mode { get; set; } = "";
    public string LocalDriveRoot { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Email { get; set; } = "";
    public string RootFolderId { get; set; } = "";
    public string CurrentFolderId { get; set; } = "";
    public string CurrentFolderName { get; set; } = "";
    public string RangeStart { get; set; } = "";
    public int CurrentCount { get; set; }
    public string LastUpload { get; set; } = "";
    public string LastError { get; set; } = "";
    public List<string> Pending { get; set; } = new();
}

public class GoogleDriveService
{
    public const int FilesPerFolder = 30;
    private const string AppFolderName = "ProBooks";
    private const string LegacyAppFolderName = "CargoKhata";
    private const string UserKey = "user";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

    private readonly object _gate = new();
    private GoogleDriveSettings _settings;

    public GoogleDriveService()
    {
        _settings = Load();
    }

    public bool IsLinked =>
        _settings.Mode is "folder" or "oauth";

    public string Mode => _settings.Mode;

    public string Email => _settings.Email;

    public string ClientId
    {
        get => _settings.ClientId;
        set => _settings.ClientId = value ?? "";
    }

    public string ClientSecret
    {
        get => _settings.ClientSecret;
        set => _settings.ClientSecret = value ?? "";
    }

    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.LastError) && IsLinked)
                return "Google Drive error: " + _settings.LastError;
            if (_settings.Mode == "oauth")
                return string.IsNullOrWhiteSpace(_settings.Email)
                    ? "Signed in to Google Drive"
                    : "Signed in as " + _settings.Email;
            if (_settings.Mode == "folder")
                return "Linked to " + AppFolderOnDisk();
            return "Not linked";
        }
    }

    public string FolderInfo
    {
        get
        {
            if (!IsLinked)
                return "Each Drive folder holds 30 backups, then a new dated folder is created.";
            var name = string.IsNullOrWhiteSpace(_settings.CurrentFolderName)
                ? "(will be created on the next backup)"
                : _settings.CurrentFolderName;
            return $"{name}  —  {_settings.CurrentCount} of {FilesPerFolder}";
        }
    }

    public string LastUploadText =>
        string.IsNullOrWhiteSpace(_settings.LastUpload) ? "None yet" : _settings.LastUpload;

    public string? FindGoogleDriveFolder()
    {
        foreach (var c in CandidateFolders())
        {
            if (Directory.Exists(c))
                return c;
        }

        return null;
    }

    public void LinkFolder(string myDrivePath)
    {
        if (string.IsNullOrWhiteSpace(myDrivePath) || !Directory.Exists(myDrivePath))
            throw new InvalidOperationException("That folder does not exist.");

        lock (_gate)
        {
            var modern = Path.Combine(myDrivePath, AppFolderName);
            var legacy = Path.Combine(myDrivePath, LegacyAppFolderName);
            var app = Directory.Exists(legacy) && !Directory.Exists(modern) ? legacy : modern;
            Directory.CreateDirectory(app);
            _settings.Mode = "folder";
            _settings.LocalDriveRoot = myDrivePath;
            _settings.Email = "";
            _settings.RootFolderId = "";
            _settings.CurrentFolderId = "";
            _settings.LastError = "";
            ResumeDiskFolder(app);
            Save();
        }
    }

    public async Task SignInAsync(string clientId, string clientSecret, CancellationToken ct = default)
    {
        clientId = (clientId ?? "").Trim();
        clientSecret = (clientSecret ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Paste the Client ID and Client secret, then try again.");

        Directory.CreateDirectory(DbPaths.GoogleAuthDirectory);
        var secrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret };
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = Scopes,
            DataStore = new FileDataStore(DbPaths.GoogleAuthDirectory, true)
        });
        var credential = await new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver())
            .AuthorizeAsync(UserKey, ct)
            .ConfigureAwait(false);

        using var service = CreateService(credential);
        var about = service.About.Get();
        about.Fields = "user(emailAddress)";
        var info = await about.ExecuteAsync(ct).ConfigureAwait(false);

        lock (_gate)
        {
            _settings.Mode = "oauth";
            _settings.ClientId = clientId;
            _settings.ClientSecret = clientSecret;
            _settings.Email = info.User?.EmailAddress ?? "";
            _settings.LocalDriveRoot = "";
            _settings.LastError = "";
            var rootId = EnsureRootFolder(service);
            ResumeDriveFolder(service, rootId);
            Save();
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            _settings.Mode = "";
            _settings.LocalDriveRoot = "";
            _settings.Email = "";
            _settings.RootFolderId = "";
            _settings.CurrentFolderId = "";
            _settings.CurrentFolderName = "";
            _settings.RangeStart = "";
            _settings.CurrentCount = 0;
            _settings.LastError = "";
            _settings.Pending.Clear();
            Save();
        }

        try
        {
            if (Directory.Exists(DbPaths.GoogleAuthDirectory))
                Directory.Delete(DbPaths.GoogleAuthDirectory, true);
        }
        catch { /* ignore */ }
    }

    public void SaveCredentials(string clientId, string clientSecret)
    {
        lock (_gate)
        {
            _settings.ClientId = (clientId ?? "").Trim();
            _settings.ClientSecret = (clientSecret ?? "").Trim();
            Save();
        }
    }

    public void UploadAfterBackup(string localFile)
    {
        try
        {
            if (!IsLinked || string.IsNullOrWhiteSpace(localFile) || !File.Exists(localFile))
                return;

            if (_settings.Mode == "folder")
                CopyToFolder(localFile);
            else if (_settings.Mode == "oauth")
                UploadToApi(localFile);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _settings.LastError = ex.Message;
                EnqueuePending(localFile);
                Save();
            }

            Console.WriteLine("Google Drive upload skipped: " + ex.Message);
        }
    }

    public void RetryPending()
    {
        List<string> pending;
        lock (_gate)
            pending = _settings.Pending.ToList();

        if (pending.Count == 0 || !IsLinked)
            return;

        foreach (var path in pending)
        {
            if (!File.Exists(path))
            {
                lock (_gate)
                {
                    _settings.Pending.Remove(path);
                    Save();
                }
                continue;
            }

            try
            {
                if (_settings.Mode == "folder")
                    CopyToFolder(path);
                else if (_settings.Mode == "oauth")
                    UploadToApi(path);

                lock (_gate)
                {
                    _settings.Pending.Remove(path);
                    Save();
                }
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _settings.LastError = ex.Message;
                    Save();
                }
                Console.WriteLine("Google Drive retry skipped: " + ex.Message);
                break;
            }
        }
    }

    public void OpenLinkedLocation()
    {
        if (_settings.Mode == "folder")
        {
            var folder = Directory.Exists(AppFolderOnDisk()) ? AppFolderOnDisk() : _settings.LocalDriveRoot;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
            return;
        }

        if (_settings.Mode == "oauth" && !string.IsNullOrWhiteSpace(_settings.RootFolderId))
        {
            var url = "https://drive.google.com/drive/folders/" + _settings.RootFolderId;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://drive.google.com",
            UseShellExecute = true
        });
    }

    public static void OpenGoogleCloudSetup()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://console.cloud.google.com/apis/library/drive.googleapis.com",
            UseShellExecute = true
        });
    }

    public List<CloudBackupInfo> ListCloudBackups()
    {
        if (!IsLinked)
            return new List<CloudBackupInfo>();

        if (_settings.Mode == "folder")
            return ListFolderMode();
        if (_settings.Mode == "oauth")
            return ListOauthMode();
        return new List<CloudBackupInfo>();
    }

    public string DownloadCloudBackup(CloudBackupInfo info)
    {
        Directory.CreateDirectory(DbPaths.BackupDirectory);
        var dest = Path.Combine(DbPaths.BackupDirectory, "from-drive-" + Path.GetFileName(info.Name));
        if (info.IsLocalFolder)
        {
            if (!File.Exists(info.LocalPath))
                throw new InvalidOperationException("That Drive copy is missing on this PC.");
            File.Copy(info.LocalPath, dest, overwrite: true);
            return dest;
        }

        var credential = GetCredential();
        using var service = CreateService(credential);
        using var stream = new FileStream(dest, FileMode.Create, FileAccess.Write);
        service.Files.Get(info.Id).Download(stream);
        return dest;
    }

    private List<CloudBackupInfo> ListFolderMode()
    {
        var root = AppFolderOnDisk();
        if (!Directory.Exists(root))
            return new List<CloudBackupInfo>();
        return BackupFilesUnder(root)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new CloudBackupInfo
            {
                Id = f.FullName,
                Name = f.Name,
                Folder = f.Directory?.Name ?? "",
                WhenText = f.LastWriteTime.ToString("dd MMM yyyy  HH:mm"),
                SizeText = f.Length < 1024 * 1024 ? $"{f.Length / 1024.0:0} KB" : $"{f.Length / (1024.0 * 1024):0.0} MB",
                LocalPath = f.FullName,
                IsLocalFolder = true
            })
            .ToList();
    }

    private List<CloudBackupInfo> ListOauthMode()
    {
        var credential = GetCredential();
        using var service = CreateService(credential);
        var rootId = EnsureRootFolder(service);
        var foldersReq = service.Files.List();
        foldersReq.Q = $"'{rootId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        foldersReq.Fields = "files(id, name)";
        foldersReq.PageSize = 100;
        var folders = foldersReq.Execute().Files ?? new List<DriveFile>();
        var list = new List<CloudBackupInfo>();
        foreach (var folder in folders)
        {
            var filesReq = service.Files.List();
            filesReq.Q = $"'{folder.Id}' in parents and trashed = false";
            filesReq.Fields = "files(id, name, createdTime, size)";
            filesReq.PageSize = 100;
            var files = filesReq.Execute().Files ?? new List<DriveFile>();
            foreach (var f in files.Where(x => x.Name != null && IsBackupFileName(x.Name)))
            {
                list.Add(new CloudBackupInfo
                {
                    Id = f.Id,
                    Name = f.Name,
                    Folder = folder.Name ?? "",
                    WhenText = f.CreatedTime?.ToString("dd MMM yyyy  HH:mm") ?? "",
                    SizeText = f.Size is long sz
                        ? (sz < 1024 * 1024 ? $"{sz / 1024.0:0} KB" : $"{sz / (1024.0 * 1024):0.0} MB")
                        : "",
                    IsLocalFolder = false
                });
            }
        }

        return list.OrderByDescending(x => x.WhenText).ToList();
    }

    private void CopyToFolder(string localFile)
    {
        lock (_gate)
        {
            var destDir = EnsureDiskRangeFolder();
            var dest = UniquePath(Path.Combine(destDir, Path.GetFileName(localFile)));
            File.Copy(localFile, dest, overwrite: false);
            _settings.CurrentCount++;
            _settings.LastUpload = DateTime.Now.ToString("dd MMM yyyy  HH:mm");
            _settings.LastError = "";
            _settings.Pending.Remove(localFile);
            Save();
        }
    }

    private void UploadToApi(string localFile)
    {
        var credential = GetCredential();
        using var service = CreateService(credential);

        lock (_gate)
        {
            var folderId = EnsureDriveRangeFolder(service);
            var meta = new DriveFile
            {
                Name = Path.GetFileName(localFile),
                Parents = new List<string> { folderId }
            };
            using var stream = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var request = service.Files.Create(meta, stream, "application/octet-stream");
            request.Fields = "id, name";
            var result = request.Upload();
            if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
                throw new InvalidOperationException(result.Exception?.Message ?? "Upload to Google Drive did not finish.");

            _settings.CurrentCount++;
            _settings.LastUpload = DateTime.Now.ToString("dd MMM yyyy  HH:mm");
            _settings.LastError = "";
            _settings.Pending.Remove(localFile);
            Save();
        }
    }

    private string EnsureDiskRangeFolder()
    {
        var app = AppFolderOnDisk();
        Directory.CreateDirectory(app);

        if (_settings.CurrentCount >= FilesPerFolder ||
            string.IsNullOrWhiteSpace(_settings.CurrentFolderName) ||
            !Directory.Exists(Path.Combine(app, _settings.CurrentFolderName)))
        {
            StartNewRange(DateTime.Now);
            var created = Path.Combine(app, _settings.CurrentFolderName);
            Directory.CreateDirectory(created);
            return created;
        }

        RenameDiskIfNeeded(app, DateTime.Now);
        var dir = Path.Combine(app, _settings.CurrentFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string EnsureDriveRangeFolder(DriveService service)
    {
        var rootId = EnsureRootFolder(service);

        if (_settings.CurrentCount >= FilesPerFolder ||
            string.IsNullOrWhiteSpace(_settings.CurrentFolderId))
        {
            StartNewRange(DateTime.Now);
            var meta = new DriveFile
            {
                Name = _settings.CurrentFolderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { rootId }
            };
            var create = service.Files.Create(meta);
            create.Fields = "id, name";
            var folder = create.Execute();
            _settings.CurrentFolderId = folder.Id;
            Save();
            return folder.Id;
        }

        RenameDriveIfNeeded(service, DateTime.Now);
        return _settings.CurrentFolderId;
    }

    private void StartNewRange(DateTime now)
    {
        _settings.RangeStart = now.ToString("yyyy-MM-dd");
        _settings.CurrentCount = 0;
        _settings.CurrentFolderName = RangeName(now, now);
        _settings.CurrentFolderId = "";
    }

    private void RenameDiskIfNeeded(string app, DateTime now)
    {
        var start = ParseRangeStart() ?? now.Date;
        var desired = RangeName(start, now);
        if (desired == _settings.CurrentFolderName)
            return;

        var from = Path.Combine(app, _settings.CurrentFolderName);
        var to = Path.Combine(app, desired);
        try
        {
            if (Directory.Exists(from) && !Directory.Exists(to))
                Directory.Move(from, to);
            _settings.CurrentFolderName = desired;
        }
        catch { /* keep old name */ }
    }

    private void RenameDriveIfNeeded(DriveService service, DateTime now)
    {
        var start = ParseRangeStart() ?? now.Date;
        var desired = RangeName(start, now);
        if (desired == _settings.CurrentFolderName || string.IsNullOrWhiteSpace(_settings.CurrentFolderId))
            return;

        try
        {
            var patch = new DriveFile { Name = desired };
            service.Files.Update(patch, _settings.CurrentFolderId).Execute();
            _settings.CurrentFolderName = desired;
        }
        catch { /* keep old name */ }
    }

    private string EnsureRootFolder(DriveService service)
    {
        if (!string.IsNullOrWhiteSpace(_settings.RootFolderId))
        {
            try
            {
                var get = service.Files.Get(_settings.RootFolderId);
                get.Fields = "id, trashed";
                var existing = get.Execute();
                if (existing is not null && existing.Trashed != true)
                    return existing.Id;
            }
            catch { /* recreate */ }
        }

        var meta = new DriveFile
        {
            Name = AppFolderName,
            MimeType = "application/vnd.google-apps.folder"
        };
        var create = service.Files.Create(meta);
        create.Fields = "id, name";
        var folder = create.Execute();
        _settings.RootFolderId = folder.Id;
        Save();
        return folder.Id;
    }

    private void ResumeDiskFolder(string app)
    {
        var newest = Directory.Exists(app)
            ? Directory.GetDirectories(app, "backups_from_*")
                .Select(p => new DirectoryInfo(p))
                .OrderByDescending(d => d.LastWriteTime)
                .FirstOrDefault()
            : null;

        if (newest is null)
        {
            _settings.CurrentFolderName = "";
            _settings.CurrentFolderId = "";
            _settings.CurrentCount = 0;
            _settings.RangeStart = "";
            return;
        }

        var count = newest.GetFiles("probooks-*.db").Length + newest.GetFiles("cargokhata-*.db").Length;
        if (count >= FilesPerFolder)
        {
            _settings.CurrentFolderName = "";
            _settings.CurrentCount = 0;
            _settings.RangeStart = "";
            return;
        }

        _settings.CurrentFolderName = newest.Name;
        _settings.CurrentCount = count;
        _settings.RangeStart = newest.CreationTime.ToString("yyyy-MM-dd");
    }

    private void ResumeDriveFolder(DriveService service, string rootId)
    {
        try
        {
            var list = service.Files.List();
            list.Q = $"'{rootId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            list.Fields = "files(id, name, createdTime)";
            list.PageSize = 100;
            var files = list.Execute().Files ?? new List<DriveFile>();
            var newest = files
                .Where(f => f.Name is not null && f.Name.StartsWith("backups_from_", StringComparison.Ordinal))
                .OrderByDescending(f => f.CreatedTime)
                .FirstOrDefault();

            if (newest is null)
            {
                _settings.CurrentFolderId = "";
                _settings.CurrentFolderName = "";
                _settings.CurrentCount = 0;
                _settings.RangeStart = "";
                return;
            }

            var count = CountChildren(service, newest.Id);
            if (count >= FilesPerFolder)
            {
                _settings.CurrentFolderId = "";
                _settings.CurrentFolderName = "";
                _settings.CurrentCount = 0;
                _settings.RangeStart = "";
                return;
            }

            _settings.CurrentFolderId = newest.Id;
            _settings.CurrentFolderName = newest.Name;
            _settings.CurrentCount = count;
            _settings.RangeStart = (newest.CreatedTimeDateTimeOffset?.DateTime ?? DateTime.Now).ToString("yyyy-MM-dd");
        }
        catch
        {
            _settings.CurrentFolderId = "";
            _settings.CurrentFolderName = "";
            _settings.CurrentCount = 0;
        }
    }

    private static int CountChildren(DriveService service, string folderId)
    {
        var list = service.Files.List();
        list.Q = $"'{folderId}' in parents and trashed = false";
        list.Fields = "files(id)";
        list.PageSize = 100;
        return list.Execute().Files?.Count ?? 0;
    }

    private UserCredential GetCredential()
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            throw new InvalidOperationException("Google Drive is not signed in.");

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _settings.ClientId,
                ClientSecret = _settings.ClientSecret
            },
            Scopes = Scopes,
            DataStore = new FileDataStore(DbPaths.GoogleAuthDirectory, true)
        });
        var token = flow.LoadTokenAsync(UserKey, CancellationToken.None).GetAwaiter().GetResult();
        if (token is null)
            throw new InvalidOperationException("Google Drive sign-in expired. Open Backup and sign in again.");
        return new UserCredential(flow, UserKey, token);
    }

    private static DriveService CreateService(UserCredential credential) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ProBooks"
        });

    private string AppFolderOnDisk()
    {
        var modern = Path.Combine(_settings.LocalDriveRoot, AppFolderName);
        var legacy = Path.Combine(_settings.LocalDriveRoot, LegacyAppFolderName);
        if (Directory.Exists(legacy) && !Directory.Exists(modern))
            return legacy;
        return modern;
    }

    private static bool IsBackupFileName(string name) =>
        name.StartsWith("probooks-", StringComparison.Ordinal) ||
        name.StartsWith("cargokhata-", StringComparison.Ordinal);

    private static IEnumerable<string> BackupFilesUnder(string root)
    {
        if (!Directory.Exists(root))
            return [];
        return Directory.GetFiles(root, "probooks-*.db", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(root, "cargokhata-*.db", SearchOption.AllDirectories));
    }

    private DateTime? ParseRangeStart()
    {
        if (DateTime.TryParseExact(_settings.RangeStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
            return d.Date;
        return null;
    }

    private static string RangeName(DateTime start, DateTime end)
    {
        var a = start.ToString("dMMM", CultureInfo.InvariantCulture);
        var b = end.ToString("dMMM", CultureInfo.InvariantCulture);
        return $"backups_from_{a}_to_{b}";
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 100; i++)
        {
            var candidate = Path.Combine(dir, $"{name}-{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{DateTime.Now:HHmmssfff}{ext}");
    }

    private void EnqueuePending(string localFile)
    {
        if (!_settings.Pending.Contains(localFile, StringComparer.OrdinalIgnoreCase))
            _settings.Pending.Add(localFile);
    }

    private static IEnumerable<string> CandidateFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Google Drive", "My Drive");
        yield return Path.Combine(home, "My Drive");
        yield return Path.Combine(home, "Google Drive");

        var g = Path.Combine("G:" + Path.DirectorySeparatorChar, "My Drive");
        yield return g;

        var cloud = Path.Combine(home, "Library", "CloudStorage");
        if (!Directory.Exists(cloud))
            yield break;
        foreach (var d in Directory.GetDirectories(cloud, "GoogleDrive*"))
        {
            var my = Path.Combine(d, "My Drive");
            yield return Directory.Exists(my) ? my : d;
        }
    }

    private GoogleDriveSettings Load()
    {
        try
        {
            var path = DbPaths.GoogleDriveSettingsFile;
            if (!File.Exists(path))
                return new GoogleDriveSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GoogleDriveSettings>(json, JsonOpts) ?? new GoogleDriveSettings();
        }
        catch
        {
            return new GoogleDriveSettings();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOpts);
        File.WriteAllText(DbPaths.GoogleDriveSettingsFile, json);
    }
}
