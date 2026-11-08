using System.Numerics;
using Lenoz.Motion;

namespace LyricSpace.Studio.Composition;

internal enum Typeface
{
    Display,
    Condensed
}

internal readonly record struct TextStyle(string Text, Typeface Face, float Tracking = 0);
internal readonly record struct ScenePlane(int Node, TextStyle? Text, Vector2 Size, Vector3 Color, Func<double, float> Opacity);

internal sealed record Sequence(Rig Rig, int CameraNode, ScalarCurve Zoom, ScalarCurve Temperature, ScenePlane[] Planes, double Duration, float NearPlane = 0.08f, float FarPlane = 85)
{
    public const double ReferenceFps = 60;
    public const float Aspect = 16f / 9;
}
