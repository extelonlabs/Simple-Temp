using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Temperature;

/// <summary>
/// The full window. It reads the sensors once a second on a background thread, draws the
/// temperature and usage bars, and owns the mini bar, tray icon and startup options.
/// </summary>
public sealed class MainForm : Form
{
    private const int UpdateMs = 1000;

    internal const float CpuWarnAt = 80;
    internal const float CpuHotAt = 90;
    internal const float GpuHotAt = 85;

    // Dark colors. Status colors go on the bar, and always come with a word.
    internal static readonly Color Background = ColorTranslator.FromHtml("#1a1a19");
    internal static readonly Color TextMain = ColorTranslator.FromHtml("#f0efec");
    private static readonly Color TextSoft = ColorTranslator.FromHtml("#c3c2b7");
    private static readonly Color Track = ColorTranslator.FromHtml("#383835");
    private static readonly Color UsageBar = ColorTranslator.FromHtml("#3987e5");
    private static readonly Color Good = ColorTranslator.FromHtml("#0ca30c");
    internal static readonly Color Warning = ColorTranslator.FromHtml("#fab219");
    internal static readonly Color Critical = ColorTranslator.FromHtml("#d03b3b");

    private readonly Font _headerFont = new("Segoe UI Semibold", 10f);
    private readonly Font _labelFont = new("Segoe UI", 10f);
    private readonly Font _valueFont = new("Segoe UI Semibold", 16f);
    private readonly Font _wordFont = new("Segoe UI", 9f);

    private readonly CancellationTokenSource _stop = new();
    private readonly bool _isAdmin = IsAdmin();
    private readonly Button _adminButton;
    private readonly Button _miniButton;
    private readonly MiniBar _miniBar = new();
    private readonly TrayIcon _tray = new();
    private readonly float _scale;
    private readonly bool _bringToFront;
    private readonly bool _startMini;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _startAsMini;
    private bool _loadingOptions;
    private bool _showAdminButton;

    private Task? _poll;
    private Reading? _reading;
    private string? _error;

    public MainForm(bool bringToFront, bool startMini)
    {
        _bringToFront = bringToFront;
        _startMini = startMini;
        _scale = DeviceDpi / 96f;
        if (startMini) Opacity = 0; // So the full window doesn't flash before the bar shows.

        Text = "Simple Temp";
        LoadWindowIcon();
        BackColor = Background;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _miniButton = MakeButton("Mini bar");
        _miniButton.Click += (_, _) => EnterMiniBar();

        _adminButton = MakeButton("Run as admin to show CPU temp");
        _adminButton.Visible = false;
        _adminButton.Click += (_, _) => RelaunchAsAdmin();

        _miniBar.OpenFullWindow += LeaveMiniBar;
        // _stop is only cancelled once the app is already closing, so this can't run twice.
        _miniBar.FormClosed += (_, _) => { if (!_stop.IsCancellationRequested) Close(); };

        _tray.OpenFullWindow += LeaveMiniBar;
        _tray.ShowMiniBar += EnterMiniBar;
        _tray.CloseApp += Close;

        _startWithWindows = MakeCheckBox("Start with Windows");
        _startAsMini = MakeCheckBox("Start as mini bar");
        LoadStartupOptions();
        _startWithWindows.CheckedChanged += (_, _) => SaveStartupOptions();
        _startAsMini.CheckedChanged += (_, _) => SaveStartupOptions();

        LayOut();
    }

    private void LoadWindowIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("SimpleTemp.ico");
        if (stream is not null) Icon = new Icon(stream);
    }

    private CheckBox MakeCheckBox(string text)
    {
        var box = new CheckBox { Text = text, ForeColor = TextSoft, Font = _labelFont };
        Controls.Add(box);
        return box;
    }

    private void LoadStartupOptions()
    {
        bool can = AutoStart.CanChange;
        var (on, mini) = can ? AutoStart.Read() : (false, false);
        _loadingOptions = true;
        _startWithWindows.Checked = on;
        _startAsMini.Checked = mini;
        _loadingOptions = false;
        _startWithWindows.Visible = can;
        _startAsMini.Visible = can && on;
    }

    private void SaveStartupOptions()
    {
        if (_loadingOptions) return;
        try
        {
            AutoStart.Set(_startWithWindows.Checked, _startAsMini.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't change the startup option.\n\n" + ex.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        LoadStartupOptions(); // Always show what is really saved, even if that just failed.
    }

    private Button MakeButton(string text)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Track,
            ForeColor = TextMain,
            Font = _labelFont,
        };
        button.FlatAppearance.BorderSize = 0;
        Controls.Add(button);
        return button;
    }

    private int S(float px) => (int)Math.Round(px * _scale);

    /// <summary>Sizes the window. It only has room for the admin button when that button is showing.</summary>
    private void LayOut()
    {
        int y = S(392);
        _miniButton.SetBounds(S(16), y, S(368), S(36));
        y += S(44);
        if (_showAdminButton)
        {
            _adminButton.SetBounds(S(16), y, S(368), S(36));
            y += S(44);
        }
        if (AutoStart.CanChange)
        {
            // Room for both, so the window doesn't change size when the second one appears.
            _startWithWindows.SetBounds(S(18), y, S(366), S(26));
            y += S(28);
            _startAsMini.SetBounds(S(40), y, S(344), S(26));
            y += S(28);
        }
        ClientSize = new Size(S(400), y + S(6));
    }

    private void EnterMiniBar()
    {
        _miniBar.ShowOn(Screen.FromControl(this));
        Hide();
    }

    private void LeaveMiniBar()
    {
        _miniBar.Hide();
        Show();
        // The tray icon can ask for the window while it is minimized.
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Makes the title bar dark too. If Windows says no, nothing bad happens.
        int on = 1;
        _ = DwmSetWindowAttribute(Handle, DwmUseDarkMode, ref on, sizeof(int));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_bringToFront) RaiseAfterTaskStart();
        if (_startMini)
        {
            EnterMiniBar();
            Opacity = 1;
        }
        var token = _stop.Token;
        _poll = Task.Run(() => PollLoop(token));
    }

    /// <summary>
    /// Windows opens apps started by a task behind whatever you are using.
    /// Stay on top for a moment, because doing this right as the window appears does not stick.
    /// </summary>
    private void RaiseAfterTaskStart()
    {
        TopMost = true;
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            TopMost = false;
            Activate();
        };
        timer.Start();
    }

    /// <summary>Runs on a background thread, so the window never freezes while sensors are read.</summary>
    private void PollLoop(CancellationToken token)
    {
        try
        {
            using var reader = new SensorReader();
            while (!token.IsCancellationRequested)
            {
                var reading = reader.Read();
                BeginInvoke(() => ShowReading(reading));
                token.WaitHandle.WaitOne(UpdateMs);
            }
        }
        catch (Exception ex)
        {
            // Show it. Old numbers left on screen would look live, and that would be a lie.
            BeginInvoke(() => ShowError(ex.Message));
        }
    }

    private void ShowReading(Reading reading)
    {
        _reading = reading;
        _miniBar.SetText(MiniBar.Format(reading));
        _tray.Show(reading);
        // Keep our own flag: a button says it isn't visible whenever its window is hidden.
        bool showButton = reading.CpuTemp is null && !_isAdmin;
        if (_showAdminButton != showButton)
        {
            _showAdminButton = showButton;
            _adminButton.Visible = showButton;
            LayOut();
        }
        Invalidate();
    }

    private void ShowError(string message)
    {
        _reading = null;
        _error = message;
        _miniBar.SetText("Can't read the sensors. Double-click for details.");
        _tray.ShowError("Can't read the sensors. Click for details.");
        Invalidate();
    }

    private void RelaunchAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // They said no to the Windows prompt. Keep going with what we have.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _stop.Cancel();
        _poll?.Wait(3000); // Lets the reader finish and let go of the sensors.
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_error is not null)
        {
            DrawMessage(g, "Can't read the sensors.\n\n" + _error);
            return;
        }
        if (_reading is not { } r)
        {
            DrawMessage(g, "Starting up...");
            return;
        }

        int y = S(16);
        y = DrawHeader(g, r.CpuName, y);
        y = DrawTempRow(g, r.CpuTemp, warnAt: CpuWarnAt, hotAt: CpuHotAt, y);
        y = DrawUsageRow(g, "Usage", r.CpuLoad, y);
        y += S(12);
        y = DrawHeader(g, r.GpuName, y);
        y = DrawTempRow(g, r.GpuTemp, warnAt: 75, hotAt: GpuHotAt, y);
        y = DrawUsageRow(g, "Usage", r.GpuLoad, y);
        y += S(12);
        y = DrawHeader(g, "RAM", y);
        string ramLabel = r.RamUsedGb is { } used && r.RamTotalGb is { } total
            ? $"{used:0.0} of {total:0.0} GB used"
            : "Usage";
        DrawUsageRow(g, ramLabel, r.RamLoad, y);
    }

    private void DrawMessage(Graphics g, string message)
    {
        var box = new Rectangle(S(16), S(16), ClientSize.Width - S(32), ClientSize.Height - S(32));
        TextRenderer.DrawText(g, message, _labelFont, box, TextSoft, TextFormatFlags.WordBreak);
    }

    private int DrawHeader(Graphics g, string text, int y)
    {
        var box = new Rectangle(S(16), y, ClientSize.Width - S(32), S(24));
        TextRenderer.DrawText(g, text, _headerFont, box, TextMain,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        return y + S(26);
    }

    private int DrawTempRow(Graphics g, float? temp, float warnAt, float hotAt, int y)
    {
        if (temp is not { } t)
        {
            string why = _isAdmin ? "not available" : "needs admin";
            return DrawRow(g, "Temperature", "—", why, 0, Good, y);
        }

        var (word, color) = t >= hotAt ? ("Hot", Critical)
            : t >= warnAt ? ("Warm", Warning)
            : ("OK", Good);
        return DrawRow(g, "Temperature", $"{t:0} °C", word, t / 100f, color, y);
    }

    private int DrawUsageRow(Graphics g, string label, float? load, int y) =>
        load is { } l
            ? DrawRow(g, label, $"{l:0} %", "", l / 100f, UsageBar, y)
            : DrawRow(g, label, "—", "", 0, UsageBar, y);

    /// <summary>Label on the left, big value on the right, thin bar below.</summary>
    private int DrawRow(Graphics g, string label, string value, string word, float fraction, Color barColor, int y)
    {
        int left = S(16);
        int width = ClientSize.Width - S(32);
        var textBox = new Rectangle(left, y, width, S(34));

        TextRenderer.DrawText(g, label, _labelFont, textBox, TextSoft,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var valueBox = textBox;
        TextRenderer.DrawText(g, value, _valueFont, valueBox, TextMain,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (word.Length > 0)
        {
            int valueWidth = TextRenderer.MeasureText(g, value, _valueFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            var wordBox = new Rectangle(left, y, width - valueWidth - S(8), S(34));
            TextRenderer.DrawText(g, word, _wordFont, wordBox, TextSoft,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        DrawBar(g, new Rectangle(left, y + S(36), width, S(6)), fraction, barColor);
        return y + S(50);
    }

    private static void DrawBar(Graphics g, Rectangle track, float fraction, Color color)
    {
        using (var brush = new SolidBrush(Track))
        using (var path = Rounded(track))
            g.FillPath(brush, path);

        int fill = (int)(track.Width * Math.Clamp(fraction, 0f, 1f));
        if (fill <= 0) return;
        // Never thinner than the bar is tall, so a small value still looks like a dot and not a glitch.
        fill = Math.Max(fill, track.Height);
        using (var brush = new SolidBrush(color))
        using (var path = Rounded(new Rectangle(track.X, track.Y, fill, track.Height)))
            g.FillPath(brush, path);
    }

    private static GraphicsPath Rounded(Rectangle r)
    {
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, r.Height, r.Height, 90, 180);
        path.AddArc(r.Right - r.Height, r.Y, r.Height, r.Height, 270, 180);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop.Dispose();
            _miniBar.Dispose();
            _tray.Dispose();
            _headerFont.Dispose();
            _labelFont.Dispose();
            _valueFont.Dispose();
            _wordFont.Dispose();
        }
        base.Dispose(disposing);
    }

    internal static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private const int ErrorCancelled = 1223;
    private const int DwmUseDarkMode = 20;

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
