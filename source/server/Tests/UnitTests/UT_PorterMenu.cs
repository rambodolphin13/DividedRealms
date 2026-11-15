using System.Linq;
using DOL.GS;
using DOL.GS.Scripts;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_PorterMenu
{
    private static bool All(string name) => true;

    [Test]
    public void RealmWarGreetingNamesOnlyTheOwnRealm()
    {
        string text = PorterMenu.Greeting(eRealm.Albion, false, All);
        Assert.That(text, Does.Contain("[Border Keeps], [Battlegrounds] or [Frontier Regions]"));
        Assert.That(text, Does.Contain("our great city of [Camelot]"));
        Assert.That(text, Does.Contain("[Gothwaite Harbor]"));
        Assert.That(text, Does.Contain("the [Avalon Marsh]"));
        Assert.That(text, Does.Contain("[Oceanus]"));
        Assert.That(text, Does.Contain("the [Inconnu Crypt]"));
        Assert.That(text, Does.Contain("[towns]").And.Contain("[housing]").And.Contain("[Epic Dungeon]"));
        Assert.That(text, Does.Contain("[Labyrinth]").And.Contain("[anywhere else]"));
        Assert.That(text, Does.Not.Contain("Jordheim").And.Not.Contain("Tir na Nog").And.Not.Contain("Aegirhamn"));
    }

    [Test]
    public void CoopGreetingNamesAllThreeRealmsOwnFirst()
    {
        string text = PorterMenu.Greeting(eRealm.Midgard, true, All);
        Assert.That(text, Does.Contain("[Jordheim], [Camelot], or [Tir na Nog]"));
        Assert.That(text, Does.Contain("[Aegirhamn], [Gothwaite Harbor], or [Grove of Domnann]"));
        Assert.That(text, Does.Contain("[Gotar], [Avalon Marsh], or [Shannon Estuary]"));
        Assert.That(text, Does.Contain("[Kobold Undercity], [Inconnu Crypt], or [Shar Labyrinth]"));
    }

    [Test]
    public void NamesWithNoTeleportRowAreLeftOut()
    {
        string text = PorterMenu.Greeting(eRealm.Hibernia, false, name => name != "Labyrinth" && name != "Oceanus");
        Assert.That(text, Does.Not.Contain("[Labyrinth]").And.Not.Contain("[Oceanus]"));
        Assert.That(text, Does.Contain("The catacombs of the [Shar Labyrinth]"));
        Assert.That(text, Does.Contain("[anywhere else]"));
    }

    [Test]
    public void BorderKeepsListOldThenNewFrontiers()
    {
        string text = PorterMenu.BorderKeeps(eRealm.Hibernia, false, All);
        Assert.That(text, Is.EqualTo("Our border keeps guard the Old Frontiers at [Druim Ligen] or [Druim Cain], " +
                                     "and the New Frontiers at [Druim Ligen (New Frontiers)] or [Druim Cain (New Frontiers)]."));
        string none = PorterMenu.BorderKeeps(eRealm.Hibernia, false, _ => false);
        Assert.That(none, Does.StartWith("I cannot reach"));
    }

    [Test]
    public void EveryLinkInTheGreetingIsAWordThePorterAnswers()
    {
        // Words the porter answers itself rather than through teleport rows.
        string[] own = { "Border Keeps", "Battlegrounds", "Frontier Regions", "Shrouded Isles", "towns", "housing", "Epic Dungeon", "anywhere else" };
        var places = PorterMenu.Realms.SelectMany(r => new[] { r.Capital, r.Harbour, r.Wilds, r.Catacombs }).Concat(new[] { "Oceanus", "Labyrinth" });
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            string text = PorterMenu.Greeting(realm, true, All);
            var links = System.Text.RegularExpressions.Regex.Matches(text, @"\[([^\]]+)\]").Select(m => m.Groups[1].Value);
            foreach (string link in links)
                Assert.That(own.Contains(link) || places.Contains(link), $"{realm}: [{link}] has no answer");
        }
    }
}
