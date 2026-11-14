using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The encounter pass of 2026-11-14, middle third of the artifact list (Enyalio's Boots to Oglidarsh's Belt):
    /// each encounter keeps the zone, trigger, key and reward path it was checked against live with.
    /// </summary>
    [TestFixture]
    public class UT_ArtifactEncountersMiddle
    {
        // Id, zone, trigger, key item (null: none), underwater, world boss (no stages, credit from a Mob row).
        private static readonly object[] Cases =
        [
            new object[] { "1022", "Arbor Glen", "touch", "Offline_Artifact_Key_Small_Crystalline_Phial", false, false },
            new object[] { "1023", "Typhon's Reach", "touch", null, false, false },
            new object[] { "1024", "Oceanus Notos", "touch", null, true, false },
            new object[] { "1062", "Arbor Glen", "touch", "Offline_Artifact_Key_Aerus_Truesight_Potion", false, false },
            new object[] { "1025", "Oceanus Notos", "touch", null, true, true },
            new object[] { "1026", "Arbor Glen", "touch", "Offline_Artifact_Key_Blood_Soaked_Kibisis", false, false },
            new object[] { "1035", "Oceanus Hesperos", "night", null, false, false },
            new object[] { "1027", "Oceanus Hesperos", "touch", null, true, false },
            new object[] { "1030", "Stygian Delta", "touch", "Magical_Scarab_Wing", false, false },
            new object[] { "1028", "Stygian Delta", "touch", null, false, true },
            new object[] { "1029", "Green Glades", "touch", null, false, true },
            new object[] { "1031", "Oceanus Notos", "touch", null, false, true },
            new object[] { "1032", "Stygian Delta", "touch", null, false, true },
            new object[] { "1033", "Arbor Glen", "touch", null, false, true },
            new object[] { "1034", "Oceanus Notos", "near", null, false, false },
            new object[] { "1036", "Typhon's Reach", "touch", null, false, true },
            new object[] { "1037", "Typhon's Reach", "touch", null, false, false },
            new object[] { "1048", "Oceanus Hesperos", "touch", null, true, false },
            new object[] { "1038", "Land of Atum", "touch", null, false, true },
            new object[] { "1039", "Oceanus Anatole", "touch", "Offline_Artifact_Key_Ketos_Harness", true, false },
            new object[] { "1040", "Oceanus Hesperos", "touch", null, true, true },
        ];

        [TestCaseSource(nameof(Cases))]
        public void Encounter_KeepsItsLiveShape(string id, string zone, string trigger, string key, bool underwater, bool worldBoss)
        {
            ArtifactEncounterDef encounter = ArtifactEncounters.Find(id);

            Assert.That(encounter, Is.Not.Null, id);
            Assert.That(encounter.Zone, Is.EqualTo(zone), encounter.Name);
            Assert.That(encounter.Trigger, Is.EqualTo(trigger), encounter.Name);
            Assert.That(encounter.RequiredItem, Is.EqualTo(key), encounter.Name);
            Assert.That(encounter.Underwater, Is.EqualTo(underwater), encounter.Name);

            if (worldBoss)
            {
                Assert.That(encounter.Stages, Is.Empty, encounter.Name);
                return;
            }

            // Credit comes only from the last stage's bosses, so a staged encounter with none there could never pay.
            ArtifactStageDef last = encounter.Stages.Last();
            Assert.That(last.Spawns.Any(spawn => spawn.IsBoss), encounter.Name);
            Assert.That(encounter.Stages.Take(encounter.Stages.Length - 1).All(stage => stage.Spawns.Length > 0 || stage.Escort != null), encounter.Name);
        }

        [Test]
        public void GoldenSpear_NeedsAllThreeDjinn()
        {
            Assert.That(ArtifactEncounters.Find("1028").RequiredKills, Is.EquivalentTo(new[] { "Landrine", "Hounthro", "Jomnaer" }));
        }

        [Test]
        public void EscortEncounters_WalkSomewhere()
        {
            foreach (string id in new[] { "1027", "1039" })
            {
                ArtifactEscortDef escort = ArtifactEncounters.Find(id).Stages[0].Escort;
                Assert.That(escort, Is.Not.Null, id);
                Assert.That(escort.X != 0 && escort.Y != 0, id);
            }
        }

        [Test]
        public void GateEncounters_GuardTheirBoss()
        {
            // Bergopes needs the gorgon dead first; Malamis needs both chests broken.
            foreach (string id in new[] { "1026", "1037" })
            {
                ArtifactStageDef gate = ArtifactEncounters.Find(id).Stages.Single(stage => stage.Gate);
                Assert.That(gate.Spawns.Any(spawn => spawn.IsBoss) && gate.Spawns.Any(spawn => !spawn.IsBoss), id);
            }
        }

        [Test]
        public void KalaresWarrior_FightsAsTheTextSays()
        {
            // Our Warrior of Stone fights back (live's did not), so the stage text must not promise otherwise.
            string text = ArtifactEncounters.Find("1034").Stages[0].Text;
            Assert.That(text, Does.Not.Contain("does not raise"));
        }
    }
}
