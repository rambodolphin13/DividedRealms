namespace OfflineDaoc.Launcher;

/// <summary>
/// Make more accounts and pick the one the game logs in with (PortableCredentials). Each account has its own
/// characters; the server creates a new account the first time the game enters the realm with it.
/// </summary>
internal sealed class AccountsForm : Form
{
    private readonly string _runtime;
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill, BackColor = DaocTheme.Parchment, ForeColor = DaocTheme.Ink, Font = new Font("Georgia", 10.5f),
        IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly TextBox _name = new() { Width = 200, MaxLength = PortableCredentials.MaxNameLength, Font = new Font("Georgia", 10f) };
    private readonly Button _use = Themed("USE THIS ACCOUNT");
    private readonly Button _make = Themed("MAKE ACCOUNT");
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = DaocTheme.GoldLight, TextAlign = ContentAlignment.MiddleLeft };

    /// <summary>The account in use when the window closed.</summary>
    public string CurrentAccount { get; private set; } = "";

    /// <summary>Raised after the player switches account (the new name).</summary>
    public event Action<string>? Switched;

    public AccountsForm(string runtime)
    {
        _runtime = runtime;
        Text = "Accounts";
        Size = new Size(470, 470);
        MinimumSize = new Size(420, 400);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.GoldLight;
        Font = new Font("Georgia", 9f);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = DaocTheme.Text, Margin = new Padding(3, 0, 3, 8),
            Text = "Each account has its own characters. Play logs in with the account in use. A new account is made " +
                   "on the server the first time you enter the realm with it.",
        }, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        _use.Margin = new Padding(3, 8, 3, 12);
        layout.Controls.Add(_use, 0, 2);
        layout.Controls.Add(new Label { Text = "New account name (letters and digits):", AutoSize = true, ForeColor = DaocTheme.Text }, 0, 3);
        var make = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _name.Margin = new Padding(3, 5, 6, 3);
        make.Controls.Add(_name);
        make.Controls.Add(_make);
        layout.Controls.Add(make, 0, 4);
        layout.Controls.Add(_status, 0, 5);
        Controls.Add(layout);

        _list.SelectedIndexChanged += (_, _) => _use.Enabled = Selected() is { } a && !IsCurrent(a);
        _list.DoubleClick += (_, _) => UseSelected();
        _use.Click += (_, _) => UseSelected();
        _make.Click += (_, _) => MakeAccount();
        _name.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            MakeAccount();
        };
        AcceptButton = null;
        Reload(null);
        UiScale.Apply(this);
    }

    private SavedAccount? Selected() => (_list.SelectedItem as Entry)?.Account;

    private bool IsCurrent(SavedAccount account) => string.Equals(account.Account, CurrentAccount, StringComparison.OrdinalIgnoreCase);

    private void Reload(string? select)
    {
        IReadOnlyList<SavedAccount> accounts = PortableCredentials.List(_runtime);
        CurrentAccount = accounts[0].Account;   // the account in use is listed first
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (SavedAccount account in accounts)
            _list.Items.Add(new Entry(account, IsCurrent(account)));
        _list.EndUpdate();
        int index = accounts.ToList().FindIndex(a => string.Equals(a.Account, select ?? CurrentAccount, StringComparison.OrdinalIgnoreCase));
        _list.SelectedIndex = Math.Max(0, index);
        _use.Enabled = Selected() is { } s && !IsCurrent(s);
    }

    internal void UseSelected()
    {
        if (Selected() is not { } account || IsCurrent(account))
            return;
        try
        {
            PortableCredentials.Use(_runtime, account.Account);
            Reload(account.Account);
            _status.ForeColor = DaocTheme.Success;
            _status.Text = $"Now using {account.Account}. Close the game if it is open, then press Play.";
            Switched?.Invoke(account.Account);
        }
        catch (Exception ex) { ShowProblem(ex.Message); }
    }

    internal void MakeAccount()
    {
        try
        {
            SavedAccount account = PortableCredentials.Create(_runtime, _name.Text);
            _name.Clear();
            Reload(account.Account);
            _status.ForeColor = DaocTheme.Success;
            _status.Text = $"Made {account.Account}. Select it and press Use this account to play on it.";
        }
        catch (ArgumentException ex) { ShowProblem(ex.Message); }
        catch (Exception ex) { ShowProblem("The account couldn't be saved: " + ex.Message); }
    }

    private void ShowProblem(string message)
    {
        _status.ForeColor = DaocTheme.Danger;
        _status.Text = message;
    }

    private static Button Themed(string text) => new()
    {
        Text = text, AutoSize = true, BackColor = DaocTheme.PanelAlt, ForeColor = DaocTheme.GoldLight, FlatStyle = FlatStyle.Flat,
        Font = new Font("Georgia", 9f, FontStyle.Bold), Padding = new Padding(8, 2, 8, 2),
    };

    private sealed record Entry(SavedAccount Account, bool InUse)
    {
        public override string ToString() => InUse ? $"{Account.Account}   (in use)" : Account.Account;
    }
}
