using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

/// <summary>The launcher's update log, runtime\logs\launcher-updates.log: every step of a check and an update.</summary>
public sealed class UpdateLog(string path)
{
    private readonly object _gate = new();

    public string Path { get; } = path;

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The log is a help, never a reason to stop.
            }
        }
    }
}

/// <summary>A checked package, unpacked into runtime\updates\staging, ready for the helper to copy over the game.</summary>
public sealed record PreparedUpdate(ReleaseVersion Version, string ZipPath, string PackageFolder);

// Checks for and applies Divided Realms updates for one game folder. Used by the launcher's window (MainForm.Updates)
// and by its headless switch (--apply-update). The steps:
// - Check: one request to GitHub's releases API, at most once a day unless the player asks (UpdatePolicy), and never
//   for a folder that isn't a release package (ReleaseFolder.Detect: the developer's test install never updates).
// - Prepare: the package downloaded to runtime\updates (resumed and retried), checked against its published SHA-256 and
//   PackageCheck, unpacked to runtime\updates\staging with every file checked against the package's own manifest.
// - Hand off: a copy of this launcher in runtime\updates\helper waits for the launcher to close, copies the package
//   over the game folder (UpdateInstaller: backups, rollback, the player's files kept), removes the download and the
//   unpacked package, and opens the launcher again.
// Nothing runs while the server, the game or another launcher does, and the player's saves are never touched.
// Ported from Living Server's LauncherUpdater (chris4aker/OfflineDAoC-LivingServer cc305fe00 and b1aba9156, GPLv3).
public sealed class LauncherUpdater
{
    /// <summary>Generic: names the program, nothing about the PC or the player.</summary>
    public const string UserAgent = "DividedRealms-Launcher";
    public const string ResultFile = "last-update.txt";
    public const string HelperFolder = "helper";
    public const string StagingFolder = "staging";
    public const string BackupsFolder = "backups";
    /// <summary>Free disk space needed before a download, in sizes of the zip: the zip, the unpacked package (about 3.5x) and the backups.</summary>
    public const double FreeSpaceFactor = 6.0;

    /// <summary>The programs whose running blocks an update: the server, the client and its connector, World Workshop, another launcher.</summary>
    public static readonly string[] BlockingProcesses =
        ["CoreServer", "DOLServer", "game", "camelot", "connect", "connect-local1130", "WorldWorkshop", "OfflineDAoC"];

    private static readonly Regex PackageName = new(@"^DividedRealms-(DR-dev-\d+(?:\.\d+)*[a-z]?)(-[A-Za-z0-9._-]+)?\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IReleaseWeb _web;

    public LauncherUpdater(string gameFolder, IReleaseWeb web, UpdateLog? log = null)
    {
        GameFolder = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        _web = web;
        Log = log ?? new UpdateLog(Path.Combine(GameFolder, "runtime", "logs", "launcher-updates.log"));
        Installed = ReleaseFolder.Detect(GameFolder, out string why);
        NotUpdatable = why;
        Settings = UpdateSettings.Load(UpdatesFolder);
    }

    public string GameFolder { get; }

    public string UpdatesFolder => Path.Combine(GameFolder, "runtime", "updates");

    public UpdateLog Log { get; }

    public UpdateSettings Settings { get; }

    /// <summary>The installed release, or null when this folder never updates itself (NotUpdatable says why).</summary>
    public InstalledRelease? Installed { get; }

    public string NotUpdatable { get; }

    public void SaveSettings()
    {
        try { Settings.Save(UpdatesFolder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Couldn't save the update settings: " + e.Message); }
    }

    /// <summary>
    /// Asks GitHub for the newest release when the rules allow (always when asked). Returns the release to offer, or
    /// null. problem says why nothing could be checked; an automatic check stays silent about it. A folder that isn't a
    /// release package makes no request at all.
    /// </summary>
    public ReleaseInfo? Check(bool asked, DateTime nowUtc, out string? problem)
    {
        problem = null;
        if (Installed == null)
        {
            problem = NotUpdatable;
            if (asked)
                Log.Write("Not checking for updates: " + NotUpdatable + ".");
            return null;
        }
        if (!UpdatePolicy.ShouldCheck(Settings, nowUtc, asked))
            return null;
        if (!asked)
        {
            Settings.LastCheckUtc = nowUtc;
            SaveSettings();
        }
        Log.Write($"Checking for a newer Divided Realms than {Installed.Version.Tag}{(asked ? " (asked)" : "")}: GET {ReleaseFeed.ListUrl}");
        ReleaseInfo? newest = ReleaseFeed.TryGetNewest(_web, out problem);
        if (newest == null)
        {
            Log.Write("No answer: " + problem);
            return null;
        }
        bool offer = UpdatePolicy.ShouldOffer(newest, Installed.Version, Settings, asked);
        Log.Write($"Newest release {newest.TagName} ({newest.Package?.Name}): {(offer ? "newer, offered" : "nothing to offer")}.");
        return offer ? newest : null;
    }

    public void Skip(ReleaseVersion version)
    {
        Settings.SkippedVersion = version.Tag;
        SaveSettings();
        Log.Write("The player skipped " + version.Tag + ".");
    }

    /// <summary>What blocks an update now ("the server", "game.exe"...); empty when it can go ahead.</summary>
    public static List<string> RunningParts(int ownProcessId, bool serverListening)
    {
        var running = new List<string>();
        if (serverListening)
            running.Add("the server");
        foreach (string name in BlockingProcesses)
        {
            Process[] found;
            try { found = Process.GetProcessesByName(name); }
            catch (InvalidOperationException) { continue; }
            int others = found.Count(p => p.Id != ownProcessId);
            foreach (Process p in found) p.Dispose();
            if (others > 0)
                running.Add(name switch
                {
                    "OfflineDAoC" => "another launcher",
                    "CoreServer" or "DOLServer" => "the server",
                    "WorldWorkshop" => "World Workshop",
                    _ => name + ".exe",
                });
        }
        return running.Distinct().ToList();
    }

    /// <summary>Downloads, checks and unpacks a release's package. Throws PackageRefusedException with the reason.</summary>
    public PreparedUpdate PrepareFromRelease(ReleaseInfo release, Action<string, long, long>? progress, CancellationToken cancel)
    {
        try
        {
            ReleaseVersion version = CheckUpdatable(release.Version, release.TagName);
            ReleaseAsset asset = release.Package ?? throw new PackageRefusedException("The " + release.TagName + " release has no package.");
            CheckFreeSpace(asset.Size);
            Log.Write($"Downloading {asset.Name} ({asset.Size:N0} bytes) to {UpdatesFolder}");
            string zip = ReleaseFeed.DownloadPackage(_web, release, UpdatesFolder, (d, t) => progress?.Invoke("download", d, t), cancel, Log.Write);
            Log.Write($"Downloaded and checked against its published SHA-256: {zip}");
            return Stage(version, zip, release.PackageFolderName!, progress, cancel);
        }
        catch (PackageRefusedException e)
        {
            Log.Write("REFUSED: " + e.Message);
            throw;
        }
    }

    /// <summary>
    /// Checks and unpacks a package already on disk (the --apply-update switch). Its SHA-256 must be given, or be in the
    /// .sha256 file beside it (as the release page has it, or as the launcher wrote it after a download).
    /// </summary>
    public PreparedUpdate PrepareFromFile(string zipPath, string? sha256, Action<string, long, long>? progress = null)
    {
        try
        {
            string zip = Path.GetFullPath(zipPath);
            string name = Path.GetFileName(zip);
            Match m = PackageName.Match(name);
            if (!m.Success)
                throw new PackageRefusedException("Can't tell which release " + name + " is: it should be named DividedRealms-<release>-<name>.zip.");
            ReleaseVersion version = CheckUpdatable(ReleaseVersion.Parse(m.Groups[1].Value), m.Groups[1].Value);
            if (!File.Exists(zip))
                throw new PackageRefusedException("There's no " + zip + ".");
            string? expected = PackageDigest.Sha256Of(sha256)
                               ?? (File.Exists(zip + ".sha256") ? PackageDigest.FromSha256File(File.ReadAllText(zip + ".sha256"), name) : null);
            DigestCheck check = PackageDigest.Check(zip, expected);
            if (check != DigestCheck.Match)
                throw new PackageRefusedException(check == DigestCheck.Missing
                    ? "No SHA-256 to check " + name + " against (give --sha256, or put its .sha256 file beside it)."
                    : name + " doesn't match the SHA-256 it should have.");
            Log.Write($"{zip}: SHA-256 matches {expected}");
            Directory.CreateDirectory(UpdatesFolder);
            string kept = Path.Combine(UpdatesFolder, name);
            if (!string.Equals(Path.GetFullPath(kept), zip, StringComparison.OrdinalIgnoreCase))
                File.Copy(zip, kept, true);
            ReleaseFeed.WriteSha256File(kept, expected!);
            return Stage(version, kept, name[..^".zip".Length], progress, CancellationToken.None);
        }
        catch (PackageRefusedException e)
        {
            Log.Write("REFUSED: " + e.Message);
            throw;
        }
    }

    private ReleaseVersion CheckUpdatable(ReleaseVersion? version, string? tag)
    {
        if (Installed == null)
            throw new PackageRefusedException("This folder doesn't update itself: " + NotUpdatable + ".");
        if (version == null)
            throw new PackageRefusedException("Not a Divided Realms release: " + tag);
        if (!version.IsNewerThan(Installed.Version))
            throw new PackageRefusedException($"{version.Tag} isn't newer than the installed {Installed.Version.Tag}.");
        return version;
    }

    private void CheckFreeSpace(long zipBytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(GameFolder)!);
            long need = (long)(zipBytes * FreeSpaceFactor);
            if (drive.IsReady && drive.AvailableFreeSpace < need)
                throw new PackageRefusedException($"The update needs about {need / 1073741824.0:0.0} GB free on {drive.Name} " +
                                                  $"(the download, the unpacked package and backups), and there are {drive.AvailableFreeSpace / 1073741824.0:0.0} GB.");
        }
        catch (ArgumentException) { /* not a local drive: go ahead */ }
    }

    // Checks the package, then unpacks it into runtime\updates\staging with every file checked against its manifest.
    private PreparedUpdate Stage(ReleaseVersion version, string zip, string topFolder, Action<string, long, long>? progress, CancellationToken cancel)
    {
        PackageContents contents;
        try
        {
            contents = PackageCheck.Inspect(zip, version, topFolder);
        }
        catch (PackageRefusedException)
        {
            PackageDigest.TryDelete(zip);
            PackageDigest.TryDelete(zip + ".sha256");
            throw;
        }
        string staging = Path.Combine(UpdatesFolder, StagingFolder);
        if (Directory.Exists(staging))
            Directory.Delete(staging, true);
        string unpacked = PackageCheck.Extract(zip, contents, staging, (d, t) => progress?.Invoke("unpack", d, t), cancel);
        Log.Write($"Unpacked to {unpacked}; all {contents.Manifest.Count:N0} files match the package's {ReleaseFolder.ManifestFile}.");
        return new PreparedUpdate(version, zip, unpacked);
    }

    /// <summary>
    /// Copies this launcher into runtime\updates\helper and starts it there with --finish-update. The caller closes the
    /// launcher right after: the helper waits for it (launcherProcessId; 0 to not wait), copies the package over the
    /// game folder and, with restart, opens the launcher again.
    /// </summary>
    public Process StartHandoff(PreparedUpdate update, int launcherProcessId, bool restart, bool quiet, string launcherFolder,
        int serverPort = 10330, bool checkRunning = true)
    {
        string helper = Path.Combine(UpdatesFolder, HelperFolder);
        if (Directory.Exists(helper))
            Directory.Delete(helper, true);
        Directory.CreateDirectory(helper);
        string source = Path.GetFullPath(launcherFolder);
        foreach (string file in Directory.GetFiles(source))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith("OfflineDAoC.", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(helper, name));
        }
        string runtimes = Path.Combine(source, "runtimes");
        if (Directory.Exists(runtimes))
        {
            foreach (string file in Directory.GetFiles(runtimes, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(helper, "runtimes", Path.GetRelativePath(runtimes, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        string exe = Path.Combine(helper, "OfflineDAoC.exe");
        if (!File.Exists(exe))
            throw new InvalidOperationException("The launcher has no OfflineDAoC.exe in " + source + " to run the update with.");
        string result = Path.Combine(UpdatesFolder, ResultFile);
        PackageDigest.TryDelete(result);

        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = helper };
        foreach (string arg in new[] { FinishUpdateCommand.Switch, "--pid", launcherProcessId.ToString(), "--package", update.PackageFolder,
                     "--game", GameFolder, "--version", update.Version.Tag, "--port", serverPort.ToString() })
            start.ArgumentList.Add(arg);
        if (restart) start.ArgumentList.Add("--restart");
        if (quiet) start.ArgumentList.Add("--quiet");
        if (!checkRunning) start.ArgumentList.Add("--no-running-check");
        SetBundledDotnet(start, GameFolder);
        Log.Write($"Handing over to {exe}: it waits for the launcher (process {launcherProcessId}) to close, copies {update.PackageFolder} " +
                  $"over {GameFolder}{(restart ? " and opens the launcher again" : "")}.");
        return Process.Start(start) ?? throw new InvalidOperationException("Windows didn't start the update helper.");
    }

    /// <summary>The game server's port from runtime\modern-client.json (10330 when it can't be read).</summary>
    public static int ServerPort(string gameFolder)
    {
        try
        {
            string path = Path.Combine(gameFolder, "runtime", "modern-client.json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("Port", out var p) && p.TryGetInt32(out int port) && port is > 0 and < 65536 ? port : 10330;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return 10330;
        }
    }

    /// <summary>The game's own .NET, as START DIVIDED REALMS.cmd sets it (when the folder has one).</summary>
    public static void SetBundledDotnet(ProcessStartInfo start, string gameFolder)
    {
        string dotnet = Path.Combine(gameFolder, "tools", "dotnet");
        if (!File.Exists(Path.Combine(dotnet, "dotnet.exe")))
            return;
        start.Environment["DOTNET_ROOT"] = dotnet;
        start.Environment["DOTNET_ROOT_X64"] = dotnet;
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
    }

    /// <summary>The last update's result as the helper wrote it (once; the file is then marked as shown).</summary>
    public IReadOnlyDictionary<string, string>? TakeLastResult()
    {
        string path = Path.Combine(UpdatesFolder, ResultFile);
        try
        {
            if (!File.Exists(path))
                return null;
            var info = ReleaseFolder.KeyValues(File.ReadAllText(path));
            File.Move(path, path + ".shown", true);
            return info;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// After an install: deletes the unpacked package, the downloaded packages (about 1.2 GB each; the backups, not the
    /// zip, undo an update), and all but the newest two update backups.
    /// </summary>
    public void CleanUp()
    {
        void Try(Action action, string what)
        {
            try { action(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Couldn't remove " + what + ": " + e.Message); }
        }
        string staging = Path.Combine(UpdatesFolder, StagingFolder);
        if (Directory.Exists(staging))
            Try(() => Directory.Delete(staging, true), staging);
        if (!Directory.Exists(UpdatesFolder))
            return;
        foreach (string file in Directory.GetFiles(UpdatesFolder, "DividedRealms-*"))
        {
            if (PackageName.IsMatch(Regex.Replace(Path.GetFileName(file), @"\.(sha256|part)$", "", RegexOptions.IgnoreCase)))
            {
                Try(() => File.Delete(file), file);
                Log.Write("Removed the download " + Path.GetFileName(file) + ".");
            }
        }
        string backups = Path.Combine(UpdatesFolder, BackupsFolder);
        if (Directory.Exists(backups))
        {
            foreach (string old in Directory.GetDirectories(backups).OrderByDescending(d => Directory.GetCreationTimeUtc(d)).ThenByDescending(d => d).Skip(2))
                Try(() => Directory.Delete(old, true), old);
        }
    }
}
