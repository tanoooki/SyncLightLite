// Coordinates are inclusive pixel samples in the engine's 10x-downsampled frame.
// Keep in parity with src/mapper.js; capture-geometry.test.js executes both implementations.
internal sealed record CaptureSample(int X, int Y, int Width, int Height, string Edge);
internal sealed record CaptureGeometry(int FrameWidth, int FrameHeight, double EffectiveScale, IReadOnlyList<CaptureSample> Samples)
{
    public static CaptureGeometry Create(int screenWidth, int screenHeight, double scale, double offset,
        double margin, bool reverse = false, int ledCount = 71)
    {
        int width = Math.Max(1, Round(screenWidth / 10.0)), height = Math.Max(1, Round(screenHeight / 10.0));
        scale = Clamp(scale, .35, 1);
        offset = Clamp(offset, 0, .65);
        scale = Math.Min(scale, 1 - offset);
        int horizontalMargin = Round(width * Clamp(margin, 0, .25));
        int top = Round(ledCount * (double)width / (width + 2 * height));
        int left = (ledCount - top) / 2, right = ledCount - top - left;
        var samples = new List<CaptureSample>();
        int Y(double t) => Round((height - 1) * (offset + t * scale));
        int X(double t) => Round(horizontalMargin + (width - 1 - horizontalMargin * 2) * t);
        void Add(int cx, int cy, int rw, int rh, string edge)
        {
            int x0 = Math.Max(0, (int)Math.Floor(cx - rw / 2.0));
            int y0 = Math.Max(0, (int)Math.Floor(cy - rh / 2.0));
            int x1 = Math.Min(width - 1, (int)Math.Ceiling(cx + rw / 2.0));
            int y1 = Math.Min(height - 1, (int)Math.Ceiling(cy + rh / 2.0));
            samples.Add(new(x0, y0, Math.Max(0, x1 - x0 + 1), Math.Max(0, y1 - y0 + 1), edge));
        }
        for (int i = 0; i < right; i++) Add(width - 6 - horizontalMargin, Y(1 - (right <= 1 ? 0 : i / (double)(right - 1))), 6, 8, "Right");
        for (int i = 0; i < top; i++) Add(X(1 - (top <= 1 ? 0 : i / (double)(top - 1))), Y(0), 10, 6, "Top");
        for (int i = 0; i < left; i++) Add(horizontalMargin, Y(left <= 1 ? 0 : i / (double)(left - 1)), 6, 8, "Left");
        if (reverse) samples.Reverse();
        return new(width, height, scale, samples);
    }

    // JavaScript Math.round rounds positive .5 upward, unlike .NET's default banker's rounding.
    private static int Round(double value) => (int)Math.Floor(value + .5);
    private static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
