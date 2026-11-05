namespace LyricSpace.Studio.Playback;

internal sealed class PlaybackClock(double duration)
{
    private double _position;
    private double _wallAnchor;
    private double _speed = 1;

    public bool IsPlaying { get; private set; }

    public double PositionAt(double now)
        => IsPlaying ? Math.Min(duration, _position + (now - _wallAnchor) * _speed) : _position;

    public void Play(double now)
    {
        if (IsPlaying)
        {
            return;
        }

        _position = _position >= duration ? 0 : _position;
        _wallAnchor = now;
        IsPlaying = true;
    }

    public void Pause(double now)
    {
        _position = PositionAt(now);
        IsPlaying = false;
    }

    public void Seek(double position, double now)
    {
        _position = Math.Clamp(position, 0, duration);
        _wallAnchor = now;
    }

    public void SetSpeed(double speed, double now)
    {
        Seek(PositionAt(now), now);
        _speed = speed;
    }
}
