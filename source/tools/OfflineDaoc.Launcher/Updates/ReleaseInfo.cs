using System.Text.Json;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

// What GitHub's releases API says about one release, reduced to what an update needs. Ported from Living Server's
// ReleaseInfo (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3). Differences for Divided Realms:
// - every DR release is a pre-release, so GET /releases/latest never sees them: the feed lists the releases and the
//   newest DR-dev-* tag by ReleaseVersion order wins (pre-releases count; drafts never do);
// - the package is DividedRealms-<tag>-<name>.zip (DividedRealms-DR-dev-0.1b-Fishing-Update.zip), its zip.sha256 beside it;
// - read with System.Text.Json (the launcher is the only user).
public sealed class ReleaseInfo
{
    public string? TagName { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? PageUrl { get; init; }
    public bool Prerelease { get; init; }
    public bool Draft { get; init; }
    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = [];

    /// <summary>The release the tag names ("DR-dev-0.1b"), or null for a tag that isn't one (old 0.38 builds, World Workshop).</summary>
    public ReleaseVersion? Version => ReleaseVersion.Parse(TagName);

    /// <summary>A published release whose tag is a DR version and that carries a package: the only kind offered.</summary>
    public bool IsUsable => !Draft && Version != null && Package != null;

    /// <summary>The release's package: DividedRealms-&lt;tag&gt;.zip or DividedRealms-&lt;tag&gt;-&lt;name&gt;.zip.</summary>
    public ReleaseAsset? Package
    {
        get
        {
            ReleaseVersion? version = Version;
            if (version == null)
                return null;
            var shape = new Regex("^DividedRealms-" + Regex.Escape(TagName!.Trim()) + @"(-[A-Za-z0-9._-]+)?\.zip$", RegexOptions.IgnoreCase);
            return Assets.Where(a => a.Name != null && shape.IsMatch(a.Name)).OrderBy(a => a.Name!.Length).FirstOrDefault();
        }
    }

    /// <summary>The package's "&lt;zip&gt;.sha256" file, used when GitHub gives no digest for the package.</summary>
    public ReleaseAsset? PackageSha256File
    {
        get
        {
            string? name = Package?.Name;
            return name == null ? null : Assets.FirstOrDefault(a => string.Equals(a.Name, name + ".sha256", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The folder the package unpacks to: the zip's name without ".zip".</summary>
    public string? PackageFolderName => Package?.Name is { } name ? name[..^".zip".Length] : null;

    /// <summary>Reads one release (GET /releases/{id} or /releases/tags/{tag}). Null when it isn't a release.</summary>
    public static ReleaseInfo? Parse(string? json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(Clean(json));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Read(doc.RootElement) : null;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads GET /releases (a list). Empty when the answer isn't one (an error message, a rate limit, not JSON).</summary>
    public static List<ReleaseInfo> ParseList(string? json)
    {
        var releases = new List<ReleaseInfo>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(Clean(json));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return releases;
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && Read(item) is { } release)
                    releases.Add(release);
            }
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            releases.Clear();
        }
        return releases;
    }

    private static string Clean(string? json) => string.IsNullOrWhiteSpace(json) ? "null" : json.TrimStart('﻿');

    private static ReleaseInfo? Read(JsonElement e)
    {
        string? tag = Text(e, "tag_name");
        if (tag == null)
            return null;
        var assets = new List<ReleaseAsset>();
        if (e.TryGetProperty("assets", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in list.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object)
                    continue;
                assets.Add(new ReleaseAsset
                {
                    Name = Text(a, "name"),
                    Size = a.TryGetProperty("size", out JsonElement size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out long s) ? s : 0,
                    Digest = Text(a, "digest"),
                    DownloadUrl = Text(a, "browser_download_url"),
                });
            }
        }
        return new ReleaseInfo
        {
            TagName = tag,
            Title = Text(e, "name"),
            Body = Text(e, "body"),
            PageUrl = Text(e, "html_url"),
            Prerelease = Flag(e, "prerelease"),
            Draft = Flag(e, "draft"),
            Assets = assets,
        };
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The first lines of the release notes as plain text, for a one-glance summary: headings, list marks, bold and
    /// links are reduced to their words; empty lines are skipped. (Living Server's NotesPreview.)
    /// </summary>
    public IReadOnlyList<string> NotesPreview(int lines)
    {
        var result = new List<string>();
        foreach (string raw in (Body ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (result.Count >= lines)
                break;
            string line = raw.Trim();
            line = Regex.Replace(line, @"^(#{1,6}\s+|[-*+]\s+|\d+\.\s+|>\s*)", "");
            line = Regex.Replace(line, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            line = line.Replace("**", "").Replace("__", "").Replace("`", "").Trim();
            if (line.Length == 0 || Regex.IsMatch(line, @"^[-=_*|: ]+$"))
                continue;
            result.Add(line);
        }
        return result;
    }
}

public sealed class ReleaseAsset
{
    public string? Name { get; init; }
    public long Size { get; init; }
    /// <summary>"sha256:&lt;64 hex digits&gt;", computed by GitHub when the file was uploaded. May be missing.</summary>
    public string? Digest { get; init; }
    public string? DownloadUrl { get; init; }
}
