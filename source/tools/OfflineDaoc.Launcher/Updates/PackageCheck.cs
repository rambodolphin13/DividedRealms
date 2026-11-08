using System.IO.Compression;
using System.Security.Cryptography;

namespace OfflineDaoc.Updates;

/// <summary>Why a package was refused, in words for the player and the log. Nothing in the game was changed.</summary>
public sealed class PackageRefusedException(string message) : Exception(message);

/// <summary>What a checked package holds: its one top folder, its release and its own file list.</summary>
public sealed class PackageContents(string topFolder, ReleaseVersion version, IReadOnlyDictionary<string, string> manifest, long unpackedBytes)
{
    /// <summary>"DividedRealms-DR-dev-0.1b-Fishing-Update": the folder everything in the zip sits in.</summary>
    public string TopFolder { get; } = topFolder;

    public ReleaseVersion Version { get; } = version;

    /// <summary>PACKAGE MANIFEST.sha256: path (with "/") to SHA-256, for every file but itself.</summary>
    public IReadOnlyDictionary<string, string> Manifest { get; } = manifest;

    public long UnpackedBytes { get; } = unpackedBytes;
}

// Checks a Divided Realms release zip before anything of it is used, the things build_modern_test_package.py makes sure
// of: everything sits in one top folder named like the zip, RELEASE.txt (or an older package's EDITION.txt) names the
// release the tag names, the launcher, server and START file are there, no player data ships (no world database, no
// account.txt), and PACKAGE MANIFEST.sha256 lists exactly the files in the zip. Extract then checks every file's
// SHA-256 against that list as it writes it.
// Ported from Living Server's PackageCheck (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3) for our package layout.
public static class PackageCheck
{
    public static readonly string[] RequiredFiles =
    [
        ReleaseFolder.ManifestFile, "START DIVIDED REALMS.cmd", "runtime/OfflineDAoC.exe", "runtime/OfflineDAoC.dll",
        "runtime/server/GameServer.dll", "runtime/server/CoreServer.exe",
    ];

    /// <summary>Checks the package at path for the release and top folder expected. Throws PackageRefusedException with the reason.</summary>
    public static PackageContents Inspect(string path, ReleaseVersion expected, string topFolder)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return Inspect(zip, expected, topFolder);
        }
        catch (InvalidDataException e)
        {
            throw new PackageRefusedException("The package isn't a readable zip file (" + e.Message + ").");
        }
    }

    public static PackageContents Inspect(ZipArchive zip, ReleaseVersion expected, string topFolder)
    {
        string prefix = topFolder + "/";
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = Normalize(entry.FullName);
            if (!IsSafe(name))
                throw new PackageRefusedException("The package has an unsafe entry: " + entry.FullName);
            if (!string.Equals(name, topFolder, StringComparison.Ordinal) && !name.StartsWith(prefix, StringComparison.Ordinal))
                throw new PackageRefusedException("Everything in the package should be in its " + topFolder + " folder, but it has " + entry.FullName + ".");
            if (IsFolder(entry) || name.Length <= prefix.Length)
                continue;
            string rel = name[prefix.Length..];
            if (PlayerFiles.IsPlayerData(rel))
                throw new PackageRefusedException("The package holds player data (" + rel + "), which an update must never ship.");
            if (!files.TryAdd(rel, entry))
                throw new PackageRefusedException("The package has " + rel + " twice.");
            bytes += entry.Length;
        }
        foreach (string file in RequiredFiles)
        {
            if (!files.ContainsKey(file))
                throw new PackageRefusedException("The package has no " + file + " in its " + topFolder + " folder.");
        }

        ReleaseVersion? named = ReleaseFolder.ReadVersion(name => files.TryGetValue(name, out var e) ? ReadText(e) : null, out string source);
        if (named == null)
            throw new PackageRefusedException("The package has no " + ReleaseFolder.StampFile + " or " + ReleaseFolder.EditionFile + " naming its release.");
        if (!named.SameAs(expected))
            throw new PackageRefusedException($"The package's {source} names {named.Tag}, but its release is {expected.Tag}.");

        Dictionary<string, string> manifest = ReleaseFolder.ReadManifest(ReadText(files[ReleaseFolder.ManifestFile]));
        foreach (string rel in files.Keys)
        {
            if (!IsManifestFile(rel) && !manifest.ContainsKey(rel))
                throw new PackageRefusedException("The package's " + ReleaseFolder.ManifestFile + " doesn't list " + rel + ".");
        }
        foreach (string rel in manifest.Keys)
        {
            if (!files.ContainsKey(rel))
                throw new PackageRefusedException("The package's " + ReleaseFolder.ManifestFile + " lists " + rel + ", which isn't in the package.");
        }
        return new PackageContents(topFolder, expected, manifest, bytes);
    }

    /// <summary>
    /// Unpacks a checked package into parent\&lt;top folder&gt; (made empty first) and returns that folder. Every entry
    /// must land inside it, and every file's SHA-256 must be the one PACKAGE MANIFEST.sha256 lists; otherwise the
    /// folder is deleted and PackageRefusedException says which file.
    /// </summary>
    public static string Extract(string path, PackageContents contents, string parent, Action<long, long>? progress = null,
        CancellationToken cancel = default)
    {
        string target = Path.Combine(parent, contents.TopFolder);
        if (Directory.Exists(target))
            Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        string root = Path.GetFullPath(target).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string prefix = contents.TopFolder + "/";
        long done = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var buffer = new byte[1 << 16];
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancel.ThrowIfCancellationRequested();
                string name = Normalize(entry.FullName);
                if (!IsSafe(name) || !(name + "/").StartsWith(prefix, StringComparison.Ordinal))
                    throw new PackageRefusedException("The package has an unsafe entry: " + entry.FullName);
                string rel = name.Length > prefix.Length ? name[prefix.Length..] : "";
                if (rel.Length == 0)
                    continue;
                string dest = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new PackageRefusedException("The package has an unsafe entry: " + entry.FullName);
                if (IsFolder(entry))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    using (Stream s = entry.Open())
                    using (var file = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        int read;
                        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            sha.AppendData(buffer, 0, read);
                            file.Write(buffer, 0, read);
                            done += read;
                        }
                    }
                    if (!IsManifestFile(rel))
                    {
                        string got = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                        if (!contents.Manifest.TryGetValue(rel, out string? want) || want != got)
                            throw new PackageRefusedException(rel + " in the package isn't the file its " + ReleaseFolder.ManifestFile + " lists.");
                    }
                }
                progress?.Invoke(done, contents.UnpackedBytes);
            }
        }
        catch
        {
            try { Directory.Delete(target, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return target;
    }

    private static bool IsManifestFile(string rel) => string.Equals(rel, ReleaseFolder.ManifestFile, StringComparison.OrdinalIgnoreCase);

    private static bool IsFolder(ZipArchiveEntry entry) => entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string Normalize(string name) => name.Replace('\\', '/').TrimEnd('/');

    // No absolute paths, drive letters or ".." steps: a package entry may only name a place inside its folder.
    private static bool IsSafe(string name) =>
        name.Length > 0 && !name.StartsWith('/') && name.IndexOf(':') < 0 &&
        name.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
}
