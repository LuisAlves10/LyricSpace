namespace LyricSpace.Studio.Playback;

internal readonly record struct MediaClock(double Position, double Duration, double Anchor, double Rate)
{
    public static MediaClock FromSnapshot(double position, double duration, double age, double now, double rate)
        => new(Math.Clamp(position + Math.Max(0, age) * rate, 0, duration), duration, now, rate);

    public double At(double now)
        => Math.Clamp(Position + Math.Max(0, now - Anchor) * Rate, 0, Duration);
}
