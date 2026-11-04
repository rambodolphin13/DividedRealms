using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The modern test package ships no retail file (only Marfach Caverns' small map, which 1.130 lacks): on first use the player picks their own DAoC 1.130 folder, it is
/// copied into the package (their install is never touched) and runtime/client-patch.json is applied to the copy
/// (made by source/tools/make_client_patch.py). Only the pristine 1.130 inputs are accepted. An entry with From makes a new
/// file from that stock file; one with Member edits that file inside an archive and rebuilds the archive.
/// </summary>
public static class ModernClientSetup
{
    public const string PatchFile = "client-patch.json";
    // Written first and removed last: a client folder with it is an interrupted setup, redone on the next try.
    public const string IncompleteMarker = "setup-in-progress.txt";
    // The player's own client folder, so a later release can patch the copy again without asking.
    public const string SourceFile = "client-source.txt";
    // The Eden client's own DLL: a folder with it has Eden's versions of game files the patch needs stock.
    public const string EdenMarker = "eden.dll";

    // The same files source/tools/bundle_modern_client.py leaves out of a client copy.
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "logs", "screenshots", "backups", "cache" };
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
        { "game.laa.dll", "user.dat", "login.dat", "account.txt", "d3d9.dll", "dxvk.conf", "dev_mode.txt" };
    private static readonly string[] ExcludedSuffixes = [".log", ".bak", ".dmp"];

    public sealed record Patch(int Format, string ClientVersion, PatchedFile[] Files);
    public sealed record PatchedFile(string Path, string InputSha256, string OutputSha256, long OutputSize, Edit[] Edits,
        string? From = null, string? Member = null, MemberEdits[]? Members = null, bool AnyBuild = false)
    {
        /// <summary>The stock file InputSha256 is the hash of.</summary>
        public string InputPath => From ?? Path;
    }
    public sealed record Edit(long At, long Remove, string Insert);
    // Rows: the whole catalog lines the edits add, placed by ID when the archive isn't the stock one (AnyBuild).
    public sealed record MemberEdits(string Member, Edit[] Edits, string[]? Rows = null);

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
    public static PatchedFile[] Stale(string client, Patch patch) => patch.Files.Where(f => !IsCurrent(Path.Combine(client, f.Path), f)).ToArray();

    /// <summary>
    /// The file is this patch's output; a file of any build counts once it is as patched as it can be (our rows are in it,
    /// or one of our IDs holds another row there).
    /// </summary>
    private static bool IsCurrent(string path, PatchedFile file)
    {
        if (!File.Exists(path))
            return false;
        if (Hash(path).Equals(file.OutputSha256, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!file.AnyBuild)
            return false;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            return Adapt(data, file).AsSpan().SequenceEqual(data);
        }
        catch (Exception ex) when (ex is not IOException) { return false; }
    }

    /// <summary>The stale files that can't be patched from the copy itself (it holds an older patch, not the pristine file).</summary>
    public static PatchedFile[] NeedSource(string client, Patch patch) => Stale(client, patch).Where(f =>
    {
        string path = Path.Combine(client, f.InputPath);
        return !File.Exists(path) || !f.AnyBuild && !Hash(path).Equals(f.InputSha256, StringComparison.OrdinalIgnoreCase);
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
            string from = File.Exists(input) && (file.AnyBuild || Hash(input).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase))
                ? input
                : source != null ? Path.Combine(source, file.InputPath)
                : throw new InvalidDataException($"Your client copy's {file.InputPath} is from an older version. Choose your Dark Age of Camelot " +
                                                 $"{patch.ClientVersion} folder so it can be patched again.");
            if (!File.Exists(from) || !file.AnyBuild && !Hash(from).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Repairs a client copy from the player's own client (<paramref name="source"/>, only read): every patched file is built
    /// again from its pristine file, and every other game file that is missing or differs from the player's client is copied
    /// again. Files only the copy has (the game's settings and character layouts, user.dat) are left alone. Every repaired
    /// file is written beside its target and then swapped in. Returns how many files were patched again and restored.
    /// </summary>
    public static (int Repatched, int Restored) Repair(string client, string source, Patch patch,
        IProgress<(long Done, long Total, string File)>? progress, CancellationToken cancel)
    {
        client = Path.GetFullPath(client);
        source = Path.GetFullPath(source);
        if (CheckSource(source, patch) is { } problem)
            throw new InvalidDataException(problem);
        var patched = new HashSet<string>(patch.Files.Where(f => f.From == null).Select(f => f.Path.Replace('/', '\\')),
            StringComparer.OrdinalIgnoreCase);
        var files = Files(source).ToList();
        long total = files.Sum(f => new FileInfo(f).Length), done = 0;
        int restored = 0, repatched = 0;
        foreach (string path in files)
        {
            cancel.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, path);
            progress?.Report((done, total, relative));
            done += new FileInfo(path).Length;
            if (patched.Contains(relative))
                continue;
            string target = Path.Combine(client, relative);
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(path).Length &&
                Hash(target).Equals(Hash(path), StringComparison.OrdinalIgnoreCase))
                continue;
            Swap(target, File.ReadAllBytes(path));
            restored++;
        }
        foreach (var file in patch.Files)
        {
            cancel.ThrowIfCancellationRequested();
            string target = Path.Combine(client, file.Path);
            if (IsCurrent(target, file))
                continue;
            Swap(target, Apply(File.ReadAllBytes(Path.Combine(source, file.InputPath)), file));
            repatched++;
        }
        if (Stale(client, patch).FirstOrDefault() is { } wrong)
            throw new InvalidDataException($"The patched {wrong.Path} doesn't verify.");
        File.WriteAllText(Path.Combine(client, SourceFile), source);
        File.Delete(Path.Combine(client, IncompleteMarker));
        progress?.Report((total, total, ""));
        return (repatched, restored);
    }

    private static void Swap(string target, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".repair";
        File.WriteAllBytes(temporary, data);
        if (File.Exists(target))
            File.Replace(temporary, target, null);
        else
            File.Move(temporary, target);
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
            if (file.AnyBuild)
            {
                // Any build of this file is patched (a later live update or another client changes it often); it only has to be readable.
                try { Adapt(File.ReadAllBytes(path), file); }
                catch (Exception ex) when (ex is not IOException) { return $"That client's {file.InputPath} can't be read. Nothing was changed."; }
                continue;
            }
            if (!Hash(path).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase))
                return File.Exists(Path.Combine(source, EdenMarker))
                    ? $"That Dark Age of Camelot folder was changed by the Eden client: its {file.InputPath} (and other files) are Eden's " +
                      "versions, and the live patcher doesn't put the originals back. Choose a folder with an unchanged " +
                      $"{patch.ClientVersion} client, or install Dark Age of Camelot again into a new folder and choose that one. " +
                      "Nothing was changed."
                    : $"That client's {file.InputPath} isn't the {patch.ClientVersion} build this game is made for, so it can't be patched. " +
                      "It may be a newer or modified client. Nothing was changed.";
        }
        return null;
    }

    /// <summary>The patched file, from the pristine <paramref name="input"/>; both ends are hash-checked.</summary>
    public static byte[] Apply(byte[] input, PatchedFile file)
    {
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(file.InputSha256, StringComparison.OrdinalIgnoreCase))
            return file.AnyBuild ? Adapt(input, file) : throw new InvalidDataException($"{file.InputPath} isn't the pristine client file.");
        byte[] result;
        if (file.Members is { Length: > 0 })
        {
            // Several files inside one archive (the fishing rod's catalog rows), edited in order over the archive so far.
            result = input;
            foreach (var part in file.Members)
                result = WithMember(result, Splice(MemberOf(result, part.Member), part.Edits, file.Path), part.Member);
        }
        else
            result = file.Member == null ? Splice(input, file) : WithMember(input, Splice(MemberOf(input, file.Member), file), file.Member);
        if (result.Length != file.OutputSize ||
            !Convert.ToHexString(SHA256.HashData(result)).Equals(file.OutputSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Patching {file.Path} didn't give the expected file.");
        return result;
    }

    /// <summary>
    /// A file of another build (a later live gamedata.mpk, or one another client changed) gets our catalog rows by ID, as
    /// make_client_patch.py placed them in the stock one, instead of by offset. Rows already there are kept. When one of our
    /// IDs holds another row the whole file is left as it is (the rod then has no model) rather than taking that ID.
    /// </summary>
    public static byte[] Adapt(byte[] input, PatchedFile file)
    {
        byte[] result = input;
        foreach (var part in file.Members ?? [])
        {
            string table = System.Text.Encoding.Latin1.GetString(MemberOf(result, part.Member));
            string? added = WithRows(table, part.Rows ?? throw new InvalidDataException($"The client patch for {file.Path} has no rows."));
            if (added == null)
                return input;
            if (added != table)
                result = WithMember(result, System.Text.Encoding.Latin1.GetBytes(added), part.Member);
        }
        return result;
    }

    /// <summary>The catalog with each row added before the first higher ID (tools/pet-art add_row); null when an ID holds another row.</summary>
    private static string? WithRows(string table, string[] rows)
    {
        var lines = table.Split("\r\n").ToList();
        foreach (string row in rows)
        {
            long id = IdOf(row) ?? throw new InvalidDataException("A client patch row has no ID.");
            var numbered = lines.Select((line, at) => (At: at, Id: IdOf(line))).Where(l => l.Id != null).ToList();
            if (numbered.Count == 0)
                return null;
            if (numbered.Where(l => l.Id == id).Select(l => lines[l.At]).ToList() is { Count: > 0 } same)
            {
                if (same.Any(line => line != row))
                    return null;
                continue;
            }
            int before = numbered.Where(l => l.Id > id).Select(l => l.At).DefaultIfEmpty(numbered[^1].At + 1).First();
            lines.Insert(before, row);
        }
        return string.Join("\r\n", lines);
    }

    private static long? IdOf(string line)
    {
        string first = line.Split(',', 2)[0].Trim();
        return first.Length > 0 && first.All(char.IsAsciiDigit) && long.TryParse(first, out long id) ? id : null;
    }

    private static byte[] Splice(byte[] input, PatchedFile file) => Splice(input, file.Edits, file.Path);

    private static byte[] Splice(byte[] input, Edit[] edits, string path)
    {
        using var output = new MemoryStream(input.Length);
        long position = 0;
        foreach (var edit in edits)
        {
            if (edit.At < position || edit.At + edit.Remove > input.Length)
                throw new InvalidDataException($"The client patch for {path} is damaged.");
            output.Write(input, (int)position, (int)(edit.At - position));
            output.Write(Convert.FromBase64String(edit.Insert));
            position = edit.At + edit.Remove;
        }
        output.Write(input, (int)position, (int)(input.Length - position));
        return output.ToArray();
    }

    // An MPAK v2 archive: "MPAK", version, a header of four XOR-masked integers (directory CRC, directory size, name size,
    // file count), then the zlib-compressed name, directory rows and files. make_client_patch.py mirrors these.
    private const int ArchiveHeader = 21, RowSize = 284, RowSizeField = 268;

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

    private static byte[] DirectoryOf(byte[] archive)
    {
        if (archive.Length < ArchiveHeader || !archive.AsSpan(0, 5).SequenceEqual("MPAK\x02"u8))
            throw new InvalidDataException("That archive isn't an MPAK v2 archive.");
        return Inflate(archive, ArchiveHeader + (int)HeaderField(archive, 2), (int)HeaderField(archive, 1));
    }

    private static int DataStart(byte[] archive) => ArchiveHeader + (int)HeaderField(archive, 2) + (int)HeaderField(archive, 1);

    private static string RowName(byte[] directory, int row)
    {
        var name = directory.AsSpan(row * RowSize, 256);
        int end = name.IndexOf((byte)0);
        return System.Text.Encoding.Latin1.GetString(end < 0 ? name : name[..end]);
    }

    private static int RowOf(byte[] directory, int count, string member)
    {
        int[] found = Enumerable.Range(0, count).Where(row => RowName(directory, row).Equals(member, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (found.Length != 1)
            throw new InvalidDataException($"That archive has no single {member}.");
        return found[0];
    }

    private static byte[] MemberOf(byte[] archive, string member)
    {
        byte[] directory = DirectoryOf(archive);
        int row = RowOf(directory, (int)HeaderField(archive, 3), member) * RowSize + RowSizeField;
        int offset = BitConverter.ToInt32(directory, row + 4), packed = BitConverter.ToInt32(directory, row + 8);
        return Inflate(archive, DataStart(archive) + offset, packed);
    }

    /// <summary>The archive with one file replaced, written with stored blocks; every name, timestamp and other file is kept.</summary>
    private static byte[] WithMember(byte[] archive, byte[] data, string member)
    {
        int nameSize = (int)HeaderField(archive, 2), count = (int)HeaderField(archive, 3), start = DataStart(archive);
        byte[] rows = DirectoryOf(archive);
        int target = RowOf(rows, count, member), memory = 0;
        using var files = new MemoryStream();
        for (int index = 0; index < count; index++)
        {
            int row = index * RowSize + RowSizeField;
            int offset = BitConverter.ToInt32(rows, row + 4), packed = BitConverter.ToInt32(rows, row + 8);
            byte[] payload = archive[(start + offset)..(start + offset + packed)];
            if (index == target)
            {
                payload = StoredZlib(data);
                BitConverter.TryWriteBytes(rows.AsSpan(row), data.Length);
                BitConverter.TryWriteBytes(rows.AsSpan(row + 12), Crc32(payload));
            }
            BitConverter.TryWriteBytes(rows.AsSpan(row - 4), memory);
            BitConverter.TryWriteBytes(rows.AsSpan(row + 4), (int)files.Position);
            BitConverter.TryWriteBytes(rows.AsSpan(row + 8), payload.Length);
            memory += BitConverter.ToInt32(rows, row);
            files.Write(payload);
        }
        byte[] directory = StoredZlib(rows);
        using var output = new MemoryStream();
        output.Write(archive, 0, 5);
        uint[] header = [Crc32(directory), (uint)directory.Length, (uint)nameSize, (uint)count];
        for (int index = 0; index < 16; index++)
            output.WriteByte((byte)((header[index / 4] >> (index % 4 * 8)) ^ index));
        output.Write(archive, ArchiveHeader, nameSize);
        output.Write(directory);
        files.WriteTo(output);
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
            if (!IsCurrent(Path.Combine(destination, file.Path), file))
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
