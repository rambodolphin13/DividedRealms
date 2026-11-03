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
