using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Discord 2026-11-18 (BornToReignGamingYT): DR-291 gem reuse, DR-487 /bank.</summary>
    [TestFixture]
    public class UT_DiscordFixes1118
    {
        [Test]
        public void MerchantGemCanBeCalledAgainAsSoonAsTheCampLeaves() =>
            Assert.That(SummonedMerchantCamp.ReuseMs, Is.EqualTo(SummonedMerchantCamp.DurationMs));

        [TestCase(true, false, null)]
        [TestCase(true, true, "You can't reach your vaults while in combat.")]
        [TestCase(false, false, "You can't reach your vaults while dead.")]
        public void BankOnlyRefusesTheDeadAndThoseInCombat(bool alive, bool inCombat, string expected) =>
            Assert.That(BankCommandHandler.Refusal(alive, inCombat), Is.EqualTo(expected));

        [Test]
        public void ArtifactCreditMissTellsThePlayerWhy()
        {
            Assert.That(ArtifactEncounters.MissedMessage("The weakened criosphinx", "Shades of Mist", "dead"), Does.Contain("while you were dead"));
            Assert.That(ArtifactEncounters.MissedMessage("Danos", "Guard of Valor", "far"), Does.Contain("too far").And.Contain("7000"));
            Assert.That(ArtifactEncounters.MissedMessage("Danos", "Guard of Valor", "offline"), Is.Null);
            Assert.That(ArtifactEncounters.MissedReason(null, null), Is.EqualTo("offline"));
        }

        [TestCase(true, true, false, true)]
        [TestCase(true, true, true, false)]    // the group mate's target is mezzed too
        [TestCase(false, true, false, false)]  // a group mate who is not fighting
        [TestCase(true, false, false, false)]  // a heal target or a dead monster
        public void GroupFightKeepsCompanionsOffTheMezzedAdd(bool fighting, bool enemy, bool controlled, bool expected) =>
            Assert.That(DOL.AI.Brain.BotBrain.UncontrolledFightRule(fighting, enemy, controlled), Is.EqualTo(expected));

        [Test]
        public void LevelFiftyCompanionKillsFillTheMasterLevelBar()
        {
            // one span of level 49->50 experience at rate 1 is one Master Level
            var one = AccountCompanionEndgame.AddMasterExperience(0, 0, 1000, 1000, 1.0);
            Assert.That((one.Level, one.Experience, one.LevelsGained), Is.EqualTo((1, 0L, 1)));
            var half = AccountCompanionEndgame.AddMasterExperience(3, 100, 500, 1000, 1.0);
            Assert.That((half.Level, half.Experience), Is.EqualTo((3, 16100L)));
            var capped = AccountCompanionEndgame.AddMasterExperience(9, 0, 100000, 1000, 1.0);
            Assert.That((capped.Level, capped.Experience), Is.EqualTo((10, 0L)));
        }

        [Test]
        public void CompanionChampionExperienceComesFromRealmPointsOnlyForChampions()
        {
            Assert.That(AccountCompanionEndgame.AddChampionExperience(false, 0, 0, 100000, 1.0).Experience, Is.EqualTo(0));
            var p = AccountCompanionEndgame.AddChampionExperience(true, 0, 0, 60000, 1.0);
            Assert.That((p.Level, p.Experience, p.LevelsGained), Is.EqualTo((2, 60000L, 2)));
        }

        [TestCase(true, true, true, true)]
        [TestCase(false, true, true, false)]
        [TestCase(true, true, false, false)]
        public void CompanionEarnsArtifactCreditWhenAliveAndNear(bool alive, bool inWorld, bool near, bool expected) =>
            Assert.That(ArtifactEncounters.CompanionEarns(alive, inWorld, near), Is.EqualTo(expected));

        [TestCase(eCraftingSkill.ArmorCrafting, true, true)]   // armor crafter with spellcraft as a secondary trade
        [TestCase(eCraftingSkill.WeaponCrafting, true, true)]
        [TestCase(eCraftingSkill.NoCrafting, true, false)]     // dropped (not crafted) gear
        [TestCase(eCraftingSkill.Tailoring, false, false)]     // no spellcraft or alchemy
        public void SpellcraftButtonLightsForAnySpellcrafter(eCraftingSkill itemSkill, bool knows, bool expected) =>
            Assert.That(DOL.GS.PacketHandler.PacketLib1124.CraftButtonEnabled(itemSkill, knows), Is.EqualTo(expected));

        [TestCase(1, 2, false, true)]   // the next Master Level, as before
        [TestCase(2, 6, false, true)]   // live 1.76: an ML2 player may hold an ML6 step
        [TestCase(5, 5, false, false)]  // a level already reached
        [TestCase(2, 6, true, false)]   // the step is already done
        public void TrialStepsCountForAnyMasterLevelNotYetReached(int ml, int stepMl, bool finished, bool expected) =>
            Assert.That(MasterLevelRules.CanEarnStep(ml, stepMl, finished), Is.EqualTo(expected));

        [TestCase(true, false, true)]
        [TestCase(false, true, true)]   // Ring of Unyielding Will, Crocodile Tear Ring: in water, not flagged
        [TestCase(false, false, false)]
        public void SeaFloorFightsLendBreath(bool flagged, bool belowWater, bool expected) =>
            Assert.That(SeaFloorBreath.FightIsUnderwater(flagged, belowWater), Is.EqualTo(expected));
    }
}
