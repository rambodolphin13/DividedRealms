using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture]
public class CombatNumbersTests
{
    // The exact line the server writes (camelCase names, enums as numbers).
    [Test] public void ReadsTheServersLine()
    {
        var number = CombatNumberFeed.ParseLine("data: {\"kind\":0,\"who\":1,\"value\":247,\"critical\":true,\"damageType\":2,\"text\":null}");
        Assert.That(number, Is.EqualTo(new CombatNumber(CombatNumberKind.Out, CombatNumberWho.Pet, 247, true, 2, null)));
    }

    [Test] public void SkipsCommentsBlanksAndBrokenLines()
    {
        Assert.That(CombatNumberFeed.ParseLine(": idle"), Is.Null);
        Assert.That(CombatNumberFeed.ParseLine(""), Is.Null);
        Assert.That(CombatNumberFeed.ParseLine(null), Is.Null);
        Assert.That(CombatNumberFeed.ParseLine("data: {not json"), Is.Null);
    }

    [Test] public void StreamsFromTheLoginPortPlusThirtyForThisAccount()
    {
        Assert.That(CombatNumberFeed.StreamUri(10330, "my name").AbsoluteUri, Is.EqualTo("http://127.0.0.1:10360/combat?account=my%20name"));
    }

    [Test] public void LabelsSayWhatHappened()
    {
        Assert.That(FloatingNumbers.Label(new(CombatNumberKind.Out, CombatNumberWho.Self, 612, true, 1, null)), Is.EqualTo("612!"));
        Assert.That(FloatingNumbers.Label(new(CombatNumberKind.In, CombatNumberWho.Self, 189, false, 1, null)), Is.EqualTo("-189"));
        Assert.That(FloatingNumbers.Label(new(CombatNumberKind.HealIn, CombatNumberWho.Self, 250, false, 0, null)), Is.EqualTo("+250"));
        Assert.That(FloatingNumbers.Label(new(CombatNumberKind.AvoidIn, CombatNumberWho.Self, 0, false, 1, "Parry")), Is.EqualTo("Parry"));
    }

    [Test] public void YoursRiseOnTheRightAndTakenOnTheLeftThenFadeAway()
    {
        var numbers = new FloatingNumbers();
        numbers.Add(new(CombatNumberKind.Out, CombatNumberWho.Self, 100, false, 1, null), 0);
        numbers.Add(new(CombatNumberKind.In, CombatNumberWho.Self, 50, false, 1, null), 0);

        var start = numbers.Frame(0);
        Assert.That(start[0].X, Is.GreaterThan(0.5f));
        Assert.That(start[1].X, Is.LessThan(0.5f));
        Assert.That(start.All(n => n.Alpha == 1), Is.True);

        var late = numbers.Frame(FloatingNumbers.LifeMilliseconds - 100);
        Assert.That(late[0].Y, Is.LessThan(start[0].Y));
        Assert.That(late[0].Alpha, Is.LessThan(0.5f));

        Assert.That(numbers.Frame(FloatingNumbers.LifeMilliseconds), Is.Empty);
    }

    [Test] public void ABurstFansOutAndNeverPilesUpWithoutLimit()
    {
        var numbers = new FloatingNumbers();
        for (int i = 0; i < 100; i++)
            numbers.Add(new(CombatNumberKind.Out, CombatNumberWho.Group, i, false, 1, null), 0);
        var frame = numbers.Frame(0);
        Assert.That(frame, Has.Count.EqualTo(FloatingNumbers.MaxOnScreen));
        Assert.That(frame.Select(n => n.X).Distinct().Count(), Is.GreaterThan(1));
    }

    [Test] public void YourOwnNumbersStandOutFromGroupAndPet()
    {
        float yours = FloatingNumbers.SizeOf(new(CombatNumberKind.Out, CombatNumberWho.Self, 1, false, 1, null));
        Assert.That(FloatingNumbers.SizeOf(new(CombatNumberKind.Out, CombatNumberWho.Group, 1, false, 1, null)), Is.LessThan(yours));
        Assert.That(FloatingNumbers.SizeOf(new(CombatNumberKind.Out, CombatNumberWho.Pet, 1, false, 1, null)), Is.LessThan(yours));
        Assert.That(FloatingNumbers.SizeOf(new(CombatNumberKind.Out, CombatNumberWho.Self, 1, true, 1, null)), Is.GreaterThan(yours));
    }
}
