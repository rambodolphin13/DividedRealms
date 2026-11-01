using System.Net;
using System.Xml.Linq;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

public sealed class HostForFriendsTests
{
    private string _runtime = string.Empty;
    private string Config => HostForFriends.ConfigPath(_runtime);

    // A shipped serverconfig.xml, trimmed: loopback only, UPnP and region-IP detection off.
    private const string Shipped = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <Server>
            <Port>10300</Port>
            <IP>127.0.0.1</IP>
            <RegionIP>127.0.0.1</RegionIP>
            <RegionPort>10400</RegionPort>
            <UdpIP>127.0.0.1</UdpIP>
            <UdpPort>10400</UdpPort>
            <EnableUPnP>False</EnableUPnP>
            <DetectRegionIP>False</DetectRegionIP>
            <ServerName>Divided Realms</ServerName>
            <DBType>SQLITE</DBType>
            <DBConnectionString>Data Source=../data/opendaoc.sqlite3.db</DBConnectionString>
          </Server>
        </root>
        """;

    [SetUp]
    public void SetUp()
    {
        _runtime = Path.Combine(Path.GetTempPath(), "odc-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
        File.WriteAllText(Config, Shipped);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_runtime))
            Directory.Delete(_runtime, true);
    }

    private string Value(string name) => XDocument.Load(Config).Root!.Element("Server")!.Element(name)!.Value;

    [Test]
    public void ShippedConfigIsNotHosting()
    {
        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting, Is.False);
        Assert.That(state.LoginPort, Is.EqualTo(10300));
        Assert.That(state.UdpPort, Is.EqualTo(10400));
    }

    [Test]
    public void EnableListensEverywhereButKeepsLoopbackRegionAndRouterClosed()
    {
        HostForFriends.Enable(_runtime);

        Assert.That(HostForFriends.Read(_runtime).Hosting, Is.True);
        Assert.That(Value("IP"), Is.EqualTo("0.0.0.0"));
        Assert.That(Value("UdpIP"), Is.EqualTo("0.0.0.0"));
        // A loopback RegionIP makes the server answer each client with the address it connected to.
        Assert.That(Value("RegionIP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("EnableUPnP"), Is.EqualTo("False"));
        Assert.That(Value("DetectRegionIP"), Is.EqualTo("False"));
        Assert.That(Value("AutoAccountCreation"), Is.EqualTo("True"));
        Assert.That(Value("ServerName"), Is.EqualTo("Divided Realms"));
        Assert.That(Value("DBConnectionString"), Is.EqualTo("Data Source=../data/opendaoc.sqlite3.db"));
        Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Config)!, HostForFriends.BackupFileName)), Is.EqualTo(Shipped));
    }

    [Test]
    public void EnablingTwiceKeepsTheOriginalBackup()
    {
        HostForFriends.Enable(_runtime);
        HostForFriends.Enable(_runtime);
        Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Config)!, HostForFriends.BackupFileName)), Is.EqualTo(Shipped));
    }

    [Test]
    public void DisableReturnsToLoopback()
    {
        HostForFriends.Enable(_runtime);
        HostForFriends.Disable(_runtime);

        Assert.That(HostForFriends.Read(_runtime).Hosting, Is.False);
        Assert.That(Value("IP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("UdpIP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("Port"), Is.EqualTo("10300"));
    }

    [Test]
    public void FirewallRulesAreLimitedToFriendsAndThisServer()
    {
        var state = HostForFriends.Read(_runtime);
        string add = HostForFriends.FirewallScript(_runtime, state, add: true);
        string program = Path.Combine(_runtime, "server", "CoreServer.exe");

        Assert.That(add, Does.Contain("protocol=TCP localport=10300 remoteip=100.64.0.0/10,LocalSubnet"));
        Assert.That(add, Does.Contain("protocol=UDP localport=10400 remoteip=100.64.0.0/10,LocalSubnet"));
        Assert.That(add, Does.Contain($"program=\"{program}\""));
        Assert.That(add.Split('\n').Count(line => line.Contains("add rule")), Is.EqualTo(2));

        string remove = HostForFriends.FirewallScript(_runtime, state, add: false);
        Assert.That(remove, Does.Not.Contain("add rule"));
        Assert.That(remove, Does.Contain("delete rule name=\"Divided Realms friends TCP 10300\""));
    }

    [TestCase("100.64.0.1", true)]
    [TestCase("100.100.100.100", true)]
    [TestCase("192.168.1.2", false)]
    public void TailscaleRange(string address, bool tailscale) =>
        Assert.That(HostForFriends.IsTailscale(IPAddress.Parse(address)), Is.EqualTo(tailscale));

    [Test]
    public void InternetModeNeedsHostingAndOpensTheRouterWithoutChangingTheRegionAddress()
    {
        Assert.Throws<InvalidOperationException>(() => HostForFriends.EnableInternet(_runtime));

        HostForFriends.Enable(_runtime);
        Assert.Throws<InvalidOperationException>(() => HostForFriends.EnableInternet(_runtime));

        HostForFriends.SetHostPassword(_runtime, "swordfish");
        HostForFriends.EnableInternet(_runtime);

        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting && state.Internet && state.PasswordSet, Is.True);
        Assert.That(Value("EnableUPnP"), Is.EqualTo("True"));
        Assert.That(Value("DetectRegionIP"), Is.EqualTo("False"));
        Assert.That(Value("RegionIP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("PublicIP"), Is.EqualTo("auto"));
    }

    [Test]
    public void TurningHostingOffAlsoClosesTheRouterAndClearsThePassword()
    {
        HostForFriends.Enable(_runtime);
        HostForFriends.SetHostPassword(_runtime, "swordfish");
        HostForFriends.EnableInternet(_runtime);
        HostForFriends.Disable(_runtime);

        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting || state.Internet || state.PasswordSet, Is.False);
        Assert.That(Value("EnableUPnP"), Is.EqualTo("False"));
    }

    [Test]
    public void InternetModeOffKeepsHosting()
    {
        HostForFriends.Enable(_runtime);
        HostForFriends.SetHostPassword(_runtime, "swordfish");
        HostForFriends.EnableInternet(_runtime);
        HostForFriends.DisableInternet(_runtime);

        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting, Is.True);
        Assert.That(state.Internet, Is.False);
    }

    [Test]
    public void ManualPublicAddressIsKept()
    {
        HostForFriends.Enable(_runtime);
        var document = XDocument.Load(Config);
        document.Root!.Element("Server")!.Add(new XElement("PublicIP", "203.0.113.7"));
        document.Save(Config);

        HostForFriends.SetHostPassword(_runtime, "swordfish");
        HostForFriends.EnableInternet(_runtime);
        Assert.That(Value("PublicIP"), Is.EqualTo("203.0.113.7"));
        Assert.That(HostForFriends.ManualPublicAddress(_runtime), Is.EqualTo(IPAddress.Parse("203.0.113.7")));
    }

    [Test]
    public void AutomaticPublicAddressIsNotAnAddress()
    {
        HostForFriends.Enable(_runtime);
        HostForFriends.SetHostPassword(_runtime, "swordfish");
        HostForFriends.EnableInternet(_runtime);
        Assert.That(HostForFriends.ManualPublicAddress(_runtime), Is.Null);
    }

    [Test]
    public void InternetFirewallAdmitsAnyAddress()
    {
        var state = HostForFriends.Read(_runtime);
        string add = HostForFriends.FirewallScript(_runtime, state, add: true, openToInternet: true);
        Assert.That(add, Does.Contain("protocol=TCP localport=10300 remoteip=any "));
        Assert.That(add, Does.Contain("protocol=UDP localport=10400 remoteip=any "));
    }

    [Test]
    public void FirewallSettingRoundTrips()
    {
        HostForFriends.SaveSettings(_runtime, new HostForFriends.Settings { FirewallOpenToInternet = true });
        Assert.That(HostForFriends.LoadSettings(_runtime).FirewallOpenToInternet, Is.True);

        File.WriteAllText(HostForFriends.SettingsPath(_runtime), "{ broken");
        Assert.That(HostForFriends.LoadSettings(_runtime).FirewallOpenToInternet, Is.False);
    }

    [Test]
    public void OldSettingsFileLoadsWithTheFirewallNotOpen()
    {
        File.WriteAllText(HostForFriends.SettingsPath(_runtime), "{\"FriendAddresses\":[\"203.0.113.25\"],\"FirewallAppliedFor\":[\"203.0.113.25\"]}");
        Assert.That(HostForFriends.LoadSettings(_runtime).FirewallOpenToInternet, Is.False);
    }

    [Test]
    public void HostPasswordIsStoredOnlyAsAHash()
    {
        HostForFriends.Enable(_runtime);
        HostForFriends.SetHostPassword(_runtime, "swordfish");

        Assert.That(Value("HostPasswordHash"), Is.EqualTo(HostForFriends.HashHostPassword("swordfish")));
        Assert.That(File.ReadAllText(Config), Does.Not.Contain("swordfish"));

        HostForFriends.SetHostPassword(_runtime, "");
        Assert.That(HostForFriends.Read(_runtime).PasswordSet, Is.False);
    }

    // The server's HostPassword.Hash has this same value in UT_HostPassword.
    [Test]
    public void HostPasswordHashMatchesTheServer() =>
        Assert.That(HostForFriends.HashHostPassword("swordfish"), Is.EqualTo("4a9e1d7e2bb770e3f89110ee8d09517a3a4dbd6f4165881d3a79c6d3ef9d0273"));

    [TestCase("", false, true)]
    [TestCase("", true, false)]
    [TestCase("short", true, false)]
    [TestCase("short", false, false)]
    [TestCase("swordfish", true, true)]
    [TestCase("abcdefghijklmnopqrstu", true, false)]
    [TestCase("sw\u00f6rdfish", true, false)]
    public void HostPasswordRules(string password, bool internet, bool valid) =>
        Assert.That(HostForFriends.TryCheckHostPassword(password, internet, out _), Is.EqualTo(valid));
}
