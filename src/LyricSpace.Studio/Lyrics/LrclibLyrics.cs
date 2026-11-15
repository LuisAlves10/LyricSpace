using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyricSpace.Studio.Lyrics;

internal sealed class LrclibLyrics(string cacheFolder, HttpClient http)
{
    private const int CacheEntries = 256;
    private static readonly TimeSpan HitLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan MissLifetime = TimeSpan.FromMinutes(20);
    private long _retryAfterTicks;

    public static HttpClient CreateHttp()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://lrclib.net"), Timeout = TimeSpan.FromSeconds(10) };
        var version = typeof(LrclibLyrics).Assembly.GetName().Version;
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"LyricSpace/{version} (https://github.com/LuisAlves10/LyricSpace)");
        return client;
    }

    public async Task<LyricCue[]> FindAsync(Track track, CancellationToken cancellation)
    {
        string title = Uri.EscapeDataString(track.Title);
        string artist = Uri.EscapeDataString(track.Artist);
        string album = Uri.EscapeDataString(track.Album);
        string duration = Math.Round(track.Duration).ToString(CultureInfo.InvariantCulture);
        string fields = $"track_name={title}&artist_name={artist}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{fields}&album_name={album}&duration={duration}")));
        string path = Path.Combine(cacheFolder, hash + ".lrc");
        if (ReadCache(path) is { } cached)
        {
            return LrcParser.Parse(cached);
        }

        string? text = null;
        if (track.Artist.Length > 0)
        {
            var exact = await GetAsync<Entry>($"/api/get?{fields}&album_name={album}&duration={duration}", cancellation);
            if (exact is not null && Matches(exact, track))
            {
                text = exact.SyncedLyrics;
            }
        }

        if (!HasTimedText(text))
        {
            // Sequential fallback keeps requests bounded. Rank different releases by album, then duration.
            var candidates = await GetAsync<Entry[]>(track.Artist.Length == 0
                ? $"/api/search?track_name={title}" : $"/api/search?{fields}", cancellation);
            var ranked = (candidates ?? []).Where(entry => Matches(entry, track) && HasTimedText(entry.SyncedLyrics))
                .OrderByDescending(entry => Same(entry.AlbumName, track.Album))
                .ThenBy(entry => Math.Abs(entry.Duration - track.Duration)).ToArray();

            if (track.Artist.Length > 0 || ranked.Select(entry => entry.ArtistName).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() <= 1)
            {
                text = ranked.FirstOrDefault()?.SyncedLyrics;
            }
        }

        cancellation.ThrowIfCancellationRequested();
        string lrc = HasTimedText(text) ? text! : "";
        WriteCache(path, lrc);
        return LrcParser.Parse(lrc);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellation)
    {
        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _retryAfterTicks))
        {
            throw new HttpRequestException("LRCLIB requested a retry delay.", null, HttpStatusCode.TooManyRequests);
        }

        using HttpResponseMessage response = await http.GetAsync(url, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var until = retry?.Date ?? DateTimeOffset.UtcNow + (retry?.Delta ?? TimeSpan.FromMinutes(1));
            Interlocked.Exchange(ref _retryAfterTicks, until.UtcTicks);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellation);
    }

    private static bool Matches(Entry entry, Track track)
        => Math.Abs(entry.Duration - track.Duration) <= 2.5
            && Same(entry.TrackName, track.Title)
            && (track.Artist.Length == 0 || Same(entry.ArtistName, track.Artist));

    private static bool Same(string? a, string b)
        => string.Equals(a?.Trim().Normalize(), b.Trim().Normalize(), StringComparison.OrdinalIgnoreCase);

    private static bool HasTimedText(string? text)
        => text is not null && LrcParser.Parse(text).Any(cue => cue.Text.Length > 0);

    private static string? ReadCache(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists && file.Length <= 4L * LrcParser.MaxCharacters
                && DateTime.UtcNow - file.LastWriteTimeUtc < (file.Length == 0 ? MissLifetime : HitLifetime))
            {
                return File.ReadAllText(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A missing or inaccessible cache can be regenerated from the provider.
        }

        return null;
    }

    private void WriteCache(string path, string lrc)
    {
        string temporary = Path.Combine(cacheFolder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(cacheFolder);
            File.WriteAllText(temporary, lrc);
            File.Move(temporary, path, overwrite: true);
            foreach (FileInfo expired in new DirectoryInfo(cacheFolder).EnumerateFiles("*.lrc")
                .OrderByDescending(file => file.LastWriteTimeUtc).Skip(CacheEntries))
            {
                expired.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Network results still work if disk caching is unavailable.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Entry(
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("albumName")] string? AlbumName,
        [property: JsonPropertyName("duration")] double Duration,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics);
}
