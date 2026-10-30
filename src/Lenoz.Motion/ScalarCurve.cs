namespace Lenoz.Motion;

public readonly record struct Ease(double Influence, double Velocity = 0);
public readonly record struct Key(double Seconds, double Value, Ease? In = null, Ease? Out = null);

/// <summary>Absolute-time scalar keys. Velocities are signed channel units per second.</summary>
public sealed class ScalarCurve
{
    private readonly Key[] _keys;
    private readonly Segment[] _segments;

    public ScalarCurve(params Key[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length == 0)
        {
            throw new ArgumentException("A curve needs at least one key.", nameof(keys));
        }

        _keys = (Key[])keys.Clone();
        _segments = new Segment[keys.Length - 1];
        for (int index = 0; index < _keys.Length; index++)
        {
            Key key = _keys[index];
            if (!double.IsFinite(key.Seconds) || !double.IsFinite(key.Value)
                || (index > 0 && key.Seconds <= _keys[index - 1].Seconds))
            {
                throw new ArgumentException("Keys need increasing times and finite values.", nameof(keys));
            }

            ValidateEase(key.In);
            ValidateEase(key.Out);
            if (index > 0)
            {
                _segments[index - 1] = new Segment(_keys[index - 1], key);
            }
        }
    }

    public double EndTime => _keys[^1].Seconds;

    public static ScalarCurve Between(double start, double end, double from, double to, Ease? outgoing = null, Ease? incoming = null)
        => new(new Key(start, from, Out: outgoing), new Key(end, to, In: incoming));

    public double At(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(seconds), true, nameof(seconds));
        int index = KeyAtOrBefore(seconds);
        if (index < 0)
        {
            return _keys[0].Value;
        }

        return index == _segments.Length ? _keys[^1].Value : _segments[index].ValueAt(seconds);
    }

    /// <summary>Velocity of the base curve; at a key, uses the outgoing side except at the final key.</summary>
    public double VelocityAt(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(seconds), true, nameof(seconds));
        int index = KeyAtOrBefore(seconds);
        if (_segments.Length == 0 || index < 0 || seconds > EndTime)
        {
            return 0;
        }

        return _segments[Math.Min(index, _segments.Length - 1)].VelocityAt(seconds);
    }

    internal double ArrivalVelocity(double probeSeconds)
    {
        if (_segments.Length == 0)
        {
            return 0;
        }

        double start = _keys[^2].Seconds;
        double sample = EndTime - Math.Min(probeSeconds, (EndTime - start) * 0.5);
        return _segments[^1].VelocityAt(sample);
    }

    private int KeyAtOrBefore(double seconds)
    {
        int first = 0;
        int last = _keys.Length;
        while (first < last)
        {
            int mid = first + (last - first) / 2;
            if (_keys[mid].Seconds <= seconds)
            {
                first = mid + 1;
            }
            else
            {
                last = mid;
            }
        }

        return first - 1;
    }

    private static void ValidateEase(Ease? handle)
    {
        if (handle is { } ease && (!(ease.Influence > 0 && ease.Influence <= 1) || !double.IsFinite(ease.Velocity)))
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "Influence must be in (0, 1] and velocity finite.");
        }
    }

    private readonly struct Segment
    {
        private readonly double _start;
        private readonly double _duration;
        private readonly double _from;
        private readonly double _to;
        private readonly double _slope;
        private readonly bool _linear;
        private readonly double _x1;
        private readonly double _x2;
        private readonly double _y1;
        private readonly double _y2;

        public Segment(Key a, Key b)
        {
            _start = a.Seconds;
            _duration = b.Seconds - a.Seconds;
            _from = a.Value;
            _to = b.Value;
            _slope = (_to - _from) / _duration;
            _linear = a.Out is null && b.In is null;

            var left = a.Out ?? new Ease(1.0 / 6, _slope);
            var right = b.In ?? new Ease(1.0 / 6, _slope);
            _x1 = left.Influence;
            _x2 = 1 - right.Influence;
            _y1 = _from + left.Velocity * (_duration * left.Influence);
            _y2 = _to - right.Velocity * (_duration * right.Influence);
        }

        public double ValueAt(double seconds)
        {
            double time = (seconds - _start) / _duration;
            if (_linear)
            {
                return _from + (_to - _from) * time;
            }

            return Cubic(_from, _y1, _y2, _to, ParameterAt(time));
        }

        public double VelocityAt(double seconds)
        {
            if (_linear)
            {
                return _slope;
            }

            double u = ParameterAt((seconds - _start) / _duration);
            double dx = CubicDerivative(0, _x1, _x2, 1, u);
            double dy = CubicDerivative(_from, _y1, _y2, _to, u);
            if (dx == 0 && dy == 0)
            {
                // The time curve can have a stationary inflection for two 100% handles.
                // Apply the derivative ratio on the first nonzero order.
                double second = 6 * ((1 - u) * (_y2 - 2 * _y1 + _from) + u * (_to - 2 * _y2 + _y1));
                if (second != 0)
                {
                    return double.NaN;
                }

                return (_to - 3 * _y2 + 3 * _y1 - _from) / (1 - 3 * _x2 + 3 * _x1) / _duration;
            }

            return dy / (dx * _duration);
        }

        private double ParameterAt(double time)
        {
            if (time <= 0 || time >= 1)
            {
                return Math.Clamp(time, 0, 1);
            }

            double low = 0;
            double high = 1;
            double u = time;
            for (int iteration = 0; iteration < 12; iteration++)
            {
                double error = Cubic(0, _x1, _x2, 1, u) - time;
                if (Math.Abs(error) <= 1e-14)
                {
                    return u;
                }

                if (error > 0)
                {
                    high = u;
                }
                else
                {
                    low = u;
                }

                double candidate = u - error / CubicDerivative(0, _x1, _x2, 1, u);
                u = candidate > low && candidate < high ? candidate : (low + high) / 2;
            }

            for (int iteration = 0; iteration < 42; iteration++)
            {
                u = (low + high) / 2;
                if (Cubic(0, _x1, _x2, 1, u) < time)
                {
                    low = u;
                }
                else
                {
                    high = u;
                }
            }

            return (low + high) / 2;
        }

        private static double Cubic(double a, double b, double c, double d, double u)
        {
            double v = 1 - u;
            return v * v * v * a + 3 * v * v * u * b + 3 * v * u * u * c + u * u * u * d;
        }

        private static double CubicDerivative(double a, double b, double c, double d, double u)
        {
            double v = 1 - u;
            return 3 * (v * v * (b - a) + 2 * v * u * (c - b) + u * u * (d - c));
        }
    }
}
