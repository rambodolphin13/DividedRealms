using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace OfflineDaoc.Launcher;

/// <summary>Opt-in original retail client profile. Absent for normal released installs.</summary>
public sealed record ModernClientProfile
{
    public string ClientDirectory { get; init; } = "";
    public string ConnectorPath { get; init; } = "";
    public string GameSha256 { get; init; } = "";
    public string ConnectorSha256 { get; init; } = "";
    public int Port { get; init; }
    public string Endpoint => $"127.0.0.1:{Port}";

    public static ModernClientProfile? Load(string runtime)
    {
        string file = Path.Combine(runtime, "modern-client.json");
        if (!File.Exists(file)) return null;
        var profile = JsonSerializer.Deserialize<ModernClientProfile>(File.ReadAllText(file))
            ?? throw new InvalidDataException("Modern client profile is empty.");
        if (profile.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(profile.ClientDirectory) ||
            string.IsNullOrWhiteSpace(profile.ConnectorPath) || !IsHash(profile.GameSha256) || !IsHash(profile.ConnectorSha256))
            throw new InvalidDataException("Modern client profile requires client/helper paths, SHA256 hashes and a valid port.");
        profile = profile with
        {
            ClientDirectory = Path.GetFullPath(profile.ClientDirectory, Path.GetFullPath(runtime)),
            ConnectorPath = Path.GetFullPath(profile.ConnectorPath, Path.GetFullPath(runtime))
        };
        profile.ValidateServer(runtime);
        return profile;
    }

    private static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    public void ValidateServer(string runtime)
    {
        var server = XDocument.Load(Path.Combine(runtime, "server", "config", "serverconfig.xml")).Root?.Element("Server")
            ?? throw new InvalidDataException("Test server configuration is missing.");
        var connection = new DbConnectionStringBuilder { ConnectionString = server.Element("DBConnectionString")?.Value ?? "" };
        // A relative Data Source (the packages' ../data/opendaoc.sqlite3.db) is relative to the server folder.
        string database = connection.TryGetValue("Data Source", out object? value) ? Convert.ToString(value) ?? "" : "";
        const string config = @"runtime\server\config\serverconfig.xml";
        string? ip = server.Element("IP")?.Value.Trim();
        if (!HostForFriends.IsAllowedListenAddress(ip))
            throw new InvalidDataException(
                $"The server address in {config} is <IP>{ip}</IP>. It must be {HostForFriends.Loopback} (play alone) or " +
                $"{HostForFriends.AllInterfaces} (Host for Friends). Set it back to {HostForFriends.Loopback} and use HOST FOR FRIENDS " +
                "to let friends in. Your internet address, if you need one, goes in <PublicIP>, not <IP>.");
        if (server.Element("Port")?.Value.Trim() != Port.ToString())
            throw new InvalidDataException($"The server port in {config} is <Port>{server.Element("Port")?.Value.Trim()}</Port>. This client logs in on {Port}; set it back to {Port}.");
        if (server.Element("DBType")?.Value != "SQLITE" ||
            !string.Equals(Path.GetFullPath(database, Path.GetFullPath(Path.Combine(runtime, "server"))),
                Path.GetFullPath(Path.Combine(runtime, "data", "opendaoc.sqlite3.db")), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"{config} must use this install's own SQLite database (DBType SQLITE, Data Source ../data/opendaoc.sqlite3.db). " +
                "Was the file copied from another install?");
    }

    // host: a friend's server address (Join Friend); null starts against this install's own server.
    public ProcessStartInfo CreateStartInfo(string account, string password, string? host = null)
    {
        Verify(Path.Combine(ClientDirectory, "game.dll"), GameSha256);
        Verify(ConnectorPath, ConnectorSha256);
        var info = new ProcessStartInfo(ConnectorPath) { WorkingDirectory = ClientDirectory, UseShellExecute = false };
        foreach (string argument in new[] { Path.Combine(ClientDirectory, "game.dll"), host == null ? Endpoint : $"{host}:{Port}", account, password })
            info.ArgumentList.Add(argument);
        return info;
    }

    private static void Verify(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The verified modern client/helper has changed: {Path.GetFileName(path)}.");
    }
}
