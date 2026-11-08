using System.Numerics;
using Lenoz.Motion;
using Lenoz.Motion.Reference;

namespace LyricSpace.Studio.Composition;

internal static class ReferenceSequence
{
    public static Sequence Create(string evidenceDirectory)
    {
        var reference = AeCameraReference.Load(evidenceDirectory);
        var nodes = new List<RigNode>();
        for (int i = 0; i < reference.Rig.Count; i++)
        {
            nodes.Add(reference.Rig.GetNode(i));
        }

        var planes = new List<ScenePlane>();
        var poses = new Matrix4x4[reference.Rig.Count];

        void Field(string id, string word, string support, int atFrame, float depth, Vector3 color)
        {
            double at = AeCameraReference.SecondsAtFrame(atFrame);
            reference.Rig.Evaluate(at, poses);
            Matrix4x4 camera = poses[AeCameraReference.CameraIndex];
            float unitsPerPixel = depth / (float)reference.Zoom.At(at);
            int field = nodes.Count;
            nodes.Add(new RigNode(id, null, _ => Matrix4x4.CreateTranslation(0, 0, -depth) * camera));

            int lead = nodes.Count;
            nodes.Add(new RigNode(id + ".lead", field, _ => new Pose(new Vector3(-105, 136, 5) * unitsPerPixel)));
            planes.Add(new ScenePlane(lead, new TextStyle(support, Typeface.Condensed, 0.20f), new Vector2(0, 33 * unitsPerPixel), color, _ => 1));

            int main = nodes.Count;
            nodes.Add(new RigNode(id + ".main", field, _ => new Pose(Vector3.Zero)));
            planes.Add(new ScenePlane(main, new TextStyle(word, Typeface.Display, -0.01f), new Vector2(0, 155 * unitsPerPixel),
                new Vector3(0.97f, 0.94f, 0.87f), _ => 1));
        }

        Field("reference.early", "DOBRA", "A LUZ", 252, 1500, new Vector3(1, 0.42f, 0.21f));
        Field("reference.middle", "TRÂNSITO", "TUDO EM", 339, 1250, new Vector3(0.40f, 0.87f, 0.98f));
        Field("reference.final", "SILÊNCIO", "ATÉ VIRAR", 422, 1350, new Vector3(0.67f, 0.65f, 1));

        double duration = AeCameraReference.SecondsAtFrame(AeCameraReference.LastFrame);
        var tone = new ScalarCurve(new Key(0, 0), new Key(duration * 0.5, 1), new Key(duration, 2));
        return new Sequence(new Rig(nodes.ToArray()), AeCameraReference.CameraIndex, reference.Zoom, tone, planes.ToArray(), duration, 1, 30_000);
    }
}
