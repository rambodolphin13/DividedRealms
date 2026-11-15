using System.Linq;
using DOL.AI.Brain;
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
        public void RingOfFire_WavesOfFiveThenTheWardenAlone()
        {
            // Waves of 7 and 6 guardians plus the 12x Warden wiped a bot group three times; live had CC and a rest
            // between rounds. The 19 guardians now come at most five at a time, then the Warden alone.
            ArtifactEncounterDef ring = ArtifactEncounters.Find("1043");
            ArtifactStageDef[] waves = ring.Stages[..^1];

            Assert.That(waves.Sum(wave => wave.Spawns.Sum(spawn => spawn.Count)), Is.EqualTo(19));
            Assert.That(waves.All(wave => wave.Spawns.All(spawn => !spawn.IsBoss) && wave.Spawns.Sum(spawn => spawn.Count) <= 5));
            Assert.That(ring.Stages[^1].Spawns, Has.Length.EqualTo(1));
            Assert.That(ring.Stages[^1].Spawns[0].IsBoss, Is.True);
        }

        [Test]
        public void GateAdds_WaitToBeAttacked()
        {
            // Ten Control Links aggroing at once wiped a full bot group twice at Karise; live links stayed put.
            StandardMobBrain brain = new() { AggroLevel = 100, AggroRange = 1500 };
            ArtifactEncounterAnchor.MakeGateAddWait(brain);
            Assert.That(brain.AggroLevel, Is.Zero);
            Assert.That(brain.AggroCount, Is.Zero);
        }
    }
}
