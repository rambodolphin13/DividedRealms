using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

internal sealed class BotGoalsSettingsControl : UserControl
{
    private readonly string _path;
    private readonly Func<bool> _serverStopped;
    private readonly NumericUpDown[,] _values = new NumericUpDown[3, 4];
    private readonly Label[] _totals = new Label[3];
    private readonly CheckBox _spreadEnabled = new() { Text = "Stop some bots below level 50", AutoSize = true, Margin = new Padding(3, 3, 3, 8) };
    private readonly NumericUpDown[] _spread = new NumericUpDown[5];
    private readonly Label _spreadTotal = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(850, 0) };
    private readonly Button _save = new() { Text = "Save settings", AutoSize = true };
    private readonly Button _undo = new() { Text = "Undo edits", AutoSize = true };
    private readonly Button _defaults = new() { Text = "Restore defaults", AutoSize = true };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private BotGoalSettings _saved = BotGoalSettings.Defaults;
    private bool _loading, _loadFailed, _savedFile;
    private string _notice = "";
    public bool HasUnsavedChanges => ReadValues() != _saved || _loadFailed;

    public BotGoalsSettingsControl(string path, Func<bool> serverStopped)
    {
        _path = path;
        _serverStopped = serverStopped;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        AutoScroll = true;
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16),
        };
        body.Controls.Add(new Label { Text = "Bot Goals Setting", AutoSize = true,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = DaocTheme.GoldLight });
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(850, 0), Margin = new Padding(3, 10, 3, 15),
            Text = "Choose the goal mix for each level bracket. Each row must total 100%.\n0% disables a goal; 100% selects only that goal. Applies to autonomous gamebots in all three realms." });
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 6, RowCount = 4, Margin = new Padding(3, 3, 3, 15) };
        foreach (int width in new[] { 150, 125, 125, 125, 140, 190 }) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        string[] headings = ["Level bracket", "Solo PvE %", "Group PvE %", "RvR %", "Battlegrounds %", "Row total"];
        for (int col = 0; col < 6; col++) table.Controls.Add(new Label { Text = headings[col], AutoSize = true, Padding = new Padding(0, 4, 0, 8) }, col, 0);
        for (int row = 0; row < 3; row++)
        {
            table.Controls.Add(new Label { Text = new[] { "Levels 1–19", "Levels 20–49", "Level 50" }[row], AutoSize = true, Padding = new Padding(0, 8, 0, 8) }, 0, row + 1);
            for (int col = 0; col < 4; col++)
            {
                var value = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 100,
                    Margin = new Padding(3, 5, 3, 8), AccessibleName = headings[col] + " " + row,
                    BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
                _values[row, col] = value;
                value.ValueChanged += (_, _) => { if (!_loading) { _notice = ""; UpdateState(); } };
                table.Controls.Add(value, col + 1, row + 1);
            }
            _totals[row] = new Label { AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
            table.Controls.Add(_totals[row], 5, row + 1);
        }
        body.Controls.Add(table);
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(850, 0),
            Text = "Levels 1–19 cannot do RvR. Saved settings replace forced PvE after RvR, allowing back-to-back RvR.\n" +
                "Battlegrounds % sends bots to the battleground of their level: Abermenai 15–19, Thidranki 20–24, Murdaigean 25–29,\n" +
                "Caledonia 30–35. Bots outside those levels (1–14 and 36–49) do solo or group PvE with that share instead. Level 50 cannot.\n" +
                "Percentages are population targets, not exact head counts at every moment. Existing allowed tasks finish normally.\n" +
                "Group-only bots wait for a complete eight-member role roster; they never fall back to a 0% goal.\n" +
                "Normal training, selling, recovery and town breaks remain. Player characters and /spawn companions are unchanged." });
        body.Controls.Add(new Label { Text = "Level spread", AutoSize = true, Margin = new Padding(3, 22, 3, 6),
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold), ForeColor = DaocTheme.GoldLight });
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(850, 0), Margin = new Padding(3, 0, 3, 8),
            Text = "Keeps bots in every level range. Each bot gets a stop level from these shares; when it reaches it, it stops\n" +
                "gaining experience but keeps playing, looting and earning realm points. The shares must total 100%." });
        body.Controls.Add(_spreadEnabled);
        _spreadEnabled.CheckedChanged += (_, _) => { if (!_loading) { _notice = ""; UpdateState(); } };
        var spreadTable = new TableLayoutPanel { AutoSize = true, ColumnCount = 6, RowCount = 2, Margin = new Padding(3, 3, 3, 8) };
        foreach (int width in new[] { 120, 120, 120, 120, 120, 170 }) spreadTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        string[] spreadHeadings = ["Stop at 1–9 %", "Stop at 10–19 %", "Stop at 20–34 %", "Stop at 35–49 %", "Reach 50 %", "Total"];
        for (int col = 0; col < 6; col++) spreadTable.Controls.Add(new Label { Text = spreadHeadings[col], AutoSize = true, Padding = new Padding(0, 4, 0, 8) }, col, 0);
        for (int col = 0; col < 5; col++)
        {
            var value = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 100, Margin = new Padding(3, 5, 3, 8),
                AccessibleName = spreadHeadings[col], BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
            _spread[col] = value;
            value.ValueChanged += (_, _) => { if (!_loading) { _notice = ""; UpdateState(); } };
            spreadTable.Controls.Add(value, col, 1);
        }
        spreadTable.Controls.Add(_spreadTotal, 5, 1);
        body.Controls.Add(spreadTable);
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(850, 0),
            Text = "A bot's stop level comes from its roster id, so it stays the same every start. Bots already past their stop\n" +
                "keep their level. A world full of level 50s fills the lower ranges from new bots (+ Lv.1 / + Lv.25) and bots\n" +
                "still levelling. Special Forces, account companions, players and /spawn companions are never held back." });
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 10) };
        buttons.Controls.AddRange([_save, _undo, _defaults]);
        body.Controls.Add(buttons);
        body.Controls.Add(_status);
        Controls.Add(body);
        _save.Click += (_, _) => SaveSettings();
        _undo.Click += (_, _) => LoadSettings();
        _defaults.Click += (_, _) => { SetValues(BotGoalSettings.Defaults); _notice = "Default percentages loaded. Click Save settings to apply."; UpdateState(); };
        LoadSettings();
        _poll.Tick += (_, _) => UpdateState();
        _poll.Start();
    }

    private BotGoalSettings ReadValues() => new()
    {
        Levels1To19 = Row(0), Levels20To49 = Row(1), Level50 = Row(2),
        LevelSpread = Spread(),
    };
    private BotLevelSpread Spread() => new(_spreadEnabled.Checked, (int)_spread[0].Value, (int)_spread[1].Value,
        (int)_spread[2].Value, (int)_spread[3].Value, (int)_spread[4].Value);
    private BotGoalWeights Row(int row) => new((int)_values[row, 0].Value, (int)_values[row, 1].Value, (int)_values[row, 2].Value,
        (int)_values[row, 3].Value);

    private void SetValues(BotGoalSettings settings)
    {
        _loading = true;
        BotGoalWeights[] rows = [settings.Levels1To19, settings.Levels20To49, settings.Level50];
        for (int row = 0; row < 3; row++)
        {
            _values[row, 0].Value = rows[row].SoloPve;
            _values[row, 1].Value = rows[row].GroupPve;
            _values[row, 2].Value = rows[row].RvR;
            _values[row, 3].Value = rows[row].Battlegrounds;
        }
        BotLevelSpread spread = settings.LevelSpread ?? BotLevelSpread.Defaults;
        _spreadEnabled.Checked = spread.Enabled;
        _spread[0].Value = spread.Stop1To9;
        _spread[1].Value = spread.Stop10To19;
        _spread[2].Value = spread.Stop20To34;
        _spread[3].Value = spread.Stop35To49;
        _spread[4].Value = spread.Reach50;
        _loading = false;
    }

    private void LoadSettings()
    {
        try
        {
            _saved = BotGoalSettings.Load(_path);
            _savedFile = File.Exists(_path);
            _loadFailed = false;
            _notice = "";
            SetValues(_saved);
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            SetValues(BotGoalSettings.Defaults);
            _notice = "Cannot read saved goals: " + ex.Message + " Review these values and Save to repair the settings file.";
        }
        UpdateState();
    }

    private void SaveSettings()
    {
        try
        {
            var values = ReadValues();
            values.Save(_path, _serverStopped);
            _saved = values;
            _savedFile = true;
            _loadFailed = false;
            _notice = "Saved. These goals will apply on the next server start.";
        }
        catch (Exception ex) { _notice = "Not saved: " + ex.Message; }
        UpdateState();
    }

    public void UpdateState()
    {
        bool stopped = _serverStopped();
        bool valid = true;
        for (int row = 0; row < 3; row++)
        {
            int total = Row(row).Total;
            valid &= total == 100;
            _totals[row].Text = total == 100 ? "100% — ready" : $"{total}% — needs 100%";
            _totals[row].ForeColor = total == 100 ? Color.LightGreen : Color.Salmon;
            // Levels 1–19 have no RvR and level 50 has no battleground bracket.
            for (int col = 0; col < 4; col++) _values[row, col].Enabled = stopped && !(row == 0 && col == 2) && !(row == 2 && col == 3);
        }
        int spreadTotal = Spread().Total;
        valid &= spreadTotal == 100;
        _spreadTotal.Text = spreadTotal == 100 ? "100% — ready" : $"{spreadTotal}% — needs 100%";
        _spreadTotal.ForeColor = spreadTotal == 100 ? Color.LightGreen : Color.Salmon;
        _spreadEnabled.Enabled = stopped;
        foreach (NumericUpDown value in _spread) value.Enabled = stopped;
        _save.Enabled = stopped && valid;
        _defaults.Enabled = _undo.Enabled = stopped;
        _status.Text = !stopped ? "Locked: stop the server to change bot goals." :
            _notice.Length > 0 ? _notice : !valid ? "Not saved. Adjust each row and the level spread to exactly 100%." :
            HasUnsavedChanges ? "Unsaved changes — click Save settings before starting the server." :
            _savedFile ? "Saved settings ready. Start the server when ready." :
            "Current default percentages shown. Click Save settings to enable this goal policy (including back-to-back RvR).";
        _status.ForeColor = stopped && valid && !_loadFailed ? DaocTheme.GoldLight : Color.Salmon;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _poll.Dispose();
        base.Dispose(disposing);
    }
}
