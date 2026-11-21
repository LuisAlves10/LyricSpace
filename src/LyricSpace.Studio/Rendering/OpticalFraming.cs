using System.Numerics;

namespace LyricSpace.Studio.Rendering;

internal readonly record struct InkCluster(Vector2[] Hull, Vector2 Centroid, float Area, float TrackingIndex);

internal struct ProjectedInk
{
    private Vector2 _min;
    private Vector2 _max;
    private Vector2 _moment;
    private float _area;
    public bool Valid { get; private set; }
    public Vector2 Min => _min;
    public Vector2 Max => _max;
    public Vector2 Center => _area > 0 ? _moment / _area : (_min + _max) * 0.5f;

    public void Add(ReadOnlySpan<InkCluster> clusters, Matrix4x4 transform, Vector2 pixels, float tracking, bool lead)
    {
        foreach (ref readonly InkCluster cluster in clusters)
        {
            float dx = tracking * cluster.TrackingIndex;
            Vector2 min = new(float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity);
            bool clipped = false;
            foreach (Vector2 point in cluster.Hull)
            {
                Vector4 clip = Vector4.Transform(new Vector4(point.X + dx, point.Y, 0, 1), transform);
                if (clip.W <= 0 || clip.Z < 0 || clip.Z > clip.W)
                {
                    clipped = true;
                    break;
                }

                Vector2 position = new Vector2(clip.X, clip.Y) / clip.W * pixels;
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
            }

            if (clipped)
            {
                continue;
            }

            _min = Valid ? Vector2.Min(_min, min) : min;
            _max = Valid ? Vector2.Max(_max, max) : max;
            Valid = true;

            Vector4 centre = Vector4.Transform(new Vector4(cluster.Centroid.X + dx, cluster.Centroid.Y, 0, 1), transform);
            float inverseW = 1 / centre.W;
            Vector2 normalized = new Vector2(centre.X, centre.Y) * inverseW;
            // Pixel-space Jacobian of the perspective divide at the ink centroid.
            Vector2 axisX = (new Vector2(transform.M11, transform.M12) - normalized * transform.M14) * pixels * inverseW;
            Vector2 axisY = (new Vector2(transform.M21, transform.M22) - normalized * transform.M24) * pixels * inverseW;
            float area = cluster.Area * MathF.Abs(axisX.X * axisY.Y - axisX.Y * axisY.X) * (lead ? 1.10f : 1f);
            _area += area;
            _moment += normalized * pixels * area;
        }
    }

    public float Fit(Vector2 center, Vector2 safe)
    {
        if (!Valid)
        {
            return 1;
        }

        Vector2 half = Vector2.Max(center - Min, Max - center);
        return Math.Min(1, Math.Min(safe.X / Math.Max(half.X, 1), safe.Y / Math.Max(half.Y, 1)));
    }
}

internal readonly record struct FramingTransform(float Scale, Vector2 Offset)
{
    public static FramingTransform At(in ProjectedInk ink, Vector2 center, Vector2 safe, float plannedScale,
        float mix, Vector2 pixels)
    {
        float fit = Math.Min(plannedScale, ink.Fit(center, safe));
        Vector2 translation = (-center * (fit * mix)) / pixels;
        return new FramingTransform(1 - (1 - fit) * mix, translation);
    }

    public Matrix4x4 ClipMatrix => Matrix4x4.CreateScale(Scale, Scale, 1)
        * Matrix4x4.CreateTranslation(Offset.X, Offset.Y, 0);
}
