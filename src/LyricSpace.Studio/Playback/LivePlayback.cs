using System.Runtime.InteropServices;
using System.Text.Json;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Lyrics;
using Windows.Media.Control;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace LyricSpace.Studio.Playback;

internal sealed class LivePlayback : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private LiveFrame? _frame;
    private SongLook _look;
    private string _status = "Aguardando música";

    public LivePlayback(string cacheFolder, string localLyrics, uint seed, SongLook look, string? logPath)
    {
        _look = look;
        StreamWriter? log = logPath is null ? null : new StreamWriter(logPath);
        _worker = Task.Run(() => RunAsync(cacheFolder, localLyrics, seed, log, _stop.Token));
    }

    public LiveFrame? Frame => Volatile.Read(ref _frame);
    public string TrayStatus => Volatile.Read(ref _status);

    public SongLook Appearance => Volatile.Read(ref _look);

    public void SetAppearance(SongLook look) => Volatile.Write(ref _look, look);

    private async Task RunAsync(string cacheFolder, string localLyrics, uint seed, StreamWriter? log, CancellationToken stop)
    {
        using var trace = log;
        using var http = LrclibLyrics.CreateHttp();
        var lyrics = new LrclibLyrics(cacheFolder, http);
        GlobalSystemMediaTransportControlsSessionManager? manager = null;
        (string Source, Track Track)? identity = null;
        CancellationTokenSource? lookupCancellation = null;
        Task<ProceduralSong>? lookup = null;
        double retryAt = 0;
        var spotifyAudio = new SpotifyAudioSession();
        var spotifyClock = new LocalPauseClock();
        ProceduralSong? song = null;
        bool noLyrics = false;
        SongLook currentLook = Appearance;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                Reading? sample = null;
                try
                {
                    using var probe = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    probe.CancelAfter(TimeSpan.FromSeconds(3));
                    manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(probe.Token);
                    sample = await ReadAsync(manager, probe.Token);
                }
                catch (Exception error) when (error is COMException or UnauthorizedAccessException || (error is OperationCanceledException && !stop.IsCancellationRequested))
                {
                    manager = null;
                    Trace(trace, new { Event = "session_error", error.HResult });
                }

                var next = sample is { } media ? (media.Source, media.Track) : ((string, Track)?)null;
                SongLook look = Appearance;
                bool trackChanged = identity != next;
                if (trackChanged || currentLook != look)
                {
                    Volatile.Write(ref _frame, null);
                    CancelLookup();
                    identity = next;
                    song = null;
                    noLyrics = false;
                    retryAt = FramePacer.Now + 0.45;
                    currentLook = look;
                    if (trackChanged)
                    {
                        spotifyClock = new LocalPauseClock();
                    }
                }

                double now = FramePacer.Now;
                if (sample is { } reading)
                {
                    MediaClock clock = reading.Clock;
                    LocalAudioState audio = LocalAudioState.Unknown;
                    if (reading.Source.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                    {
                        audio = spotifyAudio.Read(now);
                        clock = spotifyClock.Update(clock, reading.ReportedPosition, reading.SampleTimestamp, audio);
                    }

                    if (lookup is { IsCompleted: true } completed)
                    {
                        try
                        {
                            song = await completed;
                            noLyrics = !song.HasText;
                        }
                        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
                        {
                            retryAt = now + 45;
                            Trace(trace, new { Event = "lookup_error", Error = error.GetType().Name });
                        }

                        lookupCancellation!.Dispose();
                        lookupCancellation = null;
                        lookup = null;
                    }

                    if (lookup is null && song is null && now >= retryAt)
                    {
                        lookupCancellation = CancellationTokenSource.CreateLinkedTokenSource(stop);
                        lookup = LoadSongAsync(lyrics, localLyrics, reading.Track, seed, look, lookupCancellation.Token);
                    }

                    double time = clock.At(now);
                    TimingKind? timing = song?.TimingAt(time);
                    Volatile.Write(ref _frame, new LiveFrame(clock, song));
                    Volatile.Write(ref _status, noLyrics ? "Sem letra sincronizada para esta faixa"
                        : song is null ? now + 1 < retryAt ? "Busca indisponível. Tentando novamente em instantes" : "Buscando letra"
                        : clock.Rate <= 0 ? "Reprodução pausada"
                        : timing == TimingKind.Estimated ? "Palavras com tempo estimado" : "Letra sincronizada");

                    if (trace is not null)
                    {
                        Trace(trace, new
                        {
                            Event = "playback", reading.Source, reading.Track, clock.Rate, Audio = audio.ToString(),
                            Position = time, Sample = reading.ReportedPosition, reading.SampleAge,
                            Cue = song?.CueAt(time), Timing = timing?.ToString(), Utc = DateTimeOffset.UtcNow
                        });
                    }
                }
                else
                {
                    Volatile.Write(ref _status, "Aguardando música com posição disponível");
                }

                await Task.Delay(250, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        finally
        {
            CancelLookup();
        }

        void CancelLookup()
        {
            lookupCancellation?.Cancel();
            lookupCancellation?.Dispose();
            if (lookup is { } abandoned)
            {
                _ = abandoned.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            lookup = null;
            lookupCancellation = null;
        }
    }

    private static async Task<Reading?> ReadAsync(GlobalSystemMediaTransportControlsSessionManager manager, CancellationToken stop)
    {
        var current = manager.GetCurrentSession();
        var session = current?.GetPlaybackInfo().PlaybackStatus == Status.Playing
            ? current
            : manager.GetSessions().FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == Status.Playing) ?? current;
        if (session is null)
        {
            return null;
        }

        var metadata = await session.TryGetMediaPropertiesAsync().AsTask(stop);
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var utc = DateTimeOffset.UtcNow;
        double now = FramePacer.Now;
        double duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
        if (playback.PlaybackStatus is not (Status.Playing or Status.Paused)
            || string.IsNullOrWhiteSpace(metadata?.Title) || duration is < 1 or > 3600
            || timeline.LastUpdatedTime == DateTimeOffset.MinValue)
        {
            return null;
        }

        double rate = playback.PlaybackStatus == Status.Playing ? playback.PlaybackRate is > 0 ? playback.PlaybackRate.Value : 1 : 0;
        double age = (utc - timeline.LastUpdatedTime).TotalSeconds;
        double position = (timeline.Position - timeline.StartTime).TotalSeconds;
        var clock = MediaClock.FromSnapshot(position, duration, age, now, rate);
        var track = new Track(metadata.Title.Trim(), metadata.Artist?.Trim() ?? "", metadata.AlbumTitle?.Trim() ?? "", duration);
        return new Reading(session.SourceAppUserModelId, track, clock, position, age, timeline.LastUpdatedTime);
    }

    private static async Task<ProceduralSong> LoadSongAsync(LrclibLyrics source, string localFolder, Track track,
        uint seed, SongLook look, CancellationToken stop)
    {
        string name = string.Concat($"{track.Artist} - {track.Title}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string path = Path.Combine(localFolder, name + ".lrc");
        LyricCue[]? local = null;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 4L * LrcParser.MaxCharacters)
            {
                local = LrcParser.Parse(await File.ReadAllTextAsync(path, stop));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An unavailable local override can be recovered from LRCLIB.
        }

        LyricCue[] lyrics = local?.Any(cue => cue.Text.Length > 0) == true ? local : await source.FindAsync(track, stop);
        stop.ThrowIfCancellationRequested();
        return new ProceduralSong(lyrics, track.Duration, seed, look);
    }
    private static void Trace<T>(StreamWriter? trace, T value)
    {
        if (trace is not null)
        {
            trace.WriteLine(JsonSerializer.Serialize(value));
            trace.Flush();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _ = _worker.ContinueWith(task =>
        {
            _ = task.Exception;
            _stop.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private readonly record struct Reading(string Source, Track Track, MediaClock Clock, double ReportedPosition, double SampleAge, DateTimeOffset SampleTimestamp);
}

internal sealed record LiveFrame(MediaClock Clock, ProceduralSong? Song);
