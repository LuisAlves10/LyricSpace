namespace Lenoz.Motion;

/// <summary>A damped sinusoid whose signed amplitude comes from the incoming channel velocity.</summary>
public sealed class ArrivalOscillation
{
    private readonly ScalarCurve _curve;
    private readonly double _amplitude;
    private readonly double _angularFrequency;
    private readonly double _decay;

    public ArrivalOscillation(ScalarCurve curve, double gainSeconds, double frequencyHz, double decayPerSecond, double referenceFps)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (!double.IsFinite(gainSeconds) || gainSeconds < 0
            || !double.IsFinite(frequencyHz) || frequencyHz <= 0
            || !double.IsFinite(decayPerSecond) || decayPerSecond <= 0
            || !double.IsFinite(referenceFps) || referenceFps <= 0)
        {
            throw new ArgumentException("Oscillation needs finite parameters, positive frequency, decay and reference FPS, and nonnegative gain.");
        }

        _curve = curve;
        _amplitude = gainSeconds == 0 ? 0 : curve.ArrivalVelocity(0.1 / referenceFps) * gainSeconds;
        _angularFrequency = frequencyHz * Math.Tau;
        _decay = decayPerSecond;
    }

    public double At(double seconds)
    {
        double value = _curve.At(seconds);
        double age = seconds - _curve.EndTime;
        return age <= 0 ? value : value + _amplitude * Math.Sin(_angularFrequency * age) * Math.Exp(-_decay * age);
    }
}
