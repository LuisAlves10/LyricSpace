using System.Net;
using System.Numerics;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Lyrics;
using LyricSpace.Studio.Playback;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lenoz.Motion.Tests;

[TestClass]
public sealed class LiveLyricsTests
{
    [TestMethod]
    public void LrcParsesRepeatedTimestampsOffsetAndExplicitSilence()
    {
        LyricCue[] cues = LrcParser.Parse("\uFEFF[ar:Artist]\r\n[00:01.50][00:08.5]Sentido\r\n[00:04.250]\r\n[offset:+250]");
        CollectionAssert.AreEqual(
            new[] { new LyricCue(1.25, "Sentido"), new LyricCue(4, ""), new LyricCue(8.25, "Sentido") }, cues);
    }

    [TestMethod]
    public void ReplacementMediaAnchorsSupportPauseResumeAndBothSeekDirections()
    {
        var playing = MediaClock.FromSnapshot(9, 200, 1.25, 50, 1);
        Assert.AreEqual(11.25, playing.At(51), 1e-6);
        var paused = MediaClock.FromSnapshot(11.25, 200, 10, 51, 0);
        Assert.AreEqual(11.25, paused.At(60), 1e-6);
        var forward = MediaClock.FromSnapshot(85, 200, 0, 60, 1);
        Assert.AreEqual(86, forward.At(61), 1e-6);
        var backward = MediaClock.FromSnapshot(5, 200, 0, 61, 1);
        Assert.AreEqual(6, backward.At(62), 1e-6);
        Assert.AreEqual(200, backward.At(500), 1e-6);
    }

    [TestMethod]
    public void SpotifyFallbackFreezesAndResumesEvenWithAStalePlayingFlag()
    {
        var fallback = new LocalPauseClock();
        var stamp = DateTimeOffset.UnixEpoch;
        MediaClock Read(double now, double position, DateTimeOffset sample, LocalAudioState audio)
            => fallback.Update(new MediaClock(position, 180, now, 1), position, sample, audio);

        Assert.AreEqual(20, Read(10, 20, stamp, LocalAudioState.Active).Position);
        var stalled = Read(10.25, 20.25, stamp, LocalAudioState.Inactive);
        Assert.AreEqual(0, stalled.Rate);
        Assert.AreEqual(20.125, stalled.At(55));
        var seek = Read(12, 65, stamp.AddSeconds(2), LocalAudioState.Inactive);
        Assert.AreEqual(65, seek.At(55));
        var resumed = Read(18, 71, stamp.AddSeconds(2), LocalAudioState.Active);
        Assert.AreEqual(65, resumed.Position);
        Assert.AreEqual(66, resumed.At(19));
        var official = Read(19, 70, stamp.AddSeconds(9), LocalAudioState.Active);
        Assert.AreEqual(70, official.Position);
    }

    [TestMethod]
    public void SceneChangePreservesCameraAndBothGroupsOverlap()
    {
        var lyrics = Enumerable.Range(0, 9).Select(i => new LyricCue(i * 2, $"Ponte em movimento {i}")).ToArray();
        var song = new ProceduralSong(lyrics, 20, 12345);
        var reproduction = new ProceduralSong(lyrics, 20, 12345);
        var variant = new ProceduralSong(lyrics, 20, 48);
        var outgoing = song.SceneAt(7.99)!;
        var incoming = song.SceneAt(8)!;
        Assert.AreNotSame(outgoing, incoming);
        Assert.AreEqual(4, song.CueAt(8));
        Assert.AreEqual(2, song.CueAt(4.1));
        Assert.IsTrue(incoming.Planes.Any(plane => plane.Text?.Text.EndsWith("3", StringComparison.Ordinal) == true && plane.Opacity(8.1) > 0));
        Assert.IsTrue(incoming.Planes.Any(plane => plane.Text?.Text.EndsWith("4", StringComparison.Ordinal) == true && plane.Opacity(8.1) > 0));
        Assert.AreEqual(Camera(outgoing, 8.1), Camera(incoming, 8.1));
        Assert.AreEqual(Camera(incoming, 8.1), Camera(reproduction.SceneAt(8.1)!, 8.1));
        Assert.AreNotEqual(Camera(incoming, 8.1), Camera(variant.SceneAt(8.1)!, 8.1));

        float before = Camera(outgoing, 7.9999).Translation.Z;
        float at = Camera(incoming, 8).Translation.Z;
        float after = Camera(incoming, 8.0001).Translation.Z;
        Assert.IsTrue(before > at && at > after);
        Assert.AreEqual(before - at, at - after, 0.00001);
    }

    [TestMethod]
    public async Task ProviderRejectsWrongReleaseAndCachesMatchedTimedText()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LyricSpace.Tests", Guid.NewGuid().ToString("N"));
        var responses = new[]
        {
            """{"trackName":"A & B","artistName":"Artista","albumName":"Ao Vivo","duration":200,"syncedLyrics":"[00:01]Versão ao vivo"}""",
            """[{"trackName":"Outro título","artistName":"Artista","duration":180,"syncedLyrics":"[00:01]Outro título"},{"trackName":"A & B","artistName":"Outro artista","duration":180,"syncedLyrics":"[00:01]Outro artista"},{"trackName":"A & B","artistName":"Artista","albumName":"Original","duration":180,"syncedLyrics":"[00:03.5]Versão certa"}]"""
        };

        try
        {
            using var handler = new FakeHttp(responses);
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net") };
            var provider = new LrclibLyrics(folder, client);
            var track = new Track("A & B", "Artista", "Original", 180);
            var cues = await provider.FindAsync(track, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { new LyricCue(3.5, "Versão certa") }, cues);
            Assert.IsTrue(handler.Paths[0].Contains("track_name=A%20%26%20B", StringComparison.Ordinal));
            var reopened = new LrclibLyrics(folder, client);
            CollectionAssert.AreEqual(cues, await reopened.FindAsync(track, CancellationToken.None));
            Assert.HasCount(2, handler.Paths);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    File.Delete(file);
                }

                Directory.Delete(folder);
            }
        }
    }

    private static Matrix4x4 Camera(Sequence scene, double seconds)
    {
        var matrices = new Matrix4x4[scene.Rig.Count];
        scene.Rig.Evaluate(seconds, matrices);
        return matrices[scene.CameraNode];
    }

    private sealed class FakeHttp(string[] responses) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string response = responses[Paths.Count];
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
}
