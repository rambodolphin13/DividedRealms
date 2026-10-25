namespace OfflineDaoc.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-modern-profile")
        {
            try
            {
                var profile = ModernClientProfile.Load(AppContext.BaseDirectory)
                    ?? throw new InvalidDataException("No modern client profile is installed.");
                _ = profile.CreateStartInfo("verification-only", "not-used");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "modern-profile-verification.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { Passed = true, profile.Endpoint, ServerStarted = false, ClientLaunched = false }));
            }
            catch { Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 1 && args[0] == "--prepare-portable-account")
        {
            // the new client's launcher keeps its account with the game it plays (server_root.txt)
            PortableCredentials.ReadOrCreate(MainForm.ServerRootFor(AppContext.BaseDirectory));
            return;
        }
        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainForm()); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Offline DAoC launcher could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
