using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The modern test package ships no retail file (only Marfach Caverns' small map, which 1.130 lacks): on first use the player picks their own DAoC 1.130 folder, it is
/// copied into the package (their install is never touched) and runtime/client-patch.json is applied to the copy
/// (made by source/tools/make_client_patch.py). Only the pristine 1.130 inputs are accepted. An entry with From makes a new
/// file from that stock file; one with Member edits the file inside a single-file archive and rebuilds the archive.
/// </summary>
public static class ModernClientSetup
{
    public const string PatchFile = "client-patch.json";
    // Written first and removed last: a client folder with it is an interrupted setup, redone on the next try.
    public const string IncompleteMarker = "setup-in-progress.txt";
    // The player's own client folder, so a later release can patch the copy again without asking.
    public const string SourceFile = "client-source.txt";

    // The same files source/tools/bundle_modern_client.py leaves out of a client copy.
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "logs", "screenshots", "backups", "cache" };
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
        { "game.laa.dll", "user.dat", "login.dat", "account.txt", "d3d9.dll", "dxvk.conf", "dev_mode.txt" };
    private static readonly string[] ExcludedSuffixes = [".log", ".bak", ".dmp"];

    public sealed record Patch(int Format, string ClientVersion, PatchedFile[] Files);
    public sealed record PatchedFile(string Path, string InputSha256, string OutputSha256, long OutputSize, Edit[] Edits,
        string? From = null, string? Member = null)
    {
        /// <summary>The stock file InputSha256 is the hash of.</summary>
        public string InputPath => From ?? Path;
    }
    public sealed record Edit(long At, long Remove, string Insert);

    public static Patch Load(string runtime)
    {
        var patch = JsonSerializer.Deserialize<Patch>(File.ReadAllText(Path.Combine(runtime, PatchFile)))
            ?? throw new InvalidDataException("The client patch is empty.");
        if (patch.Format != 1 || patch.Files.Length == 0)
            throw new InvalidDataException("This launcher doesn't know the client patch's format.");
        return patch;
    }

    public static bool IsSetUp(ModernClientProfile profile) => File.Exists(Path.Combine(profile.ClientDirectory, "game.dll")) &&
        !File.Exists(Path.Combine(profile.ClientDirectory, IncompleteMarker));

    /// <summary>
    /// The patched files whose copy isn't this patch's output: a newer release copied over an older one leaves the client
    /// copy patched by the older release, since setup only runs once.
    /// </summary>
    public static PatchedFile[] Stale(string client, Patch patch) => patch.Files.Where(f =>
    {
        string path = Path.Combine(client, f.Path);
        return !File.Exists(path) || !Hash(path).Equals(f.OutputSha256, StringComparison.OrdinalIgnoreCase);
    }).ToArray();

    /// <summary>The stale files that can't be patched from the copy itself (it holds an older patch, not the pristine file).</summary>
    public static PatchedFile[] NeedSource(string client, Patch patch) => Stale(client, patch).Where(f =>
    {
        string path = Path.Combine(client, f.InputPath);
        return !File.Exists(path) || !Hash(path).Equals(f.InputSha256, StringComparison.OrdinalIgnoreCase);
    }).ToArray();

    /// <summary>The player's own client folder the copy was made from, when it is still there.</summary>
    public static string? RememberedSource(string client)
    {
        string file = Path.Combine(client, SourceFile);
        if (!File.Exists(file)) return null;
        string source = File.ReadAllText(file).Trim();
        return source.Length > 0 && Directory.Exists(source) ? source : null;
    }

    /// <summary>
    /// Brings an existing client copy to this patch: each stale file is patched again from the pristine file, taken from the
    /// copy when it still is pristine, otherwise from <paramref name="source"/> (the player's own client, never written to).
    /// Every new file is built and verified before any is replaced; the replaced ones are kept as .bak.
    /// </summary>
    public static void Update(string client, Patch patch, string? source)
    {
        client = Path.GetFullPath(client);
        var outputs = new List<(string Target, byte[] Data)>();
        foreach (var file in Stale(client, patch))
        {
            string target = Path.Combine(client, file.Path);
            string input = Path.Combine(client, file.InputPath);
            string from = File.Exists(input) && Hash(input).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase)
                ? input
                : source != null ? Path.Combine(source, file.InputPath)
                : throw new InvalidDataException($"Your client copy's {file.InputPath} is from an older version. Choose your Dark Age of Camelot " +
                                                 $"{patch.ClientVersion} folder so it can be patched again.");
            if (!File.Exists(from) || !Hash(from).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"That client's {file.InputPath} isn't the {patch.ClientVersion} build this game is made for, so it " +
                                               "can't be patched. It may be a newer or modified client. Nothing was changed.");
            outputs.Add((target, Apply(File.ReadAllBytes(from), file)));
        }
        foreach (var (target, data) in outputs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + ".update";
            File.WriteAllBytes(temporary, data);
            if (File.Exists(target))
                File.Replace(temporary, target, target + ".bak");
            else
                File.Move(temporary, target);
        }
        if (Stale(client, patch).FirstOrDefault() is { } wrong)
            throw new InvalidDataException($"The patched {wrong.Path} doesn't verify.");
        if (source != null)
            File.WriteAllText(Path.Combine(client, SourceFile), Path.GetFullPath(source));
    }

    /// <summary>Null when <paramref name="source"/> is a pristine client this patch accepts, otherwise why it isn't.</summary>
    public static string? CheckSource(string source, Patch patch)
    {
        if (!File.Exists(Path.Combine(source, "game.dll")))
            return "That folder has no game.dll. Choose the folder that holds your Dark Age of Camelot client (with game.dll and camelot.exe).";
        foreach (var file in patch.Files)
        {
            string path = Path.Combine(source, file.InputPath);
            if (!File.Exists(path))
                return $"That client has no {file.InputPath}.";
            if (!Hash(path).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase))
                return $"That client's {file.InputPath} isn't the {patch.ClientVersion} build this game is made for, so it can't be patched. " +
                       "It may be a newer or modified client. Nothing was changed.";
        }
        return null;
    }

    /// <summary>The patched file, from the pristine <paramref name="input"/>; both ends are hash-checked.</summary>
    public static byte[] Apply(byte[] input, PatchedFile file)
    {
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{file.InputPath} isn't the pristine client file.");
        byte[] result = file.Member == null ? Splice(input, file) : WithMember(input, Splice(MemberOf(input), file));
        if (result.Length != file.OutputSize ||
            !Convert.ToHexString(SHA256.HashData(result)).Equals(file.OutputSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Patching {file.Path} didn't give the expected file.");
        return result;
    }

    private static byte[] Splice(byte[] input, PatchedFile file)
    {
        using var output = new MemoryStream(input.Length);
        long position = 0;
        foreach (var edit in file.Edits)
        {
            if (edit.At < position || edit.At + edit.Remove > input.Length)
                throw new InvalidDataException($"The client patch for {file.Path} is damaged.");
            output.Write(input, (int)position, (int)(edit.At - position));
            output.Write(Convert.FromBase64String(edit.Insert));
            position = edit.At + edit.Remove;
        }
        output.Write(input, (int)position, (int)(input.Length - position));
        return output.ToArray();
    }

    // A single-file MPAK v2 archive: "MPAK", version, a header of four XOR-masked integers (directory CRC, directory size,
    // name size, file count), then the zlib-compressed name, directory row and file. make_client_patch.py mirrors these.
    private const int ArchiveHeader = 21, RowSizeField = 268;

    private static uint HeaderField(byte[] archive, int index)
    {
        var field = new byte[4];
        for (int i = 0; i < 4; i++)
            field[i] = (byte)(archive[5 + index * 4 + i] ^ (index * 4 + i));
        return BitConverter.ToUInt32(field);
    }

    private static byte[] Inflate(byte[] data, int start, int length)
    {
        using var input = new ZLibStream(new MemoryStream(data, start, length), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] MemberOf(byte[] archive)
    {
        if (archive.Length < ArchiveHeader || !archive.AsSpan(0, 5).SequenceEqual("MPAK\x02"u8) || HeaderField(archive, 3) != 1)
            throw new InvalidDataException("That archive isn't a single-file MPAK.");
        int start = ArchiveHeader + (int)HeaderField(archive, 2) + (int)HeaderField(archive, 1);
        return Inflate(archive, start, archive.Length - start);
    }

    /// <summary>The single-file archive with its file replaced, written with stored blocks; name and timestamp are kept.</summary>
    private static byte[] WithMember(byte[] archive, byte[] data)
    {
        int nameSize = (int)HeaderField(archive, 2);
        byte[] row = Inflate(archive, ArchiveHeader + nameSize, (int)HeaderField(archive, 1));
        byte[] payload = StoredZlib(data);
        BitConverter.TryWriteBytes(row.AsSpan(RowSizeField), data.Length);
        BitConverter.TryWriteBytes(row.AsSpan(RowSizeField + 4), 0);
        BitConverter.TryWriteBytes(row.AsSpan(RowSizeField + 8), payload.Length);
        BitConverter.TryWriteBytes(row.AsSpan(RowSizeField + 12), Crc32(payload));
        byte[] directory = StoredZlib(row);
        using var output = new MemoryStream();
        output.Write(archive, 0, 5);
        uint[] header = [Crc32(directory), (uint)directory.Length, (uint)nameSize, 1];
        for (int index = 0; index < 16; index++)
            output.WriteByte((byte)((header[index / 4] >> (index % 4 * 8)) ^ index));
        output.Write(archive, ArchiveHeader, nameSize);
        output.Write(directory);
        output.Write(payload);
        return output.ToArray();
    }

    // Stored blocks, not real compression: every writer makes exactly these bytes, so the output hash holds.
    private static byte[] StoredZlib(byte[] data)
    {
        const int blockSize = 65535;
        using var output = new MemoryStream();
        output.Write([0x78, 0x01]);
        for (int start = 0; start < data.Length; start += blockSize)
        {
            int length = Math.Min(blockSize, data.Length - start);
            output.WriteByte((byte)(start + blockSize >= data.Length ? 1 : 0));
            output.Write(BitConverter.GetBytes((ushort)length));
            output.Write(BitConverter.GetBytes((ushort)(length ^ 0xFFFF)));
            output.Write(data, start, length);
        }
        uint low = 1, high = 0;
        foreach (byte value in data)
        {
            low = (low + value) % 65521;
            high = (high + low) % 65521;
        }
        output.Write([(byte)(high >> 8), (byte)high, (byte)(low >> 8), (byte)low]);
        return output.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? crc >> 1 ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/> and patches the copy; never writes to the
    /// source. The folder isn't renamed into place (antivirus scanning new files can block a rename): the incomplete
    /// marker is written first and removed once every patched file verifies.
    /// </summary>
    public static void Install(string source, string destination, Patch patch, IProgress<(long Done, long Total, string File)>? progress,
        CancellationToken cancel)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (CheckSource(source, patch) is { } problem)
            throw new InvalidDataException(problem);
        if (Directory.Exists(destination) && !File.Exists(Path.Combine(destination, IncompleteMarker)))
            throw new IOException("A client is already set up here.");
        if (destination.StartsWith(source.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destination.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a client folder outside this game's folder.");
        if (Directory.Exists(destination))
            Directory.Delete(destination, true);  // an earlier setup that was cancelled or failed
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, IncompleteMarker), "Client setup did not finish; it is redone on the next start.");

        var files = Files(source).ToList();
        long total = files.Sum(f => new FileInfo(f).Length), done = 0;
        var patched = patch.Files.Where(f => f.From == null).ToDictionary(f => f.Path.Replace('/', '\\'), StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            cancel.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, path);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            progress?.Report((done, total, relative));
            if (patched.TryGetValue(relative, out var file))
                File.WriteAllBytes(target, Apply(File.ReadAllBytes(path), file));
            else
                File.Copy(path, target);
            done += new FileInfo(path).Length;
        }
        foreach (var file in patch.Files.Where(f => f.From != null))
        {
            string target = Path.Combine(destination, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, Apply(File.ReadAllBytes(Path.Combine(source, file.InputPath)), file));
        }
        foreach (var file in patch.Files)
            if (!Hash(Path.Combine(destination, file.Path)).Equals(file.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The patched {file.Path} doesn't verify.");
        File.WriteAllText(Path.Combine(destination, SourceFile), source);
        File.Delete(Path.Combine(destination, IncompleteMarker));
        progress?.Report((total, total, ""));
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);
            if (!ExcludedNames.Contains(name) && !ExcludedSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) &&
                !new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint))
                yield return file;
        }
        foreach (string sub in Directory.EnumerateDirectories(directory))
            if (!ExcludedDirectories.Contains(Path.GetFileName(sub)) && !new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint))
                foreach (string file in Files(sub))
                    yield return file;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

/// <summary>
/// The first-run window: choose your 1.130 client, then copy and patch it with a progress bar. In update mode (the copy
/// holds an older release's patch) it only patches the changed files again from the chosen client.
/// </summary>
public sealed class ModernClientSetupForm : Form
{
    private readonly ModernClientProfile _profile;
    private readonly ModernClientSetup.Patch _patch;
    private readonly bool _update;
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button _browse = new() { Text = "Browse…", AutoSize = true };
    private readonly Button _start = new() { Text = "Set up client", AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private CancellationTokenSource? _running;

    private ModernClientSetupForm(ModernClientProfile profile, ModernClientSetup.Patch patch, bool update)
    {
        _profile = profile;
        _patch = patch;
        _update = update;
        Text = update ? "Update your game's client copy" : "Set up your Dark Age of Camelot client";
        if (update) _start.Text = "Update client";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 230);
        var intro = new Label
        {
            Dock = DockStyle.Fill,
            Text = update
                ? "This version of the game patches the client differently from the one your client copy was set up with. Choose your " +
                  $"own Dark Age of Camelot {patch.ClientVersion} folder (the one with game.dll and camelot.exe) so the changed files can " +
                  "be patched again. Nothing is copied again and your own client is not changed."
                : $"This game plays with your own Dark Age of Camelot {patch.ClientVersion} client. Choose its folder (the one with " +
                  "game.dll and camelot.exe). It is copied into this game's folder (about 6 GB) and the copy is patched for the " +
                  "new classes. Your own client is not changed."
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 2);
        layout.Controls.Add(_folder, 0, 1);
        layout.Controls.Add(_browse, 1, 1);
        layout.Controls.Add(_bar, 0, 2);
        layout.SetColumnSpan(_bar, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.SetColumnSpan(_status, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_start);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        _browse.Click += (_, _) => Browse();
        _start.Click += async (_, _) => await RunSetup();
        _cancel.Click += (_, _) => { if (_running != null) _running.Cancel(); else Close(); };
        FormClosing += (_, e) => { if (_running != null) { _running.Cancel(); e.Cancel = true; } };
    }

    /// <summary>
    /// True once the client copy is set up and matches this release's patch. Sets it up on first use; brings an older
    /// release's copy up to date, without asking when the copy or the remembered client folder has the pristine files.
    /// </summary>
    public static bool EnsureSetUp(IWin32Window owner, ModernClientProfile profile, string runtime)
    {
        bool setUp = ModernClientSetup.IsSetUp(profile);
        ModernClientSetup.Patch patch;
        try { patch = ModernClientSetup.Load(runtime); }
        catch (Exception ex)
        {
            if (setUp) return true;  // nothing to compare against; the start check still verifies game.dll
            MessageBox.Show(owner, ex.Message, "Client patch missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        string client = profile.ClientDirectory;
        if (setUp)
        {
            if (ModernClientSetup.Stale(client, patch).Length == 0)
                return true;
            string? remembered = ModernClientSetup.RememberedSource(client);
            if (ModernClientSetup.NeedSource(client, patch).Length == 0 || remembered != null && ModernClientSetup.CheckSource(remembered, patch) == null)
            {
                try
                {
                    ModernClientSetup.Update(client, patch, remembered);
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, ex.Message, "Client update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
        }
        using var form = new ModernClientSetupForm(profile, patch, setUp);
        form.ShowDialog(owner);
        return ModernClientSetup.IsSetUp(profile) && ModernClientSetup.Stale(client, patch).Length == 0;
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Your Dark Age of Camelot client folder", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _folder.Text = dialog.SelectedPath;
        string? problem = ModernClientSetup.CheckSource(dialog.SelectedPath, _patch);
        _status.Text = problem ?? "That client can be set up.";
        _start.Enabled = problem == null;
    }

    private async Task RunSetup()
    {
        _start.Enabled = _browse.Enabled = false;
        _running = new CancellationTokenSource();
        var progress = new Progress<(long Done, long Total, string File)>(p =>
        {
            _bar.Value = p.Total == 0 ? 0 : (int)(1000 * p.Done / p.Total);
            _status.Text = p.File.Length == 0 ? "Done." : $"Copying {p.File}";
        });
        string source = _folder.Text, destination = _profile.ClientDirectory;
        try
        {
            if (_update)
            {
                _status.Text = "Patching the changed client files…";
                await Task.Run(() => ModernClientSetup.Update(destination, _patch, source));
            }
            else
                await Task.Run(() => ModernClientSetup.Install(source, destination, _patch, progress, _running.Token));
            _running = null;
            MessageBox.Show(this, _update ? "Your client copy is up to date. Use ENTER REALM to play." : "Your client is set up. Use ENTER REALM to play.",
                "Client ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Setup was cancelled. Nothing was set up.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _running = null;
            _start.Enabled = _browse.Enabled = true;
        }
    }
}
