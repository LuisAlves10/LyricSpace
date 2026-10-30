using System.Numerics;

namespace Lenoz.Motion;

/// <summary>Local geometry evaluated by distance fraction independently from the temporal curve.</summary>
public sealed class SpatialPath
{
    private const int ArcSamples = 256;
    private readonly Vector3 _start;
    private readonly Vector3 _end;
    private readonly Vector3 _control1;
    private readonly Vector3 _control2;
    private readonly double[]? _distanceAtSample;

    public SpatialPath(Vector3 start, Vector3 end, Vector3 outTangent = default, Vector3 inTangent = default)
    {
        _start = start;
        _end = end;
        _control1 = start + outTangent;
        _control2 = end + inTangent;
        if (outTangent == Vector3.Zero && inTangent == Vector3.Zero)
        {
            Length = Vector3.Distance(start, end);
            return;
        }

        _distanceAtSample = new double[ArcSamples + 1];
        Vector3 previous = start;
        for (int i = 1; i <= ArcSamples; i++)
        {
            Vector3 point = PointAt((float)i / ArcSamples);
            _distanceAtSample[i] = _distanceAtSample[i - 1] + Vector3.Distance(previous, point);
            previous = point;
        }

        Length = _distanceAtSample[^1];
    }

    public double Length { get; }

    public Vector3 AtDistanceFraction(double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(fraction), true, nameof(fraction));
        fraction = Math.Clamp(fraction, 0, 1);
        if (_distanceAtSample is null || Length == 0)
        {
            return Vector3.Lerp(_start, _end, (float)fraction);
        }

        double distance = fraction * Length;
        int index = Array.BinarySearch(_distanceAtSample, distance);
        if (index >= 0)
        {
            return PointAt((float)index / ArcSamples);
        }

        int next = ~index;
        double along = (distance - _distanceAtSample[next - 1]) / (_distanceAtSample[next] - _distanceAtSample[next - 1]);
        return PointAt((float)((next - 1 + along) / ArcSamples));
    }

    private Vector3 PointAt(float u)
    {
        float v = 1 - u;
        return _start * (v * v * v) + _control1 * (3 * v * v * u)
            + _control2 * (3 * v * u * u) + _end * (u * u * u);
    }
}
