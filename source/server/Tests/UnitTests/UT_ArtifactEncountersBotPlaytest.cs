using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot playtest 2026-11-14: a level 50 player with seven bot companions fought the encounters for real. These pin
    /// the changes that made a lost fight winnable for one group.
    /// </summary>
    [TestFixture]
    public class UT_ArtifactEncountersBotPlaytest
    {
        [Test]
        public void GoldenSpear_ThreeKillsFitOneGroupsPace()
        {
            // Each 12x boss took one bot group about 7 minutes, and they stand 3,000-4,000 apart.
            ArtifactEncounterDef spear = ArtifactEncounters.Find("1028");
            Assert.That(spear.RequiredKills, Has.Length.EqualTo(3));
            Assert.That(ArtifactEncounters.KillWindowMs, Is.GreaterThanOrEqualTo(30 * 60 * 1000));
        }

        [Test]
        public void RingOfFire_WardenComesAloneAfterTheThirdWave()
        {
            // Six level 63 guardians plus the 12x Warden at once wiped a bot group twice; live rested between rounds.
            ArtifactEncounterDef ring = ArtifactEncounters.Find("1043");
            Assert.That(ring.Stages, Has.Length.EqualTo(4));

            for (int i = 0; i < 3; i++)
                Assert.That(ring.Stages[i].Spawns.All(spawn => !spawn.IsBoss), "wave " + (i + 1));

            Assert.That(ring.Stages[3].Spawns, Has.Length.EqualTo(1));
            Assert.That(ring.Stages[3].Spawns[0].IsBoss, Is.True);
        }
    }
}
