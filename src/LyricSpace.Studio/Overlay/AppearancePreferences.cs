using System.Diagnostics;
using LyricSpace.Studio.Composition;

namespace LyricSpace.Studio.Overlay;

internal sealed class AppearancePreferences(string directory)
{
    private const string BaselinePreset = "visual-baseline-v1";
    private string CustomPath => Path.Combine(directory, "appearance.json");
    private string SelectionPath => Path.Combine(directory, "appearance-preset.txt");

    public SongLook Read(string? preset = null, string? custom = null)
    {
        if (custom is not null)
        {
            return SongLook.Read(custom);
        }

        string selection = preset ?? (File.Exists(SelectionPath) ? File.ReadAllText(SelectionPath).Trim() : BaselinePreset);
        return selection switch
        {
            BaselinePreset => SongLook.Read(Path.Combine(AppContext.BaseDirectory, "white-estimated.json")),
            "custom" => SongLook.Read(CustomPath),
            _ => SongLook.Preset(selection)
        };
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
            return Read(BaselinePreset);
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
