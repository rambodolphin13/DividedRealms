using System.Diagnostics;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

// 2026-11-02: a player with no tools\dotnet and no .NET installed saw the server sit on STARTING and stop, with
// "You must install or update .NET" only in server-console.log. The launcher checks the runtime before starting.
[TestFixture]
public class DotNetRuntimeCheckTests
{
    private string _folder = "";

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"offline-daoc-dotnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_folder, "runtime", "server"));
        File.WriteAllText(Path.Combine(_folder, "runtime", "server", "CoreServer.runtimeconfig.json"), """
            { "runtimeOptions": { "tfm": "net10.0", "frameworks": [
                { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
                { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" } ] } }
            """);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_folder, true);

    private string Server => Path.Combine(_folder, "runtime", "server", "CoreServer.exe");

    private string Install(string root, string name, string version)
    {
        Directory.CreateDirectory(Path.Combine(root, "host", "fxr", version));
        Directory.CreateDirectory(Path.Combine(root, "shared", name, version));
        return root;
    }

    [Test]
    public void ReadsBothFrameworksFromTheRuntimeConfig()
    {
        var required = DotNetRuntimeCheck.Required(Server);
        Assert.That(required.Select(framework => framework.Name),
            Is.EquivalentTo(new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" }));
    }

    [Test]
    public void ASelfContainedServerNeedsNothing()
    {
        Assert.That(DotNetRuntimeCheck.Required(Path.Combine(_folder, "NoConfig.exe")), Is.Empty);
    }

    [Test]
    public void AnEmptyRootMissesBoth()
    {
        var missing = DotNetRuntimeCheck.Missing(DotNetRuntimeCheck.Required(Server), Path.Combine(_folder, "nothing"));
        Assert.That(missing, Has.Count.EqualTo(2));
    }

    [Test]
    public void TheCoreRuntimeAloneStillMissesAspNetCore()
    {
        string root = Install(Path.Combine(_folder, "dotnet"), "Microsoft.NETCore.App", "10.0.3");
        var missing = DotNetRuntimeCheck.Missing(DotNetRuntimeCheck.Required(Server), root);
        Assert.That(missing.Select(framework => framework.Name), Is.EqualTo(new[] { "Microsoft.AspNetCore.App" }));
    }

    [Test]
    public void ALaterPatchCountsAndAnotherMajorDoesNot()
    {
        string root = Install(Path.Combine(_folder, "dotnet"), "Microsoft.NETCore.App", "10.0.3");
        Install(root, "Microsoft.AspNetCore.App", "9.0.8");
        Install(root, "Microsoft.AspNetCore.App", "11.0.0-preview.1.26001.1");
        var missing = DotNetRuntimeCheck.Missing(DotNetRuntimeCheck.Required(Server), root);
        Assert.That(missing.Select(framework => framework.Name), Is.EqualTo(new[] { "Microsoft.AspNetCore.App" }));

        Install(root, "Microsoft.AspNetCore.App", "10.0.3");
        Assert.That(DotNetRuntimeCheck.Missing(DotNetRuntimeCheck.Required(Server), root), Is.Empty);
    }

    [Test]
    public void TheGamesOwnCopyWins()
    {
        string bundled = Install(Path.Combine(_folder, "tools", "dotnet"), "Microsoft.NETCore.App", "10.0.3");
        Assert.That(DotNetRuntimeCheck.ResolveRoot(Path.Combine(_folder, "runtime")), Is.EqualTo(Path.GetFullPath(bundled)));
    }

    [Test]
    public void AMissingRootIsNotPassedToTheServer()
    {
        var start = new ProcessStartInfo("CoreServer.exe");
        start.Environment["DOTNET_ROOT"] = Path.Combine(_folder, "tools", "dotnet");
        DotNetRuntimeCheck.Apply(start, Path.Combine(_folder, "tools", "dotnet"));
        Assert.That(start.Environment.ContainsKey("DOTNET_ROOT"), Is.False);

        string root = Install(Path.Combine(_folder, "dotnet"), "Microsoft.NETCore.App", "10.0.3");
        DotNetRuntimeCheck.Apply(start, root);
        Assert.That(start.Environment["DOTNET_ROOT_X64"], Is.EqualTo(root));
    }
}
