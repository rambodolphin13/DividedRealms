using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using OfflineDaoc.Passport;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The client this launcher starts for Join Friend: the original client (connect.exe, login port 10300) or the
/// modern 1.130 test client (its verified connector and its own port). The new client is not supported yet.
/// </summary>
public sealed record JoinClient(string ClientDirectory, string Connector, int LoginPort, ModernClientProfile? Modern)
{
    public const int ClassicLoginPort = 10300;

    public static JoinClient For(string runtime, ModernClientProfile? modern)
    {
        if (modern != null)
            return new JoinClient(modern.ClientDirectory, modern.ConnectorPath, modern.Port, modern);
        string official = Path.Combine(runtime, "client-opendaoc", "app");
        string directory = File.Exists(Path.Combine(official, "game.dll")) ? official : Path.Combine(runtime, "client");
        return new JoinClient(directory, Path.Combine(directory, "connect.exe"), ClassicLoginPort, null);
    }

    public bool IsInstalled => File.Exists(Connector) && File.Exists(Path.Combine(ClientDirectory, "game.dll"));

    public ProcessStartInfo CreateStartInfo(IPAddress host, string account, string password)
    {
        if (Modern != null)
            return Modern.CreateStartInfo(account, password, host.ToString());
        return new ProcessStartInfo(Connector)
        {
            WorkingDirectory = ClientDirectory,
            UseShellExecute = false,
            ArgumentList = { "game.dll", host.ToString(), account, password },
        };
    }
}

/// <summary>Input checks and the reachability probe behind the Join Friend window.</summary>
public static class JoinFriend
{
    // A host on Tailscale, the home network or (internet mode) the internet; never this PC or a non-host address.
    public static bool IsAllowedHost(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;
        byte[] b = address.GetAddressBytes();
        return b[0] != 0 && b[0] < 224 && !(b[0] == 169 && b[1] == 254);
    }

    public static bool TryParseHost(string text, out IPAddress address, out string error)
    {
        text = text.Trim();
        if (!IPAddress.TryParse(text, out IPAddress? parsed) || parsed.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(parsed.ToString(), text, StringComparison.Ordinal))
        {
            address = IPAddress.None;
            error = "Enter the host's address in dotted form, such as 100.90.12.34.";
            return false;
        }
        if (!IsAllowedHost(parsed))
        {
            address = IPAddress.None;
            error = "Use the host's Tailscale, home-network or internet address, not this PC's own.";
            return false;
        }
        address = parsed;
        error = string.Empty;
        return true;
    }

    /// <summary>What the guest typed: an invite code, or a host address as before. The hosts come in the order to try them.</summary>
    public static bool TryParseTarget(string text, int clientPort, out IReadOnlyList<IPAddress> hosts, out string error)
    {
        hosts = [];
        if (text.Contains('.'))
        {
            bool parsed = TryParseHost(text, out IPAddress address, out error);
            if (parsed)
                hosts = [address];
            return parsed;
        }
        if (!InviteCode.TryParse(text, out InviteCode.Invite invite))
        {
            error = "That invite code isn't right. Copy the whole code from your friend, starting with DR.";
            return false;
        }
        if (invite.Port != clientPort)
        {
            error = $"This code is for a different game (login port {invite.Port}; this one uses {clientPort}). You both need the same game.";
            return false;
        }
        hosts = invite.Addresses.Where(IsAllowedHost).ToList();
        error = hosts.Count == 0 ? "That invite code has no address this PC can use." : string.Empty;
        return hosts.Count > 0;
    }

    /// <summary>The first host that answers on the login port.</summary>
    public static async Task<IPAddress?> FindReachableAsync(IReadOnlyList<IPAddress> hosts, int port, TimeSpan timeoutEach)
    {
        foreach (IPAddress host in hosts)
        {
            if (await CanReachAsync(host, port, timeoutEach))
                return host;
        }
        return null;
    }

    /// <summary>The local player's characters, for Bring a Character. A join-only install has none.</summary>
    public static IReadOnlyList<LocalCharacter> HomeCharacters(string runtime)
    {
        if (!File.Exists(Path.Combine(runtime, "account.txt")) || !File.Exists(Path.Combine(runtime, "data", "opendaoc.sqlite3.db")))
            return [];
        return PassportEngine.Characters(runtime, PortableCredentials.ReadOrCreate(runtime).Account);
    }

    public static string DescribeCharacter(LocalCharacter character)
    {
        string realm = character.Realm switch { 1 => "Albion", 2 => "Midgard", 3 => "Hibernia", _ => "no realm" };
        string line = $"{character.Name}, level {character.Level}, {realm}";
        return character.AwayAt == null ? line : $"{line} (away at {character.AwayAt})";
    }

    public static string TravelMessage(PassportException refusal) => refusal.Code switch
    {
        "server_running" => "Stop your own server first: a character's move changes your save. Nothing was changed.",
        "name_taken" => refusal.Message + " Type one in the visiting name box.",
        _ => refusal.Message,
    };

    public static bool IsValidAccount(string account) =>
        account.Length is >= 1 and <= 20 && account.All(char.IsAsciiLetterOrDigit);

    // The legacy client sends 1-20 printable ASCII characters.
    public static bool IsValidPassword(string password) =>
        password.Length is >= 1 and <= 20 && password.All(character => character is >= ' ' and <= '~');

    public static async Task<bool> CanReachAsync(IPAddress host, int port, TimeSpan timeout)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            await client.ConnectAsync(host, port, cancel.Token);
            return client.Connected;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}
