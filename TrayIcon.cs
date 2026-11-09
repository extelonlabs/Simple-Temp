using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Temperature;

/// <summary>The icon by the clock. It shows the CPU temp as a number, and all the numbers on hover.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int MaxTooltipLength = 127; // Windows refuses anything longer.

    private readonly NotifyIcon _notify = new();
    private readonly ContextMenuStrip _menu = new();
    private IntPtr _iconHandle;
    private string? _drawn;

    public event Action? OpenFullWindow;
    public event Action? ShowMiniBar;
    public event Action? CloseApp;

    public TrayIcon()
    {
        _menu.Items.Add("Open full window", null, (_, _) => OpenFullWindow?.Invoke());
        _menu.Items.Add("Mini bar", null, (_, _) => ShowMiniBar?.Invoke());
        _menu.Items.Add("Close app", null, (_, _) => CloseApp?.Invoke());
        _notify.ContextMenuStrip = _menu;
        _notify.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenFullWindow?.Invoke(); };

        SetIcon("...", MainForm.Background, MainForm.TextMain);
        SetTooltip("Starting...");
        _notify.Visible = true;
    }

    public void Show(Reading r)
    {
        SetTooltip(MiniBar.Format(r));
        if (r.CpuTemp is not { } t)
        {
            SetIcon("--", MainForm.Background, MainForm.TextMain);
        }
        else
        {
            // Same levels as the temperature bar in the full window.
            string number = t.ToString("0");
            if (t >= MainForm.CpuHotAt) SetIcon(number, MainForm.Critical, Color.White);
            else if (t >= MainForm.CpuWarnAt) SetIcon(number, MainForm.Warning, Color.Black);
            else SetIcon(number, MainForm.Background, MainForm.TextMain);
        }
    }

    public void ShowError(string message)
    {
        SetTooltip(message);
        SetIcon("!", MainForm.Critical, Color.White);
    }

    private void SetTooltip(string text) =>
        _notify.Text = text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;

    private void SetIcon(string text, Color back, Color fore)
    {
        // Icons cost system resources, so only make a new one when it would look different.
        string key = $"{text}|{back.ToArgb()}";
        if (key == _drawn) return;
        _drawn = key;

        using var bitmap = Draw(text, back, fore, SystemInformation.SmallIconSize.Width);
        IntPtr old = _iconHandle;
        _iconHandle = bitmap.GetHicon();
        _notify.Icon = Icon.FromHandle(_iconHandle); // Doesn't own the handle. We destroy that ourselves.
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    /// <summary>A rounded square with text. The square keeps it readable on light and dark taskbars.</summary>
    internal static Bitmap Draw(string text, Color back, Color fore, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float arc = size * 0.35f;
        using (var path = new GraphicsPath())
        using (var brush = new SolidBrush(back))
        {
            path.AddArc(0, 0, arc, arc, 180, 90);
            path.AddArc(size - arc - 1, 0, arc, arc, 270, 90);
            path.AddArc(size - arc - 1, size - arc - 1, arc, arc, 0, 90);
            path.AddArc(0, size - arc - 1, arc, arc, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }

        // Three digits (100 °C) only fit in a smaller font.
        float fontSize = size * (text.Length > 2 ? 0.52f : 0.72f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(fore);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), format);
        return bitmap;
    }

    public void Dispose()
    {
        // Hide first, or a dead icon stays by the clock until the mouse goes over it.
        _notify.Visible = false;
        _notify.Dispose();
        _menu.Dispose();
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
