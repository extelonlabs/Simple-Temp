using System.Runtime.InteropServices;

namespace Temperature;

/// <summary>
/// A thin text-only bar at the top middle of the screen, on top of other windows.
/// Double-click it to open the full window, or right-click it for a menu.
/// </summary>
public sealed class MiniBar : Form
{
    // These go in CreateParams. Using the normal TopMost setting would make the bar grab focus when it shows.
    private const int ExTopMost = 0x8;
    private const int ExToolWindow = 0x80;
    private const int ExNoActivate = 0x8000000;

    // The widest normal text. The bar keeps at least this width, so it doesn't wobble as numbers change.
    private const string WidestText = "CPU 100°C 100%  |  GPU 100°C 100%  |  RAM 100%";

    private const int CornerPreference = 33;
    private const int RoundCorners = 2;

    private readonly Font _font = new("Segoe UI Semibold", 10f);
    private readonly ToolTip _tip = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly float _scale;
    private string _text = "Starting...";

    /// <summary>The bar wants the full window back.</summary>
    public event Action? OpenFullWindow;

    public MiniBar()
    {
        _scale = DeviceDpi / 96f;

        Text = "Simple Temp mini bar";
        BackColor = MainForm.Background;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Height = S(26);
        Width = NeededWidth();

        _menu.Items.Add("Open full window", null, (_, _) => OpenFullWindow?.Invoke());
        _menu.Items.Add("Close app", null, (_, _) => Close());
        ContextMenuStrip = _menu;

        _tip.SetToolTip(this, "Double-click to open. Right-click for more.");
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Stays on top, stays out of Alt+Tab, and clicking it doesn't take focus from your game or work.
            cp.ExStyle |= ExTopMost | ExToolWindow | ExNoActivate;
            return cp;
        }
    }

    /// <summary>The text for one reading. "—" means no value. "HOT" means the temp bar would be red.</summary>
    public static string Format(Reading r) =>
        $"CPU {Temp(r.CpuTemp, MainForm.CpuHotAt)} {Usage(r.CpuLoad)}  |  " +
        $"GPU {Temp(r.GpuTemp, MainForm.GpuHotAt)} {Usage(r.GpuLoad)}  |  " +
        $"RAM {Usage(r.RamLoad)}";

    private static string Temp(float? temp, float hotAt) =>
        temp is { } t ? (t >= hotAt ? $"{t:0}°C HOT" : $"{t:0}°C") : "—";

    private static string Usage(float? load) => load is { } l ? $"{l:0}%" : "—";

    public void ShowOn(Screen screen)
    {
        Place(screen);
        Show();
    }

    public void SetText(string text)
    {
        _text = text;
        int width = NeededWidth();
        if (Width != width) Width = width;
        if (Visible) Place(Screen.FromControl(this));
        Invalidate();
    }

    private void Place(Screen screen)
    {
        var area = screen.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top);
    }

    private int S(float px) => (int)Math.Round(px * _scale);

    private int NeededWidth() =>
        Math.Max(TextRenderer.MeasureText(WidestText, _font).Width, TextRenderer.MeasureText(_text, _font).Width) + S(28);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Round corners on Windows 11. If Windows says no, they just stay square.
        int round = RoundCorners;
        _ = MainForm.DwmSetWindowAttribute(Handle, CornerPreference, ref round, sizeof(int));
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) OpenFullWindow?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(e.Graphics, _text, _font, ClientRectangle, MainForm.TextMain,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
            _tip.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
