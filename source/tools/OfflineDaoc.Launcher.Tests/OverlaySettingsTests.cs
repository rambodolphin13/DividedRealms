using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class OverlaySettingsTests
{
    private string _folder = "";
    private string Path_ => Path.Combine(_folder, OverlaySettings.FileName);

    [SetUp] public void Setup() => Directory.CreateDirectory(_folder = Path.Combine(Path.GetTempPath(), "overlay-settings-" + Guid.NewGuid().ToString("N")));
    [TearDown] public void Cleanup() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    [Test] public void OnlyChangedValuesAreSavedAndTheyComeBack()
    {
        var settings = new OverlaySettings();
        settings["numbers.group"] = "False";
        settings["card.x"] = "0.6";
        settings["color.crit"] = "#ff00aa";
        settings["numbers.size"] = "1";
        settings.Save(Path_);

        var loaded = OverlaySettings.Load(Path_);

        Assert.That(loaded.Changed.Keys, Is.EquivalentTo(new[] { "numbers.group", "card.x", "color.crit" }));
        Assert.That(loaded.On("numbers.group"), Is.False);
        Assert.That(loaded.Number("card.x"), Is.EqualTo(0.6f));
        Assert.That(loaded["color.crit"], Is.EqualTo("#FF00AA"));
    }

    [Test] public void BadOrUnknownValuesFallBackAndNumbersStayInRange()
    {
        File.WriteAllText(Path_, "{\"numbers.size\":\"99\",\"card.seconds\":\"soon\",\"color.heal\":\"green\",\"no.such\":\"1\",\"panels.self\":\"maybe\"}");

        var loaded = OverlaySettings.Load(Path_);

        Assert.That(loaded.Number("numbers.size"), Is.EqualTo(2f));
        Assert.That(loaded.Number("card.seconds"), Is.EqualTo(15f));
        Assert.That(loaded["color.heal"], Is.EqualTo("#5AEB6E"));
        Assert.That(loaded.On("panels.self"), Is.True);
        Assert.That(loaded.Changed.Keys, Is.EqualTo(new[] { "numbers.size" }));
    }

    [Test] public void ABrokenFileIsTheDefaults()
    {
        File.WriteAllText(Path_, "{ not json");
        Assert.That(OverlaySettings.Load(Path_).Changed, Is.Empty);
        Assert.That(OverlaySettings.Load(Path.Combine(_folder, "missing.json")).Changed, Is.Empty);
    }

    [Test] public void EveryDefaultIsAlreadyClean()
    {
        foreach (OverlaySetting setting in OverlaySettings.Schema)
        {
            var settings = new OverlaySettings();
            settings[setting.Key] = setting.Default;
            Assert.That(settings.Changed, Is.Empty, setting.Key);
        }
    }

    [Test] public void SwitchedOffKindsOfNumbersAreNotShown()
    {
        var settings = new OverlaySettings();
        settings["numbers.group"] = "False";
        settings["numbers.avoided"] = "False";
        var mine = new CombatNumber(CombatNumberKind.Out, CombatNumberWho.Self, 10, false, 1, null);

        Assert.That(FloatingNumbers.Shows(mine, settings), Is.True);
        Assert.That(FloatingNumbers.Shows(mine with { Who = CombatNumberWho.Group }, settings), Is.False);
        Assert.That(FloatingNumbers.Shows(mine with { Kind = CombatNumberKind.AvoidIn, Text = "Block" }, settings), Is.False);
        settings["numbers.enabled"] = "False";
        Assert.That(FloatingNumbers.Shows(mine, settings), Is.False);
    }

    [Test] public void NumbersMoveAndRecolourWithTheSettings()
    {
        var settings = new OverlaySettings();
        settings["numbers.x"] = "0.3";
        settings["color.yours"] = "#00FF00";
        var numbers = new FloatingNumbers();
        numbers.Add(new CombatNumber(CombatNumberKind.Out, CombatNumberWho.Self, 10, false, 1, null), 0);

        FloatingNumber shown = numbers.Frame(0, settings)[0];

        Assert.That(shown.X, Is.EqualTo(0.36f).Within(0.001f));
        Assert.That(shown.Color.ToArgb(), Is.EqualTo(Color.FromArgb(0, 255, 0).ToArgb()));
    }

    [Test] public void PanelsCanBeSwitchedOffOneByOne()
    {
        var snapshot = new EffectSnapshot([new EffectInfo("Haste", 1, 5, 10, true, true)], [new EffectUnit("Fang", 18, 90, [])],
            new EffectUnit("crab", 12, 40, []));
        var settings = new OverlaySettings();
        settings["panels.pets"] = "False";

        Assert.That(EffectPanels.Sections(snapshot, settings).Select(section => section.Title), Is.EqualTo(new[] { "You", "crab  12" }));
    }

    [Test] public void TheTabHasAControlForEverySettingAndSavesWhatChanged()
    {
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.OverlaySettingsControl")!;
        using var tab = (Control)Activator.CreateInstance(type, Path_)!;
        var named = All(tab).Where(control => control.AccessibleName != null).ToDictionary(control => control.AccessibleName!);

        Assert.That(named.Keys, Is.SupersetOf(OverlaySettings.Schema.Select(setting => setting.Key)));
        ((CheckBox)named["card.enabled"]).Checked = false;
        ((TrackBar)named["panels.y"]).Value = 0;
        Assert.That((bool)type.GetProperty("HasUnsavedChanges")!.GetValue(tab)!, Is.True);
        All(tab).OfType<Button>().Single(button => button.Text == "Save").PerformClick();

        var saved = OverlaySettings.Load(Path_);
        Assert.That(saved.On("card.enabled"), Is.False);
        Assert.That(saved.Number("panels.y"), Is.EqualTo(0f));
        Assert.That((bool)type.GetProperty("HasUnsavedChanges")!.GetValue(tab)!, Is.False);
    }

    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(child => All(child).Prepend(child));
}
