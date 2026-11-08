using System.Diagnostics;

namespace OfflineDaoc.Updates;

// OfflineDAoC.exe --finish-update --pid <launcher> --package <unpacked package> --game <game folder> --version <release>
//                 [--port <server port>] [--restart] [--quiet] [--no-running-check]
//
// The update helper: a copy of the launcher in runtime\updates\helper, started by LauncherUpdater.StartHandoff. It
// waits for the launcher to close, refuses while the server, a client or another launcher runs, copies the package over
// the game folder (UpdateInstaller, with backups in runtime\updates\backups and a rollback on any failure), writes the
// result to runtime\updates\last-update.txt for the launcher to show, and opens the launcher again. A small window shows
// the progress unless --quiet. Exit codes: 0 installed, 1 failed (rolled back), 3 the game runs, 4 bad arguments.
// It replaces Living Server's apply-update.ps1 (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3): no PowerShell,
// so no execution policy can stop it.
public static class FinishUpdateCommand
{
    public const string Switch = "--finish-update";

    public sealed record Options(int LauncherPid, string PackageFolder, string GameFolder, ReleaseVersion Version, int Port,
        bool Restart, bool Quiet, bool CheckRunning);

    public static Options? Parse(string[] args)
    {
        string? Get(string key)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string key) => args.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        if (Get("--package") is not { } package || Get("--game") is not { } game || ReleaseVersion.Parse(Get("--version")) is not { } version)
            return null;
        int pid = int.TryParse(Get("--pid"), out int p) ? p : 0;
        int port = int.TryParse(Get("--port"), out int q) ? q : 10330;
        return new Options(pid, package, game, version, port, Has("--restart"), Has("--quiet"), !Has("--no-running-check"));
    }

    public static int Run(string[] args)
    {
        Options? options = Parse(args);
        if (options == null)
        {
            Console.WriteLine("Usage: OfflineDAoC.exe --finish-update --pid <id> --package <folder> --game <folder> --version DR-dev-<n> [--port <n>] [--restart] [--quiet]");
            return 4;
        }
        if (options.Quiet)
            return Finish(options, null);
        int code = 1;
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var window = new UpdateProgressWindow("Updating Divided Realms to " + options.Version.Tag);
        window.Shown += async (_, _) =>
        {
            code = await Task.Run(() => Finish(options, (text, done, total) => window.Report(text, done, total)));
            window.Close();
        };
        Application.Run(window);
        return code;
    }

    /// <summary>Does the update. report gets (what, files done, files total) for a window.</summary>
    public static int Finish(Options o, Action<string, int, int>? report)
    {
        string game = Path.GetFullPath(o.GameFolder).TrimEnd('\\', '/');
        string updates = Path.Combine(game, "runtime", "updates");
        var log = new UpdateLog(Path.Combine(game, "runtime", "logs", "launcher-updates.log"));
        void Say(string line)
        {
            Console.WriteLine(line);
            log.Write("helper: " + line);
        }
        string from = ReleaseFolder.Detect(game, out _)?.Version.Tag ?? "(unknown)";
        string backup = "";
        int code;
        string state, detail;
        try
        {
            Say($"Update from {from} to {o.Version.Tag}: {o.PackageFolder} over {game}");
            report?.Invoke("Waiting for the launcher to close…", 0, 0);
            if (!WaitForExit(o.LauncherPid, TimeSpan.FromMinutes(2)))
                throw new BlockedException("the launcher didn't close within two minutes; nothing was changed. Close it and try again.");
            if (o.CheckRunning)
            {
                List<string> running = LauncherUpdater.RunningParts(Environment.ProcessId, o.Port > 0 && Launcher.ServerPortProbe.IsListening(o.Port));
                if (running.Count > 0)
                    throw new BlockedException($"{string.Join(", ", running)} is running; nothing was changed. Close it and install the update again from the launcher.");
            }
            CheckPackage(o, game);
            backup = Path.Combine(updates, LauncherUpdater.BackupsFolder, $"{from}-to-{o.Version.Tag}-{DateTime.Now:yyyyMMdd-HHmmss}");
            report?.Invoke("Copying the new files (your characters and world are kept)…", 0, 0);
            InstallResult result = UpdateInstaller.Install(o.PackageFolder, game, backup, Say,
                (done, total) => report?.Invoke("Copying the new files (your characters and world are kept)…", done, total));
            File.WriteAllText(Path.Combine(backup, "README.txt"),
                $"Divided Realms update from {from} to {o.Version.Tag}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}.{Environment.NewLine}" +
                $"files\\ holds the {result.Replaced} files it replaced; journal.txt lists every file it replaced or added ({result.Added} added).{Environment.NewLine}" +
                $"To undo it, close the launcher, the server and the game, then double-click ROLLBACK.cmd in this folder.{Environment.NewLine}" +
                $"Your world database, account.txt, logs and settings were not touched by the update.{Environment.NewLine}" +
                $"The launcher adds {o.Version.Tag}'s world fixes to your world on its next start, after a backup of the world in " +
                $"runtime\\backups\\world-update-*. A rollback puts back the game's files only; to put the world back too, copy " +
                $"that backup's opendaoc.sqlite3.db over runtime\\data\\opendaoc.sqlite3.db (you lose what you played since).{Environment.NewLine}");
            if (ReleaseVersion.Parse(from) is { } fromVersion)
                File.WriteAllText(Path.Combine(backup, "ROLLBACK.cmd"), RollbackScript(fromVersion, o.Version));
            state = "installed";
            detail = $"Divided Realms {o.Version.Tag} is installed ({result.Replaced} files replaced, {result.Added} added, {result.Unchanged} already the same).";
            code = 0;
            new LauncherUpdater(game, new NoWeb(), log).CleanUp();
        }
        catch (BlockedException e)
        {
            state = "not installed"; detail = e.Message; code = 3;
        }
        catch (Exception e)
        {
            state = "not installed"; detail = "the update stopped: " + e.Message; code = 1;
            try { if (Directory.Exists(o.PackageFolder)) Directory.Delete(o.PackageFolder, true); }
            catch (Exception) { /* the next update replaces it */ }
        }
        Say($"{state}: {detail}");
        try
        {
            Directory.CreateDirectory(updates);
            File.WriteAllLines(Path.Combine(updates, LauncherUpdater.ResultFile),
            [
                "Version=" + o.Version.Tag, "From=" + from, "Result=" + state, "Detail=" + detail.Replace('\r', ' ').Replace('\n', ' '),
                "Backup=" + backup, "Time=" + DateTime.UtcNow.ToString("o"),
            ]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say("Couldn't write the result: " + e.Message); }
        if (o.Restart)
            Restart(game, Say);
        return code;
    }

    private static void CheckPackage(Options o, string game)
    {
        string package = Path.GetFullPath(o.PackageFolder);
        string staging = Path.GetFullPath(Path.Combine(game, "runtime", "updates", LauncherUpdater.StagingFolder)) + Path.DirectorySeparatorChar;
        if (!package.StartsWith(staging, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(package))
            throw new UpdateFailedException("the package folder isn't one the launcher unpacked in runtime\\updates\\staging; nothing was changed.");
        ReleaseVersion? named = ReleaseFolder.ReadVersion(name =>
        {
            string path = Path.Combine(package, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }, out _);
        if (named == null || !named.SameAs(o.Version))
            throw new UpdateFailedException($"the package folder isn't {o.Version.Tag}'s; nothing was changed.");
    }

    private static bool WaitForExit(int pid, TimeSpan timeout)
    {
        if (pid <= 0 || pid == Environment.ProcessId)
            return true;
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
    }

    private static void Restart(string game, Action<string> say)
    {
        string launcher = Path.Combine(game, "runtime", "OfflineDAoC.exe");
        if (!File.Exists(launcher))
            return;
        try
        {
            var start = new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = game };
            LauncherUpdater.SetBundledDotnet(start, game);
            Process.Start(start)?.Dispose();
            say("Opened the launcher again: " + launcher);
        }
        catch (Exception e)
        {
            say("Couldn't open the launcher again (" + e.Message + "); start it with START DIVIDED REALMS.cmd.");
        }
    }

    // OfflineDAoC.exe --rollback-update <backup folder> [--game <folder>] [--no-running-check]: undoes an update from its
    // backup (UpdateInstaller.Rollback). The game is the folder the backup is in (<game>\runtime\updates\backups\<it>)
    // unless --game names it. Each backup's ROLLBACK.cmd runs this with the helper copy, which the rollback doesn't replace.
    public static int RunRollback(string[] args, string serverRoot)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, "--rollback-update", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length || !Directory.Exists(args[i + 1]))
        {
            Console.WriteLine("Usage: OfflineDAoC.exe --rollback-update <runtime\\updates\\backups\\...> [--game <folder>]");
            return 4;
        }
        string backup = Path.GetFullPath(args[i + 1]).TrimEnd('\\', '/');
        int g = Array.FindIndex(args, a => string.Equals(a, "--game", StringComparison.OrdinalIgnoreCase));
        string game = g >= 0 && g + 1 < args.Length ? Path.GetFullPath(args[g + 1]) : GameOfBackup(backup) ?? ReleaseFolder.GameFolderFor(serverRoot);
        var log = new UpdateLog(Path.Combine(game, "runtime", "logs", "launcher-updates.log"));
        List<string> running = args.Contains("--no-running-check", StringComparer.OrdinalIgnoreCase) ? []
            : LauncherUpdater.RunningParts(Environment.ProcessId, Launcher.ServerPortProbe.IsListening(LauncherUpdater.ServerPort(game)));
        if (running.Count > 0)
        {
            Console.WriteLine("Close " + string.Join(", ", running) + " first.");
            return 3;
        }
        try
        {
            Console.WriteLine($"Rolling back the update in {game} from {backup}...");
            int count = UpdateInstaller.Rollback(game, backup, line => { Console.WriteLine(line); log.Write(line); });
            Console.WriteLine($"Put back {count} files.");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAILED: " + e.Message);
            return 1;
        }
    }

    /// <summary>The game folder of a backup in &lt;game&gt;\runtime\updates\backups\&lt;backup&gt;, or null.</summary>
    public static string? GameOfBackup(string backup)
    {
        DirectoryInfo? backups = new DirectoryInfo(backup).Parent, updates = backups?.Parent, runtime = updates?.Parent;
        bool shaped = string.Equals(backups?.Name, LauncherUpdater.BackupsFolder, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(updates?.Name, "updates", StringComparison.OrdinalIgnoreCase)
                      && string.Equals(runtime?.Name, "runtime", StringComparison.OrdinalIgnoreCase);
        return shaped ? runtime!.Parent?.FullName : null;
    }

    /// <summary>The backup's ROLLBACK.cmd: the helper copy (which a rollback never replaces) on the game's own .NET.</summary>
    public static string RollbackScript(ReleaseVersion from, ReleaseVersion to) =>
        string.Join("\r\n",
            "@echo off",
            "setlocal",
            $"rem Undoes the Divided Realms update from {from.Tag} to {to.Tag}. Close the launcher, the server and the game first.",
            $"rem Your world, account.txt, logs and settings are not touched (the update didn't touch them either).",
            "cd /d \"%~dp0\"",
            "set \"GAME=%~dp0..\\..\\..\\..\"",
            "if exist \"%GAME%\\tools\\dotnet\\dotnet.exe\" (",
            "  set \"DOTNET_ROOT=%GAME%\\tools\\dotnet\"",
            "  set \"DOTNET_ROOT_X64=%GAME%\\tools\\dotnet\"",
            "  set \"DOTNET_MULTILEVEL_LOOKUP=0\"",
            ")",
            "set \"LAUNCHER=%GAME%\\runtime\\updates\\helper\\OfflineDAoC.exe\"",
            "if not exist \"%LAUNCHER%\" (",
            "  echo The update helper isn't in runtime\\updates\\helper any more. Copy runtime\\OfflineDAoC.* there, or extract the old",
            "  echo release over this folder instead.",
            "  pause",
            "  exit /b 1",
            ")",
            "\"%LAUNCHER%\" --rollback-update \"%~dp0.\" --game \"%GAME%\" | more",
            "pause",
            "");

    private sealed class BlockedException(string message) : Exception(message);

    /// <summary>The helper makes no request: CleanUp needs no internet.</summary>
    private sealed class NoWeb : IReleaseWeb
    {
        public string GetText(string url, TimeSpan timeout) => throw new InvalidOperationException("no network in the update helper");
        public void Download(string url, string path, Action<long, long>? progress, CancellationToken cancel) => throw new InvalidOperationException("no network in the update helper");
    }
}

/// <summary>The helper's small window: what it is doing and how far.</summary>
internal sealed class UpdateProgressWindow : Form
{
    private readonly Label _text = new() { Dock = DockStyle.Top, Height = 44, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0) };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 18, Style = ProgressBarStyle.Marquee };

    public UpdateProgressWindow(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 90);
        Font = new Font("Segoe UI", 9f);
        var pad = new Panel { Dock = DockStyle.Top, Height = 12 };
        Controls.Add(pad);
        Controls.Add(_bar);
        Controls.Add(_text);
        _text.Text = title + "…";
    }

    public void Report(string text, int done, int total)
    {
        if (!IsHandleCreated)
            return;
        BeginInvoke(() =>
        {
            _text.Text = total > 0 ? $"{text} {done:N0} of {total:N0} files" : text;
            if (total > 0)
            {
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Maximum = total;
                _bar.Value = Math.Min(done, total);
            }
        });
    }
}
