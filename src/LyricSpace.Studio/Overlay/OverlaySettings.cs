using System.Diagnostics;
using System.Text.Json;

namespace LyricSpace.Studio.Overlay;

internal static class OverlaySettings
{
    private static string PathName =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyricSpace", "overlay-layout.json");

    public static Rectangle? Load(Size display)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Placement>(File.ReadAllText(PathName));
            if (saved is null || saved.MonitorWidth != display.Width || saved.MonitorHeight != display.Height)
            {
                return null;
            }

            var rectangle = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
            return OverlayPlacement.Fits(rectangle, display) ? rectangle : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(Size display, Rectangle rectangle)
    {
        string path = PathName;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var saved = new Placement(display.Width, display.Height, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            File.WriteAllText(temporary, JsonSerializer.Serialize(saved));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Overlay placement could not be saved: {0}", error.Message);
        }
    }

    private sealed record Placement(int MonitorWidth, int MonitorHeight, int X, int Y, int Width, int Height);
}
