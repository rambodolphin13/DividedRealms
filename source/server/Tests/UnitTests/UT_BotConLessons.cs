using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotConLessons
    {
        private static RouteThreatAction Solo(ConColor con, int pack, bool highLevel = false, bool dense = false, bool mayRetarget = true) =>
            AutonomousRouteThreatPolicy.Decide(con, false, pack, false, false, 0, mayRetarget, highLevelSolo: highLevel, denseWorld: dense);

        private static RouteThreatAction Party(ConColor con, int pack, bool strong, int above = 5, bool detour = false) =>
            AutonomousRouteThreatPolicy.Decide(con, true, pack, false, detour, 0, true, levelsAboveReference: above, strongParty: strong);

        [Test]
        public void AHighLevelSoloBotPullsASingleOrangeInsteadOfGivingUp()
        {
            Assert.That(Solo(ConColor.ORANGE, 1), Is.EqualTo(RouteThreatAction.Retarget), "below level 45 the old rule stands");
            Assert.That(Solo(ConColor.ORANGE, 1, highLevel: true), Is.EqualTo(RouteThreatAction.Pull));
            Assert.That(Solo(ConColor.ORANGE, 2, highLevel: true), Is.EqualTo(RouteThreatAction.Retarget), "not a pair");
            Assert.That(Solo(ConColor.RED, 1, highLevel: true), Is.EqualTo(RouteThreatAction.Retarget), "red stays off limits alone");
        }

        [Test]
        public void ASoloBotFightsAYellowPairItCannotGoAround()
        {
            Assert.That(Solo(ConColor.YELLOW, 2), Is.EqualTo(RouteThreatAction.Pull));
            Assert.That(Solo(ConColor.YELLOW, 3), Is.EqualTo(RouteThreatAction.Retarget), "a yellow pack is still a pack");
            Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.YELLOW, false, 2, false, true, 0, true), Is.EqualTo(RouteThreatAction.Detour), "around it if it can");
        }

        [Test]
        public void PacksCountForMoreInAtlantis()
        {
            Assert.That(Solo(ConColor.BLUE, 2), Is.EqualTo(RouteThreatAction.Pull));
            Assert.That(Solo(ConColor.BLUE, 3, dense: true, mayRetarget: true), Is.EqualTo(RouteThreatAction.Retarget));
            Assert.That(Solo(ConColor.BLUE, 1, dense: true), Is.EqualTo(RouteThreatAction.Pull));
        }

        [Test]
        public void AStrongPartyTreatsRedAsNormal()
        {
            Assert.That(Party(ConColor.RED, 2, strong: true, detour: true), Is.EqualTo(RouteThreatAction.Pull), "pulled, not walked around");
            Assert.That(Party(ConColor.RED, 2, strong: false, detour: true), Is.EqualTo(RouteThreatAction.Detour));
            Assert.That(Party(ConColor.RED, 3, strong: true, detour: true), Is.EqualTo(RouteThreatAction.Detour), "a red pack is still a pack");
        }

        [Test]
        public void OnlyASingleFarPurpleIsPulled()
        {
            Assert.That(Party(ConColor.PURPLE, 1, strong: true, above: 11), Is.EqualTo(RouteThreatAction.Pull));
            Assert.That(Party(ConColor.PURPLE, 2, strong: true, above: 11), Is.EqualTo(RouteThreatAction.Retarget), "a purple camp wiped a party");
            Assert.That(Party(ConColor.PURPLE, 1, strong: true, above: 15), Is.EqualTo(RouteThreatAction.Retarget), "too far above");
            Assert.That(Party(ConColor.PURPLE, 1, strong: false, above: 11), Is.EqualTo(RouteThreatAction.Retarget));
        }

        [Test]
        public void AStrongPartyHasAHealerAndFourAtLevel()
        {
            Assert.That(AutonomousRouteThreatPolicy.IsStrongParty(4, true, 45), Is.True);
            Assert.That(AutonomousRouteThreatPolicy.IsStrongParty(3, true, 50), Is.False);
            Assert.That(AutonomousRouteThreatPolicy.IsStrongParty(5, false, 50), Is.False);
            Assert.That(AutonomousRouteThreatPolicy.IsStrongParty(5, true, 44), Is.False);
        }

        [Test]
        public void APartyBreaksOffOnlyFromALostFight()
        {
            Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(5, 5, true, true, 100, ConColor.RED), Is.False);
            Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(5, 3, true, true, 100, ConColor.RED), Is.False, "two down of five, healer up");
            Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(5, 2, true, true, 90, ConColor.BLUE), Is.True, "more than half down");
            Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(5, 4, true, false, 90, ConColor.ORANGE), Is.True, "the healer fell to something hard");
            Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(7, 6, true, false, 90, ConColor.YELLOW), Is.False, "an even monster is finished without a healer");
            Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(5, 2, true, false, 20, ConColor.RED), Is.False, "nearly won: finish it");
            Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(2, 1, true, false, 90, ConColor.RED), Is.False, "too small to run as a party");
        }

        [Test]
        public void TooFewFightsTeachNothing()
        {
            Assert.That(AutonomousMobLessons.Felt(ConColor.RED, 1, 10, false), Is.EqualTo(ConColor.RED));
            Assert.That(AutonomousMobLessons.Felt(ConColor.RED, 11, 0, true), Is.EqualTo(ConColor.RED));
        }

        [Test]
        public void AMonsterBotsKeepBeatingFeelsEasier()
        {
            Assert.That(AutonomousMobLessons.Felt(ConColor.RED, 19, 1, true), Is.EqualTo(ConColor.ORANGE));
            Assert.That(AutonomousMobLessons.Felt(ConColor.ORANGE, 19, 1, false), Is.EqualTo(ConColor.YELLOW));
            Assert.That(AutonomousMobLessons.Felt(ConColor.YELLOW, 40, 0, false), Is.EqualTo(ConColor.YELLOW), "never below yellow");
        }

        [Test]
        public void AMonsterThatKeepsKillingFeelsHarder()
        {
            Assert.That(AutonomousMobLessons.Felt(ConColor.BLUE, 7, 8, false), Is.EqualTo(ConColor.YELLOW));
            Assert.That(AutonomousMobLessons.Felt(ConColor.PURPLE, 1, 20, true), Is.EqualTo(ConColor.PURPLE));
        }

        [Test]
        public void ALoneBotNeverLearnsAPurpleIsBelowRed()
        {
            Assert.That(AutonomousMobLessons.Felt(ConColor.PURPLE, 30, 0, false), Is.EqualTo(ConColor.RED));
            Assert.That(AutonomousMobLessons.Felt(ConColor.PURPLE, 30, 0, true), Is.EqualTo(ConColor.RED));
        }

        [Test]
        public void ShapePotionsStayInTownsAndHousing()
        {
            foreach (ushort region in new ushort[] { 10, 101, 201, 2, 102, 202 })
                Assert.That(DOL.GS.Spells.CostumeSpellHandlerRules.AllowedIn(region), Is.True, $"region {region}");
            Assert.That(DOL.GS.Spells.CostumeSpellHandlerRules.AllowedIn(1), Is.False);
            Assert.That(DOL.GS.Spells.CostumeSpellHandlerRules.AllowedIn(73), Is.False);
        }

        [Test]
        public void SoloAndGroupLessonsAreKeptApart()
        {
            Assert.That(AutonomousMobLessons.Key("cyclops", true), Is.Not.EqualTo(AutonomousMobLessons.Key("cyclops", false)));
        }
    }
}
