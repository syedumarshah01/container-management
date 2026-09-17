using System.Diagnostics;
using System.Text;
using ContainerManagement.Data;

namespace ContainerManagement.Services;

/// <summary>What the shop is told about updates, and what the page is allowed to do about it.</summary>
public enum UpdateState
{
    Unknown,
    /// <summary>Not a source folder: an installed copy, which updates the way it was installed.</summary>
    NotInstall,
    /// <summary>A source folder whose tools - git, or the SDK - do not answer.</summary>
    NoTools,
    /// <summary>Nothing came back from the branch. A shop with a slow line is not a shop with no update.</summary>
    Offline,
    UpToDate,
    Available,
    /// <summary>Work in this folder was never saved to git, and no program should decide what happens to it.</summary>
    BlockedLocalChanges,
    /// <summary>Both this folder and the branch have their own commits: only a person can say which is which.</summary>
    Diverged,
    /// <summary>The changes are fetched, but this PC cannot build them.</summary>
    NoSdk
}

public readonly record struct UpdateOutcome(UpdateState State, string Message, bool CanApply, bool ChangesWaiting);

/// <summary>What a check found, with the two figures the page shows beside it.</summary>
public sealed class UpdateStatus
{
    public UpdateState State { get; init; }
    public string Message { get; init; } = "";
    public string Notes { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public string RemoteVersion { get; init; } = "";
    public bool CanApply { get; init; }
    public bool ChangesWaiting { get; init; }
    public string Folder { get; init; } = "";
}

/// <summary>
/// The whole of the decision, with no fetching and no building in it, so it can be read and checked the way the
/// arithmetic in the rest of the book is checked. The service collects facts; this says what they mean. The two
/// are apart on purpose - a rule that only runs after a network call is a rule nobody has ever seen tested.
/// </summary>
public static class UpdateRules
{
    public const string ProjectPath = @"src\ContainerManagement\ContainerManagement.csproj";
    public const string ExePath = @"src\ContainerManagement\bin\Debug\net8.0\ProBooks.exe";

    /// <summary>A version as three numbers: "1.10" is 1.10.0, and anything unparsable is no version at all.</summary>
    public static (int Major, int Minor, int Patch)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Trim().TrimStart('v', 'V').Split('-')[0].Split('+')[0].Split('.');
        if (parts.Length is 0 or > 3)
            return null;
        var major = 0;
        var minor = 0;
        var patch = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var v) || v < 0)
                return null;
            if (i == 0) major = v;
            else if (i == 1) minor = v;
            else patch = v;
        }
        return (major, minor, patch);
    }

    /// <summary>
    /// Compared by numbers, never by text: "1.10.0" is newer than "1.9.0" and a string compare says the
    /// opposite, which is how a shop can sit on an old build for a year believing it is the new one. An
    /// unparsable figure on either side is "the same", because guessing that an update exists is worse than
    /// waiting to be told twice.
    /// </summary>
    public static int Compare(string? a, string? b)
    {
        var x = Parse(a);
        var y = Parse(b);
        if (x is null || y is null)
            return 0;
        var (am, ai, ap) = x.Value;
        var (bm, bi, bp) = y.Value;
        if (am != bm) return am.CompareTo(bm);
        if (ai != bi) return ai.CompareTo(bi);
        return ap.CompareTo(bp);
    }

    public static bool IsUpdate(string? current, string? remote) => Compare(remote, current) > 0;

    /// <summary>The version a build will carry, read out of the project file the shop's folder holds.</summary>
    public static string? VersionFromProject(string? csproj)
    {
        if (string.IsNullOrWhiteSpace(csproj))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(csproj, @"<Version>([^<]+)</Version>");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// The newest changelog entry - what the shop is offered as "what changed here": the lines between the first
    /// heading that opens a version and the next one. Forgiving on purpose, because a changelog that will not
    /// parse is not worth an error at the moment somebody is deciding whether to update.
    /// </summary>
    public static string NotesFrom(string? changelog)
    {
        if (string.IsNullOrWhiteSpace(changelog))
            return "";
        var sb = new StringBuilder();
        var inside = false;
        foreach (var raw in changelog.Replace("\r\n", "\n").Split('\n'))
        {
            var l = raw.TrimEnd();
            var heading = l.StartsWith("## ");
            if (!inside && heading)
            {
                inside = true;
                sb.AppendLine(l[3..].Trim());
                continue;
            }
            if (inside && heading)
                break;
            if (inside && !string.IsNullOrWhiteSpace(l))
                sb.AppendLine(l.TrimStart('-', ' ').Trim());
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// What to tell the shop, from the facts alone. The order is the safety: a folder with uncommitted work, or
    /// a branch that has gone its own way, is stopped before anything is merged or built, because the worst
    /// update is the one that quietly steps over a change somebody made at the counter.
    /// </summary>
    public static UpdateOutcome Decide(
        bool haveGit, bool haveSdk, bool fetchWorked, bool treeClean, bool behind, bool diverged,
        string? currentVersion, string? remoteVersion)
    {
        if (!haveGit)
            return new(UpdateState.NotInstall,
                "This ProBooks is an installed copy, not the folder it was built from, so it cannot update "
                + "itself. Copy the new folder over this one, the way this one arrived.", false, false);
        if (diverged)
            return new(UpdateState.Diverged,
                "This folder has work in it that the branch has not seen, and the branch has work this folder "
                + "has not. That needs a person, so no button is offered.", false, false);
        if (!treeClean)
            return new(UpdateState.BlockedLocalChanges,
                "Files in this folder are changed but not saved to git. Put them away first - the update will "
                + "not decide what happens to work that exists only on this PC.", false, false);
        if (!fetchWorked)
            return new(UpdateState.Offline,
                "The branch could not be reached. Nothing here was changed. Try again when the line is back.", false, false);
        if (!behind)
            return new(UpdateState.UpToDate,
                string.IsNullOrWhiteSpace(currentVersion) ? "Nothing is waiting." : $"Nothing is waiting. This is {currentVersion}.",
                false, false);
        if (!haveSdk)
            return new(UpdateState.NoSdk,
                "The changes are fetched, but this PC has no .NET SDK to build them. Build it on the PC that "
                + "makes this one's copies, or install the SDK here.", false, true);
        var label = IsUpdate(currentVersion, remoteVersion)
            ? $"{currentVersion} to {remoteVersion}"
            : "work waiting on the branch";
        return new(UpdateState.Available,
            $"Update ready - {label}. Your books are not part of it: the data folder is not opened.", true, true);
    }

    /// <summary>
    /// The batch file that does the work once ProBooks has let go of its own files. Written as text, here, so
    /// it can be read and checked: it fast-forwards or it stops, it never resets, never cleans, and it never
    /// names the folder where the shop's books live.
    /// </summary>
    public static string BuildScript(string repoRoot, string branch, int processId, DateTime when)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine($"rem ProBooks update - written {when:dd MMM yyyy HH:mm}. The log of this run is update.log in the folder below.");
        sb.AppendLine("setlocal");
        sb.AppendLine($"cd /d \"{repoRoot}\"");
        sb.AppendLine("echo ============================================= >> update.log");
        sb.AppendLine($"echo update started {when:yyyy-MM-dd HH:mm:ss} >> update.log");
        sb.AppendLine(":waitloop");
        sb.AppendLine($"tasklist /fi \"PID eq {processId}\" 2>nul | find \"{processId}\" >nul");
        sb.AppendLine("if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto waitloop)");
        sb.AppendLine($"git fetch origin {branch} >> update.log 2>&1");
        sb.AppendLine("if errorlevel 1 (echo fetch failed - nothing was changed & pause & exit /b 1)");
        sb.AppendLine($"git merge --ff-only origin/{branch} >> update.log 2>&1");
        sb.AppendLine("if errorlevel 1 (echo this folder has its own work, so the update stopped & pause & exit /b 1)");
        sb.AppendLine($"dotnet build {ProjectPath} -c Debug >> update.log 2>&1");
        sb.AppendLine("if errorlevel 1 (echo the build failed - read update.log & pause & exit /b 1)");
        sb.AppendLine($"start \"\" \"{ExePath}\"");
        return sb.ToString();
    }

    /// <summary>
    /// The rule the script is held to, stated once so the writer and the check cannot drift apart. The
    /// destructive verbs are refused outright: a fast-forward that cannot happen is a message, never a reset.
    /// </summary>
    public static readonly string[] ScriptMustNotContain =
        { "reset --hard", "clean -fd", "clean -df", "git checkout .", "git switch -C", "git work remove" };

    public static readonly string[] ScriptMustContain =
        { "git fetch", "--ff-only", "dotnet build", "start \"\"", "update.log" };
}

/// <summary>
/// Brings the updates made to ProBooks onto the PC that runs it, for the shops whose folder is the folder it
/// was built from - which is how this book has been looked after all along. It fetches, it fast-forwards, it
/// builds, and it starts the new one; it backs the books up first, and it never opens the data folder for
/// anything else than that.
/// </summary>
public sealed class UpdateService
{
    private const int FetchSeconds = 45;
    private readonly BackupService _backups;

    public UpdateService(BackupService backups) => _backups = backups;

    /// <summary>The source folder, found by walking up from where the running files are.</summary>
    public static string? FindRepoRoot()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch
        {
            // A folder that cannot be walked is a folder that is not a checkout, which the state below says.
        }
        return null;
    }

    public async Task<UpdateStatus> CheckAsync()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            var none = UpdateRules.Decide(false, false, false, true, false, false, AppInfo.Version, null);
            return new UpdateStatus { State = none.State, Message = none.Message, Folder = "", CurrentVersion = AppInfo.Version };
        }

        var haveGit = await Tool(root, "git --version") is not null;
        var haveSdk = await Tool(root, "dotnet --version") is not null;
        if (!haveGit)
        {
            return new UpdateStatus
            {
                State = UpdateState.NoTools,
                Folder = root,
                CurrentVersion = AppInfo.Version,
                Message = haveSdk
                    ? "This is the source folder, but git does not run on this PC, so nothing can be fetched."
                    : "Neither git nor the .NET SDK answered on this PC, so this folder cannot update itself."
            };
        }

        var branch = Or(await Tool(root, "git rev-parse --abbrev-ref HEAD"), "main");
        var fetch = await Tool(root, $"git fetch origin {branch}", FetchSeconds);
        var fetched = fetch is not null;
        var behind = fetched && ParseCount(await Tool(root, $"git rev-list --count HEAD..origin/{branch}")) > 0;
        var ahead = fetched && ParseCount(await Tool(root, $"git rev-list --count origin/{branch}..HEAD")) > 0;
        var dirty = fetched && !string.IsNullOrWhiteSpace(await Tool(root, "git status --porcelain"));
        var remoteProject = fetched ? await Tool(root, $"git show origin/{branch}:{UpdateRules.ProjectPath.Replace('\\', '/')}") : null;
        var remoteLog = fetched ? await Tool(root, $"git show origin/{branch}:CHANGELOG.md") : null;

        var current = AppInfo.Version;
        var remote = UpdateRules.VersionFromProject(remoteProject);
        var outcome = UpdateRules.Decide(haveGit, haveSdk, fetched, !dirty, behind, ahead, current, remote);
        return new UpdateStatus
        {
            State = outcome.State,
            Message = outcome.Message,
            CanApply = outcome.CanApply,
            ChangesWaiting = outcome.ChangesWaiting,
            CurrentVersion = current,
            RemoteVersion = remote ?? "",
            Notes = UpdateRules.NotesFrom(remoteLog),
            Folder = root,
        };
    }

    /// <summary>
    /// Backs the books up, hands the rest to a batch file, and closes ProBooks so the new one can be built over
    /// the old. The backup is not skipped for speed: a shop must never be one bad update away from a morning of
    /// retyping, whatever else an update does.
    /// </summary>
    public async Task<string> ApplyAsync()
    {
        var status = await CheckAsync();
        if (!status.CanApply)
            throw new InvalidOperationException(status.Message);
        var root = status.Folder;
        var branch = Or(await Tool(root, "git rev-parse --abbrev-ref HEAD"), "main");

        try
        {
            _backups.BackupNow("before update");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The books were not backed up ({ex.Message}), so the update was stopped before it began.");
        }

        var script = UpdateRules.BuildScript(root, branch, Environment.ProcessId, DateTime.Now);
        foreach (var banned in UpdateRules.ScriptMustNotContain)
        {
            if (script.Contains(banned, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The update was stopped: the script it would have run says \"{banned}\".");
        }
        foreach (var needed in UpdateRules.ScriptMustContain)
        {
            if (!script.Contains(needed, StringComparison.Ordinal))
                throw new InvalidOperationException($"The update was stopped: the script it would have run does not say \"{needed}\".");
        }
        // Nothing an update writes may name the folder holding the books. The backup above is the one thing
        // that reads it, and it is done here, in the app, where it can be seen.
        foreach (var path in new[] { DbPaths.DatabaseFile, DbPaths.PrintDirectory })
        {
            if (!string.IsNullOrWhiteSpace(path) && script.Contains(path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update was stopped: it would have reached into the folder holding the books.");
        }

        var file = Path.Combine(DbPaths.DirectoryPath, "update.cmd");
        await File.WriteAllTextAsync(file, script, Encoding.ASCII);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = file,
                WorkingDirectory = Path.GetDirectoryName(file),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The update could not be started ({ex.Message}). Your books were backed up and nothing else changed.");
        }
        return file;
    }

    private static string Or(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    private static int ParseCount(string? text) => int.TryParse((text ?? "").Trim(), out var n) && n > 0 ? n : 0;

    /// <summary>
    /// Runs a command and brings back what it printed, or null when it would not answer. It does not throw:
    /// a fetch that fails because the line is down is a fact about the shop's internet, not a fault to report
    /// as an exception at a woman who is trying to close her accounts.
    /// </summary>
    private static async Task<string?> Tool(string? workDir, string command, int seconds = 25)
    {
        try
        {
            var space = command.IndexOf(' ');
            var psi = new ProcessStartInfo
            {
                FileName = space < 0 ? command : command[..space],
                Arguments = space < 0 ? "" : command[(space + 1)..],
                WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
                return null;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(seconds * 1000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* it has gone already */ }
                return null;
            }
            var text = await outTask;
            if (p.ExitCode != 0 && string.IsNullOrWhiteSpace(text))
                text = await errTask;
            return text;
        }
        catch
        {
            return null;
        }
    }
}
