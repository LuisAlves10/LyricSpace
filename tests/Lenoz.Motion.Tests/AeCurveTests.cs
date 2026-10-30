using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lenoz.Motion.Tests;

[TestClass]
public sealed class AeCurveTests
{
    [TestMethod]
    public void OpposedDepthOffsetsPeakAtReportFrameThenRecover()
    {
        var faster = ScalarCurve.Between(F(246), F(286), -519, 0, new Ease(0.70166776492651), new Ease(0.496177190685825));
        var slower = ScalarCurve.Between(F(261), F(324), 519, 0, new Ease(0.535206872770507), new Ease(0.797037641658465));
        double Sum(int frame) => slower.At(F(frame)) + faster.At(F(frame));

        var peak = Enumerable.Range(246, 79).MaxBy(Sum);
        Assert.AreEqual(278, peak);
        Assert.AreEqual(0, Sum(246), 1e-8);
        Assert.AreEqual(398.444, Sum(peak), 0.016);
        Assert.AreEqual(77.03, Sum(261), 0.016);
        Assert.AreEqual(267.31, Sum(286), 0.016);
        Assert.AreEqual(4.98, Sum(316), 0.016);
        Assert.AreEqual(0, Sum(324), 1e-8);
    }

    [TestMethod]
    public void MixedLinearBezierZoomReproducesPeakBeforeNegativeArrival()
    {
        var zoom = ScalarCurve.Between(F(276), F(317), 1096, 1500, incoming: new Ease(0.448813287766649, -494.969101481677));
        int peak = Enumerable.Range(276, 42).MaxBy(frame => zoom.At(F(frame)));
        Assert.AreEqual(310, peak);
        Assert.AreEqual(1529.0670, zoom.At(F(peak)), 0.001);
        Assert.AreEqual(-494.9691, zoom.VelocityAt(F(317)), 0.001);
    }

    [TestMethod]
    [DataRow(332, 337, 1.82, 4.0, 0.6961633746, -0.2561041932)]
    [DataRow(212, 216, 1.00, 3.0, 0.5284199971, -0.2496079324)]
    public void VelocityDependentScaleMatchesBothMeasuredLobes(int start, int land, double scale, double decay, double positive, double negative)
    {
        var curve = ScalarCurve.Between(F(start), F(land), 0, scale);
        var overshoot = new ArrivalOscillation(curve, 0.05, 2, decay, 60);
        Assert.AreEqual(positive, overshoot.At(F(land + 6)) - scale, 1e-8);
        Assert.AreEqual(negative, overshoot.At(F(land + 21)) - scale, 1e-8);
    }

    private static double F(int frame) => frame / 60.0;
}
