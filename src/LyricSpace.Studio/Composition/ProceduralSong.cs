using System.Globalization;
using System.Numerics;
using Lenoz.Motion;
using LyricSpace.Studio.Lyrics;

namespace LyricSpace.Studio.Composition;

internal sealed class ProceduralSong
{
    private const int CuesPerScene = 4;
    private readonly double[] _starts;
    private readonly double[] _visibleUntil;
    private readonly Sequence[] _scenes;

    public ProceduralSong(LyricCue[] lyrics, double duration, uint seed, SongLook? look = null)
    {
        look ??= SongLook.Default;
        var cues = lyrics.Where(cue => cue.Seconds < duration).ToArray();
        _starts = cues.Select(cue => cue.Seconds).ToArray();
        _visibleUntil = new double[cues.Length];
        _scenes = new Sequence[(_starts.Length + CuesPerScene - 1) / CuesPerScene];
        if (_scenes.Length == 0)
        {
            return;
        }

        var rng = new LayoutRandom(seed);
        var profiles = new GestureProfile[cues.Length];
        var centers = new Vector3[cues.Length];
        double[] lengths = _starts.Select((start, i) => (i + 1 < cues.Length ? _starts[i + 1] : duration) - start).ToArray();
        for (int i = 0; i < cues.Length; i++)
        {
            MotionFamily? previous = i > 0 ? profiles[i - 1].Family : null;
            var profile = profiles[i] = GestureProfile.Choose(ref rng, previous, lengths[i]);
            double spacing = i == 0 ? 0 : _starts[i] - _starts[i - 1];
            float strength = look.Motion * (float)Math.Clamp(spacing, 0.07, 1);
            float z = i == 0 ? 0 : centers[i - 1].Z - (float)Math.Min(spacing * 1.55, 3.3) * look.Motion;
            centers[i] = new Vector3(profile.Direction * profile.Reach * 0.65f * strength,
                (rng.Next() - 0.5f) * 0.7f * strength, z);
        }

        ScalarCurve x = Travel(centers.Select(c => c.X).ToArray(), false);
        ScalarCurve y = Travel(centers.Select(c => c.Y).ToArray(), false);
        ScalarCurve zTravel = Travel(centers.Select(c => c.Z).ToArray(), true);
        var depthAttack = Pulse(lengths, profiles, p => (p.Depth * -0.46 * look.Motion, p.Attack * 0.46, 0.63, p.Out, p.In));
        var depthRecovery = Pulse(lengths, profiles, p => (p.Depth * 0.33 * look.Motion, p.Counter, 0.91, p.In, p.Out));
        var roll = Pulse(lengths, profiles, p => (p.Direction * p.Rotation * 3.6 * look.Motion, p.Attack, 0.94, p.Out, p.In));
        var zoom = Pulse(lengths, profiles, p => (-56 * p.Depth * look.Motion, p.Counter, 0.93, p.Out, p.In), 1370);
        var temperature = new ScalarCurve(new Key(0, 0));
        var camera = new RigNode[]
        {
            new("camera.traverse", null, t => new Pose(new Vector3((float)x.At(t), (float)y.At(t), 9.5f + (float)zTravel.At(t)))),
            new("camera.depth-attack", 0, t => new Pose(new Vector3(0, 0, (float)depthAttack.At(t)))),
            new("camera.depth-recovery", 1, t => new Pose(new Vector3(0, 0, (float)depthRecovery.At(t)))),
            new("camera.roll", 2, t => new Pose(Vector3.Zero, new Vector3(0, 0, (float)roll.At(t))))
        };

        var groups = new Phrase?[cues.Length];
        WordPalette palette = look.Palette;
        for (int i = 0; i < cues.Length; i++)
        {
            if (cues[i].Text.Length == 0)
            {
                continue;
            }

            double nextLength = i + 1 < cues.Length && cues[i + 1].Text.Length > 0 ? lengths[i + 1] : 0;
            var group = BuildGroup(i, cues[i], centers[i], profiles[i], look, palette, lengths[i], nextLength, ref rng);
            groups[i] = group;
            _visibleUntil[i] = group.End;
        }

        for (int page = 0; page < _scenes.Length; page++)
        {
            int first = page * CuesPerScene;
            var nodes = new List<RigNode>(camera);
            var planes = new List<ScenePlane>();
            for (int i = Math.Max(0, first - 1); i < Math.Min(cues.Length, first + CuesPerScene + 1); i++)
            {
                if (groups[i] is not { } group)
                {
                    continue;
                }

                int parent = nodes.Count;
                nodes.Add(group.Field);
                nodes.Add(group.Counter with { ParentIndex = parent });
                foreach (WordPlane word in group.Words)
                {
                    planes.Add(word.Plane with { Node = nodes.Count });
                    nodes.Add(word.Node with { ParentIndex = parent + 1 });
                }
            }

            _scenes[page] = new Sequence(new Rig(nodes.ToArray()), camera.Length - 1, zoom, temperature, planes.ToArray(), duration);
        }
    }

    public bool HasText => _scenes.Any(scene => scene.Planes.Length > 0);
    public double Duration => _scenes.Length == 0 ? 0 : _scenes[0].Duration;

    public bool CanRender(double seconds)
    {
        const double exposurePadding = 0.5 / Sequence.ReferenceFps;
        int cue = CueAt(seconds + exposurePadding);
        return (cue >= 0 && _visibleUntil[cue] > seconds - exposurePadding)
            || (cue > 0 && _visibleUntil[cue - 1] > seconds - exposurePadding);
    }

    public TimingKind? TimingAt(double seconds)
    {
        int index = CueAt(seconds);
        return index < 0 ? null : SceneAt(seconds)?.Planes.FirstOrDefault(
            plane => plane.Word?.Phrase.Index == index).Word?.Vocal.Timing;
    }

    public int CueAt(double seconds)
    {
        int index = Array.BinarySearch(_starts, seconds);
        return index < 0 ? ~index - 1 : index;
    }

    public Sequence? SceneAt(double seconds)
        => _scenes.Length == 0 ? null : _scenes[Math.Max(0, CueAt(seconds)) / CuesPerScene];

    public Sequence? NextSceneAt(double seconds)
    {
        int next = Math.Max(0, CueAt(seconds)) / CuesPerScene + 1;
        return next < _scenes.Length ? _scenes[next] : null;
    }

    private ScalarCurve Travel(float[] values, bool carryVelocity)
    {
        var keys = new Key[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double velocity = 0;
            if (carryVelocity && i > 0 && i < values.Length - 1)
            {
                double incoming = (values[i - 1] - values[i]) / (_starts[i] - _starts[i - 1]);
                double outgoing = (values[i] - values[i + 1]) / (_starts[i + 1] - _starts[i]);
                velocity = -0.58 * Math.Min(incoming, outgoing);
            }
            keys[i] = new Key(_starts[i], values[i], new Ease(0.73, velocity), new Ease(0.26, velocity));
        }
        return new ScalarCurve(keys);
    }

    private ScalarCurve Pulse(double[] lengths, GestureProfile[] profiles,
        Func<GestureProfile, (double Amplitude, double Peak, double End, double Out, double In)> shape, double basis = 0)
    {
        var keys = new List<Key>(_starts.Length * 3);
        for (int i = 0; i < _starts.Length; i++)
        {
            var gesture = shape(profiles[i]);
            double start = _starts[i];
            double span = Math.Min(lengths[i], 5);
            double amplitude = gesture.Amplitude * Math.Clamp(span / 1.3, 0, 1);
            keys.Add(new Key(start, basis, new Ease(0.75), new Ease(gesture.Out)));
            keys.Add(new Key(start + span * gesture.Peak, basis + amplitude, new Ease(0.58), new Ease(0.28)));
            keys.Add(new Key(start + span * gesture.End, basis, new Ease(gesture.In), new Ease(0.32)));
        }
        return new ScalarCurve(keys.ToArray());
    }

    private static Phrase BuildGroup(int index, LyricCue cue, Vector3 origin, GestureProfile g,
        SongLook look, WordPalette palette, double interval, double nextInterval, ref LayoutRandom rng)
    {
        string id = $"cue.{index}";
        double start = cue.Seconds;
        bool hasAuthoredWords = cue.Words is { Length: > 0 };
        double life = hasAuthoredWords ? Math.Min(interval, Math.Max(8, cue.Words![^1].End.GetValueOrDefault(cue.Words[^1].Start + 1) - start))
            : Math.Min(interval, 8);
        double attack = Math.Min(life * g.Attack, 0.83);
        double exit = start + life;
        double tail = life == interval ? Math.Min(g.Tail, nextInterval * 0.38) : 0;
        double release = Math.Min(life * g.Release, 0.52);
        double departureAt = exit - release;
        double readStart = start + Math.Min(attack * 0.62, 0.19);
        double readEnd = departureAt + release * 0.20;
        if (hasAuthoredWords && cue.Words![^1] is { } last)
        {
            readEnd = Math.Min(exit, Math.Max(readEnd, last.End ?? last.Start));
            if (tail > 0)
            {
                departureAt = Math.Max(departureAt, readEnd - release * 0.3);
            }
        }
        var frameIn = Progress(start, readStart, 0.23, 0.77);
        var frameOut = Progress(readEnd, exit + tail, 0.65, 0.35);
        var style = new PhraseStyle(index, g.Family, start, readStart, readEnd, exit + tail, frameIn, frameOut,
            g.Direction * g.Stagger * look.Motion, g.Width, look.Tracking, look.GlowRadius);
        var entry = Progress(start, start + attack, g.Out, g.In);
        var departure = Progress(departureAt, exit + tail, g.In, g.Out);
        var countermove = Progress(start + attack * g.Counter, start + Math.Min(life * 0.79, 1.55), 0.48, g.In);
        var fadeIn = Progress(start, start + Math.Min(attack * 0.70, 0.20), 0.24, 0.73);
        float energy = look.Motion * (float)Math.Clamp(life / 1.12, 0.08, 1);

        Vector3 enter = g.Family switch
        {
            MotionFamily.Traverse => new(g.Direction * g.Reach * -1.7f, g.Lift * 0.30f, -g.Depth * 0.45f),
            MotionFamily.Depth => new(g.Direction * g.Reach * 0.40f, -g.Lift * 0.30f, -g.Depth * 2.30f),
            _ => new(g.Direction * g.Reach * 0.53f, g.Lift * 1.05f, g.Depth * 0.42f)
        };
        Vector3 exitMove = g.Family switch
        {
            MotionFamily.Traverse => new(g.Direction * g.Reach * 2.9f, g.Lift * -0.42f, g.Depth * 0.70f),
            MotionFamily.Depth => new(g.Direction * g.Reach * 0.92f, g.Lift * 0.92f, g.Depth * 2.1f),
            _ => new(g.Direction * g.Reach * 1.3f, g.Lift * -1.7f, -g.Depth * 1.04f)
        };
        Vector3 enterRotation = new(g.Lift * (g.Family == MotionFamily.Depth ? 15 : 7),
            g.Direction * g.Rotation * (g.Family == MotionFamily.Traverse ? 20 : 11),
            g.Direction * g.Rotation * (g.Family == MotionFamily.Pivot ? 21 : 5));
        var field = new RigNode(id, null, t =>
        {
            float ins = 1 - (float)entry.At(t);
            float outs = (float)departure.At(t);
            return new Pose(origin + (enter * ins + exitMove * outs) * energy,
                (enterRotation * ins + new Vector3(9 * g.Lift, -24 * g.Direction, 13 * g.Direction) * outs * g.Rotation) * energy);
        });
        var counter = new RigNode(id + ".counter", null, t =>
        {
            float p = (1 - (float)countermove.At(t)) * energy;
            Vector3 shift = new(-enter.X * 0.28f, -enter.Y * 0.36f, -enter.Z * 0.48f);
            return new Pose(shift * p, -enterRotation * (0.48f * p));
        });

        var lyrics = WordTiming.Resolve(cue, exit, look.EstimateWords);
        int hero = Array.IndexOf(lyrics, lyrics.MaxBy(w => new StringInfo(w.Text).LengthInTextElements));
        var words = new WordPlane[lyrics.Length];
        for (int i = 0; i < words.Length; i++)
        {
            var word = lyrics[i];
            bool lead = i == hero;
            float n = rng.Next();
            bool pop = word.Timing != TimingKind.Line && (look.Reveal == "all"
                || (look.Reveal == "some" && n < 0.30f && (i != 0 || lyrics.Length == 1)));
            double span = word.End - word.Start;
            double localAttack = Math.Min(0.15, span * 0.48);
            var vocalIn = Progress(word.Start, word.Start + Math.Min(0.060, span * 0.30), 0.22, 0.78);
            var vocalOut = Progress(word.End, word.End + Math.Min(0.115, span * 0.55), 0.32, 0.79);
            var birth = Progress(word.Start, word.Start + localAttack, 0.24, 0.84);
            var landing = ScalarCurve.Between(word.Start, word.Start + localAttack, 0, 1,
                new Ease(0.25), new Ease(0.72, 0.21 / localAttack));
            var punch = new ArrivalOscillation(landing, 0.012, 3.2, 9.2, Sequence.ReferenceFps);
            var visual = new WordStyle(style, word, lead, pop, palette, vocalIn, vocalOut, look.ActiveTracking, look.Glow, look.AmbientGlow);
            Vector3 pose = new(0, (rng.Next() - 0.5f) * 0.08f * energy,
                (lead ? 0.21f : (rng.Next() - 0.5f) * 0.34f) * energy);
            Vector3 tilt = new((rng.Next() - 0.5f) * 7, (rng.Next() - 0.5f) * (lead ? 18 : 11),
                (lead ? g.Direction * 3.5f : (rng.Next() - 0.5f) * 4.8f));
            var node = new RigNode($"{id}.word.{i}", null, t =>
            {
                float active = visual.AccentAt(t);
                float local = pop ? 1 - (float)birth.At(t) : 0;
                float bounce = word.Timing == TimingKind.Line ? 0 : (float)(punch.At(t) - landing.At(t));
                float scale = 1 + energy * (active * 0.053f + Math.Clamp(bounce, -0.10f, 0.12f) - local * 0.16f);
                return new Pose(pose + new Vector3(0.13f * g.Direction, -0.22f, -0.42f) * (local * energy),
                    (tilt + new Vector3(8, g.Direction * 16, -11) * local) * energy,
                    new Vector3(scale), Vector3.Zero, Vector3.Zero);
            });

            string text = word.Text;
            float fontSize = lead ? 0.91f : 0.67f + rng.Next() * 0.11f;
            var plane = new ScenePlane(0, new TextStyle(text, lead ? Typeface.Display : Typeface.Condensed, Family: lead ? look.DisplayFont : look.BodyFont),
                new Vector2(0, fontSize), palette.Base, t =>
                {
                    float show = pop ? (t < word.Start ? 0 : 0.30f + 0.70f * (float)birth.At(t)) : 1;
                    return show * (float)(fadeIn.At(t) * (1 - departure.At(t)));
                }, visual);
            words[i] = new WordPlane(node, plane);
        }
        return new Phrase(field, counter, words, exit + tail);
    }

    private static VocalWord[] LimitFrameText(VocalWord[] words)
    {
        var result = new List<VocalWord>();
        int remaining = 216;
        foreach (VocalWord word in words)
        {
            var text = new StringInfo(word.Text);
            int limit = Math.Min(56, remaining);
            if (limit < 2)
            {
                result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd('…') + "…" };
                break;
            }

            if (text.LengthInTextElements > limit)
            {
                result.Add(word with { Text = text.SubstringByTextElements(0, limit - 1) + "…" });
                remaining -= limit + 1;
            }
            else
            {
                result.Add(word);
                remaining -= text.LengthInTextElements + 1;
            }
        }
        return result.ToArray();
    }
    private static ScalarCurve Progress(double start, double end, double outgoing, double incoming)
        => ScalarCurve.Between(start, end, 0, 1, new Ease(outgoing), new Ease(incoming));

    private readonly record struct Phrase(RigNode Field, RigNode Counter, WordPlane[] Words, double End);
    private readonly record struct WordPlane(RigNode Node, ScenePlane Plane);

    private readonly record struct GestureProfile(MotionFamily Family, float Direction, float Lift, float Reach,
        float Depth, float Rotation, double Attack, double Release, double Counter, double Tail, double Out, double In,
        float Width, float Stagger)
    {
        public static GestureProfile Choose(ref LayoutRandom rng, MotionFamily? previous, double duration)
        {
            MotionFamily family = rng.Next() switch
            {
                < 0.35f => MotionFamily.Traverse,
                < 0.68f => MotionFamily.Depth,
                _ => MotionFamily.Pivot
            };
            if (family == previous || (duration < 0.7 && family == MotionFamily.Pivot))
            {
                family = family == MotionFamily.Traverse || previous == MotionFamily.Traverse
                    ? MotionFamily.Depth : MotionFamily.Traverse;
                if (family == previous)
                {
                    family = MotionFamily.Traverse;
                }
            }

            return new GestureProfile(family, rng.Next() < 0.5 ? -1 : 1, rng.Between(-1, 1), rng.Between(0.67f, 1.34f),
                rng.Between(0.56f, 1.46f), rng.Between(0.58f, 1.38f), rng.Between(0.16f, 0.39f), rng.Between(0.12f, 0.28f),
                rng.Between(0.24f, 0.52f), rng.Between(0.22f, 0.42f), rng.Between(0.16f, 0.43f), rng.Between(0.63f, 0.88f),
                family == MotionFamily.Traverse ? rng.Between(5.6f, 7.0f) : rng.Between(3.6f, 5.5f),
                rng.Between(0.08f, 0.65f));
        }
    }

    private struct LayoutRandom(uint seed)
    {
        private uint _state = seed == 0 ? 0x9e3779b9 : seed;

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state >> 8) / 16777216f;
        }

        public float Between(float from, float to) => from + (to - from) * Next();
    }
}
