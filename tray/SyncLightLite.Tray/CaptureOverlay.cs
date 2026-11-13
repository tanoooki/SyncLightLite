using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SyncLightLite.Tray;

internal sealed class CaptureOverlay : Form
{
    private CaptureGeometry? _geometry;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CaptureGeometry? Geometry { get => _geometry; set { _geometry = value; Invalidate(); } }
    public CaptureOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        TopMost = true;
        DoubleBuffered = true;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED
            cp.ExStyle |= 0x080800A0;
            return cp;
        }
    }
    public bool TryShowProtected(Rectangle bounds)
    {
        Hide();
        Bounds = bounds;
        // Before Windows 10 2004, 0x11 degrades to WDA_MONITOR (black capture).
        // Fail closed rather than showing lines that could feed back into LEDs.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return false;
        try
        {
            const uint WdaExcludeFromCapture = 0x11;
            if (!SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture) ||
                !GetWindowDisplayAffinity(Handle, out var affinity) || affinity != WdaExcludeFromCapture) return false;
            Show();
            return true;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or Win32Exception)
        {
            Hide();
            return false;
        }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
        if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Geometry != null) CaptureDrawing.Draw(e.Graphics, Geometry, ClientRectangle, true);
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
}
