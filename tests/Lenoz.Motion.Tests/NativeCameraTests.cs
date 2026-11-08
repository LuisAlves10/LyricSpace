using System.Numerics;
using System.Text.Json;
using Lenoz.Motion.Reference;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lenoz.Motion.Tests;

[TestClass]
[TestCategory("LocalEvidence")]
public sealed class NativeCameraTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SixNodeRigMatchesNativeWorldMatricesAndProjection()
    {
        string? evidence = Environment.GetEnvironmentVariable("LYRICSPACE_AE_EVIDENCE");
        string? worldPath = Environment.GetEnvironmentVariable("LYRICSPACE_AE_NATIVE");
        if (string.IsNullOrWhiteSpace(evidence) || string.IsNullOrWhiteSpace(worldPath))
        {
            Assert.Inconclusive("Provide the read-only evidence folder and the native camera probe JSON.");
        }

        var rig = AeCameraReference.Load(evidence!);
        using var file = JsonDocument.Parse(File.ReadAllText(worldPath!));
        JsonElement data = file.RootElement;
        int first = data.GetProperty("firstFrame").GetInt32();
        int last = data.GetProperty("lastFrame").GetInt32();
        Assert.AreEqual(AeCameraReference.FirstFrame, first);
        Assert.AreEqual(AeCameraReference.LastFrame, last);
        Matrix4x4[] matrices = new Matrix4x4[rig.Rig.Count];
        float maxBasis = 0, maxOrigin = 0, maxProjection = 0;
        var actualCamera = new Vector3[last - first + 1];
        var expectedCamera = new Vector3[actualCamera.Length];
        int projected = 0;
        int width = data.GetProperty("width").GetInt32();
        int height = data.GetProperty("height").GetInt32();
        for (int frame = first; frame <= last; frame++)
        {
            int index = frame - first;
            double t = AeCameraReference.SecondsAtFrame(frame);
            rig.Rig.Evaluate(t, matrices);

            for (int layer = 0; layer < matrices.Length; layer++)
            {
                JsonElement source = data.GetProperty("layers")[layer];
                Assert.AreEqual(721 - layer, source.GetProperty("id").GetInt32());
                JsonElement native = source.GetProperty("world");
                Vector3 x = Vector(native[0][index]);
                Vector3 y = Vector(native[1][index]);
                Vector3 z = Vector(native[2][index]);
                Vector3 origin = Vector(native[3][index]);
                Matrix4x4 expected = AeCameraReference.FromAeMatrix(new Matrix4x4(
                    x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, origin.X, origin.Y, origin.Z, 1));
                Matrix4x4 actual = matrices[layer];
                foreach (Vector3 axis in Axes)
                {
                    maxBasis = Math.Max(maxBasis,
                        Vector3.Distance(Vector3.TransformNormal(axis, expected), Vector3.TransformNormal(axis, actual)));
                }

                maxOrigin = Math.Max(maxOrigin, Vector3.Distance(expected.Translation, actual.Translation));
                if (layer == AeCameraReference.CameraIndex)
                {
                    actualCamera[index] = actual.Translation;
                    expectedCamera[index] = expected.Translation;
                }
            }

            Matrix4x4 camera = matrices[AeCameraReference.CameraIndex];
            Matrix4x4 viewProjection = PerspectiveCamera.ViewProjection(camera,
                PerspectiveCamera.VerticalFovFromZoom(height, (float)rig.Zoom.At(t)), (float)width / height, 1, 30_000);
            foreach (JsonElement point in data.GetProperty("points").EnumerateArray())
            {
                Vector3 world = AeCameraReference.FromAePoint(Vector(point.GetProperty("world")));
                Vector4 clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
                Vector3 expected = Vector(point.GetProperty("values")[index]);
                if (clip.W <= 50 || expected.X < 0 || expected.X > width || expected.Y < 0 || expected.Y > height)
                {
                    continue;
                }

                var screen = new Vector2((0.5f + clip.X / clip.W * 0.5f) * width, (0.5f - clip.Y / clip.W * 0.5f) * height);
                maxProjection = Math.Max(maxProjection, Vector2.Distance(screen, new Vector2(expected.X, expected.Y)));
                projected++;
            }
        }

        const float fps = (float)AeCameraReference.Fps;
        var speed = new float[actualCamera.Length];
        var nativeSpeed = new float[actualCamera.Length];
        var acceleration = new float[actualCamera.Length];
        var nativeAcceleration = new float[actualCamera.Length];
        for (int i = 1; i < actualCamera.Length - 1; i++)
        {
            speed[i] = Vector3.Distance(actualCamera[i + 1], actualCamera[i - 1]) * (fps / 2);
            nativeSpeed[i] = Vector3.Distance(expectedCamera[i + 1], expectedCamera[i - 1]) * (fps / 2);
            acceleration[i] = (actualCamera[i + 1] - 2 * actualCamera[i] + actualCamera[i - 1]).Length() * fps * fps;
            nativeAcceleration[i] = (expectedCamera[i + 1] - 2 * expectedCamera[i] + expectedCamera[i - 1]).Length() * fps * fps;
        }

        int peakSpeed = Array.IndexOf(speed, speed.Max()) + first;
        int peakAcceleration = Array.IndexOf(acceleration, acceleration.Max()) + first;
        float errorSpeed = speed.Zip(nativeSpeed, (a, b) => Math.Abs(a - b)).Max();
        float errorAcceleration = acceleration.Zip(nativeAcceleration, (a, b) => Math.Abs(a - b)).Max();
        TestContext.WriteLine(FormattableString.Invariant(
            $"Frames {first}-{last}, all six world matrices: max basis {maxBasis:G6}, max origin {maxOrigin:G6}"));
        TestContext.WriteLine(FormattableString.Invariant($"Camera speed peak F{peakSpeed}, {speed.Max():F2} units/s, max error {errorSpeed:F3}"));
        TestContext.WriteLine(FormattableString.Invariant($"Camera acceleration peak F{peakAcceleration}, {acceleration.Max():F2} units/s², max error {errorAcceleration:F3}"));
        TestContext.WriteLine(FormattableString.Invariant($"Native toComp points inside the frame: {projected}, max screen error {maxProjection:G6} px"));
        Assert.AreEqual(Array.IndexOf(nativeSpeed, nativeSpeed.Max()) + first, peakSpeed);
        Assert.AreEqual(Array.IndexOf(nativeAcceleration, nativeAcceleration.Max()) + first, peakAcceleration);
        Assert.IsLessThan(0.0001f, maxBasis);
        Assert.IsLessThan(0.10f, maxOrigin);
        Assert.IsLessThan(0.30f, maxProjection);
        Assert.IsGreaterThan(100, projected);
    }

    private static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

    private static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
}
