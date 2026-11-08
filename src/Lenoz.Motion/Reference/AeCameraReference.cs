using System.Numerics;
using System.Text.Json;

namespace Lenoz.Motion.Reference;

/// <summary>The six-node camera chain in audit composition 704. This reader supports its known channels only.</summary>
public sealed class AeCameraReference
{
    public const int FirstFrame = 212;
    public const int LastFrame = 429;
    public const double Fps = 60;
    public const int CameraIndex = 5;

    private static readonly Matrix4x4 Basis = Matrix4x4.CreateScale(1, -1, -1);

    private AeCameraReference(Rig rig, ScalarCurve zoom)
    {
        Rig = rig;
        Zoom = zoom;
    }

    public Rig Rig { get; }
    public ScalarCurve Zoom { get; }

    public static Vector3 FromAePoint(Vector3 point) => Vector3.Transform(point, Basis);

    public static Matrix4x4 FromAeMatrix(Matrix4x4 matrix) => Basis * matrix * Basis;

    public static double SecondsAtFrame(int frame) => (frame - FirstFrame) / Fps;

    public static AeCameraReference Load(string directory)
    {
        using var audit = Open(directory, "ae_animation_audit.json");
        using var samples = Open(directory, "ae_animation_samples.json");
        JsonElement source = Composition(audit);
        JsonElement measured = Composition(samples);
        if (source.GetProperty("frameRate").GetDouble() != Fps)
        {
            throw new InvalidDataException("The reference rig requires the captured time base of 60 fps.");
        }

        double offset = source.GetProperty("displayStartTime").GetDouble() - FirstFrame / Fps;
        var nodes = new RigNode[CameraIndex + 1];
        for (int i = 0; i < nodes.Length; i++)
        {
            int index = 30 + i;
            bool camera = i == CameraIndex;
            JsonElement layer = Layer(source, index);
            JsonElement sampled = Layer(measured, index);
            if (layer.GetProperty("id").GetInt32() != 721 - i || sampled.GetProperty("id").GetInt32() != 721 - i
                || (i > 0 && layer.GetProperty("parent").GetInt32() != index - 1))
            {
                throw new InvalidDataException("Unexpected reference layer identity or hierarchy.");
            }

            Func<double, Vector3> position = Position(layer, offset);
            Func<double, Vector3> rotation = Axes(layer, offset, "Rotation");
            Func<double, Vector3> orientation = camera
                ? SampledAngles(Property(sampled, "/Transform/Orientation"), measured.GetProperty("firstFrame").GetInt32())
                : VectorTrack(Property(layer, "/Transform/Orientation"), offset);
            Func<double, Vector3> scale = VectorTrack(Property(layer, "/Transform/Scale"), offset);
            Func<double, Vector3> anchor = VectorTrack(Property(layer, camera ? "/Transform/Point of Interest" : "/Transform/Anchor Point"), offset);

            Matrix4x4 Local(double t)
            {
                Vector3 pos = position(t);
                Matrix4x4 angular = Rotate(rotation(t)) * Rotate(orientation(t));
                Matrix4x4 local = camera
                    ? angular * AimAlongPositiveZ(pos, anchor(t)) * Matrix4x4.CreateTranslation(pos)
                    : Matrix4x4.CreateTranslation(-anchor(t)) * Matrix4x4.CreateScale(scale(t) / 100)
                        * angular * Matrix4x4.CreateTranslation(pos);
                return FromAeMatrix(local);
            }

            nodes[i] = new RigNode($"ae.camera.{index}", i == 0 ? null : i - 1, Local);
        }

        return new AeCameraReference(new Rig(nodes), Track(Property(Layer(source, 35), "/Camera Options/Zoom"), offset));
    }

    private static Matrix4x4 AimAlongPositiveZ(Vector3 position, Vector3 target)
    {
        Vector3 forward = Vector3.Normalize(target - position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 down = Vector3.Cross(forward, right);
        return new Matrix4x4(right.X, right.Y, right.Z, 0, down.X, down.Y, down.Z, 0,
            forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1);
    }

    private static Matrix4x4 Rotate(Vector3 degrees)
    {
        Vector3 angles = degrees * (MathF.PI / 180);
        return Matrix4x4.CreateRotationZ(angles.Z) * Matrix4x4.CreateRotationY(angles.Y) * Matrix4x4.CreateRotationX(angles.X);
    }

    private static Func<double, Vector3> Position(JsonElement layer, double offset)
    {
        JsonElement property = Property(layer, "/Transform/Position");
        if (property.GetProperty("dimensionsSeparated").GetBoolean())
        {
            return Axes(layer, offset, "Position");
        }

        foreach (JsonElement key in property.GetProperty("keys").EnumerateArray())
        {
            if (Vector(key.GetProperty("inSpatialTangent")) != Vector3.Zero || Vector(key.GetProperty("outSpatialTangent")) != Vector3.Zero)
            {
                throw new InvalidDataException("This reference requires straight local position paths.");
            }
        }

        return VectorTrack(property, offset, spatialSpeed: true);
    }

    private static Func<double, Vector3> Axes(JsonElement layer, double offset, string suffix)
    {
        var x = Track(Property(layer, $"/Transform/X {suffix}"), offset);
        var y = Track(Property(layer, $"/Transform/Y {suffix}"), offset);
        var z = Track(Property(layer, $"/Transform/Z {suffix}"), offset);
        return t => new Vector3((float)x.At(t), (float)y.At(t), (float)z.At(t));
    }

    private static Func<double, Vector3> VectorTrack(JsonElement property, double offset, bool spatialSpeed = false)
    {
        var x = Track(property, offset, 0, spatialSpeed);
        var y = Track(property, offset, 1, spatialSpeed);
        var z = Track(property, offset, 2, spatialSpeed);
        return t => new Vector3((float)x.At(t), (float)y.At(t), (float)z.At(t));
    }

    private static ScalarCurve Track(JsonElement property, double offset, int? axis = null, bool spatialSpeed = false)
    {
        JsonElement[] keys = property.TryGetProperty("keys", out JsonElement nativeKeys) ? nativeKeys.EnumerateArray().ToArray() : [];
        double Component(JsonElement value) => (axis is int component ? value[component] : value).GetDouble();
        if (keys.Length == 0)
        {
            return new ScalarCurve(new Key(0, Component(property.GetProperty("value"))));
        }

        var result = new Key[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            JsonElement key = keys[i];
            JsonElement value = key.GetProperty("value");
            Ease? Handle(string side, int left, int right)
            {
                string? interpolation = key.GetProperty(side + "Interpolation").GetString();
                if (interpolation == "6612")
                {
                    return null;
                }

                if (interpolation != "6613")
                {
                    throw new InvalidDataException("Unsupported interpolation in the reference.");
                }

                JsonElement ease = key.GetProperty(side + "Ease")[spatialSpeed ? 0 : axis.GetValueOrDefault()];
                double velocity = ease.GetProperty("speed").GetDouble();
                if (spatialSpeed && velocity != 0)
                {
                    JsonElement from = keys[left].GetProperty("value");
                    JsonElement to = keys[right].GetProperty("value");
                    double length = Vector3.Distance(Vector(from), Vector(to));
                    velocity = length == 0 ? 0 : velocity * (Component(to) - Component(from)) / length;
                }

                return new Ease(ease.GetProperty("influence").GetDouble() / 100, velocity);
            }

            result[i] = new Key(Math.Round((key.GetProperty("time").GetDouble() + offset) * Fps) / Fps, Component(value),
                i == 0 ? null : Handle("in", i - 1, i),
                i == keys.Length - 1 ? null : Handle("out", i, i + 1));
        }

        return new ScalarCurve(result);
    }

    private static Func<double, Vector3> SampledAngles(JsonElement property, int firstFrame)
    {
        Vector3[] values = property.GetProperty("values").EnumerateArray().Select(Vector).ToArray();
        for (int i = 1; i < values.Length; i++)
        {
            Vector3 delta = values[i] - values[i - 1];
            values[i] = values[i - 1] + new Vector3(
                (float)Math.IEEERemainder(delta.X, 360),
                (float)Math.IEEERemainder(delta.Y, 360),
                (float)Math.IEEERemainder(delta.Z, 360));
        }

        return seconds =>
        {
            double frame = Math.Clamp(seconds * Fps + FirstFrame - firstFrame, 0, values.Length - 1);
            int start = (int)frame;
            return Vector3.Lerp(values[start], values[Math.Min(start + 1, values.Length - 1)], (float)(frame - start));
        };
    }

    private static JsonDocument Open(string directory, string file)
    {
        using var stream = File.OpenRead(Path.Combine(directory, file));
        return JsonDocument.Parse(stream);
    }

    private static JsonElement Composition(JsonDocument document) => document.RootElement.GetProperty("compositions")
        .EnumerateArray().Single(comp => comp.GetProperty("id").GetInt32() == 704);

    private static JsonElement Layer(JsonElement comp, int index) => comp.GetProperty("layers")
        .EnumerateArray().Single(layer => layer.GetProperty("index").GetInt32() == index);

    private static JsonElement Property(JsonElement layer, string path) => layer.GetProperty("properties")
        .EnumerateArray().Single(property => property.GetProperty("path").GetString() == path);

    private static Vector3 Vector(JsonElement array) => new(array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle());
}
