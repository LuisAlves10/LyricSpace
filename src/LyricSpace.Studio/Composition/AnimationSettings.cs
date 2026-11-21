using System.Text.Json;

namespace LyricSpace.Studio.Composition;

internal sealed record AnimationSettings
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static AnimationSettings Default { get; } = new();

    public bool EstimateWords { get; init; } = true;
    public string Reveal { get; init; } = "some";
    public float Motion { get; init; } = 1.08f;
    public float Tracking { get; init; } = 0.034f;
    public float ActiveTracking { get; init; } = 0.017f;

    public static AnimationSettings Read(string path)
    {
        if (!File.Exists(path))
            return Default;

        var settings = JsonSerializer.Deserialize<AnimationSettings>(File.ReadAllText(path), Json)
            ?? throw new ArgumentException("O arquivo de animação está vazio.");
        if (settings.Reveal is not ("none" or "some" or "all"))
            throw new ArgumentException("Reveal precisa ser none, some ou all.");
        Check(settings.Motion, 1.6f, nameof(Motion));
        Check(settings.Tracking, 0.055f, nameof(Tracking));
        Check(settings.ActiveTracking, 0.035f, nameof(ActiveTracking));
        return settings;
    }

    private static void Check(float value, float max, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > max)
            throw new ArgumentException($"{name} precisa estar entre 0 e {max}.");
    }
}
