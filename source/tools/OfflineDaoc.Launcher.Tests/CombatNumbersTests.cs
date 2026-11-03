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

[TestFixture]
public class FightCardTests
{
    private const string FightLine =
        "data: {\"seconds\":38,\"allies\":[{\"name\":\"Cnwarrior\",\"class\":\"Warrior\",\"level\":20,\"kind\":\"player\",\"count\":1,\"done\":1240,\"taken\":96,\"healed\":0,\"healedTaken\":0,\"kills\":2,\"deaths\":0}]," +
        "\"enemies\":[{\"name\":\"soft-shelled crab\",\"class\":null,\"level\":12,\"kind\":\"npc\",\"count\":2,\"done\":96,\"taken\":1240,\"healed\":0,\"healedTaken\":0,\"kills\":0,\"deaths\":2}]}";

    [Test] public void ReadsAFightEventAndStillReadsNumbersAfterIt()
    {
        var reader = new CombatFeedReader();
        Assert.That(reader.Read("event: fight"), Is.Null);
        var report = reader.Read(FightLine) as FightReport;
        Assert.That(reader.Read(""), Is.Null);
        var number = reader.Read("data: {\"kind\":1,\"who\":0,\"value\":9,\"critical\":false,\"damageType\":1,\"text\":null}");

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Seconds, Is.EqualTo(38));
        Assert.That(report.Allies[0].Done, Is.EqualTo(1240));
        Assert.That(report.Enemies[0].Count, Is.EqualTo(2));
        Assert.That(number, Is.EqualTo(new CombatNumber(CombatNumberKind.In, CombatNumberWho.Self, 9, false, 1, null)));
    }

    [Test] public void TheCardListsYourSideThenTheEnemies()
    {
        var report = (FightReport)new CombatFeedReader().Let(r => { r.Read("event: fight"); return r.Read(FightLine)!; });
        var lines = FightCard.Lines(report);

        Assert.That(FightCard.Title(report), Is.EqualTo("Fight  38s"));
        Assert.That(lines[0], Is.EqualTo(new FightCard.Line("Cnwarrior", "Warrior", "1.2k", "96", "-", "2/0", false)));
        Assert.That(lines[1], Is.EqualTo(new FightCard.Line("2× soft-shelled crab", "level 12", "96", "1.2k", "-", "0/2", true)));
    }

    [Test] public void LongListsAreCut()
    {
        FightRow Row(int i) => new($"Bot{i}", "Cleric", 50, "companion", 1, i, 0, 0, 0, 0, 0);
        var report = new FightReport(75, Enumerable.Range(0, 12).Select(Row).ToArray(), Array.Empty<FightRow>());

        var lines = FightCard.Lines(report);

        Assert.That(lines, Has.Count.EqualTo(FightCard.MaxAllies + 1));
        Assert.That(lines[^1].Name, Is.EqualTo("+4 more"));
        Assert.That(FightCard.Title(report), Is.EqualTo("Fight  1m 15s"));
    }

    [Test] public void NumbersAreShortAndTheCardFadesThenGoes()
    {
        Assert.That(FightCard.Compact(0), Is.EqualTo("-"));
        Assert.That(FightCard.Compact(999), Is.EqualTo("999"));
        Assert.That(FightCard.Compact(12_400), Is.EqualTo("12k"));
        Assert.That(FightCard.Compact(2_500_000), Is.EqualTo("2.5M"));
        Assert.That(FightCard.Alpha(1000), Is.EqualTo(1));
        Assert.That(FightCard.Alpha(FightCard.ShowMilliseconds - FightCard.FadeMilliseconds / 2), Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(FightCard.Alpha(FightCard.ShowMilliseconds), Is.Zero);
    }
}

internal static class TestLet
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> use) => use(value);
}
