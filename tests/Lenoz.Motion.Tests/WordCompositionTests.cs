using System.Numerics;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Lyrics;
using LyricSpace.Studio.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lenoz.Motion.Tests;

[TestClass]
public sealed class WordCompositionTests
{
    [TestMethod]
    [DataRow("ivory")]
    [DataRow("white")]
    [DataRow("ice")]
    [DataRow("visual-baseline-v1")]
    public void VisualPresetChangesPreserveTimingAndLocalMotion(string preset)
    {
        var cues = LrcParser.Parse("[00:01.000]Eu volto devagar\n[00:04.000]");
        var animation = AnimationSettings.Default;
        var baseline = SongLook.Read(Path.Combine(AppContext.BaseDirectory, "white-estimated.json"));
        var look = preset == "visual-baseline-v1" ? baseline : SongLook.Preset(preset);
        var first = new ProceduralSong(cues, 5, 42, baseline, animation);
        var second = new ProceduralSong(cues, 5, 42, look with { DisplayFont = "Segoe UI" }, animation);
        Assert.AreEqual(TimingKind.Estimated, first.TimingAt(1.6));
        Assert.AreEqual(first.TimingAt(1.6), second.TimingAt(1.6));

        var a = first.SceneAt(1.6)!;
        var b = second.SceneAt(1.6)!;
        for (int i = 0; i < a.Rig.Count; i++)
            Assert.AreEqual(a.Rig.GetNode(i).MatrixAt(1.6), b.Rig.GetNode(i).MatrixAt(1.6));
        for (int i = 0; i < a.Planes.Length; i++)
        {
            Assert.AreEqual(a.Planes[i].Opacity(1.6), b.Planes[i].Opacity(1.6));
            Assert.AreEqual(a.Planes[i].Word!.TrackingAt(1.6), b.Planes[i].Word!.TrackingAt(1.6));
        }
    }

    [TestMethod]
    public void EnhancedLrcRetainsRealWordTimesRepeatedLinesAndOffset()
    {
        var cues = LrcParser.Parse("[offset:+100]\n[00:02.000][00:08.000]<00:02.000>Eu <00:02.250>volto<00:03.100>");
        Assert.HasCount(2, cues);
        Assert.AreEqual("Eu volto", cues[0].Text);
        var words = WordTiming.Resolve(cues[1], 12, estimate: true);
        Assert.AreEqual(TimingKind.Authored, words[0].Timing);
        Assert.AreEqual(7.9, words[0].Start, 1e-8);
        Assert.AreEqual(8.15, words[0].End, 1e-8);
        Assert.AreEqual(9.0, words[1].End, 1e-8);
    }

    [TestMethod]
    public void EstimatedTimesAreOptInAndCoverThePhrase()
    {
        var cue = new LyricCue(2, "Eu volto devagar");
        Assert.AreEqual(TimingKind.Line, WordTiming.Resolve(cue, 5, estimate: false)[1].Timing);
        var words = WordTiming.Resolve(cue, 5, estimate: true);
        Assert.AreEqual(TimingKind.Estimated, words[1].Timing);
        Assert.AreEqual(2, words[0].Start);
        Assert.AreEqual(words[0].End, words[1].Start);
        Assert.AreEqual(5, words[^1].End);
        Assert.IsGreaterThan(words[0].End - words[0].Start, words[^1].End - words[^1].Start);
    }

    [TestMethod]
    public void WordRevealWaitsForVocalAndPhraseTrackingKeepsOpeningDuringReading()
    {
        var original = LrcParser.Parse("[00:01.0]<00:01.0>Eu <00:02.0>volto <00:03.0>devagar<00:04.0>\n[00:05.0]");
        var song = new ProceduralSong(original, 5.3, 42, SongLook.Preset("white"),
            AnimationSettings.Default with { Reveal = "all" });
        ScenePlane plane = song.SceneAt(0)!.Planes.Single(plane => plane.Text?.Text == "volto");
        Assert.AreEqual(0, plane.Opacity(1.999));
        Assert.IsGreaterThan(0, plane.Opacity(2.01));
        WordStyle motion = plane.Word!;
        Assert.AreEqual(WordState.Future, motion.StateAt(1.999));
        Assert.AreEqual(WordState.Active, motion.StateAt(2.5));
        Assert.AreEqual(WordState.Past, motion.StateAt(3));
        Assert.IsGreaterThan(motion.Phrase.TrackingAt(2.1), motion.Phrase.TrackingAt(2.6));
        Assert.IsGreaterThan(motion.GlowAt(1.9), motion.GlowAt(2.5));
        Assert.AreEqual(new Vector3(1), motion.ColorAt(2.5));
    }

    [TestMethod]
    public void MotionFamiliesAreDeterministicVaryAndDoNotRepeatImmediately()
    {
        var lyrics = Enumerable.Range(0, 28).Select(i => new LyricCue(i * 1.25, "O espaço continua")).ToArray();
        MotionFamily[] Plan(uint seed)
        {
            var song = new ProceduralSong(lyrics, 36, seed);
            return lyrics.Select((cue, i) => song.SceneAt(cue.Seconds)!.Planes
                .First(plane => plane.Word!.Phrase.Index == i).Word!.Phrase.Family).ToArray();
        }

        MotionFamily[] first = Plan(12345);
        CollectionAssert.AreEqual(first, Plan(12345));
        CollectionAssert.AreNotEqual(first, Plan(42));
        Assert.HasCount(3, first.Distinct().ToArray());
        for (int i = 1; i < first.Length; i++)
        {
            Assert.AreNotEqual(first[i - 1], first[i]);
        }
    }

    [TestMethod]
    public void ProjectedTrackingAndTiltFitWithinSafeRegionDuringReading()
    {
        var cluster = new InkCluster([new(-0.4f, -0.4f), new(0.4f, -0.4f), new(0.4f, 0.4f), new(-0.4f, 0.4f)],
            Vector2.Zero, 0.64f, 1);
        Matrix4x4 transform = Matrix4x4.CreateScale(2.5f) * Matrix4x4.CreateRotationY(0.4f)
            * Matrix4x4.CreateRotationZ(0.35f) * Matrix4x4.CreateTranslation(2.2f, 1.1f, -4.5f)
            * Matrix4x4.CreatePerspectiveFieldOfView(0.85f, 16f / 9f, 0.1f, 50);
        Vector2 viewport = new(240, 135);
        var first = new ProjectedInk();
        first.Add([cluster], transform, viewport, 0, false);
        var tracked = new ProjectedInk();
        tracked.Add([cluster], transform, viewport, 0.061f, false);
        Assert.IsTrue(tracked.Valid);
        Assert.AreNotEqual(first.Max, tracked.Max);
        Vector2 safe = new(40, 27);
        FramingTransform fit = FramingTransform.At(tracked, tracked.Center, safe, 1, 1, viewport);
        Assert.IsLessThan(1f, fit.Scale);
        var measured = new ProjectedInk();
        measured.Add([cluster], transform * fit.ClipMatrix, viewport, 0.061f, false);
        Assert.IsLessThan(0.002f, measured.Center.Length());
        Assert.IsLessThanOrEqualTo(safe.X + 0.002f, MathF.Max(-measured.Min.X, measured.Max.X));
        Assert.IsLessThanOrEqualTo(safe.Y + 0.002f, MathF.Max(-measured.Min.Y, measured.Max.Y));
        FramingTransform travel = FramingTransform.At(tracked, tracked.Center, safe, 0.5f, 0, viewport);
        Assert.AreEqual(Matrix4x4.Identity, travel.ClipMatrix);
    }
}
