using System.Numerics;

namespace Lenoz.Motion;

public readonly record struct Pose(Vector3 Position, Vector3 RotationDegrees, Vector3 Scale, Vector3 Pivot, Vector3 OrientationDegrees)
{
    public Pose(Vector3 position, Vector3 rotationDegrees = default)
        : this(position, rotationDegrees, Vector3.One, Vector3.Zero, Vector3.Zero)
    {
    }

    public Matrix4x4 LocalMatrix()
        => Matrix4x4.CreateTranslation(-Pivot)
            * Matrix4x4.CreateScale(Scale)
            * Rotation(RotationDegrees)
            * Rotation(OrientationDegrees)
            * Matrix4x4.CreateTranslation(Position);

    private static Matrix4x4 Rotation(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180);
        return Matrix4x4.CreateRotationX(radians.X)
            * Matrix4x4.CreateRotationY(radians.Y)
            * Matrix4x4.CreateRotationZ(radians.Z);
    }
}

public readonly record struct RigNode(string Id, int? ParentIndex, Func<double, Pose> SampleLocal);

/// <summary>Compiled in parent-first order. Each evaluation writes to caller-owned storage.</summary>
public sealed class Rig
{
    private readonly RigNode[] _nodes;

    public Rig(params RigNode[] nodes)
    {
        _nodes = (RigNode[])nodes.Clone();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < _nodes.Length; index++)
        {
            RigNode node = _nodes[index];
            if (string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id) || node.SampleLocal is null
                || (node.ParentIndex is int parent && (parent < 0 || parent >= index)))
            {
                throw new ArgumentException("Rig nodes need unique IDs, a pose sampler, and a preceding parent.", nameof(nodes));
            }
        }
    }

    public int Count => _nodes.Length;

    public void Evaluate(double seconds, Span<Matrix4x4> world)
    {
        if (world.Length < _nodes.Length)
        {
            throw new ArgumentException("One output matrix is required per node.", nameof(world));
        }

        for (int index = 0; index < _nodes.Length; index++)
        {
            RigNode node = _nodes[index];
            Matrix4x4 local = node.SampleLocal(seconds).LocalMatrix();
            world[index] = node.ParentIndex is int parent ? local * world[parent] : local;
        }
    }
}
