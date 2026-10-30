namespace Lenoz.Motion;

public static class Wiggle
{
    // Each channel hashes two lattice points. The quintic interpolation has zero first and second derivatives at the knots.
    public static float At(double seconds, double frequencyHz, float amplitude, uint seed, uint channel)
    {
        double phase = seconds * frequencyHz;
        long index = (long)Math.Floor(phase);
        double t = phase - index;
        double weight = t * t * t * (t * (t * 6 - 15) + 10);
        double first = Lattice(index, seed, channel);
        double second = Lattice(index + 1, seed, channel);
        return (float)((first + (second - first) * weight) * amplitude);
    }

    private static double Lattice(long index, uint seed, uint channel)
    {
        unchecked
        {
            uint value = (uint)index ^ (uint)(index >> 32) * 0x9e3779b9u ^ seed * 0x85ebca6bu ^ channel * 0xc2b2ae35u;
            value = (value ^ (value >> 16)) * 0x7feb352du;
            value = (value ^ (value >> 15)) * 0x846ca68bu;
            value ^= value >> 16;
            return 2.0 * value / uint.MaxValue - 1;
        }
    }
}
