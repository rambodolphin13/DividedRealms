using System.Data.SQLite;
using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class AccountsTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private string _runtime = null!;

    [SetUp] public void Setup()
    {
        _runtime = Path.Combine(Path.GetTempPath(), "accounts-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_runtime);
    }

    [TearDown] public void Cleanup() { SQLiteConnection.ClearAllPools(); Directory.Delete(_runtime, true); }

    private string AccountFile => File.ReadAllText(Path.Combine(_runtime, "account.txt"));

    [Test] public void TheFirstListKeepsTheAccountInUseAndItsPassword()
    {
        File.WriteAllText(Path.Combine(_runtime, "account.txt"), "Account: offline\r\nPassword: SECRET123\r\n");
        var accounts = PortableCredentials.List(_runtime);
        Assert.That(accounts, Is.EqualTo(new[] { new SavedAccount("offline", "SECRET123") }));
        Assert.That(File.ReadAllText(Path.Combine(_runtime, "accounts.txt")), Does.Contain("Account: offline\r\nPassword: SECRET123"));
    }

    [Test] public void MakeAndSwitchKeepEveryPassword()
    {
        File.WriteAllText(Path.Combine(_runtime, "account.txt"), "Account: offline\r\nPassword: SECRET123\r\n");
        SavedAccount alt = PortableCredentials.Create(_runtime, "altone");
        Assert.That(alt.Password, Has.Length.EqualTo(20));
        Assert.That(AccountFile, Does.Contain("offline"), "making an account does not switch to it");

        PortableCredentials.Use(_runtime, "ALTONE");
        Assert.That(PortableCredentials.ReadOrCreate(_runtime), Is.EqualTo(("altone", alt.Password)));
        Assert.That(PortableCredentials.List(_runtime).Select(a => a.Account), Is.EqualTo(new[] { "altone", "offline" }), "in use first");

        PortableCredentials.Use(_runtime, "offline");
        Assert.That(PortableCredentials.ReadOrCreate(_runtime), Is.EqualTo(("offline", "SECRET123")), "the old password came back");
        Assert.That(File.Exists(Path.Combine(_runtime, "account.txt.new")), Is.False);
    }

    [TestCase("ab", TestName = "TooShort")]
    [TestCase("abcdefghijklmnopqrstu", TestName = "TooLong")]
    [TestCase("my alt", TestName = "Space")]
    [TestCase("alt_1", TestName = "Symbol")]
    [TestCase("ÄltOne", TestName = "NotAscii")]
    [TestCase("  ", TestName = "Blank")]
    public void BadNamesAreRefused(string name)
    {
        PortableCredentials.ReadOrCreate(_runtime);
        Assert.That(PortableCredentials.NameProblem(name), Is.Not.Null);
        Assert.Throws<ArgumentException>(() => PortableCredentials.Create(_runtime, name));
    }

    [Test] public void ADuplicateNameIsRefusedWhateverTheCase()
    {
        PortableCredentials.ReadOrCreate(_runtime);
        Assert.Throws<ArgumentException>(() => PortableCredentials.Create(_runtime, "OFFLINE"));
        PortableCredentials.Create(_runtime, "Alt2");
        Assert.Throws<ArgumentException>(() => PortableCredentials.Create(_runtime, "alt2"));
        Assert.Throws<ArgumentException>(() => PortableCredentials.Use(_runtime, "nobody"));
    }

    [Test] public void TheAccountsWindowMakesAndSwitches()
    {
        PortableCredentials.ReadOrCreate(_runtime);
        Type formType = Launcher.GetType("OfflineDaoc.Launcher.AccountsForm")!;
        using var form = (Form)Activator.CreateInstance(formType, Hidden, null, [_runtime], null)!;
        var name = (TextBox)formType.GetField("_name", Hidden)!.GetValue(form)!;
        var list = (ListBox)formType.GetField("_list", Hidden)!.GetValue(form)!;
        string? switched = null;
        formType.GetEvent("Switched")!.AddEventHandler(form, new Action<string>(n => switched = n));

        name.Text = "Second";
        formType.GetMethod("MakeAccount", Hidden)!.Invoke(form, null);
        Assert.That(list.Items, Has.Count.EqualTo(2));
        Assert.That(list.SelectedItem!.ToString(), Is.EqualTo("Second"), "the new account is selected, not yet in use");

        formType.GetMethod("UseSelected", Hidden)!.Invoke(form, null);
        Assert.That(switched, Is.EqualTo("Second"));
        Assert.That(PortableCredentials.ReadOrCreate(_runtime).Account, Is.EqualTo("Second"));
        Assert.That(list.Items[0].ToString(), Does.EndWith("(in use)"));
    }

    [Test] public void MakeMeGmFollowsTheAccountInUse()
    {
        string db = Path.Combine(_runtime, "world.db");
        using var c = new SQLiteConnection($"Data Source={db};Pooling=False");
        c.Open();
        using (var setup = c.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE Account(Name TEXT PRIMARY KEY COLLATE NOCASE, PrivLevel INT);" +
                                "INSERT INTO Account VALUES('offline',2),('second',1),('third',1);" +
                                "CREATE TABLE offline_local_options(Key TEXT PRIMARY KEY,Value TEXT); INSERT INTO offline_local_options VALUES('MakeMeGM','true');";
            setup.ExecuteNonQuery();
        }
        MethodInfo apply = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!.GetMethod("ApplyGmToAccount", Hidden)!;
        Assert.That(apply.Invoke(null, [c, "Second"]), Is.EqualTo(1));
        Assert.That(apply.Invoke(null, [c, "nobody"]), Is.EqualTo(0), "an account the server hasn't made yet");
        using (var off = c.CreateCommand()) { off.CommandText = "UPDATE offline_local_options SET Value='false'"; off.ExecuteNonQuery(); }
        Assert.That(apply.Invoke(null, [c, "third"]), Is.EqualTo(0), "GM switch off");
        using var read = c.CreateCommand();
        read.CommandText = "SELECT group_concat(Name || '=' || PrivLevel) FROM (SELECT * FROM Account ORDER BY Name)";
        Assert.That(read.ExecuteScalar(), Is.EqualTo("offline=2,second=2,third=1"));
    }
}
