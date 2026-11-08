using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher;

// Divided Realms updates in the launcher's window (the work itself is in Updates/):
// - Options > Update and repair: CHECK FOR UPDATES, the installed release, and "Check automatically when the launcher
//   starts" (at most once a day; on by default);
// - a newer release found (on start or by the button) is offered in a dialog: Install, Later or Skip this version;
// - Install only with the server, the game and any other launcher closed: the package is downloaded with progress
//   (resumed and retried), checked, unpacked, and the launcher closes while its helper copies it over the game folder,
//   then opens again. Nothing installs without the player's click. A folder that isn't a release package (the
//   developer's test install) never checks or updates.
// Ported from Living Server's MainForm.Updates (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3), with our dialog
// on start instead of their GET UPDATE button.
internal sealed partial class MainForm
{
    private LauncherUpdater? _updater;
    private HttpReleaseWeb? _releaseWeb;
    private bool _updating;
    private bool _checkingUpdates;
    private Button? _checkUpdatesButton;
    private readonly CheckBox _autoUpdateCheck = new()
    {
        AutoSize = true,
        Text = "Check automatically when the launcher starts (at most once a day)",
        ForeColor = DaocTheme.Muted,
        Font = new Font("Georgia", 8.5f),
        Margin = new Padding(0, 2, 0, 0),
    };

    private LauncherUpdater Updater => _updater ??= new LauncherUpdater(ReleaseFolder.GameFolderFor(_root),
        _releaseWeb = new HttpReleaseWeb(LauncherUpdater.UserAgent));

    private Control BuildUpdateCheckDescription()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        InstalledRelease? installed = Updater.Installed;
        var text = XpRateDescription(installed != null
            ? $"You have Divided Realms {installed.Version.Tag}. Looks for a newer release on GitHub and installs it here: your " +
              "characters, world and settings are kept."
            : "Updates install only into a folder unpacked from a Divided Realms release download, and this folder isn't one.");
        if (installed == null)
            _helpTip.SetToolTip(text, "Why: " + Updater.NotUpdatable + ".");
        text.Dock = DockStyle.None;
        text.AutoSize = true;
        text.MaximumSize = new Size(560, 0);
        text.Margin = new Padding(0, 4, 0, 2);
        panel.Controls.Add(text);
        panel.Controls.Add(_autoUpdateCheck);
        _autoUpdateCheck.Checked = Updater.Settings.CheckAutomatically;
        _autoUpdateCheck.Enabled = installed != null;
        _autoUpdateCheck.CheckedChanged += (_, _) =>
        {
            Updater.Settings.CheckAutomatically = _autoUpdateCheck.Checked;
            Updater.SaveSettings();
            Updater.Log.Write("Automatic update checks " + (_autoUpdateCheck.Checked ? "on." : "off."));
        };
        SetToolTip(_autoUpdateCheck, "When the launcher starts, ask GitHub at most once a day whether a newer Divided Realms is out. " +
                                     "Only the list of releases is asked for; nothing about you or your game is sent.");
        if (_checkUpdatesButton != null && installed == null)
            _checkUpdatesButton.Enabled = false;
        return panel;
    }

    private void SetUpdateStatus(string text, Color color)
    {
        _updateRepairStatus.Text = text;
        _updateRepairStatus.ForeColor = color;
    }

    private void ShowLastUpdateResult()
    {
        IReadOnlyDictionary<string, string>? last = Updater.TakeLastResult();
        if (last == null || !last.TryGetValue("Version", out string? version))
            return;
        bool installed = last.TryGetValue("Result", out string? result) && result == "installed";
        string detail = last.TryGetValue("Detail", out string? d) ? d : "";
        Updater.Log.Write($"Opened after the update to {version}: {result} ({detail})");
        SetUpdateStatus(installed ? $"Updated to Divided Realms {version}. Your characters and world are as you left them."
                                  : $"The update to {version} didn't install, and the game is as it was.", installed ? DaocTheme.Success : DaocTheme.Danger);
        if (!installed)
            MessageBox.Show(this, $"The update to {version} didn't install:\n{detail}\n\nThe game is as it was. Details are in runtime\\logs\\launcher-updates.log.",
                "Divided Realms update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Runs after the window opens (asked: false) and from the button (asked: true). Silent on failure unless asked.</summary>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        if (_updating || _checkingUpdates)
            return;
        if (Updater.Installed == null)
        {
            if (asked)
                SetUpdateStatus("This folder doesn't update itself: " + Updater.NotUpdatable + ".", DaocTheme.Muted);
            return;
        }
        if (!asked && !UpdatePolicy.ShouldCheck(Updater.Settings, DateTime.UtcNow, false))
            return;
        _checkingUpdates = true;
        if (_checkUpdatesButton != null) _checkUpdatesButton.Enabled = false;
        if (asked)
            SetUpdateStatus("Asking GitHub for the newest Divided Realms…", DaocTheme.Muted);
        try
        {
            var (offer, problem) = await Task.Run(() =>
            {
                ReleaseInfo? found = Updater.Check(asked, DateTime.UtcNow, out string? why);
                return (found, why);
            });
            if (offer != null)
            {
                SetUpdateStatus($"Divided Realms {offer.Version!.Tag} is out.", DaocTheme.GoldLight);
                ShowUpdateDialog(offer);
            }
            else if (asked)
            {
                SetUpdateStatus(problem != null
                    ? "Couldn't reach GitHub (" + problem + "). Try again later."
                    : $"You have the newest Divided Realms ({Updater.Installed.Version.Tag}).", problem != null ? DaocTheme.Danger : DaocTheme.Muted);
            }
        }
        catch (Exception e)
        {
            Updater.Log.Write("Update check failed: " + e.Message);
            if (asked)
                SetUpdateStatus("Couldn't check for updates: " + e.Message, DaocTheme.Danger);
        }
        finally
        {
            _checkingUpdates = false;
            if (_checkUpdatesButton != null) _checkUpdatesButton.Enabled = !_updating;
        }
    }

    private void ShowUpdateDialog(ReleaseInfo release)
    {
        if (release.Version == null || _updating || IsDisposed)
            return;
        string tag = release.Version.Tag;
        var install = new TaskDialogButton("Install");
        var later = new TaskDialogButton("Later");
        var skip = new TaskDialogButton("Skip this version");
        var notes = release.NotesPreview(6);
        var page = new TaskDialogPage
        {
            Caption = "Divided Realms update",
            Heading = $"Divided Realms {tag} is out. Install it? Your characters and world are kept.",
            Text = (release.Title is { Length: > 0 } title ? title + Environment.NewLine + Environment.NewLine : "") +
                   (notes.Count > 1 ? string.Join(Environment.NewLine, notes.Skip(1).Take(4)) + Environment.NewLine + Environment.NewLine : "") +
                   $"You have {Updater.Installed?.Version.Tag}. The launcher downloads {release.Package?.Name} ({(release.Package?.Size ?? 0) / 1073741824.0:0.00} GB) " +
                   "from GitHub and checks it, then closes while it copies the new files over this folder (every file it replaces is backed up), " +
                   "and opens again. Your world gets this release's fixes on the next start.",
            Footnote = release.PageUrl != null ? new TaskDialogFootnote("Release notes: " + release.PageUrl) : null,
            Buttons = { install, later, skip },
            DefaultButton = install,
            Icon = TaskDialogIcon.Information,
            AllowCancel = true,
        };
        TaskDialogButton chosen = TaskDialog.ShowDialog(this, page);
        if (chosen == install)
        {
            _ = ApplyUpdateAsync(release);
        }
        else if (chosen == skip)
        {
            Updater.Skip(release.Version);
            SetUpdateStatus($"Skipped {tag}. CHECK FOR UPDATES shows it again.", DaocTheme.Muted);
        }
        else
        {
            Updater.Log.Write("The player chose Later for " + tag + ".");
            SetUpdateStatus($"Divided Realms {tag} is out: CHECK FOR UPDATES installs it.", DaocTheme.GoldLight);
        }
    }

    private List<string> RunningGameParts() =>
        LauncherUpdater.RunningParts(Environment.ProcessId, IsServerRunning() || FindExactServerProcess() is not null);

    private async Task ApplyUpdateAsync(ReleaseInfo release)
    {
        string tag = release.Version!.Tag;
        List<string> running = RunningGameParts();
        if (running.Count > 0)
        {
            Updater.Log.Write("Not updating while running: " + string.Join(", ", running));
            MessageBox.Show(this, $"Close {string.Join(", ", running)} first: Divided Realms is never updated while the game runs.",
                "Divided Realms update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _updating = true;
        SetUpdateRepairBusy(true);
        using var cancel = new CancellationTokenSource();
        using var window = new UpdateDownloadWindow("Divided Realms " + tag, cancel);
        Task<PreparedUpdate> work = Task.Run(() => Updater.PrepareFromRelease(release, (phase, done, total) =>
            window.Report(phase == "download" ? "Downloading" : "Unpacking and checking", done, total), cancel.Token));
        window.Shown += async (_, _) =>
        {
            try { await work; }
            catch (Exception) { /* reported below */ }
            window.Close();
        };
        window.ShowDialog(this);
        try
        {
            PreparedUpdate prepared = await work;
            running = RunningGameParts();
            if (running.Count > 0)
                throw new PackageRefusedException($"{string.Join(", ", running)} started during the download. Close it and install again; the download is kept.");
            SetUpdateStatus($"Installing {tag}: the launcher closes now and opens again when it's done.", DaocTheme.GoldLight);
            Updater.StartHandoff(prepared, Environment.ProcessId, restart: true, quiet: false, AppContext.BaseDirectory,
                _modernClient?.Port ?? 10330).Dispose();
            Updater.Log.Write("Closing the launcher for the install.");
            Close();
        }
        catch (Exception e)
        {
            string why = e is PackageRefusedException ? e.Message
                : e is OperationCanceledException ? "You stopped the download. What arrived is kept, and the next Install continues it."
                : "The download didn't finish: " + ReleaseFeed.Innermost(e).Message + " What arrived is kept, and the next Install continues it.";
            Updater.Log.Write("Update not applied: " + why);
            SetUpdateStatus($"{tag} wasn't installed. Nothing in your game was changed.", DaocTheme.Danger);
            if (e is not OperationCanceledException)
                MessageBox.Show(this, why + Environment.NewLine + Environment.NewLine + "Nothing in your game was changed.",
                    "Divided Realms update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _updating = false;
            SetUpdateRepairBusy(false);
        }
    }
}

/// <summary>The download's window: what it is doing, how far, and Cancel.</summary>
internal sealed class UpdateDownloadWindow : Form
{
    private readonly Label _text = new() { Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DaocTheme.Parchment };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 18, Maximum = 1000 };

    public UpdateDownloadWindow(string title, CancellationTokenSource cancel)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 120);
        BackColor = DaocTheme.Panel;
        Font = new Font("Georgia", 9f);
        Padding = new Padding(14, 8, 14, 8);
        var stop = new Button { Text = "Cancel", Dock = DockStyle.Bottom, Height = 28, ForeColor = DaocTheme.Text, FlatStyle = FlatStyle.Flat };
        stop.Click += (_, _) =>
        {
            stop.Enabled = false;
            _text.Text = "Stopping…";
            cancel.Cancel();
        };
        Controls.Add(_bar);
        Controls.Add(_text);
        Controls.Add(stop);
        _text.Text = "Starting the download…";
    }

    public void Report(string what, long done, long total)
    {
        if (!IsHandleCreated || IsDisposed)
            return;
        BeginInvoke(() =>
        {
            _text.Text = total > 0 ? $"{what}: {done / 1048576.0:N0} of {total / 1048576.0:N0} MB" : $"{what}: {done / 1048576.0:N0} MB";
            _bar.Value = total > 0 ? (int)Math.Clamp(done * 1000 / total, 0, 1000) : 0;
        });
    }
}
