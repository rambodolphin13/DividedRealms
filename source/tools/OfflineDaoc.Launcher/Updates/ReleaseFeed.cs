namespace OfflineDaoc.Updates;

// Divided Realms' releases on GitHub: the newest one, and its package downloaded and verified. Only one request is
// made for the check, with a generic User-Agent and nothing about the player; a failure is reported to the caller, who
// stays quiet about it unless the player asked.
// Ported from Living Server's ReleaseFeed (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3). Ours lists the releases
// (every DR release is a pre-release, which /releases/latest leaves out), takes the .zip.sha256 file when GitHub has no
// digest, and resumes and retries the download.
public static class ReleaseFeed
{
    public const string Repository = "rambodolphin13/DividedRealms";
    /// <summary>The newest releases, pre-releases included (GitHub lists them newest first).</summary>
    public const string ListUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=30";
    /// <summary>Packages are downloaded only from this repository's own release files.</summary>
    public const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);
    /// <summary>Tries for one download; each after the first resumes where the last stopped.</summary>
    public const int DownloadAttempts = 4;
    /// <summary>The wait before the second try (doubled for each later one). Tests set it to zero.</summary>
    public static TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The newest usable release (a DR-dev-* tag with a package, by ReleaseVersion order, not by date), or null with
    /// the reason (offline, rate limit, no release). Never throws.
    /// </summary>
    public static ReleaseInfo? TryGetNewest(IReleaseWeb web, out string? problem)
    {
        try
        {
            string json = web.GetText(ListUrl, CheckTimeout);
            List<ReleaseInfo> releases = ReleaseInfo.ParseList(json);
            ReleaseInfo? newest = releases.Where(r => r.IsUsable).MaxBy(r => r.Version);
            problem = newest != null ? null
                : releases.Count == 0 && !json.TrimStart('﻿').TrimStart().StartsWith('[') ? "GitHub's answer wasn't a list of releases"
                : "there is no Divided Realms release with a package";
            return newest;
        }
        catch (Exception e)
        {
            problem = Innermost(e).Message;
            return null;
        }
    }

    /// <summary>
    /// The SHA-256 the package must have: GitHub's digest for it, else the hash in the release's .zip.sha256 file.
    /// Throws PackageRefusedException when there is neither (nothing is downloaded then).
    /// </summary>
    public static string ExpectedSha256(IReleaseWeb web, ReleaseInfo release, out string source)
    {
        ReleaseAsset asset = release.Package ?? throw new PackageRefusedException("The " + release.TagName + " release has no package.");
        if (PackageDigest.Sha256Of(asset.Digest) is { } digest)
        {
            source = "GitHub's digest";
            return digest;
        }
        ReleaseAsset? file = release.PackageSha256File;
        if (file?.DownloadUrl is { } url)
        {
            CheckAddress(url);
            string? fromFile;
            try { fromFile = PackageDigest.FromSha256File(web.GetText(url, CheckTimeout), asset.Name!); }
            catch (Exception e) { throw new PackageRefusedException("Couldn't read " + file.Name + " (" + Innermost(e).Message + "), so the package can't be checked. Nothing was downloaded."); }
            if (fromFile != null)
            {
                source = file.Name!;
                return fromFile;
            }
        }
        throw new PackageRefusedException("No SHA-256 is published for " + asset.Name + ", so it can't be checked. Nothing was downloaded.");
    }

    /// <summary>
    /// Downloads the release's package into folder (resuming an interrupted download, retrying a broken one) and checks
    /// it against its published SHA-256. A file that fails is deleted. A package already there with the right hash is
    /// used again. Returns its path. Throws PackageRefusedException with the reason; download errors pass through
    /// after the last try (the part downloaded stays for the next click).
    /// </summary>
    public static string DownloadPackage(IReleaseWeb web, ReleaseInfo release, string folder, Action<long, long>? progress,
        CancellationToken cancel, Action<string>? log = null)
    {
        if (release.Version == null)
            throw new PackageRefusedException("The release's tag " + release.TagName + " isn't a Divided Realms release.");
        ReleaseAsset asset = release.Package ?? throw new PackageRefusedException("The " + release.TagName + " release has no DividedRealms-" + release.TagName + "-*.zip.");
        string url = asset.DownloadUrl ?? "";
        CheckAddress(url);
        // Fail closed before spending the download: without a published hash the file couldn't be checked.
        string sha = ExpectedSha256(web, release, out string source);
        log?.Invoke($"{asset.Name} must have SHA-256 {sha} ({source}).");

        Directory.CreateDirectory(folder);
        string final = Path.Combine(folder, asset.Name!);
        if (File.Exists(final) && PackageDigest.Check(final, sha) == DigestCheck.Match)
        {
            log?.Invoke("Already downloaded and checked: " + final);
            WriteSha256File(final, sha);
            return final;
        }
        PackageDigest.TryDelete(final);
        string part = final + ".part";
        if (File.Exists(part) && asset.Size > 0 && new FileInfo(part).Length > asset.Size)
            File.Delete(part);
        for (int attempt = 1; ; attempt++)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(part))
                    log?.Invoke($"Resuming the download at {new FileInfo(part).Length:N0} bytes (try {attempt}).");
                web.Download(url, part, progress, cancel);
                long got = new FileInfo(part).Length;
                if (asset.Size > 0 && got < asset.Size)
                    throw new IOException($"The download stopped at {got:N0} of {asset.Size:N0} bytes.");
                break;
            }
            catch (Exception e) when (attempt < DownloadAttempts && !cancel.IsCancellationRequested &&
                                      e is IOException or HttpRequestException or TimeoutException or TaskCanceledException)
            {
                log?.Invoke($"Download try {attempt} failed: {Innermost(e).Message}. Trying again.");
                TimeSpan wait = RetryDelay * Math.Pow(2, attempt - 1);
                if (wait > TimeSpan.Zero && cancel.WaitHandle.WaitOne(wait))
                    cancel.ThrowIfCancellationRequested();
            }
        }
        string? refused = PackageDigest.VerifyOrDelete(part, sha);
        if (refused != null)
            throw new PackageRefusedException("The download was refused and deleted: " + refused + ".");
        File.Move(part, final, true);
        // What it was checked against, so a later install of the kept file can check it again offline.
        WriteSha256File(final, sha);
        return final;
    }

    public static void WriteSha256File(string zip, string sha) =>
        File.WriteAllText(zip + ".sha256", sha + " *" + Path.GetFileName(zip) + Environment.NewLine);

    private static void CheckAddress(string url)
    {
        if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal))
            throw new PackageRefusedException("The package's address isn't one of this project's release files: " + url);
    }

    public static Exception Innermost(Exception e)
    {
        while (e is AggregateException && e.InnerException != null)
            e = e.InnerException;
        // HttpRequestException wraps the socket or TLS error that says what really happened.
        return e.InnerException != null && e is HttpRequestException ? e.InnerException : e;
    }
}
