using System.Text;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Launcher;

/// <summary>Parts of a character's interface settings that can be copied, each a set of sections of its .ini file.</summary>
public enum UiPart { Layout, Chat, Quickbars, Macros, Display }

/// <summary>One character's settings file: Name-Server.ini in the client's settings folder.</summary>
public sealed record CharacterUi(string Name, int Server, string Path, DateTime LastPlayed)
{
    public override string ToString() => $"{Name} (server {Server}, played {LastPlayed:d MMM})";
}

/// <summary>
/// UI profiles: the game keeps each character's window layout, chat setup, quickbars, macros and display options in
/// its own .ini. This copies chosen parts from one character, or from a saved profile, to others. Every file it
/// changes is backed up first, sections it was not asked to copy stay as they were, and nothing is written while the
/// game runs (the game rewrites the file when the character logs out). The idea is Eden's layout profiles; no code is taken.
/// </summary>
public static class UiProfiles
{
    public const string FolderName = "ui-profiles";
    private static readonly Regex CharacterFile = new(@"^(?<name>[A-Za-z]+)-(?<server>\d+)\.ini$", RegexOptions.CultureInvariant);

    public static readonly IReadOnlyDictionary<UiPart, string[]> Sections = new Dictionary<UiPart, string[]>
    {
        [UiPart.Layout] = ["Panels"],
        [UiPart.Chat] = ["Chat"],
        [UiPart.Quickbars] = ["Quickbar", "Quickbar2", "Quickbar3"],
        [UiPart.Macros] = ["Macros", "QuickBinds"],
        [UiPart.Display] = ["NameOptions", "Camera"],
    };

    public static string Describe(UiPart part) => part switch
    {
        UiPart.Layout => "Window layout",
        UiPart.Chat => "Chat windows, colours and filters",
        UiPart.Quickbars => "Quickbars (best between characters of one class)",
        UiPart.Macros => "Macros and quick binds",
        _ => "Name plates and camera",
    };

    /// <summary>The game's settings folder for a client: paths.dat's Settings= name, else the given default.</summary>
    public static string SettingsFolder(string clientDirectory, string defaultName)
    {
        string name = defaultName;
        string paths = System.IO.Path.Combine(clientDirectory, "paths.dat");
        if (File.Exists(paths))
        {
            string? line = File.ReadLines(paths).FirstOrDefault(text => text.TrimStart().StartsWith("settings=", StringComparison.OrdinalIgnoreCase));
            string value = line == null ? "" : line[(line.IndexOf('=') + 1)..].Trim();
            if (value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
                name = value;
        }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Electronic Arts", "Dark Age of Camelot", name);
    }

    public static List<CharacterUi> Characters(string settingsFolder)
    {
        if (!Directory.Exists(settingsFolder))
            return new();
        return Directory.EnumerateFiles(settingsFolder, "*.ini")
            .Select(path => (path, match: CharacterFile.Match(System.IO.Path.GetFileName(path))))
            .Where(file => file.match.Success)
            .Select(file => new CharacterUi(file.match.Groups["name"].Value, int.Parse(file.match.Groups["server"].Value), file.path,
                File.GetLastWriteTime(file.path)))
            // The game rewrites a character's file at logout, so the newest file is the one played last.
            .OrderByDescending(character => character.LastPlayed).ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> SavedProfiles(string profileFolder) =>
        Directory.Exists(profileFolder)
            ? Directory.EnumerateFiles(profileFolder, "*.ini").Select(path => System.IO.Path.GetFileNameWithoutExtension(path)!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
            : new();

    /// <summary>Saves the chosen parts of a character's settings as a named profile in profileFolder.</summary>
    public static string SaveProfile(string characterPath, IReadOnlyCollection<UiPart> parts, string profileFolder, string profileName)
    {
        string clean = CleanName(profileName);
        Directory.CreateDirectory(profileFolder);
        Ini source = Ini.Read(characterPath);
        var profile = new Ini();
        foreach (string section in SectionsOf(parts))
        {
            if (source.Get(section) is { } lines)
                profile.Set(section, lines);
        }
        string path = System.IO.Path.Combine(profileFolder, clean + ".ini");
        profile.Write(path);
        return path;
    }

    /// <summary>
    /// Copies the chosen parts from a character's file or a profile into each target. Returns each target's backup.
    /// A part the source does not have is left as the target has it.
    /// </summary>
    public static List<string> Apply(string sourcePath, IReadOnlyCollection<UiPart> parts, IEnumerable<string> targetPaths, string backupFolder,
        DateTime now)
    {
        Ini source = Ini.Read(sourcePath);
        var backups = new List<string>();
        Directory.CreateDirectory(backupFolder);
        foreach (string target in targetPaths)
        {
            if (string.Equals(System.IO.Path.GetFullPath(target), System.IO.Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                continue;
            string backup = System.IO.Path.Combine(backupFolder,
                $"{System.IO.Path.GetFileNameWithoutExtension(target)}-{now:yyyyMMdd-HHmmss}.ini");
            File.Copy(target, backup, true);
            backups.Add(backup);

            Ini ini = Ini.Read(target);
            foreach (string section in SectionsOf(parts))
            {
                if (source.Get(section) is { } lines)
                    ini.Set(section, lines);
            }
            ini.Write(target);
        }
        return backups;
    }

    public static string CleanName(string name)
    {
        string clean = new(name.Trim().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray());
        if (clean.Length == 0)
            throw new ArgumentException("Give the profile a name made of letters, digits, spaces, - or _.");
        return clean.Length > 40 ? clean[..40] : clean;
    }

    private static IEnumerable<string> SectionsOf(IEnumerable<UiPart> parts) => parts.Distinct().SelectMany(part => Sections[part]);

    /// <summary>An .ini kept line for line: text before the first section, then each section's lines in order.</summary>
    private sealed class Ini
    {
        private readonly List<string> _head = new();
        private readonly List<(string Name, List<string> Lines)> _sections = new();

        public static Ini Read(string path)
        {
            var ini = new Ini();
            List<string> current = ini._head;
            // The game writes these in the old Windows codepage; Latin-1 keeps every byte as it was.
            foreach (string line in File.ReadAllLines(path, Encoding.Latin1))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    current = new List<string>();
                    ini._sections.Add((trimmed[1..^1], current));
                }
                else
                    current.Add(line);
            }
            return ini;
        }

        public List<string>? Get(string name) =>
            _sections.FirstOrDefault(section => string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase)).Lines;

        public void Set(string name, List<string> lines)
        {
            int index = _sections.FindIndex(section => string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase));
            var copy = new List<string>(lines);
            if (index >= 0)
                _sections[index] = (_sections[index].Name, copy);
            else
                _sections.Add((name, copy));
        }

        public void Write(string path)
        {
            var text = new StringBuilder();
            foreach (string line in _head)
                text.Append(line).Append("\r\n");
            foreach ((string name, List<string> lines) in _sections)
            {
                text.Append('[').Append(name).Append("]\r\n");
                foreach (string line in lines)
                    text.Append(line).Append("\r\n");
            }
            string temp = path + ".tmp";
            File.WriteAllText(temp, text.ToString(), Encoding.Latin1);
            File.Move(temp, path, true);
        }
    }
}
