namespace OfflineDaoc.Launcher;

/// <summary>
/// The UI Profiles tab: copy window layout, chat setup, quickbars, macros and display options from one character (or a
/// saved profile) to others, or save them as a named profile. UiProfiles does the copying and backs up every file.
/// </summary>
internal sealed class UiProfilesControl : UserControl
{
    private const string ProfilePrefix = "Profile: ";
    private readonly string _settingsRoot;
    private readonly string _profileFolder;
    private readonly Func<bool> _gameRunning;
    private readonly ComboBox _folder = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Settings folder" };
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Copy from" };
    private readonly CheckedListBox _targets = new() { Width = 300, Height = 260, CheckOnClick = true, AccessibleName = "Copy to",
        BackColor = DaocTheme.PanelAlt, ForeColor = DaocTheme.Text, BorderStyle = BorderStyle.FixedSingle };
    private readonly Dictionary<UiPart, CheckBox> _parts = new();
    private readonly TextBox _profileName = new() { Width = 200, AccessibleName = "Profile name" };
    private readonly Button _copy = new() { Text = "Copy to checked characters", AutoSize = true };
    private readonly Button _saveProfile = new() { Text = "Save as profile", AutoSize = true };
    private readonly Button _openBackups = new() { Text = "Open backups", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = DaocTheme.Muted };
    private List<CharacterUi> _characters = new();

    /// <param name="settingsFolder">This install's game settings folder (where Name-Server.ini files are).</param>
    /// <param name="profileFolder">Where saved profiles and backups go.</param>
    public UiProfilesControl(string settingsFolder, string profileFolder, Func<bool> gameRunning)
    {
        _settingsRoot = Path.GetDirectoryName(settingsFolder)!;
        _profileFolder = profileFolder;
        _gameRunning = gameRunning;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        AutoScroll = true;

        var body = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        body.Controls.Add(new Label { Text = "UI Profiles", AutoSize = true, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = DaocTheme.GoldLight });
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(820, 0), Margin = new Padding(3, 8, 3, 12),
            Text = "Copy one character's window layout, chat setup, quickbars, macros or display options to other characters, or keep them " +
                "as a profile. The game saves a character's settings when it logs out, so close the game first. Every changed file is backed up." });

        body.Controls.Add(Row("Characters in", _folder));
        body.Controls.Add(Row("Copy from", _source));
        body.Controls.Add(new Label { Text = "What to copy", AutoSize = true, ForeColor = DaocTheme.Gold, Margin = new Padding(3, 10, 3, 4),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold) });
        foreach (UiPart part in Enum.GetValues<UiPart>())
        {
            var box = new CheckBox { Text = UiProfiles.Describe(part), AutoSize = true, ForeColor = DaocTheme.Text,
                Checked = part != UiPart.Quickbars, AccessibleName = part.ToString() };
            box.CheckedChanged += (_, _) => UpdateState();
            _parts[part] = box;
            body.Controls.Add(box);
        }
        body.Controls.Add(new Label { Text = "Copy to", AutoSize = true, ForeColor = DaocTheme.Gold, Margin = new Padding(3, 10, 3, 4),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold) });
        body.Controls.Add(_targets);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        actions.Controls.AddRange([_copy, new Label { Text = "  or name it", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
            _profileName, _saveProfile, _openBackups]);
        body.Controls.Add(actions);
        body.Controls.Add(_status);
        Controls.Add(body);

        _folder.SelectedIndexChanged += (_, _) => LoadCharacters();
        _source.SelectedIndexChanged += (_, _) => { ShowTargets(); UpdateState(); };
        // ItemCheck fires before the tick lands, so count the one changing now.
        _targets.ItemCheck += (_, change) => UpdateState(change.NewValue == CheckState.Checked ? 1 : -1);
        _profileName.TextChanged += (_, _) => UpdateState();
        _copy.Click += (_, _) => Copy();
        _saveProfile.Click += (_, _) => SaveProfile();
        _openBackups.Click += (_, _) =>
        {
            string backups = Path.Combine(_profileFolder, "backups");
            Directory.CreateDirectory(backups);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{backups}\"") { UseShellExecute = true });
        };

        LoadFolders(Path.GetFileName(settingsFolder));
    }

    /// <summary>Whether a game window (class DAoCMWC) is open on this PC.</summary>
    public static bool GameWindowOpen() => FindWindow("DAoCMWC", null) != IntPtr.Zero;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    private static Control Row(string label, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 110, Height = 24, TextAlign = ContentAlignment.MiddleLeft });
        row.Controls.Add(control);
        return row;
    }

    // Every game settings folder with characters in it, this install's first.
    private void LoadFolders(string own)
    {
        var names = Directory.Exists(_settingsRoot)
            ? Directory.EnumerateDirectories(_settingsRoot).Select(Path.GetFileName).OfType<string>()
                .Where(name => UiProfiles.Characters(Path.Combine(_settingsRoot, name)).Count > 0).ToList()
            : new List<string>();
        names.Remove(own);
        names.Insert(0, own);
        _folder.Items.Clear();
        _folder.Items.AddRange(names.ToArray());
        _folder.SelectedIndex = 0;
    }

    private void LoadCharacters()
    {
        _characters = UiProfiles.Characters(Path.Combine(_settingsRoot, (string)_folder.SelectedItem!));
        _source.Items.Clear();
        _source.Items.AddRange(_characters.Cast<object>().ToArray());
        _source.Items.AddRange(UiProfiles.SavedProfiles(_profileFolder).Select(name => (object)(ProfilePrefix + name)).ToArray());
        if (_source.Items.Count > 0)
            _source.SelectedIndex = 0;
        ShowTargets();
        UpdateState();
        if (_characters.Count == 0)
            _status.Text = "No characters here yet. A character's settings file appears after it logs in and out once.";
    }

    private void ShowTargets()
    {
        _targets.Items.Clear();
        foreach (CharacterUi character in _characters)
        {
            if (!ReferenceEquals(character, _source.SelectedItem))
                _targets.Items.Add(character);
        }
    }

    private List<UiPart> Parts() => _parts.Where(pair => pair.Value.Checked).Select(pair => pair.Key).ToList();

    private string? SourcePath() => _source.SelectedItem switch
    {
        CharacterUi character => character.Path,
        string profile => Path.Combine(_profileFolder, profile[ProfilePrefix.Length..] + ".ini"),
        _ => null,
    };

    private void UpdateState() => UpdateState(0);

    private void UpdateState(int checking)
    {
        bool parts = Parts().Count > 0;
        _copy.Enabled = parts && SourcePath() != null && _targets.CheckedItems.Count + checking > 0;
        _saveProfile.Enabled = parts && _source.SelectedItem is CharacterUi && _profileName.Text.Trim().Length > 0;
    }

    private bool GameClosed()
    {
        if (!_gameRunning())
            return true;
        _status.Text = "Close the game first: it saves each character's settings when it logs out, which would undo the copy.";
        return false;
    }

    private void Copy()
    {
        string? source = SourcePath();
        if (source == null || !GameClosed())
            return;
        var targets = _targets.CheckedItems.Cast<CharacterUi>().ToList();
        try
        {
            UiProfiles.Apply(source, Parts(), targets.Select(target => target.Path), Path.Combine(_profileFolder, "backups"), DateTime.Now);
            _status.Text = $"Copied to {targets.Count} character{(targets.Count == 1 ? "" : "s")}: {string.Join(", ", targets.Select(t => t.Name))}. " +
                "Their old files are in Open backups.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Could not copy: " + exception.Message;
        }
    }

    private void SaveProfile()
    {
        if (_source.SelectedItem is not CharacterUi character)
            return;
        try
        {
            string path = UiProfiles.SaveProfile(character.Path, Parts(), _profileFolder, _profileName.Text);
            _status.Text = $"Saved profile \"{Path.GetFileNameWithoutExtension(path)}\". Pick it under Copy from to put it on other characters.";
            object selected = _source.SelectedItem;
            LoadCharacters();
            _source.SelectedItem = selected;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = "Could not save the profile: " + exception.Message;
        }
    }
}
