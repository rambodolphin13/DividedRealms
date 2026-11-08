namespace OfflineDaoc.Updates;

// OfflineDAoC.exe --apply-update <DividedRealms-<release>-<name>.zip> [--sha256 <hex>] [--game <folder>] [--no-restart]
//                 [--quiet] [--wait] [--no-running-check]
//
// The update path without the window or the dialog, for tests and support: the same checks and hand-over as the
// launcher's Install button, from a package already on disk. Its SHA-256 must be given (as the release page lists it)
// or be in the .sha256 file beside it. The game folder is the one this launcher belongs to unless --game names another;
// it must be a release package newer than nothing it is given (ReleaseFolder.Detect). --wait runs the helper without
// waiting for this process and waits for its result (for tests). --no-running-check is for tests on a PC where the game
// runs elsewhere. Exit codes: 0 handed over (or, with --wait, installed), 2 refused (nothing changed), 3 the game runs,
// 4 bad arguments, 1 failed.
// Ported from Living Server's ApplyUpdateCommand (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3).
public static class ApplyUpdateCommand
{
    public const string Switch = "--apply-update";

    public static int Run(string[] args, string serverRoot, string launcherFolder)
    {
        string? Get(string key)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string key) => args.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));

        string? zip = Get(Switch);
        if (zip == null)
        {
            Console.WriteLine("Usage: OfflineDAoC.exe --apply-update <DividedRealms-<release>-<name>.zip> [--sha256 <hex>] [--game <folder>] [--no-restart] [--quiet] [--wait]");
            return 4;
        }
        string game = Get("--game") ?? ReleaseFolder.GameFolderFor(serverRoot);
        using var web = new HttpReleaseWeb(LauncherUpdater.UserAgent);
        var updater = new LauncherUpdater(game, web);
        void Say(string line)
        {
            Console.WriteLine(line);
            updater.Log.Write(line);
        }
        Say($"{Switch} {zip} for the game in {updater.GameFolder} (installed: {updater.Installed?.Version.Tag ?? "not a release package"})");
        try
        {
            bool checkRunning = !Has("--no-running-check");
            int port = LauncherUpdater.ServerPort(game);
            if (checkRunning)
            {
                List<string> running = LauncherUpdater.RunningParts(Environment.ProcessId, Launcher.ServerPortProbe.IsListening(port));
                if (running.Count > 0)
                {
                    Say("Not updating while running: " + string.Join(", ", running));
                    return 3;
                }
            }
            PreparedUpdate prepared = updater.PrepareFromFile(zip, Get("--sha256"));
            bool wait = Has("--wait");
            using var helper = updater.StartHandoff(prepared, wait ? 0 : Environment.ProcessId, restart: !Has("--no-restart"),
                quiet: Has("--quiet") || wait, launcherFolder, port, checkRunning);
            Say($"Handed over to the update helper (process {helper.Id}); its result goes to {Path.Combine(updater.UpdatesFolder, LauncherUpdater.ResultFile)}");
            if (!wait)
                return 0;
            helper.WaitForExit();
            string result = Path.Combine(updater.UpdatesFolder, LauncherUpdater.ResultFile);
            Say(File.Exists(result) ? File.ReadAllText(result).Trim() : "The helper wrote no result.");
            return helper.ExitCode;
        }
        catch (PackageRefusedException e)
        {
            Say("REFUSED, nothing changed: " + e.Message);
            return 2;
        }
        catch (Exception e)
        {
            Say("FAILED: " + e.Message);
            return 1;
        }
    }
}
