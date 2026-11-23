using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

/// <summary>A game folder that is an installed Divided Realms release package, and which release.</summary>
public sealed record InstalledRelease(ReleaseVersion Version, string GameFolder, string Source);

// How a Divided Realms folder knows its own release, and whether it may update itself at all.
// A release package (build_modern_test_package.py) has, at its top:
// - RELEASE.txt, the version stamp ("Version=DR-dev-0.1d", from DR-dev-0.1d on), or in older packages only EDITION.txt
//   ("Divided Realms DR-dev-0.1b Fishing Update: ..."), whose first word after "Divided Realms" is the release;
// - PACKAGE MANIFEST.sha256, the SHA-256 of every file the package shipped;
// - runtime\OfflineDAoC.exe, this launcher.
// Anything else is not a release package and never updates itself: the developer's test install
// (artifacts\modern-client-test, hand-installed files, no stamp or manifest), any folder inside a Git checkout, a
// classic 0.3x install.
public static class ReleaseFolder
{
    public const string StampFile = "RELEASE.txt";
    public const string EditionFile = "EDITION.txt";
    public const string ManifestFile = "PACKAGE MANIFEST.sha256";

    private static readonly Regex EditionShape = new(@"^\s*Divided Realms\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The game folder a launcher belongs to: the parent of the runtime folder its server lives in.</summary>
    public static string GameFolderFor(string runtimeFolder) =>
        Path.GetDirectoryName(Path.GetFullPath(runtimeFolder).TrimEnd('\\', '/')) ?? runtimeFolder;

    /// <summary>The release a package's RELEASE.txt or EDITION.txt names (read through text), or null.</summary>
    public static ReleaseVersion? ReadVersion(Func<string, string?> text, out string source)
    {
        source = "";
        if (text(StampFile) is { } stamp && KeyValues(stamp).TryGetValue("Version", out string? named))
        {
            source = StampFile;
            return ReleaseVersion.Parse(named);
        }
        if (text(EditionFile) is { } edition && EditionShape.Match(edition.TrimStart('﻿')) is { Success: true } m)
        {
            source = EditionFile;
            return ReleaseVersion.Parse(m.Groups[1].Value);
        }
        return null;
    }

    /// <summary>The installed release, or null with the reason this folder never updates itself.</summary>
    public static InstalledRelease? Detect(string gameFolder, out string why)
    {
        string game = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        string? unreadable = null;
        string? Read(string name)
        {
            string path = Path.Combine(game, name);
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                unreadable ??= name + " (" + e.Message.TrimEnd('.') + ")";
                return null;
            }
        }
        ReleaseVersion? version = ReadVersion(Read, out string source);
        if (version == null)
        {
            // DR-524: a real release folder synced by OneDrive could fail here, and the player only saw "isn't one".
            why = unreadable != null
                ? "this folder's " + unreadable + " couldn't be read" + CloudHint(game)
                : "this folder isn't a Divided Realms release package (it has no " + StampFile + " or " + EditionFile +
                  " naming a DR-dev release)" + CloudHint(game);
            return null;
        }
        if (!File.Exists(Path.Combine(game, ManifestFile)))
        {
            why = "this folder has no " + ManifestFile + ", so it isn't an unpacked release package" + CloudHint(game);
            return null;
        }
        if (!File.Exists(Path.Combine(game, "runtime", "OfflineDAoC.exe")))
        {
            why = "this folder has no runtime\\OfflineDAoC.exe" + CloudHint(game);
            return null;
        }
        if (InsideGitCheckout(game) is { } repo)
        {
            why = "this folder is inside a Git checkout (" + repo + "): a developer's folder never updates itself. " +
                  "Move the game folder somewhere outside it, such as C:\\Divided Realms";
            return null;
        }
        why = "";
        return new InstalledRelease(version, game, source);
    }

    /// <summary>
    /// Advice added to a refusal when the folder is under OneDrive (or another synced folder Windows names that way): files
    /// there can be online-only or locked while syncing. Moving the folder out fixed it for a player (Discord 2026-11-22).
    /// Pure.
    /// </summary>
    public static string CloudHint(string folder) =>
        folder.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) || folder.Contains("Dropbox", StringComparison.OrdinalIgnoreCase)
            ? ". This folder is synced by OneDrive or Dropbox, which can keep files online-only or locked: move the game folder " +
              "out of it, for example to C:\\Divided Realms"
            : "";

    /// <summary>The Git checkout a folder is in (a ".git" folder or file in it or above it), or null.</summary>
    public static string? InsideGitCheckout(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
        {
            string git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir.FullName;
        }
        return null;
    }

    /// <summary>
    /// PACKAGE MANIFEST.sha256 as path (with "/") to lower-case SHA-256. Lines are "&lt;hex&gt;  &lt;path&gt;" (sha256sum);
    /// anything else is skipped.
    /// </summary>
    public static Dictionary<string, string> ReadManifest(string? text)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in (text ?? "").TrimStart('﻿').Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length < 66 || line[64] != ' ')
                continue;
            string? sha = PackageDigest.Sha256Of(line[..64]);
            string path = line[65..].TrimStart(' ', '*').Replace('\\', '/');
            if (sha != null && path.Length > 0)
                files[path] = sha;
        }
        return files;
    }

    /// <summary>Key=value lines (RELEASE.txt, the update result file).</summary>
    public static Dictionary<string, string> KeyValues(string text)
    {
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n'))
        {
            Match m = Regex.Match(line, @"^\s*([^=#]+?)\s*=\s*(.*?)\s*$");
            if (m.Success)
                info[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return info;
    }
}
