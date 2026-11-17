using NUnit.Framework;

namespace DOL.GS.Tests
{
    // DR-453: a destination said to a porter with /say reads the same as the clicked [link].
    [TestFixture]
    public class UT_PorterSay
    {
        [TestCase("Camelot", "Camelot")]
        [TestCase("[Camelot]", "Camelot")]
        [TestCase("  shrouded isles. ", "shrouded isles")]
        [TestCase("Tir na Nog!", "Tir na Nog")]
        public void SaidWordIsTheDestination(string said, string expected) =>
            Assert.That(PorterSay.Destination(said), Is.EqualTo(expected));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("[]")]
        public void EmptyLineIsNoDestination(string said) =>
            Assert.That(PorterSay.Destination(said), Is.Null);
    }
}
