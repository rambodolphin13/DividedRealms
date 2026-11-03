using System.Drawing.Drawing2D;
using System.Globalization;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The Overlay tab: a control for every OverlaySettings.Schema entry, grouped, beside a live preview drawn by the
/// overlay's own Render with sample numbers, a fight card and effect panels. Saving writes overlay.json, which a
/// running overlay picks up within a second.
/// </summary>
internal sealed class OverlaySettingsControl : UserControl
{
    private static readonly Size PreviewGame = new(1920, 1080);
    private readonly string _path;
    private readonly PictureBox _preview = new() { Size = new Size(640, 360), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = DaocTheme.Muted };
    private readonly Button _save = new() { Text = "Save", AutoSize = true };
    private readonly Button _undo = new() { Text = "Undo edits", AutoSize = true };
    private readonly Button _defaults = new() { Text = "Restore defaults", AutoSize = true };
    private readonly Dictionary<string, Action<string>> _show = new();
    private OverlaySettings _saved;
    private OverlaySettings _editing;
    private bool _loading;

    public OverlaySettingsControl(string path)
    {
        _path = path;
        _saved = OverlaySettings.Load(path);
        _editing = _saved.Copy();
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;

        // The settings scroll; the preview beside them stays in view.
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16, 16, 16, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 490));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var settings = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        settings.Controls.Add(BuildSettings());
        layout.Controls.Add(settings, 0, 0);
        layout.Controls.Add(BuildPreview(), 1, 0);
        Controls.Add(layout);

        _save.Click += (_, _) => Save();
        _undo.Click += (_, _) => { _editing = _saved.Copy(); ShowValues(); Changed(); };
        _defaults.Click += (_, _) => { _editing = new OverlaySettings(); ShowValues(); Changed(); };
        ShowValues();
        Changed();
    }

    public bool HasUnsavedChanges => !Same(_editing, _saved);

    private Control BuildSettings()
    {
        var body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        body.Controls.Add(new Label { Text = "Overlay", AutoSize = true, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = DaocTheme.GoldLight });
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(3, 8, 3, 10),
            Text = "Floating numbers, the fight card and the effect panels drawn over the game. They show while the game is in front, " +
                "in a window or borderless. Positions are parts of the game window: 0 is the left or top edge, 1 the right or bottom." });

        foreach (IGrouping<string, OverlaySetting> group in OverlaySettings.Schema.GroupBy(setting => setting.Group))
        {
            body.Controls.Add(new Label { Text = group.Key, AutoSize = true, ForeColor = DaocTheme.Gold,
                Font = new Font(Font.FontFamily, 11, FontStyle.Bold), Margin = new Padding(3, 12, 3, 4) });
            foreach (OverlaySetting setting in group)
                body.Controls.Add(BuildControl(setting));
        }
        return body;
    }

    private Control BuildControl(OverlaySetting setting)
    {
        switch (setting.Kind)
        {
            case OverlaySettingKind.Toggle:
            {
                var box = new CheckBox { Text = setting.Label, AutoSize = true, ForeColor = DaocTheme.Text, AccessibleName = setting.Key };
                box.CheckedChanged += (_, _) => Set(setting.Key, box.Checked ? "True" : "False");
                _show[setting.Key] = value => box.Checked = value == "True";
                return box;
            }
            case OverlaySettingKind.Slider:
            {
                int steps = (int)Math.Round((setting.Max - setting.Min) / setting.Step);
                var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(3, 0, 3, 0) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
                var bar = new TrackBar { Minimum = 0, Maximum = steps, TickStyle = TickStyle.None, Width = 210, AutoSize = false, Height = 26,
                    AccessibleName = setting.Key };
                var shown = new Label { AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
                row.Controls.Add(new Label { Text = setting.Label, AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, 0, 0);
                row.Controls.Add(bar, 1, 0);
                row.Controls.Add(shown, 2, 0);
                bar.ValueChanged += (_, _) =>
                {
                    string value = (setting.Min + bar.Value * setting.Step).ToString("0.###", CultureInfo.InvariantCulture);
                    shown.Text = value;
                    Set(setting.Key, value);
                };
                _show[setting.Key] = value =>
                {
                    float number = float.Parse(value, CultureInfo.InvariantCulture);
                    bar.Value = Math.Clamp((int)Math.Round((number - setting.Min) / setting.Step), 0, steps);
                    shown.Text = value;
                };
                return row;
            }
            default:
            {
                var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(3, 0, 3, 0) };
                var swatch = new Button { Width = 46, Height = 22, FlatStyle = FlatStyle.Flat, AccessibleName = setting.Key };
                row.Controls.Add(swatch);
                row.Controls.Add(new Label { Text = setting.Label, AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
                swatch.Click += (_, _) =>
                {
                    using var dialog = new ColorDialog { Color = swatch.BackColor, FullOpen = true };
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        Set(setting.Key, ColorTranslator.ToHtml(Color.FromArgb(dialog.Color.R, dialog.Color.G, dialog.Color.B)));
                    _show[setting.Key](_editing[setting.Key]);
                };
                _show[setting.Key] = value => swatch.BackColor = ColorTranslator.FromHtml(value);
                return row;
            }
        }
    }

    private Control BuildPreview()
    {
        var body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(16, 32, 3, 3) };
        body.Controls.Add(new Label { Text = "Preview", AutoSize = true, ForeColor = DaocTheme.Gold, Font = new Font(Font.FontFamily, 11, FontStyle.Bold) });
        body.Controls.Add(_preview);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
        buttons.Controls.AddRange([_save, _undo, _defaults]);
        body.Controls.Add(buttons);
        body.Controls.Add(_status);
        return body;
    }

    private void Set(string key, string value)
    {
        if (_loading)
            return;
        _editing[key] = value;
        Changed();
    }

    private void ShowValues()
    {
        _loading = true;
        foreach (OverlaySetting setting in OverlaySettings.Schema)
            _show[setting.Key](_editing[setting.Key]);
        _loading = false;
    }

    private void Changed()
    {
        bool unsaved = HasUnsavedChanges;
        _save.Enabled = unsaved;
        _undo.Enabled = unsaved;
        _defaults.Enabled = _editing.Changed.Count > 0;
        if (unsaved)
            _status.Text = "Not saved yet.";
        Image? old = _preview.Image;
        _preview.Image = Preview(_editing);
        old?.Dispose();
    }

    private void Save()
    {
        try
        {
            _editing.Save(_path);
            _saved = _editing.Copy();
            _status.Text = "Saved. A running overlay picks it up within a second.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Could not save " + _path + ": " + exception.Message;
        }
        Changed();
    }

    private static bool Same(OverlaySettings a, OverlaySettings b) =>
        a.Changed.Count == b.Changed.Count && a.Changed.All(pair => b.Changed.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    /// <summary>A sample frame drawn the way the overlay draws it, over a plain stand-in for the game.</summary>
    internal static Bitmap Preview(OverlaySettings settings)
    {
        var numbers = new FloatingNumbers();
        CombatNumber[] samples =
        [
            new(CombatNumberKind.Out, CombatNumberWho.Self, 76, false, 1, null),
            new(CombatNumberKind.Out, CombatNumberWho.Self, 105, true, 1, null),
            new(CombatNumberKind.Out, CombatNumberWho.Pet, 48, false, 1, null),
            new(CombatNumberKind.Out, CombatNumberWho.Group, 131, false, 1, null),
            new(CombatNumberKind.AvoidOut, CombatNumberWho.Self, 0, false, 1, "Parry"),
            new(CombatNumberKind.In, CombatNumberWho.Self, 34, false, 1, null),
            new(CombatNumberKind.HealIn, CombatNumberWho.Self, 212, false, 0, null),
            new(CombatNumberKind.AvoidIn, CombatNumberWho.Self, 0, false, 1, "Block"),
        ];
        long born = 0;
        foreach (CombatNumber sample in samples)
        {
            if (FloatingNumbers.Shows(sample, settings))
                numbers.Add(sample, born);
            born += 170;
        }
        var card = new FightReport(38,
            [new("You", "Warrior", 20, "player", 1, 1240, 96, 0, 212, 2, 0), new("Aelfric", "Cleric", 20, "companion", 1, 180, 40, 860, 0, 0, 0),
             new("Fang", null, 18, "pet", 1, 410, 133, 0, 0, 1, 0)],
            [new("soft-shelled crab", null, 12, "npc", 3, 269, 1830, 0, 0, 0, 3)]);
        var effects = new EffectSnapshot(
            [new("Poisoned", 2, 14, 30, false, false), new("Strength/Constitution", 1, 1180, 1200, true, false), new("Haste", 3, 41, 60, true, true)],
            [new("Fang", 18, 72, [new EffectInfo("Pet Strength", 5, 290, 600, true, true)])],
            new EffectUnit("soft-shelled crab", 12, 38, [new EffectInfo("Snare", 6, 9, 20, false, true)]));

        var image = new Bitmap(PreviewGame.Width, PreviewGame.Height);
        using Graphics graphics = Graphics.FromImage(image);
        var area = new Rectangle(Point.Empty, PreviewGame);
        using (var sky = new LinearGradientBrush(area, Color.FromArgb(70, 110, 150), Color.FromArgb(60, 80, 45), 90f))
            graphics.FillRectangle(sky, area);
        graphics.FillEllipse(Brushes.SaddleBrown, PreviewGame.Width / 2 - 20, PreviewGame.Height / 2 - 14, 40, 94);
        using var layer = new Bitmap(PreviewGame.Width, PreviewGame.Height);
        using (Graphics overlay = Graphics.FromImage(layer))
            CombatNumbersOverlay.Render(overlay, PreviewGame, numbers.Frame(1300, settings),
                settings.On("card.enabled") ? card : null, 1, effects, settings);
        graphics.DrawImage(layer, 0, 0);
        return image;
    }
}
