using System.ComponentModel;
using System.Diagnostics;

namespace OfflineDaoc.Launcher;

/// <summary>The host's side of playing together: open this server to friends, and the firewall rules for it.</summary>
internal sealed class HostForFriendsForm : Form
{
    private readonly string _runtime;
    private readonly Func<bool> _serverRunning;
    private readonly Label _state = Line(13f, FontStyle.Bold, DaocTheme.GoldLight);
    private readonly Label _address = Line(10f, FontStyle.Bold, DaocTheme.Text);
    private readonly Label _firewall = Line(9f, FontStyle.Regular, DaocTheme.Parchment);
    private readonly Label _status = Line(8.5f, FontStyle.Regular, DaocTheme.GoldLight);
    private readonly Button _toggle = Action("TURN ON", Color.FromArgb(110, 128, 70));
    private readonly Button _firewallButton = Action("ADD FIREWALL RULES", Color.FromArgb(65, 94, 145));
    private readonly Button _copy = Action("COPY ADDRESS", Color.FromArgb(92, 84, 128));
    private readonly Button _close = Action("CLOSE", DaocTheme.Iron);
    private HostForFriends.State _current = new(false, 10300, 10400, true);
    private bool _rulesPresent;

    public HostForFriendsForm(string runtime, Func<bool> serverRunning)
    {
        _runtime = runtime;
        _serverRunning = serverRunning;

        Text = "Host for Friends — Divided Realms";
        ClientSize = new Size(640, 470);
        MinimumSize = new Size(600, 450);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 8.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var frame = new StoneSurface { Dock = DockStyle.Fill, Padding = new Padding(14) };
        Controls.Add(frame);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Color.Transparent, Padding = new Padding(4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 38f, 30f, 46f, 112f, 46f })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        layout.Controls.Add(new Label
        {
            Text = "HOST FOR FRIENDS",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        layout.Controls.Add(_state, 0, 1);
        layout.Controls.Add(_address, 0, 2);
        layout.Controls.Add(new Label
        {
            Text = "1. Both of you install Tailscale and share this PC with your friend (or join one tailnet).\n" +
                   "2. Turn hosting on, add the firewall rules once (Windows asks for permission), then start the server.\n" +
                   "3. Your friend opens JOIN FRIEND in their launcher with the address above and picks an account name " +
                   "and password for your server. Their account and characters are saved in YOUR world.\n" +
                   "Nothing is opened on your router; only Tailscale and your home network can reach the server.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 8.5f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 3);
        layout.Controls.Add(_firewall, 0, 4);
        layout.Controls.Add(_status, 0, 5);

        var row1 = Buttons();
        row1.Controls.Add(_firewallButton);
        row1.Controls.Add(_toggle);
        layout.Controls.Add(row1, 0, 6);
        var row2 = Buttons();
        row2.Controls.Add(_close);
        row2.Controls.Add(_copy);
        layout.Controls.Add(row2, 0, 7);
        frame.Controls.Add(layout);

        _toggle.Click += (_, _) => Toggle();
        _firewallButton.Click += async (_, _) => await ChangeFirewallAsync();
        _copy.Click += (_, _) => CopyAddress();
        _close.Click += (_, _) => Close();
        CancelButton = _close;
        Shown += async (_, _) => await RefreshStateAsync();
    }

    private async Task RefreshStateAsync()
    {
        try
        {
            _current = HostForFriends.Read(_runtime);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not read serverconfig.xml: {exception.Message}", DaocTheme.Danger);
            _toggle.Enabled = _firewallButton.Enabled = false;
            return;
        }
        _state.Text = _current.Hosting ? "Hosting is ON: friends can join while your server runs." : "Hosting is OFF: only this PC can join your server.";
        _state.ForeColor = _current.Hosting ? DaocTheme.Success : DaocTheme.GoldLight;
        _toggle.Text = _current.Hosting ? "TURN OFF" : "TURN ON";

        var tailscale = HostForFriends.TailscaleAddresses();
        _address.Text = tailscale.Count > 0
            ? $"Friends join at  {string.Join("  or  ", tailscale)}   (login port {_current.LoginPort})"
            : "Tailscale is not running on this PC. Install it from tailscale.com, or friends in your house can use this PC's home-network address.";
        _address.ForeColor = tailscale.Count > 0 ? DaocTheme.Text : DaocTheme.Danger;
        _copy.Enabled = tailscale.Count > 0;

        _rulesPresent = await RulesPresentAsync();
        if (IsDisposed)
            return;
        _firewall.Text = _rulesPresent
            ? $"Firewall: friends' rules are in place (TCP {_current.LoginPort}, UDP {_current.UdpPort}, Tailscale and home network only)."
            : $"Firewall: no rules yet. Friends cannot connect until you add them (TCP {_current.LoginPort}, UDP {_current.UdpPort}).";
        _firewall.ForeColor = _rulesPresent ? DaocTheme.Success : DaocTheme.Parchment;
        _firewallButton.Text = _rulesPresent ? "REMOVE FIREWALL RULES" : "ADD FIREWALL RULES";
    }

    private void Toggle()
    {
        try
        {
            if (_current.Hosting)
                HostForFriends.Disable(_runtime);
            else
                HostForFriends.Enable(_runtime);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not change serverconfig.xml: {exception.Message}", DaocTheme.Danger);
            return;
        }
        bool nowHosting = !_current.Hosting;
        string restart = _serverRunning() ? " Stop and start the server for it to take effect." : string.Empty;
        SetStatus((nowHosting
            ? $"Hosting turned on. The previous settings are saved as server\\config\\{HostForFriends.BackupFileName}."
            : "Hosting turned off: the server listens on this PC only again.") + restart, DaocTheme.Success);
        _ = RefreshStateAsync();
    }

    private async Task ChangeFirewallAsync()
    {
        bool add = !_rulesPresent;
        string script = Path.Combine(_runtime, "host-for-friends-firewall.cmd");
        try
        {
            File.WriteAllText(script, HostForFriends.FirewallScript(_runtime, _current, add));
            using Process? process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", script },
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process != null)
                await process.WaitForExitAsync();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            SetStatus("Windows permission was declined; the firewall was not changed.", DaocTheme.Danger);
            return;
        }
        catch (Exception exception)
        {
            SetStatus($"Could not change the firewall: {exception.Message}", DaocTheme.Danger);
            return;
        }
        await RefreshStateAsync();
        SetStatus(_rulesPresent == add
            ? (add ? "Firewall rules added." : "Firewall rules removed.")
            : $"The firewall did not change. You can run {script} as administrator yourself.", _rulesPresent == add ? DaocTheme.Success : DaocTheme.Danger);
    }

    private async Task<bool> RulesPresentAsync()
    {
        try
        {
            foreach (string protocol in new[] { "TCP", "UDP" })
            {
                using Process? process = Process.Start(new ProcessStartInfo("netsh")
                {
                    ArgumentList = { "advfirewall", "firewall", "show", "rule", $"name={HostForFriends.FirewallRuleName(_current, protocol)}" },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                });
                if (process == null)
                    return false;
                await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private void CopyAddress()
    {
        var tailscale = HostForFriends.TailscaleAddresses();
        if (tailscale.Count == 0)
            return;
        Clipboard.SetText(tailscale[0].ToString());
        SetStatus($"Copied {tailscale[0]}. Send it to your friend for JOIN FRIEND.", DaocTheme.Success);
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static Label Line(float size, FontStyle style, Color color) => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = color,
        Font = new Font("Georgia", size, style),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Button Action(string text, Color accent) => new RuneButton
    {
        Text = text,
        Width = 210,
        Height = 34,
        Accent = accent,
        Margin = new Padding(4),
    };

    private static FlowLayoutPanel Buttons() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
        BackColor = Color.Transparent,
    };
}
