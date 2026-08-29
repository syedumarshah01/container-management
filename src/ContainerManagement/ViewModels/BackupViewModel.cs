using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class BackupViewModel : ViewModelBase
{
    private readonly BackupService _backups;
    private readonly GoogleDriveService _drive;
    private readonly AccessService _access;
    private readonly IAppShell _shell;

    public BackupViewModel(BackupService backups, GoogleDriveService drive, AccessService access, IAppShell shell)
    {
        _backups = backups;
        _drive = drive;
        _access = access;
        _shell = shell;
        ClientId = _drive.ClientId;
        ClientSecret = _drive.ClientSecret;
    }

    public string LiveFile => _backups.LiveFile;
    public string BackupFolder => _backups.BackupFolder;

    public ObservableCollection<BackupInfo> Backups { get; } = new();
    public ObservableCollection<CloudBackupInfo> CloudBackups { get; } = new();

    [ObservableProperty] private BackupInfo? selected;
    [ObservableProperty] private string lastBackup = "None yet";
    [ObservableProperty] private bool confirmRestore;

    [ObservableProperty] private bool isLinked;
    [ObservableProperty] private bool showGoogleSetup;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string driveStatus = "Not linked";
    [ObservableProperty] private string driveFolderInfo = "";
    [ObservableProperty] private string driveLastUpload = "None yet";
    [ObservableProperty] private string clientId = "";
    [ObservableProperty] private string clientSecret = "";
    [ObservableProperty] private CloudBackupInfo? selectedCloud;
    [ObservableProperty] private bool confirmCloudRestore;

    public bool IsUnlinked => !IsLinked;

    public override Task LoadAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void BackupNow()
    {
        try
        {
            var info = _backups.BackupNow("manual");
            ConfirmRestore = false;
            Refresh();
            var extra = IsLinked ? " A copy was also sent to Google Drive." : "";
            _shell.Notify($"Backup saved: {info.Name}.{extra}");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try { _backups.OpenBackupFolder(); }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void Restore()
    {
        if (Selected is null)
        {
            _shell.Notify("Select a backup in the list first.", true);
            return;
        }

        if (!ConfirmRestore)
        {
            ConfirmRestore = true;
            _shell.Notify($"Click Restore again to load {Selected.Name}. A safety copy of today is taken first.");
            return;
        }

        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to restore.", true);
            return;
        }

        try
        {
            _backups.Restore(Selected.Path);
            ConfirmRestore = false;
            Refresh();
            _shell.Notify("Backup restored. Open Home to see the data.");
            _shell.GoDashboard();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task LinkFolderAsync()
    {
        try
        {
            var path = await PickGoogleDriveFolderAsync();
            if (string.IsNullOrWhiteSpace(path))
                return;
            _drive.LinkFolder(path);
            ShowGoogleSetup = false;
            RefreshDrive();
            _shell.Notify("Google Drive linked. Every backup will be copied there.");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            ShowGoogleSetup = true;
            _shell.Notify("Paste the Client ID and Client secret, then click Sign in again.");
            return;
        }

        try
        {
            IsBusy = true;
            _drive.SaveCredentials(ClientId, ClientSecret);
            DriveStatus = "Waiting for Google in your browser…";
            _shell.Notify("A browser window will open. Sign in and allow CargoKhata to create files.");
            await _drive.SignInAsync(ClientId, ClientSecret);
            ShowGoogleSetup = false;
            RefreshDrive();
            _shell.Notify("Google Drive signed in. Backups will be uploaded there.");
        }
        catch (Exception ex)
        {
            RefreshDrive();
            _shell.Notify(ex.Message, true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Disconnect()
    {
        try
        {
            _drive.Disconnect();
            ShowGoogleSetup = false;
            RefreshDrive();
            _shell.Notify("Google Drive unlinked. Local backups are unchanged.");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void OpenDrive()
    {
        try { _drive.OpenLinkedLocation(); }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void OpenGoogleCloud()
    {
        try { GoogleDriveService.OpenGoogleCloudSetup(); }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void RestoreFromDrive()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to restore.", true);
            return;
        }
        if (SelectedCloud is null)
        {
            _shell.Notify("Select a Google Drive copy first.", true);
            return;
        }
        if (!ConfirmCloudRestore)
        {
            ConfirmCloudRestore = true;
            _shell.Notify($"Click Restore from Drive again to load {SelectedCloud.Name}. A safety copy of today is taken first.");
            return;
        }
        try
        {
            var path = _drive.DownloadCloudBackup(SelectedCloud);
            _backups.Restore(path);
            ConfirmCloudRestore = false;
            Refresh();
            _shell.Notify("Google Drive copy restored. Open Home to see the data.");
            _shell.GoDashboard();
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void RefreshCloud()
    {
        try
        {
            CloudBackups.Clear();
            foreach (var b in _drive.ListCloudBackups())
                CloudBackups.Add(b);
            if (CloudBackups.Count == 0 && IsLinked)
                _shell.Notify("No copies found on Google Drive yet. Click Backup now first.");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void ShowSignInHelp()
    {
        ShowGoogleSetup = true;
        ClientId = _drive.ClientId;
        ClientSecret = _drive.ClientSecret;
    }

    private void Refresh()
    {
        Backups.Clear();
        foreach (var b in _backups.ListBackups())
            Backups.Add(b);
        LastBackup = Backups.FirstOrDefault()?.WhenText ?? "None yet";
        RefreshDrive();
        try
        {
            CloudBackups.Clear();
            if (_drive.IsLinked)
            {
                foreach (var b in _drive.ListCloudBackups())
                    CloudBackups.Add(b);
            }
        }
        catch { /* Drive list is optional */ }
    }

    private void RefreshDrive()
    {
        IsLinked = _drive.IsLinked;
        DriveStatus = _drive.StatusText;
        DriveFolderInfo = _drive.FolderInfo;
        DriveLastUpload = _drive.LastUploadText;
        OnPropertyChanged(nameof(IsUnlinked));
    }

    private async Task<string?> PickGoogleDriveFolderAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
            return _drive.FindGoogleDriveFolder();

        var options = new FolderPickerOpenOptions
        {
            Title = "Select your Google Drive folder (usually named My Drive)",
            AllowMultiple = false
        };

        var detected = _drive.FindGoogleDriveFolder();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            try
            {
                var start = await window.StorageProvider.TryGetFolderFromPathAsync(detected);
                if (start is not null)
                    options.SuggestedStartLocation = start;
            }
            catch { /* picker still works */ }
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0)
            return null;
        return folders[0].TryGetLocalPath();
    }

    partial void OnIsLinkedChanged(bool value) => OnPropertyChanged(nameof(IsUnlinked));
}
