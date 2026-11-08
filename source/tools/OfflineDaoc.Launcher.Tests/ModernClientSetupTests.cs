using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

public sealed class ModernClientSetupTests
{
    private string root = "";

    [SetUp]
    public void Prepare() => root = Path.Combine(Path.GetTempPath(), "odc-setup-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void Clean()
    {
        if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(root).StartsWith("odc-setup-", StringComparison.Ordinal) && Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    // game.dll: two bytes replaced in place and a section appended; the layout: one line inserted.
    private static readonly byte[] Game = Encoding.ASCII.GetBytes("MZ pristine game bytes 0123456789");
    private static readonly byte[] PatchedGame = Encoding.ASCII.GetBytes("MZ pristine GAME bytes 0123456789+section");
    private static readonly byte[] Layout = Encoding.ASCII.GetBytes("<a/>\r\n<b/>\r\n");
    private static readonly byte[] PatchedLayout = Encoding.ASCII.GetBytes("<a/>\r\n<new/>\r\n<b/>\r\n");

    private static ModernClientSetup.Patch TestPatch() => new(1, "1.130",
    [
        new("game.dll", Hash(Game), Hash(PatchedGame), PatchedGame.Length,
            [new(12, 4, Convert.ToBase64String("GAME"u8.ToArray())), new(Game.Length, 0, Convert.ToBase64String("+section"u8.ToArray()))]),
        new("pregame/character_creation.xml", Hash(Layout), Hash(PatchedLayout), PatchedLayout.Length,
            [new(6, 0, Convert.ToBase64String("<new/>\r\n"u8.ToArray()))])
    ]);

    private string PristineClient()
    {
        string client = Path.Combine(root, "client");
        Directory.CreateDirectory(Path.Combine(client, "pregame"));
        Directory.CreateDirectory(Path.Combine(client, "logs"));
        File.WriteAllBytes(Path.Combine(client, "game.dll"), Game);
        File.WriteAllBytes(Path.Combine(client, "pregame", "character_creation.xml"), Layout);
        File.WriteAllText(Path.Combine(client, "camelot.exe"), "exe");
        File.WriteAllText(Path.Combine(client, "login.dat"), "the player's saved login");
        File.WriteAllText(Path.Combine(client, "logs", "chat.log"), "a log");
        return client;
    }

    [Test]
    public void ApplyRebuildsBothFilesAndRefusesOtherInputs()
    {
        var patch = TestPatch();
        Assert.That(ModernClientSetup.Apply(Game, patch.Files[0]), Is.EqualTo(PatchedGame));
        Assert.That(ModernClientSetup.Apply(Layout, patch.Files[1]), Is.EqualTo(PatchedLayout));
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Apply("a newer client"u8.ToArray(), patch.Files[0]));
    }

    [Test]
    public void AWholeFileReplacementSwapsTheSplashAndRefusesAnotherOne()
    {
        // pregame/splash.mpk: one edit removes the stock archive and inserts the Divided Realms one.
        byte[] stock = "stock 1.130 splash archive"u8.ToArray(), ours = "divided realms splash"u8.ToArray();
        var splash = new ModernClientSetup.PatchedFile("pregame/splash.mpk", Hash(stock), Hash(ours), ours.Length,
            [new(0, stock.Length, Convert.ToBase64String(ours))]);
        Assert.That(ModernClientSetup.Apply(stock, splash), Is.EqualTo(ours));
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Apply("a player's own splash"u8.ToArray(), splash));
    }

    // make_client_patch.py wrote this expected archive from the same stock archive and edit (see the test below).
    private const string StockArchive = "TVBBSwKsztdCIAUGBxkJCgsNDQ4PeNqrys9LLdbLLcgGABK0A6Z42qvKz0st1ktJLGEYmeASCwMDC5QtD6XVgVg8aPo2AAwXBm142ouuys9LNTI1iOXlKkpNz8zPszU0M+PlykvMTbV15OUCAJnACM4=";
    private const string PatchedArchive = "TVBBSwJgA6A7IwQGBxkJCgsNDQ4PeNqrys9LLdbLLcgGABK0A6Z4AQEcAeP+em9uZXMuZGF0AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAANIEAAAEAAAAAAAAADgAAAAAAAAAQwAAALfxqFgSqAeUeAEBOADH/1t6b25lMjUwXQ0KcmVnaW9uPTI1MA0KbmFtZT1BDQoNClt6b25lMjUzXQ0KcmVnaW9uPTI1Mw0Kz+MPdg==";

    private static (byte[] Stock, byte[] Output, ModernClientSetup.PatchedFile File) MemberEdit()
    {
        byte[] stock = Convert.FromBase64String(StockArchive), output = Convert.FromBase64String(PatchedArchive);
        return (stock, output, new("zones/zones.mpk", Hash(stock), Hash(output), output.Length,
            [new(18, 3, Convert.ToBase64String("250"u8.ToArray())), new(31, 0, Convert.ToBase64String("\r\n[zone253]\r\nregion=253\r\n"u8.ToArray()))],
            Member: "zones.dat"));
    }

    [Test]
    public void AMemberEditRebuildsTheArchiveByteForByteAsThePatchBuilderDid()
    {
        var (stock, output, file) = MemberEdit();
        Assert.That(ModernClientSetup.Apply(stock, file), Is.EqualTo(output));
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Apply(output, file));
    }

    // make_client_patch.py's with_member on a three-file archive: the same bytes, offsets and expanded-memory pointers.
    private const string StockZone = "TVBBSwKS7DbVSQUGBxoJCgsPDQ4PeNpLSSwxMDDQyy3IBgARpQNAeNpL1EsuLmMYuYARiFmgbFYozQvEgTH9Hkn5pXkpIzh8mKBhAwoXDmi4CACx3bSnG6pGeLphhoYNKEwmABORLJAWBOIb/cavAcL7Du542kvLLCouAQAGVwIpeNrLz0lRKE/MyQEADa8DEHjay0ksLskZxYMGAwBkz6pR";
    private const string PatchedZone = "TVBBSwLDvH0qWwYGBxoJCgsPDQ4PeNpLSSwxMDDQyy3IBgARpQNAeAEBVAOr/GEuY3N2AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAABAAAAAAAAAAFAAAAAAAAAA0AAABRXI9IYm91bmQuY3N2AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAAAAEAAAABQAAAAoAAAANAAAAFQAAAL0aJtd6LmNzdgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwAAAAQAAAAPAAAAkAEAACIAAAARAAAA2I8z6yVQDmd42kvLLCouAQAGVwIpeAEBCgD1/2EgbmV3IHdhbGwS1AOceNrLSSwuyRnFgwYDAGTPqlE=";

    [Test]
    public void AMemberOfALargerArchiveChangesAlone()
    {
        byte[] stock = Convert.FromBase64String(StockZone), output = Convert.FromBase64String(PatchedZone);
        var file = new ModernClientSetup.PatchedFile("zones/zone000/dat000.mpk", Hash(stock), Hash(output), output.Length,
            [new(0, 3, Convert.ToBase64String("a new"u8.ToArray()))], Member: "bound.csv");
        Assert.That(ModernClientSetup.Apply(stock, file), Is.EqualTo(output));
    }

    [Test]
    public void SeveralMembersOfOneArchiveAreEditedInTurn()
    {
        // The fishing rod adds rows to two catalogs of gamedata.mpk in one entry: the same as two single-member entries in turn.
        byte[] stock = Convert.FromBase64String(StockZone);
        // bound.csv's edit is the one AMemberOfALargerArchiveChangesAlone checks against the Python builder's bytes.
        ModernClientSetup.Edit[] bound = [new(0, 3, Convert.ToBase64String("a new"u8.ToArray()))];
        ModernClientSetup.Edit[] rows = [new(0, 0, Convert.ToBase64String("4927,rod\r\n"u8.ToArray()))];
        byte[] afterBound = Convert.FromBase64String(PatchedZone);
        byte[] zRows = Archive("MemberOf", afterBound, "z.csv");
        byte[] edited = [.. "4927,rod\r\n"u8.ToArray(), .. zRows];
        byte[] expected = Archive("WithMember", afterBound, edited, "z.csv");

        var both = new ModernClientSetup.PatchedFile("zones/zone000/dat000.mpk", Hash(stock), Hash(expected), expected.Length, [],
            Members: [new("bound.csv", bound), new("z.csv", rows)]);
        Assert.That(ModernClientSetup.Apply(stock, both), Is.EqualTo(expected));
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Apply(afterBound, both), "only the pristine archive is accepted");
    }

    // gamedata.mpk's catalogs, as z.csv of a small archive: the stock table, and our rod row added at its place by ID.
    private const string StockCatalog = "10,staff\r\n20,spear\r\n14999,end\r\n";
    private const string RodRow = "15,rod";

    private static (byte[] Stock, byte[] Output, ModernClientSetup.PatchedFile File) Gamedata()
    {
        byte[] stock = Archive("WithMember", Convert.FromBase64String(StockZone), Encoding.Latin1.GetBytes(StockCatalog), "z.csv");
        byte[] output = Archive("WithMember", stock, Encoding.Latin1.GetBytes("10,staff\r\n15,rod\r\n20,spear\r\n14999,end\r\n"), "z.csv");
        return (stock, output, new("gamedata.mpk", Hash(stock), Hash(output), output.Length, [],
            Members: [new("z.csv", [new(10, 0, Convert.ToBase64String(Encoding.Latin1.GetBytes(RodRow + "\r\n")))], [RodRow])], AnyBuild: true));
    }

    private static string CatalogOf(byte[] archive) => Encoding.Latin1.GetString(Archive("MemberOf", archive, "z.csv"));

    [Test]
    public void AGamedataOfAnotherBuildGetsTheRodRowsByID()
    {
        var (stock, output, file) = Gamedata();
        Assert.That(ModernClientSetup.Apply(stock, file), Is.EqualTo(output), "the stock archive still patches byte for byte");
        // A later live gamedata.mpk: a new row and a changed one; ours goes in by ID and nothing else moves.
        byte[] newer = Archive("WithMember", stock, Encoding.Latin1.GetBytes("10,staff v2\r\n12,new live row\r\n20,spear\r\n14999,end\r\n"), "z.csv");
        byte[] adapted = ModernClientSetup.Apply(newer, file);
        Assert.That(CatalogOf(adapted), Is.EqualTo("10,staff v2\r\n12,new live row\r\n15,rod\r\n20,spear\r\n14999,end\r\n"));
        Assert.That(ModernClientSetup.Apply(adapted, file), Is.EqualTo(adapted), "rows already there aren't added twice");
        Assert.That(Archive("MemberOf", adapted, "bound.csv"), Is.EqualTo(Archive("MemberOf", newer, "bound.csv")), "other files are kept");
        // Our ID taken by another row: the archive stays as it is, the rod just has no model.
        byte[] taken = Archive("WithMember", stock, Encoding.Latin1.GetBytes("10,staff\r\n15,someone else's\r\n14999,end\r\n"), "z.csv");
        Assert.That(ModernClientSetup.Apply(taken, file), Is.EqualTo(taken));
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Apply("not an archive"u8.ToArray(), file));
    }

    [Test]
    public void SetupAcceptsAClientWhoseGamedataIsAnotherBuild()
    {
        var (stock, _, rod) = Gamedata();
        var patch = TestPatch() with { Files = [.. TestPatch().Files, rod] };
        string client = PristineClient(), destination = Path.Combine(root, "game", "client-modern", "app");
        byte[] newer = Archive("WithMember", stock, Encoding.Latin1.GetBytes("10,staff\r\n12,new live row\r\n20,spear\r\n14999,end\r\n"), "z.csv");
        File.WriteAllBytes(Path.Combine(client, "gamedata.mpk"), newer);
        Assert.That(ModernClientSetup.CheckSource(client, patch), Is.Null);
        ModernClientSetup.Install(client, destination, patch, null, CancellationToken.None);
        Assert.That(CatalogOf(File.ReadAllBytes(Path.Combine(destination, "gamedata.mpk"))), Does.Contain("12,new live row\r\n15,rod\r\n"));
        Assert.That(File.ReadAllBytes(Path.Combine(client, "gamedata.mpk")), Is.EqualTo(newer), "the player's own client is unchanged");
        Assert.That(ModernClientSetup.Stale(destination, patch), Is.Empty);
        Assert.That(ModernClientSetup.Repair(destination, client, patch, null, CancellationToken.None), Is.EqualTo((0, 0)));

        // A copy set up by an older release (no rod rows yet) is brought up to date from itself, without the client folder.
        File.WriteAllBytes(Path.Combine(destination, "gamedata.mpk"), newer);
        Assert.That(ModernClientSetup.Stale(destination, patch).Select(f => f.Path), Is.EqualTo(new[] { "gamedata.mpk" }));
        Assert.That(ModernClientSetup.NeedSource(destination, patch), Is.Empty);
        ModernClientSetup.Update(destination, patch, null);
        Assert.That(ModernClientSetup.Stale(destination, patch), Is.Empty);

        File.WriteAllText(Path.Combine(client, "gamedata.mpk"), "damaged");
        Assert.That(ModernClientSetup.CheckSource(client, patch), Does.Contain("can't be read"));
    }

    // The archive helpers are private to the launcher; the tests reach them as the launcher does.
    private static dynamic Archive(string method, byte[] archive, params object[] rest) =>
        typeof(ModernClientSetup).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [archive, .. rest])!;

    [Test]
    public void RepairRestoresDamagedFilesAndKeepsThePlayersOwn()
    {
        string client = PristineClient(), destination = Path.Combine(root, "game", "client-modern", "app");
        var patch = TestPatch();
        ModernClientSetup.Install(client, destination, patch, null, CancellationToken.None);
        File.WriteAllText(Path.Combine(destination, "camelot.exe"), "damaged");
        File.Delete(Path.Combine(destination, "pregame", "character_creation.xml"));
        File.WriteAllText(Path.Combine(destination, "game.dll"), "overwritten by something");
        File.WriteAllText(Path.Combine(destination, "user.dat"), "the player's settings");
        File.WriteAllText(Path.Combine(destination, "server-Hero.ini"), "a character's window layout");

        var (repatched, restored) = ModernClientSetup.Repair(destination, client, patch, null, CancellationToken.None);
        Assert.That((repatched, restored), Is.EqualTo((2, 1)));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "game.dll")), Is.EqualTo(PatchedGame));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "pregame", "character_creation.xml")), Is.EqualTo(PatchedLayout));
        Assert.That(File.ReadAllText(Path.Combine(destination, "camelot.exe")), Is.EqualTo("exe"));
        Assert.That(File.ReadAllText(Path.Combine(destination, "user.dat")), Is.EqualTo("the player's settings"));
        Assert.That(File.ReadAllText(Path.Combine(destination, "server-Hero.ini")), Is.EqualTo("a character's window layout"));
        Assert.That(File.Exists(Path.Combine(destination, "login.dat")), Is.False, "the player's own login stays out");
        Assert.That(File.ReadAllBytes(Path.Combine(client, "game.dll")), Is.EqualTo(Game), "the player's own client is unchanged");
        Assert.That(ModernClientSetup.Repair(destination, client, patch, null, CancellationToken.None), Is.EqualTo((0, 0)));
    }

    [Test]
    public void RepairRefusesAClientItCantPatch()
    {
        string client = PristineClient(), destination = Path.Combine(root, "game", "client-modern", "app");
        ModernClientSetup.Install(client, destination, TestPatch(), null, CancellationToken.None);
        File.WriteAllText(Path.Combine(client, "game.dll"), "a newer client");
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Repair(destination, client, TestPatch(), null, CancellationToken.None));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "game.dll")), Is.EqualTo(PatchedGame), "nothing was touched");
    }

    [Test]
    public void AClientTheEdenLauncherChangedIsNamedAsSuch()
    {
        string client = PristineClient();
        File.WriteAllText(Path.Combine(client, "pregame", "character_creation.xml"), "Eden's layout");
        Assert.That(ModernClientSetup.CheckSource(client, TestPatch()), Does.Contain("may be a newer or modified client"));
        File.WriteAllText(Path.Combine(client, ModernClientSetup.EdenMarker), "eden");
        Assert.That(ModernClientSetup.CheckSource(client, TestPatch()), Does.Contain("changed by the Eden client"));
        File.WriteAllBytes(Path.Combine(client, "pregame", "character_creation.xml"), Layout);
        Assert.That(ModernClientSetup.CheckSource(client, TestPatch()), Is.Null, "Eden's DLL alone beside stock files is fine");
    }

    [Test]
    public void TheLauncherCarriesTheProgressTablesInsideItself()
    {
        var rules = OfflineDaoc.ProgressImport.ImportEngine.Rules;
        Assert.That(rules.ProgressTables, Does.Contain("DOLCharacters").And.Contain("offline_fishing").And.Contain("Inventory"));
        Assert.That(rules.OptionalProgressTables, Does.Contain("offline_artifact_progress"));
    }

    // Set OFFLINE_DAOC_PRISTINE_1130 and OFFLINE_DAOC_CLIENT_PATCH (a folder with make_client_patch.py's client-patch.json)
    // to run the real patch through this code; every file is hash-checked against what the Python builder made.
    [Test]
    public void TheRealClientPatchBuildsFromTheRealClient()
    {
        string? client = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PRISTINE_1130");
        string? folder = Environment.GetEnvironmentVariable("OFFLINE_DAOC_CLIENT_PATCH");
        if (client == null || folder == null) Assert.Ignore("No pristine client and patch on this machine.");
        var patch = ModernClientSetup.Load(folder!);
        Assert.That(ModernClientSetup.CheckSource(client!, patch), Is.Null);
        foreach (var file in patch.Files)
            Assert.DoesNotThrow(() => ModernClientSetup.Apply(File.ReadAllBytes(Path.Combine(client!, file.InputPath)), file), file.Path);
    }

    // With OFFLINE_DAOC_PRISTINE_1130 and OFFLINE_DAOC_CLIENT_PATCH: the real rod rows go into a later build of gamedata.mpk
    // (the stock one with a new objects.csv row, as a live update adds them) and nothing else of it changes. With
    // OFFLINE_DAOC_OTHER_GAMEDATA too (a real other build, such as Eden's): the rows go in, or, where that build already uses
    // one of our IDs, the archive is left exactly as it is.
    [Test]
    public void TheRealRodRowsGoIntoAnotherBuildsGamedata()
    {
        string? client = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PRISTINE_1130");
        string? folder = Environment.GetEnvironmentVariable("OFFLINE_DAOC_CLIENT_PATCH");
        if (client == null || folder == null) Assert.Ignore("No pristine client and patch on this machine.");
        var file = ModernClientSetup.Load(folder!).Files.Single(f => f.Path == "gamedata.mpk");
        byte[] stock = File.ReadAllBytes(Path.Combine(client!, "gamedata.mpk"));
        string objects = Encoding.Latin1.GetString((byte[])Archive("MemberOf", stock, "objects.csv"));
        int last = objects.LastIndexOf("\r\n4923,", StringComparison.Ordinal);
        Assert.That(last, Is.GreaterThan(0), "stock objects.csv ends at 4923");
        int end = objects.IndexOf("\r\n", last + 2, StringComparison.Ordinal);
        string later = objects[..end] + "\r\n4924,A later live object,1,566" + objects[end..];
        CheckRows((byte[])Archive("WithMember", stock, Encoding.Latin1.GetBytes(later), "objects.csv"), file, expectRows: true);

        if (Environment.GetEnvironmentVariable("OFFLINE_DAOC_OTHER_GAMEDATA") is { } other)
            CheckRows(File.ReadAllBytes(other), file, expectRows: null);
    }

    private static void CheckRows(byte[] input, ModernClientSetup.PatchedFile file, bool? expectRows)
    {
        byte[] output = ModernClientSetup.Apply(input, file);
        Assert.That(ModernClientSetup.Apply(output, file), Is.EqualTo(output), "a second pass changes nothing");
        bool added = !output.AsSpan().SequenceEqual(input);
        if (expectRows is { } expected)
            Assert.That(added, Is.EqualTo(expected));
        foreach (var part in file.Members!)
        {
            List<string> before = Encoding.Latin1.GetString((byte[])Archive("MemberOf", input, part.Member)).Split("\r\n").ToList();
            List<string> after = Encoding.Latin1.GetString((byte[])Archive("MemberOf", output, part.Member)).Split("\r\n").ToList();
            if (added)
            {
                // An older release's client already holds some of our rows (DR-dev-0.1d's has the rod's, not the Regalia's).
                Assert.That(after.Where(line => !part.Rows!.Contains(line)), Is.EqualTo(before.Where(line => !part.Rows!.Contains(line))),
                    part.Member);
                Assert.That(part.Rows, Is.SubsetOf(after), part.Member);
            }
        }
        if (!added)
        {
            // Left alone only because one of our IDs holds another row there, or because every row of ours already is
            // (the developer's test client, where the art installers put the same rows).
            var ids = file.Members!.SelectMany(p => p.Rows!.Select(r => (p.Member, Id: r.Split(',')[0], Row: r))).ToList();
            List<string> Lines(string member) => Encoding.Latin1.GetString((byte[])Archive("MemberOf", input, member)).Split("\r\n").ToList();
            bool taken = ids.Any(x => Lines(x.Member).Any(line => line.Split(',')[0].Trim() == x.Id && line != x.Row));
            bool allThere = ids.All(x => Lines(x.Member).Contains(x.Row));
            Assert.That(taken || allThere, Is.True, "an ID of ours is taken, or all our rows are there");
        }
    }

    [Test]
    public void AFileMadeFromAnotherStockFileIsCheckedAgainstThatFile()
    {
        byte[] thidranki = "archive ter252.mpk contents"u8.ToArray(), abermenai = "archive ter253.mpk contents"u8.ToArray();
        var patch = new ModernClientSetup.Patch(1, "1.130", [.. TestPatch().Files,
            new("zones/zone253/ter253.mpk", Hash(thidranki), Hash(abermenai), abermenai.Length,
                [new(13, 1, Convert.ToBase64String("3"u8.ToArray()))], From: "zones/zone252/ter252.mpk")]);
        string source = PristineClient(), client = Path.Combine(root, "copy");
        Directory.CreateDirectory(Path.Combine(source, "zones", "zone252"));
        Assert.That(ModernClientSetup.CheckSource(source, patch), Does.Contain("ter252"));
        File.WriteAllBytes(Path.Combine(source, "zones", "zone252", "ter252.mpk"), thidranki);

        ModernClientSetup.Install(source, client, patch, null, CancellationToken.None);
        Assert.That(File.ReadAllBytes(Path.Combine(client, "zones", "zone253", "ter253.mpk")), Is.EqualTo(abermenai));
        Assert.That(File.Exists(Path.Combine(source, "zones", "zone253", "ter253.mpk")), Is.False);

        // A copy set up by an older release has the stock file, so the new one needs no client folder.
        File.Delete(Path.Combine(client, "zones", "zone253", "ter253.mpk"));
        Assert.That(ModernClientSetup.NeedSource(client, patch), Is.Empty);
        ModernClientSetup.Update(client, patch, null);
        Assert.That(ModernClientSetup.Stale(client, patch), Is.Empty);
    }

    [Test]
    public void InstallCopiesAndPatchesWithoutTouchingTheSource()
    {
        string client = PristineClient(), destination = Path.Combine(root, "game", "client-modern", "app");
        ModernClientSetup.Install(client, destination, TestPatch(), null, CancellationToken.None);
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "game.dll")), Is.EqualTo(PatchedGame));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "pregame", "character_creation.xml")), Is.EqualTo(PatchedLayout));
        Assert.That(File.Exists(Path.Combine(destination, "camelot.exe")), Is.True);
        Assert.That(File.Exists(Path.Combine(destination, "login.dat")), Is.False, "the player's login stays out");
        Assert.That(Directory.Exists(Path.Combine(destination, "logs")), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(client, "game.dll")), Is.EqualTo(Game), "the player's own client is unchanged");
        Assert.That(File.Exists(Path.Combine(destination, ModernClientSetup.IncompleteMarker)), Is.False);
    }

    [Test]
    public void AnInterruptedSetupCountsAsNotSetUpAndIsRedone()
    {
        string client = PristineClient(), destination = Path.Combine(root, "game", "client-modern", "app");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "game.dll"), "half copied");
        File.WriteAllText(Path.Combine(destination, ModernClientSetup.IncompleteMarker), "");
        var profile = new ModernClientProfile { ClientDirectory = destination };
        Assert.That(ModernClientSetup.IsSetUp(profile), Is.False);
        ModernClientSetup.Install(client, destination, TestPatch(), null, CancellationToken.None);
        Assert.That(ModernClientSetup.IsSetUp(profile), Is.True);
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "game.dll")), Is.EqualTo(PatchedGame));
    }

    // A later release: game.dll gets one more edit than TestPatch's, and pregame/splash.mpk is patched for the first time.
    private static readonly byte[] Splash = "stock 1.130 splash archive"u8.ToArray();
    private static readonly byte[] PatchedSplash = "divided realms splash"u8.ToArray();
    private static readonly byte[] NewerGame = Encoding.ASCII.GetBytes("MZ pristine GAME BYTES 0123456789+section");

    private static ModernClientSetup.Patch NewerPatch() => new(1, "1.130",
    [
        new("game.dll", Hash(Game), Hash(NewerGame), NewerGame.Length,
            [new(12, 10, Convert.ToBase64String("GAME BYTES"u8.ToArray())), new(Game.Length, 0, Convert.ToBase64String("+section"u8.ToArray()))]),
        TestPatch().Files[1],
        new("pregame/splash.mpk", Hash(Splash), Hash(PatchedSplash), PatchedSplash.Length, [new(0, Splash.Length, Convert.ToBase64String(PatchedSplash))])
    ]);

    private (string Client, string Destination) OlderReleaseCopy()
    {
        string client = PristineClient(), destination = Path.Combine(root, "game", "client-modern", "app");
        File.WriteAllBytes(Path.Combine(client, "pregame", "splash.mpk"), Splash);
        ModernClientSetup.Install(client, destination, TestPatch(), null, CancellationToken.None);
        return (client, destination);
    }

    [Test]
    public void SetupRemembersThePlayersClientFolder()
    {
        var (client, destination) = OlderReleaseCopy();
        Assert.That(ModernClientSetup.RememberedSource(destination), Is.EqualTo(Path.GetFullPath(client)));
        Assert.That(ModernClientSetup.Stale(destination, TestPatch()), Is.Empty, "a fresh setup matches its own release");
    }

    [Test]
    public void ANewerReleasePatchesTheOlderCopyAgainFromTheRememberedClient()
    {
        var (client, destination) = OlderReleaseCopy();
        var patch = NewerPatch();
        Assert.That(ModernClientSetup.Stale(destination, patch).Select(f => f.Path), Is.EqualTo(new[] { "game.dll", "pregame/splash.mpk" }));
        Assert.That(ModernClientSetup.NeedSource(destination, patch).Select(f => f.Path), Is.EqualTo(new[] { "game.dll" }),
            "the copy's splash is still the stock one, but its game.dll holds the older patch");

        ModernClientSetup.Update(destination, patch, ModernClientSetup.RememberedSource(destination));
        Assert.That(ModernClientSetup.Stale(destination, patch), Is.Empty);
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "game.dll")), Is.EqualTo(NewerGame));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "pregame", "splash.mpk")), Is.EqualTo(PatchedSplash));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "game.dll.bak")), Is.EqualTo(PatchedGame), "the replaced file is kept");
        Assert.That(File.ReadAllBytes(Path.Combine(client, "game.dll")), Is.EqualTo(Game), "the player's own client is unchanged");
    }

    [Test]
    public void FilesStillPristineInTheCopyArePatchedWithoutTheClientFolder()
    {
        var (_, destination) = OlderReleaseCopy();
        var patch = new ModernClientSetup.Patch(1, "1.130", [TestPatch().Files[0], TestPatch().Files[1], NewerPatch().Files[2]]);
        Assert.That(ModernClientSetup.NeedSource(destination, patch), Is.Empty);
        ModernClientSetup.Update(destination, patch, null);
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "pregame", "splash.mpk")), Is.EqualTo(PatchedSplash));
    }

    [Test]
    public void AnUpdateWithoutAPristineClientChangesNothing()
    {
        var (client, destination) = OlderReleaseCopy();
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Update(destination, NewerPatch(), null));
        File.WriteAllText(Path.Combine(client, "game.dll"), "a newer client");
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Update(destination, NewerPatch(), client));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "game.dll")), Is.EqualTo(PatchedGame));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "pregame", "splash.mpk")), Is.EqualTo(Splash),
            "the splash, which could have been patched, waits until every file can be");
    }

    [Test]
    public void AWrongClientIsRefusedBeforeAnythingIsCopied()
    {
        string client = PristineClient(), destination = Path.Combine(root, "game", "app");
        File.WriteAllText(Path.Combine(client, "game.dll"), "a newer client");
        Assert.That(ModernClientSetup.CheckSource(client, TestPatch()), Does.Contain("isn't the 1.130 build"));
        Assert.Throws<InvalidDataException>(() => ModernClientSetup.Install(client, destination, TestPatch(), null, CancellationToken.None));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    [Test]
    public void TheDestinationMayNotBeInsideTheSource()
    {
        string client = PristineClient();
        Assert.Throws<IOException>(() => ModernClientSetup.Install(client, Path.Combine(client, "copy"), TestPatch(), null, CancellationToken.None));
    }

    [Test]
    public void APackageRelativeDatabasePathValidates()
    {
        string runtime = Path.Combine(root, "runtime");
        Directory.CreateDirectory(Path.Combine(runtime, "server", "config"));
        new XDocument(new XElement("root", new XElement("Server", new XElement("IP", "127.0.0.1"), new XElement("Port", "10330"),
                new XElement("DBType", "SQLITE"), new XElement("DBConnectionString", "Data Source=../data/opendaoc.sqlite3.db;Version=3"))))
            .Save(Path.Combine(runtime, "server", "config", "serverconfig.xml"));
        Assert.DoesNotThrow(() => new ModernClientProfile { Port = 10330 }.ValidateServer(runtime));
    }
}
