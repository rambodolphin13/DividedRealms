using System.Diagnostics;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Owner 2026-11-15: the Bot AI diagnostics box turns on the whole bot-run recording, not only the log lines. On the
/// developer's PC (a DevKit folder with its Python environment above the install) the launcher also starts DevKit's
/// night sampler (dr night start) when the server starts with the box ticked, or when the box is ticked while the
/// server runs, and asks it to finish (dr night stop) when the box is unticked. On any other PC (a release a friend
/// runs) there is no DevKit and nothing happens. The sampler only reads: logs, Bot Watch's admin endpoints, the
/// server process; it waits for the server and finishes by itself when the server stops.
/// </summary>
public static class NightSampler
{
    /// <summary>DevKit's python.exe when a DevKit folder sits beside one of the install's parent folders, else null.</summary>
    public static string? FindDevKitPython(string runtimeDirectory, int maxLevels = 6)
    {
        DirectoryInfo? dir = new DirectoryInfo(runtimeDirectory);
        for (int i = 0; dir != null && i < maxLevels; i++, dir = dir.Parent)
        {
            string devKit = Path.Combine(dir.FullName, "DevKit");
            string python = Path.Combine(devKit, ".venv", "Scripts", "python.exe");
            if (File.Exists(Path.Combine(devKit, "drkit", "night.py")) && File.Exists(python))
                return python;
        }
        return null;
    }

    /// <summary>The dr arguments, without the python part: the install is named by its folder (the one holding runtime).</summary>
    public static string[] Arguments(string action, string installRoot) => action == "start"
        ? ["-m", "drkit", "night", "start", "--install", installRoot, "--anyway",
           "--note", "Started by the launcher's Bot AI diagnostics box"]
        : ["-m", "drkit", "night", action, "--install", installRoot];

    /// <summary>Runs dr night start/stop hidden. Returns false when there is no DevKit (not the developer's PC).
    /// The output goes to logs\night-sampler.log; a sampler already running for the install is not an error.</summary>
    public static bool Run(string action, string runtimeDirectory, string logsDirectory)
    {
        string? python = FindDevKitPython(runtimeDirectory);
        if (python == null)
            return false;
        string devKit = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(python)!)!)!;
        string installRoot = Path.GetDirectoryName(Path.GetFullPath(runtimeDirectory).TrimEnd(Path.DirectorySeparatorChar))!;
        var info = new ProcessStartInfo(python)
        {
            WorkingDirectory = devKit,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in Arguments(action, installRoot))
            info.ArgumentList.Add(a);
        info.Environment["PYTHONPATH"] = devKit;
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        try
        {
            Process p = Process.Start(info)!;
            string log = Path.Combine(logsDirectory, "night-sampler.log");
            _ = Task.Run(() =>
            {
                try
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    Directory.CreateDirectory(logsDirectory);
                    File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] dr night {action} (exit {p.ExitCode})"
                                            + Environment.NewLine + output + Environment.NewLine);
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
