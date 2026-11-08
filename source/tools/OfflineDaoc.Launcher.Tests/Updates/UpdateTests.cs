#nullable enable
using System.Diagnostics;
using NUnit.Framework;
using OfflineDaoc.Updates;
using static OfflineDaoc.Launcher.Tests.Updates.UpdateFixtures;

namespace OfflineDaoc.Launcher.Tests.Updates;

// The launcher's auto-update (source/tools/OfflineDaoc.Launcher/Updates). No test touches the network: GitHub's answers
// are recorded (Fixtures/dr-releases-2026-11-07.json) and the downloads are fake. Adapted from Living Server's
// UpdateTests (chris4aker/OfflineDAoC-LivingServer cc305fe00, GPLv3) for Divided Realms' releases and packages.

[TestFixture]
public class ReleaseVersionTests
{
    [TestCase("DR-dev-0.1", "DR-dev-0.1a")]
    [TestCase("DR-dev-0.1a", "DR-dev-0.1b")]
    [TestCase("DR-dev-0.1b", "DR-dev-0.1c")]
    [TestCase("DR-dev-0.1z", "DR-dev-0.2")]
    [TestCase("DR-dev-0.1c", "DR-dev-0.2")]
    [TestCase("DR-dev-0.9", "DR-dev-0.10")]
    [TestCase("DR-dev-0.2", "DR-dev-0.2.1")]
    [TestCase("DR-dev-0.10b", "DR-dev-1.0")]
    public void LaterReleasesCompareAsNewer(string older, string newer)
    {
        ReleaseVersion a = ReleaseVersion.Parse(older)!, b = ReleaseVersion.Parse(newer)!;
        Assert.That(b.IsNewerThan(a), Is.True);
        Assert.That(a.IsNewerThan(b), Is.False);
        Assert.That(a.SameAs(b), Is.False);
    }

    [Test]
    public void CaseAndMissingNumbersDontMatter()
    {
        Assert.That(ReleaseVersion.Parse("dr-DEV-0.1B")!.Tag, Is.EqualTo("DR-dev-0.1b"));
        Assert.That(ReleaseVersion.Parse("DR-dev-0.1")!.SameAs(ReleaseVersion.Parse("DR-dev-0.1.0")), Is.True);
        Assert.That(ReleaseVersion.Parse(" DR-dev-0.1c ")!.Tag, Is.EqualTo("DR-dev-0.1c"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("v0.38-dev8-modern-test")]
    [TestCase("world-workshop-v1.0.0")]
    [TestCase("0.1b")]
    [TestCase("DR-dev-")]
    [TestCase("DR-dev-0.1bb")]
    [TestCase("DR-dev-0.1b-hotfix")]
    [TestCase("DR-0.1")]
    [TestCase("DR-dev-0.x")]
    [TestCase("DR-dev-1.2.3.4.5")]
    public void OtherTagsAreNotReleases(string? tag) => Assert.That(ReleaseVersion.Parse(tag), Is.Null);
}

[TestFixture]
public class ReleaseFeedTests
{
    [Test]
    public void TheRecordedListIsReadAndTheNewestDrReleaseWins()
    {
        List<ReleaseInfo> releases = ReleaseInfo.ParseList(Read(RecordedList));
        Assert.That(releases.Select(r => r.TagName), Is.EqualTo(new[] { "DR-dev-0.1b", "DR-dev-0.1a", "v0.38-dev8-modern-test", "world-workshop-v1.0.0" }));
        Assert.That(releases.All(r => r.Prerelease), Is.True, "every DR release is a pre-release: /releases/latest wouldn't see them");
        Assert.That(releases.Where(r => r.IsUsable).Select(r => r.TagName), Is.EqualTo(new[] { "DR-dev-0.1b", "DR-dev-0.1a" }));

        var web = new FakeReleaseWeb { Json = Read(RecordedList) };
        ReleaseInfo newest = ReleaseFeed.TryGetNewest(web, out string? problem)!;
        Assert.That(problem, Is.Null);
        Assert.That(web.Urls, Is.EqualTo(new[] { "https://api.github.com/repos/rambodolphin13/DividedRealms/releases?per_page=30" }));
        Assert.That(newest.TagName, Is.EqualTo("DR-dev-0.1b"));
        Assert.That(newest.Package!.Name, Is.EqualTo(Recorded01bName + ".zip"));
        Assert.That(newest.Package.Size, Is.EqualTo(Recorded01bSize));
        Assert.That(newest.Package.Digest, Is.EqualTo(Recorded01bDigest));
        Assert.That(newest.Package.DownloadUrl, Is.EqualTo(Download01bUrl(Recorded01bName + ".zip")));
        Assert.That(newest.PackageSha256File!.Name, Is.EqualTo(Recorded01bName + ".zip.sha256"));
        Assert.That(newest.PackageFolderName, Is.EqualTo(Recorded01bName));
        Assert.That(newest.PageUrl, Is.EqualTo("https://github.com/rambodolphin13/DividedRealms/releases/tag/DR-dev-0.1b"));
        Assert.That(newest.NotesPreview(3)[0], Does.StartWith("Divided Realms DR-dev-0.1b: The Fishing Update"));
    }

    [Test]
    public void TheNewestIsChosenByVersionNotByListOrder()
    {
        // GitHub lists by date; an older release edited later must not win.
        string json = "[" + Release("DR-dev-0.1a") + "," + Release("DR-dev-0.2") + "," + Release("DR-dev-0.1c") + "," +
                      Release("DR-dev-0.3", draft: true) + "," + Release("DR-dev-0.4", asset: "Something-else.zip") + "]";
        ReleaseInfo newest = ReleaseFeed.TryGetNewest(new FakeReleaseWeb { Json = json }, out _)!;
        Assert.That(newest.TagName, Is.EqualTo("DR-dev-0.2"), "drafts and releases without a package are skipped");
    }

    [Test]
    public void APackageWithoutANameAfterTheTagIsFound()
    {
        ReleaseInfo r = ReleaseInfo.ParseList("[" + Release("DR-dev-0.2", asset: "DividedRealms-DR-dev-0.2.zip") + "]")[0];
        Assert.That(r.Package!.Name, Is.EqualTo("DividedRealms-DR-dev-0.2.zip"));
        Assert.That(r.PackageFolderName, Is.EqualTo("DividedRealms-DR-dev-0.2"));
        Assert.That(ReleaseInfo.ParseList("[" + Release("DR-dev-0.2", asset: "DividedRealms-DR-dev-0.2b-X.zip") + "]")[0].Package, Is.Null,
            "another release's zip isn't this one's package");
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{\"message\":\"API rate limit exceeded for 1.2.3.4.\",\"documentation_url\":\"https://docs.github.com/rest\"}")]
    [TestCase("{\"message\":\"Not Found\"}")]
    [TestCase("[1,2,3]")]
    [TestCase("[]")]
    public void AnswersWithoutAReleaseOfferNothing(string json)
    {
        Assert.That(ReleaseFeed.TryGetNewest(new FakeReleaseWeb { Json = json }, out string? problem), Is.Null);
        Assert.That(problem, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ANetworkFailureIsReportedNotThrown()
    {
        var web = new FakeReleaseWeb { GetFails = new HttpRequestException("No such host is known.") };
        Assert.That(ReleaseFeed.TryGetNewest(web, out string? problem), Is.Null);
        Assert.That(problem, Is.EqualTo("No such host is known."));
    }

    private static string Release(string tag, bool draft = false, string? asset = null) =>
        $"{{\"tag_name\":\"{tag}\",\"draft\":{(draft ? "true" : "false")},\"prerelease\":true,\"assets\":[{{\"name\":\"{asset ?? "DividedRealms-" + tag + "-Test.zip"}\"," +
        $"\"size\":10,\"digest\":\"sha256:{new string('a', 64)}\",\"browser_download_url\":\"{ReleaseFeed.DownloadPrefix}{tag}/x.zip\"}}]}}";
}

[TestFixture]
public class PackageDigestTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = TempFolder();

    [TearDown]
    public void TearDown() => Delete(_dir);

    [Test]
    public void TheReleasesSha256FileIsRead()
    {
        string hex = Recorded01bDigest["sha256:".Length..];
        Assert.That(PackageDigest.FromSha256File($"{hex} *{Recorded01bName}.zip\n", Recorded01bName + ".zip"), Is.EqualTo(hex));
        Assert.That(PackageDigest.FromSha256File($"﻿{hex.ToUpperInvariant()}  {Recorded01bName}.zip\r\n", Recorded01bName + ".zip"), Is.EqualTo(hex));
        Assert.That(PackageDigest.FromSha256File(hex, "anything.zip"), Is.EqualTo(hex));
        Assert.That(PackageDigest.FromSha256File($"{hex} *other.zip\n", Recorded01bName + ".zip"), Is.Null);
        Assert.That(PackageDigest.FromSha256File("not a hash", "x.zip"), Is.Null);
    }

    [Test]
    public void AMatchPassesAndAMismatchOrNothingIsRefusedAndDeleted()
    {
        byte[] data = Bytes("a package");
        string path = Path.Combine(_dir, "p.zip");
        File.WriteAllBytes(path, data);
        Assert.That(PackageDigest.VerifyOrDelete(path, "sha256:" + Sha(data)), Is.Null);
        Assert.That(File.Exists(path), Is.True);
        Assert.That(PackageDigest.VerifyOrDelete(path, "sha256:" + Sha(Bytes("another"))), Does.Contain("doesn't match"));
        Assert.That(File.Exists(path), Is.False);
        foreach (string? digest in new[] { null, "", "sha256:", "sha256:1234", "md5:" + new string('0', 32) })
        {
            File.WriteAllBytes(path, data);
            Assert.That(PackageDigest.VerifyOrDelete(path, digest), Does.Contain("no SHA-256"), digest);
            Assert.That(File.Exists(path), Is.False);
        }
    }
}

[TestFixture]
public class UpdatePolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static ReleaseInfo Newest => ReleaseInfo.ParseList(Read(RecordedList))[0];

    [Test]
    public void AutomaticChecksHappenAtMostOnceADayAndOnlyWhenOn()
    {
        var s = new UpdateSettings();
        Assert.That(UpdatePolicy.ShouldCheck(s, Now, false), Is.True);
        s.LastCheckUtc = Now.AddHours(-23);
        Assert.That(UpdatePolicy.ShouldCheck(s, Now, false), Is.False);
        s.LastCheckUtc = Now.AddHours(-24);
        Assert.That(UpdatePolicy.ShouldCheck(s, Now, false), Is.True);
        s.LastCheckUtc = Now.AddHours(5);
        Assert.That(UpdatePolicy.ShouldCheck(s, Now, false), Is.True, "the clock went back");
        s.CheckAutomatically = false;
        s.LastCheckUtc = null;
        Assert.That(UpdatePolicy.ShouldCheck(s, Now, false), Is.False);
        Assert.That(UpdatePolicy.ShouldCheck(s, Now, true), Is.True, "asking always checks");
    }

    [Test]
    public void OnlyANewerReleaseIsOfferedAndASkippedOneOnlyWhenAsked()
    {
        var s = new UpdateSettings();
        Assert.That(UpdatePolicy.ShouldOffer(Newest, ReleaseVersion.Parse("DR-dev-0.1a"), s, false), Is.True);
        Assert.That(UpdatePolicy.ShouldOffer(Newest, ReleaseVersion.Parse("DR-dev-0.1b"), s, true), Is.False);
        Assert.That(UpdatePolicy.ShouldOffer(Newest, ReleaseVersion.Parse("DR-dev-0.1c"), s, true), Is.False);
        Assert.That(UpdatePolicy.ShouldOffer(Newest, null, s, true), Is.False);
        s.SkippedVersion = "DR-dev-0.1b";
        Assert.That(UpdatePolicy.ShouldOffer(Newest, ReleaseVersion.Parse("DR-dev-0.1a"), s, false), Is.False);
        Assert.That(UpdatePolicy.ShouldOffer(Newest, ReleaseVersion.Parse("DR-dev-0.1a"), s, true), Is.True);
        s.SkippedVersion = "DR-dev-0.1a";
        Assert.That(UpdatePolicy.ShouldOffer(Newest, ReleaseVersion.Parse("DR-dev-0.1"), s, false), Is.True, "a newer one than the skipped one");
    }

    [Test]
    public void SettingsAreSavedAndADamagedFileFallsBackToTheDefaults()
    {
        string dir = TempFolder();
        try
        {
            var s = new UpdateSettings { CheckAutomatically = false, SkippedVersion = "DR-dev-0.1b", LastCheckUtc = Now };
            s.Save(dir);
            UpdateSettings back = UpdateSettings.Load(dir);
            Assert.That((back.CheckAutomatically, back.SkippedVersion, back.LastCheckUtc), Is.EqualTo((false, "DR-dev-0.1b", (DateTime?)Now)));
            File.WriteAllText(Path.Combine(dir, UpdateSettings.FileName), "{ damaged");
            Assert.That(UpdateSettings.Load(dir).CheckAutomatically, Is.True);
        }
        finally { Delete(dir); }
    }
}

[TestFixture]
public class ReleaseFolderTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = TempFolder();

    [TearDown]
    public void TearDown() => Delete(_dir);

    [Test]
    public void AReleaseKnowsItsVersionFromItsStampOrAnOlderPackagesEdition()
    {
        string stamped = GameFolder(_dir, "DR-dev-0.1d", stamp: true);
        InstalledRelease a = ReleaseFolder.Detect(stamped, out _)!;
        Assert.That((a.Version.Tag, a.Source), Is.EqualTo(("DR-dev-0.1d", ReleaseFolder.StampFile)));
        // DR-dev-0.1b's real EDITION.txt (packages before the stamp have only this).
        string old = GameFolder(_dir, "DR-dev-0.1b");
        File.WriteAllText(Path.Combine(old, "EDITION.txt"), "Divided Realms DR-dev-0.1b Fishing Update: the original 1.130 client (bring your own), modern test classes.\r\n");
        InstalledRelease b = ReleaseFolder.Detect(old, out _)!;
        Assert.That((b.Version.Tag, b.Source), Is.EqualTo(("DR-dev-0.1b", ReleaseFolder.EditionFile)));
    }

    [Test]
    public void TheDevelopersTestInstallNeverUpdates()
    {
        // artifacts\modern-client-test: hand-installed files, no stamp, no edition, no manifest.
        string dev = Path.Combine(_dir, "modern-client-test");
        Write(dev, "runtime/OfflineDAoC.exe", Bytes("launcher"));
        Write(dev, "runtime/data/opendaoc.sqlite3.db", Bytes("the developer's world"));
        Assert.That(ReleaseFolder.Detect(dev, out string why), Is.Null);
        Assert.That(why, Does.Contain("isn't a Divided Realms release package"));

        // A release folder without its manifest, or without the launcher, isn't one either.
        string game = GameFolder(_dir, "DR-dev-0.1a");
        File.Delete(Path.Combine(game, ReleaseFolder.ManifestFile));
        Assert.That(ReleaseFolder.Detect(game, out why), Is.Null);
        Assert.That(why, Does.Contain(ReleaseFolder.ManifestFile));

        // Any folder inside a Git checkout (the test install lives in the repository's artifacts folder).
        string repo = Path.Combine(_dir, "Source");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string inRepo = GameFolder(Path.Combine(repo, "artifacts"), "DR-dev-0.1a", stamp: true);
        Assert.That(ReleaseFolder.Detect(inRepo, out why), Is.Null);
        Assert.That(why, Does.Contain("Git checkout"));
    }

    [Test]
    public void AFolderThatIsntAReleaseMakesNoRequestEvenWhenAsked()
    {
        string dev = Path.Combine(_dir, "modern-client-test");
        Write(dev, "runtime/OfflineDAoC.exe", Bytes("launcher"));
        var web = new FakeReleaseWeb { Json = Read(RecordedList) };
        var updater = new LauncherUpdater(dev, web);
        Assert.That(updater.Check(asked: true, DateTime.UtcNow, out string? problem), Is.Null);
        Assert.That(updater.Check(asked: false, DateTime.UtcNow, out _), Is.Null);
        Assert.That(problem, Does.Contain("isn't a Divided Realms release package"));
        Assert.That(web.GetCalls, Is.EqualTo(0));
        Assert.That(Directory.Exists(Path.Combine(dev, "runtime", "updates")), Is.False, "nothing is written either");
        Assert.Throws<PackageRefusedException>(() => updater.PrepareFromRelease(ReleaseInfo.ParseList(Read(RecordedList))[0], null, CancellationToken.None));
        Assert.That(web.Downloads, Is.EqualTo(0));
    }

    [Test]
    public void TheManifestIsReadAsTheBuilderWritesIt()
    {
        var m = ReleaseFolder.ReadManifest($"{new string('A', 64)}  docs/x.md\n{new string('b', 64)}  PACKAGE MANIFEST.txt\nnot a line\n{new string('c', 64)}  runtime/server/a b.dll\n");
        Assert.That(m["docs/x.md"], Is.EqualTo(new string('a', 64)));
        Assert.That(m["runtime/server/a b.dll"], Is.EqualTo(new string('c', 64)));
        Assert.That(m.Count, Is.EqualTo(3));
    }
}

[TestFixture]
public class UpdateCheckTests
{
    private string _dir = "";
    private string _game = "";

    [SetUp]
    public void SetUp()
    {
        _dir = TempFolder();
        _game = GameFolder(_dir, "DR-dev-0.1a");
    }

    [TearDown]
    public void TearDown() => Delete(_dir);

    [Test]
    public void TheLauncherAsksOnceADayAndRemembersWhen()
    {
        var web = new FakeReleaseWeb { Json = Read(RecordedList) };
        var now = new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc);
        ReleaseInfo? offer = new LauncherUpdater(_game, web).Check(false, now, out _);
        Assert.That(offer?.TagName, Is.EqualTo("DR-dev-0.1b"));
        Assert.That(new LauncherUpdater(_game, web).Check(false, now.AddHours(3), out _), Is.Null);
        Assert.That(web.GetCalls, Is.EqualTo(1), "the second start the same day asks nothing");
        Assert.That(new LauncherUpdater(_game, web).Check(true, now.AddHours(3), out _)?.TagName, Is.EqualTo("DR-dev-0.1b"));
        Assert.That(web.GetCalls, Is.EqualTo(2));
        Assert.That(File.ReadAllText(Path.Combine(_game, "runtime", "logs", "launcher-updates.log")), Does.Contain("DR-dev-0.1b"));
    }

    [Test]
    public void AFailedCheckIsQuietAndOffersNothing()
    {
        var web = new FakeReleaseWeb { GetFails = new TaskCanceledException("timed out") };
        Assert.That(new LauncherUpdater(_game, web).Check(false, DateTime.UtcNow, out string? problem), Is.Null);
        Assert.That(problem, Is.EqualTo("timed out"));
    }

    [Test]
    public void ChecksOffMeansNoRequestAndSkippingIsRemembered()
    {
        var web = new FakeReleaseWeb { Json = Read(RecordedList) };
        var updater = new LauncherUpdater(_game, web);
        updater.Skip(ReleaseVersion.Parse("DR-dev-0.1b")!);
        Assert.That(new LauncherUpdater(_game, web).Check(false, DateTime.UtcNow, out _), Is.Null);
        Assert.That(new LauncherUpdater(_game, web).Check(true, DateTime.UtcNow, out _), Is.Not.Null);
        updater = new LauncherUpdater(_game, web);
        updater.Settings.CheckAutomatically = false;
        updater.SaveSettings();
        int calls = web.GetCalls;
        Assert.That(new LauncherUpdater(_game, web).Check(false, DateTime.UtcNow.AddDays(2), out _), Is.Null);
        Assert.That(web.GetCalls, Is.EqualTo(calls));
    }

    [Test]
    public void TheNewestInstalledReleaseIsOfferedNothing()
    {
        string newest = GameFolder(_dir, "DR-dev-0.1b", stamp: true);
        var web = new FakeReleaseWeb { Json = Read(RecordedList) };
        Assert.That(new LauncherUpdater(newest, web).Check(true, DateTime.UtcNow, out string? problem), Is.Null);
        Assert.That(problem, Is.Null);
    }
}

[TestFixture]
public class PackageDownloadTests
{
    private string _dir = "";
    private string _game = "";
    private byte[] _package = [];

    [SetUp]
    public void SetUp()
    {
        ReleaseFeed.RetryDelay = TimeSpan.Zero;
        _dir = TempFolder();
        _game = GameFolder(_dir, "DR-dev-0.1a");
        _package = Package("DR-dev-0.1b");
    }

    [TearDown]
    public void TearDown() => Delete(_dir);

    private string Updates => Path.Combine(_game, "runtime", "updates");

    private ReleaseInfo Offer(FakeReleaseWeb web) => new LauncherUpdater(_game, web).Check(true, DateTime.UtcNow, out _)!;

    [Test]
    public void AGoodPackageIsDownloadedCheckedAndUnpacked()
    {
        var web = new FakeReleaseWeb { Json = RecordedWith01bPackage(_package), File = _package };
        var phases = new HashSet<string>();
        PreparedUpdate prepared = new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), (p, _, _) => phases.Add(p), CancellationToken.None);
        Assert.That(web.Urls.Last(), Is.EqualTo(Download01bUrl(Recorded01bName + ".zip")));
        Assert.That(prepared.Version.Tag, Is.EqualTo("DR-dev-0.1b"));
        Assert.That(prepared.ZipPath, Is.EqualTo(Path.Combine(Updates, Recorded01bName + ".zip")));
        Assert.That(prepared.PackageFolder, Is.EqualTo(Path.Combine(Updates, "staging", Recorded01bName)));
        Assert.That(File.ReadAllText(Path.Combine(prepared.PackageFolder, "runtime", "server", "GameServer.dll")), Is.EqualTo("server DR-dev-0.1b"));
        Assert.That(File.ReadAllText(prepared.ZipPath + ".sha256"), Is.EqualTo($"{Sha(_package)} *{Recorded01bName}.zip{Environment.NewLine}"));
        Assert.That(phases, Is.EquivalentTo(new[] { "download", "unpack" }));
        Assert.That(ReadGame(_game, "runtime/server/GameServer.dll"), Is.EqualTo(Bytes("server DR-dev-0.1a")), "nothing in the game changes before the hand-off");

        // Asked again, the kept package is used without downloading.
        new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None);
        Assert.That(web.Downloads, Is.EqualTo(1));
    }

    [Test]
    public void WithoutGitHubsDigestTheReleasesSha256FileIsUsed()
    {
        var web = new FakeReleaseWeb { Json = RecordedWith01bPackage(_package, digest: false), File = _package };
        web.Texts[Download01bUrl(Recorded01bName + ".zip.sha256")] = $"{Sha(_package)} *{Recorded01bName}.zip\n";
        PreparedUpdate prepared = new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None);
        Assert.That(Directory.Exists(prepared.PackageFolder), Is.True);

        // A .sha256 file that names another hash: refused after the download, and deleted.
        Delete(Updates);
        web.Texts[Download01bUrl(Recorded01bName + ".zip.sha256")] = $"{Sha(Bytes("x"))} *{Recorded01bName}.zip\n";
        var e = Assert.Throws<PackageRefusedException>(() => new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("doesn't match"));
        Assert.That(Directory.GetFiles(Updates, "*.zip*"), Is.Empty);
    }

    [Test]
    public void WithNoPublishedHashNothingIsDownloaded()
    {
        var web = new FakeReleaseWeb { Json = RecordedWith01bPackage(_package, digest: false), File = _package };
        // The .sha256 file is listed but GitHub doesn't serve it (404).
        var e = Assert.Throws<PackageRefusedException>(() => new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("Nothing was downloaded"));
        Assert.That(web.Downloads, Is.EqualTo(0));
    }

    [Test]
    public void ADroppedDownloadIsResumedWhereItStopped()
    {
        var web = new FakeReleaseWeb { Json = RecordedWith01bPackage(_package), File = _package, FailingDownloads = 2, StopAfter = 100 };
        PreparedUpdate prepared = new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None);
        Assert.That(web.Downloads, Is.EqualTo(3));
        Assert.That(web.ResumedFrom, Is.EqualTo(new long[] { 0, 100, 200 }));
        Assert.That(PackageDigest.FileSha256(prepared.ZipPath), Is.EqualTo(Sha(_package)));
        Assert.That(File.ReadAllText(Path.Combine(_game, "runtime", "logs", "launcher-updates.log")), Does.Contain("Resuming the download at 200 bytes"));
    }

    [Test]
    public void AServerThatIgnoresTheResumeStillGivesTheRightFile()
    {
        var web = new FakeReleaseWeb { Json = RecordedWith01bPackage(_package), File = _package, FailingDownloads = 1, StopAfter = 100, SupportsResume = false };
        PreparedUpdate prepared = new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None);
        Assert.That(PackageDigest.FileSha256(prepared.ZipPath), Is.EqualTo(Sha(_package)));
    }

    [Test]
    public void AfterTheLastTryTheDownloadedPartIsKeptForTheNextClick()
    {
        var web = new FakeReleaseWeb { Json = RecordedWith01bPackage(_package), File = _package, FailingDownloads = 99, StopAfter = 50 };
        Assert.Throws<IOException>(() => new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None));
        Assert.That(web.Downloads, Is.EqualTo(ReleaseFeed.DownloadAttempts));
        string part = Path.Combine(Updates, Recorded01bName + ".zip.part");
        Assert.That(new FileInfo(part).Length, Is.EqualTo(50 * ReleaseFeed.DownloadAttempts));
        web.FailingDownloads = 0;
        new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None);
        Assert.That(web.ResumedFrom.Last(), Is.EqualTo(50 * ReleaseFeed.DownloadAttempts));
    }

    [Test]
    public void APackageFromAnotherAddressIsRefused()
    {
        string json = RecordedWith01bPackage(_package).Replace(Download01bUrl(Recorded01bName + ".zip\""), "https://example.com/evil.zip\"");
        var web = new FakeReleaseWeb { Json = json, File = _package };
        var e = Assert.Throws<PackageRefusedException>(() => new LauncherUpdater(_game, web).PrepareFromRelease(Offer(web), null, CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("isn't one of this project's release files"));
        Assert.That(web.Downloads, Is.EqualTo(0));
    }
}

[TestFixture]
public class PackageCheckTests
{
    private string _dir = "";
    private static readonly ReleaseVersion V01b = ReleaseVersion.Parse("DR-dev-0.1b")!;
    private const string Top = "DividedRealms-DR-dev-0.1b-Fishing-Update";

    [SetUp]
    public void SetUp() => _dir = TempFolder();

    [TearDown]
    public void TearDown() => Delete(_dir);

    private string Zip(byte[] data)
    {
        string path = Path.Combine(_dir, Top + ".zip");
        File.WriteAllBytes(path, data);
        return path;
    }

    [Test]
    public void AGoodPackagePassesAndEveryFileIsCheckedAsItIsUnpacked()
    {
        string zip = Zip(Package("DR-dev-0.1b"));
        PackageContents contents = PackageCheck.Inspect(zip, V01b, Top);
        Assert.That(contents.Manifest.Count, Is.EqualTo(PackageFiles("DR-dev-0.1b").Count));
        string folder = PackageCheck.Extract(zip, contents, Path.Combine(_dir, "staging"));
        Assert.That(folder, Is.EqualTo(Path.Combine(_dir, "staging", Top)));
        Assert.That(File.ReadAllText(Path.Combine(folder, "RELEASE.txt")), Does.Contain("Version=DR-dev-0.1b"));
    }

    [Test]
    public void AnOlderPackageWithOnlyItsEditionPasses()
    {
        string zip = Zip(Package("DR-dev-0.1b", before: f => f.Remove("RELEASE.txt")));
        Assert.That(PackageCheck.Inspect(zip, V01b, Top).Version.Tag, Is.EqualTo("DR-dev-0.1b"));
    }

    [TestCase("PACKAGE MANIFEST.sha256")]
    [TestCase("runtime/OfflineDAoC.exe")]
    [TestCase("runtime/server/GameServer.dll")]
    [TestCase("START DIVIDED REALMS.cmd")]
    public void AMissingRequiredFileIsRefused(string file)
    {
        string zip = Zip(Package("DR-dev-0.1b", change: f => f.Remove(file)));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain(file));
    }

    [TestCase("runtime/data/opendaoc.sqlite3.db")]
    [TestCase("runtime/account.txt")]
    [TestCase("runtime/logs/server.log")]
    [TestCase("runtime/client-modern/app/game.dll")]
    public void APackageWithPlayerDataIsRefused(string file)
    {
        string zip = Zip(Package("DR-dev-0.1b", before: f => f[file] = Bytes("someone's save")));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain("player data"));
    }

    [Test]
    public void TheWrongReleaseOrFolderIsRefused()
    {
        string zip = Zip(Package("DR-dev-0.1c", top: Top));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain("names DR-dev-0.1c"));
        zip = Zip(Package("DR-dev-0.1b", top: "SomethingElse"));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain("should be in its"));
        zip = Zip(Package("DR-dev-0.1b", change: f => f["../evil.cmd"] = Bytes("x")));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain("unsafe"));
        File.WriteAllBytes(zip, Bytes("not a zip"));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain("readable zip"));
    }

    [Test]
    public void TheManifestMustListExactlyThePackagesFiles()
    {
        string zip = Zip(Package("DR-dev-0.1b", change: f => f["runtime/extra.dll"] = Bytes("unlisted")));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain("doesn't list runtime/extra.dll"));
        zip = Zip(Package("DR-dev-0.1b", change: f => f.Remove("docs/README.md")));
        Assert.That(Assert.Throws<PackageRefusedException>(() => PackageCheck.Inspect(zip, V01b, Top))!.Message, Does.Contain("lists docs/README.md"));
    }

    [Test]
    public void AChangedFileIsCaughtWhileUnpackingAndNothingIsLeft()
    {
        string zip = Zip(Package("DR-dev-0.1b", change: f => f["runtime/server/GameServer.dll"] = Bytes("tampered")));
        PackageContents contents = PackageCheck.Inspect(zip, V01b, Top);
        var e = Assert.Throws<PackageRefusedException>(() => PackageCheck.Extract(zip, contents, Path.Combine(_dir, "staging")));
        Assert.That(e!.Message, Does.Contain("runtime/server/GameServer.dll"));
        Assert.That(Directory.Exists(Path.Combine(_dir, "staging", Top)), Is.False);
    }
}

[TestFixture]
public class UpdateInstallerTests
{
    private string _dir = "";
    private string _game = "";
    private string _package = "";
    private string _backup = "";
    private readonly List<string> _log = [];

    [SetUp]
    public void SetUp()
    {
        _dir = TempFolder();
        _game = GameFolder(_dir, "DR-dev-0.1a");
        _package = Path.Combine(_dir, "package");
        Dictionary<string, byte[]> files = PackageFiles("DR-dev-0.1b");
        files["runtime/server/navmesh/zone999.nav"] = Bytes("a new mesh");
        files["runtime/server/CoreServer.exe"] = Bytes("core server"); // the same as 0.1a's
        files["runtime/server/config/serverconfig.xml"] = Bytes("<root><Server><Port>10330</Port><New>1</New></Server></root>");
        files[ReleaseFolder.ManifestFile] = Manifest(files);
        foreach (var (rel, data) in files)
            Write(_package, rel, data);
        _backup = Path.Combine(_game, "runtime", "updates", "backups", "test");
    }

    [TearDown]
    public void TearDown()
    {
        UpdateInstaller.BeforeWrite = null;
        Delete(_dir);
    }

    [Test]
    public void ThePackageReplacesTheGameButNeverThePlayersFiles()
    {
        InstallResult result = UpdateInstaller.Install(_package, _game, _backup, _log.Add);
        Assert.That(ReadGame(_game, "runtime/server/GameServer.dll"), Is.EqualTo(Bytes("server DR-dev-0.1b")));
        Assert.That(ReadGame(_game, "runtime/server/navmesh/zone999.nav"), Is.EqualTo(Bytes("a new mesh")));
        Assert.That(ReleaseFolder.Detect(_game, out _)!.Version.Tag, Is.EqualTo("DR-dev-0.1b"));
        foreach (var (rel, data) in PlayerData())
            Assert.That(ReadGame(_game, rel), Is.EqualTo(data), rel);
        // serverconfig.xml was the one 0.1a shipped (unchanged by the player), so the new default replaces it.
        Assert.That(ReadGame(_game, "runtime/server/config/serverconfig.xml"), Is.EqualTo(Bytes("<root><Server><Port>10330</Port><New>1</New></Server></root>")));
        Assert.That(result.Added, Is.EqualTo(2), "RELEASE.txt and the new mesh");
        Assert.That(result.Unchanged, Is.EqualTo(3), "CoreServer.exe, START DIVIDED REALMS.cmd and modern-client.json are the same");
        Assert.That(File.ReadAllBytes(Path.Combine(_backup, "files", "runtime", "server", "GameServer.dll")), Is.EqualTo(Bytes("server DR-dev-0.1a")));
        Assert.That(File.ReadAllLines(Path.Combine(_backup, "journal.txt")).Last(), Is.EqualTo("done"));
        Assert.That(File.ReadAllLines(Path.Combine(_backup, "journal.txt")).Reverse().Skip(1).Take(3),
            Is.EquivalentTo(new[] { "replaced\tPACKAGE MANIFEST.sha256", "replaced\tEDITION.txt", "added\tRELEASE.txt" }),
            "the stamp and manifest are written last");
    }

    [Test]
    public void AServerConfigThePlayerChangedIsKept()
    {
        byte[] hosted = Bytes("<root><Server><Port>10330</Port><IP>0.0.0.0</IP></Server></root>");
        Write(_game, "runtime/server/config/serverconfig.xml", hosted);
        InstallResult result = UpdateInstaller.Install(_package, _game, _backup, _log.Add);
        Assert.That(ReadGame(_game, "runtime/server/config/serverconfig.xml"), Is.EqualTo(hosted));
        Assert.That(result.KeptPlayerFiles, Does.Contain("runtime/server/config/serverconfig.xml"));
    }

    [Test]
    public void AFailureHalfwayPutsEverythingBack()
    {
        Dictionary<string, byte[]> before = Snapshot(_game);
        UpdateInstaller.BeforeWrite = rel =>
        {
            if (rel == "runtime/server/navmesh/zone999.nav")
                throw new IOException("The process cannot access the file because it is being used by another process (test).");
        };
        var e = Assert.Throws<UpdateFailedException>(() => UpdateInstaller.Install(_package, _game, _backup, _log.Add));
        Assert.That(e!.Message, Does.Contain("put back"));
        Dictionary<string, byte[]> after = Snapshot(_game);
        Assert.That(after.Keys, Is.EquivalentTo(before.Keys), "added files are removed again");
        foreach (var (rel, data) in before)
            Assert.That(after[rel], Is.EqualTo(data), rel);
    }

    [Test]
    public void ALockedFileMakesItRollBack()
    {
        Dictionary<string, byte[]> before = Snapshot(_game);
        // The real thing: a file the update must replace is open in another program.
        using (new FileStream(Path.Combine(_game, "runtime", "OfflineDAoC.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<UpdateFailedException>(() => UpdateInstaller.Install(_package, _game, _backup, _log.Add));
        Dictionary<string, byte[]> after = Snapshot(_game);
        Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
        foreach (var (rel, data) in before)
            Assert.That(after[rel], Is.EqualTo(data), rel);
    }

    [Test]
    public void AnInstalledUpdateCanBeRolledBackFromItsBackup()
    {
        Dictionary<string, byte[]> before = Snapshot(_game);
        UpdateInstaller.Install(_package, _game, _backup, _log.Add);
        Write(_game, "runtime/data/opendaoc.sqlite3.db", Bytes("played on 0.1b since"));
        UpdateInstaller.Rollback(_game, _backup, _log.Add);
        Dictionary<string, byte[]> after = Snapshot(_game);
        Assert.That(ReleaseFolder.Detect(_game, out _)!.Version.Tag, Is.EqualTo("DR-dev-0.1a"));
        Assert.That(after["runtime/data/opendaoc.sqlite3.db"], Is.EqualTo(Bytes("played on 0.1b since")), "a rollback never touches the save either");
        foreach (var (rel, data) in before.Where(f => f.Key != "runtime/data/opendaoc.sqlite3.db"))
            Assert.That(after[rel], Is.EqualTo(data), rel);
    }

    /// <summary>Every file of the game folder but the updater's own (runtime\updates, the log).</summary>
    private static Dictionary<string, byte[]> Snapshot(string game) =>
        Directory.GetFiles(game, "*", SearchOption.AllDirectories)
            .Select(f => PlayerFiles.Normalize(Path.GetRelativePath(game, f)))
            .Where(r => !r.StartsWith("runtime/updates/", StringComparison.OrdinalIgnoreCase) && !r.StartsWith("runtime/logs/launcher-updates", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => r, r => ReadGame(game, r));
}

// End to end: a fake DR-dev-0.1a folder that has been played, updated to a fake DR-dev-0.1b package offered by the
// recorded release list, keeping its save database.
[TestFixture]
public class UpdateEndToEndTests
{
    private string _dir = "";
    private string _game = "";
    private byte[] _package = [];

    [SetUp]
    public void SetUp()
    {
        ReleaseFeed.RetryDelay = TimeSpan.Zero;
        _dir = TempFolder();
        _game = GameFolder(_dir, "DR-dev-0.1a");
        _package = Package("DR-dev-0.1b", before: f => f["runtime/server/navmesh/zone250.nav"] = Bytes("a mesh new in 0.1b"));
    }

    [TearDown]
    public void TearDown() => Delete(_dir);

    [Test]
    public void TheLaunchersPathUpdatesAPlayedFolderAndKeepsTheSave()
    {
        var web = new FakeReleaseWeb { Json = RecordedWith01bPackage(_package), File = _package };
        var updater = new LauncherUpdater(_game, web);
        Assert.That(updater.Installed!.Version.Tag, Is.EqualTo("DR-dev-0.1a"));
        ReleaseInfo offer = updater.Check(asked: false, DateTime.UtcNow, out _)!;
        Assert.That(offer.TagName, Is.EqualTo("DR-dev-0.1b"));
        PreparedUpdate prepared = updater.PrepareFromRelease(offer, null, CancellationToken.None);

        // What the helper does once the launcher has closed (without the window or the restart).
        int code = FinishUpdateCommand.Finish(new FinishUpdateCommand.Options(0, prepared.PackageFolder, _game, prepared.Version, 0,
            Restart: false, Quiet: true, CheckRunning: false), null);

        Assert.That(code, Is.EqualTo(0));
        CheckUpdated(_game);
        // The launcher shows the result once on its next start.
        var after = new LauncherUpdater(_game, web);
        IReadOnlyDictionary<string, string> result = after.TakeLastResult()!;
        Assert.That((result["Version"], result["From"], result["Result"]), Is.EqualTo(("DR-dev-0.1b", "DR-dev-0.1a", "installed")));
        Assert.That(after.TakeLastResult(), Is.Null);
        Assert.That(Directory.Exists(Path.Combine(_game, "runtime", "updates", "staging")), Is.False, "the unpacked package is removed");
        Assert.That(File.Exists(prepared.ZipPath), Is.False, "the 1.2 GB download is removed once installed");
        Assert.That(File.Exists(prepared.ZipPath + ".sha256"), Is.False);
        string backup = Directory.GetDirectories(Path.Combine(_game, "runtime", "updates", "backups")).Single();
        Assert.That(Path.GetFileName(backup), Does.StartWith("DR-dev-0.1a-to-DR-dev-0.1b-"));
        Assert.That(File.ReadAllText(Path.Combine(backup, "README.txt")), Does.Contain("ROLLBACK.cmd"));
        string script = File.ReadAllText(Path.Combine(backup, "ROLLBACK.cmd"));
        Assert.That(script, Does.Contain("runtime\\updates\\helper\\OfflineDAoC.exe").And.Contain("--rollback-update").And.Contain("tools\\dotnet"));
        Assert.That(after.Check(asked: true, DateTime.UtcNow, out _), Is.Null, "0.1b is now the newest");

        // Played on 0.1b, then rolled back from the backup (as ROLLBACK.cmd does, the game found from the backup's place).
        Write(_game, "runtime/data/opendaoc.sqlite3.db", Bytes("played on 0.1b"));
        Assert.That(FinishUpdateCommand.GameOfBackup(backup), Is.EqualTo(_game));
        Assert.That(FinishUpdateCommand.RunRollback(["--rollback-update", backup, "--no-running-check"], Path.Combine(_dir, "elsewhere")), Is.EqualTo(0));
        Assert.That(ReleaseFolder.Detect(_game, out _)!.Version.Tag, Is.EqualTo("DR-dev-0.1a"));
        Assert.That(ReadGame(_game, "runtime/server/GameServer.dll"), Is.EqualTo(Bytes("server DR-dev-0.1a")));
        Assert.That(File.Exists(Path.Combine(_game, "runtime", "server", "navmesh", "zone250.nav")), Is.False);
        Assert.That(ReadGame(_game, "runtime/data/opendaoc.sqlite3.db"), Is.EqualTo(Bytes("played on 0.1b")));
        Assert.That(ReadGame(_game, "runtime/account.txt"), Is.EqualTo(PlayerData()["runtime/account.txt"]));
    }

    [Test]
    public void TheRealLauncherExeHandsOverToItsHelperWhichUpdatesTheFolder()
    {
        // The launcher as built (OfflineDaoc.Launcher/bin/<configuration>/net10.0-windows), run as a player's would be:
        // --apply-update checks the package, starts the helper copy in runtime\updates\helper and exits; the helper waits
        // for it, copies the package over the folder and writes the result.
        string testDir = TestContext.CurrentContext.TestDirectory;
        string configuration = new DirectoryInfo(testDir).Parent!.Name;
        string launcher = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "OfflineDaoc.Launcher", "bin", configuration, "net10.0-windows", "OfflineDAoC.exe"));
        Assert.That(File.Exists(launcher), Is.True, launcher);
        string zip = Path.Combine(_dir, Recorded01bName + ".zip");
        File.WriteAllBytes(zip, _package);

        var start = new ProcessStartInfo(launcher) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (string arg in new[] { "--apply-update", zip, "--sha256", Sha(_package), "--game", _game, "--no-restart", "--quiet", "--no-running-check" })
            start.ArgumentList.Add(arg);
        using Process apply = Process.Start(start)!;
        string output = apply.StandardOutput.ReadToEnd();
        Assert.That(apply.WaitForExit(60_000), Is.True);
        Assert.That(apply.ExitCode, Is.EqualTo(0), output);

        string result = Path.Combine(_game, "runtime", "updates", LauncherUpdater.ResultFile);
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!File.Exists(result) && DateTime.UtcNow < deadline)
            Thread.Sleep(200);
        Assert.That(File.Exists(result), Is.True, "the helper wrote no result: " + output + File.ReadAllText(Path.Combine(_game, "runtime", "logs", "launcher-updates.log")));
        Thread.Sleep(300); // let the helper finish writing and exit
        Assert.That(File.ReadAllText(result), Does.Contain("Result=installed"));
        CheckUpdated(_game);
        Assert.That(File.Exists(Path.Combine(_game, "runtime", "updates", "helper", "OfflineDAoC.exe")), Is.True, "the helper ran from its own copy");
    }

    [Test]
    public void TheHeadlessSwitchRefusesTheDevelopersFolderAndAnOlderPackage()
    {
        string dev = Path.Combine(_dir, "modern-client-test");
        Write(dev, "runtime/OfflineDAoC.exe", Bytes("launcher"));
        string zip = Path.Combine(_dir, Recorded01bName + ".zip");
        File.WriteAllBytes(zip, _package);
        Assert.That(ApplyUpdateCommand.Run(["--apply-update", zip, "--sha256", Sha(_package), "--game", dev, "--no-running-check"], dev, dev), Is.EqualTo(2));
        Assert.That(ReadGame(dev, "runtime/OfflineDAoC.exe"), Is.EqualTo(Bytes("launcher")));

        string newer = GameFolder(Path.Combine(_dir, "newer"), "DR-dev-0.1c", stamp: true);
        Assert.That(ApplyUpdateCommand.Run(["--apply-update", zip, "--sha256", Sha(_package), "--game", newer, "--no-running-check"], newer, newer), Is.EqualTo(2));
        Assert.That(ReleaseFolder.Detect(newer, out _)!.Version.Tag, Is.EqualTo("DR-dev-0.1c"));

        // The wrong hash: refused, nothing changed.
        Assert.That(ApplyUpdateCommand.Run(["--apply-update", zip, "--sha256", Sha(Bytes("x")), "--game", _game, "--no-running-check"], _game, _game), Is.EqualTo(2));
        Assert.That(ReleaseFolder.Detect(_game, out _)!.Version.Tag, Is.EqualTo("DR-dev-0.1a"));
    }

    private static void CheckUpdated(string game)
    {
        Assert.That(ReleaseFolder.Detect(game, out _)!.Version.Tag, Is.EqualTo("DR-dev-0.1b"));
        Assert.That(ReadGame(game, "runtime/server/GameServer.dll"), Is.EqualTo(Bytes("server DR-dev-0.1b")));
        Assert.That(ReadGame(game, "runtime/OfflineDAoC.exe"), Is.EqualTo(Bytes("launcher DR-dev-0.1b")));
        Assert.That(ReadGame(game, "runtime/data/world-clean.sqlite3.db"), Is.EqualTo(Bytes("clean world DR-dev-0.1b")));
        Assert.That(ReadGame(game, "runtime/server/navmesh/zone250.nav"), Is.EqualTo(Bytes("a mesh new in 0.1b")));
        foreach (var (rel, data) in PlayerData())
            Assert.That(ReadGame(game, rel), Is.EqualTo(data), "the player's " + rel + " is untouched");
    }
}
