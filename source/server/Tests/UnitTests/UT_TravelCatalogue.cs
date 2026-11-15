using DOL.GS.Scripts;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_TravelCatalogue
{
    [TestCase(10, "Cities")] [TestCase(101, "Cities")] [TestCase(201, "Cities")]
    [TestCase(1, "Albion")] [TestCase(100, "Midgard")] [TestCase(200, "Hibernia")]
    [TestCase(51, "Shrouded Isles")] [TestCase(151, "Shrouded Isles")] [TestCase(181, "Shrouded Isles")]
    [TestCase(73, "Atlantis")] [TestCase(30, "Atlantis")] [TestCase(130, "Atlantis")] [TestCase(147, "Atlantis")]
    [TestCase(65, "Catacombs")] [TestCase(243, "Catacombs")] [TestCase(93, "Catacombs")] [TestCase(339, "Catacombs")]
    [TestCase(163, "New Frontiers")] [TestCase(233, "New Frontiers")] [TestCase(244, "New Frontiers")] [TestCase(245, "New Frontiers")]
    [TestCase(249, "Darkness Falls")]
    [TestCase(248, "Dungeons")] [TestCase(277, "Dungeons")] [TestCase(191, "Dungeons")] [TestCase(24, "Dungeons")]
    public void PlacesAreListedUnderTheirLand(int region, string family) =>
        Assert.That(TravelCatalogue.FamilyOf((ushort)region), Is.EqualTo(family));
}
