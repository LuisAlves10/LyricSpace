using System.Numerics;
using Lenoz.Motion;

namespace LyricSpace.Studio.Composition;

internal static class DemoSequence
{
    public const uint DefaultSeed = 12345;
    private const double Duration = 8.6;

    private static readonly Vector3 Ivory = Color(0xf7efdf);
    private static readonly Vector3 Amber = Color(0xff6936);
    private static readonly Vector3 Cyan = Color(0x64defa);
    private static readonly Vector3 Lilac = Color(0xaca5ff);

    public static Sequence Create(uint seed)
    {
        var nodes = new List<RigNode>();
        var planes = new List<ScenePlane>();
        int Node(string id, int? parent, Func<double, Pose> sample)
        {
            nodes.Add(new RigNode(id, parent, sample));
            return nodes.Count - 1;
        }

        var a = new Vector3(0, 0, 0);
        var b = new Vector3(4.2f, 0.08f, -6.5f);
        var c = new Vector3(-3.4f, -0.07f, -13.0f);
        var firstTravel = new SpatialPath(a, b, new Vector3(-0.42f, 0.42f, -0.9f), new Vector3(-1.1f, -0.3f, 0.36f));
        var lastTravel = new SpatialPath(b, c, new Vector3(0.55f, -0.66f, -1.1f), new Vector3(1.5f, 0.25f, 1.1f));
        var bridge = Progress(2.08, 3.88, 0.584244, 0.865033);
        var traverse = Progress(5.04, 6.98, 0.53, 0.73);
        int platform = Node("camera.travel", null, t =>
        {
            Vector3 position = t < 4.5 ? firstTravel.AtDistanceFraction(bridge.At(t)) : lastTravel.AtDistanceFraction(traverse.At(t));
            return new Pose(position + new Vector3(0, 0, 9.2f));
        });

        var orbit = new ScalarCurve(
            new Key(0, -4, Out: new Ease(0.30)),
            new Key(1.50, 0, new Ease(0.88), new Ease(0.78)),
            new Key(2.85, -13, new Ease(0.28), new Ease(0.14)),
            new Key(3.85, 0, new Ease(0.85), new Ease(0.53)),
            new Key(4.94, 0.6, new Ease(0.81), new Ease(0.17)),
            new Key(5.44, 8, new Ease(0.70), new Ease(0.30)),
            new Key(6.87, 0, new Ease(0.86)));
        int aim = Node("camera.turn", platform, t => new Pose(Vector3.Zero, new Vector3(0, (float)orbit.At(t), 0)));

        const double depthStart = 1.93;
        const double depthStretch = 1.24;
        const float depth = 1.55f;
        double AtFrame(int frame) => depthStart + (frame - 246) / Sequence.ReferenceFps * depthStretch;
        var plus = ScalarCurve.Between(AtFrame(261), AtFrame(324), depth, 0, new Ease(0.5352068728), new Ease(0.7970376417));
        var minus = ScalarCurve.Between(AtFrame(246), AtFrame(286), -depth, 0, new Ease(0.7016677649), new Ease(0.4961771907));
        int recovery = Node("camera.depth-recovery", aim, t => new Pose(new Vector3(0, 0, (float)plus.At(t))));
        int impulse = Node("camera.depth-impulse", recovery, t => new Pose(new Vector3(0, 0, (float)minus.At(t))));

        var roll = new ScalarCurve(
            new Key(0, -4, Out: new Ease(0.18, 9)),
            new Key(1.46, 1.5, new Ease(0.86), new Ease(0.7)),
            new Key(2.51, -12, new Ease(0.46), new Ease(0.15)),
            new Key(3.63, -2, new Ease(0.83), new Ease(0.78)),
            new Key(5.09, 1, new Ease(0.74), new Ease(0.78)),
            new Key(6.92, -360, new Ease(0.64), new Ease(0.26)),
            new Key(8.6, -361.5, new Ease(0.9)));
        int lens = Node("camera.lens", impulse, t => new Pose(
            Vector3.Zero,
            new Vector3(Wiggle.At(t, 2, 0.38f, seed, 1),
                Wiggle.At(t, 2, 0.48f, seed, 2),
                (float)roll.At(t) + Wiggle.At(t, 2, 0.38f, seed, 3))));

        var zoom = new ScalarCurve(
            new Key(0, 1320, Out: new Ease(0.2, 18)),
            new Key(1.62, 1400, new Ease(0.81), new Ease(0.52)),
            new Key(2.52, 1130, new Ease(0.29), new Ease(0.25)),
            new Key(3.70, 1400, new Ease(0.62, -60), new Ease(0.42)),
            new Key(4.76, 1390, new Ease(0.69), new Ease(0.52)),
            new Key(5.92, 1020, new Ease(0.48), new Ease(0.39)),
            new Key(7.07, 1410, new Ease(0.88), new Ease(0.53)),
            new Key(Duration, 1450, new Ease(0.73)));
        var temperature = new ScalarCurve(
            new Key(0, 0), new Key(2.13, 0, Out: new Ease(0.55)),
            new Key(3.84, 1, new Ease(0.83), new Ease(0.64)),
            new Key(5.2, 1, new Ease(0.60), new Ease(0.50)),
            new Key(7.1, 2, new Ease(0.84)));

        int Field(string id, Vector3 origin, double enter, double settle, double leave, double exit,
            Vector3 entryOffset, Vector3 exitOffset, Vector3 initialRotation, Vector3 endRotation)
        {
            var entry = Progress(enter, settle, 0.185, 0.92, velocity: 0.7);
            var departure = Progress(leave, exit, 0.87, 0.10);
            return Node(id, null, t =>
            {
                float leading = 1 - (float)entry.At(t);
                float trailing = (float)departure.At(t);
                return new Pose(origin + entryOffset * leading + exitOffset * trailing,
                    initialRotation * leading + endRotation * trailing);
            });
        }

        void Text(string id, int parent, string text, Typeface face, float tracking, Vector3 position,
            float height, Vector3 color, Vector3 rotation, double enter, double settle, double leave, double exit, bool emphasis = false)
        {
            var opacityIn = Progress(enter, enter + 0.30, 0.18, 0.73);
            var opacityOut = Progress(leave, exit, 0.20, 0.62);
            var landing = ScalarCurve.Between(enter, settle, emphasis ? 0.61 : 0.88, 1,
                new Ease(0.18, emphasis ? 0.55 : 0.22), new Ease(0.69, emphasis ? 0.95 : 0));
            var oscillation = new ArrivalOscillation(landing, emphasis ? 0.09 : 0, 2, 4, Sequence.ReferenceFps);
            var rotate = Progress(enter, settle + 0.23, 0.52, 0.91);
            int node = Node(id, parent, t =>
            {
                float tilt = 1 - (float)rotate.At(t);
                float scale = (float)oscillation.At(t);
                return new Pose(position + new Vector3(0, -0.19f * tilt, -0.48f * tilt),
                    rotation + new Vector3(-17, 26, 9) * tilt, new Vector3(scale), Vector3.Zero, Vector3.Zero);
            });
            planes.Add(new ScenePlane(node, new TextStyle(text, face, tracking), new Vector2(0, height), color,
                t => (float)(opacityIn.At(t) * (1 - opacityOut.At(t)))));
        }

        void Rule(string id, int parent, Vector3 position, Vector2 size, Vector3 rotation,
            Vector3 color, double appear, double disappear, float opacity)
        {
            var ins = Progress(appear, appear + 0.42, 0.37, 0.82);
            var outs = Progress(disappear - 0.6, disappear, 0.48, 0.61);
            int node = Node(id, parent, _ => new Pose(position, rotation));
            planes.Add(new ScenePlane(node, null, size, color, t => opacity * (float)(ins.At(t) * (1 - outs.At(t)))));
        }

        int light = Field("light.field", a, -0.12, 1.06, 2.04, 3.06,
            new Vector3(-0.7f, 0.18f, -0.8f), new Vector3(-3.4f, 1.2f, 2.3f),
            new Vector3(8, -14, -8), new Vector3(4, 56, 22));
        Text("light.lead", light, "A LUZ", Typeface.Condensed, 0.19f, new(-2.69f, 1.34f, -0.2f),
            0.36f, Ivory, new(0, 9, 0), -0.27, 0.50, 2.46, 3.14);
        Text("light.hero", light, "DOBRA", Typeface.Display, -0.018f, new(-0.15f, 0.03f, 0),
            1.64f, Amber, new(0, -2, 0), -0.17, 0.49, 2.45, 3.12, emphasis: true);
        Text("light.tail", light, "O ESPAÇO", Typeface.Condensed, 0.17f, new(2.04f, -1.35f, 0.42f),
            0.43f, Ivory, new(4, -12, -2), 0.1, 0.83, 2.37, 3.09);
        Rule("light.upper-rule", light, new(0.79f, 1.35f, -0.51f), new(3.85f, 0.015f), new(0, -10, -3), Amber, -0.18, 2.95, 0.57f);
        Rule("light.lower-rule", light, new(-1.65f, -1.44f, -0.38f), new(3.0f, 0.022f), new(0, 12, 0), Amber, 0.02, 2.85, 0.65f);
        Rule("light.near-accent", light, new(-4.18f, -0.77f, 1.2f), new(0.037f, 1.38f), new(0, -17, -12), Amber, 0.12, 2.9, 0.60f);

        int transit = Field("transit.field", b, 2.11, 3.87, 5.10, 6.19,
            new Vector3(0.9f, 0.5f, -1.5f), new Vector3(3.5f, -0.6f, 2.9f),
            new Vector3(-12, 28, 13), new Vector3(-28, -58, -25));
        Text("transit.lead", transit, "TUDO", Typeface.Condensed, 0.21f, new(-2.28f, 1.15f, 0.43f),
            0.40f, Cyan, new(-8, 12, 0), 2.22, 2.81, 5.38, 6.13);
        Text("transit.link", transit, "EM", Typeface.Condensed, 0.13f, new(-0.80f, 1.14f, -0.11f),
            0.34f, Ivory, new(-6, -16, -2), 2.41, 3.10, 5.46, 6.19);
        Text("transit.hero", transit, "TRÂNSITO", Typeface.Display, -0.004f, new(-0.15f, -0.27f, 0),
            1.47f, Ivory, new(3, -9, 0.6f), 2.43, 3.17, 5.58, 6.27, emphasis: true);
        Rule("transit.upper-rule", transit, new(2.0f, 1.19f, -0.54f), new(3.21f, 0.018f), new(0, -14, 4), Cyan, 2.31, 6.05, 0.7f);
        Rule("transit.near-rule", transit, new(0.82f, -1.53f, 0.23f), new(5.40f, 0.024f), new(0, 24, -1), Cyan, 2.40, 6.08, 0.52f);
        Rule("transit.far-rule", transit, new(-0.89f, -1.66f, -1.6f), new(4.58f, 0.021f), new(0, 15, -2), Cyan, 2.55, 6.0, 0.33f);

        int rails = Node("crossing.rails", null, _ => new Pose(Vector3.Zero));
        Rule("crossing.cyan-high", rails, new(1.25f, 1.42f, -8.77f), new(7.4f, 0.032f),
            new(-8, 71, 4), Cyan, 5.27, 6.95, 0.58f);
        Rule("crossing.cyan-low", rails, new(2.06f, -1.69f, -7.84f), new(5.7f, 0.045f),
            new(3, 84, -6), Cyan, 5.35, 6.80, 0.61f);
        Rule("crossing.lilac-near", rails, new(-1.81f, -1.03f, -7.12f), new(5.3f, 0.048f),
            new(3, 73, 11), Lilac, 5.54, 6.90, 0.72f);
        Rule("crossing.lilac-far", rails, new(-3.01f, 1.92f, -11.06f), new(6.6f, 0.027f),
            new(5, 78, 2), Lilac, 5.60, 7.02, 0.54f);

        int silence = Field("silence.field", c, 5.28, 7.07, Duration, Duration + 1,
            new Vector3(-0.92f, 0.68f, -1.0f), Vector3.Zero, new Vector3(13, -23, -16), Vector3.Zero);
        Text("silence.lead", silence, "ATÉ VIRAR", Typeface.Condensed, 0.21f, new(0, 1.23f, -0.3f),
            0.31f, Lilac, new(0, 5, 0), 5.78, 6.70, Duration, Duration + 1);
        Text("silence.hero", silence, "SILÊNCIO", Typeface.Display, -0.027f, new(0, -0.03f, 0),
            1.61f, Ivory, new(2, 0, 0), 5.64, 6.61, Duration, Duration + 1, emphasis: true);
        Rule("silence.left-rule", silence, new(-2.46f, 1.26f, -0.29f), new(1.65f, 0.012f), new(0, 4, 0), Lilac, 6.07, Duration + 1, 0.52f);
        Rule("silence.right-rule", silence, new(2.46f, 1.26f, -0.29f), new(1.65f, 0.012f), new(0, 4, 0), Lilac, 6.15, Duration + 1, 0.52f);
        Rule("silence.near-accent", silence, new(0, -1.35f, 0.3f), new(0.68f, 0.027f), new(0, 6, 0), Lilac, 6.47, Duration + 1, 0.66f);

        return new Sequence(new Rig(nodes.ToArray()), lens, zoom, temperature, planes.ToArray(), Duration);
    }

    private static ScalarCurve Progress(double start, double end, double outgoing, double incoming, double velocity = 0)
        => ScalarCurve.Between(start, end, 0, 1, new Ease(outgoing, velocity), new Ease(incoming));

    private static Vector3 Color(int rgb)
        => new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
}
