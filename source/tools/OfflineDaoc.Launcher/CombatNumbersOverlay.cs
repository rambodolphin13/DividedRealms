using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Draws the server's floating combat numbers and fight cards over the game window. It is a see-through, click-through
/// window that never takes focus, covers the game's client area and shows only while the game is in front. It needs
/// the game in a window or borderless; exclusive fullscreen draws over it. One per launcher, on its own thread.
/// </summary>
internal sealed class CombatNumbersOverlay : Form
{
    private const string GameWindowClass = "DAoCMWC";
    private static readonly Rectangle Hidden = new(-32000, -32000, 1, 1);
    private static readonly object StartLock = new();
    private static CombatNumbersOverlay? _instance;

    private readonly ConcurrentQueue<object> _incoming = new();
    private readonly FloatingNumbers _numbers = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 33 };
    private readonly CancellationTokenSource _stop = new();
    private volatile Uri _stream;
    private FightReport? _card;
    private long _cardShown;
    private Bitmap? _canvas;
    private EffectSnapshot? _effects;
    private long _effectsAt;
    private Rectangle _drawnGame = Hidden;
    private bool _drawnEmpty;
    private bool _dirty;

    /// <summary>Starts the overlay, or points the running one at this server and account.</summary>
    public static void EnsureRunning(int loginPort, string account)
    {
        Uri stream = CombatNumberFeed.StreamUri(loginPort, account);
        lock (StartLock)
        {
            if (_instance is { IsDisposed: false } running)
            {
                running._stream = stream;
                return;
            }
            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                // Game windows report real pixels; without this a scaled display would place the numbers off-centre.
                SetThreadDpiAwarenessContext(new IntPtr(-4));
                _instance = new CombatNumbersOverlay(stream);
                ready.Set();
                Application.Run(_instance);
            })
            { IsBackground = true, Name = "Combat numbers overlay" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait(5000);
        }
    }

    private CombatNumbersOverlay(Uri stream)
    {
        _stream = stream;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Bounds = Hidden;
        _tick.Tick += (_, _) => Step();
        _tick.Start();
        _ = ReadFeedAsync(_stop.Token);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return parameters;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _stop.Cancel();
        _tick.Dispose();
        _canvas?.Dispose();
        base.OnFormClosed(e);
    }

    // Reconnects every few seconds: the server may start after the launcher, restart, or be stopped.
    private async Task ReadFeedAsync(CancellationToken stop)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        while (!stop.IsCancellationRequested)
        {
            Uri stream = _stream;
            try
            {
                using var response = await http.GetAsync(stream, HttpCompletionOption.ResponseHeadersRead, stop);
                if (response.IsSuccessStatusCode)
                {
                    using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop));
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    var feed = new CombatFeedReader();
                    while (!stop.IsCancellationRequested && stream == _stream)
                    {
                        // The server writes at least every 15 seconds; silence for longer means it went away.
                        idle.CancelAfter(TimeSpan.FromSeconds(40));
                        string? line = await reader.ReadLineAsync(idle.Token);
                        if (line == null)
                            break;
                        if (feed.Read(line) is { } item)
                            _incoming.Enqueue(item);
                    }
                }
            }
            catch (Exception) when (!stop.IsCancellationRequested)
            {
            }
            try
            {
                await Task.Delay(3000, stop);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void Step()
    {
        long now = _clock.ElapsedMilliseconds;
        Rectangle? game = GameClientArea();
        while (_incoming.TryDequeue(out object? item))
        {
            if (item is FightReport report)
            {
                _card = report;
                _cardShown = now;
                _dirty = true;
            }
            else if (item is EffectSnapshot effects)
            {
                _effects = effects;
                _effectsAt = now;
                _dirty = true;
            }
            else if (item is CombatNumber number && game != null)
                _numbers.Add(number, now);
        }

        IReadOnlyList<FloatingNumber> frame = _numbers.Frame(now);
        float cardAlpha = _card == null ? 0 : FightCard.Alpha(now - _cardShown);
        if (_card != null && cardAlpha <= 0)
        {
            _card = null;
            _dirty = true;
        }
        // The server sends effects every second while connected; older ones mean it stopped.
        if (_effects != null && now - _effectsAt > EffectPanels.StaleMilliseconds)
        {
            _effects = null;
            _dirty = true;
        }
        bool panels = _effects != null && EffectPanels.Sections(_effects).Count > 0;

        if (game == null || (frame.Count == 0 && _card == null && !panels))
        {
            if (!_drawnEmpty)
            {
                Present(Hidden, Array.Empty<FloatingNumber>(), null, 0, null);
                _drawnGame = Hidden;
                _drawnEmpty = true;
            }
            return;
        }

        // Cards and panels sitting still with no numbers in flight need no new frame.
        bool moving = frame.Count > 0 || (_card != null && cardAlpha < 1);
        if (!moving && !_dirty && !_drawnEmpty && _drawnGame == game.Value)
            return;
        _dirty = false;
        _drawnEmpty = false;
        if (!Visible)
            Show();
        Present(game.Value, frame, _card, cardAlpha, panels ? _effects : null);
        _drawnGame = game.Value;
    }

    /// <summary>The game's client area on screen, while the game is the window in front; otherwise null.</summary>
    private static Rectangle? GameClientArea()
    {
        IntPtr front = GetForegroundWindow();
        if (front == IntPtr.Zero || IsIconic(front))
            return null;
        var name = new StringBuilder(32);
        if (GetClassName(front, name, name.Capacity) == 0 || name.ToString() != GameWindowClass)
            return null;
        if (!GetClientRect(front, out RECT client) || client.Right <= 0 || client.Bottom <= 0)
            return null;
        var origin = new POINT();
        ClientToScreen(front, ref origin);
        return new Rectangle(origin.X, origin.Y, client.Right, client.Bottom);
    }

    // Draws over the whole game area with real per-pixel transparency, so edges stay smooth.
    private void Present(Rectangle game, IReadOnlyList<FloatingNumber> frame, FightReport? card, float cardAlpha, EffectSnapshot? effects)
    {
        if (_canvas == null || _canvas.Width != game.Width || _canvas.Height != game.Height)
        {
            _canvas?.Dispose();
            _canvas = new Bitmap(Math.Max(1, game.Width), Math.Max(1, game.Height), PixelFormat.Format32bppArgb);
        }
        using (Graphics graphics = Graphics.FromImage(_canvas))
            Render(graphics, game.Size, frame, card, cardAlpha, effects);

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr hBitmap = _canvas.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memory, hBitmap);
        try
        {
            var size = new SIZE { cx = _canvas.Width, cy = _canvas.Height };
            var source = new POINT();
            var destination = new POINT { X = game.X, Y = game.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(hBitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>One frame: numbers in flight, the fight card and the effect panels, on a transparent surface the size of the game area.</summary>
    internal static void Render(Graphics graphics, Size game, IReadOnlyList<FloatingNumber> frame, FightReport? card, float cardAlpha,
        EffectSnapshot? effects)
    {
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float baseSize = Math.Max(14f, game.Height / 34f);
        using var family = new FontFamily("Segoe UI");
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        foreach (FloatingNumber number in frame)
        {
            int alpha = (int)(255 * Math.Clamp(number.Alpha, 0, 1));
            if (alpha <= 0)
                continue;
            using var path = new GraphicsPath();
            path.AddString(number.Label, family, (int)FontStyle.Bold, baseSize * number.Size,
                new PointF(number.X * game.Width, number.Y * game.Height), format);
            using var outline = new Pen(Color.FromArgb(alpha * 3 / 4, 0, 0, 0), Math.Max(3f, baseSize / 7f)) { LineJoin = LineJoin.Round };
            using var fill = new SolidBrush(Color.FromArgb(alpha, number.Color));
            graphics.DrawPath(outline, path);
            graphics.FillPath(fill, path);
        }
        if (card != null && cardAlpha > 0)
            DrawCard(graphics, game, card, cardAlpha);
        if (effects != null)
            DrawPanels(graphics, game, effects);
    }

    // The panels sit left, under the top of the screen and clear of the chat at the bottom left.
    private static void DrawPanels(Graphics graphics, Size game, EffectSnapshot snapshot)
    {
        float scale = Math.Clamp(game.Height / 900f, 0.8f, 2.4f);
        float row = 19 * scale, pad = 8 * scale, gap = 8 * scale;
        float width = 250 * scale;
        float x = game.Width * 0.015f, y = game.Height * 0.2f;
        float bottom = game.Height * 0.62f;

        using var background = new SolidBrush(Color.FromArgb(185, 14, 16, 22));
        using var border = new Pen(Color.FromArgb(120, 196, 160, 90), Math.Max(1f, scale));
        using var titleFont = new Font("Segoe UI", 12 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var font = new Font("Segoe UI", 11.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var mineFont = new Font("Segoe UI", 11.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var gold = new SolidBrush(Color.FromArgb(255, 230, 200, 120));
        using var text = new SolidBrush(Color.FromArgb(255, 225, 228, 236));
        using var dim = new SolidBrush(Color.FromArgb(255, 140, 145, 160));
        using var good = new SolidBrush(Color.FromArgb(255, 90, 200, 110));
        using var bad = new SolidBrush(Color.FromArgb(255, 225, 80, 70));
        using var track = new SolidBrush(Color.FromArgb(120, 60, 64, 76));
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        using var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        foreach (EffectPanels.Section section in EffectPanels.Sections(snapshot))
        {
            float lines = 1.15f + section.Effects.Count + (section.Hidden > 0 ? 1 : 0);
            float height = pad * 2 + row * lines + (section.Health != null ? 7 * scale : 0);
            if (y + height > bottom)
                break;
            using var box = Rounded(new RectangleF(x, y, width, height), 6 * scale);
            graphics.FillPath(background, box);
            graphics.DrawPath(border, box);

            float left = x + pad, top = y + pad, inner = width - pad * 2;
            graphics.DrawString(section.Title, titleFont, gold, new RectangleF(left, top, inner, row), clip);
            top += row * 1.15f;
            if (section.Health is int health)
            {
                Color lifeColor = health > 50 ? Color.FromArgb(255, 90, 200, 110)
                    : health > 25 ? Color.FromArgb(255, 230, 190, 70) : Color.FromArgb(255, 225, 80, 70);
                using var life = new SolidBrush(lifeColor);
                graphics.FillRectangle(track, left, top - 4 * scale, inner, 4 * scale);
                graphics.FillRectangle(life, left, top - 4 * scale, inner * health / 100f, 4 * scale);
                top += 7 * scale;
            }
            foreach (EffectInfo effect in section.Effects)
            {
                Brush tone = effect.Good ? good : bad;
                float nameLeft = left + row * 0.6f, barWidth = inner - row * 0.6f;
                graphics.FillEllipse(tone, left, top + row * 0.3f, row * 0.4f, row * 0.4f);
                graphics.DrawString(effect.Name, effect.Mine ? mineFont : font, effect.Mine ? text : dim,
                    new RectangleF(nameLeft, top, inner * 0.72f, row), clip);
                graphics.DrawString(EffectPanels.Time(effect.Seconds), font, text, left + inner, top, right);
                graphics.FillRectangle(track, nameLeft, top + row - 3 * scale, barWidth, 2 * scale);
                graphics.FillRectangle(tone, nameLeft, top + row - 3 * scale, barWidth * EffectPanels.Left(effect), 2 * scale);
                top += row;
            }
            if (section.Hidden > 0)
                graphics.DrawString("+" + section.Hidden + " more", font, dim, left, top);
            y += height + gap;
        }
    }

    // The card sits right of centre, clear of the chat (bottom left) and quickbars (bottom).
    private static void DrawCard(Graphics graphics, Size game, FightReport report, float alpha)
    {
        List<FightCard.Line> lines = FightCard.Lines(report);
        float scale = Math.Clamp(game.Height / 900f, 0.8f, 2.4f);
        float row = 22 * scale, pad = 12 * scale;
        float width = 430 * scale;
        float height = pad * 2 + row * 2.4f + row * lines.Count + (lines.Any(line => line.Enemy) ? row * 0.6f : 0);
        float x = game.Width - width - game.Width * 0.02f;
        float y = game.Height * 0.22f;
        int a(int value) => (int)(value * alpha);

        using var background = new SolidBrush(Color.FromArgb(a(200), 14, 16, 22));
        using var border = new Pen(Color.FromArgb(a(170), 196, 160, 90), Math.Max(1f, scale));
        using var box = Rounded(new RectangleF(x, y, width, height), 8 * scale);
        graphics.FillPath(background, box);
        graphics.DrawPath(border, box);

        using var titleFont = new Font("Segoe UI", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var headFont = new Font("Segoe UI", 11 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var font = new Font("Segoe UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 10.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var gold = new SolidBrush(Color.FromArgb(a(255), 230, 200, 120));
        using var dim = new SolidBrush(Color.FromArgb(a(255), 140, 145, 160));
        using var ally = new SolidBrush(Color.FromArgb(a(255), 235, 238, 245));
        using var enemy = new SolidBrush(Color.FromArgb(a(255), 240, 150, 140));
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        using var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        float[] columns = { 0.56f, 0.70f, 0.84f, 0.97f };
        float left = x + pad, top = y + pad;
        graphics.DrawString(FightCard.Title(report), titleFont, gold, left, top);
        top += row * 1.3f;
        string[] heads = { "Damage", "Taken", "Healed", "K/D" };
        for (int i = 0; i < heads.Length; i++)
            graphics.DrawString(heads[i], headFont, dim, x + width * columns[i], top, right);
        top += row * 1.1f;

        bool enemies = false;
        float nameWidth = width * 0.40f;
        foreach (FightCard.Line line in lines)
        {
            if (line.Enemy && !enemies)
            {
                enemies = true;
                using var rule = new Pen(Color.FromArgb(a(90), 196, 160, 90));
                graphics.DrawLine(rule, left, top + row * 0.3f, x + width - pad, top + row * 0.3f);
                top += row * 0.6f;
            }
            Brush brush = line.Enemy ? enemy : ally;
            SizeF name = graphics.MeasureString(line.Name, font, (int)nameWidth, clip);
            graphics.DrawString(line.Name, font, brush, new RectangleF(left, top, nameWidth, row), clip);
            if (line.Detail.Length > 0 && name.Width + 6 * scale < nameWidth)
                graphics.DrawString(line.Detail, small, dim,
                    new RectangleF(left + name.Width + 4 * scale, top + 2 * scale, nameWidth - name.Width - 4 * scale, row), clip);
            string[] cells = { line.Done, line.Taken, line.Healed, line.KillsDeaths };
            for (int i = 0; i < cells.Length; i++)
                graphics.DrawString(cells[i], font, brush, x + width * columns[i], top, right);
            top += row;
        }
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref POINT destination, ref SIZE size,
        IntPtr sourceDc, ref POINT source, int colorKey, ref BLENDFUNCTION blend, int flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr gdiObject);
}
