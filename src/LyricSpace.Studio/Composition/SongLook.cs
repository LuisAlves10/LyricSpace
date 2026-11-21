using System.Numerics;
using System.Text.Json;

namespace LyricSpace.Studio.Composition;

internal sealed record SongLook
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public string Base { get; init; } = "#F4EBDD";
    public string Secondary { get; init; } = "#C1C8D2";
    public string Accent { get; init; } = "#FFC078";
    public string DisplayFont { get; init; } = "Arial";
    public string BodyFont { get; init; } = "Bahnschrift";
    public float Glow { get; init; } = 0.62f;
    public float AmbientGlow { get; init; } = 0.04f;
    public float GlowRadius { get; init; } = 10;

    public static SongLook Default { get; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public WordPalette Palette => new(ParseColor(Base), ParseColor(Secondary), ParseColor(Accent));

    public static SongLook Preset(string name) => name switch
    {
        "ivory" => Default,
        "white" => new()
        {
            Base = "#FFFFFF", Secondary = "#D8D8D8", Accent = "#FFFFFF",
            Glow = 1.05f, AmbientGlow = 0.16f, GlowRadius = 13
        },
        "ice" => new()
        {
            Base = "#D7E8F7", Secondary = "#A6BBD2", Accent = "#E9F5FF",
            Glow = 0.78f, AmbientGlow = 0.1f, GlowRadius = 11
        },
        _ => throw new ArgumentException($"Preset desconhecido: {name}.")
    };

    public static SongLook Read(string path)
    {
        var look = JsonSerializer.Deserialize<SongLook>(File.ReadAllText(path), Json)
            ?? throw new ArgumentException("O arquivo de aparência está vazio.");
        _ = look.Palette;
        Check(look.Glow, 0, 1.5f, nameof(Glow));
        Check(look.AmbientGlow, 0, 0.4f, nameof(AmbientGlow));
        Check(look.GlowRadius, 2, 22, nameof(GlowRadius));
        if (string.IsNullOrWhiteSpace(look.DisplayFont) || string.IsNullOrWhiteSpace(look.BodyFont))
        {
            throw new ArgumentException("Informe nomes de fontes.");
        }

        return look;
    }

    public void WriteTemplate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew);
        JsonSerializer.Serialize(file, this, Json);
    }

    private static Vector3 ParseColor(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            throw new ArgumentException("Cores devem usar #RRGGBB.");
        }
        return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
    }

    private static void Check(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new ArgumentException($"{name} precisa estar entre {min} e {max}.");
        }
    }
}

internal readonly record struct WordPalette(Vector3 Base, Vector3 Secondary, Vector3 Accent);
