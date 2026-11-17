namespace LyricSpace.Studio.Playback;

internal enum LocalAudioState { Unknown, Active, Inactive }

/// <summary>Repairs a stale Playing flag after a confirmed local stream stops.</summary>
internal sealed class LocalPauseClock
{
    private MediaClock _previous;
    private DateTimeOffset _stamp;
    private bool _observedActive;
    private bool _useLocalAnchor;

    public MediaClock Update(MediaClock reported, double rawPosition, DateTimeOffset stamp, LocalAudioState audio)
    {
        bool fresh = stamp != _stamp;
        _stamp = stamp;
        _observedActive |= audio == LocalAudioState.Active;
        if (audio == LocalAudioState.Unknown || fresh)
        {
            _useLocalAnchor = false;
        }

        MediaClock clock = reported;
        if (reported.Rate > 0 && _observedActive && audio == LocalAudioState.Inactive)
        {
            // A seek delivered during pause replaces the frozen position. Otherwise estimate the
            // stop between audio observations. The previous anchor already includes the sample age.
            double position = fresh ? rawPosition : _previous.At((_previous.Anchor + reported.Anchor) * 0.5);
            clock = reported with { Position = Math.Clamp(position, 0, reported.Duration), Rate = 0 };
            _useLocalAnchor = true;
        }
        else if (_useLocalAnchor && reported.Rate > 0 && audio == LocalAudioState.Active)
        {
            clock = reported with { Position = _previous.At(reported.Anchor) };
        }

        _previous = clock;
        return clock;
    }
}
