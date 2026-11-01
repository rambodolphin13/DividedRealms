using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using OfflineDaoc.Passport;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Host for Friends: lets friends on Tailscale, the home network or (internet mode) the internet join this game's server.
///
/// The server normally listens on 127.0.0.1 only. Hosting makes it listen on every interface (IP and UdpIP
/// 0.0.0.0) so this PC's own client keeps using 127.0.0.1 and friends reach it on the Tailscale address.
/// RegionIP stays 127.0.0.1: the server then sends each client the address that client connected to
/// (RegionAddress.ForClient in the server's packet libraries).
/// Windows Firewall rules (made by the host with administrator approval) admit Tailscale and the local subnet.
///
/// Internet mode (no Tailscale) adds router port forwarding: the server asks the router over UPnP at start
/// (EnableUPnP) and removes the mapping when it stops. DetectRegionIP stays off; the server keeps the router's
/// internet address as PublicIP and gives it only to clients connecting from the internet. The firewall rules then
/// admit any address, because friends join with an invite code and nobody lists them. So internet mode needs a host
/// password: without it the server (HostPassword) would make an account for anyone who finds the port.
///
/// While hosting, the server's Passport Desk listens on the login port + PassportDeskClient.PortOffset, so friends can
/// bring characters along. That port gets the same firewall rule and router mapping, and the invite code needs no change.
/// </summary>
public static class HostForFriends
{
    public const string Loopback = "127.0.0.1";
    public const string AllInterfaces = "0.0.0.0";
    public const string FriendRemoteAddresses = "100.64.0.0/10,LocalSubnet";
    public const string BackupFileName = "serverconfig.before-host-for-friends.xml";
    public const string SettingsFileName = "host-for-friends.json";
    public const int MinHostPasswordLength = 6;
    public const int MaxHostPasswordLength = 20;
    private const string HostPasswordHashSetting = "HostPasswordHash";
    private const string HostPasswordSalt = "DividedRealmsHost:";

    public sealed record State(bool Hosting, int LoginPort, int UdpPort, bool AutoAccountCreation, bool Internet = false, bool PasswordSet = false)
    {
        public int DeskPort => LoginPort + PassportDeskClient.PortOffset;
    }

    /// <summary>What the firewall rules were last made for.</summary>
    public sealed class Settings
    {
        public bool FirewallOpenToInternet { get; set; }
    }

    public static string ConfigPath(string runtime) => Path.Combine(runtime, "server", "config", "serverconfig.xml");

    // A server the launcher may start: loopback only, or Host for Friends.
    public static bool IsAllowedListenAddress(string? ip) => ip is Loopback or AllInterfaces;

    public static State Read(string runtime)
    {
        XElement server = LoadServer(ConfigPath(runtime), out _);
        return new State(
            Hosting: Value(server, "IP") == AllInterfaces,
            LoginPort: int.TryParse(Value(server, "Port"), out int port) ? port : 10300,
            UdpPort: int.TryParse(Value(server, "UdpPort"), out int udp) ? udp : 10400,
            AutoAccountCreation: !string.Equals(Value(server, "AutoAccountCreation"), "False", StringComparison.OrdinalIgnoreCase),
            Internet: Value(server, "IP") == AllInterfaces &&
                      string.Equals(Value(server, "EnableUPnP"), "True", StringComparison.OrdinalIgnoreCase),
            PasswordSet: !string.IsNullOrEmpty(Value(server, HostPasswordHashSetting)));
    }

    public static void Enable(string runtime)
    {
        string path = ConfigPath(runtime);
        XElement server = LoadServer(path, out XDocument document);
        string backup = Path.Combine(Path.GetDirectoryName(path)!, BackupFileName);
        if (Value(server, "IP") != AllInterfaces)
            File.Copy(path, backup, overwrite: true);
        Set(server, "IP", AllInterfaces);
        Set(server, "UdpIP", AllInterfaces);
        Set(server, "EnableUPnP", "False");
        Set(server, "DetectRegionIP", "False");
        // Friends get their account on their first login, as the local player does.
        Set(server, "AutoAccountCreation", "True");
        Save(document, path);
    }

    public static void Disable(string runtime)
    {
        string path = ConfigPath(runtime);
        XElement server = LoadServer(path, out XDocument document);
        Set(server, "IP", Loopback);
        Set(server, "UdpIP", Loopback);
        Set(server, "EnableUPnP", "False");
        Set(server, HostPasswordHashSetting, string.Empty);
        Save(document, path);
    }

    /// <summary>Internet mode: the server opens its ports on the router (UPnP) while it runs. Needs hosting and a host password.</summary>
    public static void EnableInternet(string runtime)
    {
        string path = ConfigPath(runtime);
        XElement server = LoadServer(path, out XDocument document);
        if (Value(server, "IP") != AllInterfaces)
            throw new InvalidOperationException("Turn hosting on first.");
        if (string.IsNullOrEmpty(Value(server, HostPasswordHashSetting)))
            throw new InvalidOperationException("Set a host password first.");
        Set(server, "EnableUPnP", "True");
        Set(server, "DetectRegionIP", "False");
        if (server.Element("PublicIP") == null)
            Set(server, "PublicIP", "auto");
        Save(document, path);
    }

    public static void DisableInternet(string runtime)
    {
        string path = ConfigPath(runtime);
        XElement server = LoadServer(path, out XDocument document);
        Set(server, "EnableUPnP", "False");
        Save(document, path);
    }

    /// <summary>The password a new friend's account must use; empty for none. Only its hash is stored.</summary>
    public static void SetHostPassword(string runtime, string password)
    {
        string path = ConfigPath(runtime);
        XElement server = LoadServer(path, out XDocument document);
        Set(server, HostPasswordHashSetting, password.Length == 0 ? string.Empty : HashHostPassword(password));
        Save(document, path);
    }

    /// <summary>The same hash as the server's HostPassword.Hash.</summary>
    public static string HashHostPassword(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HostPasswordSalt + password))).ToLowerInvariant();

    public static bool TryCheckHostPassword(string password, bool internet, out string error)
    {
        error = string.Empty;
        if (password.Length == 0 && !internet)
            return true;
        if (password.Length is >= MinHostPasswordLength and <= MaxHostPasswordLength && JoinFriend.IsValidPassword(password))
            return true;
        error = password.Length == 0
            ? "Hosting over the internet needs a password, or anyone who finds your server could make an account."
            : $"The password must be {MinHostPasswordLength}-{MaxHostPasswordLength} printable ASCII characters.";
        return false;
    }

    /// <summary>A PublicIP typed into serverconfig.xml, for a router that was forwarded by hand.</summary>
    public static IPAddress? ManualPublicAddress(string runtime)
    {
        string? value = Value(LoadServer(ConfigPath(runtime), out _), "PublicIP");
        return IPAddress.TryParse(value, out IPAddress? address) && IsInternet(address) ? address : null;
    }

    /// <summary>
    /// The addresses an invite code carries, in the order a friend tries them. Tailscale and internet addresses go
    /// before the home network one, which could be some other device on the friend's own network.
    /// </summary>
    public static IReadOnlyList<IPAddress> InviteAddresses(IPAddress? internet)
    {
        var addresses = new List<IPAddress>(TailscaleAddresses().Take(1));
        if (internet != null)
            addresses.Add(internet);
        addresses.AddRange(HomeAddresses().Take(1));
        return addresses;
    }

    public static string SettingsPath(string runtime) => Path.Combine(runtime, SettingsFileName);

    public static Settings LoadSettings(string runtime)
    {
        try
        {
            string path = SettingsPath(runtime);
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings() : new Settings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    public static void SaveSettings(string runtime, Settings settings) =>
        File.WriteAllText(SettingsPath(runtime), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>True for a public internet address; false for loopback, private, link-local and shared (CGNAT, Tailscale) ones.</summary>
    public static bool IsInternet(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;
        byte[] b = address.GetAddressBytes();
        return !(b[0] == 0 || b[0] == 10 || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 169 && b[1] == 254) ||
                 (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || b[0] >= 224);
    }

    public sealed record RouterCheck(bool Usable, IPAddress? InternetAddress, string Message);

    /// <summary>Asks the router over UPnP for its internet address, as the server does at start. Blocks a few seconds.</summary>
    public static RouterCheck CheckRouter(State state)
    {
        try
        {
            var nat = new UpnpNat();
            if (!nat.Discover())
                return new RouterCheck(false, null, NoUpnp(state));
            IPAddress external = nat.GetExternalIP();
            if (!IsInternet(external))
                return new RouterCheck(false, external,
                    $"Your router's internet address is {external}: your provider shares addresses between customers (CGNAT), " +
                    "or another router sits in front of yours. Friends can't reach you this way; use Tailscale.");
            return new RouterCheck(true, external,
                $"Router OK (UPnP). Friends join at  {external}. The server opens TCP {state.LoginPort} and {state.DeskPort} and UDP {state.UdpPort} " +
                $"on the router while it runs, to {nat.LocalAddress}.");
        }
        catch (Exception exception) when (exception is SocketException or WebException or IOException or
                                          System.Xml.XmlException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return new RouterCheck(false, null, NoUpnp(state));
        }
    }

    private static string NoUpnp(State state) =>
        "Your router did not answer UPnP (it may be off in the router's settings). Turn UPnP on, or forward " +
        $"TCP {state.LoginPort} and {state.DeskPort} and UDP {state.UdpPort} to this PC by hand and set PublicIP in server\\config\\serverconfig.xml " +
        "to your internet address.";

    /// <summary>This PC's Tailscale IPv4 addresses (100.64.0.0/10) on interfaces that are up.</summary>
    public static IReadOnlyList<IPAddress> TailscaleAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(IsTailscale)
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>This PC's addresses on the networks with a gateway, so not virtual adapters (WSL, Hyper-V).</summary>
    public static IReadOnlyList<IPAddress> HomeAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up && network.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IsInternet(address) && !IsTailscale(address) &&
                                  !IPAddress.IsLoopback(address) && JoinFriend.IsAllowedHost(address))
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    public static bool IsTailscale(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    public static string FirewallRuleName(string protocol, int port) => $"Divided Realms friends {protocol} {port}";

    private static (string Protocol, int Port)[] FirewallPorts(State state) =>
        [("TCP", state.LoginPort), ("TCP", state.DeskPort), ("UDP", state.UdpPort)];

    public static IReadOnlyList<string> FirewallRuleNames(State state) =>
        FirewallPorts(state).Select(port => FirewallRuleName(port.Protocol, port.Port)).ToList();

    /// <summary>
    /// The netsh lines that admit friends. Rules are tied to this install's CoreServer.exe, so Windows does not
    /// show its own (unrestricted) "allow access" prompt when the server first listens on the network.
    /// </summary>
    public static string FirewallScript(string runtime, State state, bool add, bool openToInternet = false)
    {
        string remote = openToInternet ? "any" : FriendRemoteAddresses;
        string program = Path.Combine(runtime, "server", "CoreServer.exe");
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine(add
            ? "rem Divided Realms: let friends reach the game server."
            : "rem Divided Realms: remove the Host for Friends firewall rules.");
        foreach (var (protocol, port) in FirewallPorts(state))
        {
            string name = FirewallRuleName(protocol, port);
            script.AppendLine($"netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1");
            if (add)
                script.AppendLine($"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} " +
                                  $"localport={port} remoteip={remote} program=\"{program}\" profile=any");
        }
        return script.ToString();
    }

    private static XElement LoadServer(string path, out XDocument document)
    {
        document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        return document.Root?.Element("Server") ?? throw new InvalidDataException("serverconfig.xml has no <Server> section.");
    }

    private static string? Value(XElement server, string name) => server.Element(name)?.Value.Trim();

    private static void Set(XElement server, string name, string value)
    {
        XElement? element = server.Element(name);
        if (element != null)
        {
            element.Value = value;
            return;
        }
        // A new setting goes on its own line after the last one, with the same indentation.
        XElement? last = server.Elements().LastOrDefault();
        if (last?.PreviousNode is XText { Value: var indent } && string.IsNullOrWhiteSpace(indent))
            last.AddAfterSelf(new XText(indent), new XElement(name, value));
        else
            server.Add(new XElement(name, value));
    }

    private static void Save(XDocument document, string path)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            document.Save(temporary, SaveOptions.DisableFormatting);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
