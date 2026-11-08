using System.Net;
using System.Net.Http.Headers;

namespace OfflineDaoc.Updates;

/// <summary>
/// The internet, as far as updates need it: GETs for the releases API and the small .sha256 file, and one file
/// download. Kept behind this interface so tests never touch the network (they give a fake with recorded answers).
/// </summary>
public interface IReleaseWeb
{
    /// <summary>The text at url (the API's JSON, a .sha256 file). Throws on any failure, including the timeout.</summary>
    string GetText(string url, TimeSpan timeout);

    /// <summary>
    /// Downloads url into path, reporting (bytes so far, total or -1). When path already holds the start of the file (an
    /// interrupted download), only the rest is asked for and appended. Throws on any failure; what arrived stays in path.
    /// </summary>
    void Download(string url, string path, Action<long, long>? progress, CancellationToken cancel);
}

// IReleaseWeb over HttpClient. Sends a generic User-Agent, which GitHub's API requires, and nothing else about the PC or
// the player. Ported from Living Server's HttpReleaseWeb (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3); the
// resumed download (HTTP Range) is ours, for 1.2 GB packages.
public sealed class HttpReleaseWeb : IReleaseWeb, IDisposable
{
    /// <summary>A download that receives nothing for this long is given up (and resumed by the caller's retry).</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;

    public HttpReleaseWeb(string userAgent)
    {
        _client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, UseCookies = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public string GetText(string url, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (url.StartsWith("https://api.github.com/", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        // The default completion option reads the whole answer before returning, all inside the timeout.
        using HttpResponseMessage response = _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("GitHub answered " + (int)response.StatusCode + " " + response.ReasonPhrase);
        return response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
    }

    public void Download(string url, string path, Action<long, long>? progress, CancellationToken cancel)
    {
        long have = File.Exists(path) ? new FileInfo(path).Length : 0;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stall.CancelAfter(StallTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (have > 0)
            request.Headers.Range = new RangeHeaderValue(have, null);
        using HttpResponseMessage response = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The part on disk is as long as the file or longer: it can't be resumed, so the next try starts over.
            File.Delete(path);
            throw new IOException("The interrupted download couldn't be resumed; it starts over.");
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("GitHub answered " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + url);
        bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        long done = resumed ? have : 0;
        long? length = response.Content.Headers.ContentLength;
        long total = length is { } l ? done + l : -1;
        using Stream body = response.Content.ReadAsStreamAsync(stall.Token).GetAwaiter().GetResult();
        using CancellationTokenRegistration closeOnStall = stall.Token.Register(() => response.Dispose());
        using var file = new FileStream(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var buffer = new byte[1 << 16];
        DateTime lastReport = DateTime.MinValue;
        progress?.Invoke(done, total);
        while (true)
        {
            int read;
            try
            {
                read = body.ReadAsync(buffer, stall.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception) when (stall.IsCancellationRequested)
            {
                cancel.ThrowIfCancellationRequested();
                throw new TimeoutException("The download stopped receiving data for " + (int)StallTimeout.TotalSeconds + " seconds.");
            }
            if (read == 0)
                break;
            file.Write(buffer, 0, read);
            done += read;
            stall.CancelAfter(StallTimeout);
            if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= 200)
            {
                lastReport = DateTime.UtcNow;
                progress(done, total);
            }
        }
        if (total >= 0 && done != total)
            throw new IOException("The download ended after " + done + " of " + total + " bytes.");
        progress?.Invoke(done, total);
    }

    public void Dispose() => _client.Dispose();
}
