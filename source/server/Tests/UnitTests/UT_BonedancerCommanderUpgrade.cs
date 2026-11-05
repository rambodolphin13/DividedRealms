using NUnit.Framework;
using static DOL.GS.CommanderPet;

namespace DOL.GS.Tests;

/// <summary>
/// Night 2026-11-04: seven level 50 Bonedancers froze in Jordheim all night. Summon Dread Lord (level 50, template 52)
/// summons a DreadCommander (rank 45), and 45 &lt; 50 made the bot replace its commander on every think.
/// </summary>
[TestFixture]
public sealed class UT_BonedancerCommanderUpgrade
{
    [Test]
    public void ADreadCommanderIsNotReplacedByTheLevel50DreadLordSpell()
    {
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadCommander, 52, 50), Is.False);
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadCommander, 52, 40), Is.False);
    }

    [Test]
    public void ALowerTierIsStillReplaced()
    {
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.ReturnedCommander, 50, 15), Is.True);
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.BoneCommander, 52, 40), Is.True);
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.SkeletalCommander, 50, 22), Is.False);
    }

    [Test]
    public void AnUnknownCommanderIsNeverReplacedInALoop()
    {
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.Unknown, 52, 50), Is.False);
    }
}
