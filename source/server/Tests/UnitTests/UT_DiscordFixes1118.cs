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
    }
}
