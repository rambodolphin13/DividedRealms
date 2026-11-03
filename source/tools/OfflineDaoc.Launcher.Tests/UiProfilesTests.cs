using System.Reflection;
using System.Text;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class UiProfilesTests
{
    private string _folder = "";
    private string Settings => Path.Combine(_folder, "LotM");
    private string Profiles => Path.Combine(_folder, "ui-profiles");

    private const string Main = "[Panels]\r\nVersion=1\r\nChatWindow0=Main,0,701,468,196,100,100,11,1,0,1\r\nLFGClass2=0,0\r\nLFGClass2=80,97\r\n" +
        "[Quickbar]\r\nGroupSize=10\r\nHotkey_0=35,300,Thunderstroke,1635\r\n[Quickbar2]\r\nGroupSize=10\r\n[Quickbar3]\r\nGroupSize=10\r\n" +
        "[Macros]\r\nMacro_0=Hi,/say Hello\r\n[Chat]\r\nChannelVer=1\r\n[NameOptions]\r\nVersion=3\r\n\r\n[QuickBinds]\r\n\r\n[HouseHelp]\r\n\r\n[Camera]\r\ndistance=500.00\r\n";
    private const string Alt = "[Panels]\r\nVersion=1\r\nChatWindow0=Main,0,0,10,10,100,100,11,1,0,1\r\n[Quickbar]\r\nGroupSize=10\r\nHotkey_0=10,1,Heal,99\r\n" +
        "[Macros]\r\n[Chat]\r\nChannelVer=1\r\nOld=1\r\n[Camera]\r\ndistance=200.00\r\n";

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "ui-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Settings);
        File.WriteAllText(Path.Combine(Settings, "Natex-5.ini"), Main, Encoding.Latin1);
        File.WriteAllText(Path.Combine(Settings, "Altey-5.ini"), Alt, Encoding.Latin1);
        File.SetLastWriteTime(Path.Combine(Settings, "Natex-5.ini"), new DateTime(2026, 11, 1, 19, 0, 0));
        File.SetLastWriteTime(Path.Combine(Settings, "Altey-5.ini"), new DateTime(2026, 10, 28, 9, 0, 0));
        File.WriteAllText(Path.Combine(Settings, "Natex-5.ign"), "ignore list");
        File.WriteAllText(Path.Combine(Settings, "user.dat"), "[main]");
    }

    [TearDown] public void Cleanup() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    private string Read(string name) => File.ReadAllText(Path.Combine(Settings, name), Encoding.Latin1);

    [Test] public void OnlyCharacterFilesAreListedLastPlayedFirst()
    {
        Assert.That(UiProfiles.Characters(Settings).Select(c => c.ToString()),
            Is.EqualTo(new[] { "Natex (server 5, played 1 Nov)", "Altey (server 5, played 28 Oct)" }));
        Assert.That(UiProfiles.Characters(Path.Combine(_folder, "missing")), Is.Empty);
    }

    [Test] public void CopyingTheLayoutChangesOnlyTheLayoutAndBacksUpFirst()
    {
        var backups = UiProfiles.Apply(Path.Combine(Settings, "Natex-5.ini"), [UiPart.Layout], [Path.Combine(Settings, "Altey-5.ini")],
            Path.Combine(Profiles, "backups"), new DateTime(2026, 11, 3, 8, 0, 0));

        string alt = Read("Altey-5.ini");
        Assert.That(alt, Does.Contain("ChatWindow0=Main,0,701,468"));
        Assert.That(alt, Does.Contain("LFGClass2=0,0\r\nLFGClass2=80,97"), "repeated keys stay as the game wrote them");
        Assert.That(alt, Does.Contain("Hotkey_0=10,1,Heal,99"), "quickbars untouched");
        Assert.That(alt, Does.Contain("distance=200.00"), "camera untouched");
        Assert.That(backups, Has.Count.EqualTo(1));
        Assert.That(Path.GetFileName(backups[0]), Is.EqualTo("Altey-5-20261103-080000.ini"));
        Assert.That(File.ReadAllText(backups[0], Encoding.Latin1), Is.EqualTo(Alt));
    }

    [Test] public void SectionsTheTargetLacksAreAddedAndOnesTheSourceLacksAreKept()
    {
        UiProfiles.Apply(Path.Combine(Settings, "Natex-5.ini"), [UiPart.Quickbars, UiPart.Display, UiPart.Chat],
            [Path.Combine(Settings, "Altey-5.ini")], Path.Combine(Profiles, "backups"), DateTime.Now);

        string alt = Read("Altey-5.ini");
        Assert.That(alt, Does.Contain("[Quickbar3]\r\nGroupSize=10"));
        Assert.That(alt, Does.Contain("Hotkey_0=35,300,Thunderstroke,1635"));
        Assert.That(alt, Does.Contain("[NameOptions]\r\nVersion=3"));
        Assert.That(alt, Does.Contain("distance=500.00"));
        Assert.That(alt, Does.Not.Contain("Old=1"), "chat replaced as a whole");
    }

    [Test] public void AProfileKeepsOnlyItsPartsAndCanBePutOnOthers()
    {
        string profile = UiProfiles.SaveProfile(Path.Combine(Settings, "Natex-5.ini"), [UiPart.Macros], Profiles, "  My macros! ");

        Assert.That(Path.GetFileName(profile), Is.EqualTo("My macros.ini"));
        Assert.That(File.ReadAllText(profile, Encoding.Latin1), Is.EqualTo("[Macros]\r\nMacro_0=Hi,/say Hello\r\n[QuickBinds]\r\n\r\n"));
        Assert.That(UiProfiles.SavedProfiles(Profiles), Is.EqualTo(new[] { "My macros" }));

        UiProfiles.Apply(profile, [UiPart.Macros, UiPart.Layout], [Path.Combine(Settings, "Altey-5.ini")], Path.Combine(Profiles, "backups"), DateTime.Now);
        string alt = Read("Altey-5.ini");
        Assert.That(alt, Does.Contain("Macro_0=Hi,/say Hello"));
        Assert.That(alt, Does.Contain("ChatWindow0=Main,0,0,10,10"), "a profile without the layout leaves the layout alone");
        Assert.Throws<ArgumentException>(() => UiProfiles.SaveProfile(Path.Combine(Settings, "Natex-5.ini"), [UiPart.Macros], Profiles, "!!"));
    }

    [Test] public void UntouchedBytesSurviveARoundTrip()
    {
        string accented = "[Panels]\r\nTitle=Café\r\n[Camera]\r\ndistance=1\r\n";
        File.WriteAllText(Path.Combine(Settings, "Natex-5.ini"), accented, Encoding.Latin1);

        UiProfiles.Apply(Path.Combine(Settings, "Altey-5.ini"), [UiPart.Chat], [Path.Combine(Settings, "Natex-5.ini")], Path.Combine(Profiles, "backups"), DateTime.Now);

        byte[] bytes = File.ReadAllBytes(Path.Combine(Settings, "Natex-5.ini"));
        Assert.That(bytes, Does.Contain((byte)0xE9));
        Assert.That(Encoding.Latin1.GetString(bytes), Does.StartWith("[Panels]\r\nTitle=Café\r\n[Camera]\r\ndistance=1\r\n[Chat]\r\nChannelVer=1\r\nOld=1\r\n"));
    }

    [Test] public void TheTabRefusesWhileTheGameRunsThenCopies()
    {
        bool running = true;
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.UiProfilesControl")!;
        using var tab = (Control)Activator.CreateInstance(type, Settings, Profiles, (Func<bool>)(() => running))!;
        var controls = All(tab).ToList();
        var source = (ComboBox)controls.Single(c => c.AccessibleName == "Copy from");
        var targets = (CheckedListBox)controls.Single(c => c.AccessibleName == "Copy to");
        var copy = controls.OfType<Button>().Single(b => b.Text == "Copy to checked characters");

        Assert.That(source.SelectedItem!.ToString(), Does.StartWith("Natex"), "the last played character is the default source");
        Assert.That(targets.Items.Cast<object>().Select(item => item.ToString()), Is.EqualTo(new[] { "Altey (server 5, played 28 Oct)" }));
        targets.SetItemChecked(0, true);
        copy.PerformClick();
        Assert.That(Read("Altey-5.ini"), Is.EqualTo(Alt), "nothing written while the game runs");

        running = false;
        copy.PerformClick();
        Assert.That(Read("Altey-5.ini"), Does.Contain("ChatWindow0=Main,0,701,468"));
        Assert.That(Read("Altey-5.ini"), Does.Contain("Hotkey_0=10,1,Heal,99"), "quickbars are off by default");
    }

    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(child => All(child).Prepend(child));
}
