using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Lenoz.Motion.Tests;

[TestClass]
public sealed class MotionTests
{
    [TestMethod]
    public void LinearKeysClampAndKeepTheirPhysicalSlope()
    {
        var curve = ScalarCurve.Between(1, 3, 40, -20);
        Assert.AreEqual(40, curve.At(0));
        Assert.AreEqual(10, curve.At(2));
        Assert.AreEqual(-20, curve.At(4));
        Assert.AreEqual(-30, curve.VelocityAt(2));
        Assert.AreEqual(0, curve.VelocityAt(4));
    }

    [TestMethod]
    public void AsymmetricTimingAndSignedEndpointVelocitiesSurviveInversion()
    {
        var curve = ScalarCurve.Between(0, 2, 1, 8, new Ease(0.78, 4), new Ease(0.64, -1.2));
        Assert.AreEqual(4, curve.VelocityAt(0), 1e-10);
        Assert.AreEqual(-1.2, curve.VelocityAt(2), 1e-10);
        Assert.IsGreaterThan(8, curve.At(1.9));
        const double t = 1.12;
        const double dt = 1e-5;
        Assert.AreEqual((curve.At(t + dt) - curve.At(t - dt)) / (2 * dt), curve.VelocityAt(t), 1e-5);
    }

    [TestMethod]
    public void CrossedTimeHandlesAndStationaryInflectionRemainEvaluable()
    {
        var sharp = ScalarCurve.Between(0, 1, 0, 1, new Ease(1), new Ease(1));
        Assert.AreEqual(0.5, sharp.At(0.5), 1e-10);
        Assert.IsTrue(double.IsPositiveInfinity(sharp.VelocityAt(0.5)));
        var linearThroughStationary = ScalarCurve.Between(0, 1, 0, 1, new Ease(1, 1), new Ease(1, 1));
        Assert.AreEqual(1, linearThroughStationary.VelocityAt(0.5), 1e-10);
        Assert.IsLessThan(0.5, sharp.At(0.499));
        Assert.IsGreaterThan(0.5, sharp.At(0.501));
    }

    [TestMethod]
    public void InvalidRigTopologyAndDuplicateKeysAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Rig(new RigNode("self", 0, _ => new Pose(Vector3.Zero))));
        Assert.ThrowsExactly<ArgumentException>(() => new Rig(
            new RigNode("name", null, _ => new Pose(Vector3.Zero)),
            new RigNode("name", 0, _ => new Pose(Vector3.Zero))));
        Assert.ThrowsExactly<ArgumentException>(() => new ScalarCurve(new Key(1, 0), new Key(1, 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScalarCurve.Between(0, 1, 0, 1, new Ease(1.1)));
    }

    [TestMethod]
    public void PivotThenParentRotationTransformsChildPositionInParentSpace()
    {
        var rig = new Rig(
            new RigNode("parent", null, _ => new Pose(new Vector3(12, 0, 0), new Vector3(0, 0, 90))),
            new RigNode("child", 0, _ => new Pose(new Vector3(2, 0, 0), Vector3.Zero, new Vector3(2), Vector3.UnitX, Vector3.Zero)));
        var world = new Matrix4x4[2];
        rig.Evaluate(0, world);

        Vector3 pivot = Vector3.Transform(Vector3.UnitX, world[1]);
        Assert.AreEqual(12, pivot.X, 1e-5);
        Assert.AreEqual(2, pivot.Y, 1e-5);
        Vector3 besidePivot = Vector3.Transform(Vector3.UnitX * 2, world[1]);
        Assert.AreEqual(4, besidePivot.Y, 1e-5);
    }

    [TestMethod]
    public void ParentChainsSampleAllActiveGesturesAtTheSameTime()
    {
        var turn = ScalarCurve.Between(0.75, 1.75, 0, 90);
        var travel = ScalarCurve.Between(0, 1, 0, 2);
        var rig = new Rig(
            new RigNode("turn", null, t => new Pose(Vector3.Zero, new(0, 0, (float)turn.At(t)))),
            new RigNode("travel", 0, t => new Pose(new((float)travel.At(t), 0, 0))));
        var world = new Matrix4x4[2];
        rig.Evaluate(0.9, world);
        Assert.AreEqual(1.8, world[1].Translation.Length(), 1e-5);
        Assert.IsGreaterThan(0.4, world[1].Translation.Y);
        rig.Evaluate(1.5, world);
        Assert.AreEqual(2, world[1].Translation.Length(), 1e-5);
        Assert.IsGreaterThan(1.8, world[1].Translation.Y);
    }

    [TestMethod]
    public void MultiTurnEulerPathIsPreserved()
    {
        var roll = ScalarCurve.Between(0, 1, 0, -409);
        Assert.AreEqual(-306.75, roll.At(0.75), 1e-8);
        Assert.AreEqual(-409, roll.At(1));
        var direct = new Pose(Vector3.Zero, new(0, 0, (float)roll.At(0.5)));
        Assert.IsLessThan(-0.9, Vector3.Transform(Vector3.UnitX, direct.LocalMatrix()).X);
    }

    [TestMethod]
    public void StraightPathUsesDistanceAndCurvedPathRemapsArcLength()
    {
        var line = new SpatialPath(Vector3.Zero, Vector3.UnitX * 8);
        Assert.AreEqual(2, line.AtDistanceFraction(0.25).X, 1e-5);

        var arch = new SpatialPath(Vector3.Zero, Vector3.UnitX * 4, Vector3.UnitY * 8, Vector3.UnitY * 8);
        Assert.AreEqual(2, arch.AtDistanceFraction(0.5).X, 1e-4);
        Assert.AreEqual(6, arch.AtDistanceFraction(0.5).Y, 1e-4);
        Assert.AreEqual(3.3393, arch.AtDistanceFraction(0.25).Y, 0.002);
        Assert.AreEqual(Vector3.UnitX * 4, arch.AtDistanceFraction(1));
    }

    [TestMethod]
    public void PerspectiveProjectsNearElementsLargerAndZoomChangesFov()
    {
        var camera = new Pose(new Vector3(0, 0, 10)).LocalMatrix();
        float fov = PerspectiveCamera.VerticalFovFromZoom(1080, 1500);
        var projection = PerspectiveCamera.ViewProjection(camera, fov, 16f / 9, 0.1f, 100);
        Vector4 near = Vector4.Transform(new Vector4(1, 1, 0, 1), projection);
        Vector4 far = Vector4.Transform(new Vector4(1, 1, -10, 1), projection);
        Assert.AreEqual(2, (near.X / near.W) / (far.X / far.W), 1e-6);
        Assert.IsGreaterThan(fov, PerspectiveCamera.VerticalFovFromZoom(1080, 1100));
        Assert.AreEqual(10, near.W, 1e-6);
    }

    [TestMethod]
    public void OscillationUsesArrivalVelocitySignAndDecays()
    {
        var rising = new ArrivalOscillation(ScalarCurve.Between(0, 0.5, 0, 1), 0.05, 2, 4, 60);
        var falling = new ArrivalOscillation(ScalarCurve.Between(0, 0.5, 1, 0), 0.05, 2, 4, 60);
        Assert.AreEqual(1, rising.At(0.5));
        Assert.AreEqual(0.5, rising.At(0.25));
        Assert.IsGreaterThan(1, rising.At(0.625));
        Assert.IsLessThan(0, falling.At(0.625));
        Assert.IsLessThan(rising.At(0.625), rising.At(1.125));
        Assert.AreEqual(1, rising.At(10), 1e-12);
    }

    [TestMethod]
    public void ConstantTrackHasNoArrivalOscillation()
    {
        var value = new ArrivalOscillation(new ScalarCurve(new Key(0, 27)), 0.05, 2, 4, 60);
        Assert.AreEqual(27, value.At(0.125));
    }

    [TestMethod]
    public void WiggleEvaluatesDeterministicallyDuringRandomAccess()
    {
        float a = Wiggle.At(1.72, 2, 0.4f, 12345, 3);
        _ = Wiggle.At(21.5, 2, 0.4f, 12345, 3);
        Assert.AreEqual(a, Wiggle.At(1.72, 2, 0.4f, 12345, 3));
        Assert.AreNotEqual(a, Wiggle.At(1.72, 2, 0.4f, 12346, 3));
        Assert.IsTrue(Math.Abs(a) <= 0.4f);
        Assert.AreEqual(Wiggle.At(1.5 - 0.00001, 2, 0.4f, 12345, 3), Wiggle.At(1.5 + 0.00001, 2, 0.4f, 12345, 3), 1e-6f);
    }
}
