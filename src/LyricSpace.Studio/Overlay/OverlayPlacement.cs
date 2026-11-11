using System.Drawing;

namespace LyricSpace.Studio.Overlay;

internal static class OverlayPlacement
{
    public static Rectangle Default(Size display)
    {
        const double aspect = 16.0 / 9;
        int width = Math.Max(1, (int)Math.Round(0.42 * Math.Min(display.Width, display.Height * aspect)));
        int height = Math.Max(1, (int)Math.Round(width / aspect));
        int margin = (int)Math.Round(Math.Min(display.Width, display.Height) * 0.039);
        return new Rectangle(Math.Max(0, display.Width - width - margin), Math.Max(0, display.Height - height - margin), width, height);
    }

    public static bool Fits(Rectangle zone, Size display)
        => zone.X >= 0 && zone.Y >= 0 && zone.Width > 0 && zone.Height > 0
            && (long)zone.X + zone.Width <= display.Width && (long)zone.Y + zone.Height <= display.Height;

    public static Point Corner(Rectangle zone, Point cursor, int grip)
    {
        int x = cursor.X - zone.Left < grip ? -1 : zone.Right - cursor.X <= grip ? 1 : 0;
        int y = cursor.Y - zone.Top < grip ? -1 : zone.Bottom - cursor.Y <= grip ? 1 : 0;
        return x == 0 || y == 0 ? Point.Empty : new Point(x, y);
    }

    public static Rectangle Drag(Rectangle zone, Point delta, Point corner, Size display)
    {
        if (corner.IsEmpty)
        {
            return new Rectangle(
                Math.Clamp(zone.Left + delta.X, 0, display.Width - zone.Width),
                Math.Clamp(zone.Top + delta.Y, 0, display.Height - zone.Height), zone.Width, zone.Height);
        }

        int anchorX = corner.X < 0 ? zone.Right : zone.Left;
        int anchorY = corner.Y < 0 ? zone.Bottom : zone.Top;
        double width = zone.Width;
        double height = zone.Height;
        double maximum = Math.Min((corner.X < 0 ? anchorX : display.Width - anchorX) / width,
            (corner.Y < 0 ? anchorY : display.Height - anchorY) / height);
        double minimum = Math.Min(1, Math.Min(maximum, Math.Max(240 / width, 135 / height)));
        double offset = (corner.X * delta.X * width + corner.Y * delta.Y * height) / (width * width + height * height);
        double scale = Math.Clamp(1 + offset, minimum, maximum);
        int nextWidth = Math.Max(1, (int)Math.Round(width * scale));
        int nextHeight = Math.Max(1, (int)Math.Round(height * scale));
        return new Rectangle(corner.X < 0 ? anchorX - nextWidth : anchorX, corner.Y < 0 ? anchorY - nextHeight : anchorY, nextWidth, nextHeight);
    }
}
