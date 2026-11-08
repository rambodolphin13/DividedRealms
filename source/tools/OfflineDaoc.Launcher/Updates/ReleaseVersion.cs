using System.Globalization;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

// A Divided Realms release as its tag names it: "DR-dev-0.1", "DR-dev-0.1a", "DR-dev-0.1b", ... "DR-dev-0.2".
// Ported from Living Server's BuildVersion (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3) with our own order:
// - the numbers compare as numbers, missing ones are 0: DR-dev-0.1 < DR-dev-0.2 < DR-dev-0.10 < DR-dev-1.0;
// - a letter after the numbers is a later build of the same numbers: DR-dev-0.1 < DR-dev-0.1a < DR-dev-0.1b < DR-dev-0.1z;
// - the "DR-dev-" prefix is required (any case); anything else ("v0.38-dev8-modern-test", "world-workshop-v1.0.0",
//   "DR-dev-0.1b-hotfix") is not a release this reads, and Parse returns null.
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex Shape = new(@"^DR-dev-(\d{1,6}(?:\.\d{1,6}){0,3})([a-z])?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly int[] _numbers;

    private ReleaseVersion(int[] numbers, char letter, string tag)
    {
        _numbers = numbers;
        Letter = letter;
        Tag = tag;
    }

    /// <summary>The tag as the releases spell it: "DR-dev-0.1b".</summary>
    public string Tag { get; }

    /// <summary>The build letter after the numbers ('\0' for none).</summary>
    public char Letter { get; }

    /// <summary>The release a tag (or a version stamp) names, or null when it isn't one.</summary>
    public static ReleaseVersion? Parse(string? text)
    {
        if (text == null)
            return null;
        Match m = Shape.Match(text.Trim());
        if (!m.Success)
            return null;
        int[] numbers = m.Groups[1].Value.Split('.').Select(n => int.Parse(n, CultureInfo.InvariantCulture)).ToArray();
        char letter = m.Groups[2].Success ? char.ToLowerInvariant(m.Groups[2].Value[0]) : '\0';
        string tag = "DR-dev-" + m.Groups[1].Value + (letter == '\0' ? "" : letter.ToString());
        return new ReleaseVersion(numbers, letter, tag);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other == null)
            return 1;
        int length = Math.Max(_numbers.Length, other._numbers.Length);
        for (int i = 0; i < length; i++)
        {
            int mine = i < _numbers.Length ? _numbers[i] : 0;
            int theirs = i < other._numbers.Length ? other._numbers[i] : 0;
            if (mine != theirs)
                return mine.CompareTo(theirs);
        }
        return Letter.CompareTo(other.Letter);
    }

    public bool IsNewerThan(ReleaseVersion? other) => CompareTo(other) > 0;

    public bool SameAs(ReleaseVersion? other) => other != null && CompareTo(other) == 0;

    public override string ToString() => Tag;
}
