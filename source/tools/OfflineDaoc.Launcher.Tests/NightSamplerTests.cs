using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// Owner 2026-11-15: the Bot AI diagnostics box also starts DevKit's night sampler, but only on the developer's PC
/// (a DevKit above the install). Everywhere else the launcher finds no DevKit and does nothing.
/// </summary>
public sealed class NightSamplerTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "dr-night-" + Guid.NewGuid())).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private string MakeRuntime(params string[] below)
    {
        string runtime = Path.Combine(new[] { _dir }.Concat(below).Append("runtime").ToArray());
        Directory.CreateDirectory(runtime);
        return runtime;
    }

    private void MakeDevKit(bool withPython = true, bool withNight = true)
    {
        string devKit = Path.Combine(_dir, "DevKit");
        Directory.CreateDirectory(Path.Combine(devKit, "drkit"));
        Directory.CreateDirectory(Path.Combine(devKit, ".venv", "Scripts"));
        if (withNight) File.WriteAllText(Path.Combine(devKit, "drkit", "night.py"), "");
        if (withPython) File.WriteAllText(Path.Combine(devKit, ".venv", "Scripts", "python.exe"), "");
    }

    [Test]
    public void FindsDevKitAboveTheTestInstall()
    {
        // DAoC Offline\Source\artifacts\modern-client-test\runtime with DAoC Offline\DevKit
        string runtime = MakeRuntime("Source", "artifacts", "modern-client-test");
        MakeDevKit();
        Assert.That(NightSampler.FindDevKitPython(runtime), Is.EqualTo(Path.Combine(_dir, "DevKit", ".venv", "Scripts", "python.exe")));
    }

    [Test]
    public void NoDevKitMeansNothing()
    {
        string runtime = MakeRuntime("Divided Realms");
        Assert.That(NightSampler.FindDevKitPython(runtime), Is.Null);
        Assert.That(NightSampler.Run("start", runtime, Path.Combine(runtime, "logs")), Is.False);
    }

    [Test]
    public void AHalfDevKitIsIgnored()
    {
        string runtime = MakeRuntime("game");
        MakeDevKit(withPython: false);
        Assert.That(NightSampler.FindDevKitPython(runtime), Is.Null);
        MakeDevKit(withNight: false);
        File.Delete(Path.Combine(_dir, "DevKit", "drkit", "night.py"));
        Assert.That(NightSampler.FindDevKitPython(runtime), Is.Null);
    }

    [Test]
    public void StartNamesTheInstallFolderAndNeverStopsOnAFailedCheck()
    {
        string[] start = NightSampler.Arguments("start", @"C:\x\modern-client-test");
        Assert.That(start, Is.EqualTo(new[] { "-m", "drkit", "night", "start", "--install", @"C:\x\modern-client-test", "--anyway",
            "--note", "Started by the launcher's Bot AI diagnostics box" }));
        Assert.That(NightSampler.Arguments("stop", @"C:\x\i"), Is.EqualTo(new[] { "-m", "drkit", "night", "stop", "--install", @"C:\x\i" }));
    }
}
