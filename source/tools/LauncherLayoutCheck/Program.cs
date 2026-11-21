using System.Reflection;
using System.Drawing.Imaging;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.MainForm", true)!;
        // Never show the form: its Shown handler reads the live database.
        using var form = (Form)Activator.CreateInstance(type, true)!;
        ((Label)type.GetField("_serverState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Text = "STARTING…";
        foreach (string field in new[] { "_albionValue", "_midgardValue", "_hiberniaValue" })
            ((Label)type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Text = "3,334 ROSTER\n3,334 ONLINE";
        // The Home cards (MainForm.BuildCards) are the six-column table that holds the server state; other pages
        // have six-column tables of their own.
        Control? holder = (Label)type.GetField("_serverState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        while (holder is not null and not TableLayoutPanel { ColumnCount: 6 })
            holder = holder.Parent;
        var cards = (TableLayoutPanel)(holder ?? throw new Exception("The server state label is no longer in the six-column Home cards."));
        cards.Parent!.Controls.Remove(cards);
        cards.Dock = DockStyle.None;
        foreach (int width in new[] { 960, 1100, 1440 })
        {
            cards.Size = new Size(width - 48, 110);
            cards.CreateControl();
            Layout(cards);
            using var bitmap = new Bitmap(cards.Width, cards.Height);
            cards.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            if (Descendants(cards).OfType<Button>().Any())
                throw new Exception("The Home cards hold buttons again: check them here too.");
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"launcher-{width}.png"), ImageFormat.Png);
            Console.WriteLine($"PASS {width}: rendered the Home cards as launcher-{width}.png");
        }
        var hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        // The "+ Lv." buttons moved from the Home cards to the Bots page (MainForm.BuildBotsPage): one card per realm
        // in a fixed-size row, so the window width does not change them.
        var generate = ((IEnumerable<Button>)type.GetField("_realmGenerateButtons", hidden)!.GetValue(form)!).ToList();
        int[] levels = (int[])type.GetField("BotStartLevels", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        string[] expected = [.. Enumerable.Repeat(levels.Select(level => $"+ Lv.{level}"), 3).SelectMany(texts => texts)];
        if (!generate.Select(b => b.Text).SequenceEqual(expected))
            throw new Exception($"Expected the buttons {string.Join(", ", expected)} but found {string.Join(", ", generate.Select(b => b.Text))}");
        // button -> the row of level buttons -> the card's table -> the realm card -> the row of three realm cards
        Control realms = generate[0].Parent!.Parent!.Parent!.Parent!;
        if (realms is not TableLayoutPanel { ColumnCount: 3 } || generate.Any(b => b.Parent!.Parent!.Parent!.Parent != realms))
            throw new Exception("The generation buttons are no longer in the Bots page's row of three realm cards.");
        Size realmsSize = realms.Size;
        realms.Parent!.Controls.Remove(realms);
        realms.Dock = DockStyle.None;
        realms.Size = realmsSize;
        realms.CreateControl();
        Layout(realms);
        foreach (Button button in generate)
        {
            for (Control inner = button; inner != realms; inner = inner.Parent!)
                if (!inner.Parent!.ClientRectangle.Contains(inner.Bounds))
                    throw new Exception($"Clipped on the Bots page: {inner.GetType().Name} of {button.Text} {inner.Bounds} in {inner.Parent.ClientRectangle}");
            var textSize = TextRenderer.MeasureText(button.Text, button.Font);
            if (textSize.Width > button.Width - 6 || textSize.Height > button.Height - 4)
                throw new Exception($"Button text does not fit: {button.Text} {button.Size}");
        }
        using (var botsBitmap = new Bitmap(realms.Width, realms.Height))
        {
            realms.DrawToBitmap(botsBitmap, new Rectangle(Point.Empty, botsBitmap.Size));
            botsBitmap.Save(Path.Combine(AppContext.BaseDirectory, "bots-realm-cards.png"), ImageFormat.Png);
        }
        Console.WriteLine($"PASS: all {generate.Count} generation buttons fit on the Bots page; rendered bots-realm-cards.png");
        using var rvrForm = (Form)Activator.CreateInstance(type, true)!;
        var snapshotType = type.GetNestedType("RvrWorldSnapshot", BindingFlags.NonPublic)!;
        string json = """
            {"UpdatedUtc":"2026-09-04T15:00:00Z","Running":true,"Objectives":[
              {"Kind":"Keep","Name":"Dun Crimthainn","Owner":"Hibernia","State":"SIEGE — under attack","Location":"Hibernia","Carrier":"","Forces":"Albion: 80 · Hibernia: 64 · third realm: 48 · cap 128/realm"},
              {"Kind":"Relic","Name":"Thor's Hammer","Owner":"In transit","State":"ESCORT / INTERCEPTION","Location":"Odin's Gate","Carrier":"Test carrier","Forces":"Original realm: Midgard · Strength"},
              {"Kind":"Relic keep","Name":"Grallarhorn Faste","Owner":"Midgard","State":"Relic siege rally","Location":"Midgard","Carrier":"","Forces":"Albion: 96 · Midgard: 64 · third realm: 40 · cap 192/realm"},
              {"Kind":"Keep","Name":"Caer Benowyc","Owner":"Albion","State":"Secure","Location":"Albion","Carrier":"","Forces":"No organized rally"}]}
            """;
        type.GetField("_rvrWorld", hidden)!.SetValue(rvrForm, System.Text.Json.JsonSerializer.Deserialize(json, snapshotType));
        type.GetField("_rvrServerRunning", hidden)!.SetValue(rvrForm, true);
        type.GetMethod("RenderActiveRvr", hidden)!.Invoke(rvrForm, null);
        var grid = (DataGridView)type.GetField("_rvrObjectivesGrid", hidden)!.GetValue(rvrForm)!;
        Control panel = grid.Parent!;
        panel.Parent!.Controls.Remove(panel);
        panel.Dock = DockStyle.None;
        panel.Size = new Size(1020, 390);
        panel.CreateControl();
        Layout(panel);
        using var rvrBitmap = new Bitmap(panel.Width, panel.Height);
        panel.DrawToBitmap(rvrBitmap, new Rectangle(Point.Empty, panel.Size));
        rvrBitmap.Save(Path.Combine(AppContext.BaseDirectory, "rvr-panel.png"), ImageFormat.Png);
        Console.WriteLine("PASS: rendered Active RvR mock data without opening the launcher or reading the database");
    }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }
    static void Layout(Control root)
    {
        root.PerformLayout();
        foreach (Control child in root.Controls) Layout(child);
    }
}
