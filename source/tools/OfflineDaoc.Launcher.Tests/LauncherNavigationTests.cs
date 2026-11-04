using System.Reflection;
using NUnit.Framework;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class LauncherNavigationTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type MainForm = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.MainForm")!;
    private static T Field<T>(Form form, string name) => (T)MainForm.GetField(name, Hidden)!.GetValue(form)!;
    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(child => All(child).Prepend(child));

    [Test] public void EveryPageHasASidebarEntryAndTheEntryShowsIt()
    {
        using var form = (Form)Activator.CreateInstance(MainForm)!;
        var pages = Field<TabControl>(form, "_pages");
        var sidebar = All(form).Single(control => control.GetType().Name == "LauncherSidebar");
        var entries = (System.Collections.IDictionary)sidebar.GetType().GetProperty("Entries")!.GetValue(sidebar)!;

        Assert.That(pages.TabPages.Cast<TabPage>().Where(page => !entries.Contains(page)).Select(page => page.Text), Is.Empty);
        Assert.That(pages.TabPages[0].Text, Is.EqualTo("Home"), "the launcher opens on Home");
        TabPage bots = pages.TabPages.Cast<TabPage>().Single(page => page.Text == "Bots");
        _ = pages.Handle; // A tab control without a window ignores a new selection.
        // A button in a window that was never shown cannot be clicked, so raise its click as a click would.
        typeof(Control).GetMethod("OnClick", Hidden)!.Invoke(entries[bots], [EventArgs.Empty]);
        Assert.That(pages.SelectedTab, Is.SameAs(bots));
    }

    [Test] public void BotCreationAndDeleteAllLiveOnTheBotsPageOnly()
    {
        using var form = (Form)Activator.CreateInstance(MainForm)!;
        var pages = Field<TabControl>(form, "_pages");
        TabPage bots = pages.TabPages.Cast<TabPage>().Single(page => page.Text == "Bots");
        var generate = Field<List<Button>>(form, "_realmGenerateButtons");
        var deleteAll = Field<Button>(form, "_deleteAllBotsButton");

        Assert.That(generate, Has.Count.EqualTo(6));
        Assert.That(generate.All(button => All(bots).Contains(button)), Is.True);
        Assert.That(All(bots).Contains(deleteAll), Is.True);
        Assert.That(deleteAll.Enabled, Is.False);
    }

    [Test] public void BotsPageShowsTheCountsPerRealm()
    {
        using var form = (Form)Activator.CreateInstance(MainForm)!;
        Type row = MainForm.GetNestedType("BotRow", BindingFlags.NonPublic)!;
        object Bot(string realm, bool online, bool queued = false) => Activator.CreateInstance(row,
            [null, "x", realm, "", "", "", 1, "", "", online, true, queued, "", "", "", "", "", "", "", ""])!;
        var bots = Array.CreateInstance(row, 4);
        bots.SetValue(Bot("Albion", true), 0);
        bots.SetValue(Bot("Albion", false), 1);
        bots.SetValue(Bot("Hibernia", true), 2);
        bots.SetValue(Bot("Midgard", true, queued: true), 3);
        MainForm.GetMethod("UpdateBotsPageCounts", Hidden)!.Invoke(form, [bots]);

        var counts = Field<Dictionary<string, Label>>(form, "_botsPageRealmCounts");
        Assert.That(counts["ALBION"].Text, Is.EqualTo("2 bots  ·  1 online"));
        Assert.That(counts["MIDGARD"].Text, Is.EqualTo("0 bots  ·  0 online"));
        Assert.That(counts["HIBERNIA"].Text, Is.EqualTo("1 bot  ·  1 online"));
        Assert.That(Field<Label>(form, "_botsPageTotal").Text, Is.EqualTo("3 bots in all  ·  2 online now"));
    }

    [Test] public void PlaySaysWhatItWillDo()
    {
        using var form = (Form)Activator.CreateInstance(MainForm)!;
        var play = Field<Button>(form, "_homePlay");
        var start = Field<Button>(form, "_startButton");
        var enter = Field<Button>(form, "_playButton");
        var status = Field<Label>(form, "_statusPill");
        MethodInfo update = MainForm.GetMethod("UpdateHome", Hidden)!;

        start.Enabled = true;
        enter.Enabled = false;
        update.Invoke(form, null);
        Assert.That(play.Text, Is.EqualTo("PLAY"));
        Assert.That(play.Enabled, Is.True);
        Assert.That(status.Text, Is.EqualTo("● SERVER STOPPED"));

        start.Enabled = false;
        Field<Label>(form, "_serverState").Text = "STARTING…";
        update.Invoke(form, null);
        Assert.That(play.Enabled, Is.False);
        Assert.That(play.Text, Is.EqualTo("STARTING…"));

        Field<Label>(form, "_serverState").Text = "RUNNING";
        Field<Label>(form, "_onlineValue").Text = "3,334";
        enter.Enabled = true;
        update.Invoke(form, null);
        Assert.That(play.Text, Is.EqualTo("ENTER REALM"));
        Assert.That(status.Text, Is.EqualTo("● RUNNING  ·  3,334 bots online"));
    }
}
