using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

public enum DigestCheck
{
    /// <summary>The file's SHA-256 is the one published.</summary>
    Match,
    /// <summary>The file differs from what was published (a damaged or changed download).</summary>
    Mismatch,
    /// <summary>No SHA-256 was published for the file (or something that isn't one), so it can't be checked.</summary>
    Missing,
}

// Checks a downloaded package against the SHA-256 published for it: GitHub's asset "digest" ("sha256:..."), or the
// release's "<zip>.sha256" file ("<hex> *<name>", as sha256sum writes it) when GitHub gives none. An update is used
// only on Match: a missing hash is refused the same as a wrong one.
// Ported from Living Server's PackageDigest (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3); the .sha256 file is ours.
public static class PackageDigest
{
    private static readonly Regex Hex = new(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    /// <summary>The 64 lower-case hex digits of "sha256:&lt;hex&gt;" or a bare &lt;hex&gt;, or null when it isn't one.</summary>
    public static string? Sha256Of(string? digest)
    {
        if (digest == null)
            return null;
        string text = digest.Trim();
        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            text = text["sha256:".Length..];
        return Hex.IsMatch(text) ? text.ToLowerInvariant() : null;
    }

    /// <summary>
    /// The hash a "&lt;zip&gt;.sha256" file gives for fileName: its first "&lt;hex&gt; [*]&lt;name&gt;" line naming that file,
    /// or its only hash when the line names none. Null when it has none.
    /// </summary>
    public static string? FromSha256File(string? text, string fileName)
    {
        string? only = null;
        foreach (string raw in (text ?? "").TrimStart('﻿').Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            int space = line.IndexOfAny([' ', '\t']);
            string hash = space < 0 ? line : line[..space];
            string? sha = Sha256Of(hash);
            if (sha == null)
                continue;
            string named = space < 0 ? "" : line[space..].Trim().TrimStart('*').Trim();
            if (named.Length == 0)
                only ??= sha;
            else if (string.Equals(Path.GetFileName(named.Replace('/', '\\')), fileName, StringComparison.OrdinalIgnoreCase))
                return sha;
        }
        return only;
    }

    public static string FileSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static DigestCheck Check(string path, string? digest)
    {
        string? expected = Sha256Of(digest);
        if (expected == null)
            return DigestCheck.Missing;
        return string.Equals(FileSha256(path), expected, StringComparison.Ordinal) ? DigestCheck.Match : DigestCheck.Mismatch;
    }

    /// <summary>
    /// Checks the file and deletes it unless it matches, so a damaged or unverifiable download never stays behind
    /// looking like a good one. Returns why it was refused, or null when it matches.
    /// </summary>
    public static string? VerifyOrDelete(string path, string? digest)
    {
        DigestCheck result;
        try
        {
            result = Check(path, digest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(path);
            return "the download couldn't be read (" + e.Message + ")";
        }
        if (result == DigestCheck.Match)
            return null;
        TryDelete(path);
        return result == DigestCheck.Missing
            ? "no SHA-256 was published for the package, so it can't be checked"
            : "the download doesn't match the SHA-256 published for it";
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left behind; the next download replaces it, and it is never used unverified.
        }
    }
}
