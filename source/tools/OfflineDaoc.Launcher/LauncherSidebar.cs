namespace OfflineDaoc.Launcher;

/// <summary>
/// The launcher's left-hand navigation: sections (World, Settings, Tools) of entries that either show one of the
/// main tab control's pages (its tab strip is hidden) or run an action, such as opening the World Editor.
/// The entry for the page on show is highlighted.
/// </summary>
internal sealed class LauncherSidebar : Panel
{
    internal static readonly Color Background = Color.FromArgb(29, 25, 21);
    private static readonly Color Selected = Color.FromArgb(58, 48, 32);
    private static readonly Color Hover = Color.FromArgb(42, 37, 30);
    private static readonly Color ItemText = Color.FromArgb(201, 191, 164);
    private static readonly Color SelectedText = Color.FromArgb(245, 226, 173);

    private readonly TabControl _pages;
    private readonly FlowLayoutPanel _list = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        Padding = new Padding(8, 8, 4, 8), BackColor = Background,
    };
    private readonly Dictionary<TabPage, Button> _entries = new();

    public LauncherSidebar(TabControl pages)
    {
        _pages = pages;
        BackColor = Background;
        Controls.Add(_list);
        _pages.SelectedIndexChanged += (_, _) => Highlight();
    }

    /// <summary>The entry that shows this page.</summary>
    public IReadOnlyDictionary<TabPage, Button> Entries => _entries;

    public void AddSection(string title) => _list.Controls.Add(new Label
    {
        Text = title.ToUpperInvariant(),
        AutoSize = true,
        Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        ForeColor = Color.FromArgb(166, 154, 128),
        Margin = new Padding(10, 9, 3, 2),
    });

    public Button AddPage(string text, TabPage page)
    {
        Button entry = Entry(text);
        entry.Click += (_, _) => _pages.SelectedTab = page;
        _entries[page] = entry;
        Highlight();
        return entry;
    }

    public Button AddAction(string text, Action action)
    {
        Button entry = Entry(text);
        entry.Click += (_, _) => action();
        return entry;
    }

    private Button Entry(string text)
    {
        var entry = new Button
        {
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = ItemText,
            BackColor = Background,
            Width = 164,
            Height = 27,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        entry.FlatAppearance.BorderSize = 0;
        entry.FlatAppearance.MouseOverBackColor = Hover;
        entry.FlatAppearance.MouseDownBackColor = Selected;
        _list.Controls.Add(entry);
        return entry;
    }

    private void Highlight()
    {
        // Before the tab control has a window it reports no selection, though it will open on its first page.
        int selected = Math.Max(0, _pages.SelectedIndex);
        foreach (var (page, entry) in _entries)
        {
            bool on = _pages.TabPages.IndexOf(page) == selected;
            entry.BackColor = on ? Selected : Background;
            entry.ForeColor = on ? SelectedText : ItemText;
            entry.Font = new Font("Segoe UI", 10f, on ? FontStyle.Bold : FontStyle.Regular);
            entry.AccessibleDescription = on ? "Current page" : null;
        }
    }
}
