using System.Numerics;
using Lenoz.Motion;
using LyricSpace.Studio.Lyrics;

namespace LyricSpace.Studio.Composition;

internal enum MotionFamily { Traverse, Depth, Pivot }
internal enum WordState { Line, Future, Active, Past }

internal sealed record PhraseStyle(int Index, MotionFamily Family, double Start, double ReadStart, double ReadEnd, double End,
    ScalarCurve FrameIn, ScalarCurve FrameOut, float Stagger, float Width, float Tracking, float GlowRadius)
{
    public float FramingAt(double seconds) => (float)(FrameIn.At(seconds) * (1 - FrameOut.At(seconds)));
    public float TrackingAt(double seconds) => Tracking * (float)Math.Clamp((seconds - Start) / (ReadEnd - Start), 0, 1);
}

internal sealed record WordStyle(PhraseStyle Phrase, VocalWord Vocal, bool Lead, bool RevealOnVocal, WordPalette Palette,
    ScalarCurve AccentIn, ScalarCurve AccentOut, float AccentTracking, float Glow, float AmbientGlow)
{
    public WordState StateAt(double t) => Vocal.Timing == TimingKind.Line ? WordState.Line
        : t < Vocal.Start ? WordState.Future : t < Vocal.End ? WordState.Active : WordState.Past;

    public float AccentAt(double t) => Vocal.Timing == TimingKind.Line ? (Lead ? 0.37f : 0)
        : (float)(AccentIn.At(t) * (1 - AccentOut.At(t)));

    public float TrackingAt(double t) => Phrase.TrackingAt(t) + (Vocal.Timing == TimingKind.Line ? 0 : AccentTracking * AccentAt(t));
    public float MaximumTracking => Phrase.Tracking + AccentTracking;

    public Vector3 ColorAt(double t)
    {
        if (Vocal.Timing == TimingKind.Line)
        {
            return Vector3.Lerp(Palette.Base, Palette.Accent, AccentAt(t));
        }

        float highlight = AccentAt(t);
        Vector3 rest = StateAt(t) == WordState.Past ? Palette.Secondary : Palette.Base;
        return Vector3.Lerp(rest, Palette.Accent, highlight);
    }

    public float GlowAt(double t) => AmbientGlow + Glow * AccentAt(t);
}
