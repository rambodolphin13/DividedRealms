#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher.Tests.Updates;

// Shared by the update tests: a fake internet (recorded answers, no network), throwaway folders, and small packages and
// game folders laid out like the real ones (DividedRealms-<release>-<name>/ with RELEASE.txt, PACKAGE MANIFEST.sha256,
// runtime\OfflineDAoC.exe, runtime\server\...). Adapted from Living Server's UpdateTestSupport (cc305fe00, GPLv3).
internal sealed class FakeReleaseWeb : IReleaseWeb
{
    public string? Json;
    public Exception? GetFails;
    public readonly Dictionary<string, string> Texts = new();
    public byte[] File = [];
    /// <summary>The first this many downloads stop after StopAfter bytes with an IOException (a dropped connection).</summary>
    public int FailingDownloads;
    public int StopAfter;
    /// <summary>Whether the server honours a resume (HTTP Range); when not, it sends the whole file again.</summary>
    public bool SupportsResume = true;
    public int GetCalls;
    public int Downloads;
    public readonly List<long> ResumedFrom = [];
    public readonly List<string> Urls = [];

    public string GetText(string url, TimeSpan timeout)
    {
        GetCalls++;
        Urls.Add(url);
        Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
        if (url == ReleaseFeed.ListUrl)
        {
            if (GetFails != null)
                throw GetFails;
            return Json ?? "";
        }
        return Texts.TryGetValue(url, out string? text) ? text : throw new HttpRequestException("404 for " + url);
    }

    public void Download(string url, string path, Action<long, long>? progress, CancellationToken cancel)
    {
        Downloads++;
        Urls.Add(url);
        long have = System.IO.File.Exists(path) ? new FileInfo(path).Length : 0;
        ResumedFrom.Add(have);
        long start = SupportsResume ? have : 0;
        using (var file = new FileStream(path, SupportsResume && have > 0 ? FileMode.Append : FileMode.Create))
        {
            int end = Downloads <= FailingDownloads ? (int)Math.Min(File.Length, start + StopAfter) : File.Length;
            file.Write(File, (int)start, end - (int)start);
            progress?.Invoke(end, File.Length);
            if (end < File.Length)
                throw new IOException("The connection dropped (test).");
        }
    }
}

internal static class UpdateFixtures
{
    /// <summary>GET /repos/rambodolphin13/DividedRealms/releases as GitHub answered it on 2026-11-07 (0.1b, 0.1a, 0.38-dev8, World Workshop).</summary>
    public const string RecordedList = "dr-releases-2026-11-07.json";
    public const string Recorded01bName = "DividedRealms-DR-dev-0.1b-Fishing-Update";
    public const string Recorded01bDigest = "sha256:1ad8e3bfeac005ea0d34222b7515441888cdba3168d2e995f0cf55d263428e1c";
    public const long Recorded01bSize = 1200457112;

    public static string Read(string name) =>
        System.IO.File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));

    public static string TempFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dr-update-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Delete(string dir)
    {
        if (!Directory.Exists(dir))
            return;
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            System.IO.File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }

    public static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>The files of a release package (relative paths with "/"), with its RELEASE.txt; no manifest yet.</summary>
    public static Dictionary<string, byte[]> PackageFiles(string tag, string label = "Fishing Update", bool stamp = true)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["START DIVIDED REALMS.cmd"] = Bytes("@echo off\r\nstart \"\" \"%~dp0runtime\\OfflineDAoC.exe\"\r\n"),
            ["EDITION.txt"] = Bytes($"Divided Realms {tag} {label}: the original 1.130 client (bring your own), modern test classes.\r\n"),
            ["runtime/OfflineDAoC.exe"] = Bytes("launcher " + tag),
            ["runtime/OfflineDAoC.dll"] = Bytes("launcher code " + tag),
            ["runtime/server/GameServer.dll"] = Bytes("server " + tag),
            ["runtime/server/CoreServer.exe"] = Bytes("core server"),
            ["runtime/server/config/serverconfig.xml"] = Bytes("<root><Server><Port>10330</Port></Server></root>"),
            ["runtime/data/world-clean.sqlite3.db"] = Bytes("clean world " + tag),
            ["runtime/modern-client.json"] = Bytes("{\"Port\": 10330}"),
            ["docs/README.md"] = Bytes("# docs " + tag),
        };
        if (stamp)
            files["RELEASE.txt"] = Bytes($"Version={tag}\r\nLabel={tag} {label}\r\nPackage=DividedRealms-{tag}-Fishing-Update\r\nRepository=rambodolphin13/DividedRealms\r\n");
        return files;
    }

    public static byte[] Manifest(Dictionary<string, byte[]> files) =>
        Bytes(string.Concat(files.Where(f => f.Key != ReleaseFolder.ManifestFile).OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{Sha(f.Value)}  {f.Key}\n")));

    /// <summary>A release zip: everything under top/, with a manifest of the files (change lets a test break it afterwards).</summary>
    public static byte[] Package(string tag, Action<Dictionary<string, byte[]>>? change = null, string? top = null,
        Action<Dictionary<string, byte[]>>? before = null)
    {
        Dictionary<string, byte[]> files = PackageFiles(tag);
        before?.Invoke(files);
        files[ReleaseFolder.ManifestFile] = Manifest(files);
        change?.Invoke(files);
        string root = top ?? $"DividedRealms-{tag}-Fishing-Update";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry(root + "/");
            foreach (var (name, data) in files)
            {
                using Stream s = zip.CreateEntry(name.StartsWith("..") ? name : root + "/" + name).Open();
                s.Write(data);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// An unpacked release in a game folder, as a player has it after playing: the package's files (older packages have
    /// EDITION.txt only, stamp: false) plus a save database, account.txt, logs, backups, settings and the client copy.
    /// </summary>
    public static string GameFolder(string parent, string tag, bool stamp = false)
    {
        string game = Path.Combine(parent, $"DividedRealms-{tag}-Fishing-Update");
        Dictionary<string, byte[]> files = PackageFiles(tag, stamp: stamp);
        files[ReleaseFolder.ManifestFile] = Manifest(files);
        foreach (var (rel, data) in files.Concat(PlayerData()))
            Write(game, rel, data);
        return game;
    }

    /// <summary>The player's own files a played folder has, none of which an update may change.</summary>
    public static Dictionary<string, byte[]> PlayerData() => new()
    {
        ["runtime/data/opendaoc.sqlite3.db"] = Bytes("SQLite format 3\0 the player's characters, items and bots"),
        ["runtime/data/opendaoc.sqlite3.db-wal"] = Bytes("write-ahead log"),
        ["runtime/data/backups/opendaoc-20261106.db"] = Bytes("yesterday's save"),
        ["runtime/account.txt"] = Bytes("player\r\nsecret\r\n"),
        ["runtime/logs/server.log"] = Bytes("log lines"),
        ["runtime/overlay.json"] = Bytes("{\"FloatingDamage\": true}"),
        ["runtime/client-modern/app/game.dll"] = Bytes("the player's patched client"),
    };

    public static void Write(string root, string rel, byte[] data)
    {
        string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, data);
    }

    public static byte[] ReadGame(string game, string rel) => System.IO.File.ReadAllBytes(Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// The recorded release list with DR-dev-0.1b's package swapped for a test package (its digest and size), so the
    /// rest of the answer stays exactly as GitHub gave it. digest: false leaves GitHub's digest out (null).
    /// </summary>
    public static string RecordedWith01bPackage(byte[] package, bool digest = true)
    {
        string json = Read(RecordedList);
        Assert.That(json, Does.Contain($"\"digest\": \"{Recorded01bDigest}\""));
        Assert.That(json, Does.Contain($"\"size\": {Recorded01bSize}"));
        return json.Replace($"\"digest\": \"{Recorded01bDigest}\"", digest ? $"\"digest\": \"sha256:{Sha(package)}\"" : "\"digest\": null")
                   .Replace($"\"size\": {Recorded01bSize}", $"\"size\": {package.Length}");
    }

    public static string Download01bUrl(string file) =>
        "https://github.com/rambodolphin13/DividedRealms/releases/download/DR-dev-0.1b/" + file;
}
