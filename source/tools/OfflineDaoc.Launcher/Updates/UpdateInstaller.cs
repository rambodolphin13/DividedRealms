using System.Security.Cryptography;

namespace OfflineDaoc.Updates;

/// <summary>Why an update stopped; the game folder was put back as it was (Rollback).</summary>
public sealed class UpdateFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What an install did.</summary>
public sealed record InstallResult(int Replaced, int Added, int Unchanged, IReadOnlyList<string> KeptPlayerFiles, string BackupFolder);

// Which files of a game folder are the player's. Paths are relative to the game folder, with "/".
public static class PlayerFiles
{
    /// <summary>Never written, replaced or deleted by an update, and a package holding one is refused.</summary>
    private static readonly string[] DataFiles =
    [
        "runtime/data/opendaoc.sqlite3.db", "runtime/data/opendaoc.sqlite3.db-wal", "runtime/data/opendaoc.sqlite3.db-shm",
        "runtime/data/opendaoc.sqlite3.db-journal", "runtime/account.txt",
    ];

    /// <summary>Folders an update never writes into: saves' backups, logs, the client copy, the updater's own folder.</summary>
    private static readonly string[] DataFolders =
    [
        "runtime/data/backups/", "runtime/backups/", "runtime/server-backups/", "runtime/logs/", "runtime/server/logs/",
        "runtime/updates/", "runtime/client-modern/", "runtime/herald/", "runtime/world-workshop-data/",
    ];

    /// <summary>
    /// The player's settings. A package may ship a default; the update writes it only where the player has none or
    /// still has the one the installed package shipped (unchanged since, by its PACKAGE MANIFEST.sha256 hash).
    /// </summary>
    private static readonly string[] Settings =
    [
        "runtime/server/config/serverconfig.xml", "runtime/server/config/serverconfig.before-host-for-friends.xml",
        "runtime/overlay.json", "runtime/host-for-friends.json", "runtime/launcher-ui-scale.txt",
        "runtime/server/bot-goals.json", "runtime/server/bot-world.json", "runtime/server/rvr-world.json",
        "runtime/server/bot-diagnostics.on",
    ];

    public static string Normalize(string relative) => relative.Replace('\\', '/').TrimStart('/');

    public static bool IsPlayerData(string relative)
    {
        string path = Normalize(relative);
        return DataFiles.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase))
               || DataFolders.Any(d => path.StartsWith(d, StringComparison.OrdinalIgnoreCase))
               || path.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlayerSetting(string relative) =>
        Settings.Any(f => string.Equals(f, Normalize(relative), StringComparison.OrdinalIgnoreCase));
}

// Copies an unpacked package over a game folder: the documented upgrade ("extract over your folder and replace every
// file"), done by the launcher's helper once the launcher has closed. Rules:
// - the player's data (PlayerFiles.IsPlayerData: the world database, account.txt, logs, backups, the client copy) is
//   never touched, and a player's changed settings are kept (PlayerFiles.IsPlayerSetting);
// - files already the same are left alone (so files in use, like the bundled .NET the helper runs on, aren't rewritten);
// - every replaced file is backed up first, and every step is written to a journal in the backup folder before it is
//   done; on any failure everything done is undone (Rollback), so the game is as it was;
// - nothing is deleted from the game folder (as extracting over it doesn't).
// The world database is brought up to the release by the launcher's own WorldUpdates on its next start, and the
// client copy is patched again by ModernClientSetup, as after a manual upgrade.
// Replaces Living Server's apply-update.ps1 + INSTALL.cmd step (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3):
// our packages are whole games without an INSTALL.cmd, and C# needs no PowerShell execution policy.
public static class UpdateInstaller
{
    public const string JournalFile = "journal.txt";
    public const string BackupFilesFolder = "files";
    private const string NewSuffix = ".dr-update-new";

    /// <summary>Test hook: called before each file is written (relative path), to make a write fail on purpose.</summary>
    public static Action<string>? BeforeWrite { get; set; }

    public static InstallResult Install(string packageFolder, string gameFolder, string backupFolder, Action<string> log,
        Action<int, int>? progress = null)
    {
        string package = Path.GetFullPath(packageFolder).TrimEnd('\\', '/');
        string game = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        string newManifestPath = Path.Combine(package, ReleaseFolder.ManifestFile);
        if (!File.Exists(newManifestPath))
            throw new UpdateFailedException("The package has no " + ReleaseFolder.ManifestFile + "; nothing was changed.");
        Dictionary<string, string> oldManifest = ReadManifestFile(Path.Combine(game, ReleaseFolder.ManifestFile));

        // The plan first, so nothing is written for a package that can't be installed.
        var replace = new List<string>();
        var add = new List<string>();
        var kept = new List<string>();
        int unchanged = 0;
        foreach (string file in Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string rel = PlayerFiles.Normalize(Path.GetRelativePath(package, file));
            string target = Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar));
            if (PlayerFiles.IsPlayerData(rel))
            {
                kept.Add(rel);
                continue;
            }
            if (!File.Exists(target))
            {
                add.Add(rel);
                continue;
            }
            if (SameContent(file, target))
            {
                unchanged++;
                continue;
            }
            if (PlayerFiles.IsPlayerSetting(rel) &&
                !(oldManifest.TryGetValue(rel, out string? shipped) && shipped == PackageDigest.FileSha256(target)))
            {
                kept.Add(rel);
                continue;
            }
            replace.Add(rel);
        }
        log($"Plan: {replace.Count} files replaced (backed up first), {add.Count} added, {unchanged} already the same, " +
            $"{kept.Count} of the player's kept as they are{(kept.Count > 0 ? " (" + string.Join(", ", kept.Take(8)) + (kept.Count > 8 ? ", ..." : "") + ")" : "")}.");

        Directory.CreateDirectory(Path.Combine(backupFolder, BackupFilesFolder));
        string journal = Path.Combine(backupFolder, JournalFile);
        int total = replace.Count + add.Count, done = 0;
        try
        {
            using var writer = new StreamWriter(journal, append: false) { AutoFlush = true };
            // The version stamp and manifest last: until they are written, the folder still names the release it was.
            foreach (var (rel, isNew) in replace.Select(r => (r, false)).Concat(add.Select(a => (a, true))).OrderBy(x => IsManifest(x.Item1)))
            {
                string source = Path.Combine(package, rel.Replace('/', Path.DirectorySeparatorChar));
                string target = Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!isNew)
                {
                    string saved = Path.Combine(backupFolder, BackupFilesFolder, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(target, saved, true);
                }
                writer.WriteLine((isNew ? "added\t" : "replaced\t") + rel);
                BeforeWrite?.Invoke(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + NewSuffix;
                File.Copy(source, temporary, true);
                if (File.Exists(target) && File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
                File.Move(temporary, target, true);
                progress?.Invoke(++done, total);
            }
            writer.WriteLine("done");
        }
        catch (Exception e)
        {
            log($"Stopped at file {done + 1} of {total}: {e.Message}. Putting back what was changed...");
            int undone = Rollback(game, backupFolder, log);
            throw new UpdateFailedException($"The update stopped ({e.Message}). The {undone} files it had changed were put back, so the game is as it was.", e);
        }
        return new InstallResult(replace.Count, add.Count, unchanged, kept, backupFolder);
    }

    private static bool IsManifest(string rel) =>
        new[] { ReleaseFolder.StampFile, ReleaseFolder.EditionFile, ReleaseFolder.ManifestFile }.Contains(rel, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Undoes an install from its backup folder's journal, newest step first: replaced files are copied back, added ones
    /// deleted. Returns how many files were put back. Used on a failed install, and by --rollback-update.
    /// </summary>
    public static int Rollback(string gameFolder, string backupFolder, Action<string> log)
    {
        string game = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        string journal = Path.Combine(backupFolder, JournalFile);
        if (!File.Exists(journal))
            return 0;
        int count = 0;
        var problems = new List<string>();
        foreach (string line in File.ReadAllLines(journal).Reverse())
        {
            int tab = line.IndexOf('\t');
            if (tab < 0)
                continue;
            string kind = line[..tab], rel = line[(tab + 1)..];
            if (PlayerFiles.IsPlayerData(rel) || rel.Contains(".."))
                continue;
            string target = Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                PackageDigest.TryDelete(target + NewSuffix);
                if (kind == "replaced")
                {
                    File.Copy(Path.Combine(backupFolder, BackupFilesFolder, rel.Replace('/', Path.DirectorySeparatorChar)), target, true);
                    count++;
                }
                else if (kind == "added" && File.Exists(target))
                {
                    File.Delete(target);
                    count++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add(rel + " (" + e.Message + ")");
            }
        }
        File.Move(journal, journal + ".rolled-back", true);
        log($"Rolled back {count} files from {backupFolder}." + (problems.Count > 0 ? " Couldn't put back: " + string.Join("; ", problems) : ""));
        if (problems.Count > 0)
            throw new UpdateFailedException("Rolling back left " + problems.Count + " files as they were after the update: " + string.Join("; ", problems));
        return count;
    }

    private static Dictionary<string, string> ReadManifestFile(string path)
    {
        try { return File.Exists(path) ? ReleaseFolder.ReadManifest(File.ReadAllText(path)) : new(StringComparer.OrdinalIgnoreCase); }
        catch (IOException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static bool SameContent(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length)
            return false;
        try
        {
            using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
        }
        catch (IOException)
        {
            return false; // unreadable: replace it (a failure there rolls back)
        }
    }
}
