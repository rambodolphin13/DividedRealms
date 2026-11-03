using System.Globalization;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

public enum OverlaySettingKind { Toggle, Slider, Color }

/// <summary>One thing a player can change about the overlay. Values are stored as text: True/False, a number, or #RRGGBB.</summary>
public sealed record OverlaySetting(string Key, OverlaySettingKind Kind, string Group, string Label, string Default,
    float Min = 0, float Max = 1, float Step = 0.01f);

/// <summary>
/// The overlay's settings: every one is declared once in Schema, the Overlay tab builds its controls from that list,
/// and only values that differ from the default are saved, in overlay.json beside the launcher's account.txt.
/// The pattern follows Eden's typed overlay settings; no code is taken.
/// </summary>
public sealed class OverlaySettings
{
    public const string FileName = "overlay.json";

    public static readonly IReadOnlyList<OverlaySetting> Schema =
    [
        new("numbers.enabled", OverlaySettingKind.Toggle, "Floating numbers", "Show floating numbers", "True"),
        new("numbers.heals", OverlaySettingKind.Toggle, "Floating numbers", "Show heals", "True"),
        new("numbers.avoided", OverlaySettingKind.Toggle, "Floating numbers", "Show Miss, Block, Parry, Evade and Resist", "True"),
        new("numbers.pet", OverlaySettingKind.Toggle, "Floating numbers", "Show your pets' numbers", "True"),
        new("numbers.group", OverlaySettingKind.Toggle, "Floating numbers", "Show your group's numbers", "True"),
        new("numbers.size", OverlaySettingKind.Slider, "Floating numbers", "Size", "1", 0.5f, 2f, 0.05f),
        new("numbers.x", OverlaySettingKind.Slider, "Floating numbers", "Across (centre)", "0.5", 0.2f, 0.8f),
        new("numbers.y", OverlaySettingKind.Slider, "Floating numbers", "Down (start)", "0.45", 0.15f, 0.85f),
        new("color.yours", OverlaySettingKind.Color, "Number colours", "Your hits", "#FFFFFF"),
        new("color.crit", OverlaySettingKind.Color, "Number colours", "Your critical hits", "#FFEB50"),
        new("color.pet", OverlaySettingKind.Color, "Number colours", "Pet hits", "#FFAA5A"),
        new("color.group", OverlaySettingKind.Color, "Number colours", "Group hits", "#AFB9D2"),
        new("color.taken", OverlaySettingKind.Color, "Number colours", "Damage taken", "#FF5046"),
        new("color.heal", OverlaySettingKind.Color, "Number colours", "Heals", "#5AEB6E"),
        new("color.avoidIn", OverlaySettingKind.Color, "Number colours", "Attacks you avoided", "#82BEFF"),
        new("color.avoidOut", OverlaySettingKind.Color, "Number colours", "Your misses", "#BEBEBE"),
        new("card.enabled", OverlaySettingKind.Toggle, "Fight card", "Show a card after each fight", "True"),
        new("card.seconds", OverlaySettingKind.Slider, "Fight card", "Seconds on screen", "15", 5, 60, 1),
        new("card.size", OverlaySettingKind.Slider, "Fight card", "Size", "1", 0.6f, 2f, 0.05f),
        new("card.x", OverlaySettingKind.Slider, "Fight card", "Across (right edge)", "0.98", 0.3f, 1f),
        new("card.y", OverlaySettingKind.Slider, "Fight card", "Down (top edge)", "0.22", 0f, 0.8f),
        new("panels.self", OverlaySettingKind.Toggle, "Effect panels", "Show effects on you", "True"),
        new("panels.pets", OverlaySettingKind.Toggle, "Effect panels", "Show your pets", "True"),
        new("panels.target", OverlaySettingKind.Toggle, "Effect panels", "Show your target", "True"),
        new("panels.size", OverlaySettingKind.Slider, "Effect panels", "Size", "1", 0.6f, 2f, 0.05f),
        new("panels.opacity", OverlaySettingKind.Slider, "Effect panels", "Background opacity", "0.73", 0.2f, 1f),
        new("panels.x", OverlaySettingKind.Slider, "Effect panels", "Across (left edge)", "0.015", 0f, 0.7f, 0.005f),
        new("panels.y", OverlaySettingKind.Slider, "Effect panels", "Down (top edge)", "0.2", 0f, 0.8f),
    ];

    public static OverlaySettings Defaults { get; } = new();

    private static readonly Dictionary<string, OverlaySetting> ByKey = Schema.ToDictionary(setting => setting.Key);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string this[string key]
    {
        get => _values.TryGetValue(key, out string? value) ? value : ByKey[key].Default;
        set
        {
            OverlaySetting setting = ByKey[key];
            string clean = Clean(setting, value);
            if (clean == setting.Default)
                _values.Remove(key);
            else
                _values[key] = clean;
        }
    }

    public bool On(string key) => this[key] == "True";

    public float Number(string key) => float.Parse(this[key], CultureInfo.InvariantCulture);

    public Color Color(string key) => ColorTranslator.FromHtml(this[key]);

    /// <summary>The values that differ from the defaults, as saved.</summary>
    public IReadOnlyDictionary<string, string> Changed => _values;

    public OverlaySettings Copy()
    {
        var copy = new OverlaySettings();
        foreach (var (key, value) in _values)
            copy._values[key] = value;
        return copy;
    }

    /// <summary>Reads the file; a missing or broken file, or an unknown or bad value, falls back to the default.</summary>
    public static OverlaySettings Load(string path)
    {
        var settings = new OverlaySettings();
        try
        {
            if (!File.Exists(path))
                return settings;
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            foreach (var (key, value) in values)
            {
                if (ByKey.ContainsKey(key))
                    settings[key] = value;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return settings;
    }

    public void Save(string path)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_values.OrderBy(pair => pair.Key).ToDictionary(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }

    private static string Clean(OverlaySetting setting, string? value)
    {
        value = value?.Trim() ?? "";
        switch (setting.Kind)
        {
            case OverlaySettingKind.Toggle:
                return bool.TryParse(value, out bool on) ? (on ? "True" : "False") : setting.Default;
            case OverlaySettingKind.Slider:
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) || !float.IsFinite(number))
                    return setting.Default;
                return Math.Clamp(number, setting.Min, setting.Max).ToString("0.###", CultureInfo.InvariantCulture);
            default:
                return value.Length == 7 && value[0] == '#' && int.TryParse(value[1..], NumberStyles.HexNumber, null, out _)
                    ? value.ToUpperInvariant() : setting.Default;
        }
    }
}
