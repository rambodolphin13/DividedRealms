using System.Drawing;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

// The server's floating combat numbers (GameServer gameutils/CombatNumbers.cs): what kind, whose, and how much.
public enum CombatNumberKind { Out, In, HealOut, HealIn, AvoidOut, AvoidIn }
public enum CombatNumberWho { Self, Pet, Group }

public sealed record CombatNumber(CombatNumberKind Kind, CombatNumberWho Who, int Value, bool Critical, int DamageType, string? Text);

/// <summary>Reads the server's event stream: one "data: {json}" line per number, comments and blank lines between.</summary>
public static class CombatNumberFeed
{
    // The server streams at the login port plus this; the Passport Desk has +10 and Bot Watch +20.
    public const int PortOffset = 30;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static Uri StreamUri(int loginPort, string account) =>
        new($"http://127.0.0.1:{loginPort + PortOffset}/combat?account={Uri.EscapeDataString(account)}");

    public static CombatNumber? ParseLine(string? line)
    {
        if (line == null || !line.StartsWith("data:", StringComparison.Ordinal))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CombatNumber>(line.AsSpan(5).Trim(), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A number on screen: its words, look and where it is in its rise. Positions are fractions of the game's client area.</summary>
public sealed record FloatingNumber(string Label, Color Color, float Size, float X, float Y, float Alpha);

/// <summary>
/// The numbers in flight. Yours (and your pets' and group's) rise right of the character, what is done to you rises
/// on the left. Each number rises for a second and a half and fades over its last part; a burst fans out sideways
/// instead of stacking.
/// </summary>
public sealed class FloatingNumbers
{
    public const int LifeMilliseconds = 1500;
    public const int MaxOnScreen = 40;
    private const float Rise = 0.16f;
    private readonly List<(CombatNumber Number, long Born, float Lane)> _live = new();
    private readonly int[] _nextLane = new int[2];

    public int Count => _live.Count;

    public void Add(CombatNumber number, long nowMilliseconds)
    {
        int side = IsYours(number.Kind) ? 0 : 1;
        float lane = _nextLane[side]++ % 4 * 0.035f;
        _live.Add((number, nowMilliseconds, lane));
        if (_live.Count > MaxOnScreen)
            _live.RemoveAt(0);
    }

    public IReadOnlyList<FloatingNumber> Frame(long nowMilliseconds)
    {
        _live.RemoveAll(entry => nowMilliseconds - entry.Born >= LifeMilliseconds);
        if (_live.Count == 0)
            Array.Clear(_nextLane);
        var frame = new List<FloatingNumber>(_live.Count);
        foreach (var (number, born, lane) in _live)
        {
            float age = Math.Clamp((nowMilliseconds - born) / (float)LifeMilliseconds, 0, 1);
            float alpha = age < 0.6f ? 1 : 1 - (age - 0.6f) / 0.4f;
            bool yours = IsYours(number.Kind);
            float x = yours ? 0.56f + lane : 0.44f - lane;
            float y = 0.45f - Rise * age;
            frame.Add(new FloatingNumber(Label(number), ColorOf(number), SizeOf(number), x, y, alpha));
        }
        return frame;
    }

    public static bool IsYours(CombatNumberKind kind) =>
        kind is CombatNumberKind.Out or CombatNumberKind.HealOut or CombatNumberKind.AvoidOut;

    public static string Label(CombatNumber number) => number.Kind switch
    {
        CombatNumberKind.AvoidOut or CombatNumberKind.AvoidIn => number.Text ?? "Miss",
        CombatNumberKind.HealOut or CombatNumberKind.HealIn => "+" + number.Value,
        CombatNumberKind.In => "-" + number.Value,
        _ => number.Critical ? number.Value + "!" : number.Value.ToString(),
    };

    public static Color ColorOf(CombatNumber number) => (number.Kind, number.Who) switch
    {
        (CombatNumberKind.In, _) => Color.FromArgb(255, 80, 70),
        (CombatNumberKind.HealOut or CombatNumberKind.HealIn, CombatNumberWho.Group) => Color.FromArgb(150, 210, 150),
        (CombatNumberKind.HealOut or CombatNumberKind.HealIn, _) => Color.FromArgb(90, 235, 110),
        (CombatNumberKind.AvoidIn, _) => Color.FromArgb(130, 190, 255),
        (CombatNumberKind.AvoidOut, _) => Color.FromArgb(190, 190, 190),
        (_, CombatNumberWho.Pet) => Color.FromArgb(255, 170, 90),
        (_, CombatNumberWho.Group) => Color.FromArgb(175, 185, 210),
        _ => number.Critical ? Color.FromArgb(255, 235, 80) : Color.FromArgb(255, 255, 255),
    };

    // Relative to the normal size: crits bigger, group and pet numbers smaller so your own stand out.
    public static float SizeOf(CombatNumber number) =>
        (number.Critical ? 1.4f : 1f) * number.Who switch
        {
            CombatNumberWho.Group => 0.75f,
            CombatNumberWho.Pet => 0.85f,
            _ => 1f,
        };
}

// The server's fight report (GameServer gameutils/FightReports.cs), sent as an "event: fight" when a fight goes quiet.
public sealed record FightRow(string Name, string? Class, int Level, string Kind, int Count, long Done, long Taken,
    long Healed, long HealedTaken, int Kills, int Deaths);
public sealed record FightReport(int Seconds, FightRow[] Allies, FightRow[] Enemies);

/// <summary>Reads the stream line by line: a CombatNumber for each plain data line, a FightReport after "event: fight",
/// an EffectSnapshot after "event: effects".</summary>
public sealed class CombatFeedReader
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private string? _event;

    public object? Read(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            _event = null;
            return null;
        }
        if (line.StartsWith("event:", StringComparison.Ordinal))
        {
            _event = line[6..].Trim();
            return null;
        }
        if (_event is not ("fight" or "effects"))
            return CombatNumberFeed.ParseLine(line);
        if (!line.StartsWith("data:", StringComparison.Ordinal))
            return null;
        try
        {
            return _event == "fight"
                ? JsonSerializer.Deserialize<FightReport>(line.AsSpan(5).Trim(), Json)
                : JsonSerializer.Deserialize<EffectSnapshot>(line.AsSpan(5).Trim(), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What the fight card shows: a title, your side, then the enemies, each row cut to fit.</summary>
public static class FightCard
{
    public const int ShowMilliseconds = 15000;
    public const int FadeMilliseconds = 1500;
    public const int MaxAllies = 8;
    public const int MaxEnemies = 5;

    public sealed record Line(string Name, string Detail, string Done, string Taken, string Healed, string KillsDeaths, bool Enemy);

    public static string Title(FightReport report) =>
        $"Fight  {(report.Seconds >= 60 ? $"{report.Seconds / 60}m {report.Seconds % 60:00}s" : $"{report.Seconds}s")}";

    public static List<Line> Lines(FightReport report)
    {
        var lines = new List<Line>();
        foreach (FightRow row in report.Allies.Take(MaxAllies))
            lines.Add(new Line(row.Name, row.Kind == "pet" ? "pet" : row.Class ?? "", Compact(row.Done), Compact(row.Taken),
                Compact(row.Healed), $"{row.Kills}/{row.Deaths}", false));
        if (report.Allies.Length > MaxAllies)
            lines.Add(new Line($"+{report.Allies.Length - MaxAllies} more", "", "", "", "", "", false));
        foreach (FightRow row in report.Enemies.Take(MaxEnemies))
            lines.Add(new Line(row.Count > 1 ? $"{row.Count}× {row.Name}" : row.Name, row.Level > 0 ? $"level {row.Level}" : "",
                Compact(row.Done), Compact(row.Taken), Compact(row.Healed), $"{row.Kills}/{row.Deaths}", true));
        if (report.Enemies.Length > MaxEnemies)
            lines.Add(new Line($"+{report.Enemies.Length - MaxEnemies} more", "", "", "", "", "", true));
        return lines;
    }

    /// <summary>1-3 digits as they are, then 1.2k, 12k, 1.2M.</summary>
    public static string Compact(long value) => value switch
    {
        0 => "-",
        < 1000 => value.ToString(),
        < 10_000 => (value / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "k",
        < 1_000_000 => (value / 1000) + "k",
        _ => (value / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "M",
    };

    /// <summary>Opacity at this age: full, then a fade, then gone (0).</summary>
    public static float Alpha(long ageMilliseconds) =>
        ageMilliseconds >= ShowMilliseconds ? 0
        : ageMilliseconds <= ShowMilliseconds - FadeMilliseconds ? 1
        : (ShowMilliseconds - ageMilliseconds) / (float)FadeMilliseconds;
}

// The server's buff, debuff and pet windows (GameServer gameutils/EffectWindows.cs), sent once a second as "event: effects".
public sealed record EffectInfo(string Name, int Icon, int Seconds, int Total, bool Good, bool Mine);
public sealed record EffectUnit(string Name, int Level, int Health, EffectInfo[] Effects);
public sealed record EffectSnapshot(EffectInfo[] Self, EffectUnit[] Pets, EffectUnit? Target);

/// <summary>What the effect panels show: you, each pet, then your target, each with its effects cut to fit.</summary>
public static class EffectPanels
{
    public const int StaleMilliseconds = 3000;
    public const int MaxSelf = 10;
    public const int MaxPet = 6;
    public const int MaxTarget = 8;

    public sealed record Section(string Title, int? Health, IReadOnlyList<EffectInfo> Effects, int Hidden);

    public static List<Section> Sections(EffectSnapshot snapshot)
    {
        var sections = new List<Section>();
        if (snapshot.Self.Length > 0)
            sections.Add(Cut("You", null, snapshot.Self, MaxSelf));
        foreach (EffectUnit pet in snapshot.Pets)
            sections.Add(Cut($"{pet.Name}  {pet.Level}", pet.Health, pet.Effects, MaxPet));
        if (snapshot.Target is { } target)
            sections.Add(Cut($"{target.Name}  {target.Level}", target.Health, target.Effects, MaxTarget));
        return sections;
    }

    private static Section Cut(string title, int? health, EffectInfo[] effects, int max) =>
        new(title, health, effects.Take(max).ToList(), Math.Max(0, effects.Length - max));

    /// <summary>"" for an endless effect, then 45s, 1:05, 1h 02.</summary>
    public static string Time(int seconds) => seconds switch
    {
        <= 0 => "",
        < 60 => seconds + "s",
        < 3600 => $"{seconds / 60}:{seconds % 60:00}",
        _ => $"{seconds / 3600}h {seconds % 3600 / 60:00}",
    };

    /// <summary>How much of the bar is left, 0 to 1; a full bar for an endless effect.</summary>
    public static float Left(EffectInfo effect) =>
        effect.Total <= 0 ? 1 : Math.Clamp(effect.Seconds / (float)effect.Total, 0, 1);
}
