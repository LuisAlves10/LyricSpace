using System.Globalization;
using System.Numerics;
using Lenoz.Motion;
using LyricSpace.Studio.Lyrics;

namespace LyricSpace.Studio.Composition;

internal sealed class ProceduralSong
{
    private const int CuesPerScene = 4;
    private static readonly Vector3[] Accents = [new(0.39f, 0.87f, 0.98f), new(1, 0.60f, 0.35f), new(0.73f, 0.70f, 1)];
    private static readonly Vector3 Ivory = new(0.97f, 0.94f, 0.87f);
    private readonly double[] _starts;
    private readonly double[] _visibleUntil;
    private readonly Sequence[] _scenes;

    public ProceduralSong(LyricCue[] lyrics, double duration, uint seed)
    {
        var cues = lyrics.Where(cue => cue.Seconds < duration).ToArray();
        _starts = cues.Select(cue => cue.Seconds).ToArray();
        _visibleUntil = new double[cues.Length];
        _scenes = new Sequence[(_starts.Length + CuesPerScene - 1) / CuesPerScene];
        if (_scenes.Length == 0)
        {
            return;
        }

        var rng = new LayoutRandom(seed);
        var centers = new Vector3[cues.Length];
        var accents = new Vector3[cues.Length];
        var directions = new float[cues.Length];
        int palette = (int)(rng.Next() * Accents.Length);
        for (int i = 0; i < cues.Length; i++)
        {
            double spacing = i == 0 ? 0 : _starts[i] - _starts[i - 1];
            float scale = (float)Math.Clamp(spacing, 0.05, 1);
            float direction = (i % 2 == 0 ? -1 : 1) * (palette == 1 ? -1 : 1);
            float z = i == 0 ? 0 : centers[i - 1].Z - (float)Math.Min(spacing * 1.45, 3.0);
            centers[i] = new Vector3(direction * (0.33f + rng.Next() * 0.43f) * scale, (rng.Next() - 0.5f) * 0.34f, z);
            accents[i] = Accents[(palette + i / 4) % Accents.Length];
            directions[i] = direction * (0.65f + rng.Next() * 0.35f);
        }

        ScalarCurve x = Travel(centers.Select(c => c.X).ToArray(), false);
        ScalarCurve y = Travel(centers.Select(c => c.Y).ToArray(), false);
        ScalarCurve zTravel = Travel(centers.Select(c => c.Z).ToArray(), true);
        double[] lengths = _starts.Select((start, i) => (i + 1 < cues.Length ? _starts[i + 1] : duration) - start).ToArray();
        ScalarCurve depthAttack = Gesture(lengths, 0.06, 0.56, -0.62, 0.24, 0.82);
        ScalarCurve depthRecovery = Gesture(lengths, 0.28, 0.83, 0.48, 0.70, 0.52);
        ScalarCurve roll = Gesture(lengths, 0.16, 0.95, 3.5, 0.45, 0.82, directions);
        var zoom = new ScalarCurve(PulseKeys(lengths, 0.28, 0.89, -74, 0.27, 0.76, null)
            .Select(k => k with { Value = 1390 + k.Value }).ToArray());
        var temperature = new ScalarCurve(new Key(0, palette));
        var camera = new RigNode[]
        {
            new("camera.traverse", null, t => new Pose(new Vector3((float)x.At(t), (float)y.At(t), 9.3f + (float)zTravel.At(t)))),
            new("camera.depth-attack", 0, t => new Pose(new Vector3(0, 0, (float)depthAttack.At(t)))),
            new("camera.depth-recovery", 1, t => new Pose(new Vector3(0, 0, (float)depthRecovery.At(t)))),
            new("camera.roll", 2, t => new Pose(Vector3.Zero, new Vector3(0, 0, (float)roll.At(t))))
        };

        var groups = new Phrase?[cues.Length];
        for (int i = 0; i < cues.Length; i++)
        {
            if (cues[i].Text.Length > 0)
            {
                bool nextIsText = i + 1 < cues.Length && cues[i + 1].Text.Length > 0;
                double nextLength = i + 1 < cues.Length ? lengths[i + 1] : 0;
                var group = BuildGroup(i, cues[i], centers[i], directions[i], accents[i], lengths[i], nextLength, nextIsText, rng.Next());
                groups[i] = group;
                _visibleUntil[i] = group.End;
            }
        }

        for (int page = 0; page < _scenes.Length; page++)
        {
            int first = page * CuesPerScene;
            // Neighboring cues retain their original absolute transforms during a scene swap.
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

    public bool CanRender(double seconds)
    {
        const double exposurePadding = 0.5 / Sequence.ReferenceFps;
        int cue = CueAt(seconds + exposurePadding);
        return (cue >= 0 && _visibleUntil[cue] > seconds - exposurePadding)
            || (cue > 0 && _visibleUntil[cue - 1] > seconds - exposurePadding);
    }

    public int CueAt(double seconds)
    {
        int index = Array.BinarySearch(_starts, seconds);
        return index < 0 ? ~index - 1 : index;
    }

    public Sequence? SceneAt(double seconds)
        => _scenes.Length == 0 ? null : _scenes[Math.Max(0, CueAt(seconds)) / CuesPerScene];

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

    private ScalarCurve Gesture(double[] lengths, double peak, double finish, double amplitude,
        double attack, double release, float[]? signs = null)
        => new(PulseKeys(lengths, peak, finish, amplitude, attack, release, signs));

    private Key[] PulseKeys(double[] lengths, double peak, double finish, double amplitude,
        double attack, double release, float[]? signs)
    {
        var keys = new List<Key>(_starts.Length * 3);
        for (int i = 0; i < _starts.Length; i++)
        {
            double start = _starts[i];
            double span = Math.Min(lengths[i], 5);
            double strength = Math.Clamp(span / 1.3, 0, 1) * (signs?[i] ?? 1);
            keys.Add(new Key(start, 0, new Ease(0.78), new Ease(attack)));
            keys.Add(new Key(start + span * peak, strength * amplitude, new Ease(0.58), new Ease(0.26)));
            keys.Add(new Key(start + span * finish, 0, new Ease(release), new Ease(0.3)));
        }

        return keys.ToArray();
    }

    private static Phrase BuildGroup(int index, LyricCue cue,
        Vector3 origin, float direction, Vector3 accent, double interval, double nextInterval, bool nextText, float layout)
    {
        string id = $"cue.{index}";
        double start = cue.Seconds;
        double life = Math.Min(interval, 8);
        double attack = Math.Min(life * 0.32, 0.66);
        double exit = start + life;
        double tail = nextText && life == interval ? Math.Min(0.36, nextInterval * 0.32) : 0;
        var entry = Progress(start, start + attack, 0.24, 0.87);
        var departure = Progress(exit - Math.Min(life * 0.16, 0.38), exit + tail, 0.73, 0.36);
        var settle = Progress(start + attack * 0.36, start + Math.Min(life * 0.79, 1.4), 0.54, 0.83);
        float energy = (float)Math.Clamp(life / 1.25, 0.08, 1);
        var field = new RigNode(id, null, t =>
        {
            float incoming = 1 - (float)entry.At(t);
            float outgoing = (float)departure.At(t);
            return new Pose(origin + new Vector3(direction * (0.65f * incoming + 1.3f * outgoing),
                    0.18f * (incoming - outgoing), -0.6f * incoming + 1.45f * outgoing) * energy,
                new Vector3(5 * incoming, direction * (16 * incoming - 22 * outgoing), direction * (6 * incoming + 8 * outgoing)) * energy);
        });
        var counter = new RigNode(id + ".counter", null, t =>
        {
            float p = (1 - (float)settle.At(t)) * energy;
            return new Pose(new Vector3(-0.22f * direction, -0.16f, 0.4f) * p, new Vector3(-7, -12 * direction, -5 * direction) * p);
        });

        string[] rows = Rows(cue.Text);
        int hero = Array.IndexOf(rows, rows.MaxBy(row => row.Split(' ').Max(word => word.Length))!);
        var words = new WordPlane[rows.Length];
        float lineSpacing = Math.Min(0.99f + 0.22f * layout, 3.50f / rows.Length);
        float stagger = layout < 0.33f ? 0.15f : layout < 0.66f ? 0.65f : -0.52f;
        for (int row = 0; row < rows.Length; row++)
        {
            bool lead = hero == row;
            float height = lineSpacing * (lead ? 0.94f : 0.59f);
            double delay = attack * 0.24 * row / rows.Length;
            var fadeIn = Progress(start + delay, start + delay + attack * 0.47, 0.24, 0.72);
            var landing = ScalarCurve.Between(start + delay, start + delay + attack, 1 - 0.21 * energy, 1,
                new Ease(0.22), new Ease(0.70, 0.20 * energy / attack));
            var overshoot = new ArrivalOscillation(landing, 0.085 * energy, 2.1, 5.0, Sequence.ReferenceFps);
            Vector3 position = new((row - (rows.Length - 1) * 0.5f) * direction * stagger,
                ((rows.Length - 1) * 0.5f - row) * lineSpacing, lead ? 0.22f : -0.18f);
            var node = new RigNode($"{id}.row.{row}", null, t => new Pose(position,
                new Vector3((lead ? -3 : 4) * direction, (lead ? 5 : -6) * direction, 0),
                new Vector3((float)overshoot.At(t)), Vector3.Zero, Vector3.Zero));
            var plane = new ScenePlane(0, new TextStyle(rows[row], lead ? Typeface.Display : Typeface.Condensed, lead ? 0 : 0.035f),
                new Vector2(6.35f, height), lead ? accent : Ivory, t => (float)(fadeIn.At(t) * (1 - departure.At(t))));
            words[row] = new WordPlane(node, plane);
        }

        return new Phrase(field, counter, words, exit + tail);
    }

    private static string[] Rows(string text)
    {
        const int target = 24;
        // Keep unusual single-line transcriptions within a readable GPU text budget.
        var elements = new StringInfo(text);
        if (elements.LengthInTextElements > 216)
        {
            text = elements.SubstringByTextElements(0, 215) + "…";
        }

        var rows = new List<string>();
        string line = "";
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            int[] boundaries = StringInfo.ParseCombiningCharacters(word);
            for (int first = 0; first < boundaries.Length; first += target)
            {
                int end = first + target < boundaries.Length ? boundaries[first + target] : word.Length;
                string part = word[boundaries[first]..end];
                if (line.Length > 0 && new StringInfo(line + " " + part).LengthInTextElements > target)
                {
                    rows.Add(line);
                    line = "";
                }

                line = line.Length == 0 ? part : line + " " + part;
            }
        }

        rows.Add(line);
        if (rows.Count == 1 && line.Contains(' '))
        {
            // A short cue still gets a lead and a response on separate depth planes.
            int midpoint = line.Length / 2;
            int split = Enumerable.Range(0, line.Length).Where(i => line[i] == ' ').MinBy(i => Math.Abs(i - midpoint));
            return [line[..split], line[(split + 1)..]];
        }

        return rows.ToArray();
    }

    private static ScalarCurve Progress(double start, double end, double outgoing, double incoming)
        => ScalarCurve.Between(start, end, 0, 1, new Ease(outgoing), new Ease(incoming));

    private readonly record struct Phrase(RigNode Field, RigNode Counter, WordPlane[] Words, double End);
    private readonly record struct WordPlane(RigNode Node, ScenePlane Plane);

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
    }
}
