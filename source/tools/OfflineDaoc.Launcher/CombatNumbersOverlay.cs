using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Draws the server's floating combat numbers over the game window. It is a see-through, click-through window that
/// never takes focus, follows the game window and shows only while the game is in front. It needs the game in a window
/// or borderless; exclusive fullscreen draws over it. One per launcher, on its own thread.
/// </summary>
internal sealed class CombatNumbersOverlay : Form
{
    private const string GameWindowClass = "DAoCMWC";
    private static readonly object StartLock = new();
    private static CombatNumbersOverlay? _instance;

    private readonly ConcurrentQueue<CombatNumber> _incoming = new();
    private readonly FloatingNumbers _numbers = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 33 };
    private readonly CancellationTokenSource _stop = new();
    private volatile Uri _stream;
    private Rectangle _area;
    private bool _drawnEmpty;

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
        Bounds = new Rectangle(-32000, -32000, 1, 1);
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
                    while (!stop.IsCancellationRequested && stream == _stream)
                    {
                        // The server writes at least every 15 seconds; silence for longer means it went away.
                        idle.CancelAfter(TimeSpan.FromSeconds(40));
                        string? line = await reader.ReadLineAsync(idle.Token);
                        if (line == null)
                            break;
                        if (CombatNumberFeed.ParseLine(line) is { } number)
                            _incoming.Enqueue(number);
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
        while (_incoming.TryDequeue(out CombatNumber? number))
        {
            if (game != null)
                _numbers.Add(number, now);
        }

        IReadOnlyList<FloatingNumber> frame = _numbers.Frame(now);
        if (game == null || frame.Count == 0)
        {
            if (!_drawnEmpty)
            {
                Present(Rectangle.Empty, Array.Empty<FloatingNumber>());
                _drawnEmpty = true;
            }
            return;
        }
        _drawnEmpty = false;
        Present(game.Value, frame);
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

    // Draws the numbers into a band around the character, with real per-pixel transparency so edges stay smooth.
    private void Present(Rectangle game, IReadOnlyList<FloatingNumber> frame)
    {
        if (game.IsEmpty)
        {
            if (_area != Rectangle.Empty)
            {
                _area = Rectangle.Empty;
                Bounds = new Rectangle(-32000, -32000, 1, 1);
            }
            Draw(new Rectangle(-32000, -32000, 1, 1), frame, game);
            return;
        }
        var area = new Rectangle(game.X + game.Width / 5, game.Y + game.Height / 10, game.Width * 3 / 5, game.Height * 3 / 5);
        if (!Visible)
            Show();
        _area = area;
        Draw(area, frame, game);
    }

    private void Draw(Rectangle area, IReadOnlyList<FloatingNumber> frame, Rectangle game)
    {
        using Bitmap bitmap = Render(area, frame, game);
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memory, hBitmap);
        try
        {
            var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
            var source = new POINT();
            var destination = new POINT { X = area.X, Y = area.Y };
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

    /// <summary>One frame of numbers as a transparent image of the band; the game area places them.</summary>
    internal static Bitmap Render(Rectangle area, IReadOnlyList<FloatingNumber> frame, Rectangle game)
    {
        var bitmap = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float baseSize = Math.Max(14f, game.Height / 34f);
            using var family = new FontFamily("Segoe UI");
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            foreach (FloatingNumber number in frame)
            {
                int alpha = (int)(255 * Math.Clamp(number.Alpha, 0, 1));
                if (alpha <= 0)
                    continue;
                float x = game.X + number.X * game.Width - area.X;
                float y = game.Y + number.Y * game.Height - area.Y;
                using var path = new GraphicsPath();
                path.AddString(number.Label, family, (int)FontStyle.Bold, baseSize * number.Size, new PointF(x, y), format);
                using var outline = new Pen(Color.FromArgb(alpha * 3 / 4, 0, 0, 0), Math.Max(3f, baseSize / 7f)) { LineJoin = LineJoin.Round };
                using var fill = new SolidBrush(Color.FromArgb(alpha, number.Color));
                graphics.DrawPath(outline, path);
                graphics.FillPath(fill, path);
            }
        }
        return bitmap;
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
