using System.Diagnostics;
using System.Net;
using OfflineDaoc.Passport;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Client-only mode: start this game's client against a friend's server, from their invite code or address.
/// It never starts the local server. The local save is only changed to send a character on a trip or take it back,
/// see docs/CHARACTER-PASSPORT.md. Ported from stefanrows/OfflineDAoC (Join Friend).
/// </summary>
internal sealed class JoinFriendForm : Form
{
    private sealed record TravelChoice(LocalCharacter? Character, string Text)
    {
        public override string ToString() => Text;
    }

    private readonly string _runtime;
    private readonly string _logsDirectory;
    private readonly JoinClient _client;
    private readonly ComboBox _character = new()
    {
        Dock = DockStyle.Bottom,
        DropDownStyle = ComboBoxStyle.DropDownList,
        BackColor = DaocTheme.PanelAlt,
        ForeColor = DaocTheme.Text,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Georgia", 9f),
        Margin = new Padding(0, 3, 0, 0),
    };
    private readonly TextBox _visitingName = InputBox(20);
    private readonly TextBox _hostAddress = InputBox(64);
    private readonly TextBox _guestAccount = InputBox(20);
    private readonly TextBox _password = InputBox(20, password: true);
    private readonly CheckBox _rememberPassword = new()
    {
        Text = "Remember my password (encrypted for this Windows user)",
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 8f),
        BackColor = Color.Transparent,
    };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.GoldLight,
        Font = new Font("Georgia", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Button _joinButton = new RuneButton
    {
        Text = "JOIN FRIEND",
        Width = 170,
        Height = 34,
        Accent = Color.FromArgb(104, 111, 77),
        Margin = new Padding(4),
    };
    private readonly Button _comeHomeButton = new RuneButton
    {
        Text = "COME HOME",
        Width = 130,
        Height = 34,
        Accent = Color.FromArgb(92, 84, 128),
        Margin = new Padding(4),
    };
    private readonly Button _recallButton = new RuneButton
    {
        Text = "RECALL",
        Width = 100,
        Height = 34,
        Accent = DaocTheme.Iron,
        Margin = new Padding(4),
    };
    private readonly Button _closeButton = new RuneButton
    {
        Text = "CLOSE",
        Width = 110,
        Height = 34,
        Accent = DaocTheme.Iron,
        Margin = new Padding(4),
        DialogResult = DialogResult.Cancel,
    };

    public JoinFriendForm(string runtime, ModernClientProfile? modern)
    {
        _runtime = runtime;
        _client = JoinClient.For(runtime, modern);
        _logsDirectory = Path.Combine(runtime, "logs");

        Text = modern != null ? "Join a Friend — Divided Realms (modern 1.130 client)" : "Join a Friend — Divided Realms";
        ClientSize = new Size(680, 640);
        MinimumSize = new Size(660, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 8.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var frame = new StoneSurface { Dock = DockStyle.Fill, Padding = new Padding(14) };
        Controls.Add(frame);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            Padding = new Padding(4),
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        for (int index = 0; index < 5; index++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        layout.Controls.Add(new Label
        {
            Text = "JOIN A FRIEND'S WORLD",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Plays in your friend's world with an account on their server. A character you bring " +
                   "is locked in your own save until it comes home, with what it earned.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);
        layout.Controls.Add(Field("Invite code from your friend (or their address)", _hostAddress), 0, 2);
        layout.Controls.Add(Field("Your account on the host (made on your first login there)", _guestAccount), 0, 3);
        layout.Controls.Add(Field("Your password there (first time: the password your friend gave you, if any)", _password), 0, 4);
        layout.Controls.Add(ComboField("Bring a character from your own save", _character), 0, 5);
        layout.Controls.Add(Field("Visiting name (only if your character's name is taken there)", _visitingName), 0, 6);
        layout.Controls.Add(_rememberPassword, 0, 7);

        var connectionNote = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(2, 4, 2, 2),
        };
        connectionNote.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connectionNote.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connectionNote.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connectionNote.Controls.Add(new Label
        {
            Text = $"The host presses START HOSTING in HOST FOR FRIENDS. You both need the same game " +
                   $"(Classic, Stormforged or the modern test). This client logs in on port {_client.LoginPort}.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Muted,
            Font = new Font("Georgia", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        connectionNote.Controls.Add(_status, 0, 1);
        layout.Controls.Add(connectionNote, 0, 8);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_joinButton);
        buttons.Controls.Add(_comeHomeButton);
        buttons.Controls.Add(_recallButton);
        layout.Controls.Add(buttons, 0, 9);
        frame.Controls.Add(layout);

        _joinButton.Click += async (_, _) => await JoinAsync();
        _comeHomeButton.Click += async (_, _) => await ComeHomeAsync();
        _recallButton.Click += async (_, _) => await RecallAsync();
        _character.SelectedIndexChanged += (_, _) => UpdateTravelButtons();
        Shown += async (_, _) => await ReloadCharactersAsync(finishPending: true);
        _closeButton.Click += (_, _) => Close();
        AcceptButton = _joinButton;
        CancelButton = _closeButton;
        _status.Text = "Paste your friend's invite code and choose an account name.";
        LoadSavedProfile();
    }

    private void LoadSavedProfile()
    {
        JoinFriendProfile saved = JoinFriendProfile.Load(JoinFriendProfile.DefaultPath);
        _hostAddress.Text = saved.HostAddress;
        _guestAccount.Text = saved.GuestAccount;
        _password.Text = saved.Password;
        _rememberPassword.Checked = saved.RememberPassword;
        if (saved.GuestAccount.Length > 0)
            _status.Text = saved.RememberPassword
                ? "Saved details loaded. Press JOIN FRIEND to connect."
                : "Saved details loaded. Enter your password to connect.";
    }

    private void SaveProfile(string target, string guestAccount, string password)
    {
        try
        {
            JoinFriendProfile.Save(JoinFriendProfile.DefaultPath, target, guestAccount, password, _rememberPassword.Checked);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Joining matters more than remembering; the next session asks again.
        }
    }

    private LocalCharacter? SelectedCharacter => (_character.SelectedItem as TravelChoice)?.Character;

    private void UpdateTravelButtons() =>
        _comeHomeButton.Enabled = _recallButton.Enabled = SelectedCharacter?.AwayAt != null;

    private async Task ReloadCharactersAsync(bool finishPending = false)
    {
        string? selected = SelectedCharacter?.Id;
        IReadOnlyList<LocalCharacter> characters = [];
        IReadOnlyList<string> finished = [];
        try
        {
            (characters, finished) = await Task.Run(() =>
            {
                IReadOnlyList<string> done = finishPending ? PassportTravel.ImportPending(_runtime, PassportTravel.DefaultFolder) : [];
                return (JoinFriend.HomeCharacters(_runtime), done);
            });
        }
        catch (Exception exception) when (exception is PassportException or IOException or System.Data.SQLite.SQLiteException or InvalidDataException)
        {
            SetStatus(exception.Message, DaocTheme.Danger);
        }
        if (IsDisposed || Disposing)
            return;

        _character.Items.Clear();
        _character.Items.Add(new TravelChoice(null, "No one: play a character in your friend's world"));
        foreach (LocalCharacter character in characters)
            _character.Items.Add(new TravelChoice(character, JoinFriend.DescribeCharacter(character)));
        _character.SelectedIndex = Math.Max(0, characters.ToList().FindIndex(c => c.Id == selected) + 1);
        UpdateTravelButtons();
        if (finished.Count > 0)
            SetStatus(string.Join(" ", finished), DaocTheme.Success);
    }

    private bool TryReadDetails(out IReadOnlyList<IPAddress> hosts, out string guestAccount, out string password)
    {
        guestAccount = _guestAccount.Text.Trim();
        password = _password.Text;
        if (!JoinFriend.TryParseTarget(_hostAddress.Text, _client.LoginPort, out hosts, out string error))
        {
            SetStatus(error, DaocTheme.Danger);
            return false;
        }
        if (!JoinFriend.IsValidAccount(guestAccount))
        {
            SetStatus("Your account name must be 1–20 letters or numbers.", DaocTheme.Danger);
            return false;
        }
        if (!JoinFriend.IsValidPassword(password))
        {
            SetStatus("The client accepts a password of 1–20 printable ASCII characters.", DaocTheme.Danger);
            return false;
        }
        return true;
    }

    private PassportDeskClient Desk(IPAddress host, string guestAccount, string password) =>
        new(host.ToString(), _client.LoginPort + PassportDeskClient.PortOffset, guestAccount, password);

    /// <summary>Hands the chosen character to the host, if there is one to hand over. False when the join must stop here.</summary>
    private async Task<bool> BringCharacterAsync(IPAddress host, string guestAccount, string password)
    {
        LocalCharacter? character = SelectedCharacter;
        if (character == null)
            return true;

        try
        {
            using PassportDeskClient desk = Desk(host, guestAccount, password);
            if (character.AwayAt != null && PassportTravel.PendingArrival(character.Id) == null)
            {
                if ((await desk.VisitorsAsync()).Any(visitor => visitor.CharacterId == character.Id))
                    return true;
                SetStatus($"{character.Name} is away at {character.AwayAt}, not on this account here.", DaocTheme.Danger);
                return false;
            }

            SetStatus($"Sending {character.Name} to your friend's world…", DaocTheme.GoldLight);
            string visitingName = _visitingName.Text;
            string arrived = await Task.Run(() => PassportTravel.BringAsync(_runtime, character.Name, host.ToString(), desk, PassportTravel.DefaultFolder, visitingName));
            await ReloadCharactersAsync();
            SetStatus($"{arrived} arrived. Choose it on the character screen.", DaocTheme.Success);
            return true;
        }
        catch (PassportException exception)
        {
            await ReloadCharactersAsync();
            SetStatus(JoinFriend.TravelMessage(exception), DaocTheme.Danger);
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetStatus("Your friend's world did not answer. Check their game is up to date, and that they are hosting.", DaocTheme.Danger);
            return false;
        }
    }

    private async Task ComeHomeAsync()
    {
        LocalCharacter? character = SelectedCharacter;
        if (character?.AwayAt == null || !TryReadDetails(out IReadOnlyList<IPAddress> hosts, out string guestAccount, out string password))
            return;

        _comeHomeButton.Enabled = _recallButton.Enabled = false;
        SetStatus($"Bringing {character.Name} home…", DaocTheme.GoldLight);
        try
        {
            IPAddress? host = await JoinFriend.FindReachableAsync(hosts, _client.LoginPort, TimeSpan.FromSeconds(4));
            if (host == null)
            {
                SetStatus("Cannot reach that world. If it is gone for good, RECALL brings your character back as it left.", DaocTheme.Danger);
                return;
            }

            using PassportDeskClient desk = Desk(host, guestAccount, password);
            VisitorInfo? visitor = (await desk.VisitorsAsync()).FirstOrDefault(v => v.CharacterId == character.Id);
            if (visitor == null)
            {
                SetStatus($"{character.Name} is not on this account in that world.", DaocTheme.Danger);
                return;
            }

            IReadOnlyList<string> notes = await Task.Run(() => PassportTravel.ComeHomeAsync(_runtime, visitor, desk, PassportTravel.DefaultFolder));
            await ReloadCharactersAsync();
            SetStatus(string.Join(" ", notes.Prepend($"{character.Name} came home.")), DaocTheme.Success);
        }
        catch (PassportException exception)
        {
            SetStatus(JoinFriend.TravelMessage(exception), DaocTheme.Danger);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetStatus("That world stopped answering. Try again; your character is safe there, and locked here.", DaocTheme.Danger);
        }
        finally
        {
            UpdateTravelButtons();
        }
    }

    private async Task RecallAsync()
    {
        LocalCharacter? character = SelectedCharacter;
        if (character?.AwayAt == null)
            return;

        DialogResult answer = MessageBox.Show(this,
            $"{character.Name} comes back as it left, with the coin it carried.\n\nEverything it earned at {character.AwayAt} is lost. " +
            "Use this only if that world is gone. Otherwise use COME HOME.\n\nRecall it?",
            "Recall", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        try
        {
            await Task.Run(() => PassportTravel.Recall(_runtime, character));
            await ReloadCharactersAsync();
            SetStatus($"{character.Name} is back home.", DaocTheme.Success);
        }
        catch (PassportException exception)
        {
            SetStatus(JoinFriend.TravelMessage(exception), DaocTheme.Danger);
        }
    }

    private async Task JoinAsync()
    {
        if (!TryReadDetails(out IReadOnlyList<IPAddress> hosts, out string guestAccount, out string password))
            return;
        if (!_client.IsInstalled)
        {
            SetStatus(_client.Modern != null
                ? "Your client is not set up yet. Open the launcher once (START DIVIDED REALMS.cmd); it asks for your DAoC folder."
                : "The game client is missing. Restore connect.exe and game.dll in the client folder.", DaocTheme.Danger);
            return;
        }

        _joinButton.Enabled = false;
        SetStatus("Looking for your friend's server…", DaocTheme.GoldLight);
        IPAddress? hostAddress = await JoinFriend.FindReachableAsync(hosts, _client.LoginPort, TimeSpan.FromSeconds(4));
        if (IsDisposed || Disposing)
            return;
        if (hostAddress == null)
        {
            SetStatus(
                "Cannot reach your friend's server. Check it has finished starting and they are hosting, and ask for a new code " +
                "if their internet address may have changed.",
                DaocTheme.Danger);
            _joinButton.Enabled = true;
            return;
        }

        if (!await BringCharacterAsync(hostAddress, guestAccount, password))
        {
            _joinButton.Enabled = true;
            return;
        }

        try
        {
            ProcessStartInfo start = _client.CreateStartInfo(hostAddress, guestAccount, password);
            if (_client.Modern == null)
                ClientDisplayPreferences.EnsureBorderlessFullscreen(_client.ClientDirectory);
            ClientSessionDiagnostics.Prepare(_logsDirectory);
            Process? client = Process.Start(start);
            if (client == null)
            {
                SetStatus("Windows did not start the game client. Check the client installation and try again.", DaocTheme.Danger);
                _joinButton.Enabled = true;
                return;
            }

            ClientSessionDiagnostics.Start(client, _client.ClientDirectory, _logsDirectory, hostAddress);
            SaveProfile(_hostAddress.Text.Trim(), guestAccount, password);
            _rememberPassword.Enabled = false;
            _joinButton.Text = "CLIENT STARTED";
            SetStatus("Client started. Keep this window open while you play. A character you brought goes back with COME HOME.", DaocTheme.Success);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not start the game client: {exception.Message}", DaocTheme.Danger);
            _joinButton.Enabled = true;
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static TextBox InputBox(int maxLength, bool password = false) => new()
    {
        Dock = DockStyle.Bottom,
        BackColor = DaocTheme.PanelAlt,
        ForeColor = DaocTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Georgia", 9f),
        MaxLength = maxLength,
        UseSystemPasswordChar = password,
        Margin = new Padding(0, 3, 0, 0),
    };

    private static Control ComboField(string caption, ComboBox input) => Labelled(caption, input);

    private static Control Field(string caption, TextBox input) => Labelled(caption, input);

    private static Control Labelled(string caption, Control input)
    {
        var field = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(2, 2, 2, 3) };
        field.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 8f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        field.Controls.Add(input);
        return field;
    }
}
