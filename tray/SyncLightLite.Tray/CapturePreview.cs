using System.Drawing.Drawing2D;

namespace SyncLightLite.Tray;

internal sealed class PixelMonitor : Control
{
    public PixelMonitor() { DoubleBuffered = true; TabStop = false; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var ink = new SolidBrush(Color.FromArgb(62, 47, 38));
        using var screen = new SolidBrush(Color.FromArgb(38, 112, 105));
        using var glow = new SolidBrush(Color.FromArgb(231, 173, 71));
        e.Graphics.FillRectangle(ink, 4, 2, 60, 40);
        e.Graphics.FillRectangle(screen, 8, 6, 52, 30);
        e.Graphics.FillRectangle(glow, 12, 10, 8, 8);
        e.Graphics.FillRectangle(glow, 24, 10, 8, 8);
        e.Graphics.FillRectangle(ink, 28, 42, 12, 8);
        e.Graphics.FillRectangle(ink, 16, 50, 36, 4);
    }
}

internal sealed class CapturePreview : Control
{
    private CaptureGeometry? _geometry;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public CaptureGeometry? Geometry { get => _geometry; set { _geometry = value; Invalidate(); } }
    public CapturePreview()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(247, 239, 219);
        AccessibleRole = AccessibleRole.Graphic;
        TabStop = false;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Geometry == null || Width < 60 || Height < 60) return;
        float ratio = Math.Min((Width - 36f) / Geometry.FrameWidth, (Height - 30f) / Geometry.FrameHeight);
        var bounds = new RectangleF((Width - Geometry.FrameWidth * ratio) / 2,
            (Height - Geometry.FrameHeight * ratio) / 2, Geometry.FrameWidth * ratio, Geometry.FrameHeight * ratio);
        using var border = new Pen(Color.FromArgb(62, 47, 38), 3);
        using var screen = new SolidBrush(Color.FromArgb(255, 253, 247));
        e.Graphics.FillRectangle(screen, bounds);
        e.Graphics.DrawRectangle(border, bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6);
        CaptureDrawing.Draw(e.Graphics, Geometry, bounds, false);
    }
}

internal static class CaptureDrawing
{
    public static void Draw(Graphics g, CaptureGeometry geometry, RectangleF bounds, bool overlay)
    {
        g.SmoothingMode = SmoothingMode.None;
        float sx = bounds.Width / geometry.FrameWidth, sy = bounds.Height / geometry.FrameHeight;
        using var sidePen = new Pen(Color.FromArgb(38, 112, 105), overlay ? 2 : 1);
        using var topPen = new Pen(Color.FromArgb(155, 91, 18), overlay ? 2 : 1);
        using var fill = new SolidBrush(Color.FromArgb(40, 38, 112, 105));
        foreach (var sample in geometry.Samples)
        {
            if (sample.Width <= 0 || sample.Height <= 0) continue;
            var r = new RectangleF(bounds.X + sample.X * sx, bounds.Y + sample.Y * sy, sample.Width * sx, sample.Height * sy);
            // Every rectangle is an actual inclusive mapper footprint, not a fictional full-width bottom edge.
            if (!overlay) g.FillRectangle(fill, r);
            g.DrawRectangle(sample.Edge == "Top" ? topPen : sidePen, r.X, r.Y, r.Width, r.Height);
        }
        if (geometry.Samples.Count > 0)
        {
            var first = geometry.Samples[0];
            var x = bounds.X + (first.X + first.Width / 2f) * sx;
            var y = bounds.Y + (first.Y + first.Height / 2f) * sy;
            using var marker = new SolidBrush(Color.FromArgb(62, 47, 38));
            g.FillRectangle(marker, x - 4, y - 4, 8, 8);
        }
    }
}
