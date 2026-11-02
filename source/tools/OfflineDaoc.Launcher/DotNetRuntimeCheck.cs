using System.Diagnostics;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

// Whether CoreServer.exe will find the .NET it needs. The server is framework-dependent: its runtimeconfig names
// Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.x, and a game folder carries both in tools\dotnet (the START
// cmd points DOTNET_ROOT there). A player (2026-11-02, 0.38-dev8) had no tools folder and no .NET installed: the
// server exited at once with "You must install or update .NET", the launcher sat on STARTING and fell back to STOPPED
// with no word why. The launcher now picks the runtime the server will use, checks it before starting, and says what
// is missing.
public static class DotNetRuntimeCheck
{
    public const string DownloadPage = "https://dotnet.microsoft.com/download/dotnet/10.0";

    public sealed record Framework(string Name, Version Version)
    {
        public override string ToString() => $"{Name} {Version}";
    }

    // The game's own copy (tools\dotnet beside runtime), else a DOTNET_ROOT that holds a host, else the system install.
    public static string ResolveRoot(string runtimeRoot)
    {
        string bundled = BundledRoot(runtimeRoot);
        if (HasHost(bundled))
            return bundled;
        foreach (string name in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && HasHost(value))
                return Path.GetFullPath(value);
        }
        return Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files", "dotnet");
    }

    public static string BundledRoot(string runtimeRoot) => Path.GetFullPath(Path.Combine(runtimeRoot, "..", "tools", "dotnet"));

    public static bool HasHost(string root) => Directory.Exists(Path.Combine(root, "host", "fxr"));

    // The server runs on exactly the root that was checked; an inherited DOTNET_ROOT to a missing folder is dropped.
    public static void Apply(ProcessStartInfo start, string root)
    {
        if (HasHost(root))
        {
            start.Environment["DOTNET_ROOT"] = root;
            start.Environment["DOTNET_ROOT_X64"] = root;
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }
        else
        {
            start.Environment.Remove("DOTNET_ROOT");
            start.Environment.Remove("DOTNET_ROOT_X64");
        }
    }

    // The frameworks the executable's runtimeconfig asks for. None for a self-contained build (no runtimeconfig).
    public static IReadOnlyList<Framework> Required(string executable)
    {
        string config = Path.ChangeExtension(executable, ".runtimeconfig.json");
        if (!File.Exists(config))
            return [];
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(config));
        if (!document.RootElement.TryGetProperty("runtimeOptions", out JsonElement options))
            return [];
        var required = new List<Framework>();
        if (options.TryGetProperty("framework", out JsonElement single))
            AddFramework(required, single);
        if (options.TryGetProperty("frameworks", out JsonElement many) && many.ValueKind == JsonValueKind.Array)
            foreach (JsonElement framework in many.EnumerateArray())
                AddFramework(required, framework);
        return required;
    }

    // A framework is there when shared\<name> holds a version of the same major that is not older (the host's default
    // roll forward takes any later minor or patch of that major).
    public static IReadOnlyList<Framework> Missing(IEnumerable<Framework> required, string root) =>
        required.Where(framework => !Installed(root, framework.Name).Any(version =>
            version.Major == framework.Version.Major && version >= framework.Version)).ToList();

    public static string Explain(IReadOnlyList<Framework> missing, string root, string runtimeRoot)
    {
        string bundled = BundledRoot(runtimeRoot);
        string where = HasHost(bundled)
            ? $"This game's own copy in {bundled} is incomplete."
            : $"This game's own copy (the tools\\dotnet folder beside runtime) is missing, and {root} does not have it either.";
        return "The server cannot start: it needs Microsoft .NET 10, and these parts are missing:\n\n" +
               string.Join("\n", missing.Select(framework => "  • " + framework)) + "\n\n" + where + "\n\n" +
               "Extract the whole download again (the tools folder included), or install the \"ASP.NET Core Runtime 10\" " +
               "Windows Hosting Bundle from Microsoft, which brings both parts.\n\nOpen Microsoft's .NET 10 download page now?";
    }

    private static void AddFramework(List<Framework> required, JsonElement framework)
    {
        if (framework.TryGetProperty("name", out JsonElement name) && framework.TryGetProperty("version", out JsonElement version)
            && ParseVersion(version.GetString()) is { } parsed && !string.IsNullOrEmpty(name.GetString()))
            required.Add(new Framework(name.GetString()!, parsed));
    }

    private static IEnumerable<Version> Installed(string root, string name)
    {
        string shared = Path.Combine(root, "shared", name);
        if (!Directory.Exists(shared))
            return [];
        return Directory.GetDirectories(shared).Select(directory => ParseVersion(Path.GetFileName(directory))).OfType<Version>();
    }

    // "10.0.3" or "10.0.0-rc.2.25502.107": the numbers before any pre-release tag.
    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        int dash = text.IndexOf('-');
        return Version.TryParse(dash < 0 ? text : text[..dash], out Version? version) ? version : null;
    }
}
