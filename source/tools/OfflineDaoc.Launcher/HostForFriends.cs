using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Host for Friends: lets friends on Tailscale or the home network join this game's server.
///
/// The server normally listens on 127.0.0.1 only. Hosting makes it listen on every interface (IP and UdpIP
/// 0.0.0.0) so this PC's own client keeps using 127.0.0.1 and friends reach it on the Tailscale address.
/// RegionIP stays 127.0.0.1: the server then sends each client the address that client connected to
/// (PacketLib168/1124/1126 swap a loopback region address for the socket's local address).
/// UPnP and region-IP detection stay off, so no router port is opened. Windows Firewall rules (made by
/// the host with administrator approval) only admit Tailscale and the local subnet.
/// </summary>
public static class HostForFriends
{
    public const string Loopback = "127.0.0.1";
    public const string AllInterfaces = "0.0.0.0";
    public const string FriendRemoteAddresses = "100.64.0.0/10,LocalSubnet";
    public const string BackupFileName = "serverconfig.before-host-for-friends.xml";

    public sealed record State(bool Hosting, int LoginPort, int UdpPort, bool AutoAccountCreation);

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
            AutoAccountCreation: !string.Equals(Value(server, "AutoAccountCreation"), "False", StringComparison.OrdinalIgnoreCase));
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
        Save(document, path);
    }

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

    public static bool IsTailscale(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    public static string FirewallRuleName(State state, string protocol) =>
        $"Divided Realms friends {protocol} {(protocol == "TCP" ? state.LoginPort : state.UdpPort)}";

    /// <summary>
    /// The netsh lines that admit friends. Rules are tied to this install's CoreServer.exe, so Windows does not
    /// show its own (unrestricted) "allow access" prompt when the server first listens on the network.
    /// </summary>
    public static string FirewallScript(string runtime, State state, bool add)
    {
        string program = Path.Combine(runtime, "server", "CoreServer.exe");
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine(add
            ? "rem Divided Realms: let friends on Tailscale or this home network reach the game server."
            : "rem Divided Realms: remove the Host for Friends firewall rules.");
        foreach (var (protocol, port) in new[] { ("TCP", state.LoginPort), ("UDP", state.UdpPort) })
        {
            string name = FirewallRuleName(state, protocol);
            script.AppendLine($"netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1");
            if (add)
                script.AppendLine($"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} " +
                                  $"localport={port} remoteip={FriendRemoteAddresses} program=\"{program}\" profile=any");
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
        if (element == null)
            server.Add(new XElement(name, value));
        else
            element.Value = value;
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
