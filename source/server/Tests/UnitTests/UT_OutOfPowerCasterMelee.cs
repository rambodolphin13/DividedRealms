using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Casters with no power for any attack spell hold back (no staff melee) until they can cast.</summary>
    [TestFixture]
    public class UT_OutOfPowerCasterMelee
    {
        [Test]
        public void OutOfPowerMeansNoAttackSpellIsAffordable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BrainOutOfPower(new[] { 20, 35 }, 10), Is.True, "nothing affordable");
                Assert.That(BrainOutOfPower(new[] { 20, 35 }, 20), Is.False, "the cheap nuke is affordable");
                Assert.That(BrainOutOfPower(new[] { 0, 35 }, 0), Is.False, "a free spell can still be cast");
                Assert.That(BrainOutOfPower(new int[0], 0), Is.False, "no attack spells known");
            });
        }

        [Test]
        public void DruidsAndPureCastersPreferSpellRange()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotBrain.PrefersSpellRange(new DOL.GS.PlayerClass.ClassDruid()), Is.True);
                Assert.That(BotBrain.PrefersSpellRange(new DOL.GS.PlayerClass.ClassAnimist()), Is.True);
                Assert.That(BotBrain.PrefersSpellRange(new DOL.GS.PlayerClass.ClassEnchanter()), Is.True);
                Assert.That(BotBrain.PrefersSpellRange(new DOL.GS.PlayerClass.ClassShaman()), Is.False, "Shamans melee between nukes");
            });
        }

        private static bool BrainOutOfPower(int[] costs, int mana) => BotBrain.OutOfPower(costs, mana);
    }
}
