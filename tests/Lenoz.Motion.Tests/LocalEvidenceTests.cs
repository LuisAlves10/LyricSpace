using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lenoz.Motion.Tests;

[TestClass]
[TestCategory("LocalEvidence")]
public sealed class LocalEvidenceTests
{
    public TestContext TestContext { get; set; } = null!;
    private const int CompositionId = 704;

    [TestMethod]
    public void CompareCameraChannelsWithOriginalFrameSamples()
    {
        using var audit = Read("ae_animation_audit.json");
        using var samples = Read("ae_animation_samples.json");
        JsonElement comp = Composition(audit);
        JsonElement measured = Composition(samples);
        var channels = new (int Layer, string Path, int? Component, int? Peak, double Tolerance)[]
        {
            (34, "/Transform/Position", 2, 270, 0.025),
            (33, "/Transform/Position", 2, 285, 0.025),
            (32, "/Transform/Position", 2, 318, 0.10),
            (31, "/Transform/Z Position", null, 378, 0.10),
            (30, "/Transform/Z Position", null, 401, 0.025),
            (35, "/Transform/Z Rotation", null, 381, 0.025),
            (35, "/Camera Options/Zoom", null, null, 0.025)
        };
        double[]? negative = null;
        foreach (var channel in channels)
        {
            var curve = ReadCurve(comp, channel.Layer, channel.Path, channel.Component);
            JsonElement frames = Property(measured, channel.Layer, channel.Path).GetProperty("values");
            int firstFrame = measured.GetProperty("firstFrame").GetInt32();
            double fps = comp.GetProperty("frameRate").GetDouble();
            double[] predicted = Enumerable.Range(firstFrame, frames.GetArrayLength()).Select(frame => curve.At(frame / fps)).ToArray();
            double maxError = predicted.Zip(frames.EnumerateArray(), (p, m) => Math.Abs(p - Number(m, channel.Component))).Max();
            TestContext.WriteLine(FormattableString.Invariant($"Layer {channel.Layer}, {channel.Path}: max error {maxError:G9}"));
            Assert.IsLessThanOrEqualTo(channel.Tolerance, maxError);
            if (channel.Peak is int peak)
            {
                int measuredPeak = Enumerable.Range(1, predicted.Length - 2)
                    .MaxBy(i => Math.Abs(predicted[i + 1] - predicted[i - 1])) + firstFrame;
                Assert.AreEqual(peak, measuredPeak);
            }

            if (channel.Layer == 34)
            {
                negative = predicted;
            }
            else if (channel.Layer == 33)
            {
                double[] sum = predicted.Zip(negative!, (a, b) => a + b).ToArray();
                int peakIndex = Enumerable.Range(0, sum.Length).MaxBy(i => sum[i]);
                Assert.AreEqual(278, firstFrame + peakIndex);
                Assert.AreEqual(398.444013362112, sum[peakIndex], 0.016);
            }
        }
    }

    [TestMethod]
    public void CompareDampedLobesWithPreExpressionAndEvaluatedValues()
    {
        using var audit = Read("ae_animation_audit.json");
        using var samples = Read("ae_animation_samples.json");
        using var metrics = Read("animation_metrics.json");
        JsonElement comp = Composition(audit);
        JsonElement measured = Composition(samples);
        double fps = comp.GetProperty("frameRate").GetDouble();
        int firstFrame = measured.GetProperty("firstFrame").GetInt32();

        foreach (JsonElement measuredLobes in metrics.RootElement.GetProperty("bounce_deltas").EnumerateArray())
        {
            int layer = measuredLobes.GetProperty("layer").GetInt32();
            string path = measuredLobes.GetProperty("property").GetString()!;
            double Parameter(string key) => Property(comp, layer, "/Effects/BOUNCr overSHOOT+/" + key).GetProperty("value").GetDouble();

            var curve = ReadCurve(comp, layer, path, null);
            var bounce = new ArrivalOscillation(curve, Parameter("Amplitude") / 1000, Parameter("Frequency"), Parameter("Decay"), fps);
            JsonElement channel = Property(measured, layer, path);
            var deltas = channel.GetProperty("values").EnumerateArray().Zip(channel.GetProperty("preExpressionValues").EnumerateArray(),
                (sample, original) => sample.GetDouble() - original.GetDouble()).ToArray();
            double maxError = 0;
            for (int index = 0; index < deltas.Length; index++)
            {
                double time = (firstFrame + index) / fps;
                maxError = Math.Max(maxError, Math.Abs(bounce.At(time) - curve.At(time) - deltas[index]));
            }

            TestContext.WriteLine(FormattableString.Invariant($"Layer {layer}, {path}: max delta error {maxError:G9}"));
            Assert.IsLessThanOrEqualTo(0.01, maxError);
        }
    }

    private static JsonDocument Read(string name)
    {
        string? dir = Environment.GetEnvironmentVariable("LYRICSPACE_AE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(dir))
        {
            Assert.Inconclusive("Set LYRICSPACE_AE_EVIDENCE to the folder containing the read-only AE JSONs.");
        }

        using var stream = File.OpenRead(Path.Combine(dir!, name));
        return JsonDocument.Parse(stream);
    }

    private static JsonElement Composition(JsonDocument document)
        => document.RootElement.GetProperty("compositions").EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == CompositionId);

    private static JsonElement Property(JsonElement composition, int layer, string path)
        => composition.GetProperty("layers").EnumerateArray().Single(l => l.GetProperty("index").GetInt32() == layer)
            .GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("path").GetString() == path);

    private static ScalarCurve ReadCurve(JsonElement comp, int layer, string path, int? component)
    {
        JsonElement[] keyframes = Property(comp, layer, path).GetProperty("keys").EnumerateArray().ToArray();
        double offset = comp.GetProperty("displayStartTime").GetDouble();
        double fps = comp.GetProperty("frameRate").GetDouble();
        var keys = new Key[keyframes.Length];
        for (int i = 0; i < keyframes.Length; i++)
        {
            JsonElement key = keyframes[i];
            JsonElement value = key.GetProperty("value");
            double seconds = Math.Round((key.GetProperty("time").GetDouble() + offset) * fps) / fps;
            keys[i] = new Key(seconds, Number(value, component),
                i > 0 ? Handle(key, "in", Projection(keyframes[i - 1].GetProperty("value"), value, component)) : null,
                i + 1 < keyframes.Length ? Handle(key, "out", Projection(value, keyframes[i + 1].GetProperty("value"), component)) : null);
        }

        return new ScalarCurve(keys);
    }

    private static Ease? Handle(JsonElement key, string side, double projection)
    {
        if (key.GetProperty(side + "Interpolation").GetString() == "6612")
        {
            return null;
        }

        JsonElement ease = key.GetProperty(side + "Ease")[0];
        return new Ease(ease.GetProperty("influence").GetDouble() / 100, projection * ease.GetProperty("speed").GetDouble());
    }

    private static double Number(JsonElement value, int? component)
        => component is int i ? value[i].GetDouble() : value.GetDouble();

    private static double Projection(JsonElement from, JsonElement to, int? component)
    {
        if (component is null)
        {
            return 1;
        }

        double length = Math.Sqrt(from.EnumerateArray().Zip(to.EnumerateArray(),
            (a, b) => Math.Pow(b.GetDouble() - a.GetDouble(), 2)).Sum());
        return length == 0 ? 0 : (Number(to, component) - Number(from, component)) / length;
    }
}
