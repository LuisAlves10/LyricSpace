using System.Diagnostics;
using LyricSpace.Studio.Composition;

namespace LyricSpace.Studio.Overlay;

internal sealed class AppearancePreferences(string directory)
{
    private string CustomPath => Path.Combine(directory, "appearance.json");
    private string SelectionPath => Path.Combine(directory, "appearance-preset.txt");

    public SongLook Read(string? preset = null, string? custom = null)
    {
        if (custom is not null)
        {
            return SongLook.Read(custom);
        }

        string selection = preset ?? (File.Exists(SelectionPath) ? File.ReadAllText(SelectionPath).Trim() : "ivory");
        return selection == "custom" ? SongLook.Read(CustomPath) : SongLook.Preset(selection);
    }

    public SongLook ReadDefault()
    {
        try
        {
            return Read();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.Text.Json.JsonException)
        {
            return SongLook.Default;
        }
    }

    public SongLook Select(string name)
    {
        SongLook look = Read(name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(SelectionPath, name);
        return look;
    }

    public void Edit(SongLook current)
    {
        if (!File.Exists(CustomPath))
        {
            current.WriteTemplate(CustomPath);
        }
        Process.Start(new ProcessStartInfo(CustomPath) { UseShellExecute = true });
    }
}
