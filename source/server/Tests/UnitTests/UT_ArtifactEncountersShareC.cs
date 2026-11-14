using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Encounter pass 2026-11-14, last third of the artifacts by name (Orion's Belt to Wing's Dive): each encounter
    /// has a way to give credit, and the per-artifact choices made against the live sources hold.
    /// </summary>
    [TestFixture]
    public class UT_ArtifactEncountersShareC
    {
        private static readonly string[] Share =
        [
            "1041", "1042", "1063", "1043", "1044", "1045", "1046", "1047", "1049", "1050",
            "1051", "1053", "1052", "1060", "1061", "1055", "1056", "1057", "1058", "1059"
        ];

        // Encounters whose credit comes from Mob rows out in the world (ArtifactBoss with PackageID artifact:id).
        private static readonly string[] WorldBoss = ["1042", "1044", "1046", "1049", "1051", "1055", "1056", "1059"];

        [Test]
        public void EveryArtifactInTheShareHasAnEncounter()
        {
            foreach (string id in Share)
                Assert.That(ArtifactEncounters.Find(id), Is.Not.Null, id);
        }

        [Test]
        public void AnchoredEncountersEndWithACreditBoss()
        {
            foreach (string id in Share.Except(WorldBoss))
            {
                ArtifactEncounterDef encounter = ArtifactEncounters.Find(id);
                Assert.That(encounter.Stages, Is.Not.Empty, encounter.Name);

                ArtifactStageDef last = encounter.Stages[^1];
                Assert.That(last.Spawns.Any(spawn => spawn.IsBoss), encounter.Name + ": only last-stage bosses give credit");
                Assert.That(last.AtHealth, Is.EqualTo(0), encounter.Name + ": an 'at' stage cannot be last");
                Assert.That(last.Escort, Is.Null, encounter.Name + ": an escort stage gives no credit by itself");

                if (last.Gate)
                    Assert.That(last.Spawns.Any(spawn => !spawn.IsBoss), encounter.Name + ": a gate needs something to break");
            }
        }

        [Test]
        public void PhoebusHarp_AnyOfTheThreeNamedGivesCredit()
        {
            // Live: whichever triton held the harp gave credit, Hallea in most reports (bkdaoc harp.txt, 1.68 notes).
            ArtifactEncounterDef harp = ArtifactEncounters.Find("1042");
            Assert.That(harp.RequiredKills, Is.Null.Or.Empty);
            Assert.That(harp.Underwater, Is.True);
        }

        [Test]
        public void StaffOfTheGods_IsOneGroupsFight()
        {
            // Live: done by three people with Speed of Sound and by one full group (bkdaoc staffgods.txt).
            Assert.That(ArtifactEncounters.Find("1052").HealthFactor, Is.EqualTo(12));
        }

        [Test]
        public void SeaFloorSitesSwim()
        {
            foreach (string id in new[] { "1042", "1052", "1056", "1057", "1061" })
                Assert.That(ArtifactEncounters.Find(id).Underwater, Is.True, id);
        }
    }
}
